// Optimized local WASM only. Five isolated module runtimes, no UI/remote deploy.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');
async function main() {
  const root = path.resolve(process.argv[2]);
  const fixtures = JSON.parse(await fs.readFile(process.argv[3], 'utf8'));
  const sides = ['fast','low','high','exact','primary'];
  const cold=process.argv[5]==='cold';
  const server = http.createServer(async (request,response) => {
    try {
      const url = new URL(request.url,'http://localhost');
      if(url.pathname === '/') { response.setHeader('Content-Type','text/html'); return response.end('<!doctype html><title>Local research</title>'); }
      const match = /^\/(\d+)\/(.*)$/.exec(decodeURIComponent(url.pathname)); assert(match);
      if(match[2] === 'probe.js') {
        response.setHeader('Content-Type','text/javascript');
        return response.end(`import {dotnet} from './_framework/dotnet.js'; const runtime=await dotnet.create();
          const exports=await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
          const measure=exports.YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.FastCertificateBenchmarkProbe.Measure;
          self.onmessage=({data})=>{try { const result=JSON.parse(measure(data));result.WasmHeapBytes=globalThis.getDotnetRuntime(0).Module.HEAPU8.buffer.byteLength; self.postMessage(result); }catch(error){self.postMessage({error:String(error)});} }; self.postMessage('ready');`);
      }
      const file=path.resolve(root,match[2]); assert(file.startsWith(root+path.sep));
      response.setHeader('Content-Type',file.endsWith('.wasm')?'application/wasm':file.endsWith('.js')?'text/javascript':'application/json');
      response.end(await fs.readFile(file));
    } catch { response.writeHead(404).end(); }
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  let browser;
  try {
    browser=await chromium.launch({headless:true,executablePath:process.env.YGO_BROWSER_EXECUTABLE});
    const page=await browser.newPage();
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    const createWorkers=()=>page.evaluate(async () => {
      for(const worker of window.probes??[])worker.terminate();
      window.probes=[]; const times=[];
      for(let side=0;side<5;side++) {
        const started=performance.now(); const worker=new Worker(`/${side}/probe.js`,{type:'module'}); probes[side]=worker;
        await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error('Bootstrap timeout')),60000);worker.onerror=event=>{clearTimeout(timer);reject(Error(event.message));};worker.onmessage=({data})=>{clearTimeout(timer);data==='ready'?resolve():reject(Error(JSON.stringify(data)));};});
        times.push(performance.now()-started);
      }
      return times;
    });
    const bootstrap=await createWorkers();
    let seed=170224;
    const random=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed;};
    const output=[];
    for(const fixture of fixtures) {
      const caseBootstrap=cold&&output.length?await createWorkers():bootstrap;
      const first={},samples=Object.fromEntries(sides.map(side=>[side,[]]));
      for(let trial=0;trial<(cold?1:12);trial++) {
        for(const {side,index} of sides.map((side,index)=>({side,index,order:random()})).sort((a,b)=>a.order-b.order)) {
          const input=structuredClone(fixture.Input);
          if(side==='exact'&&input.Options.Mode!==1) input.Options.Mode=2;
          const request={Input:input,MeasureCertificate:side!=='primary',RefinementBudget:side==='low'?100000:side==='high'?1000000:0};
          const measured=await page.evaluate(({index,request})=>new Promise((resolve,reject)=>{
            const worker=probes[index]; const timer=setTimeout(()=>reject(Error('Probe timeout')),600000);
            worker.onmessage=({data})=>{clearTimeout(timer);data.error?reject(Error(data.error)):resolve(data);};
            worker.postMessage(JSON.stringify(request));
          }),{index,request});
          if(trial===0) first[side]=measured;
          if(trial>=2||cold) samples[side].push(measured);
        }
      }
      output.push({Name:fixture.Name,First:first,Samples:samples,...(cold?{BootstrapMilliseconds:caseBootstrap}:{})});
      await fs.writeFile(process.argv[4],JSON.stringify({Browser:browser.version(),BootstrapMilliseconds:bootstrap,output}));
      console.log(fixture.Name+': '+sides.map(side=>side+'='+samples[side].map(x=>x.Milliseconds).sort((a,b)=>a-b)[cold?0:4].toFixed(3)).join(' / '));
    }
  } finally { await browser?.close();await new Promise(resolve=>server.close(resolve)); }
}
main().catch(error=>{console.error(error);process.exitCode=1;});
