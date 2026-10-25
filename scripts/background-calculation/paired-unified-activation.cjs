// Args: baseline optimized wwwroot, candidate optimized wwwroot, baseline native
// JSON, candidate native JSON, output JSON. Both native files come from the
// unchanged UnifiedActivationBenchmark fixture. Run alone after clean publishes.
// The wrapper observes the production worker; no measurement exports are added.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const roots = process.argv.slice(2, 4).map(value => path.resolve(value));
    const selected = process.env.YGO_UNIFIED_CASE;
    const native = await Promise.all(process.argv.slice(4, 6).map(async file => JSON.parse(await fs.readFile(file, 'utf8'))
        .filter(value => !selected || value.Name.includes(selected))));
    const outputFile = process.argv[6];
    assert.equal(roots.length, 2);
    assert(outputFile, 'Supply both published roots, native JSON files and an output path.');
    const samples = Number(process.env.YGO_UNIFIED_SAMPLES || 3);
    const timeout = Number(process.env.YGO_UNIFIED_TIMEOUT_MS || 120000);
    assert(Number.isInteger(samples) && samples > 0);
    assert(Number.isFinite(timeout) && timeout > 0);
    assert(native.every(side => side.length > 0), 'The case filter must select paired fixtures.');
    assert.deepEqual(native[0].map(value => value.Name), native[1].map(value => value.Name));
    for (let index = 0; index < native[0].length; index++) {
        assert.deepEqual(native[0][index].Input, native[1][index].Input, 'Paired fixture input must be identical.');
        if (native[0][index].PreserveBaseline && native[0][index].First.Outcome === 'Success'
            && native[1][index].First.Outcome !== 'Censored') {
            assert.equal(native[1][index].First.Outcome, 'Success', native[0][index].Name);
            assert.deepEqual(native[0][index].First.Result.Contract.Fractions,
                native[1][index].First.Result.Contract.Fractions, native[0][index].Name + ' retained result');
        }
    }
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            if (pathname === '/') {
                response.setHeader('Content-Type', 'text/html');
                return response.end('<!doctype html><title>Unified activation worker measurements</title>');
            }
            const match = /^\/([01])\/(.*)$/.exec(pathname);
            assert(match);
            if (match[2] === 'measure-worker.js') {
                response.setHeader('Content-Type', 'text/javascript');
                return response.end(`const send = self.postMessage.bind(self);
                    let started = performance.now();
                    self.postMessage = message => {
                        let bytes = null;
                        try { bytes = globalThis.getDotnetRuntime(0).Module.HEAPU8.buffer.byteLength; } catch {}
                        send({ Message: message, Milliseconds: performance.now() - started, WasmHeapBytes: bytes });
                    };
                    await import('./js/calculation-worker.js');
                    const calculate = self.onmessage;
                    self.onmessage = event => { started = performance.now(); calculate(event); };`);
            }
            const file = path.resolve(roots[Number(match[1])], match[2]);
            assert(file.startsWith(roots[Number(match[1])] + path.sep));
            response.setHeader('Content-Type', file.endsWith('.wasm') ? 'application/wasm'
                : file.endsWith('.js') ? 'text/javascript' : 'application/octet-stream');
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    let browser;
    try {
        browser = await chromium.launch({ headless: true, executablePath: process.env.YGO_BROWSER_EXECUTABLE,
            args: process.env.YGO_BROWSER_SINGLE_PROCESS === '1' ? ['--single-process', '--no-zygote', '--in-process-gpu'] : [] });
        const page = await browser.newPage();
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        const output = [];
        for (let index = 0; index < native[0].length; index++) {
            const fixture = native[0][index];
            const sides = [];
            // Alternate the first side to reduce drift while keeping every
            // calculation serial. A fresh worker gives each fixture cold data.
            for (const side of index % 2 ? [1, 0] : [0, 1]) {
                const measured = await page.evaluate(async ({side, input, samples, timeout}) => {
                    const worker = new Worker(`/${side}/measure-worker.js`, { type: 'module' });
                    const awaitMessage = send => new Promise(resolve => {
                        const timer = setTimeout(() => resolve({ Censored: 'Harness timeout', TimeoutMilliseconds: timeout }), timeout);
                        worker.onerror = event => { clearTimeout(timer); resolve({ Censored: 'Worker error', Detail: event.message }); };
                        worker.onmessage = ({data}) => { clearTimeout(timer); resolve(data); };
                        send?.();
                    });
                    const started = performance.now();
                    const ready = await awaitMessage();
                    const result = { BootstrapMilliseconds: performance.now() - started, Ready: ready, First: null, Samples: [] };
                    try {
                        if (!ready.Message?.ready) return result;
                        for (let trial = 0; trial <= samples; trial++) {
                            const value = await awaitMessage(() => worker.postMessage({ id: 'unified-benchmark', json: JSON.stringify(input),
                                ...(input.Options.Mode === 2 ? { execution: 'interactive-exact-v1' } : {}) }));
                            if (trial === 0) result.First = value;
                            else result.Samples.push(value);
                            if (value.Censored || value.Message?.error) break;
                        }
                        return result;
                    } finally { worker.terminate(); }
                }, {side, input: fixture.Input, samples, timeout});
                const expected = native[side][index].First;
                let firstResponse;
                for (const value of [measured.First, ...measured.Samples].filter(Boolean)) {
                    if (value.Censored || value.Message?.error) continue;
                    const response = JSON.parse(value.Message.response);
                    if (firstResponse) assert.deepEqual(response, firstResponse, fixture.Name + ' WASM repeatability');
                    else firstResponse = response;
                    // An externally stopped native measurement has no expected
                    // value. Preserve any completed browser result as evidence.
                    if (expected.Outcome === 'Censored') continue;
                    assert.deepEqual(response.Result, expected.Result, fixture.Name + '/side ' + side + ' native/WASM result');
                    if (expected.Outcome === 'Success') {
                        assert.equal(response.Error, null, fixture.Name);
                    } else {
                        const completion = { Unsupported: 1, WorkLimit: 2, StorageLimit: 3 }[expected.Outcome];
                        assert.equal(response.Contract.Completion, completion, fixture.Name);
                        assert.equal(response.Contract.Fractions, null, fixture.Name);
                    }
                }
                sides[side] = measured;
            }
            output.push({ Name: fixture.Name, PreserveBaseline: fixture.PreserveBaseline, Sides: sides });
            await fs.writeFile(outputFile, JSON.stringify({ Browser: browser.version(),
                Sources: native.map(side => side[0].SourceRevision), Output: output }));
            console.log(fixture.Name + ': ' + sides.map(side => {
                const values = side.Samples.filter(value => !value.Censored && !value.Message?.error)
                    .map(value => value.Milliseconds).sort((a, b) => a - b);
                return values.length === samples ? values[Math.floor(values.length / 2)].toFixed(3) + ' ms' : 'censored';
            }).join(' / '));
        }
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
    }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
