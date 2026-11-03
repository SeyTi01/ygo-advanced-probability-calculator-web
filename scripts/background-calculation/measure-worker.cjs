// Frozen production-worker replay. Run alone, after publishing; Playwright is external.
// node measure-worker.cjs <wwwroot> <fixtures.json> <output.json> [warm-samples=3] [timeout-ms=60000]
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const { serve, launchOptions } = require('./browser-support.cjs');

const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');

function integer(value, fallback, min, max, label) {
    const number = value === undefined ? fallback : Number(value);
    assert(Number.isFinite(number) && Number.isInteger(number) && number >= min && number <= max,
        `${label} must be an integer between ${min} and ${max}`);
    return number;
}

function validateFixture(fixture, names) {
    assert(fixture && typeof fixture === 'object' && !Array.isArray(fixture), 'Fixture must be an object');
    assert(typeof fixture.Name === 'string' && fixture.Name.trim(), 'Fixture name must be nonempty');
    assert(!names.has(fixture.Name), `Duplicate fixture name: ${fixture.Name}`);
    names.add(fixture.Name);
    assert(typeof fixture.Json === 'string', `${fixture.Name}: Json must be a serialized input string`);
    const input = JSON.parse(fixture.Json);
    assert(input && typeof input === 'object' && !Array.isArray(input), `${fixture.Name}: invalid input object`);
    assert(fixture.Execution == null || fixture.Execution === 'interactive-exact-v1',
        `${fixture.Name}: unsupported execution`);
    if (fixture.Execution) assert.equal(input.Options?.Mode, 2, `${fixture.Name}: interactive Exact requires Exact mode`);
    const response = fixture.ReferenceResponse;
    assert(response && typeof response === 'object' && !Array.isArray(response), `${fixture.Name}: missing reference response`);
    for (const key of ['Result', 'Error', 'LimitReason', 'FailureKind', 'Contract']) {
        assert(Object.hasOwn(response, key), `${fixture.Name}: reference missing ${key}`);
    }
    assert((response.Result && typeof response.Result === 'object') ||
        (typeof response.Error === 'string' && response.Error.length > 0 && response.Contract && typeof response.Contract === 'object'),
        `${fixture.Name}: reference must contain a result or a typed failure`);
}

// Compare every property and array element. Derived floating-point values may
// differ by a few ULPs across native and WASM; integer fields remain exact.
function compare(actual, expected, location) {
    if (typeof expected === 'number') {
        assert.equal(typeof actual, 'number', location);
        assert(Number.isFinite(actual) && Number.isFinite(expected), location);
        if (Number.isInteger(expected)) assert.equal(actual, expected, location);
        else assert(Math.abs(actual - expected) <= 4 * Number.EPSILON * Math.max(Math.abs(expected), Number.MIN_VALUE),
            `${location}: ${actual} differs from ${expected}`);
    } else if (expected === null || typeof expected !== 'object') {
        assert.equal(actual, expected, location);
    } else {
        assert(actual !== null && typeof actual === 'object', location);
        assert.equal(Array.isArray(actual), Array.isArray(expected), location);
        if (Array.isArray(expected)) assert.equal(actual.length, expected.length, `${location} length`);
        assert.deepEqual(Object.keys(actual).sort(), Object.keys(expected).sort(), `${location} keys`);
        for (const key of Object.keys(expected)) compare(actual[key], expected[key], `${location}.${key}`);
    }
}

async function artifacts(root) {
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
        manifest.push({ Path: file, Bytes: bytes.length, Sha256: sha(bytes) });
    }
    return manifest;
}

