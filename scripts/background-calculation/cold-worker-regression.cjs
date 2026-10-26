// Opt-in #295 correctness gate. Uses the unchanged production worker, with no warm-up.
// node cold-worker-regression.cjs <published-wwwroot> <frozen-input.json> <ignored-output.json> [cycles=4] [reference-response.json]
// Playwright stays external (NODE_PATH). This script does not publish or enable AOT.
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs/promises');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const playwright = require('playwright');
const { serve } = require('./browser-support.cjs');

const inputHash = '73449cb0ea76fca01d2a61959caab7df770001c61f4c3364171b8348b7f91177';
const expectedFractions = [
    '9055802594893/12015587984400',
    '792826210039/2002597997400', '6759243629/91547337024',
    '7798515821/35601742176', '8322813507/45643259200',
    '15051926251/516799483200', '4860954958/11920226175',
    '516048019/36410872680', '896130487/31787269800',
    '1332067910513/2288683425600', '837442724171/2002597997400'
];
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');

function readInput(document) {
    const captured = Array.isArray(document)
        ? document.find(entry => entry.Name === 'mixed-vision-custom-fast-40')?.Requests?.[0]
        : null;
    const input = captured?.Input ?? document.input ?? document;
    const json = captured?.canonicalJson ?? JSON.stringify(input);
    assert.equal(sha(json), inputHash, 'Use the original captured mixed worker input, not a regenerated export');
    assert.equal(sha(JSON.stringify(input)), inputHash, 'Captured object and canonical request must agree');
    assert.deepEqual(JSON.parse(json), input);
    return { input, json };
}

async function artifactManifest(root) {
    const files = ['js/calculation-worker.js'];
    async function visit(relative) {
        for (const entry of await fs.readdir(path.join(root, relative), { withFileTypes: true })) {
            const file = `${relative}/${entry.name}`;
            if (entry.isDirectory()) await visit(file);
            else if (/\.(?:js|mjs|json|wasm)$/.test(entry.name)) files.push(file);
        }
    }
    await visit('_framework');
    const manifest = [];
    for (const file of files.sort()) {
        const bytes = await fs.readFile(path.join(root, file));
        manifest.push({ path: file, bytes: bytes.length, sha256: sha(bytes) });
    }
    return manifest;
}

function validate(response, input, reference) {
    const failures = [];
    const check = (name, action) => {
        try { action(); } catch (error) { failures.push({ check: name, message: error.message }); }
    };
    check('successful response', () => {
        assert(response?.Result);
        for (const field of ['Error', 'LimitReason', 'FailureKind', 'Contract']) assert.equal(response[field], null);
    });
    const result = response?.Result;
    const contract = result?.Contract;
    const metadata = { Version: 1, RequestedMode: 0, ExecutedMode: 0, Scope: 1, Completion: 0, Accuracy: 0,
        Reference: 'Deck-search target survival under fixed modeled draw/removal policy (v1)',
        SolverRoute: 'OrderedDeferred', Proof: 'NoActiveSearch' };
    for (const [field, value] of Object.entries(metadata)) {
        check(`contract ${field}`, () => assert.equal(contract?.[field], value));
    }
    check('exact result has no interval or upgrade', () => {
        assert(result && result.SearchAvailability == null);
        assert(contract && contract.AutomaticExact == null);
    });
    const fractions = contract?.Fractions;
    check('fraction row counts', () => {
        assert.equal(fractions?.Combos?.length, 8);
        assert.equal(fractions?.Groups?.length, 2);
    });
    const rows = [fractions?.Total, ...(fractions?.Combos ?? []), ...(fractions?.Groups ?? [])];
    for (let index = 0; index < expectedFractions.length; index++) {
        check(`exact fraction ${index}`, () => {
            const actual = rows[index];
            assert.match(actual?.Numerator ?? '', /^\d+$/);
            assert.match(actual?.Denominator ?? '', /^[1-9]\d*$/);
            const [numerator, denominator] = expectedFractions[index].split('/').map(BigInt);
            assert.equal(BigInt(actual.Numerator) * denominator, numerator * BigInt(actual.Denominator));
        });
        check(`fraction encoding ${index}`, () => {
            assert.equal(`${rows[index]?.Numerator}/${rows[index]?.Denominator}`, expectedFractions[index]);
        });
    }
    check('combo identities', () => assert.deepEqual(result?.ComboProbabilities?.map(row =>
        [row.ComboIndex, row.ComboName, row.GroupId]), input.Combos.map((combo, index) => [index, combo.Name, combo.GroupId])));
    check('group identities', () => assert.deepEqual(result?.GroupProbabilities?.map(row =>
        [row.GroupId, row.GroupName, row.ActiveComboCount]), input.Groups.map(group =>
        [group.Id, group.Name, input.Combos.filter(combo => combo.GroupId === group.Id).length])));
    if (reference) check('complete reference response', () => assert.deepEqual(response, reference));
    return failures;
}

