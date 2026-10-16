// Args: clean production wwwroot, frozen fixtures, native measurements, output.json.
// Compare the shipped worker/wire boundary with each measured complete candidate.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const root = path.resolve(process.argv[2]);
    const fixtures = JSON.parse(await fs.readFile(process.argv[3], 'utf8'));
    const expected = JSON.parse(await fs.readFile(process.argv[4], 'utf8'));
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            if (pathname === '/') return response.end('<!doctype html><title>Bounded search wire smoke</title>');
            if (pathname === '/inspect.js') {
                response.setHeader('Content-Type', 'text/javascript');
                return response.end(`import { dotnet } from './_framework/dotnet.js';
                    const runtime = await dotnet.create();
                    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
                    self.postMessage(Object.keys(exports.YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation));`);
            }
            const file = path.resolve(root, '.' + pathname);
            assert(file.startsWith(root + path.sep));
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
        const exports = await page.evaluate(() => new Promise((resolve, reject) => {
            const worker = new Worker('/inspect.js', { type: 'module' });
            worker.onerror = event => reject(Error(event.message));
            worker.onmessage = ({data}) => { worker.terminate(); resolve(data); };
        }));
        assert.deepEqual(exports, ['CalculationWorkerExports'], 'Production must not export measurement probes');
        const output = [];
        for (const fixture of fixtures) {
            // A fresh worker also verifies cold startup for every input; failures
            // are retired just as the production owner retires failed workers.
            const { Response: response, Milliseconds } = await page.evaluate(input => new Promise((resolve, reject) => {
                const started = performance.now();
                const worker = new Worker('/js/calculation-worker.js', { type: 'module' });
                const timer = setTimeout(() => { worker.terminate(); reject(Error('Worker timeout')); }, 600000);
                worker.onerror = event => { clearTimeout(timer); worker.terminate(); reject(Error(event.message)); };
                worker.onmessage = ({data}) => {
                    if (data.ready) return worker.postMessage({ id: 'bounded-wire', json: JSON.stringify(input) });
                    clearTimeout(timer);
                    worker.terminate();
                    data.error ? reject(Error(data.error)) : resolve({
                        Milliseconds: performance.now() - started, Response: JSON.parse(data.response)
                    });
                };
            }), fixture.Input);
            const measured = expected.find(value => value.Name === fixture.Name).Samples[1][0];
            assert.deepEqual(response.Result, measured.Result, fixture.Name + ' full public result');
            if (measured.Outcome.endsWith('Limit')) {
                assert.equal(response.Result, null);
                assert.equal(response.Contract.Fractions, null);
                assert.equal(response.LimitReason, measured.Outcome === 'WorkLimit' ? 1 : 2);
            } else {
                assert.equal(response.Error, null);
            }
            output.push({ Name: fixture.Name, Milliseconds, Response: response });
            await fs.writeFile(process.argv[5], JSON.stringify({ browser: browser.version(), exports, output }));
            console.log(fixture.Name + ': ' + measured.Outcome);
        }
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
