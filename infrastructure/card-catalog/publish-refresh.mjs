// Keep publication in the checked-out revision, alongside the generator and locale registry.
import { execFileSync } from 'node:child_process';
import { readFile, access } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { refreshPaths } from './refresh-card-data.mjs';

const command = (program, args) => execFileSync(program, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] }).trim();

export function stageRefreshBundle(locales, run = command) {
    if (run('git', ['diff', '--cached', '--name-only'])) throw new Error('Unexpected staged files before refresh publication');
    const paths = refreshPaths(locales);
    run('git', ['add', '--', ...paths]);
    // Tests run against the working tree: require the complete tested bundle in the index.
    run('git', ['diff', '--exit-code', '--', ...paths]);
    return Boolean(run('git', ['diff', '--cached', '--name-only']));
}

export function ensureRefreshCi(sha, branch, run = command) {
    // GITHUB_TOKEN pushes suppress push CI. Explicit dispatch is supported by that token.
    // On retry, retain any existing push/manual run for this exact head, even a failed run.
    const response = JSON.parse(run('gh', ['api', '--method', 'GET',
        'repos/{owner}/{repo}/actions/workflows/tests.yml/runs', '-f', `head_sha=${sha}`, '-f', `branch=${branch}`]));
    if (!Array.isArray(response.workflow_runs)) throw new Error('Invalid CI run response');
    if (response.workflow_runs.some(run => run.head_sha === sha && run.head_branch === branch
        && ['push', 'workflow_dispatch'].includes(run.event))) return false;
    run('gh', ['workflow', 'run', 'tests.yml', '--ref', branch]);
    return true;
}

export async function publishRefresh({ branch, expectedSha, sourceDir, locales, run = command }) {
    if (branch !== 'codex/card-data-refresh' || expectedSha === undefined || !sourceDir) throw new Error('Invalid refresh publication context');
    if (run('git', ['branch', '--show-current']) !== branch) throw new Error('Not on the refresh branch');
    const changed = stageRefreshBundle(locales, run);
    if (changed) {
        run('node', ['scripts/codex/privacy-guard.mjs', 'check']);
        run('git', ['diff', '--cached', '--check']);
        run('git', ['commit', '-m', 'Refresh canonical cards, localized names and archetype audit']);
    }
    // Also resume after a successful push followed by a failed PR creation/CI dispatch.
    run('git', ['fetch', 'origin', 'dev']);
    run('git', ['merge-base', '--is-ancestor', 'origin/dev', 'HEAD']);
    if (run('git', ['rev-list', '--count', 'origin/dev..HEAD']) === '0') return;
    const sha = run('git', ['rev-parse', 'HEAD']);
    if (sha !== expectedSha) {
        run('git', ['push', `--force-with-lease=refs/heads/${branch}:${expectedSha}`, 'origin', `HEAD:refs/heads/${branch}`]);
    }
    const remoteSha = run('git', ['ls-remote', '--heads', 'origin', `refs/heads/${branch}`]).split(/\s/)[0];
    if (remoteSha !== sha) throw new Error('Published refresh head changed unexpectedly');
    const pr = run('gh', ['pr', 'list', '--head', branch, '--base', 'dev', '--state', 'open', '--json', 'number', '--jq', '.[0].number // empty']);
    const body = resolve(sourceDir, 'pr-body.md');
    let bodyArgs = [];
    try { await access(body); bodyArgs = ['--body-file', body]; }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
    if (pr) {
        if (changed) run('gh', ['pr', 'edit', pr, '--title', 'Refresh card catalog and localized names', ...bodyArgs]);
    } else {
        run('gh', ['pr', 'create', '--head', branch, '--base', 'dev', '--title', 'Refresh card catalog and localized names',
            ...(bodyArgs.length ? bodyArgs : ['--body', 'Review infrastructure/card-catalog/snapshots/refresh/audit.json for the retained refresh evidence.'])]);
    }
    ensureRefreshCi(sha, branch, run);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
    const registry = JSON.parse(await readFile('YGOProbabilityCalculatorBlazor/wwwroot/localization/locales.json', 'utf8'));
    await publishRefresh({ branch: process.env.REFRESH_BRANCH, expectedSha: process.env.EXPECTED_SHA,
        sourceDir: process.env.SOURCE_DIR, locales: registry.filter(locale => locale.id !== 'en').map(locale => locale.id) });
}
