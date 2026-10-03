const assert = require('node:assert/strict');
const { chromium, firefox } = require('playwright');
const base = process.argv[2] || 'http://127.0.0.1:5157';
const engine = process.argv[3] || 'chromium';
const categories = Array.from({length:18},(_,i)=>({Name:`C${i}`,Source:'User'}));
const heavy = {SchemaVersion:2,Categories:categories, Cards:categories.map((c,i)=>({Id:`c${i}`,Name:null,Copies:3,Categories:[c]})).concat([{Id:'blank',Name:null,Copies:22,Categories:[]}]),Combos:categories.map((c,i)=>({Name:`Exactly one C${i}`,Categories:[{BaseCategory:c,MinCount:1,MaxCount:1}]})),ComboGroups:[],HandSize:5};
const simple={SchemaVersion:2,Categories:[categories[0]],Cards:[{Id:'a',Name:null,Copies:1,Categories:[categories[0]]},{Id:'b',Name:null,Copies:1,Categories:[]}],Combos:[{Name:'One card',GroupId:'g',Categories:[{BaseCategory:categories[0],MinCount:1,MaxCount:1}]}],ComboGroups:[{Id:'g',Name:'Group'}],HandSize:1};
const wire={Cards:heavy.Cards.map(c=>({...c,ExternalCardId:null,ManualMetadataCategoryKeys:[],Categories:c.Categories.map(c=>({...c,Source:0,MetadataKey:null}))})),Combos:heavy.Combos.map(c=>({...c,GroupId:null,Cards:[],Categories:c.Categories.map(c=>({...c,MaximumMode:0,BaseCategory:{...c.BaseCategory,Source:0,MetadataKey:null}}))})),Groups:[],HandSize:5};
(async()=>{
 const browser=await ({chromium,firefox}[engine]).launch({headless:true,timeout:20000});
 try{
 const page=await browser.newPage({viewport:{width:1440,height:1000}});
 const cdp=engine==='chromium'?await browser.newBrowserCDPSession():null;
 async function rendererCpu(){
  if(!cdp)return null;
  const info=await cdp.send('SystemInfo.getProcessInfo');
  return info.processInfo.filter(p=>p.type==='renderer').reduce((sum,p)=>sum+p.cpuTime,0);
 }
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 let liveWorkers=0, closedWorkers=0;
 page.on('worker',worker=>{if(!worker.url().includes('calculation-worker.js'))return;liveWorkers++; worker.on('close',()=>{liveWorkers--;closedWorkers++})});
 await page.addInitScript(()=>{
  window.probe={beats:[],sent:[],terminated:[],cancelClicks:[]};document.addEventListener('click',e=>{if(e.target.closest('[aria-label="Cancel calculation"]'))probe.cancelClicks.push(performance.now())},true);
  setInterval(()=>probe.beats.push(performance.now()),10);
  const Original=Worker;
  window.Worker=class extends Original{
   postMessage(...args){probe.sent.push(performance.now());return super.postMessage(...args)}
   terminate(){probe.terminated.push(performance.now());return super.terminate()}
  };
 });
 await page.goto(base);
 const calculate=page.getByRole('button',{name:'Calculate',exact:true});
 await calculate.waitFor();
 async function load(session){await page.locator('#sessionFileInput').setInputFiles({name:'worker-smoke.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(session))}); await page.waitForFunction(()=>!document.querySelector('.calculate-action > button').disabled);}
 async function runSuccess(){await calculate.click();await page.waitForFunction(()=>!document.querySelector('.calculate-action > button').disabled);await page.locator('.probability-total-value').waitFor();assert.match(await page.locator('.probability-total-value').innerText(),/50[.,]00/);assert.equal(await page.locator('.probability-group').count(),1);assert.equal(await page.locator('.combo-probability-item').count(),1);}
 await load(simple);await runSuccess();
 const foreground=await page.evaluate(async json=>{
  const rt=getDotnetRuntime(0);const exports=await rt.getAssemblyExports(rt.getConfig().mainAssemblyName);
  await new Promise(r=>setTimeout(r,50));const start=performance.now();
  const response=exports.YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.CalculationWorkerExports.Calculate(json);
  const elapsed=performance.now()-start;await new Promise(r=>setTimeout(r,50));
  return {elapsed,response:JSON.parse(response),maxHeartbeatGap:Math.max(...probe.beats.slice(1).map((t,i)=>t-probe.beats[i]))};
 },JSON.stringify(wire));
 assert.ok(foreground.response.Result || foreground.response.IsLimit);
 console.log(JSON.stringify({engine,foreground:{elapsed:foreground.elapsed,maxHeartbeatGap:foreground.maxHeartbeatGap}}));
 const evidence=[];
 for(const [width,theme] of [[1440,'light'],[1440,'dark'],[390,'light'],[390,'dark']]){
  await page.setViewportSize({width,height:900});await page.getByLabel('Color theme preference').selectOption(theme);
  await load(heavy);
  const sentBefore=await page.evaluate(()=>probe.sent.length);
  await page.evaluate(()=>{probe.beats=[]});
  await calculate.click();
  await page.waitForFunction(n=>probe.sent.length>n,sentBefore);
  await page.waitForTimeout(100);
  assert.equal(await page.getByRole('button',{name:'Cancel calculation',exact:true}).count(),1,'CPU job should still be active');
  await page.getByLabel('Color theme preference').selectOption(theme==='dark'?'light':'dark');
  await page.getByRole('button',{name:'Import YDKe',exact:true}).click();
  await page.getByLabel('YDKe deck code',{exact:true}).fill('responsive input');
  assert.equal(await page.getByLabel('YDKe deck code',{exact:true}).inputValue(),'responsive input');
  await page.getByRole('button',{name:'Cancel',exact:true}).click();
  if(process.env.SMOKE_SCREENSHOT && width===390 && theme==='dark')
   await page.locator('.results-section').screenshot({path:process.env.SMOKE_SCREENSHOT.replace('.png','-cancel.png')});
  const started=Date.now();await page.getByRole('button',{name:'Cancel calculation',exact:true}).click();
  await page.waitForFunction(()=>!document.querySelector('.calculate-action > button').disabled);
  const uiCancelledMs=Date.now()-started;
  for(let i=0;i<300 && liveWorkers;i++) await page.waitForTimeout(10);
  assert.equal(liveWorkers,0,'terminated worker must lose its execution context');
  const closedAfterMs=Date.now()-started;
  const cpuAfterClose=await rendererCpu();
  await page.waitForTimeout(250);
  const postCloseCpuMs=cdp?((await rendererCpu())-cpuAfterClose)*1000:null;
  const metrics=await page.evaluate(()=>({maxHeartbeatGap:Math.max(...probe.beats.slice(1).map((t,i)=>t-probe.beats[i])),client:document.documentElement.clientWidth,scroll:document.documentElement.scrollWidth}));
  assert.equal(metrics.client,metrics.scroll);
  evidence.push({width,theme,uiCancelledMs,cancelAndCloseMs:closedAfterMs,cancelToTerminateMs:await page.evaluate(()=>probe.terminated.at(-1)-probe.cancelClicks.at(-1)),postCloseCpuMs,closedWorkers,...metrics});
  await load(simple);await runSuccess();
 }
 // Full heavy result, then stale preservation, edit, replacement and navigation cancellation.
 await load(heavy);await calculate.click();
 await page.locator('.probability-total-value').waitFor({timeout:45000});
 assert.equal(await page.locator('.combo-probability-item').count(),18);
 const oldResult=await page.locator('.probability-total-value').innerText();
 async function startCpu(){const n=await page.evaluate(()=>probe.sent.length);await calculate.click();await page.waitForFunction(n=>probe.sent.length>n,n);}
 async function waitClosed(){for(let i=0;i<300 && liveWorkers;i++)await page.waitForTimeout(10);assert.equal(liveWorkers,0);}
 await startCpu();await page.locator('#handSize').fill('4');await page.locator('#handSize').press('Tab');
 await page.locator('.probability-result-status').waitFor();await waitClosed();
 assert.equal(await page.locator('.probability-total-value').innerText(),oldResult);
 await page.locator('#handSize').fill('5');await page.locator('#handSize').press('Tab');
 await startCpu();await load(simple);await waitClosed();await runSuccess();
 await load(heavy);await startCpu();await page.getByRole('link',{name:'Help',exact:true}).click();await waitClosed();
 await page.goBack();await calculate.waitFor();await load(simple);await runSuccess();
 // Unavailable-worker failure and recovery in a fresh context.
 const failureContext=await browser.newContext();
 const failurePage=await failureContext.newPage();
 await failurePage.addInitScript(()=>{window.savedWorker=Worker;window.Worker=class{constructor(){throw new Error('worker unavailable')}}});
 await failurePage.goto(base);
 await failurePage.getByRole('button',{name:'Calculate',exact:true}).waitFor();
 await failurePage.locator('#sessionFileInput').setInputFiles({name:'simple.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(simple))});
 await failurePage.getByRole('button',{name:'Calculate',exact:true}).click();
 await failurePage.getByRole('alert').filter({hasText:'Background calculation is unavailable'}).waitFor();
 await failurePage.evaluate(()=>window.Worker=window.savedWorker);
 await failurePage.getByRole('button',{name:'Calculate',exact:true}).click();
 await failurePage.locator('.probability-total-value').waitFor();
 await failureContext.close();
 const limited={...heavy,Cards:heavy.Cards.map(c=>({...c,Copies:c.Id==='blank'?22:1})),Combos:heavy.Combos.map(c=>({...c,Categories:c.Categories.map(r=>({...r,MinCount:0,MaxCount:0}))}))};
 await load(limited);await calculate.click();await page.getByRole('alert').filter({hasText:'Calculation stopped because it exceeded'}).waitFor({timeout:45000});
 await load(simple);await runSuccess();
 if(process.env.SMOKE_SCREENSHOT) {
  await page.getByLabel('Color theme preference').selectOption('dark');
  await page.screenshot({path:process.env.SMOKE_SCREENSHOT,fullPage:true});
 }
 console.log(JSON.stringify({engine,evidence,errors,browser:browser.version()}));assert.deepEqual(errors,[]);
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
