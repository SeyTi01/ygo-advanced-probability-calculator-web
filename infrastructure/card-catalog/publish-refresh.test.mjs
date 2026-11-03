import assert from 'node:assert/strict';
import test from 'node:test';
import { execFileSync } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { stageRefreshBundle, ensureRefreshCi, publishRefresh } from './publish-refresh.mjs';
import { refreshPaths, dataRoot } from './refresh-card-data.mjs';

const branch = 'codex/card-data-refresh';
const sha = 'a'.repeat(40);

test('publication stages every locale asset and projection, including future registered locales', async () => {
    const root = await mkdtemp(join(tmpdir(), 'refresh-index-'));
    const env = { ...process.env, GIT_CONFIG_NOSYSTEM: '1', GIT_CONFIG_GLOBAL: process.platform === 'win32' ? 'NUL' : '/dev/null' };
    for (const key of Object.keys(env)) {
        if (/^GIT_(AUTHOR|COMMITTER|CONFIG_COUNT|CONFIG_KEY|CONFIG_VALUE|DIR$|WORK_TREE$|INDEX_FILE$)/.test(key)) delete env[key];
    }
    const run = (program, args) => execFileSync(program, args, { cwd: root, env, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
    try {
        run('git', ['init']);
        run('git', ['config', 'user.name', 'Refresh Test']);
        run('git', ['config', 'user.email', 'refresh@example.invalid']);
        const locales = ['de', 'ja', 'fr', 'it', 'pt', 'es'];
        for (const path of refreshPaths(locales)) {
            await mkdir(dirname(join(root, path)), { recursive: true });
            await writeFile(join(root, path), 'baseline\n');
        }
        run('git', ['add', '.']);
        run('git', ['commit', '-m', 'Fixture']);
        for (const path of refreshPaths(locales)) await writeFile(join(root, path), 'refreshed\n');
        await writeFile(join(root, 'policy.json'), 'must not publish');
        assert.equal(stageRefreshBundle(locales, run), true);
        assert.deepEqual(run('git', ['diff', '--cached', '--name-only']).split('\n').sort(), refreshPaths(locales).sort());
        run('git', ['commit', '-m', 'Complete bundle']);
        for (const locale of locales) assert.equal(run('git', ['show', `HEAD:${dataRoot}/card-names.${locale}.v1.json`]), 'refreshed');
        assert.equal(stageRefreshBundle(locales, run), false);
        run('git', ['add', 'policy.json']);
        assert.throws(() => stageRefreshBundle(locales, run), /Unexpected staged/);
    } finally { await rm(root, { recursive: true, force: true }); }
});

test('CI dispatch uses the existing workflow exactly once per head and preserves completed failures', () => {
    for (const runs of [[], [{ head_sha: 'b'.repeat(40), head_branch: branch, event: 'push' }]]) {
        const calls = [];
        const run = (program, args) => {
            calls.push([program, args]);
            return args[0] === 'api' ? JSON.stringify({ workflow_runs: runs }) : '';
        };
        assert.equal(ensureRefreshCi(sha, branch, run), true);
        assert.deepEqual(calls.at(-1), ['gh', ['workflow', 'run', 'tests.yml', '--ref', branch]]);
        assert.ok(calls[0][1].includes(`head_sha=${sha}`));
    }
    for (const event of ['push', 'workflow_dispatch']) {
        for (const status of ['queued', 'in_progress', 'completed']) {
            let calls = 0;
            assert.equal(ensureRefreshCi(sha, branch, () => {
                calls++;
                return JSON.stringify({ workflow_runs: [{ head_sha: sha, head_branch: branch, event, status, conclusion: 'failure' }] });
            }), false);
            assert.equal(calls, 1);
        }
    }
    assert.throws(() => ensureRefreshCi(sha, branch, () => { throw new Error('API unavailable'); }), /API unavailable/);
    assert.throws(() => ensureRefreshCi(sha, branch, () => '{}'), /Invalid CI/);
});

function publicationFixture({ expectedSha = sha, remoteSha = sha, fail, runs = [], hasPr = true, ahead = true } = {}) {
    const calls = [];
    const run = (program, args) => {
        calls.push([program, args]);
        if (fail?.(program, args)) throw new Error('Publication stopped');
        if (program === 'git') {
            if (args[0] === 'branch') return branch;
            if (args[0] === 'rev-list') return ahead ? '1' : '0';
            if (args[0] === 'rev-parse') return sha;
            if (args[0] === 'ls-remote') return `${remoteSha}\trefs/heads/${branch}`;
        }
        if (program === 'gh') {
            if (args[0] === 'api') return JSON.stringify({ workflow_runs: runs });
            if (args[0] === 'pr' && args[1] === 'list') return hasPr ? '350' : '';
        }
        return '';
    };
    return { calls, options: { branch, expectedSha, sourceDir: tmpdir(), locales: ['de', 'ja', 'fr', 'it', 'pt'], run } };
}

test('unchanged generation recovers PR creation/CI dispatch after an interrupted publication', async () => {
    for (const hasPr of [true, false]) {
        const { calls, options } = publicationFixture({ hasPr });
        await publishRefresh(options);
        assert.ok(calls.some(([program, args]) => program === 'gh' && args[0] === 'workflow'));
        assert.equal(calls.some(([program, args]) => program === 'gh' && args[1] === 'create'), !hasPr);
        assert.ok(!calls.some(([program, args]) => program === 'git' && ['push', 'commit'].includes(args[0])));
    }
    const { calls, options } = publicationFixture({ ahead: false, expectedSha: '' });
    await publishRefresh(options);
    assert.ok(!calls.some(([program]) => program === 'gh'));
});

test('rebased no-op publishes with an exact lease, while changed base/remote/lease stop before CI', async () => {
    const expectedSha = 'b'.repeat(40);
    const fixture = publicationFixture({ expectedSha });
    await publishRefresh(fixture.options);
    assert.ok(fixture.calls.some(([program, args]) => program === 'git' && args[0] === 'push'
        && args.includes(`--force-with-lease=refs/heads/${branch}:${expectedSha}`)));
    for (const settings of [
        { fail: (program, args) => program === 'git' && args[0] === 'merge-base' },
        { expectedSha, fail: (program, args) => program === 'git' && args[0] === 'push' },
        { remoteSha: expectedSha }
    ]) {
        const { calls, options } = publicationFixture(settings);
        await assert.rejects(publishRefresh(options), /Publication stopped|head changed/);
        assert.ok(!calls.some(([program]) => program === 'gh'));
    }
});
