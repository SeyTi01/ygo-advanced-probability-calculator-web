const fs=require('node:fs');
const assert=require('node:assert/strict');
const path=require('node:path');
const root=__dirname;
const load=name=>JSON.parse(fs.readFileSync(path.join(root,name),'utf8'));
const corpus=load('inputs/corpus.json');
const native=load('output/native.json');
const wasm=fs.existsSync(path.join(root,'output/wasm.json'))?load('output/wasm.json').output:[];
const fraction=p=>[BigInt(p.Numerator),BigInt(p.Denominator)];
const cmp=(a,b)=>{const [an,ad]=fraction(a),[bn,bd]=fraction(b);return an*bd<bn*ad?-1:an*bd>bn*ad?1:0;};
const pct=p=>Number(p.Numerator)/Number(p.Denominator)*100;
const rows=r=>r.Total?[r.Total,...r.Combos,...r.Groups]:[];
function intervals(sample){
  const c=sample.Result?.SearchAvailability;
  if(c?.Total) return [c.Total,...c.Combos,...c.Groups];
  const f=sample.Result?.Contract?.Fractions;
  return f?rows(f).map((p,i)=>({Label:i===0?'Total':i<=f.Combos.length?sample.Result.ComboProbabilities[i-1].ComboName:sample.Result.GroupProbabilities[i-f.Combos.length-1].GroupName,Lower:p,Upper:p})):[];
}
const csv=(filename,rows)=>{
  if(!rows.length)return;
  const columns=Object.keys(rows[0]);
  const escape=v=>'"'+String(v??'').replaceAll('"','""')+'"';
  fs.writeFileSync(path.join(root,'output',filename),[columns.map(escape).join(','),...rows.map(row=>columns.map(c=>escape(row[c])).join(','))].join('\n')+'\n');
};
const quant=(items,key,q)=>items.map(x=>x[key]).filter(x=>typeof x==='number').sort((a,b)=>a-b)[Math.ceil(items.length*q)-1];
const resource=[],bounds=[],checkpoints=[],cases=[];
let assertions=0;
for(const record of load('output/baseline-native.json')) {
 const measured=native.find(x=>x.Name===record.Name);
 for(const side of ['fast','exact','primary'])for(const field of ['Outcome','Route','Total','Combos','Groups','Result','Work','Entries','Cells']) {
  assert.deepEqual(record.First[side][field],measured.Samples[side][0][field],`${record.Name}/${side}/${field}: frozen baseline mismatch`);assertions++;
 }
}
for(const [platform,records] of [['native',native],['wasm',wasm]]) {
 for(const record of records) {
  const fixture=corpus.find(x=>x.Name===record.Name); const s=record.Samples;
  const base=s.fast[0], old=intervals(base), exact=s.exact[0];
  const expected=exact.Outcome==='Success'?rows(exact):[];
  assert(!exact.Result?.SearchAvailability,'Exact performed certificate');
  for(const [side,samples] of Object.entries(s)) {
   const first=samples[0];
   for(const sample of samples) {
    for(const field of ['Outcome','Route','Work','Entries','Cells','Total','Combos','Groups','Result']) {
     assert.deepEqual(sample[field],first[field],`${platform}/${record.Name}/${side}/${field}: nondeterministic`); assertions++;
    }
    if(side==='low'||side==='high') {
     for(const field of ['Outcome','Route','Total','Combos','Groups','Result']) {
      assert.deepEqual(sample[field],base[field],`${platform}/${record.Name}/${side}/${field}: changed baseline`); assertions++;
     }
     const r=sample.Refinement;
     if(!r) { assert.equal(base.Outcome,'WorkLimit');continue; }
     assert(!r.Status.startsWith('ProofFailure'),`${record.Name}/${side}/${r.Status}`);
     const cap=side==='low'?100000:1000000;
     assert(r.Work<=cap); assert(sample.Work<=fixture.Input.WorkUnits); assertions+=2;
     let previous=old;
     for(const point of [...r.Checkpoints,r]) {
      if(!point.Lower.length) continue;
      assert.equal(point.Lower.length,old.length);
      for(let row=0;row<old.length;row++) {
       assert(cmp(previous[row].Lower,point.Lower[row])<=0,'non-nested lower');
       assert(cmp(point.Upper[row],previous[row].Upper)<=0,'non-nested upper');
       assert(cmp(point.Lower[row],point.Upper[row])<=0,'reversed interval'); assertions+=3;
       if(expected[row]) {
        assert(cmp(point.Lower[row],expected[row])<=0,'lower above Exact');
        assert(cmp(expected[row],point.Upper[row])<=0,'upper below Exact');assertions+=2;
       }
      }
      previous=point.Lower.map((p,i)=>({Lower:p,Upper:point.Upper[i]}));
     }
    }
   }
   resource.push({Platform:platform,Case:record.Name,Side:side,Outcome:first.Outcome,Refinement:first.Refinement?.Status??'',
    FirstMs:record.First[side].Milliseconds,P50Ms:quant(samples,'Milliseconds',.5),P90Ms:quant(samples,'Milliseconds',.9),
    P50Allocated:quant(samples,'Allocated',.5),P90Allocated:quant(samples,'Allocated',.9),
    MaxManagedHeap:Math.max(...samples.map(x=>x.ManagedHeapBytes)),MaxWorkingSet:Math.max(...samples.map(x=>x.WorkingSetBytes??0)),
    MaxWasmCapacity:Math.max(...samples.map(x=>x.WasmHeapBytes??0)),
    Work:first.Work,PrimaryWork:first.PrimaryWork,CertificateWork:first.Work-first.PrimaryWork-(first.Refinement?.Work??0),
    RefinementWork:first.Refinement?.Work??0,BaseEntries:first.Entries,BaseCells:first.Cells,
    RefinementEntries:first.Refinement?.Entries??0,RefinementCells:first.Refinement?.Cells??0,
    PrimaryP50Ms:quant(samples,'PrimaryMilliseconds',.5),CertificateP50Ms:quant(samples,'CertificateMilliseconds',.5)});
  }
  for(let row=0;row<old.length;row++) {
   const type=row===0?'total':row<=fixture.Input.Combos.length?'combo':'group';
   const width=pct(old[row].Upper)-pct(old[row].Lower);
   for(const side of ['low','high']) {
    const r=s[side][0].Refinement;
    const lo=r?.Lower[row]??old[row].Lower,hi=r?.Upper[row]??old[row].Upper;
    const newWidth=pct(hi)-pct(lo);
    bounds.push({Platform:platform,Case:record.Name,Split:fixture.Split,Kind:fixture.Kind,Type:type,Row:row,Label:old[row].Label,
      Budget:side,Lower:pct(old[row].Lower),Upper:pct(old[row].Upper),Width:width,Exact:expected[row]?pct(expected[row]):exact.Outcome,
      NewLower:pct(lo),NewUpper:pct(hi),NewWidth:newWidth,ReductionPP:width-newWidth,ReductionPercent:width>0?(width-newWidth)/width*100:0,
      LowerGap:expected[row]?pct(expected[row])-pct(old[row].Lower):'',UpperGap:expected[row]?pct(old[row].Upper)-pct(expected[row]):'',
      Status:r?.Status??'Ineligible'});
   }
  }
  for(const side of ['low','high']) {
   const r=s[side][0].Refinement;
   for(const point of r?.Checkpoints??[]) {
    checkpoints.push({Platform:platform,Case:record.Name,Budget:side,Work:point.Work,Ms:point.Milliseconds,MassPct:pct(point.Mass),
     Lower:pct(point.Lower[0]),Upper:pct(point.Upper[0]),FullMsAtCheckpoint:base.Milliseconds+point.Milliseconds});
   }
   const relevant=bounds.filter(x=>x.Platform===platform&&x.Case===record.Name&&x.Budget===side&&x.Type!=='combo'&&x.Width>=2-1e-10);
   const p50=quant(s[side],'Milliseconds',.5),bp50=quant(s.fast,'Milliseconds',.5);
   cases.push({Platform:platform,Case:record.Name,Split:fixture.Split,Representative:['design','held-out'].includes(fixture.Split),Budget:side,
    Meaningful:relevant.length>0,Improved:relevant.some(x=>x.ReductionPP>1e-9),HalfWidth:relevant.some(x=>x.ReductionPercent>=50-1e-8),
    Eligible:!!r&&!['Ineligible','NoRemainingBudget'].includes(r.Status),Status:r?.Status??base.Outcome,
    P50Ratio:p50/bp50,P90Ratio:quant(s[side],'Milliseconds',.9)/quant(s.fast,'Milliseconds',.9),
    ExactP50Ratio:exact.Outcome==='Success'?p50/quant(s.exact,'Milliseconds',.5):null});
  }
  if(platform==='wasm') {
   const n=native.find(x=>x.Name===record.Name);
   for(const side of Object.keys(s)) {
    for(const field of ['Outcome','Route','Total','Combos','Groups','Result']) {
     assert.deepEqual(s[side][0][field],n.Samples[side][0][field],`${record.Name}/${side}/${field}: WASM/native mismatch`); assertions++;
    }
    if(s[side][0].Refinement) for(const field of ['Status','Completed','Regions','Lower','Upper']) {
     assert.deepEqual(s[side][0].Refinement[field],n.Samples[side][0].Refinement[field],`${record.Name}/${side}/${field}: WASM/native refinement mismatch`); assertions++;
    }
   }
  }
 }
}
const coldRows=[];
for(const platform of ['native','wasm']) {
 const file=path.join(root,'output','cold-'+platform+'.json');
 if(!fs.existsSync(file))continue;
 const cold=JSON.parse(fs.readFileSync(file,'utf8'));
 for(const record of platform==='wasm'?cold.output:cold) {
  const warm=(platform==='wasm'?wasm:native).find(x=>x.Name===record.Name);
  for(const [side,sample] of Object.entries(record.First)) {
   for(const field of ['Outcome','Route','Total','Combos','Groups','Result']) {
    assert.deepEqual(sample[field],warm.Samples[side][0][field],`${platform}/${record.Name}/${side}/${field}: cold mismatch`);assertions++;
   }
   if(sample.Refinement)for(const field of ['Status','Completed','Regions','Lower','Upper']){
    assert.deepEqual(sample.Refinement[field],warm.Samples[side][0].Refinement[field],`${platform}/${record.Name}/${side}/${field}: cold refinement mismatch`);assertions++;
   }
   coldRows.push({Platform:platform,Case:record.Name,Side:side,Outcome:sample.Outcome,Milliseconds:sample.Milliseconds,
    BootstrapMilliseconds:platform==='wasm'?record.BootstrapMilliseconds[['fast','low','high','exact','primary'].indexOf(side)]:null,
    Allocated:sample.Allocated,Work:sample.Work,ManagedHeap:sample.ManagedHeapBytes,WasmCapacity:sample.WasmHeapBytes??null});
  }
 }
}
csv('cold.csv',coldRows);
module.exports={resource,bounds,checkpoints,cases,coldRows};
csv('resources.csv',resource);csv('rows.csv',bounds);csv('checkpoints.csv',checkpoints);csv('cases.csv',cases);
fs.writeFileSync(path.join(root,'output','validation.json'),JSON.stringify({Assertions:assertions,NativeCases:native.length,WasmCases:wasm.length,Cases:cases},null,2));
console.log(JSON.stringify({Assertions:assertions,NativeCases:native.length,WasmCases:wasm.length}));
for(const platform of ['native','wasm'])for(const side of ['low','high']){
 const all=cases.filter(x=>x.Platform===platform&&x.Budget===side&&x.Representative), uncertain=all.filter(x=>x.Meaningful);
 console.log(platform,side,JSON.stringify({eligible:all.filter(x=>x.Eligible).length,representative:all.length,uncertain:uncertain.length,improved:uncertain.filter(x=>x.Improved).length,half:uncertain.filter(x=>x.HalfWidth).length,runtimePass:uncertain.filter(x=>x.HalfWidth&&x.P50Ratio<=1.25).length}));
}
