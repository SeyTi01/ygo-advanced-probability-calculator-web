// NODE_PATH=<playwright modules> node certificate-smoke.cjs <baseline-wwwroot>
// <candidate-wwwroot> <fixture-export.json> <evidence-dir>
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const [baseline, candidate, fixtureFile, evidence] = process.argv.slice(2).map(value => path.resolve(value));
    await fs.mkdir(evidence, { recursive: true });
    const fixtures = JSON.parse(await fs.readFile(fixtureFile, 'utf8'));
    const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css' };
    const server = http.createServer(async (request, response) => {
        try {
            let pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            const root = pathname.startsWith('/baseline/') ? baseline : candidate;
            pathname = pathname.replace(/^\/baseline\//, '/');
            const file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname));
            assert(file.startsWith(root + path.sep));
            const bytes = await fs.readFile(file);
            response.writeHead(200, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream' });
            response.end(bytes);
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const browser = await chromium.launch({ headless: true,
        ...(process.env.YGO_BROWSER_EXECUTABLE ? { executablePath: process.env.YGO_BROWSER_EXECUTABLE } : {}) });
    const output = [];
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => {
            if (message.type() === 'error' && !message.text().startsWith('Failed to load resource:')) {
                errors.push(message.text());
                console.error(message.text());
            }
        });
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        const startup = await page.evaluate(async () => {
            window.certificateWorkers = [];
            return Promise.all(['/baseline/', '/', '/'].map(async (root, index) => {
                const start = performance.now();
                const worker = new Worker(root + 'js/calculation-worker.js', { type: 'module' });
                certificateWorkers[index] = worker;
                await new Promise((resolve, reject) => {
                    worker.onerror = event => reject(Error(event.message));
                    worker.onmessage = ({ data }) => data.ready ? resolve() : reject(Error(data.error));
                });
                return performance.now() - start;
            }));
        });
        let seed = 205206;
        const random = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed; };
        for (const fixture of process.env.YGO_CERTIFICATE_UI_ONLY ? [] : fixtures) {
            const certified = structuredClone(fixture.Input);
            certified.Options = { ...certified.Options, CertifySearchAvailability: true };
            const inputs = [fixture.Input, fixture.Input, certified];
            const samples = [[], [], []];
            for (let trial = 0; trial < 13; trial++) {
                for (const { side } of [0, 1, 2].map(side => ({ side, key: random() })).sort((a, b) => a.key - b.key)) {
                    const measured = await page.evaluate(({ side, input }) => new Promise((resolve, reject) => {
                        const start = performance.now();
                        const timer = setTimeout(() => reject(Error('Worker timeout')), 120000);
                        certificateWorkers[side].onmessage = ({ data }) => {
                            clearTimeout(timer);
                            if (data.error) reject(Error(data.error));
                            else resolve({ milliseconds: performance.now() - start, response: JSON.parse(data.response) });
                        };
                        certificateWorkers[side].postMessage({ id: 'certificate-benchmark', json: JSON.stringify(input) });
                    }), { side, input: inputs[side] });
                    assert.equal(measured.response.Error, null);
                    if (trial === 0 || trial > 4) samples[side].push(measured);
                }
            }
            for (const measured of samples[1]) assert.deepEqual(measured.response, samples[0][0].response);
            for (const measured of samples[2]) {
                const result = structuredClone(measured.response);
                assert(result.Result.SearchAvailability);
                delete result.Result.SearchAvailability;
                assert.deepEqual(result, samples[0][0].response);
            }
            output.push({ name: fixture.Name, samples });
            await fs.writeFile(path.join(evidence, 'wasm-paired.json'), JSON.stringify({ browser: browser.version(), startup, output }));
            console.log(fixture.Name + ': ' + samples.map(side => side.slice(1).map(sample => sample.milliseconds).sort((a, b) => a - b)[4].toFixed(2)).join(' / '));
        }
        await page.evaluate(() => certificateWorkers.forEach(worker => worker.terminate()));

        const sample = JSON.parse(await fs.readFile(path.join(candidate, 'sample-data/example_session_state.json'), 'utf8'));
        let sequence = 0;
        async function load(session) {
            const owned = structuredClone(session);
            const marker = `${owned.Combos[0].Name} · check ${++sequence}`;
            owned.Combos[0].Name = marker;
            await page.locator('#sessionFileInput').setInputFiles({ name: 'certificate.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(owned)) });
            // Await the accepted session, not merely the disappearance of an old
            // result. An early settings edit legitimately cancels a pending load.
            await page.waitForFunction(marker => {
                try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Combos[0].Name === marker; }
                catch { return false; }
            }, marker);
        }
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        await load(sample);
        await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
        await page.getByRole('checkbox', { name: 'Certify Deck-search availability' }).check();
        await page.getByRole('checkbox', { name: 'Certify Deck-search availability' }).press('Escape');
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.locator('.search-availability summary').waitFor();
        assert.match(await page.locator('.search-availability summary').innerText(), /Exact with respect/);
        await page.locator('.search-availability summary').click();
        assert.equal(await page.locator('.search-availability tbody tr').count(), 11);
        await page.locator('.probability-results').screenshot({ path: path.join(evidence, 'certificate-desktop.png') });
        await page.getByRole('button', { name: 'Pin result', exact: true }).click();
        await page.locator('#handSize').fill('6');
        await page.locator('#handSize').press('Tab');
        assert.match(await page.locator('.probability-result-status').innerText(), /Stale|Previous result/);
        assert.equal(await page.locator('.pinned-result .search-availability').count(), 1);
        await page.getByRole('button', { name: 'Unpin result', exact: true }).click();
        await load(sample);
        await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
        await page.locator('#calculationModel').selectOption('OpeningHand');
        await page.locator('#calculationModel').press('Escape');
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.search-availability summary')?.textContent.includes('No active Deck searches'));
        await load(sample);
        await page.setViewportSize({ width: 375, height: 900 });
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.locator('.search-availability summary').waitFor();
        await page.locator('.search-availability summary').click();
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
        await page.locator('.probability-results').screenshot({ path: path.join(evidence, 'certificate-mobile.png') });
        for (const fixture of fixtures.filter(item => ['ordered-no-loss', 'deferred-no-loss'].includes(item.Name))) {
            await load(fixture.Session);
            await page.getByRole('button', { name: 'Calculate', exact: true }).click();
            await page.waitForFunction(() => document.querySelector('.search-availability summary')?.textContent.includes('Exact with respect'));
            await page.locator('.search-availability summary').click();
            assert.match(await page.locator('.search-availability').innerText(), /No eligible search target can leave hand or Deck/);
            assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
            await page.locator('.probability-results').screenshot({ path: path.join(evidence, fixture.Name + '-mobile.png') });
            await page.setViewportSize({ width: 1440, height: 1000 });
            await page.locator('.probability-results').screenshot({ path: path.join(evidence, fixture.Name + '-desktop.png') });
            await page.setViewportSize({ width: 375, height: 900 });
        }
        assert.deepEqual(errors, []);
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        await fs.writeFile(path.join(evidence, 'smoke.json'), JSON.stringify({ browser: browser.version(), errors, checks: 'opt-in, all rows, pin, stale, repeated load, opening-only, ordered/deferred exact proof, desktop/mobile overflow' }));
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
