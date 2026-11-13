// Opt-in frozen Progressive replay through the production owner and staged lease.
// node measure-storage-progressive.cjs <wwwroot> <fixtures.json> <output.json> [warm=3] [cutoff-ms=60000]
// Measures worker delivery, not Blazor rendering, cold browser startup or network loading.
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const { serve, launchOptions } = require('./browser-support.cjs');
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');

function compare(actual, expected, location) {
    if (typeof expected === 'number' && !Number.isInteger(expected)) {
        assert(Number.isFinite(actual) && Math.abs(actual - expected) <=
            4 * Number.EPSILON * Math.max(Math.abs(expected), Number.MIN_VALUE), location);
    } else if (expected === null || typeof expected !== 'object') {
        assert.equal(actual, expected, location);
    } else {
        assert(actual !== null && typeof actual === 'object', location);
        assert.equal(Array.isArray(actual), Array.isArray(expected), location);
        assert.deepEqual(Object.keys(actual).sort(), Object.keys(expected).sort(), location);
        for (const key of Object.keys(expected)) compare(actual[key], expected[key], `${location}.${key}`);
    }
}

function verify(sample, fast, exact) {
    compare(sample.First, fast.ReferenceResponse, `${fast.Name}: first`);
    if (!sample.IsPreliminary) {
        compare(sample.Final, sample.First, `${fast.Name}: immediate terminal`);
        return;
    }
    assert.equal(sample.First.Result.Contract.RequestedMode, 0);
    assert.equal(sample.First.Result.Contract.Completion, 0);
    assert([1, 2].includes(sample.First.Result.Contract.Accuracy));
    const contract = sample.Final.Result?.Contract;
    assert(contract, 'Progressive must retain a completed first result on upgrade failure');
    assert.equal(contract.RequestedMode, 0);
    assert.equal(contract.ExecutedMode, 0);
    assert.equal(contract.Completion, 0);
    if (exact.ReferenceResponse.Result) {
        assert.equal(contract.Accuracy, 0);
        assert.equal(contract.AutomaticExact, 0);
        const reference = exact.ReferenceResponse.Result;
        // The Exact call uses a different requested-mode/proof contract. Compare
        // every probability row, its identity and the exact physical fractions.
        for (const key of ['TotalProbability', 'ComboProbabilities', 'GroupProbabilities']) {
            compare(sample.Final.Result[key], reference[key], `${fast.Name}: ${key}`);
        }
        compare(contract.Fractions, reference.Contract.Fractions, `${fast.Name}: fractions`);
        assert.equal(contract.Scope, reference.Contract.Scope);
        assert.equal(sample.Final.Error, null);
        assert.equal(sample.Final.LimitReason, null);
        assert.equal(sample.Final.FailureKind, null);
    } else {
        const failure = exact.ReferenceResponse;
        const expected = failure.LimitReason === 1 ? 2 : failure.LimitReason === 2 ? 3
            : failure.FailureKind === 3 ? 4 : 5;
        assert.equal(contract.AutomaticExact, expected, 'upgrade failure kind');
        const retained = structuredClone(sample.Final);
        delete retained.Result.Contract.AutomaticExact;
        compare(retained, sample.First, `${fast.Name}: preserved first result`);
    }
}

async function manifest(root) {
    const files = ['js/background-calculation.js', 'js/calculation-worker.js'];
    async function visit(directory) {
        for (const entry of await fs.readdir(path.join(root, directory), { withFileTypes: true })) {
            const file = `${directory}/${entry.name}`;
            if (entry.isDirectory()) await visit(file);
            else if (/\.(js|mjs|json|wasm)$/.test(entry.name)) files.push(file);
        }
    }
    await visit('_framework');
    return Promise.all(files.sort().map(async file => {
        const bytes = await fs.readFile(path.join(root, file));
        return { Path: file, Bytes: bytes.length, Sha256: sha(bytes) };
    }));
}

