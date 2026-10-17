const fs=require('node:fs');
const assert=require('node:assert/strict');
const evidence=JSON.parse(fs.readFileSync(process.argv[2]));
const rows=r=>[r.Contract.Fractions.Total,...r.Contract.Fractions.Combos,...r.Contract.Fractions.Groups];
const le=(a,b)=>BigInt(a.Numerator)*BigInt(b.Denominator)<=BigInt(b.Numerator)*BigInt(a.Denominator);
const q=(a,p)=>a.length?[...a].sort((x,y)=>x-y)[Math.max(0,Math.ceil(p*a.length)-1)]:null;
let checks=0;
const check=(v,m)=>{assert(v,m);checks++;};
const summary=[];
for(const name of [...new Set(evidence.runs.map(r=>r.name))]) {
    const all=evidence.runs.filter(r=>r.name===name);
    const fast=all.find(r=>r.mode==='fast'&&r.result)?.result;
    const exact=all.find(r=>r.mode==='exact'&&r.result)?.result;
    for(const run of all) {
        if(run.mode==='fast'&&run.result) { assert.deepEqual(run.result,fast);checks++; }
        if(run.mode==='exact'&&run.result) { assert.deepEqual(run.result,exact);checks++; }
        if(run.mode==='automatic') {
            if(run.stages[0]?.Result) {assert.deepEqual(run.stages[0].Result,fast);checks++;}
            if(run.outcome==='upgraded') {assert.deepEqual(run.result,exact);checks++;}
            if(run.outcome.startsWith('fallback')||run.outcome==='already-exact') {assert.deepEqual(run.result,fast);checks++;}
            check(run.stages.reduce((sum,s)=>sum+s.Work,0)<=150000000,`${name}: shared Work`);
            if(run.forced) check(run.beats>100,`${name}: UI heartbeat`);
        }
    }
    if(fast&&exact&&fast.SearchAvailability?.Total) {
        const intervals=[fast.SearchAvailability.Total,...fast.SearchAvailability.Combos,...fast.SearchAvailability.Groups];
        rows(exact).forEach((value,i)=>check(le(intervals[i].Lower,value)&&le(value,intervals[i].Upper),`${name}: interval ${i}`));
    }
    for(const mode of ['fast','exact','automatic']) {
        const runs=all.filter(r=>r.mode===mode);
        const warm=runs.filter(r=>r.iteration>=0&&!r.cold);
        const m=runs.find(r=>r.stages.length)?.stages.at(-1);
        summary.push({name,mode,outcomes:[...new Set(runs.map(r=>r.outcome))],n:runs.length,warmN:warm.length,
            engineP50:q(warm.map(r=>r.stages.reduce((s,x)=>s+x.Milliseconds,0)),.5),
            engineP90:q(warm.map(r=>r.stages.reduce((s,x)=>s+x.Milliseconds,0)),.9),
            dispatchP50:q(warm.map(r=>r.dispatchMs),.5),dispatchP90:q(warm.map(r=>r.dispatchMs),.9),
            totalP50:q(warm.map(r=>r.totalMs),.5),totalP90:q(warm.map(r=>r.totalMs),.9),
            work:m?.Work,entries:m?.Entries,cells:m?.Cells,
            allocatedP50:q(warm.map(r=>r.stages.reduce((s,x)=>s+x.Allocated,0)),.5),
            maxObservedHeap:Math.max(0,...runs.flatMap(r=>r.stages.map(s=>s.ManagedHeapBytes))),
            maxCapacity:Math.max(0,...runs.flatMap(r=>r.stages.map(s=>s.WasmHeapBytes)))});
    }
}
fs.writeFileSync(process.argv[3],JSON.stringify({checks,summary},null,2));
console.log(JSON.stringify({checks,rows:summary.length}));
