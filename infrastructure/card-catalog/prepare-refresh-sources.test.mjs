import assert from 'node:assert/strict';
import test from 'node:test';
import { createHash } from 'node:crypto';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { prepareRefreshSources } from './prepare-refresh-sources.mjs';

function fixture(t, { checkpoints = {}, corrupt = false, incomplete = false, wrongWorkflow = false, apiFailure = false } = {}) {
    const root = mkdtempSync(join(tmpdir(), 'refresh-sources-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const output = join(root, 'inputs');
    const calls = [];
    const save = () => {
        mkdirSync(output);
        const sources = ['cardinfo.json', 'archetypes.json', 'yaml-cards.json', 'series.json', 'yaml-yugi.tar.gz'].map(file => {
            const bytes = Buffer.from(`retained ${file}`);
            writeFileSync(join(output, file), corrupt && file === 'cardinfo.json' ? 'changed' : bytes);
            return { file, bytes: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') };
        });
        if (!incomplete) writeFileSync(join(output, 'sources.json'), JSON.stringify({ version: 1, sources }));
    };
    const run = (program, args) => {
        calls.push([program, args]);
        if (program === 'python' || (program === 'gh' && args[0] === 'run')) { save(); return ''; }
        if (apiFailure) throw new Error('GitHub API unavailable');
        const endpoint = args.find(arg => arg.startsWith('repos/'));
        const id = endpoint.match(/runs\/(\d+)/)[1];
        if (endpoint.endsWith('/artifacts')) {
            const state = checkpoints[id];
            return JSON.stringify({ artifacts: state ? [{ name: `card-data-refresh-${id}`, expired: state === 'expired' }] : [] });
        }
        return JSON.stringify({ path: wrongWorkflow ? '.github/workflows/tests.yml' : '.github/workflows/card-data-refresh.yml', head_branch: 'dev' });
    };
    return { calls, options: { runId: '200', attempt: '1', output, run } };
}

test('a new scheduled run acquires fresh inputs; a rerun restores its checkpoint without provider requests', async t => {
    const fresh = fixture(t);
    assert.deepEqual(await prepareRefreshSources(fresh.options), { restored: false, checkpointExists: false, sourceRun: '200' });
    assert.deepEqual(fresh.calls.map(([program]) => program), ['python']);
    const retry = fixture(t, { checkpoints: { 200: 'ready' } });
    assert.deepEqual(await prepareRefreshSources({ ...retry.options, attempt: '2' }),
        { restored: true, checkpointExists: true, sourceRun: '200' });
    assert.ok(!retry.calls.some(([program]) => program === 'python'));
    assert.deepEqual(retry.calls.at(-1), ['gh', ['run', 'download', '200', '--name', 'card-data-refresh-200', '--dir', retry.options.output]]);
});

test('fresh workflow runs can reuse earlier inputs and retain their own checkpoint for later retries', async t => {
    const replay = fixture(t, { checkpoints: { 100: 'ready' } });
    assert.deepEqual(await prepareRefreshSources({ ...replay.options, reuseRunId: '100' }),
        { restored: true, checkpointExists: false, sourceRun: '100' });
    assert.ok(!replay.calls.some(([program]) => program === 'python'));
    const retry = fixture(t, { checkpoints: { 100: 'expired', 200: 'ready' } });
    assert.deepEqual(await prepareRefreshSources({ ...retry.options, reuseRunId: '100', attempt: '2' }),
        { restored: true, checkpointExists: true, sourceRun: '200' });
    assert.ok(!retry.calls.some(([, args]) => args.some(arg => arg.includes('/100'))));
    const interruptedCopy = fixture(t, { checkpoints: { 100: 'ready' } });
    assert.equal((await prepareRefreshSources({ ...interruptedCopy.options, reuseRunId: '100', attempt: '2' })).sourceRun, '100');
});

test('missing automatic checkpoints permit acquisition, but explicit missing/expired replays fail without provider requests', async t => {
    const automatic = fixture(t);
    assert.equal((await prepareRefreshSources({ ...automatic.options, attempt: '2' })).restored, false);
    for (const state of [undefined, 'expired']) {
        const explicit = fixture(t, { checkpoints: { 100: state } });
        await assert.rejects(prepareRefreshSources({ ...explicit.options, reuseRunId: '100' }), /missing|expired/);
        assert.ok(!explicit.calls.some(([program]) => program === 'python'));
    }
    const expired = fixture(t, { checkpoints: { 200: 'expired' } });
    await assert.rejects(prepareRefreshSources({ ...expired.options, attempt: '2' }), /expired; start a new run/);
    assert.ok(!expired.calls.some(([program]) => program === 'python'));
});

test('corrupt, incomplete, unrelated or unavailable artifacts never silently trigger fresh API requests', async t => {
    for (const settings of [{ corrupt: true }, { incomplete: true }, { wrongWorkflow: true }, { apiFailure: true }]) {
        const replay = fixture(t, { checkpoints: { 200: 'ready' }, ...settings });
        await assert.rejects(prepareRefreshSources({ ...replay.options, attempt: '2' }),
            /hash mismatch|ENOENT|must belong|API unavailable/);
        assert.ok(!replay.calls.some(([program]) => program === 'python'));
    }
});

test('run IDs are validated before commands and newly acquired sources also require a valid manifest', async t => {
    const invalid = fixture(t);
    for (const reuseRunId of ['../100', '100; anything', '-1', '0']) {
        await assert.rejects(prepareRefreshSources({ ...invalid.options, reuseRunId }), /Invalid source checkpoint/);
    }
    assert.deepEqual(invalid.calls, []);
    const badAcquisition = fixture(t, { corrupt: true });
    await assert.rejects(prepareRefreshSources(badAcquisition.options), /hash mismatch/);
});
