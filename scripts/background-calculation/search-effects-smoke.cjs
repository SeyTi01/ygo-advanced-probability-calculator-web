// NODE_PATH=<Playwright modules> node search-effects-smoke.cjs <published-wwwroot> <evidence-directory>
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const root = path.resolve(process.argv[2]);
    const evidence = path.resolve(process.argv[3]);
    const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css' };
    const server = http.createServer(async (request, response) => {
        try {
            const url = new URL(request.url, 'http://localhost');
            const file = path.resolve(root, '.' + (url.pathname === '/' ? '/index.html' : decodeURIComponent(url.pathname)));
            assert(file.startsWith(root + path.sep));
            response.writeHead(200, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream' });
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const browser = await chromium.launch({ headless: true,
        ...(process.env.YGO_BROWSER_CHANNEL ? { channel: process.env.YGO_BROWSER_CHANNEL } : {}) });
    try {
        await fs.mkdir(evidence, { recursive: true });
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => {
            if (message.type() === 'error' && !message.text().startsWith('Failed to load resource:')) {
                errors.push(message.text());
                console.error(message.text());
            }
        });
        await page.addInitScript(() => {
            window.searchProbe = { requests: [], delayNext: 0, created: 0, responses: [] };
            const Native = Worker;
            window.Worker = class extends Native {
                constructor(...args) { super(...args); searchProbe.created++; }
                postMessage(message) {
                    if (message.json) {
                        searchProbe.requests.push({ id: message.id, input: JSON.parse(message.json) });
                        const delay = searchProbe.delayNext;
                        searchProbe.delayNext = 0;
                        if (delay) {
                            setTimeout(() => { if (!this.stopped) super.postMessage(message); }, delay);
                            return;
                        }
                    }
                    super.postMessage(message);
                }
                set onmessage(handler) {
                    super.onmessage = handler ? event => {
                        if (event.data?.response) searchProbe.responses.push(JSON.parse(event.data.response));
                        handler(event);
                    } : null;
                }
                terminate() { this.stopped = true; super.terminate(); }
            };
        });
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        const calculate = page.getByRole('button', { name: 'Calculate', exact: true });
        await calculate.waitFor();
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        const sample = JSON.parse(await fs.readFile(path.join(root, 'sample-data/example_session_state.json'), 'utf8'));
        let sequence = 0;
        async function load(session) {
            const marker = `Search smoke ${++sequence}`;
            const owned = structuredClone(session);
            owned.Combos[0].Name = marker;
            await page.locator('#sessionFileInput').setInputFiles({ name: 'search-smoke.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(owned)) });
            await page.waitForFunction(marker => {
                try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Combos[0].Name === marker; }
                catch { return false; }
            }, marker);
        }
        async function run() {
            const previous = await page.evaluate(() => searchProbe.responses.length);
            await calculate.click();
            await page.waitForFunction(previous => searchProbe.responses.length > previous, previous, { timeout: 60000 });
            await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
            return page.evaluate(() => searchProbe.responses.at(-1));
        }
        const rotaIndex = sample.Cards.findIndex(card => card.ExternalCardId === 32807846);
        for (let i = 0; i < 3; i++) {
            await load(sample);
            const response = await run();
            assert.equal(response.Error, null);
            assert.equal(response.Result.TotalProbability, 550987 / 658008);
        }
        const editor = page.locator('.card-editor').nth(rotaIndex);
        await editor.locator('.accordion-button').click();
        assert.match(await editor.locator('.simulation-targets').innerText(), /Vanquish Soul Razen \(29302858\)/);
        assert.match(await editor.locator('.simulation-summary').innerText(), /flexible best choice after draws/);
        await editor.locator('.simulation-edit').click();
        await editor.locator(`#cardDraw${rotaIndex}`).selectOption('');
        await editor.locator('.simulation-cancel').click();
        assert.equal(await editor.locator('.simulation-targets').count(), 1);
        for (const width of [1440, 375]) {
            await page.setViewportSize({ width, height: 1000 });
            await editor.scrollIntoViewIfNeeded();
            await page.screenshot({ path: path.join(evidence, `search-${width}.png`) });
            assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        }
        await editor.locator('.simulation-edit').click();
        await editor.locator(`#cardDraw${rotaIndex}`).selectOption('');
        await editor.locator('.simulation-save').click();
        assert.equal(await editor.locator('.simulation-targets').count(), 0);
        assert.match(await editor.locator('.simulation-source').innerText(), /Custom/);
        await editor.locator('.simulation-restore').click();
        assert.equal(await editor.locator('.simulation-targets').count(), 1);
        await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
        await page.locator('#calculationModel').selectOption('OpeningHand');
        await page.locator('#calculationModel').press('Escape');
        const off = await run();
        assert.equal(off.Error, null);
        assert(await page.evaluate(() => searchProbe.requests.at(-1).input.Cards.every(card => !card.Search && card.DrawCount === null)));
        await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
        await page.locator('#calculationModel').selectOption('Configured');
        await page.locator('#calculationModel').press('Escape');
        await page.evaluate(() => { searchProbe.delayNext = 2000; });
        await calculate.click();
        await page.getByRole('button', { name: 'Cancel calculation', exact: true }).click();
        // Load new input while the old delivery timer is still pending. Its stale
        // callback must never publish a result into this session.
        const emptyTargets = structuredClone(sample);
        emptyTargets.Cards.find(card => card.ExternalCardId === 29302858).Active = false;
        await load(emptyTargets);
        const changed = await run();
        assert.equal(changed.Error, null);
        assert.notEqual(changed.Result.TotalProbability, 550987 / 658008);
        await page.waitForTimeout(2100);
        assert.equal(await page.evaluate(() => searchProbe.responses.at(-1).Result.TotalProbability), changed.Result.TotalProbability);
        await load(sample);
        assert.equal((await run()).Result.TotalProbability, 550987 / 658008);
        const hidden = structuredClone(sample);
        Object.assign(hidden.Cards[0], { DrawCount: 1, PreDrawDeckRemovalCount: 1 });
        await load(hidden);
        const approximated = await run();
        assert.equal(approximated.Error, null);
        const searchEditor = page.locator('.card-editor').filter({ hasText: 'Reinforcement of the Army' });
        if (await searchEditor.locator('.accordion-button').getAttribute('aria-expanded') !== 'true') {
            await searchEditor.locator('.accordion-button').click();
        }
        assert.match(await searchEditor.innerText(), /targets outside the final hand remain available/);
        const mixed = structuredClone(sample);
        Object.assign(mixed.Cards.find(card => card.Name === 'Mulcharmy Fuwalos'), { DrawCount: 2, DrawOncePerTurn: false });
        await load(mixed);
        const result = await run();
        assert.equal(result.Error, null);
        assert.equal(result.Result.TotalProbability, 0.9089511612294494);
        assert.deepEqual(errors, []);
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        const requests = await page.evaluate(() => searchProbe.requests);
        assert.equal(new Set(requests.map(request => request.id)).size, requests.length);
        console.log(JSON.stringify({ browser: browser.version(), requests: requests.length, result: 'PASS: exact example, repeated loads, dynamic targets, customize/cancel/disable/restore, opening-only, stale cancellation, identity-blind hidden access, mixed draw/search Standard, desktop/mobile layout' }));
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
