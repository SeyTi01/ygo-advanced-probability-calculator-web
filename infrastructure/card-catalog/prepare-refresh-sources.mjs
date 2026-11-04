// Reuse a run's immutable input checkpoint before contacting upstream providers.
import { execFileSync } from 'node:child_process';
import { appendFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { assertCurrentRefreshWorkflow, readRefreshSources } from './refresh-card-data.mjs';

const command = (program, args) => execFileSync(program, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] }).trim();
const validRun = value => /^[1-9]\d*$/.test(value);

export async function prepareRefreshSources({ runId, attempt, reuseRunId = '', output, run = command }) {
    if (!validRun(runId) || !validRun(attempt) || (reuseRunId && !validRun(reuseRunId)) || !output) {
        throw new Error('Invalid source checkpoint context');
    }
    // A rerun keeps its run ID. Prefer its own checkpoint, including when the
    // first attempt copied inputs from another run whose artifact may later expire.
    const candidates = [...new Set([...(Number(attempt) > 1 ? [runId] : []), ...(reuseRunId ? [reuseRunId] : [])])];
    for (const sourceRun of candidates) {
        const name = `card-data-refresh-${sourceRun}`;
        const response = JSON.parse(run('gh', ['api', '--method', 'GET',
            `repos/{owner}/{repo}/actions/runs/${sourceRun}/artifacts`, '-f', `name=${name}`]));
        if (!Array.isArray(response.artifacts)) throw new Error('Invalid source artifact response');
        const matches = response.artifacts.filter(artifact => artifact.name === name);
        if (matches.length > 1) throw new Error('Ambiguous source checkpoint');
        if (matches[0]?.expired) throw new Error(`Source checkpoint for run ${sourceRun} is expired; start a new run for fresh inputs`);
        if (!matches.length) {
            if (sourceRun === reuseRunId) throw new Error(`Source checkpoint for run ${sourceRun} is missing or expired`);
            continue;
        }
        const source = JSON.parse(run('gh', ['api', `repos/{owner}/{repo}/actions/runs/${sourceRun}`]));
        if (source.path !== '.github/workflows/card-data-refresh.yml' || source.head_branch !== 'dev') {
            throw new Error('Source checkpoint must belong to a card-data refresh on dev');
        }
        run('gh', ['run', 'download', sourceRun, '--name', name, '--dir', output]);
        // Never silently replace corrupt/incomplete evidence with fresh API data.
        await readRefreshSources(output);
        return { restored: true, checkpointExists: sourceRun === runId, sourceRun };
    }
    run('python', ['infrastructure/card-catalog/fetch-refresh.py', '--output', output]);
    await readRefreshSources(output);
    return { restored: false, checkpointExists: false, sourceRun: runId };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
    await assertCurrentRefreshWorkflow();
    const result = await prepareRefreshSources({ runId: process.env.GITHUB_RUN_ID, attempt: process.env.GITHUB_RUN_ATTEMPT,
        reuseRunId: process.env.REUSE_RUN_ID || '', output: process.env.SOURCE_DIR });
    await appendFile(process.env.GITHUB_OUTPUT, `checkpoint-exists=${result.checkpointExists}\n`);
    console.log(result.restored ? `Reusing verified source checkpoint from run ${result.sourceRun}` : 'Acquired and verified new source inputs');
}
