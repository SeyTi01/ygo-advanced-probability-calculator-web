const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const {chromium} = require('playwright');
async function main() {
    const root = path.resolve(process.argv[2]);
    const output = path.resolve(process.argv[3]);
    const selected = process.argv[4];
    const corpus = JSON.parse(fs.readFileSync(process.env.FIXTURES || path.join(__dirname, 'inputs/corpus.json'))).filter(c => !selected || new RegExp(selected).test(c.Name));
    const modes = (process.env.MODES || 'fast,exact,automatic').split(',');
    const samples = Number(process.env.SAMPLES || 10);
    const warmups = Number(process.env.WARMUPS || 2);
    const server = http.createServer((req, res) => {
        try {
            const route = new URL(req.url, 'http://localhost').pathname;
            if (route === '/') return res.setHeader('Content-Type', 'text/html'), res.end('<!doctype html><title>Frozen deadline research</title><button id="run">Run frozen request</button>');
            res.setHeader('Content-Type', route.endsWith('.wasm') ? 'application/wasm' : route.endsWith('.js') ? 'text/javascript' : 'application/json');
            if (route === '/owner.js') return res.end(fs.readFileSync(path.join(__dirname, 'owner.js')));
            if (route === '/probe.js') return res.end(`import {dotnet} from './_framework/dotnet.js';
                const runtime = await dotnet.create();
                const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
                const measure = exports.YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.FastCertificateBenchmarkProbe.Measure;
                onmessage = ({data}) => { try {
                    const measurement = JSON.parse(measure(JSON.stringify({Input: data.input, MeasureCertificate: true})));
                    measurement.WasmHeapBytes = globalThis.getDotnetRuntime(0).Module.HEAPU8.buffer.byteLength;
                    postMessage({id: data.id, measurement});
                } catch(e) { postMessage({id: data.id, error: String(e)}); } };
                postMessage({ready: true});`);
            const file = path.resolve(root, '.' + decodeURIComponent(route));
            assert(file.startsWith(root + path.sep));
            res.end(fs.readFileSync(file));
        } catch { res.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    let browser;
    const evidence = process.env.RESUME ? JSON.parse(fs.readFileSync(process.env.RESUME)) : {baseline:'63a4780fb3fa937907c1c8be4662550197179f27', startUtc:new Date().toISOString(), samples, warmups, seed:226225, runs:[], workers:[]};
    const completed = new Set(evidence.runs.map(r => `${r.name}:${r.mode}:${r.iteration}`));
    evidence.segments ??= [];
    evidence.segments.push({startUtc:new Date().toISOString(), modes, policy:'v2 initial Fast has no deadline', resume:!!process.env.RESUME});
    function save() {
        // Windows readers/virus scanning can briefly lock the destination. Keep
        // the previous complete checkpoint until the replacement is ready.
        const temporary = output + '.pending';
        fs.writeFileSync(temporary, JSON.stringify(evidence, null, 2));
        for (let retry = 0; ; retry++) {
            try { fs.renameSync(temporary, output); return; }
            catch (error) {
                if (retry === 20) throw error;
                Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 20);
            }
        }
    }
    try {
        browser = await chromium.launch({headless:true, executablePath: process.env.YGO_BROWSER_EXECUTABLE});
        evidence.browser = browser.version();
        const page = await browser.newPage();
        const origin = performance.now();
        page.on('worker', worker => {
            const state = {created:performance.now()-origin}; evidence.workers.push(state);
            worker.on('close', () => state.closed = performance.now()-origin);
        });
        await page.goto(`http://127.0.0.1:${server.address().port}`);
        await page.evaluate(async () => { const {createResearchOwner} = await import('/owner.js'); window.owner=createResearchOwner('/probe.js'); window.beats=0; setInterval(()=>beats++,20); });
        let seed = evidence.seed;
        const random = () => ((seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0) / 4294967296);
        const failed = new Set(evidence.runs.filter(r => r.outcome === 'failure').map(r => r.name+r.mode));
        for (let iteration = -warmups; iteration < samples; iteration++) {
            const order = corpus.flatMap(c => modes.map(mode => ({c,mode})));
            for (let i=order.length-1; i>0; i--) {const j=Math.floor(random()*(i+1)); [order[i],order[j]]=[order[j],order[i]];}
            for (const {c,mode} of order) {
                if (completed.has(`${c.Name}:${mode}:${iteration}`)) continue;
                if (failed.has(c.Name+mode)) continue;
                const run = await page.evaluate(async ({input,mode}) => {
                    const beatsBefore=beats;
                    const result=await owner.run(input,mode);
                    return {...result, beats:beats-beatsBefore};
                }, {input:c.Input,mode});
                evidence.runs.push({name:c.Name, split:c.Split, mode, iteration, ...run});
                if (run.outcome === 'failure' || run.outcome === 'timeout-no-result') failed.add(c.Name+mode);
                console.log(JSON.stringify({name:c.Name,mode,iteration,outcome:run.outcome,ms:Math.round(run.totalMs),engine:run.stages.map(x=>Math.round(x.Milliseconds)),work:run.stages.map(x=>x.Work)}));
                save();
            }
        }
        evidence.events=await page.evaluate(()=>owner.events);
        await page.evaluate(()=>owner.dispose());
        await page.waitForTimeout(2600);
        save();
    } finally { if(browser) await browser.close(); server.close(); }
}
main().catch(e=>{console.error(e);process.exitCode=1;});
