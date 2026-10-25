// Opt-in native process harness. Args: Release-built test project/solution,
// source revision, output JSON. DOTNET selects the SDK executable; optional
// YGO_UNIFIED_TIMEOUT_SECONDS limits the measurement process, never engine Work.
// Each fixture gets its own process. Timed-out requests are explicitly censored;
// counters unavailable after termination remain absent, not zero.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { spawn } = require('node:child_process');

async function main() {
    const [project, revision, destination] = process.argv.slice(2);
    assert(project && revision && destination, 'Provide test project/solution, source revision and output JSON.');
    const output = path.resolve(destination);
    const directory = output + '.measurements';
    await fs.mkdir(directory, { recursive: true });
    const seconds = Number(process.env.YGO_UNIFIED_TIMEOUT_SECONDS || 120);
    assert(Number.isFinite(seconds) && seconds > 0, 'Measurement timeout must be positive.');
    const catalogFile = path.join(directory, 'catalog.json');
    const catalogRun = await run({ YGO_UNIFIED_BENCHMARK: catalogFile, YGO_UNIFIED_EXPORT_ONLY: '1' }, 'catalog');
    assert.equal(catalogRun.ExitCode, 0, 'Fixture export failed; inspect ' + catalogRun.Log);
    const catalog = JSON.parse(await fs.readFile(catalogFile, 'utf8'));
    const results = [];
    for (const fixture of catalog) {
        assert(/^[A-Za-z0-9-]+$/.test(fixture.Name), 'Fixture names must be safe file names.');
        const measuredFile = path.join(directory, fixture.Name + '.json');
        console.log('Measuring ' + fixture.Name);
        const execution = await run({ YGO_UNIFIED_BENCHMARK: measuredFile,
            YGO_UNIFIED_EXPORT_ONLY: '0', YGO_UNIFIED_CASE_EXACT: fixture.Name }, fixture.Name);
        let result = fixture;
        try {
            const checkpoint = JSON.parse(await fs.readFile(measuredFile, 'utf8'));
            assert.equal(checkpoint.length, 1);
            assert.equal(checkpoint[0].Name, fixture.Name);
            result = checkpoint[0];
        } catch (error) {
            if (!execution.Censored) throw error;
        }
        result.Harness = execution;
        if (execution.Censored && result.First.Outcome === 'Pending') {
            result.First = { Outcome: 'Censored', Detail: 'External measurement process timeout; engine counters unavailable.' };
            result.Samples = [];
        }
        results.push(result);
        await fs.writeFile(output, JSON.stringify(results));
        console.log(fixture.Name + ': ' + (execution.Censored ? 'censored' : result.First.Outcome));
        if (!execution.Censored) {
            assert.equal(execution.ExitCode, 0, 'Native test failed; inspect ' + execution.Log);
            assert.notEqual(result.First.Outcome, 'Pending', 'Native test did not complete the selected fixture.');
        }
    }

    async function run(environment, name) {
        const log = path.join(directory, name + '.log');
        const chunks = [];
        const started = performance.now();
        const processGroup = process.platform !== 'win32';
        const child = spawn(process.env.DOTNET || 'dotnet', ['test', path.resolve(project), '--no-build',
            '--configuration', 'Release', '--filter', 'FullyQualifiedName~UnifiedActivationBenchmark.Measure',
            '--logger', 'console;verbosity=minimal'], {
            detached: processGroup,
            env: { ...process.env, DOTNET_TieredCompilation: '0', YGO_UNIFIED_REVISION: revision, ...environment },
            stdio: ['ignore', 'pipe', 'pipe']
        });
        child.stdout.on('data', chunk => chunks.push(chunk));
        child.stderr.on('data', chunk => chunks.push(chunk));
        let censored = false;
        const timer = setTimeout(() => {
            censored = true;
            // Terminate only this harness-owned process tree, including testhost.
            try { processGroup ? process.kill(-child.pid, 'SIGKILL') : child.kill('SIGKILL'); }
            catch (error) { if (error.code !== 'ESRCH') throw error; }
        }, seconds * 1000);
        try {
            const exitCode = await new Promise((resolve, reject) => {
                child.once('error', reject);
                child.once('close', resolve);
            });
            await fs.writeFile(log, Buffer.concat(chunks));
            return { ExitCode: exitCode, Censored: censored, ProcessMilliseconds: performance.now() - started,
                TimeoutSeconds: seconds, Log: log };
        } finally { clearTimeout(timer); }
    }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