async function main() {
    const [published, fixtureFile, outputFile, cycleArgument = '4', referenceFile] = process.argv.slice(2);
    assert(published && fixtureFile && outputFile,
        'Arguments: <published-wwwroot> <frozen-input.json> <ignored-output.json> [cycles=4] [reference-response.json]');
    const cycles = Number(cycleArgument);
    assert(Number.isInteger(cycles) && cycles >= 4, 'Use at least four independent fresh workers');
    const root = path.resolve(published), fixture = path.resolve(fixtureFile), output = path.resolve(outputFile);
    assert.notEqual(output, fixture, 'Output must not overwrite the frozen evidence');
    if (referenceFile) assert.notEqual(output, path.resolve(referenceFile));
    const repository = path.resolve(__dirname, '../..');
    const ignored = spawnSync('git', ['-c', `safe.directory=${repository}`, 'check-ignore', '--quiet', '--', output],
        { cwd: repository, encoding: 'utf8' });
    assert.equal(ignored.status, 0, 'Output must be Git-ignored in this checkout; use bin/<name>.json');
    const fixtureBytes = await fs.readFile(fixture);
    const document = JSON.parse(fixtureBytes.toString('utf8'));
    const { input, json } = readInput(document);
    const referenceDocument = referenceFile ? JSON.parse(await fs.readFile(referenceFile, 'utf8')) : document.reference;
    const reference = referenceDocument?.reference ?? referenceDocument;
    if (reference) assert.deepEqual(validate(reference, input), [], 'Supplied reference fails the frozen contract');
    const manifest = await artifactManifest(root);
    const engine = process.env.YGO_BROWSER_ENGINE || 'chromium';
    assert(['chromium', 'firefox'].includes(engine), 'YGO_BROWSER_ENGINE must be chromium or firefox');
    const executablePath = process.env.YGO_BROWSER_EXECUTABLE;
    const launchOptions = { headless: true, ...(executablePath ? { executablePath }
        : engine === 'chromium' ? { channel: process.env.YGO_BROWSER_CHANNEL || 'msedge' } : {}) };
    const timeoutMs = 120000;
    const report = { schemaVersion: 1, fixtureSha256: sha(fixtureBytes), inputCanonicalSha256: inputHash,
        requestWireSha256: sha(json), artifactManifestSha256: sha(JSON.stringify(manifest)), manifest,
        engine, cycles, callsPerWorker: 3, fullReferenceComparison: Boolean(reference), runs: [], failures: [] };
    await fs.mkdir(path.dirname(output), { recursive: true });
    const save = () => fs.writeFile(output, JSON.stringify(report, null, 2) + '\n');
    await save();
    const server = await serve(root);
    let browser;
    try {
        browser = await playwright[engine].launch(launchOptions);
        report.browser = browser.version();
        for (let cycle = 0; cycle < cycles; cycle++) {
            const run = { cycle, samples: [], pageErrors: [], workerUrls: [] };
            report.runs.push(run);
            const context = await browser.newContext({ serviceWorkers: 'block' });
            try {
                const page = await context.newPage();
                page.on('pageerror', error => run.pageErrors.push(error.message));
                page.on('worker', worker => run.workerUrls.push(new URL(worker.url(), server.url).href));
                // Reproduce the original real-page bootstrap. The page runtime is separate
                // from this worker; bootstrap must not create a preliminary calculator worker.
                await page.goto(server.url);
                assert.equal(run.workerUrls.length, 0, 'Page bootstrap created a worker before the cold request');
                await page.evaluate(async timeout => {
                    window.worker = new Worker('/js/calculation-worker.js', { type: 'module' });
                    await new Promise((resolve, reject) => {
                        const timer = setTimeout(() => { worker.terminate(); reject(Error('Worker readiness timeout')); }, timeout);
                        worker.onmessage = ({ data }) => { clearTimeout(timer); data.ready ? resolve() : reject(Error(data.error || 'Unexpected readiness reply')); };
                        worker.onerror = error => { clearTimeout(timer); reject(Error(error.message)); };
                    });
                }, timeoutMs);
                assert.deepEqual(run.workerUrls, [`${server.url}/js/calculation-worker.js`], 'Only the cold test worker may exist');
                for (let call = 0; call < 3; call++) {
                    const reply = await page.evaluate(async ({ json, timeout }) => {
                        const id = crypto.randomUUID();
                        return await new Promise((resolve, reject) => {
                            const timer = setTimeout(() => { worker.terminate(); reject(Error('Calculation timeout')); }, timeout);
                            worker.onmessage = ({ data }) => {
                                clearTimeout(timer);
                                data.id === id ? resolve(data) : reject(Error('Reply ownership mismatch'));
                            };
                            worker.onerror = error => { clearTimeout(timer); reject(Error(error.message)); };
                            worker.postMessage({ id, json });
                        });
                    }, { json, timeout: timeoutMs });
                    const sample = { call, firstCalculation: call === 0, rawReply: reply, failures: [] };
                    run.samples.push(sample);
                    if (typeof reply.response === 'string') {
                        try { sample.response = JSON.parse(reply.response); sample.failures = validate(sample.response, input, reference); }
                        catch (error) { sample.failures.push({ check: 'response JSON', message: error.message }); }
                    } else sample.failures.push({ check: 'worker response', message: reply.error || 'Missing response' });
                    if (call > 0 && sample.response && run.samples[0].response) {
                        try { assert.deepEqual(sample.response, run.samples[0].response); }
                        catch (error) { sample.failures.push({ check: 'complete first/repeat identity', message: error.message }); }
                    }
                    console.log(`worker ${cycle + 1}, call ${call + 1}: ${sample.failures.length ? 'FAIL' : 'PASS'}`);
                    await save();
                }
                await page.evaluate(() => worker.terminate());
                assert.equal(run.workerUrls.length, 1, 'Page initiated an additional worker calculation');
            } catch (error) { run.error = String(error.stack ?? error); }
            finally { await context.close(); await save(); }
        }
        assert.deepEqual(await artifactManifest(root), manifest, 'Published assets changed during the run');
    } catch (error) { report.failures.push(String(error.stack ?? error)); }
    finally { if (browser) await browser.close(); await new Promise(resolve => server.server.close(resolve)); }
    report.passed = report.failures.length === 0 && report.runs.length === cycles && report.runs.every(run =>
        !run.error && run.pageErrors.length === 0 && run.samples.length === 3 && run.samples.every(sample => sample.failures.length === 0));
    await save();
    assert(report.passed, `Cold-worker regression failed; inspect ${output}`);
    console.log(`Passed ${cycles} fresh workers and ${cycles * 3} complete responses; evidence: ${output}`);
}

main().catch(error => { console.error(error); process.exitCode = 1; });
