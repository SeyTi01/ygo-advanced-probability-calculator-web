// NODE_PATH=<Playwright modules>; args: base probe wwwroot, candidate probe wwwroot,
// fixtures.json, output.json. Probe builds use the external Compile include;
// normal shipped workers never expose this benchmark export.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const [base, candidate, fixtureFile, outputFile] = process.argv.slice(2).map(value => path.resolve(value));
    const roots = [base, candidate];
    const allFixtures = JSON.parse(await fs.readFile(fixtureFile, 'utf8'));
    const fixtures = 'Unchanged' in allFixtures[0] ? allFixtures : allFixtures.slice(0, 6);
    const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            if (pathname === '/') return response.end('<!doctype html><title>Local mode benchmark</title>');
            const match = /^\/([01])\/(.*)$/.exec(pathname);
            assert(match);
            if (match[2] === 'probe.js') {
                response.setHeader('Content-Type', 'text/javascript');
                return response.end(`import { dotnet } from './_framework/dotnet.js';
                    const runtime = await dotnet.create();
                    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
                    const measure = exports.YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.ModeBenchmarkProbe.Measure;
                    self.onmessage = ({data}) => self.postMessage(measure(data)); self.postMessage('ready');`);
            }
            const root = roots[Number(match[1])];
            const file = path.resolve(root, match[2]);
            assert(file.startsWith(root + path.sep));
            response.setHeader('Content-Type', mime[path.extname(file)] || 'application/octet-stream');
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const browser = await chromium.launch({ headless: true, ...(process.env.YGO_BROWSER_EXECUTABLE ? { executablePath: process.env.YGO_BROWSER_EXECUTABLE } : { channel: process.env.YGO_BROWSER_CHANNEL || 'msedge' }) });
    const output = [];
    try {
        const page = await browser.newPage();
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        await page.evaluate(async () => {
            window.modeWorkers = await Promise.all([0, 1].map(async side => {
                const worker = new Worker(`/${side}/probe.js`, { type: 'module' });
                await new Promise((resolve, reject) => { worker.onmessage = () => resolve(); worker.onerror = event => reject(Error(event.message)); });
                return worker;
            }));
        });
        let seed = 207208;
        const random = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed; };
        for (const fixture of fixtures) {
            const samples = [[], []];
            const firstCalls = [];
            for (let trial = 0; trial < 25; trial++) {
                for (const side of random() % 2 ? [0, 1] : [1, 0]) {
                    const measured = await page.evaluate(({ side, input }) => new Promise((resolve, reject) => {
                        const timer = setTimeout(() => reject(Error('benchmark timeout')), 120000);
                        modeWorkers[side].onmessage = ({ data }) => { clearTimeout(timer); resolve(JSON.parse(data)); };
                        modeWorkers[side].postMessage(JSON.stringify(input));
                    }), { side, input: side === 0 ? fixture.BaselineInput : fixture.Input });
                    if (trial === 0) firstCalls[side] = measured;
                    if (trial >= 5) samples[side].push(measured);
                }
            }
            for (const field of fixture.Unchanged === false ? [] : ('Unchanged' in fixture
                ? ['Total', 'Combos', 'Groups', 'Entries', 'Cells', 'Outcome', 'Work'] : ['Total', 'Combos', 'Groups', 'Entries', 'Cells'])) {
                for (const sample of samples.flat()) assert.deepEqual(sample[field], samples[0][0][field], `${fixture.Name}/${field}`);
            }
            for (const side of samples) {
                for (const field of ['Total', 'Combos', 'Groups', 'Work', 'Entries', 'Cells', 'Outcome']) {
                    for (const sample of side) assert.deepEqual(sample[field], side[0][field], `${fixture.Name}/${field} repeatability`);
                }
            }
            output.push({ name: fixture.Name, samples, firstCalls });
            await fs.writeFile(outputFile, JSON.stringify({ browser: browser.version(), output }));
            console.log(fixture.Name);
        }
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
