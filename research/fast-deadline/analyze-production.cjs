const fs=require('fs'),assert=require('node:assert/strict');
const reference=JSON.parse(fs.readFileSync(process.argv[2]));
const candidate=JSON.parse(fs.readFileSync(process.argv[3]));
const q=(a,p)=>a.length?[...a].sort((a,b)=>a-b)[Math.ceil(a.length*p)-1]:null;
let checks=0;const summary=[];
const same=(a,b)=>{assert.deepEqual(a,b);checks++;};
for(const name of new Set(candidate.runs.map(r=>r.name))) {
 const runs=candidate.runs.filter(r=>r.name===name);
 const baseline=reference.runs.filter(r=>r.name===name);
 const fast=baseline.find(r=>r.mode==='fast'&&r.result)?.result;
 const exact=baseline.find(r=>r.mode==='exact'&&r.result)?.result;
 for(const run of runs) {
  const sends=run.messages.filter(m=>m.sent);
  const explicit=JSON.parse(sends[0].json).Options?.Mode===2;
  const initial=explicit?exact:fast;
  const received=run.messages.filter(m=>m.response).map(m=>JSON.parse(m.response));
  const firstMessage=run.messages.find(m=>m.response);
  if(firstMessage?.remainingWork!==undefined) {
   const expectedWork=baseline.find(r=>r.mode==='fast').stages[0].Work;
   same(JSON.parse(sends[0].json).WorkUnits-Number(firstMessage.remainingWork),expectedWork);
  }
  if(received[0]?.Result) same(received[0].Result,initial);
  const result=run.response?.Result;
  if(!result) {
   assert(!initial,`${name} lost completed reference`);checks++;
   const outcome=baseline.find(r=>r.mode===(explicit?'exact':'fast'))?.stages[0]?.Outcome;
   if(outcome==='WorkLimit')same(run.response?.LimitReason,1);
   if(outcome==='StorageLimit')same(run.response?.LimitReason,2);
   continue;
  }
  const comparable=structuredClone(result);delete comparable.Contract.AutomaticExact;
  if(result.Contract.AutomaticExact===0) {
   const expected=structuredClone(exact);expected.Contract.RequestedMode=expected.Contract.ExecutedMode=0;
   same(comparable,expected);
  } else same(comparable,initial);
  if(explicit||fast.Contract.Accuracy===0||result.Contract.AutomaticExact===6) same(sends.length,1);
  if(sends.length===2) {
   same(sends[1].json,sends[0].json);
   same(sends[1].remainingWork,run.messages.find(m=>m.remainingWork&&!m.sent).remainingWork);
   const spent=baseline.find(r=>r.mode==='fast'&&r.stages.length)?.stages[0].Work;
   const allowance=JSON.parse(sends[0].json).WorkUnits;
   same(Number(sends[1].remainingWork),allowance-spent);
   if(result.Contract.AutomaticExact===0) {
    const final=run.messages.filter(m=>m.response).at(-1);
    const physical=baseline.find(r=>r.mode==='exact'&&r.stages.length).stages[0].Work;
    same(allowance-Number(final.remainingWork),spent+physical);
    assert(spent+physical<=allowance);checks++;
   }
  }
 }
 const warm=runs.filter(r=>r.iteration>=0&&!r.cold);
 const times=warm.map(r=>r.totalMs);
 const timeouts=runs.filter(r=>r.response?.Result?.Contract.AutomaticExact===1).map(r=>{
  const sends=r.messages.filter(m=>m.sent);
  return {upgradeMs:r.terminated-sends[1].sent,latenessMs:r.terminated-Math.min(sends[0].sent+7000,sends[1].sent+5000)};
 });
 summary.push({name,n:runs.length,warmN:warm.length,outcomes:[...new Set(runs.map(r=>r.response?.Result?.Contract?.AutomaticExact??r.response?.Error??r.error??'already-exact'))],
 p50:q(times,.5),p90:q(times,.9),coldMs:runs.filter(r=>r.cold).map(r=>r.bootstrapMs),
 totalMax:Math.max(...runs.map(r=>r.totalMs)),maxCapacity:Math.max(0,...runs.map(r=>r.heapBytes??0)),timeouts});
}
fs.writeFileSync(process.argv[4],JSON.stringify({checks,summary},null,2));console.log(JSON.stringify({checks,cases:summary.length}));
