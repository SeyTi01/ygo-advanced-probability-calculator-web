// Opt-in: NODE_PATH may point at an existing Playwright installation.
// node scripts/background-calculation/measure-draw-effects.cjs <publish/wwwroot> <draw-worker-cases.json> [trials]
// Each trial uses a fresh production worker. Startup is measured separately.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const [rootArgument, casesPath, trialArgument = '3'] = process.argv.slice(2);
    assert(rootArgument && casesPath, 'Supply published wwwroot and exported worker cases.');
    const trials = Number(trialArgument);
    assert(Number.isInteger(trials) && trials > 0, 'Trials must be a positive integer.');
    const root = path.resolve(rootArgument);
    const cases = JSON.parse(await fs.readFile(casesPath, 'utf8'));
    const types = { '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.html': 'text/html' };
    const server = http.createServer(async (request, response) => {
        try {
            if (request.url === '/measurement.html') {
                response.setHeader('Content-Type', 'text/html');
                response.end('<!doctype html><title>Draw worker measurement</title>');
                return;
            }
            const file = path.resolve(root, '.' + decodeURIComponent(new URL(request.url, 'http://localhost').pathname));
            if (!file.startsWith(root + path.sep)) {
                response.writeHead(403).end();
                return;
            }
            const content = await fs.readFile(file);
            response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
            response.end(content);
        } catch {
            response.writeHead(404).end();
        }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    let browser;
    try {
        browser = await chromium.launch({ headless: true, ...(process.env.YGO_BROWSER_CHANNEL ? { channel: process.env.YGO_BROWSER_CHANNEL } : {}) });
        const page = await browser.newPage();
        await page.goto(`http://127.0.0.1:${server.address().port}/measurement.html`);
        console.log(JSON.stringify({ browser: browser.version(), trials, coldWorker: true }));
        for (const fixture of cases) {
            const measurements = [];
            for (let trial = 0; trial < trials; trial++) {
                const measured = await page.evaluate(input => new Promise((resolve, reject) => {
                    const startup = performance.now();
                    const worker = new Worker('/js/calculation-worker.js', { type: 'module' });
                    let started;
                    let startupMilliseconds;
                    const timeout = setTimeout(() => finish(new Error('Worker exceeded the 60-second measurement deadline.')), 60000);
                    function finish(error, result) {
                        clearTimeout(timeout);
                        worker.terminate();
                        if (error) reject(error);
                        else resolve(result);
                    }
                    worker.onerror = event => finish(new Error(event.message));
                    worker.onmessage = ({ data }) => {
                        if (data.error) {
                            finish(new Error(data.error));
                        } else if (data.ready) {
                            startupMilliseconds = performance.now() - startup;
                            started = performance.now();
                            worker.postMessage({ id: 'measurement', json: JSON.stringify(input) });
                        } else {
                            finish(null, { milliseconds: performance.now() - started, startupMilliseconds, response: JSON.parse(data.response) });
                        }
                    };
                }), fixture.Input);
                const response = measured.response;
                if (fixture.Outcome === 'Success') {
                    assert.equal(response.Error, null, fixture.Name);
                    assert.equal(response.LimitReason, null, fixture.Name);
                    assert(Math.abs(response.Result.TotalProbability - fixture.Probability) < 1e-14, fixture.Name);
                    if (fixture.ReferenceResults) assert.deepEqual(response.Result, fixture.ReferenceResults, fixture.Name);
                } else {
                    assert.equal(response.Result, null, fixture.Name);
                    assert.equal(response.LimitReason, fixture.Outcome === 'Work' ? 1 : 2, fixture.Name);
                }
                measurements.push({ milliseconds: measured.milliseconds, startupMilliseconds: measured.startupMilliseconds });
            }
            console.log(JSON.stringify({ name: fixture.Name, work: fixture.Work, outcome: fixture.Outcome, measurements }));
        }
    } finally {
        if (browser) await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
