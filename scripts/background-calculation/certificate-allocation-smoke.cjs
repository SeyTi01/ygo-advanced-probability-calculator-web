// Args: dev-probe-wwwroot, candidate-probe-wwwroot, fixture-export, output.json.
// Requires the measurement-only certificate-allocation-probe.cs; no timing claims.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const roots = process.argv.slice(2, 4).map(value => path.resolve(value));
    const fixtures = JSON.parse(await fs.readFile(process.argv[4], 'utf8')).filter(fixture =>
        ['ordinary', 'plain', 'one-draw2', 'three-draw2', 'banish10', 'mill2'].includes(fixture.Name));
    const server = http.createServer(async (request, response) => {
        try {
            const url = new URL(request.url, 'http://localhost');
            if (url.pathname === '/') {
                response.setHeader('Content-Type', 'text/html');
                return response.end('<!doctype html><title>Certificate allocations</title>');
            }
            const match = /^\/([01])\/(.*)$/.exec(decodeURIComponent(url.pathname));
            assert(match);
            if (match[2] === 'probe.js') {
                response.setHeader('Content-Type', 'text/javascript');
                return response.end(`import { dotnet } from './_framework/dotnet.js';
                    const runtime = await dotnet.create();
                    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
                    const measure = exports.YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.CertificateAllocationProbe.Measure;
                    self.onmessage = ({data}) => self.postMessage(measure(data));
                    self.postMessage('ready');`);
            }
            const file = path.resolve(roots[Number(match[1])], match[2]);
            assert(file.startsWith(roots[Number(match[1])] + path.sep));
            const bytes = await fs.readFile(file);
            response.setHeader('Content-Type', file.endsWith('.wasm') ? 'application/wasm' : file.endsWith('.js') ? 'text/javascript' : 'application/json');
            response.end(bytes);
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const browser = await chromium.launch({ headless: true, executablePath: process.env.YGO_BROWSER_EXECUTABLE });
    try {
        const page = await browser.newPage();
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        await page.evaluate(async () => {
            window.probes = [];
            await Promise.all([0, 1, 1].map(async (side, index) => {
                const worker = new Worker(`/${side}/probe.js`, { type: 'module' });
                probes[index] = worker;
                await new Promise((resolve, reject) => {
                    worker.onerror = event => reject(Error(event.message));
                    worker.onmessage = ({ data }) => data === 'ready' ? resolve() : reject(Error(data));
                });
            }));
        });
        const output = [];
        for (const fixture of fixtures) {
            const samples = [[], [], []];
            for (let trial = 0; trial < 6; trial++) {
                for (let side = 0; side < 3; side++) {
                    const input = structuredClone(fixture.Input);
                    if (side === 2) input.Options = { CertifySearchAvailability: true };
                    const text = await page.evaluate(({ side, input }) => new Promise((resolve, reject) => {
                        const timer = setTimeout(() => reject(Error('Probe timeout')), 120000);
                        probes[side].onmessage = ({ data }) => { clearTimeout(timer); resolve(data); };
                        probes[side].postMessage(JSON.stringify(input));
                    }), { side, input });
                    const [bytes, work, entries, cells, ...response] = text.split('\n');
                    const measured = { bytes: Number(bytes), work: Number(work), entries: Number(entries), cells: Number(cells), response: JSON.parse(response.join('\n')) };
                    assert.equal(measured.response.Error, null);
                    if (trial >= 3) samples[side].push(measured);
                }
            }
            for (const sample of samples[1]) {
                for (const key of ['work', 'entries', 'cells', 'response']) assert.deepEqual(sample[key], samples[0][0][key]);
            }
            for (const sample of samples[2]) {
                const response = structuredClone(sample.response);
                delete response.Result.SearchAvailability;
                assert.deepEqual(response, samples[0][0].response);
                assert(sample.work <= 10_000_000);
                assert(sample.entries <= 32_768 && sample.cells <= 262_144);
            }
            output.push({ name: fixture.Name, samples });
            await fs.writeFile(process.argv[5], JSON.stringify({ browser: browser.version(), output }));
            console.log(`${fixture.Name}: ` + samples.map(side => `${side[0].bytes} bytes / ${side[0].work} Work`).join(' / '));
        }
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
