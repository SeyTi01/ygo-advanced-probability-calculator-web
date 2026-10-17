// Timings of the shipped lease/worker/C# wire path, without measurement exports.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const {serve}=require('../../scripts/background-calculation/measure-worker-lifecycle.cjs');
async function main(){
    const [root,fixturesFile,output]=process.argv.slice(2);
    const fixtures=JSON.parse(fs.readFileSync(fixturesFile));
    const {server,url}=await serve(root);const browser=await chromium.launch({headless:true,executablePath:process.env.YGO_BROWSER_EXECUTABLE});
    const evidence=process.env.RESUME?JSON.parse(fs.readFileSync(process.env.RESUME)):{browser:browser.version(),seed:226225,runs:[]};
    evidence.segments??=[];
    const segment=evidence.segments.length;
    evidence.segments.push({segment,utc:new Date().toISOString(),inheritedRuns:evidence.runs.length});
    const completed=new Set(evidence.runs.map(r=>`${r.name}:${r.iteration}`));
    function save(){
        fs.writeFileSync(output+'.pending',JSON.stringify(evidence,null,2));
        for(let retry=0;;retry++){
            try{fs.renameSync(output+'.pending',output);return;}
            catch(error){if(retry===100)throw error;Atomics.wait(new Int32Array(new SharedArrayBuffer(4)),0,0,50);}
        }
    }
    try {
        const page=await browser.newPage();let latestWorker;
        page.on('worker',worker=>{if(worker.url().includes('calculation-worker.js'))latestWorker=worker;});
        await page.goto(url);
        await page.evaluate(async()=>{
            const NativeWorker=Worker;window.trace=[];
            window.Worker=class extends NativeWorker {
                constructor(...args){super(...args);this.record={created:performance.now(),messages:[]};trace.push(this.record);
                    this.addEventListener('message',({data})=>{if(data.ready)this.record.ready=performance.now();else this.record.messages.push({at:performance.now(),...data});});}
                postMessage(data,...rest){this.record.messages.push({sent:performance.now(),...data});super.postMessage(data,...rest);}
                terminate(){this.record.terminated=performance.now();super.terminate();}
            };
            const {createOwner}=await import('./js/background-calculation.js');window.owner=createOwner();
        });
        const failed=new Set(evidence.runs.filter(r=>r.error||!r.response?.Result).map(r=>r.name));let seed=evidence.seed;const rand=()=>((seed=(Math.imul(seed,1664525)+1013904223)>>>0)/4294967296);
        const samples=Number(process.env.SAMPLES||10),warmups=Number(process.env.WARMUPS||2);
        for(let iteration=-warmups;iteration<samples;iteration++){
            const order=[...fixtures];for(let i=order.length-1;i>0;i--){const j=Math.floor(rand()*(i+1));[order[i],order[j]]=[order[j],order[i]];}
            for(const fixture of order){
                if(failed.has(fixture.Name))continue;
                if(completed.has(`${fixture.Name}:${iteration}`))continue;
                const result=await page.evaluate(async input=>{
                    const started=performance.now();const before=trace.length;const job=owner.createJob();
                    let response,error;try{
                        response=JSON.parse(await job.run(JSON.stringify(input),input.Options?.Mode!==1&&input.Options?.Mode!==2));
                        if(response.Result&&response.Error==null&&response.LimitReason==null&&response.FailureKind==null)job.release();
                        else job.dispose();
                    }
                    catch(e){error=String(e);job.dispose();}
                    const finished=performance.now();const worker=trace.at(-1);
                    return {response,error,totalMs:finished-started,cold:trace.length>before,
                        bootstrapMs:trace.length>before?worker.ready-worker.created:0,
                        messages:worker.messages.filter(m=>(m.at??m.sent)>=started),terminated:worker.terminated};
                },fixture.Input);
                if(!result.terminated&&latestWorker) result.heapBytes=await latestWorker.evaluate(()=>globalThis.getDotnetRuntime(0).Module.HEAPU8.buffer.byteLength).catch(()=>null);
                evidence.runs.push({name:fixture.Name,iteration,segment,...result});
                save();
                if(result.error||!result.response?.Result)failed.add(fixture.Name);
                if(result.response?.Result){
                    const contract=result.response.Result.Contract;
                    assert.equal(contract.RequestedMode,fixture.Input.Options?.Mode??0);assert.equal(contract.ExecutedMode,contract.RequestedMode);
                    if(contract.AutomaticExact===0)assert.equal(contract.Accuracy,0);
                }
                console.log(JSON.stringify({name:fixture.Name,iteration,ms:Math.round(result.totalMs),outcome:result.error??result.response?.Result?.Contract?.AutomaticExact??result.response?.Error??'already-exact'}));
            }
            save();
        }
        await page.evaluate(()=>owner.dispose());
    }finally{await browser.close();server.close();}
}
main().catch(e=>{console.error(e);process.exitCode=1;});
