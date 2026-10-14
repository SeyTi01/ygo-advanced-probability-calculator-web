import test, { beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { createOwner } from '../../YGOProbabilityCalculatorBlazor/wwwroot/js/background-calculation.js';

let workers;
class FakeWorker {
    sent = [];
    terminated = 0;
    constructor() { workers.push(this); }
    postMessage(message) { this.sent.push(message); }
    terminate() { this.terminated++; }
    emit(data) { this.onmessage?.({ data }); }
}
beforeEach(() => { workers = []; globalThis.Worker = FakeWorker; });

async function success(owner, json = 'input') {
    const job = owner.createJob();
    const worker = workers.at(-1);
    const result = job.run(json);
    worker.emit({ ready: true });
    const { id } = worker.sent.at(-1);
    worker.emit({ id, response: 'result:' + json });
    assert.equal(await result, 'result:' + json);
    job.release();
    return job;
}

test('sequential jobs reuse one runtime with distinct IDs and one handler set', async t => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const owner = createOwner();
    const first = await success(owner, 'first');
    const worker = workers[0];
    const handler = worker.onmessage;
    t.mock.timers.tick(59000);
    await success(owner, 'second');
    first.dispose(); // late cleanup cannot kill a newer completed lease
    assert.equal(workers.length, 1);
    assert.deepEqual(worker.sent, [{ id: 1, json: 'first' }, { id: 2, json: 'second' }]);
    assert.equal(worker.onmessage, handler);
    assert.equal(worker.terminated, 0);
    t.mock.timers.tick(59000); // first idle timer and startup timer were removed
    assert.equal(worker.terminated, 0);
    t.mock.timers.tick(1000);
    assert.equal(worker.terminated, 1);
    assert.equal(worker.onmessage, null);
    await success(owner, 'recreated');
    assert.equal(workers.length, 2);
    assert.equal(workers[1].sent[0].id, 3);
    owner.dispose();
    owner.dispose();
    assert.equal(workers[1].terminated, 1);
    t.mock.timers.tick(120000);
    assert.equal(workers[1].terminated, 1);
    assert.throws(() => owner.createJob(), /disposed/);
});

test('continuous usage recycles after twenty successes', async () => {
    const owner = createOwner();
    for (let i = 0; i < 20; i++) await success(owner, String(i));
    assert.equal(workers.length, 1);
    assert.equal(workers[0].terminated, 1);
    await success(owner);
    assert.equal(workers.length, 2);
    owner.dispose();
});

for (const running of [false, true]) test(`cancel ${running ? 'running' : 'startup'} and immediately restart`, async () => {
    const owner = createOwner();
    const old = owner.createJob(), pending = old.run('old');
    const worker = workers[0], late = worker.onmessage;
    if (running) worker.emit({ ready: true });
    old.dispose();
    old.dispose();
    await assert.rejects(pending, /cancelled/);
    assert.equal(worker.terminated, 1);
    const next = owner.createJob(), result = next.run('new');
    late({ data: { ready: true } });
    late({ data: { id: 1, response: 'obsolete' } });
    late({ data: { error: 'obsolete error' } });
    old.dispose();
    workers[1].emit({ ready: true });
    workers[1].emit({ id: 2, response: 'new' });
    assert.equal(await result, 'new');
    next.release();
    owner.dispose();
});

test('running requests have no deadline; response requires explicit acknowledgement', async t => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const owner = createOwner(), job = owner.createJob(), result = job.run('input');
    const worker = workers[0];
    worker.emit({ ready: true });
    worker.emit({ ready: true });
    t.mock.timers.tick(300000);
    assert.equal(worker.sent.length, 1);
    assert.equal(worker.terminated, 0);
    worker.emit({ id: 1, response: 'result' });
    await result;
    assert.throws(() => owner.createJob(), /already running/);
    job.dispose(); // decoding failure or cancellation after receipt retires it
    assert.equal(worker.terminated, 1);
    await success(owner);
    owner.dispose();
});

test('cancellation during idle handoff retires only that lease', async () => {
    const owner = createOwner(), job = await success(owner);
    job.dispose();
    assert.equal(workers[0].terminated, 1);
    const next = owner.createJob(), result = next.run('new');
    job.dispose();
    assert.equal(workers[1].terminated, 0);
    owner.dispose();
    await assert.rejects(result, /cancelled/);
});

test('duplicate/concurrent callers are rejected without disturbing active work', async () => {
    const owner = createOwner(), job = owner.createJob(), result = job.run('input');
    assert.throws(() => owner.createJob(), /already running/);
    await assert.rejects(job.run('duplicate'), /already running/);
    assert.equal(workers[0].terminated, 0);
    workers[0].emit({ ready: true });
    workers[0].emit({ id: 1, response: 'result' });
    await result;
    job.release();
    await assert.rejects(job.run('again'), /finished/);
    assert.throws(() => job.release(), /successful response/);
    owner.dispose();
});

for (const invalid of [{ id: 77, response: 'wrong id' }, { id: 1, response: {} }, null, { arbitrary: true }, { ready: true, id: 1, response: 'ambiguous' }, { ready: true, error: 'ambiguous' }, { id: 1, response: 'ambiguous', error: 'ambiguous' }]) {
    test(`invalid response ${JSON.stringify(invalid)} retires worker and permits recovery`, async () => {
        const owner = createOwner(), job = owner.createJob(), result = job.run('input');
        workers[0].emit({ ready: true });
        workers[0].emit(invalid);
        await assert.rejects(result, /invalid response/);
        assert.equal(workers[0].terminated, 1);
        await success(owner);
        owner.dispose();
    });
}

test('duplicate prior response cannot resolve a newer request on a reused worker', async () => {
    const owner = createOwner();
    await success(owner);
    const job = owner.createJob(), result = job.run('new');
    workers[0].emit({ id: 1, response: 'previous result' });
    await assert.rejects(result, /invalid response/);
    assert.equal(workers[0].terminated, 1);
    await success(owner);
    owner.dispose();
});

for (const event of ['onerror', 'onmessageerror']) test(`${event} during idle retires unsafe runtime`, async () => {
    const owner = createOwner();
    await success(owner);
    workers[0][event]({ preventDefault() {} });
    assert.equal(workers[0].terminated, 1);
    await success(owner);
    assert.equal(workers.length, 2);
    owner.dispose();
});

for (const failure of ['constructor', 'startup', 'send', 'timeout']) test(`recovery after ${failure} fault`, async t => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const owner = createOwner();
    if (failure === 'constructor') globalThis.Worker = class { constructor() { throw Error('unsupported'); } };
    const job = owner.createJob(), result = job.run('input');
    const rejected = assert.rejects(result);
    if (failure === 'startup') workers[0].emit({ error: 'boot failed' });
    if (failure === 'send') {
        workers[0].postMessage = () => { throw Error('clone failed'); };
        workers[0].emit({ ready: true });
    }
    if (failure === 'timeout') t.mock.timers.tick(30000);
    await rejected;
    job.dispose();
    globalThis.Worker = FakeWorker;
    await success(owner);
    owner.dispose();
});

test('discard after interop-handle cleanup is restricted to the released request ID', async () => {
    const owner = createOwner();
    await success(owner);
    owner.discard(1);
    assert.equal(workers[0].terminated, 1);
    await success(owner);
    owner.discard(1);
    assert.equal(workers[1].terminated, 0);
    const next = owner.createJob(), result = next.run('current');
    owner.discard(2);
    assert.equal(workers[1].terminated, 0);
    workers[1].emit({ id: 3, response: 'current' });
    await result;
    assert.equal(next.release(), 3);
    owner.dispose();
});