async function main() {
    const [rootArg, fixtureArg, outputArg, warmArg, timeoutArg] = process.argv.slice(2);
    assert(rootArg && fixtureArg && outputArg && process.argv.length <= 7,
        'Usage: node measure-worker.cjs <wwwroot> <fixtures.json> <output.json> [warm-samples=3] [timeout-ms=60000]');
    const warmSamples = integer(warmArg, 3, 0, 30, 'Warm samples');
    const timeoutMs = integer(timeoutArg, 60000, 1000, 600000, 'Timeout');
    const root = path.resolve(rootArg), fixturePath = path.resolve(fixtureArg), outputPath = path.resolve(outputArg);
    const canonical = file => process.platform === 'win32' ? file.toLowerCase() : file;
    assert.notEqual(canonical(outputPath), canonical(fixturePath), 'Output must not overwrite frozen fixtures');
    const inputStat = await fs.stat(fixturePath);
    const outputStat = await fs.stat(outputPath).catch(error => {
        if (error.code === 'ENOENT') return null;
        throw error;
    });
    if (outputStat) assert(!(inputStat.dev === outputStat.dev && inputStat.ino === outputStat.ino),
        'Output must not refer to the frozen fixture file');
    const bytes = await fs.readFile(fixturePath);
    const fixtures = JSON.parse(bytes.toString('utf8'));
    assert(Array.isArray(fixtures) && fixtures.length > 0, 'Fixtures must be a nonempty array');
    const names = new Set();
    for (const fixture of fixtures) validateFixture(fixture, names);
    const manifest = await artifacts(root);
    const report = { SchemaVersion: 1, GeneratedAt: new Date().toISOString(), FixtureSha256: sha(bytes),
        ArtifactManifestSha256: sha(JSON.stringify(manifest)), ArtifactManifest: manifest,
        WarmSamples: warmSamples, TimeoutMilliseconds: timeoutMs, Locale: 'en-US', Browser: null, Measurements: [], Errors: [] };
    await fs.mkdir(path.dirname(outputPath), { recursive: true });
    const save = () => fs.writeFile(outputPath, JSON.stringify(report, null, 2) + '\n');
    await save();
    let server, browser;
    try {
        server = await serve(root);
        browser = await chromium.launch(launchOptions());
        report.Browser = browser.version();
        for (const fixture of fixtures) {
            const measurement = { Name: fixture.Name, JsonSha256: sha(fixture.Json), Execution: fixture.Execution ?? null,
                BootstrapMilliseconds: null, Samples: [], Passed: false };
            report.Measurements.push(measurement);
            let context;
            try {
                context = await browser.newContext({ serviceWorkers: 'block', locale: 'en-US' });
                const page = await context.newPage();
                await page.route(`${server.url}/`, route => route.fulfill({ contentType: 'text/html',
                    body: '<!doctype html><title>Worker measurement</title>' }));
                await page.goto(server.url);
                const bootstrap = await page.evaluate(async () => {
                    const started = performance.now();
                    window.measurementWorker = new Worker('/js/calculation-worker.js', { type: 'module' });
                    return await new Promise(resolve => {
                        const timer = setTimeout(() => resolve({ Outcome: 'BootstrapTimeout', Milliseconds: performance.now() - started }), 30000);
                        measurementWorker.onerror = event => { clearTimeout(timer); resolve({ Outcome: 'TransportError', Error: event.message, Milliseconds: performance.now() - started }); };
                        measurementWorker.onmessage = ({ data }) => { clearTimeout(timer); resolve({ Outcome: data.ready ? 'Ready' : 'TransportError', Error: data.error, Milliseconds: performance.now() - started }); };
                    });
                });
                measurement.BootstrapMilliseconds = bootstrap.Milliseconds;
                if (bootstrap.Outcome !== 'Ready') throw Error(`${bootstrap.Outcome}: ${bootstrap.Error ?? ''}`);
                await save();
                for (let index = 0; index <= warmSamples; index++) {
                    const sample = await page.evaluate(async ({ json, execution, timeout, index }) => {
                        const id = `measurement-${index}`;
                        const started = performance.now();
                        return await new Promise(resolve => {
                            const finish = value => resolve({ Index: index, Cold: index === 0, Milliseconds: performance.now() - started, ...value });
                            const timer = setTimeout(() => finish({ Outcome: 'Timeout' }), timeout);
                            measurementWorker.onerror = event => { clearTimeout(timer); finish({ Outcome: 'TransportError', Error: event.message }); };
                            measurementWorker.onmessage = ({ data }) => {
                                if (data.id !== id) { clearTimeout(timer); return finish({ Outcome: 'TransportError', Error: 'Reply ownership mismatch' }); }
                                clearTimeout(timer);
                                if (data.error) return finish({ Outcome: 'TransportError', Error: data.error });
                                try { finish({ Outcome: 'Response', Response: JSON.parse(data.response) }); }
                                catch (error) { finish({ Outcome: 'TransportError', Error: String(error) }); }
                            };
                            measurementWorker.postMessage({ id, json, ...(execution ? { execution } : {}) });
                        });
                    }, { json: fixture.Json, execution: fixture.Execution, timeout: timeoutMs, index });
                    measurement.Samples.push(sample);
                    if (sample.Outcome === 'Response') {
                        try { compare(sample.Response, fixture.ReferenceResponse, fixture.Name); sample.Parity = true; }
                        catch (error) { sample.Parity = false; sample.ParityError = error.message; }
                    }
                    await save();
                    // Timeouts are censored, not Work or Storage results. Failed
                    // responses and parity failures do not warm a later sample.
                    if (sample.Outcome !== 'Response' || !sample.Parity || !sample.Response.Result) break;
                }
                measurement.Passed = measurement.Samples.length > 0 && measurement.Samples.every(sample => sample.Parity === true)
                    && (measurement.Samples.length === warmSamples + 1 || !measurement.Samples[0].Response.Result);
            } catch (error) { measurement.Error = String(error.stack ?? error); }
            finally { if (context) await context.close(); await save(); }
        }
        assert.deepEqual(await artifacts(root), manifest, 'Published assets changed during measurement');
    } catch (error) { report.Errors.push(String(error.stack ?? error)); }
    finally {
        if (browser) await browser.close();
        if (server) await new Promise(resolve => server.server.close(resolve));
        report.Passed = report.Errors.length === 0 && report.Measurements.length === fixtures.length
            && report.Measurements.every(measurement => measurement.Passed);
        await save();
    }
    assert(report.Passed, `Worker replay failed; inspect ${outputPath}`);
}

main().catch(error => { console.error(error); process.exitCode = 1; });
