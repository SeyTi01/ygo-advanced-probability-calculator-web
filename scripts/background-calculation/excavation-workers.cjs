// Args: clean optimized published wwwroot, native excavation export, output.json.
// Run alone after publishing. Use external Playwright through NODE_PATH.
// YGO_EXCAVATION_CASE selects a name substring; YGO_EXCAVATION_MODE selects
// Fast or Exact; YGO_EXCAVATION_WARM_SAMPLES
// defaults to 3; YGO_EXCAVATION_TIMEOUT_MS defaults to 60000 per calculation.
// These wall times include the shipped worker/wire boundary. Bootstrap is
// separate, and each fixture's first calculation runs in a fresh worker.
// Publish flags/logs establish AOT configuration; this script does not infer it.
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');
const { launchOptions } = require('./browser-support.cjs');

function boundedInteger(value, fallback, minimum, maximum, name) {
    const result = value === undefined ? fallback : Number(value);
    assert(Number.isInteger(result) && result >= minimum && result <= maximum,
        `${name} must be an integer from ${minimum} to ${maximum}`);
    return result;
}

function compareValues(actual, expected, location) {
    if (typeof expected === 'number') {
        assert.equal(typeof actual, 'number', location);
        assert(Number.isFinite(actual), `${location} must be finite`);
        // Fraction strings and protocol integers are exact. Derived doubles may
        // round differently between native and WASM; allow only a few ULPs.
        if (Number.isInteger(expected)) assert.equal(actual, expected, location);
        else assert(Math.abs(actual - expected) <= 4 * Number.EPSILON * Math.max(Math.abs(expected), Number.MIN_VALUE),
            `${location}: ${actual} differs from native ${expected}`);
        return;
    }
    if (expected === null || typeof expected !== 'object') {
        assert.equal(actual, expected, location);
        return;
    }
    assert(actual !== null && typeof actual === 'object', location);
    assert.equal(Array.isArray(actual), Array.isArray(expected), location);
    assert.deepEqual(Object.keys(actual).sort(), Object.keys(expected).sort(), `${location} keys`);
    for (const key of Object.keys(expected)) compareValues(actual[key], expected[key], `${location}.${key}`);
}

function checkResponse(response, fixture) {
    const label = `${fixture.Name}/${fixture.Mode}`;
    if (fixture.ReferenceResponse) {
        compareValues(response, fixture.ReferenceResponse, label);
    } else if (fixture.ReferenceResults) {
        compareValues(response, { Result: fixture.ReferenceResults, Error: null, LimitReason: null,
            FailureKind: null, Contract: null }, label);
    }
    if (response.Result) {
        assert.equal(response.Error, null, `${label} success error`);
        assert.equal(response.LimitReason, null, `${label} success limit`);
        assert.equal(response.FailureKind, null, `${label} success failure kind`);
        const contract = response.Result.Contract;
        assert(contract, `${label} result contract`);
        assert.equal(contract.Completion, 0, `${label} completion`);
        assert.equal(contract.Accuracy, 0, `${label} exactness`);
        assert.equal(contract.RequestedMode, fixture.Input.Options?.Mode ?? 0, `${label} requested mode`);
        if (fixture.ReferenceResults) {
            assert.deepEqual(contract, fixture.ReferenceResults.Contract, `${label} full rational contract`);
        }
        const fractions = contract.Fractions;
        assert(fractions, `${label} rational values`);
        const values = [fractions.Total, ...fractions.Combos, ...fractions.Groups];
        const actual = values.map(value => {
            assert.match(value.Numerator, /^\d+$/, `${label} numerator`);
            assert.match(value.Denominator, /^[1-9]\d*$/, `${label} denominator`);
            assert(BigInt(value.Numerator) <= BigInt(value.Denominator), `${label} probability bound`);
            return `${value.Numerator}/${value.Denominator}`;
        });
        if (fixture.ExactResults) assert.deepEqual(actual, fixture.ExactResults, `${label} all exact rows`);
        assert(fixture.ReferenceResults || fixture.ReferenceResponse || fixture.ExactResults,
            `${label} requires native exact reference values`);
        return 'Success';
    }
    const expectedLimit = ['Work', 'WorkLimit'].includes(fixture.Outcome) ? 1
        : ['Storage', 'StorageLimit'].includes(fixture.Outcome) ? 2 : null;
    assert(expectedLimit !== null, `${label} unexpected unsuccessful response: ${response.Error}`);
    assert.equal(response.LimitReason, expectedLimit, `${label} typed limit`);
    assert.equal(response.FailureKind, null, `${label} limit is not an input/runtime error`);
    assert(response.Error, `${label} limit explanation`);
    assert(response.Contract, `${label} failure contract`);
    assert.equal(response.Contract.Completion, expectedLimit === 1 ? 2 : 3, `${label} failure completion`);
    assert.equal(response.Contract.Fractions, null, `${label} no fractions on failure`);
    assert.equal(response.Contract.Accuracy, null, `${label} no accuracy on failure`);
    return expectedLimit === 1 ? 'WorkLimit' : 'StorageLimit';
}

