import test from 'node:test';
import assert from 'node:assert/strict';
import { createOwner } from '../../YGOProbabilityCalculatorBlazor/wwwroot/js/background-calculation.js';

let epoch = 0;
function setup(t, progressive = false) {
    let now = epoch += 1000000;
    const workers = [];
    t.mock.timers.enable({ apis: ['setTimeout'] });
    t.mock.method(performance, 'now', () => now);
    const previousWorker = globalThis.Worker;
    globalThis.Worker = class {
        sent = []; terminated = 0;
        constructor() { workers.push(this); }
        postMessage(data) { this.sent.push(data); }
        terminate() { this.terminated++; }
        emit(data) { this.onmessage?.({data}); }
    };
    t.after(() => { globalThis.Worker = previousWorker; });
    const owner = createOwner();
    t.after(() => owner.dispose());
    const job = owner.createJob();
    const promise = progressive ? job.runProgressive('frozen', true) : job.run('frozen', true);
    const worker = workers[0];
    return {owner, job, promise, worker, workers,
        tick(ms) { now += ms; t.mock.timers.tick(ms); },
        elapse(ms) { now += ms; },
        ready() { worker.emit({ready:true}); },
        emit(response, remainingWork = '100') { worker.emit({id:1, response:JSON.stringify(response), remainingWork}); }};
}
globalThis.Worker ??= class {};

test('progressive initial response is immediate but never releases the single lease', async t => {
    const s = setup(t, true); s.ready();
    let delivered = false; s.promise.then(() => delivered = true);
    await Promise.resolve(); assert.equal(delivered, false);
    const initial = success(); s.emit(initial, '1234');
    const stage = await s.promise;
    assert.equal(stage.isPreliminary, true);
    assert.deepEqual(JSON.parse(stage.response), initial);
    assert.throws(() => s.job.release(), /successful response/);
    assert.throws(() => s.owner.createJob(), /already running/);
    assert.deepEqual(s.worker.sent[1], {id:1,json:'frozen',fastStage:'progressive-exact-v1'});
    s.emit(success(0));
    assert.equal(JSON.parse(await s.job.finish()).Result.Contract.AutomaticExact, 0);
    s.job.release(); assert.equal(s.worker.terminated, 0);
});

for (const [kind, outcome] of [['work',2],['storage',3],['unsupported',4],['transport',5],['malformed',5]]) {
    test(`progressive ${kind} retains exact initial payload and clears lease`, async t => {
        const s = setup(t, true); s.ready(); const initial = success(2); s.emit(initial);
        assert.equal((await s.promise).isPreliminary, true);
        if (kind === 'work') s.emit({Error:'limit',LimitReason:1});
        if (kind === 'storage') s.emit({Error:'limit',LimitReason:2});
        if (kind === 'unsupported') s.emit({Error:'unsupported',FailureKind:3});
        if (kind === 'transport') s.worker.onerror({preventDefault(){}});
        if (kind === 'malformed') s.worker.emit({id:1,response:'{',remainingWork:'1'});
        const final = JSON.parse(await s.job.finish());
        assert.equal(final.Result.Contract.AutomaticExact, outcome);
        delete final.Result.Contract.AutomaticExact; assert.deepEqual(final, initial);
        assert.equal(s.worker.terminated,1); s.job.release();
    });
}

for (const kind of ['exact','initial-failure']) {
    test(`progressive ${kind} has only a final stage`, async t => {
        const s = setup(t, true); s.ready();
        s.emit(kind === 'initial-failure' ? {Error:'limit',LimitReason:1} : success(0), '10');
        const stage = await s.promise; assert.equal(stage.isPreliminary,false);
        assert.equal(await s.job.finish(), stage.response);
        assert.equal(s.worker.sent.length,1); s.job.release();
    });
}

test('cancel after first delivery observes terminal rejection before finish is requested', async t => {
    const s = setup(t,true); s.ready(); s.emit(success()); await s.promise;
    const late = s.worker.onmessage; const final = s.job.finish(); s.job.dispose();
    await assert.rejects(final,/cancelled/);
    late({data:{id:1,response:JSON.stringify(success(0)),remainingWork:'1'}});
    const next = s.owner.createJob(); const first = next.runProgressive('next',true);
    s.tick(2500); s.job.dispose(); s.workers[1].emit({ready:true});
    s.workers[1].emit({id:2,response:JSON.stringify(success(0)),remainingWork:'1'});
    assert.equal((await first).isPreliminary,false); next.release();
    assert.equal(s.workers[1].terminated,0);
});

test('progressive startup cancellation settles the first and unobserved terminal promises', async t => {
    const s = setup(t,true); const rejected = assert.rejects(s.promise,/cancelled/);
    s.owner.dispose(); await rejected; assert.equal(s.worker.terminated,1);
});
function success(accuracy = 1) {
    return {Result:{TotalProbability:0.75, Contract:{Version:1, RequestedMode:0, ExecutedMode:0, Completion:0,
        Accuracy:accuracy, Fractions:{Total:{Numerator:'3',Denominator:'4'}}}, SearchAvailability:{untouched:true}}};
}

