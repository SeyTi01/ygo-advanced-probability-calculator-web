// Production worker/UI smoke; inputs are frozen research fixtures, never compiled into the app.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const {chromium} = require('playwright');
const {serve, installProbe} = require('./measure-worker-lifecycle.cjs');
async function main() {
    const [root, fixturesPath, output] = process.argv.slice(2);
    const fixtures=JSON.parse(fs.readFileSync(fixturesPath));
    const {server,url}=await serve(root);
    const browser=await chromium.launch({headless:true,executablePath:process.env.YGO_BROWSER_EXECUTABLE});
    const deadline=setTimeout(()=>browser.close(),300000);
    const evidence={browser:browser.version(),cases:[],closed:[]};
    try {
        const context=await browser.newContext({permissions:['clipboard-read','clipboard-write']});
        const page=await context.newPage();
        page.on('worker',worker=>{
            if(!worker.url().includes('calculation-worker.js'))return;
            const record={}; evidence.closed.push(record);
            worker.on('close',async()=>{try{record.closedObserved=await page.evaluate(()=>performance.now());}catch{record.closed=true;}});
        });
        await page.addInitScript(installProbe);
        await page.addInitScript(()=>{window.beats=0;setInterval(()=>beats++,20);});
        await page.goto(url);
        let sequence=0;
        async function load(session) {
            const value=structuredClone(session); const marker='Deadline smoke '+ ++sequence;
            value.Cards[0].Name=marker;
            await page.waitForFunction(()=>document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId!==undefined);
            await page.locator('#sessionFileInput').setInputFiles({name:'deadline.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(value))});
            await page.waitForFunction(marker=>{try{return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Cards[0].Name===marker;}catch{return false;}},marker);
        }
        async function mode(value) {
            await page.getByRole('button',{name:'Calculation settings',exact:true}).click();
            await page.locator('#calculationModel').selectOption(value);
            await page.locator('#calculationModel').press('Escape');
        }
        async function calculate() {
            const index=await page.evaluate(()=>lifecycle.runs.length);
            await page.getByRole('button',{name:'Calculate',exact:true}).click();
            await page.waitForFunction(i=>lifecycle.runs[i]?.uiMs!==undefined,index,{timeout:40000});
            return await page.evaluate(i=>({run:lifecycle.runs[i],requests:lifecycle.requests.filter(r=>r.sent>=lifecycle.runs[i].started),
                workers:lifecycle.workers,accuracy:document.querySelector('.probability-results .result-accuracy')?.textContent,
                details:document.querySelector('.probability-results .accuracy-details')?.textContent,
                errors:[...document.querySelectorAll('[role=alert]')].map(x=>x.textContent)}),index);
        }
        for(const name of ['vsk9-three-desires','ordered-target-loss-40']) {
            const fixture=fixtures.find(x=>x.Name===name); assert(fixture?.Session,name);
            await load(fixture.Session); await mode('Fast');
            const result=await calculate(); evidence.cases.push({name,...result});
            assert.equal(result.errors.length,0);
            assert.match(result.accuracy,/Effects — fast/);
            assert.match(result.details,name==='vsk9-three-desires'?/completed a physical Exact/:/opportunity ended/);
            if(name==='vsk9-three-desires') {
                assert.match(result.accuracy,/Exact/);
                assert.equal(result.requests.at(-1).response.Result.Contract.ExecutedMode,0);
                await page.locator('.pin-result-action').click();
                assert.match(await page.locator('.pinned-result').innerText(),/Effects — fast/);
                await page.getByRole('button',{name:'Copy results',exact:true}).click();
                const copied=await page.evaluate(()=>navigator.clipboard.readText());
                assert.match(copied,/requested: Fast/); assert.match(copied,/completed a physical Exact/);
                evidence.copied=copied;
            }
        }
        // Manual cancellation and edits while the real physical worker is busy.
        const previous=await page.locator('.pinned-result').innerText();
        await page.getByRole('button',{name:'Calculate',exact:true}).click();
        await page.waitForFunction(()=>lifecycle.requests.at(-1)?.input && lifecycle.requests.length>0);
        await page.waitForTimeout(800);
        await page.getByRole('button',{name:'Cancel calculation',exact:true}).click();
        await page.waitForTimeout(2800);
        assert.equal(await page.locator('.pinned-result').innerText(),previous);
        assert.equal(await page.getByRole('alert').count(),0);
        await page.getByRole('button',{name:'Calculate',exact:true}).click();
        await page.waitForTimeout(700);
        await page.locator('#handSize').fill('6'); await page.locator('#handSize').press('Tab');
        await page.waitForTimeout(2800);
        assert.equal(await page.getByRole('button',{name:'Cancel calculation',exact:true}).count(),0);
        assert.equal(await page.locator('.pinned-result').innerText(),previous);
        // Explicit Exact is still running beyond the automatic deadline.
        await page.locator('#handSize').fill('5'); await page.locator('#handSize').press('Tab'); await mode('Exact');
        await page.getByRole('button',{name:'Calculate',exact:true}).click();
        await page.waitForTimeout(7200);
        assert.equal(await page.getByRole('button',{name:'Cancel calculation',exact:true}).count(),1);
        await page.getByRole('button',{name:'Cancel calculation',exact:true}).click();
        await page.waitForTimeout(2800);
        await mode('Fast');
        await page.getByRole('button',{name:'Calculate',exact:true}).click();
        await page.waitForTimeout(800);
        await load(fixtures.find(x=>x.Name==='vsk9-three-desires').Session);
        await page.waitForTimeout(2800);
        assert.equal(await page.getByRole('button',{name:'Cancel calculation',exact:true}).count(),0);
        assert.equal(await page.locator('.pinned-result').innerText(),previous);
        evidence.sessionReplacementCancelled=true;
        // Genuine synchronous block: the production watchdog cannot depend on
        // a worker message, engine Work charge or cooperative C# cancellation.
        const blocked=await context.newPage();
        const blockedClosures=[];
        let blockStartup=false;
        blocked.on('worker',worker=>{
            if(!worker.url().includes('calculation-worker.js'))return;
            const entry={}; blockedClosures.push(entry);
            worker.on('close',async()=>{entry.closedObserved=await blocked.evaluate(()=>performance.now()).catch(()=>null);});
        });
        await blocked.route('**/js/calculation-worker.js',async route=>{
            let source=fs.readFileSync(path.join(root,'js/calculation-worker.js'),'utf8');
            source=source.replace('api.UpgradeFast(data.json, data.remainingWork)', '(() => { while (true) {} })()');
            if(blockStartup) source='while (true) {}\n'+source;
            await route.fulfill({contentType:'text/javascript',body:source});
        });
        await blocked.goto(url);
        evidence.blocked=await blocked.evaluate(async input=>{
            const native=Worker;window.terminations=[];
            window.Worker=class extends native {terminate(){terminations.push(performance.now());super.terminate();}};
            const {createOwner}=await import('./js/background-calculation.js');
            const owner=createOwner(); const runs=[];
            for(let i=0;i<3;i++) {
                const job=owner.createJob(); const start=performance.now(); let beats=0;const heartbeat=setInterval(()=>beats++,20);
                const response=JSON.parse(await job.run(JSON.stringify(input),true)); job.release();clearInterval(heartbeat);
                runs.push({ms:performance.now()-start,beats,response});
            }
            owner.dispose(); return {runs,terminations};
        },fixtures.find(x=>x.Name==='vsk9-three-desires').Input);
        for(const run of evidence.blocked.runs) {assert.equal(run.response.Result.Contract.AutomaticExact,1);assert(run.beats>100);}
        for(let i=0;i<100&&blockedClosures.some(x=>x.closedObserved==null);i++)await blocked.waitForTimeout(50);
        assert.equal(blockedClosures.length,3);assert(blockedClosures.every(x=>x.closedObserved!=null));
        evidence.blocked.shutdownMs=blockedClosures.map((x,i)=>x.closedObserved-evidence.blocked.terminations[i]);
        // Cancellation can arrive before the first ready message. A blocked
        // startup must receive the same grace before a replacement is created.
        blockStartup=true;
        evidence.startupCancellation=await blocked.evaluate(async input=>{
            const Previous=Worker;window.startupCreates=[];
            window.Worker=class extends Previous {
                constructor(...args){super(...args);startupCreates.push(performance.now());}
            };
            const {createOwner}=await import('./js/background-calculation.js');
            const owner=createOwner();const job=owner.createJob();
            const pending=job.run(JSON.stringify(input),true).then(()=> 'unexpected success',error=>String(error));
            while(startupCreates.length===0)await new Promise(resolve=>setTimeout(resolve,10));
            await new Promise(resolve=>setTimeout(resolve,150));
            job.dispose();const first=await pending;
            const next=owner.createJob();
            const pendingNext=next.run(JSON.stringify(input),true).then(()=> 'unexpected success',error=>String(error));
            await new Promise(resolve=>setTimeout(resolve,2000));
            const beforeGrace=startupCreates.length;
            await new Promise(resolve=>setTimeout(resolve,650));
            next.dispose();const second=await pendingNext;owner.dispose();
            return {first,second,beforeGrace,created:startupCreates,terminations:terminations.slice(-2)};
        },fixtures.find(x=>x.Name==='vsk9-three-desires').Input);
        assert.match(evidence.startupCancellation.first,/cancelled/);
        assert.match(evidence.startupCancellation.second,/cancelled/);
        assert.equal(evidence.startupCancellation.beforeGrace,1);
        assert.equal(evidence.startupCancellation.created.length,2);
        for(let i=0;i<100&&blockedClosures.some(x=>x.closedObserved==null);i++)await blocked.waitForTimeout(50);
        assert.equal(blockedClosures.length,5);assert(blockedClosures.every(x=>x.closedObserved!=null));
        assert(blockedClosures[3].closedObserved<evidence.startupCancellation.created[1]);
        evidence.startupCancellation.shutdownMs=blockedClosures.slice(3).map((x,i)=>x.closedObserved-evidence.startupCancellation.terminations[i]);
        await blocked.close();
        await load(fixtures.find(x=>x.Name==='vsk9-three-desires').Session); await mode('Fast');
        evidence.recovery=await calculate(); assert.match(evidence.recovery.accuracy,/Exact/);
        await page.setViewportSize({width:390,height:844});
        await page.screenshot({path:output.replace(/\.json$/,'.mobile.png'),fullPage:true});
        assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
        await page.setViewportSize({width:1280,height:900});
        await page.screenshot({path:output.replace(/\.json$/,'.desktop.png'),fullPage:true});
        evidence.lifecycle=await page.evaluate(()=>lifecycle);
        await page.goto('about:blank'); await page.waitForTimeout(2800);
        fs.writeFileSync(output,JSON.stringify(evidence,null,2));
    } finally {clearTimeout(deadline);await browser.close();server.close();}
}
main().catch(error=>{console.error(error);process.exitCode=1;});
