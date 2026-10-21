// Paired full ordered requests, including completed certificates and typed limits.
// Measurement builds externally include search-throughput-benchmark-probe.cs;
// Define ADAPTIVE_FAST for both builds. Args: base optimized wwwroot, candidate optimized wwwroot, fixtures, output.json.
// Both measurement builds include search-throughput-benchmark-probe.cs externally.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const roots = process.argv.slice(2, 4).map(value => path.resolve(value));
    const fixtures = JSON.parse(await fs.readFile(process.argv[4], 'utf8'));
    const server = http.createServer(async (request, response) => {
        try {
            const url = new URL(request.url, 'http://localhost');
            if (url.pathname === '/') {
                response.setHeader('Content-Type', 'text/html');
                return response.end('<!doctype html><title>Fast certificate benchmark</title>');
            }
            const match = /^\/([01])\/(.*)$/.exec(decodeURIComponent(url.pathname));
            assert(match);
            if (match[2] === 'probe.js') {
                response.setHeader('Content-Type', 'text/javascript');
                return response.end(`import { dotnet } from './_framework/dotnet.js';
                    const runtime = await dotnet.create();
                    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
                    const measure = exports.YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.FastCertificateBenchmarkProbe.Measure;
                    self.onmessage = ({data}) => { try { const result = JSON.parse(measure(data)); result.WasmHeapBytes = globalThis.getDotnetRuntime(0).Module.HEAPU8.buffer.byteLength; self.postMessage(JSON.stringify(result)); } catch (error) { self.postMessage({error: String(error)}); } };
                    self.postMessage('ready');`);
            }
            const file = path.resolve(roots[Number(match[1])], match[2]);
            assert(file.startsWith(roots[Number(match[1])] + path.sep));
            response.setHeader('Content-Type', file.endsWith('.wasm') ? 'application/wasm' : file.endsWith('.js') ? 'text/javascript' : 'application/json');
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    let browser;
    try {
        browser = await chromium.launch({ headless: true, executablePath: process.env.YGO_BROWSER_EXECUTABLE, args: process.env.YGO_BROWSER_SINGLE_PROCESS === '1' ? ['--single-process', '--no-zygote', '--in-process-gpu'] : [] });
        const page = await browser.newPage();
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        const bootstrap = await page.evaluate(async () => {
            const timings = [];
            window.probes = [];
            await Promise.all([0, 1].map(async side => {
                const started = performance.now();
                const worker = new Worker(`/${side}/probe.js`, { type: 'module' });
                probes[side] = worker;
                await new Promise((resolve, reject) => {
                    worker.onerror = event => reject(Error(event.message));
                    worker.onmessage = ({data}) => data === 'ready' ? resolve() : reject(Error(JSON.stringify(data)));
                });
                timings[side] = performance.now() - started;
            }));
            return timings;
        });
        let seed = 210211;
        const random = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed; };
        const output = [];
        for (const fixture of fixtures) {
            const sides = [0, 1];
            const unlimitedExact = fixture.UnlimitedExact === undefined ? false : fixture.UnlimitedExact;
            const allowRouteChange = fixture.AllowRouteChange === undefined ? false : fixture.AllowRouteChange;
            assert.equal(typeof unlimitedExact, 'boolean', 'UnlimitedExact must be boolean');
            assert.equal(typeof allowRouteChange, 'boolean', 'AllowRouteChange must be boolean');
            const first = [];
            const samples = Array.from({ length: 2 }, () => []);
            const warmups = Number(process.env.YGO_WARMUPS || 2);
            const count = Number(process.env.YGO_SAMPLES || 10);
            for (let trial = 0; trial < warmups + count; trial++) {
                for (const {side} of sides.map(side => ({side, order: random()})).sort((a, b) => a.order - b.order)) {
                    const input = structuredClone(fixture.Input);
                    if (unlimitedExact) assert.equal(input.Options?.Mode, 2, 'UnlimitedExact requires an explicit Exact input mode');
                    const request = { Input: input, MeasureCertificate: !fixture.Primary, UnlimitedExact: unlimitedExact };
                    const measured = await page.evaluate(({side, request}) => new Promise((resolve, reject) => {
                        const worker = probes[side];
                        const timer = setTimeout(() => reject(Error('Probe timeout')), 600000);
                        worker.onmessage = ({data}) => { clearTimeout(timer); data.error ? reject(Error(data.error)) : resolve(JSON.parse(data)); };
                        worker.postMessage(JSON.stringify(request));
                    }), {side, request});
                    if (trial === 0) first[side] = measured;
                    if (unlimitedExact) {
                        assert.equal(measured.WorkPolicy, 'Unlimited', 'The measurement probe must confirm unlimited Exact work');
                        assert.ok(measured.WorkLimit == null, 'Unlimited Exact must not retain a finite Work limit');
                    }
                    if (request.MeasureCertificate && measured.Outcome === 'Success') {
                        const mode = input.Options?.Mode ?? 0;
                        assert.equal(measured.Result.Contract.RequestedMode, mode);
                        if (mode === 2) {
                            assert.equal(measured.Result.Contract.Accuracy, 0);
                            assert.equal(measured.Result.SearchAvailability, undefined);
                        }
                    }
                    if (trial >= warmups) samples[side].push(measured);
                }
            }
            for (const side of sides) {
                for (const sample of samples[side]) {
                    for (const field of ['Total', 'Combos', 'Groups', 'Route', 'Outcome', 'Work', 'Entries', 'Cells', 'Result'])
                        assert.deepEqual(sample[field], samples[side][0][field], `${fixture.Name}/${field}: nondeterministic`);
                }
            }
            const expected = samples[0][0];
            const fields = fixture.Unchanged
                ? ['Total', 'Combos', 'Groups', 'Route', 'Outcome', 'Work', 'Entries', 'Cells', 'Result']
                : expected.Outcome === 'Success' ? ['Total', 'Combos', 'Groups', 'Route', 'Outcome', 'Result'] : [];
            const comparable = (value, field) => {
                if (!allowRouteChange || field !== 'Result' || value == null) return value;
                const result = structuredClone(value);
                if (result.Contract) delete result.Contract.SolverRoute;
                return result;
            };
            for (const field of fields) {
                if (allowRouteChange && field === 'Route') continue;
                assert.deepEqual(comparable(samples[1][0][field], field), comparable(expected[field], field), `${fixture.Name}/${field}`);
            }
            output.push({Name: fixture.Name, UnlimitedExact: unlimitedExact, AllowRouteChange: allowRouteChange, First: first, Samples: samples});
            await fs.writeFile(process.argv[5], JSON.stringify({browser: browser.version(), BootstrapMilliseconds: bootstrap, output}));
            console.log(`${fixture.Name}: ` + samples.map(side => side.length ? side.map(value => value.Milliseconds).sort((a, b) => a - b)[Math.floor(side.length / 2)].toFixed(3) : '-' ).join(' / '));
        }
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
