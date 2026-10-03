import test, { beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { createJob } from '../../YGOProbabilityCalculatorBlazor/wwwroot/js/background-calculation.js';

let workers;
class FakeWorker {
    constructor() { workers.push(this); }
    postMessage(message) { this.sent = message; }
    terminate() { this.terminated = true; }
    emit(data) { this.onmessage?.({ data }); }
}
beforeEach(() => { workers = []; globalThis.Worker = FakeWorker; });

test('startup handshake sends one snapshot and resolves matching response, releasing handlers', async () => {
    const job = createJob();
    const result = job.run('snapshot');
    const worker = workers[0];
    assert.equal(worker.sent, undefined);
    worker.emit({ ready: true });
    assert.deepEqual(worker.sent, { id: 1, json: 'snapshot' });
    worker.emit({ id: 1, response: 'result' });
    assert.equal(await result, 'result');
    assert.equal(worker.terminated, true);
    assert.equal(worker.onmessage, null);
    job.dispose();
});

for (const running of [false, true]) test(`termination cancels ${running ? 'CPU work' : 'initialization'} without a worker message`, async () => {
    const job = createJob();
    const result = job.run('snapshot');
    const worker = workers[0];
    if (running) worker.emit({ ready: true });
    const lateCallback = worker.onmessage;
    job.dispose();
    assert.equal(worker.terminated, true);
    await assert.rejects(result, /cancelled/);
    lateCallback({ data: { ready: true } });
    lateCallback({ data: { id: 1, response: 'late' } });
    assert.equal(worker.onmessage, null);
});

test('rapid restart owns a different worker and old callbacks cannot settle it', async () => {
    const first = createJob(), oldResult = first.run('old');
    const late = workers[0].onmessage;
    first.dispose();
    await assert.rejects(oldResult);
    const next = createJob(), result = next.run('new');
    late({ data: { error: 'old failure' } });
    assert.equal(workers[1].terminated, undefined);
    workers[1].emit({ ready: true });
    workers[1].emit({ id: 1, response: 'new result' });
    assert.equal(await result, 'new result');
});

for (const event of ['onerror', 'onmessageerror']) test(`${event} rejects and terminates`, async () => {
    const job = createJob(), result = job.run('snapshot');
    workers[0][event]({ preventDefault() {} });
    await assert.rejects(result, /Background calculation/);
    assert.equal(workers[0].terminated, true);
});

test('unsupported runtime fails clearly without running any fallback', async () => {
    globalThis.Worker = class { constructor() { throw new Error('unsupported'); } };
    const job = createJob();
    await assert.rejects(job.run('snapshot'), /unavailable/);
});

test('initialization error before run is retained', async () => {
    const job = createJob();
    workers[0].emit({ error: 'runtime unavailable' });
    await assert.rejects(job.run('snapshot'), /runtime unavailable/);
    assert.equal(workers[0].terminated, true);
});

test('invalid response rejects instead of leaving a stuck busy job', async () => {
    const job = createJob(), result = job.run('snapshot');
    workers[0].emit({ id: 99 });
    await assert.rejects(result, /invalid response/);
    assert.equal(workers[0].terminated, true);
});

test('duplicate run is rejected without abandoning the first request', async () => {
    const job = createJob(), first = job.run('snapshot');
    await assert.rejects(job.run('duplicate'), /already running/);
    job.dispose();
    await assert.rejects(first, /cancelled/);
});

test('startup timeout terminates an unresponsive runtime', async t => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const job = createJob(), result = job.run('snapshot');
    const rejected = assert.rejects(result, /within 30 seconds/);
    t.mock.timers.tick(30000);
    await rejected;
    assert.equal(workers[0].terminated, true);
});

test('postMessage failure terminates instead of retaining a pending request', async () => {
    const job = createJob(), result = job.run('snapshot');
    workers[0].postMessage = () => { throw new Error('clone failure'); };
    workers[0].emit({ ready: true });
    await assert.rejects(result, /Could not send/);
    assert.equal(workers[0].terminated, true);
});
