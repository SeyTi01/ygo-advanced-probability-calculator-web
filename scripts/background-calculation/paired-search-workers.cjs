// NODE_PATH=<playwright modules> node paired-search-workers.cjs <old-wwwroot>
// <original-pr-wwwroot> <candidate-wwwroot> <old-export.json> <new-export.json>
// <output.json> [warmups=4] [samples=12]. Run alone, after optimized publishing.
// YGO_SEARCH_SUBSETS=1: seven source ablations; the default run measures all three.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const args = process.argv.slice(2);
    const roots = args.slice(0, 3).map(root => path.resolve(root));
    const oldCases = JSON.parse(await fs.readFile(args[3], 'utf8'));
    const cases = JSON.parse(await fs.readFile(args[4], 'utf8')).filter(item => item.Name.startsWith('search-example'));
    const warmups = Number(args[6] || 4), samples = Number(args[7] || 12);
    const subsets = process.env.YGO_SEARCH_SUBSETS === '1';
    const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            if (pathname === '/') {
                response.setHeader('Content-Type', 'text/html');
                response.end('<!doctype html><title>Search worker benchmark</title>');
                return;
            }
            const match = /^\/([012])\/(.*)$/.exec(pathname);
            assert(match);
            const root = roots[Number(match[1])];
            const file = path.resolve(root, match[2]);
            assert(file.startsWith(root + path.sep));
            response.setHeader('Content-Type', mime[path.extname(file)] || 'application/octet-stream');
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    let browser;
    const output = [];
    try {
        browser = await chromium.launch({ headless: true,
            ...(process.env.YGO_BROWSER_EXECUTABLE ? { executablePath: process.env.YGO_BROWSER_EXECUTABLE } : {}) });
        const page = await browser.newPage();
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        let seed = 2042026;
        const random = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed; };
        for (const fixture of cases) {
            for (const mask of subsets ? [0, 1, 2, 3, 4, 5, 6] : [-1]) {
                console.log(`Measuring ${fixture.Name}/${mask}`);
                const sides = subsets ? [1, 2] : [0, 1, 2];
                const input = structuredClone(fixture.Input);
                if (mask >= 0) {
                    let source = 0;
                    for (const card of input.Cards) {
                        if (card.Search && !(mask & (1 << source++))) card.Search = null;
                    }
                }
                const inputs = sides.map(side => side === 0 ? oldCases.find(item => item.Name === fixture.Name).Input : input);
                const startup = await page.evaluate(async sides => {
                    window.runners = [];
                    return Promise.all(sides.map(async side => {
                        const started = performance.now();
                        const worker = new Worker(`/${side}/js/calculation-worker.js`, { type: 'module' });
                        window.runners[side] = worker;
                        await new Promise((resolve, reject) => {
                            worker.onerror = event => reject(Error(event.message));
                            worker.onmessage = ({ data }) => data.ready ? resolve() : reject(Error(data.error));
                        });
                        return performance.now() - started;
                    }));
                }, sides);
                const measurements = sides.map(() => []);
                for (let trial = 0; trial < 1 + warmups + samples; trial++) {
                    const order = sides.map((_, index) => ({ index, key: random() })).sort((a, b) => a.key - b.key);
                    for (const { index } of order) {
                        const measured = await page.evaluate(({ side, input }) => new Promise((resolve, reject) => {
                            const worker = window.runners[side];
                            const started = performance.now();
                            const timeout = setTimeout(() => reject(Error('Worker measurement exceeded 120 seconds')), 120000);
                            worker.onmessage = ({ data }) => {
                                clearTimeout(timeout);
                                if (data.error) reject(Error(data.error));
                                else resolve({ milliseconds: performance.now() - started, response: JSON.parse(data.response) });
                            };
                            worker.postMessage({ id: 'paired', json: JSON.stringify(input) });
                        }), { side: sides[index], input: inputs[index] });
                        assert.equal(measured.response.Error, null, `${fixture.Name}/${mask}`);
                        if (mask === -1) {
                            const reference = sides[index] === 0 ? oldCases.find(item => item.Name === fixture.Name) : fixture;
                            assert.deepEqual(measured.response.Result, reference.ReferenceResults);
                        }
                        if (trial === 0 || trial > warmups) measurements[index].push(measured);
                    }
                }
                for (const measured of measurements.at(-1)) assert.deepEqual(measured.response, measurements.at(-2)[0].response);
                await page.evaluate(() => window.runners.forEach(worker => worker.terminate()));
                const row = { name: fixture.Name, mask, sides, browser: browser.version(), startup, measurements };
                output.push(row);
                await fs.writeFile(args[5], JSON.stringify(output));
                console.log(`${fixture.Name}/${mask}: ` + measurements.map(list => list.slice(1).map(item => item.milliseconds)
                    .sort((a, b) => a - b)[Math.floor(samples / 2)].toFixed(1)).join(' / '));
            }
        }
    } finally {
        if (browser) await browser.close();
        server.close();
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
