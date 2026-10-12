// Opt-in: NODE_PATH may point at an existing Playwright installation.
// node scripts/background-calculation/measure-draw-effects.cjs <publish/wwwroot> <draw-worker-cases.json> [trials]
// Each trial uses a fresh production worker. Startup is measured separately.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium, firefox } = require('playwright');

async function main() {
    const [rootArgument, casesPath, trialArgument = '3'] = process.argv.slice(2);
    assert(rootArgument && casesPath, 'Supply published wwwroot and exported worker cases.');
    const trials = Number(trialArgument);
    assert(Number.isInteger(trials) && trials > 0 && trials <= 30, 'Trials must be between 1 and 30.');
    const root = path.resolve(rootArgument);
    const cases = JSON.parse(await fs.readFile(casesPath, 'utf8'));
    assert(Array.isArray(cases) && cases.length > 0 && cases.length <= 256, 'Supply 1–256 frozen cases.');
    for (const fixture of cases) {
        assert(['Success', 'Work', 'Storage'].includes(fixture.Outcome), `${fixture.Name}: unknown outcome`);
        assert(fixture.Input && typeof fixture.Input === 'object', `${fixture.Name}: missing input`);
        if (fixture.Outcome === 'Success') assert(Number.isFinite(fixture.Probability), `${fixture.Name}: missing reference`);
    }
    const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.html': 'text/html' };
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
        const engine = process.env.YGO_BROWSER_ENGINE || 'chromium';
        assert(['chromium', 'firefox'].includes(engine));
        browser = await ({ chromium, firefox }[engine]).launch({ headless: true,
            ...(engine === 'chromium' && process.env.YGO_BROWSER_CHANNEL ? { channel: process.env.YGO_BROWSER_CHANNEL } : {}) });
        const page = await browser.newPage();
        await page.goto(`http://127.0.0.1:${server.address().port}/measurement.html`);
        console.log(JSON.stringify({ engine, browser: browser.version(), trials, coldWorker: true, warmRepeat: true }));
        for (const fixture of cases) {
            const measurements = [];
            for (let trial = 0; trial < trials; trial++) {
                const measured = await page.evaluate(input => new Promise((resolve, reject) => {
                    const startup = performance.now();
                    const worker = new Worker('/js/calculation-worker.js', { type: 'module' });
                    let started;
                    let startupMilliseconds;
                    let coldMilliseconds;
                    let coldResponse;
                    const beats = [];
                    const heartbeat = setInterval(() => beats.push(performance.now()), 10);
                    const timeout = setTimeout(() => finish(new Error('Worker exceeded the 60-second measurement deadline.')), 60000);
                    function finish(error, result) {
                        clearTimeout(timeout);
                        clearInterval(heartbeat);
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
                            const elapsed = performance.now() - started;
                            if (coldMilliseconds === undefined) {
                                coldMilliseconds = elapsed;
                                coldResponse = JSON.parse(data.response);
                                started = performance.now();
                                worker.postMessage({ id: 'warm-measurement', json: JSON.stringify(input) });
                            } else {
                                finish(null, { milliseconds: coldMilliseconds, warmMilliseconds: elapsed, startupMilliseconds,
                                    maxHeartbeatGap: Math.max(0, ...beats.slice(1).map((beat, i) => beat - beats[i])),
                                    response: coldResponse, warmResponse: JSON.parse(data.response) });
                            }
                        }
                    };
                }), fixture.Input);
                const response = measured.response;
                assert.deepEqual(measured.warmResponse, response, `${fixture.Name} warm parity`);
                if (fixture.Outcome === 'Success') {
                    assert.equal(response.Error, null, fixture.Name);
                    assert.equal(response.LimitReason, null, fixture.Name);
                    assert(Math.abs(response.Result.TotalProbability - fixture.Probability) < 1e-14, fixture.Name);
                    if (fixture.ReferenceResults) assert.deepEqual(response.Result, fixture.ReferenceResults, fixture.Name);
                } else {
                    assert.equal(response.Result, null, fixture.Name);
                    assert.equal(response.LimitReason, fixture.Outcome === 'Work' ? 1 : 2, fixture.Name);
                }
                measurements.push({ milliseconds: measured.milliseconds, warmMilliseconds: measured.warmMilliseconds,
                    startupMilliseconds: measured.startupMilliseconds, maxHeartbeatGap: measured.maxHeartbeatGap });
            }
            console.log(JSON.stringify({ name: fixture.Name, work: fixture.Work, outcome: fixture.Outcome, measurements }));
        }
    } finally {
        if (browser) await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
