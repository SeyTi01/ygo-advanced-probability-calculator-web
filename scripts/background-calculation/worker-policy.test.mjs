import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../../YGOProbabilityCalculatorBlazor/wwwroot/js/calculation-worker.js', import.meta.url), 'utf8');
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
async function worker() {
    const calls = [], replies = [];
    const api = Object.fromEntries(['Calculate', 'CalculateInteractiveExact', 'BeginFast', 'UpgradeFast', 'UpgradeProgressiveFast']
        .map(name => [name, (...args) => {
            calls.push([name, ...args]);
            return name.startsWith('Calculate') ? 'result' : JSON.stringify({ Response: 'result', RemainingWork: '0' });
        }]));
    const dotnet = { create: async () => ({ getConfig: () => ({ mainAssemblyName: 'test' }),
        getAssemblyExports: async () => ({ YGOProbabilityCalculatorBlazor: { Services: { BackgroundCalculation: { CalculationWorkerExports: api } } } }) }) };
    const self = { postMessage: data => replies.push(data) };
    await new AsyncFunction('self', 'runtimeModule', source.replace("await import('../_framework/dotnet.js')", 'runtimeModule'))(self, { dotnet });
    return { calls, replies, send: data => self.onmessage({ data: { id: 1, json: 'snapshot', ...data } }) };
}
for (const [command, expected] of [
    [{}, ['Calculate', 'snapshot']],
    [{ execution: 'interactive-exact-v1' }, ['CalculateInteractiveExact', 'snapshot']],
    [{ fastStage: 'initial' }, ['BeginFast', 'snapshot']],
    [{ fastStage: 'upgrade', remainingWork: '1' }, ['UpgradeFast', 'snapshot', '1']],
    [{ fastStage: 'progressive-exact-v1' }, ['UpgradeProgressiveFast', 'snapshot']]
]) test(`worker dispatches only the explicit ${expected[0]} command`, async () => {
    const w = await worker(); w.send(command);
    assert.deepEqual(w.calls, [expected]); assert.equal(w.replies.at(-1).response, 'result');
});
for (const command of [{ execution: 'unlimited' }, { fastStage: 'future-upgrade' }])
    test(`worker rejects unknown versioned command ${JSON.stringify(command)}`, async () => {
        const w = await worker(); w.send(command);
        assert.deepEqual(w.calls, []); assert.match(w.replies.at(-1).error, /Unknown/);
    });
