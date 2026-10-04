const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium, firefox } = require('playwright');
const base = process.argv[2] || 'http://127.0.0.1:5157';
const engine = process.argv[3] || 'chromium';
const categories = Array.from({length:18},(_,i)=>({Name:`C${i}`,Source:'User'}));
const heavy = {SchemaVersion:2,Categories:categories, Cards:categories.map((c,i)=>({Id:`c${i}`,Name:null,Copies:3,Categories:[c]})).concat([{Id:'blank',Name:null,Copies:22,Categories:[]}]),Combos:categories.map((c,i)=>({Name:`Exactly one C${i}`,Categories:[{BaseCategory:c,MinCount:1,MaxCount:1}]})),ComboGroups:[],HandSize:5};
const simple={SchemaVersion:2,Categories:[categories[0]],Cards:[{Id:'a',Name:null,Copies:1,Categories:[categories[0]]},{Id:'b',Name:null,Copies:1,Categories:[]}],Combos:[{Name:'One card',GroupId:'g',Categories:[{BaseCategory:categories[0],MinCount:1,MaxCount:1}]}],ComboGroups:[{Id:'g',Name:'Group'}],HandSize:1};
const wire={Cards:heavy.Cards.map(c=>({...c,ExternalCardId:null,ManualMetadataCategoryKeys:[],Categories:c.Categories.map(c=>({...c,Source:0,MetadataKey:null}))})),Combos:heavy.Combos.map(c=>({...c,GroupId:null,Cards:[],Categories:c.Categories.map(c=>({...c,MaximumMode:0,BaseCategory:{...c.BaseCategory,Source:0,MetadataKey:null}}))})),Groups:[],HandSize:5};
const screenshotDirectory=process.env.SMOKE_SCREENSHOT?process.env.SMOKE_SCREENSHOT.replace(/\.png$/i,''):null;
(async()=>{
 const launchOptions={headless:true,timeout:20000};
 if(engine==='chromium'&&process.env.PLAYWRIGHT_CHROMIUM_CHANNEL)launchOptions.channel=process.env.PLAYWRIGHT_CHROMIUM_CHANNEL;
 if(engine==='firefox'&&process.env.PLAYWRIGHT_FIREFOX_EXECUTABLE_PATH)launchOptions.executablePath=process.env.PLAYWRIGHT_FIREFOX_EXECUTABLE_PATH;
 const browser=await ({chromium,firefox}[engine]).launch(launchOptions);
 const deadline=setTimeout(()=>browser.close(),300000);
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
  probe.savedRecords=[];
  const setItem=Storage.prototype.setItem;
  Storage.prototype.setItem=function(key,value){const result=setItem.call(this,key,value);if(this===localStorage&&key==='ygo-calculator:session-recovery:v1')probe.savedRecords.push(value);return result};
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
 async function waitIdle(){await page.waitForFunction(()=>{const button=document.querySelector('.calculate-action > button');return button&&button.getAttribute('aria-busy')!=='true'})}
 let loadSequence=0;
 async function load(session){
  const marker=`${session.Combos[0].Name} / worker smoke load ${++loadSequence}`;
  const owned={...session,Combos:session.Combos.map((combo,i)=>i===0?{...combo,Name:marker}:combo)};
  await page.locator('#sessionFileInput').setInputFiles({name:'worker-smoke.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(owned))});
  await page.waitForFunction(marker=>{try{return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Combos[0].Name===marker}catch{return false}},marker);
  await waitIdle();
 }
 async function runSuccess(){await calculate.click();await waitIdle();await page.locator('.probability-total-value').waitFor();assert.match(await page.locator('.probability-total-value').innerText(),/50[.,]00/);assert.equal(await page.locator('.probability-group').count(),1);assert.equal(await page.locator('.combo-probability-item').count(),1);}
 async function captureLayout(phase,width,theme){if(!screenshotDirectory||!((width===1440&&theme==='light')||(width===320&&theme==='dark')))return;fs.mkdirSync(screenshotDirectory,{recursive:true});const y=await page.evaluate(()=>scrollY);await page.locator('.results-section').screenshot({path:path.join(screenshotDirectory,`${engine}-${width}-${theme}-${phase}.png`)});await page.evaluate(y=>window.scrollTo({top:y,behavior:'instant'}),y)}
 await load(simple);await runSuccess();
 // Exercise the real trimmed WASM worker's conversion of counts beyond the
 // double range, including a representable subnormal. Python Fraction/comb
 // independently gives 550/1100 and float(1 / C(1080,540)) = Number.MIN_VALUE.
 for (const [population, copies, hand, minimum, expected] of [[1100,1,550,1,0.5],[1080,540,540,540,Number.MIN_VALUE]]) {
  const category={Name:'Large-count role',Source:0,MetadataKey:null};
  const input={Cards:[{Id:'a',Copies:copies,Categories:[category]},{Id:'blank',Copies:population-copies,Categories:[]}]
   .map(c=>({...c,Name:null,ExternalCardId:null,ManualMetadataCategoryKeys:[]})),
   Combos:[{Name:'Large-count result',GroupId:'g',Categories:[{BaseCategory:category,MinCount:minimum,MaxCount:minimum,MaximumMode:0}],Cards:[],AlternativeGroups:[]}],
   Groups:[{Id:'g',Name:'Group'}],HandSize:hand};
  const response=await page.evaluate(async json=>{
   const {createJob}=await import('./js/background-calculation.js');const job=createJob();
   try{return JSON.parse(await job.run(json))}finally{job.dispose()}
  },JSON.stringify(input));
  assert.equal(response.Error,null);
  assert.equal(response.Result.TotalProbability,expected);
  assert.equal(response.Result.ComboProbabilities[0].Probability,expected);
  assert.equal(response.Result.GroupProbabilities[0].Probability,expected);
 }
 console.log(JSON.stringify({engine,largeCountWorkerProbabilities:[0.5,Number.MIN_VALUE]}));
 const foreground=await page.evaluate(async json=>{
  const rt=getDotnetRuntime(0);const exports=await rt.getAssemblyExports(rt.getConfig().mainAssemblyName);
  await new Promise(r=>setTimeout(r,50));const start=performance.now();
  const response=exports.YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.CalculationWorkerExports.Calculate(json);
  const elapsed=performance.now()-start;await new Promise(r=>setTimeout(r,50));
  return {elapsed,response:JSON.parse(response),maxHeartbeatGap:Math.max(...probe.beats.slice(1).map((t,i)=>t-probe.beats[i]))};
 },JSON.stringify(wire));
 assert.ok(foreground.response.Result || foreground.response.LimitReason);
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
   await captureLayout('responsive-cancel',width,theme);
  const started=Date.now();await page.getByRole('button',{name:'Cancel calculation',exact:true}).click();
  await waitIdle();
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
 await load(heavy);await calculate.click();await waitIdle();
 await page.locator('.probability-total-value').waitFor({timeout:45000});
 assert.equal(await page.locator('.combo-probability-item').count(),18);
 const oldResult=await page.locator('.probability-total-value').innerText();
 const layoutEvidence=[];
 let legacyRowEvidence=null;
 for(const [width,theme] of [[1440,'light'],[1440,'dark'],[320,'light'],[320,'dark'],[390,'light'],[390,'dark'],[575,'light'],[575,'dark'],[576,'light'],[576,'dark']]){
  await page.setViewportSize({width,height:900});
  await page.getByLabel('Color theme preference').selectOption(theme);
  await page.evaluate(()=>{const action=document.querySelector('.calculate-action').getBoundingClientRect();window.scrollTo({top:Math.max(0,action.top+window.scrollY-300),behavior:'instant'})});
  const viewport=await page.evaluate(()=>({innerWidth:window.innerWidth,innerHeight:window.innerHeight,client:document.documentElement.clientWidth,scroll:document.documentElement.scrollWidth}));
  assert.equal(viewport.innerWidth,width,`browser did not apply ${width}px viewport`);
  assert.equal(viewport.innerHeight,900,'browser did not apply 900px height');
  assert.equal(viewport.client,viewport.scroll,`horizontal overflow at ${width}px ${theme}`);
  const measure=()=>page.evaluate(()=>{const action=document.querySelector('.calculate-action').getBoundingClientRect();const button=document.querySelector('.calculate-action > button').getBoundingClientRect();const result=document.querySelector('.probability-results').getBoundingClientRect();return{actionTop:action.top+window.scrollY,actionViewportTop:action.top,actionHeight:action.height,buttonTop:button.top+window.scrollY,buttonHeight:button.height,resultTop:result.top+window.scrollY,resultViewportTop:result.top,scrollY:window.scrollY}});
  const keyboardCase=width===320&&theme==='dark';
  const button=page.locator('.calculate-action > button');
  await button.focus();
  if(keyboardCase){
   await page.keyboard.press('Tab');await page.keyboard.press('Shift+Tab');
   const focus=await button.evaluate(element=>({visible:element.matches(':focus-visible'),shadow:getComputedStyle(element).boxShadow,outline:getComputedStyle(element).outlineStyle}));
   assert.equal(focus.visible,true,'keyboard navigation should expose the action focus indicator');
   assert.ok(focus.shadow!=='none'||focus.outline!=='none','the action focus indicator should be visible');
  }
  await page.evaluate(()=>{const rect=document.querySelector('.calculate-action').getBoundingClientRect();window.scrollTo({top:Math.round(rect.top+window.scrollY-300),behavior:'instant'})});
  const idle=await measure();
  assert.ok(idle.actionViewportTop>=0&&idle.actionViewportTop+idle.actionHeight<=900,'the shared action must be visible in the measured viewport');
  assert.ok(idle.resultTop>idle.actionTop,'the existing result should follow the action area');
  if(!legacyRowEvidence){
   legacyRowEvidence=await page.evaluate(()=>{
    const measure=()=>{const action=document.querySelector('.calculate-action').getBoundingClientRect();const result=document.querySelector('.probability-results').getBoundingClientRect();return{actionHeight:action.height,resultTop:result.top+window.scrollY}};
    const before=measure();const oldCancel=document.createElement('button');oldCancel.type='button';oldCancel.className='btn btn-outline-secondary mt-2';oldCancel.textContent='Cancel';document.querySelector('.calculate-action').append(oldCancel);const withOldRow=measure();oldCancel.remove();return{actionHeightIncrease:withOldRow.actionHeight-before.actionHeight,resultTopShift:withOldRow.resultTop-before.resultTop};
   });
   assert.ok(legacyRowEvidence.actionHeightIncrease>5,'the prior second Cancel row should increase the action area');
   assert.ok(legacyRowEvidence.resultTopShift>5,'the prior second Cancel row should fail the results-position assertion');
  }
  await page.evaluate(y=>window.scrollTo({top:y,behavior:'instant'}),idle.scrollY);
  await captureLayout('idle',width,theme);
  const sentBefore=await page.evaluate(()=>probe.sent.length);
  await button.evaluate(element=>{window.__actionReference=element});
  await page.keyboard.press('Enter');
  await page.waitForFunction(n=>probe.sent.length>n,sentBefore);
  await page.waitForTimeout(100);
  const runningButton=page.getByRole('button',{name:'Cancel calculation',exact:true});
  assert.equal(await runningButton.count(),1,'running action should be named Cancel calculation');
  assert.equal(await runningButton.isDisabled(),false,'running cancellation action should remain enabled');
  assert.equal(await page.locator('.calculate-action > button').count(),1,'there should be no second action row');
  const sameFocusedNode=await page.evaluate(()=>document.querySelector('.calculate-action > button')===window.__actionReference&&document.activeElement===window.__actionReference);
  assert.equal(sameFocusedNode,true,'changing the action role should preserve the focused button');
  assert.equal(await page.getByRole('button',{name:'Copy results',exact:true}).isDisabled(),true,'copy must be unavailable during CPU work');
  const running=await measure();
  assert.ok(Math.abs(running.scrollY-idle.scrollY)<=0.75,`scroll position changed while running at ${width}px ${theme}`);
  assert.ok(Math.abs(running.actionTop-idle.actionTop)<=0.75,`action document position changed while running at ${width}px ${theme}`);
  assert.ok(Math.abs(running.actionViewportTop-idle.actionViewportTop)<=0.75,`action moved in the viewport while running at ${width}px ${theme}`);
  assert.ok(Math.abs(running.actionHeight-idle.actionHeight)<=0.75,`action area height changed while running at ${width}px ${theme}`);
  assert.ok(Math.abs(running.buttonTop-idle.buttonTop)<=0.75,`button document position changed while running at ${width}px ${theme}`);
  assert.ok(Math.abs(running.buttonHeight-idle.buttonHeight)<=0.75,`button height changed while running at ${width}px ${theme}`);
  assert.ok(Math.abs(running.resultViewportTop-idle.resultViewportTop)<=0.75,`results moved in the viewport while running at ${width}px ${theme}`);
  assert.ok(Math.abs(running.resultTop-idle.resultTop)<=0.75,`results moved while running at ${width}px ${theme}`);
  await captureLayout('running',width,theme);
  const started=Date.now();
  await page.keyboard.press('Enter');
  await waitIdle();
  const cancelled=await measure();
  assert.equal(await page.getByRole('button',{name:'Copy results',exact:true}).isDisabled(),false,'cancel must restore copying of a still-current accepted result');
  assert.ok(Math.abs(cancelled.scrollY-idle.scrollY)<=0.75,`scroll position changed after cancellation at ${width}px ${theme}`);
  assert.ok(Math.abs(cancelled.actionTop-idle.actionTop)<=0.75,`action document position changed after cancellation at ${width}px ${theme}`);
  assert.ok(Math.abs(cancelled.actionViewportTop-idle.actionViewportTop)<=0.75,`action moved in the viewport after cancellation at ${width}px ${theme}`);
  assert.ok(Math.abs(cancelled.actionHeight-idle.actionHeight)<=0.75,`action area height changed after cancellation at ${width}px ${theme}`);
  assert.ok(Math.abs(cancelled.buttonTop-idle.buttonTop)<=0.75,`button document position changed after cancellation at ${width}px ${theme}`);
  assert.ok(Math.abs(cancelled.buttonHeight-idle.buttonHeight)<=0.75,`button height changed after cancellation at ${width}px ${theme}`);
  assert.ok(Math.abs(cancelled.resultViewportTop-idle.resultViewportTop)<=0.75,`results moved in the viewport after cancellation at ${width}px ${theme}`);
  assert.ok(Math.abs(cancelled.resultTop-idle.resultTop)<=0.75,`results moved after cancellation at ${width}px ${theme}`);
  assert.equal(await page.locator('.probability-total-value').innerText(),oldResult,'cancellation must preserve the accepted result');
  assert.equal(await page.locator('.probability-result-status').count(),0,'the current result should retain its current status');
  assert.equal(await page.locator('[role="alert"]').count(),0,'cancellation must not create an alert');
  assert.equal(await page.getByText(/calculation was cancelled/i).count(),0,'cancellation must not create a cancellation notice');
  const sameIdleNode=await page.evaluate(()=>document.querySelector('.calculate-action > button')===window.__actionReference&&document.activeElement===window.__actionReference);
  assert.equal(sameIdleNode,true,'returning to Calculate should preserve the focused button');
  await captureLayout('cancelled',width,theme);
  for(let i=0;i<300&&liveWorkers;i++)await page.waitForTimeout(10);
  assert.equal(liveWorkers,0,'cancelled layout-check worker should terminate');
  const totalCancelMs=Date.now()-started;
  const afterViewport=await page.evaluate(()=>({innerWidth:window.innerWidth,client:document.documentElement.clientWidth,scroll:document.documentElement.scrollWidth}));
  assert.equal(afterViewport.innerWidth,width);
  assert.equal(afterViewport.client,afterViewport.scroll,`horizontal overflow after cancellation at ${width}px ${theme}`);
  layoutEvidence.push({width,theme,viewport:afterViewport,idle,running,cancelled,totalCancelMs,keyboardCase});
 }
 async function startCpu(){const n=await page.evaluate(()=>probe.sent.length);await calculate.click();await page.waitForFunction(n=>probe.sent.length>n,n);}
 async function waitClosed(){for(let i=0;i<300 && liveWorkers;i++)await page.waitForTimeout(10);assert.equal(liveWorkers,0);}
 await startCpu();await page.locator('#handSize').fill('4');await page.locator('#handSize').press('Tab');
 await page.locator('.probability-result-status').waitFor();await waitClosed();
 assert.equal(await page.getByRole('button',{name:'Copy results',exact:true}).isDisabled(),false,'idle stale results retain their captured export snapshot');
 assert.equal(await page.locator('.probability-total-value').innerText(),oldResult);
 if(engine==='chromium')await page.context().grantPermissions(['clipboard-read','clipboard-write'],{origin:new URL(base).origin});
 await observeClipboard();
 await page.getByRole('button',{name:'Copy results',exact:true}).click();
 await page.locator('.probability-result-copy-success-icon').waitFor();
 const historicalCopy=await page.evaluate(()=>smokeCopiedText);
 assert.ok(historicalCopy.startsWith('Previous result — current inputs have changed.\nProbability results\nHand size: 5\n'),'idle stale copy retains the accepted hand size rather than edited hand 4');
 assert.ok(historicalCopy.includes(oldResult),'idle stale copy retains the accepted odds');
 if(engine==='chromium')assert.equal((await page.evaluate(()=>navigator.clipboard.readText())).replace(/\r\n/g,'\n'),historicalCopy);
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
 await failurePage.waitForFunction(()=>{try{return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Combos[0].Name==='One card'}catch{return false}});
 await failurePage.getByRole('button',{name:'Calculate',exact:true}).click();
 await failurePage.getByRole('alert').filter({hasText:'Background calculation is unavailable'}).waitFor();
 await failurePage.evaluate(()=>window.Worker=window.savedWorker);
 await failurePage.getByRole('button',{name:'Calculate',exact:true}).click();
 await failurePage.locator('.probability-total-value').waitFor();
 await failureContext.close();
 const limited={...heavy,Cards:heavy.Cards.map(c=>({...c,Copies:c.Id==='blank'?22:1})),Combos:heavy.Combos.map(c=>({...c,Categories:c.Categories.map(r=>({...r,MinCount:0,MaxCount:0}))}))};
 await load(limited);await calculate.click();await page.getByRole('alert').filter({hasText:'Calculation stopped because it exceeded'}).waitFor({timeout:45000});
 await load(simple);await runSuccess();
 // Combined recovery/export flows use the real components and stored envelope bytes.
 if(engine==='chromium')await page.context().grantPermissions(['clipboard-read','clipboard-write'],{origin:new URL(base).origin});
 async function savedWorkspace(handSize,count=null,name=null){
  await page.waitForFunction(({handSize,count,name})=>{try{const envelope=JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1'));const session=JSON.parse(envelope.payload);return session.HandSize===handSize&&(count===null||session.Cards.length===count)&&(name===null||session.Cards.some(c=>c.Name===name))}catch{return false}},{handSize,count,name});
  return page.evaluate(()=>localStorage.getItem('ygo-calculator:session-recovery:v1'));
 }
 async function observeClipboard(){await page.evaluate(()=>{const write=navigator.clipboard.writeText.bind(navigator.clipboard);navigator.clipboard.writeText=async text=>{if(window.failSmokeClipboard)throw new DOMException('Smoke clipboard denial','NotAllowedError');await write(text);window.smokeCopiedText=text}})}
 const combinedEvidence=[];
 for(const [width,theme] of [[1440,'light'],[1440,'dark'],[390,'light'],[390,'dark']]){
  await page.setViewportSize({width,height:900});await page.getByLabel('Color theme preference').selectOption(theme);
  await page.getByRole('link',{name:'Help',exact:true}).click();
  await page.getByRole('button',{name:'Try It with Example Data',exact:true}).click();
  await page.waitForFunction(()=>document.querySelectorAll('.card-editor').length>0&&document.querySelector('#handSize')?.value==='5');
  await calculate.click();await waitIdle();await page.locator('.probability-total-value').waitFor();
  const sampleTotal=await page.locator('.probability-total-value').innerText();
  assert.equal(await page.locator('.combo-probability-item').count(),8);
  const sampleRecord=await savedWorkspace(5,null,'Vanquish Soul Razen');
  const writesBeforeCopy=await page.evaluate(()=>probe.savedRecords.length);
  await observeClipboard();await page.getByRole('button',{name:'Copy results',exact:true}).click();
  await page.locator('.probability-result-copy-success-icon').waitFor();
  const copied=await page.evaluate(()=>smokeCopiedText);
  assert.ok(copied.includes('Probability results')&&copied.includes(sampleTotal)&&copied.includes('Full VS')&&copied.includes('VS Starter + (Fire OR Dark)'),'copied example should be readable and retain its hierarchy/names');
  let clipboardRead=false;
  // Windows clipboard text uses CRLF; compare every character after line-ending normalization.
  if(engine==='chromium'){assert.equal((await page.evaluate(()=>navigator.clipboard.readText())).replace(/\r\n/g,'\n'),copied);clipboardRead=true}
  if(screenshotDirectory){fs.mkdirSync(screenshotDirectory,{recursive:true});await page.locator('.results-section').screenshot({path:path.join(screenshotDirectory,`${engine}-${width}-${theme}-example-copy.png`)})}
  await page.locator('.probability-result-copy-success-icon').waitFor({state:'detached'});
  assert.equal(await page.evaluate(()=>localStorage.getItem('ygo-calculator:session-recovery:v1')),sampleRecord,'copy/checkmark expiry must preserve saved bytes');
  assert.equal(await page.evaluate(()=>probe.savedRecords.length),writesBeforeCopy,'copy/checkmark expiry must not write storage');
  await page.evaluate(()=>window.failSmokeClipboard=true);
  await page.getByRole('button',{name:'Copy results',exact:true}).click();
  const fallback=page.locator('textarea.probability-result-copy-text');await fallback.waitFor();assert.equal(await fallback.inputValue(),copied);
  await fallback.focus();await page.keyboard.press('Control+A');
  assert.equal(await fallback.evaluate(e=>e.selectionStart===0&&e.selectionEnd===e.value.length),true,'fallback should be fully selectable');
  await page.getByRole('button',{name:'Close copy fallback',exact:true}).click();await page.evaluate(()=>window.failSmokeClipboard=false);
  await load(heavy);await calculate.click();await waitIdle();await page.locator('.probability-total-value').waitFor();
  await startCpu();await page.locator('#handSize').fill('4');await page.locator('#handSize').press('Tab');
  await page.locator('.probability-result-status').waitFor();await waitClosed();
  const editedRecord=await savedWorkspace(4,19);
  assert.equal(JSON.parse(JSON.parse(editedRecord).payload).HandSize,4);
  await page.reload();await calculate.waitFor();
  await page.getByRole('button',{name:'Restore previous session',exact:true}).waitFor();
  assert.equal(await page.locator('.card-editor').count(),0,'refresh must wait for explicit recovery acceptance');
  assert.equal(await page.evaluate(()=>localStorage.getItem('ygo-calculator:session-recovery:v1')),editedRecord,'default startup must preserve the edited draft');
  await page.getByRole('button',{name:'Dismiss',exact:true}).click();
  const dismissalGap=await page.getByRole('heading',{name:'Categories',exact:true}).evaluate(e=>e.getBoundingClientRect().top-document.querySelector('.session-recovery-dismissed').getBoundingClientRect().bottom);
  assert.ok(dismissalGap>=12,'dismissed recovery row should remain separated from Categories');
  if(screenshotDirectory){await page.screenshot({path:path.join(screenshotDirectory,`${engine}-${width}-${theme}-dismissed-recovery.png`)})}
  await page.getByRole('button',{name:'Show recovery',exact:true}).click();
  await page.getByRole('button',{name:'Restore previous session',exact:true}).click();
  await page.waitForFunction(()=>document.querySelector('#handSize')?.value==='4'&&document.querySelectorAll('.card-editor').length===19);
  assert.equal(await page.getByRole('button',{name:'Restore previous session',exact:true}).count(),0);
  await calculate.click();await waitIdle();await page.locator('.probability-total-value').waitFor();
  assert.equal(await page.locator('.combo-probability-item').count(),18);
  await startCpu();assert.equal(await page.getByRole('button',{name:'Cancel calculation',exact:true}).count(),1);await load(simple);await waitClosed();await runSuccess();
  const replacementRecord=await savedWorkspace(1,2);
  const viewport=await page.evaluate(()=>({width:innerWidth,client:document.documentElement.clientWidth,scroll:document.documentElement.scrollWidth}));
  assert.equal(viewport.width,width);assert.equal(viewport.client,viewport.scroll);
  assert.equal(await page.locator('[role="alert"]').count(),0);
  combinedEvidence.push({width,theme,sampleTotal,clipboardRead,copiedCharacters:copied.length,editedHandSize:4,restoredCards:19,replacementHandSize:JSON.parse(JSON.parse(replacementRecord).payload).HandSize,dismissalGap,viewport});
  console.log(JSON.stringify({engine,combinedCase:combinedEvidence.at(-1)}));
 }
 if(process.env.SMOKE_SCREENSHOT) {
  await page.getByLabel('Color theme preference').selectOption('dark');
  fs.mkdirSync(screenshotDirectory,{recursive:true});await page.screenshot({path:path.join(screenshotDirectory,`${engine}-final.png`),fullPage:true});
 }
 console.log(JSON.stringify({engine,evidence,layoutEvidence,legacyRowEvidence,combinedEvidence,errors,browser:browser.version()}));assert.deepEqual(errors,[]);
 }finally{clearTimeout(deadline);await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