test('cancelling automatic startup also delays replacement until shutdown grace ends', async t => {
    const s=setup(t);
    const rejection=assert.rejects(s.promise,/cancelled/);
    s.job.dispose(); await rejection;
    assert.equal(s.worker.terminated,1);
    const next=s.owner.createJob(); const pending=next.run('next',true);
    s.tick(2499); assert.equal(s.workers.length,1);
    s.tick(1); assert.equal(s.workers.length,2);
    next.dispose(); await assert.rejects(pending,/cancelled/);
});

test('retains complete fallback, spends only remaining Work and accepts Fast-proven Exact', async t => {
    const s=setup(t); s.ready(); s.tick(400); s.emit(success(), '149000000');
    assert.deepEqual(s.worker.sent[1], {id:1,json:'frozen',fastStage:'upgrade',remainingWork:'149000000'});
    s.tick(4500); const exact=success(0); s.emit(exact,'120000000');
    const result=JSON.parse(await s.promise);
    assert.equal(result.Result.Contract.AutomaticExact,0);
    assert.equal(result.Result.Contract.ExecutedMode,0);
    assert.deepEqual(result.Result.Contract.Fractions,exact.Result.Contract.Fractions);
    s.job.release(); assert.equal(s.worker.terminated,0);
});

test('five-second upgrade timer really terminates and preserves an Uncertified fallback', async t => {
    const s=setup(t); s.ready(); s.tick(300); const original=success(2); s.emit(original);
    s.tick(4999); assert.equal(s.worker.terminated,0);
    s.tick(1); assert.equal(s.worker.terminated,1);
    const result=JSON.parse(await s.promise);
    assert.equal(result.Result.Contract.AutomaticExact,1);
    delete result.Result.Contract.AutomaticExact;
    assert.deepEqual(result,original);
    s.job.release();
    const next=s.owner.createJob(); const pending=next.run('new',true);
    assert.equal(s.workers.length,1,'no simultaneous restart during shutdown grace');
    s.tick(2499); assert.equal(s.workers.length,1);
    s.tick(1); assert.equal(s.workers.length,2);
    next.dispose(); await assert.rejects(pending,/cancelled/);
    s.job.dispose(); assert.equal(s.workers[1].terminated,1);
});

test('seven-second opportunity prevents four plus four seconds', async t => {
    const s=setup(t); s.ready(); s.tick(4000); s.emit(success()); s.tick(3000);
    assert.equal(JSON.parse(await s.promise).Result.Contract.AutomaticExact,1);
    assert.equal(s.worker.terminated,1); s.job.release();
});

test('startup is separate and long initial Fast completes without an upgrade or termination', async t => {
    const s=setup(t); s.tick(29000); assert.equal(s.worker.sent.length,0);
    s.ready(); s.tick(20000); assert.equal(s.worker.terminated,0);
    const original=success(); s.emit(original);
    const result=JSON.parse(await s.promise);
    assert.equal(result.Result.Contract.AutomaticExact,6);
    delete result.Result.Contract.AutomaticExact; assert.deepEqual(result,original);
    assert.equal(s.worker.sent.length,1); s.job.release();
});

test('long initial proven Exact retains its original outcome', async t => {
    const s=setup(t); s.ready(); s.tick(12000); const original=success(0); s.emit(original);
    assert.deepEqual(JSON.parse(await s.promise),original); s.job.release();
    assert.equal(s.worker.sent.length,1); assert.equal(s.worker.terminated,0);
});

test('long initial typed failure remains a typed failure without invented fallback', async t => {
    const s=setup(t); s.ready(); s.tick(20000);
    const original={Error:'work limit',LimitReason:1}; s.emit(original,'0');
    assert.deepEqual(JSON.parse(await s.promise),original);
    assert.equal(s.worker.sent.length,1); assert.equal(s.worker.terminated,0);
    s.job.dispose();
});

test('long initial Fast remains manually cancellable', async t => {
    const s=setup(t); s.ready(); s.tick(30000);
    const rejection=assert.rejects(s.promise,/cancelled/);
    s.job.dispose(); await rejection; assert.equal(s.worker.terminated,1);
});

test('already exact does not launch a second solver', async t => {
    const s=setup(t); s.ready(); s.emit(success(0)); await s.promise; s.job.release();
    s.tick(6000); assert.equal(s.worker.sent.length,1); assert.equal(s.worker.terminated,0);
});

test('manual cancel after fallback must never accept it or a late upgrade', async t => {
    const s=setup(t); s.ready(); s.emit(success()); const late=s.worker.onmessage;
    const rejection=assert.rejects(s.promise,/cancelled/); s.job.dispose(); await rejection;
    late({data:{id:1,response:JSON.stringify(success(0)),remainingWork:'90'}});
    const next=s.owner.createJob(); const pending=next.run('next',true); s.tick(2500);
    s.job.dispose(); s.workers[1].emit({ready:true});
    s.workers[1].emit({id:2,response:JSON.stringify(success(0)),remainingWork:'99'});
    await pending; next.release(); assert.equal(s.workers[1].terminated,0);
});

