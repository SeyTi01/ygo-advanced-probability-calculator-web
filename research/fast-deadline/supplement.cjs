// Additional execution-band validation, declared after the primary first observations.
// No change to routing, deadlines, Work, Storage, or acceptance thresholds.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'../..');
const corpus=JSON.parse(fs.readFileSync(path.join(__dirname,'inputs/corpus.json')));
const original=corpus.find(c=>c.Name==='vsk9-three-desires');
const extra=[];
for(const hand of [7,8])for(const draw of [2,3]) {
    const c=structuredClone(original);c.Name=`band-hand-${hand}-draw-${draw}`;c.Split='supplemental';c.Kind='hypothetical-saved-override';
    c.Input.HandSize=hand;c.Input.Cards.find(c=>c.Id==='research-desires').DrawCount=draw;extra.push(c);
}
for(const sources of [1,2]) {
    const c=structuredClone(original);c.Name=`band-extra-draw-${sources}`;c.Split='supplemental';c.Kind='synthetic-normal-40';
    const donor=c.Input.Cards.find(x=>x.ExternalCardId===14558127);donor.Copies-=sources;
    for(let i=0;i<sources;i++)c.Input.Cards.push({Id:'supplemental-draw-'+i,Name:'Hypothetical draw',Copies:1,Categories:[],ManualMetadataCategoryKeys:[],DrawCount:1,DrawOncePerTurn:true});
    extra.push(c);
}
fs.writeFileSync(path.join(__dirname,'inputs/supplemental.json'),JSON.stringify(extra,null,2));
const hash=crypto.createHash('sha256').update(fs.readFileSync(path.join(__dirname,'inputs/supplemental.json'))).digest('hex');
fs.writeFileSync(path.join(__dirname,'inputs/supplemental-freeze.json'),JSON.stringify({utc:new Date().toISOString(),hash,
    purpose:'Independent validation of the 3-5 second execution band; no policy tuning. Primary first observations were visible; supplemental results were not.'},null,2));
const smoke=['vsk9-three-desires','ordered-target-loss-40'].map(name=>{
    const c=structuredClone(corpus.find(c=>c.Name===name));
    c.Session=JSON.parse(cp.execFileSync('git',['-c',`safe.directory=${root.replaceAll('\\','/')}`,'show',
        `487b344766734874d7c70c2d8f95c7f567ea62bc:research/fast-refinement/inputs/${name}.session.json`],{cwd:root,maxBuffer:5000000}));return c;
});
fs.writeFileSync(path.join(__dirname,'inputs/smoke.json'),JSON.stringify(smoke,null,2));
console.log(JSON.stringify({supplementalCases:extra.length,hash}));