async function main() {
    const [rootArg, inputArg, outputArg, warmArg = '3', cutoffArg = '60000'] = process.argv.slice(2);
    assert(rootArg && inputArg && outputArg && process.argv.length <= 7,
        'Usage: measure-storage-progressive.cjs <wwwroot> <fixtures.json> <output.json> [warm=3] [cutoff-ms=60000]');
    const warm = Number(warmArg), cutoff = Number(cutoffArg);
    assert(Number.isInteger(warm) && warm >= 0 && warm < 20, 'Warm samples must be 0..19');
    assert(Number.isInteger(cutoff) && cutoff >= 1000 && cutoff <= 600000, 'Cutoff must be 1000..600000 ms');
    const root = path.resolve(rootArg), input = path.resolve(inputArg), output = path.resolve(outputArg);
    assert.notEqual(input.toLowerCase(), output.toLowerCase(), 'Output must not overwrite fixtures');
    const inputStat = await fs.stat(input), outputStat = await fs.stat(output).catch(error => {
        if (error.code !== 'ENOENT') throw error;
        return null;
    });
    assert(!outputStat || inputStat.dev !== outputStat.dev || inputStat.ino !== outputStat.ino,
        'Output must not alias fixtures');
    const bytes = await fs.readFile(input), fixtures = JSON.parse(bytes.toString('utf8'));
    assert(Array.isArray(fixtures) && fixtures.length, 'Expected frozen fixture array');
    const byName = new Map(fixtures.map(fixture => [fixture.Name, fixture]));
    assert.equal(byName.size, fixtures.length, 'Duplicate fixture names');
    const pairs = fixtures.filter(fixture => fixture.Name.endsWith('/Fast') &&
        (!process.env.YGO_STORAGE_CASE || fixture.Name.includes(process.env.YGO_STORAGE_CASE))).map(fast => {
        const exact = byName.get(fast.Name.replace(/\/Fast$/, '/Exact'));
        assert(exact, `${fast.Name}: missing Exact reference`);
        assert.equal(fast.Execution ?? null, null);
        assert.equal(exact.Execution, 'interactive-exact-v1');
        const fastInput = JSON.parse(fast.Json), exactInput = JSON.parse(exact.Json);
        assert.equal(fastInput.Options.Mode, 0);
        assert.equal(exactInput.Options.Mode, 2);
        fastInput.Options.Mode = 2;
        assert.deepEqual(fastInput, exactInput, 'Paired inputs may differ only in mode');
        assert(fast.ReferenceResponse && exact.ReferenceResponse, 'Missing frozen responses');
        return { fast, exact };
    });
    assert(pairs.length, 'No paired Fast fixtures selected');
    const assets = await manifest(root);
    const report = { SchemaVersion: 1, FixtureSha256: sha(bytes), ArtifactManifest: assets,
        ArtifactManifestSha256: sha(JSON.stringify(assets)), WarmSamples: warm, CutoffMilliseconds: cutoff,
        Metrics: 'Fresh worker first request, then same-worker repeats. Bootstrap measured separately; no UI paint or cold network claim.',
        Measurements: [], Errors: [] };
    await fs.mkdir(path.dirname(output), { recursive: true });
    const save = () => fs.writeFile(output, JSON.stringify(report, null, 2) + '\n');
    await save();
    let server, browser;
    try {
        server = await serve(root);
        browser = await chromium.launch(launchOptions());
        report.Browser = browser.version();
        for (const { fast, exact } of pairs) {
            const result = { Name: fast.Name.replace(/\/Fast$/, '/Progressive'),
                FastJsonSha256: sha(fast.Json), ExactJsonSha256: sha(exact.Json), Samples: [], Passed: false };
            report.Measurements.push(result);
            const context = await browser.newContext({ serviceWorkers: 'block', locale: 'en-US' });
            try {
                const page = await context.newPage();
                await page.route(`${server.url}/`, route => route.fulfill({ contentType: 'text/html',
                    body: '<!doctype html><title>Progressive measurement</title>' }));
                await page.goto(server.url);
                await page.evaluate(async () => {
                    window.observedWorkers = [];
                    const NativeWorker = Worker;
                    window.Worker = class extends NativeWorker {
                        constructor(...args) {
                            super(...args);
                            const entry = { Started: performance.now() };
                            entry.Ready = new Promise((resolve, reject) => {
                                const timer = setTimeout(() => reject(Error('Bootstrap cutoff')), 31000);
                                this.addEventListener('message', ({ data }) => {
                                    if (data.ready) {
                                        entry.BootstrapMilliseconds = performance.now() - entry.Started;
                                        clearTimeout(timer);
                                        resolve();
                                    }
                                });
                                this.addEventListener('error', () => { clearTimeout(timer); reject(Error('Worker error')); });
                            });
                            observedWorkers.push(entry);
                        }
                    };
                    window.owner = (await import('/js/background-calculation.js')).createOwner();
                });
                for (let index = 0; index <= warm; index++) {
                    const sample = await page.evaluate(async ({ json, cutoff, index }) => {
                        const workersBefore = observedWorkers.length;
                        const job = owner.createJob();
                        const worker = observedWorkers.at(-1);
                        await worker.Ready;
                        const start = performance.now();
                        let timer, first;
                        try {
                            const timed = new Promise(resolve => {
                                timer = setTimeout(() => {
                                    resolve({ Outcome: 'Timeout', Milliseconds: performance.now() - start,
                                        First: first?.Response, IsPreliminary: first?.IsPreliminary });
                                    job.dispose();
                                }, cutoff);
                            });
                            const run = (async () => {
                                const stage = await job.runProgressive(json, true);
                                first = { Response: JSON.parse(stage.response), IsPreliminary: stage.isPreliminary,
                                    Milliseconds: performance.now() - start };
                                const final = JSON.parse(await job.finish());
                                return { Outcome: 'Response', First: first.Response, Final: final,
                                    IsPreliminary: first.IsPreliminary, FirstMilliseconds: first.Milliseconds,
                                    FinalMilliseconds: performance.now() - start };
                            })();
                            const value = await Promise.race([timed, run]);
                            clearTimeout(timer);
                            // Root validates replies before acknowledging this lease.
                            window.measurementJob = job;
                            return { Index: index, Phase: index === 0 ? 'first' : 'warm',
                                WorkersCreated: observedWorkers.length - workersBefore,
                                BootstrapMilliseconds: index === 0 ? worker.BootstrapMilliseconds : null,
                                ...value };
                        } catch (error) {
                            clearTimeout(timer);
                            job.dispose();
                            return { Index: index, Outcome: 'TransportError', Error: String(error), First: first?.Response };
                        }
                    }, { json: fast.Json, cutoff, index });
                    result.Samples.push(sample);
                    if (sample.Outcome === 'Response') {
                        try {
                            verify(sample, fast, exact);
                            assert.equal(sample.WorkersCreated, index === 0 ? 1 : 0, 'Expected one reused worker');
                            sample.Parity = true;
                        } catch (error) { sample.Parity = false; sample.ParityError = error.message; }
                    }
                    await page.evaluate(accepted => {
                        if (accepted) measurementJob.release();
                        else window.measurementJob?.dispose();
                    }, sample.Parity === true && !!sample.Final?.Result);
                    await save();
                    if (!sample.Parity || !sample.Final?.Result || sample.Final.Result.Contract.AutomaticExact > 0) break;
                }
                result.Passed = result.Samples.length > 0 && result.Samples.every(sample => sample.Parity === true);
            } catch (error) { result.Error = String(error.stack ?? error); }
            finally { await context.close(); await save(); }
        }
        assert.deepEqual(await manifest(root), assets, 'Published assets changed during replay');
    } catch (error) { report.Errors.push(String(error.stack ?? error)); }
    finally {
        if (browser) await browser.close();
        if (server) await new Promise(resolve => server.server.close(resolve));
        report.Passed = report.Errors.length === 0 && report.Measurements.length === pairs.length &&
            report.Measurements.every(result => result.Passed);
        await save();
    }
    assert(report.Passed, `Progressive replay failed; inspect ${output}`);
}

main().catch(error => { console.error(error); process.exitCode = 1; });