test('absolute deadline rejects a reply even when its event precedes delayed timer callback', async t => {
    const s=setup(t); s.ready(); s.emit(success()); s.elapse(5000); s.emit(success(0));
    assert.equal(JSON.parse(await s.promise).Result.Contract.AutomaticExact,1); s.job.release();
});

for (const [failure,outcome] of [
    [{Error:'work',LimitReason:1},2], [{Error:'storage',LimitReason:2},3],
    [{Error:'unsupported',FailureKind:3},4], [{Error:'execution',FailureKind:2},5],
    [success(1),5]
]) test(`upgrade failure ${JSON.stringify(failure)} retains completed response`, async t => {
    const s=setup(t); s.ready(); const original=success(); s.emit(original); s.emit(failure);
    const result=JSON.parse(await s.promise); assert.equal(result.Result.Contract.AutomaticExact,outcome);
    delete result.Result.Contract.AutomaticExact; assert.deepEqual(result,original);
    assert.equal(s.worker.terminated,1); s.job.release();
});

test('worker transport failure after fallback retains it; initial transport error does not', async t => {
    const s=setup(t); s.ready(); s.emit(success()); s.worker.onerror({preventDefault(){}});
    assert.equal(JSON.parse(await s.promise).Result.Contract.AutomaticExact,5); s.job.release();
});

test('cancelling during restart cooldown never creates a worker later', async t => {
    const s=setup(t); s.ready(); s.emit(success()); s.tick(5000); await s.promise; s.job.release();
    const next=s.owner.createJob(); const pending=next.run('next',true);
    next.dispose(); await assert.rejects(pending,/cancelled/); s.tick(10000);
    assert.equal(s.workers.length,1);
});

for (const initialDelay of [0, 12000]) {
    test(`Progressive continues beyond both former windows after ${initialDelay}ms initial Fast`, async t => {
        const s = setup(t, true); s.ready(); s.tick(initialDelay); s.emit(success(), '123');
        assert.equal((await s.promise).isPreliminary, true);
        let settled = false; const final = s.job.finish(); final.then(() => settled = true);
        s.tick(60000); await Promise.resolve();
        assert.equal(settled, false); assert.equal(s.worker.terminated, 0);
        assert.equal(s.worker.sent.length, 2);
        s.emit(success(0));
        assert.equal(JSON.parse(await final).Result.Contract.AutomaticExact, 0);
        s.job.release();
        const next = s.owner.createJob(); const result = next.run('fast-only', false);
        assert.equal(s.workers.length, 1);
        assert.deepEqual(s.worker.sent.at(-1), {id:2,json:'fast-only'});
        s.worker.emit({id:2,response:'single'}); assert.equal(await result,'single'); next.release();
    });
}

for (const [remaining, outcome] of [['',5], ['-1',5], [null,5]]) {
    test(`Progressive invalid/empty Work allowance ${remaining} keeps completed Fast`, async t => {
        const s=setup(t,true); s.ready(); const initial=success(); s.emit(initial,remaining);
        const stage=await s.promise;
        assert.equal(stage.isPreliminary,false);
        const result=JSON.parse(stage.response);
        assert.equal(result.Result.Contract.AutomaticExact,outcome);
        delete result.Result.Contract.AutomaticExact;
        assert.deepEqual(result,initial);
        assert.equal(s.worker.sent.length,1);
        assert.equal(await s.job.finish(),stage.response);
        s.job.release();
    });
}

for (const remaining of ['0', '000', '1', '149999999']) test(`progressive Exact outlives old deadlines with ${remaining} Fast Work remaining`, async t => {
    const s = setup(t, true); s.ready(); s.emit(success(), remaining);
    assert.equal((await s.promise).isPreliminary, true);
    assert.deepEqual(s.worker.sent[1], { id: 1, json: 'frozen', fastStage: 'progressive-exact-v1' });
    s.tick(600000);
    assert.equal(s.worker.terminated, 0);
    s.emit(success(0), '0');
    assert.equal(JSON.parse(await s.job.finish()).Result.Contract.AutomaticExact, 0);
    s.job.release();
});

test('manual Exact uses an explicit worker command and remains cancellable after ten minutes', async t => {
    const s = setup(t); s.job.dispose(); await assert.rejects(s.promise);
    const job = s.owner.createJob(); const pending = job.run('exact', false, true);
    s.tick(2500); const worker = s.workers.at(-1); worker.emit({ ready: true });
    assert.deepEqual(worker.sent[0], { id: 2, json: 'exact', execution: 'interactive-exact-v1' });
    s.tick(600000); assert.equal(worker.terminated, 0);
    job.dispose(); await assert.rejects(pending, /cancelled/);
    assert.equal(worker.terminated, 1);
});
