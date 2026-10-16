// Args: base optimized wwwroot, candidate optimized wwwroot, fixtures, output.json.
// Both measurement builds include fast-certificate-benchmark-probe.cs externally.
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
                    self.onmessage = ({data}) => { try { self.postMessage(measure(data)); } catch (error) { self.postMessage({error: String(error)}); } };
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
        browser = await chromium.launch({ headless: true, executablePath: process.env.YGO_BROWSER_EXECUTABLE });
        const page = await browser.newPage();
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        await page.evaluate(async () => {
            window.probes = [];
            await Promise.all([0, 1].map(async side => {
                const worker = new Worker(`/${side}/probe.js`, { type: 'module' });
                probes[side] = worker;
                await new Promise((resolve, reject) => {
                    worker.onerror = event => reject(Error(event.message));
                    worker.onmessage = ({data}) => data === 'ready' ? resolve() : reject(Error(JSON.stringify(data)));
                });
            }));
        });
        let seed = 210211;
        const random = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed; };
        const output = [];
        for (const fixture of fixtures) {
            const sides = fixture.Physical ? 5 : 4;
            const samples = Array.from({ length: sides }, () => []);
            for (let trial = 0; trial < 20; trial++) {
                for (const {side} of Array.from({length: sides}, (_, side) => ({side, order: random()})).sort((a, b) => a.order - b.order)) {
                    const input = structuredClone(fixture.Input);
                    if (side === 4) input.Options.Mode = 2;
                    const request = { Input: input, MeasureCertificate: side % 2 === 1 || side === 4 };
                    const measured = await page.evaluate(({side, request}) => new Promise((resolve, reject) => {
                        const worker = probes[side < 2 ? 0 : 1];
                        const timer = setTimeout(() => reject(Error('Probe timeout')), 120000);
                        worker.onmessage = ({data}) => { clearTimeout(timer); data.error ? reject(Error(data.error)) : resolve(JSON.parse(data)); };
                        worker.postMessage(JSON.stringify(request));
                    }), {side, request});
                    if (trial >= 5) samples[side].push(measured);
                }
            }
            for (const side of samples.slice(0, 4)) {
                for (const sample of side) {
                    for (const field of ['Total', 'Combos', 'Groups', 'Route', 'Outcome']) assert.deepEqual(sample[field], samples[0][0][field], `${fixture.Name}/${field}`);
                }
            }
            for (const field of ['Work', 'Entries', 'Cells']) assert.deepEqual(samples[0][0][field], samples[2][0][field], `${fixture.Name}/${field}`);
            output.push({Name: fixture.Name, Samples: samples});
            await fs.writeFile(process.argv[5], JSON.stringify({browser: browser.version(), output}));
            console.log(`${fixture.Name}: ` + samples.map(side => side.map(value => value.Milliseconds).sort((a, b) => a - b)[7].toFixed(3)).join(' / '));
        }
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