async function main() {
    assert(process.argv[2] && process.argv[3] && process.argv[4],
        'Usage: node excavation-workers.cjs <published-wwwroot> <native-export.json> <output.json>');
    const root = path.resolve(process.argv[2]);
    const inputText = await fs.readFile(process.argv[3], 'utf8');
    const allFixtures = JSON.parse(inputText);
    assert(Array.isArray(allFixtures), 'Native export must be an array');
    const fixtures = allFixtures.filter(fixture => (!process.env.YGO_EXCAVATION_CASE
        || fixture.Name.includes(process.env.YGO_EXCAVATION_CASE))
        && (!process.env.YGO_EXCAVATION_MODE || fixture.Mode === process.env.YGO_EXCAVATION_MODE));
    assert(fixtures.length > 0, 'No matching excavation fixtures');
    const warmSamples = boundedInteger(process.env.YGO_EXCAVATION_WARM_SAMPLES, 3, 0, 10, 'Warm samples');
    const timeoutMilliseconds = boundedInteger(process.env.YGO_EXCAVATION_TIMEOUT_MS, 60000, 1000, 600000, 'Timeout');
    const workerText = await fs.readFile(path.join(root, 'js', 'calculation-worker.js'));
    await fs.access(path.join(root, '_framework', 'dotnet.js'));
    const output = {
        GeneratedAt: new Date().toISOString(), Browser: null, WarmSamples: warmSamples,
        TimeoutMilliseconds: timeoutMilliseconds,
        InputSha256: crypto.createHash('sha256').update(inputText).digest('hex'),
        WorkerSha256: crypto.createHash('sha256').update(workerText).digest('hex'),
        Measurements: []
    };
    await fs.mkdir(path.dirname(path.resolve(process.argv[4])), { recursive: true });
    const save = () => fs.writeFile(process.argv[4], JSON.stringify(output, null, 2));
    const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.wasm': 'application/wasm',
        '.json': 'application/json', '.html': 'text/html' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            if (pathname === '/') {
                response.setHeader('Content-Type', 'text/html');
                return response.end('<!doctype html><title>Excavation worker measurements</title>');
            }
            const file = path.resolve(root, '.' + pathname);
            if (!file.startsWith(root + path.sep)) return response.writeHead(403).end();
            response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    let browser;
    let failures = 0;
    try {
        browser = await chromium.launch(launchOptions());
        output.Browser = browser.version();
        for (const fixture of fixtures) {
            const context = await browser.newContext({ locale: 'en-US' });
            const page = await context.newPage();
            try {
                await page.goto(`http://127.0.0.1:${server.address().port}/`);
                const measurement = await page.evaluate(async ({ input, name, warmSamples, timeoutMilliseconds }) => {
                    const started = performance.now();
                    const worker = new Worker('/js/calculation-worker.js', { type: 'module' });
                    const result = { Name: name, BootstrapMilliseconds: null, Samples: [] };
                    try {
                        const bootstrap = await new Promise(resolve => {
                            const timer = setTimeout(() => resolve({ Outcome: 'BootstrapTimeout' }), 30000);
                            worker.onerror = event => { clearTimeout(timer); resolve({ Outcome: 'TransportError', Error: event.message }); };
                            worker.onmessage = ({ data }) => {
                                clearTimeout(timer);
                                resolve(data.ready ? { Outcome: 'Ready' } : { Outcome: 'TransportError', Error: data.error || 'Unexpected startup message' });
                            };
                        });
                        result.BootstrapMilliseconds = performance.now() - started;
                        if (bootstrap.Outcome !== 'Ready') return { ...result, ...bootstrap };
                        for (let index = 0; index <= warmSamples; index++) {
                            const id = `excavation-${index}`;
                            const sample = await new Promise(resolve => {
                                const sent = performance.now();
                                const finish = value => resolve({ Index: index, Cold: index === 0,
                                    Milliseconds: performance.now() - sent, ...value });
                                const timer = setTimeout(() => finish({ Outcome: 'Timeout' }), timeoutMilliseconds);
                                worker.onerror = event => { clearTimeout(timer); finish({ Outcome: 'TransportError', Error: event.message }); };
                                worker.onmessage = ({ data }) => {
                                    if (data.id !== id) return;
                                    clearTimeout(timer);
                                    if (data.error) return finish({ Outcome: 'TransportError', Error: data.error });
                                    try { finish({ Outcome: 'Response', Response: JSON.parse(data.response) }); }
                                    catch (error) { finish({ Outcome: 'TransportError', Error: String(error) }); }
                                };
                                worker.postMessage({ id, json: JSON.stringify(input),
                                    ...(input.Options?.Mode === 2 ? { execution: 'interactive-exact-v1' } : {}) });
                            });
                            result.Samples.push(sample);
                            // Production retires failed workers, so a failure is
                            // never used as a warmup for a subsequent sample.
                            if (sample.Outcome !== 'Response' || !sample.Response.Result) break;
                        }
                        return result;
                    } finally { worker.terminate(); }
                }, { input: fixture.Input, name: fixture.Name, warmSamples, timeoutMilliseconds });
                measurement.Mode = fixture.Mode;
                measurement.ExpectedOutcome = fixture.Outcome;
                measurement.Parity = true;
                if (measurement.Outcome) {
                    measurement.Parity = false;
                    failures++;
                }
                for (const sample of measurement.Samples) {
                    if (sample.Outcome !== 'Response') {
                        measurement.Parity = false;
                        failures++;
                        continue;
                    }
                    try { sample.Outcome = checkResponse(sample.Response, fixture); }
                    catch (error) {
                        sample.ParityError = error.message;
                        measurement.Parity = false;
                        failures++;
                    }
                }
                output.Measurements.push(measurement);
                await save();
                console.log(`${fixture.Name}/${fixture.Mode}: bootstrap ${measurement.BootstrapMilliseconds.toFixed(1)} ms; `
                    + measurement.Samples.map(sample => `${sample.Cold ? 'first' : 'warm'} ${sample.Milliseconds.toFixed(1)} ms ${sample.Outcome}`).join('; ')
                    + `; parity ${measurement.Parity ? 'pass' : 'FAIL'}`);
            } finally { await context.close(); }
        }
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
        await save();
    }
    if (failures > 0) process.exitCode = 1;
}

main().catch(error => { console.error(error); process.exitCode = 1; });
