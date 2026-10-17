const fs=require('node:fs'),assert=require('node:assert/strict');
const native=JSON.parse(fs.readFileSync(process.argv[2]));
const refs=JSON.parse(fs.readFileSync(process.argv[3]));let checks=0;
for(const run of native){
 const reference=refs.runs.find(r=>r.name===run.name&&r.mode===(run.mode===0?'fast':'exact'));
 assert(reference,run.name);assert.deepEqual(run.measurement.Result,reference.result,run.name);
 assert.equal(run.measurement.Outcome,reference.stages[0].Outcome,run.name);
 assert.equal(run.measurement.Work,reference.stages[0].Work,run.name);checks+=4;
}
fs.writeFileSync(process.argv[4],JSON.stringify({nativeRuns:native.length,checks}));
console.log(JSON.stringify({nativeRuns:native.length,checks}));
