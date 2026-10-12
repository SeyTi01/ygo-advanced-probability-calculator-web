// Opt-in browser check: NODE_PATH points at an existing Playwright installation.
// node scripts/card-search/measure-search.cjs <published/wwwroot> [trials]
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const root = path.resolve(process.argv[2]);
    const trials = Number(process.argv[3] || 3);
    const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.html': 'text/html', '.css': 'text/css' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = new URL(request.url, 'http://localhost').pathname;
            const file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : decodeURIComponent(pathname)));
            if (!file.startsWith(root + path.sep)) return response.writeHead(403).end();
            response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    let browser;
    try {
        browser = await chromium.launch({ channel: process.env.YGO_BROWSER_CHANNEL || 'msedge', headless: true });
        const sample = JSON.parse(await fs.readFile(path.join(root, 'sample-data/example_session_state.json'), 'utf8'));
        assert.equal(sample.Cards.reduce((sum, card) => sum + card.Copies, 0), 40);
        assert.equal(sample.HandSize, 5);
        const catalog = JSON.parse(await fs.readFile(path.join(root, 'data/card-catalog.v1.json'), 'utf8'));
        console.log(JSON.stringify({ browser: browser.version(), catalogCards: catalog.cards.length, trials, deck: 40, hand: 5 }));
        for (const rate of [1, 4]) {
            for (let trial = 0; trial < trials; trial++) {
                const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
                const errors = [];
                const phases = [];
                let catalogLoads = 0;
                page.on('request', request => { if (request.url().endsWith('/data/card-catalog.v1.json')) catalogLoads++; });
                page.on('pageerror', error => errors.push(error.message));
                page.on('console', message => { if (message.text().startsWith('SEARCH_PROFILE:')) phases.push(message.text()); });
                await page.route('**/card-artwork/**', route => route.abort());
                await page.goto(`http://127.0.0.1:${server.address().port}`);
                await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
                try { await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined); }
                catch (error) { console.error(await page.evaluate(() => ({ ready: document.readyState, runtime: typeof getDotnetRuntime, blazor: typeof Blazor, fileKeys: Object.keys(document.querySelector('#sessionFileInput') || {}) })), errors); throw error; }
                await page.locator('#sessionFileInput').setInputFiles({ name: 'search-sample.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(sample)) });
                try { await page.locator('.card-editor').first().waitFor(); }
                catch (error) { console.error(await page.locator('body').innerText(), errors); throw error; }
                await page.locator('.card-editor').first().locator('.accordion-button').click();
                const input = page.locator('#cardName0');
                await input.waitFor({ state: 'visible' });
                const cdp = await page.context().newCDPSession(page);
                await cdp.send('Emulation.setCPUThrottlingRate', { rate });
                await page.evaluate(() => {
                    window.searchProbe = { events: [], longTasks: [] };
                    new PerformanceObserver(list => searchProbe.longTasks.push(...list.getEntries().map(entry => entry.duration))).observe({ type: 'longtask', buffered: false });
                    document.addEventListener('input', event => {
                        if (event.target.id !== 'cardName0') return;
                        const record = { value: event.target.value, start: performance.now() };
                        searchProbe.events.push(record);
                        requestAnimationFrame(() => requestAnimationFrame(() => {
                            record.feedback = performance.now() - record.start;
                            record.visibleValue = event.target.value;
                        }));
                    }, true);
                    // Registered after Blazor's delegated handler: measures synchronous dispatch,
                    // including parent/render work and a warm synchronous catalog search.
                    document.addEventListener('input', event => {
                        if (event.target.id !== 'cardName0') return;
                        const record = searchProbe.events.at(-1);
                        record.dispatch = performance.now() - record.start;
                        const names = [...document.querySelectorAll('.card-search-result')].map(element => element.textContent.trim());
                        if (names.length) {
                            record.suggestions = record.dispatch;
                            record.names = names;
                        }
                    });
                    new MutationObserver(() => {
                        const last = searchProbe.events.at(-1);
                        const names = [...document.querySelectorAll('.card-search-result')].map(element => element.textContent.trim());
                        if (last && !last.suggestions && names.length) {
                            last.suggestions = performance.now() - last.start;
                            last.names = names;
                        }
                    }).observe(document.querySelector('.card-name-field'), { childList: true, subtree: true, characterData: true });
                });
                const coldStart = Date.now();
                const catalogLoadsBeforeFocus = catalogLoads;
                await input.focus();
                await page.locator('.card-search-result').first().waitFor();
                const coldFocus = Date.now() - coldStart;
                await page.waitForTimeout(200);
                await page.evaluate(() => { searchProbe.events = []; searchProbe.longTasks = []; });
                for (const sequence of ['Ash Blossom', 'Dark Magic']) {
                    await input.fill('');
                    await input.pressSequentially(sequence, { delay: 100 });
                    await page.waitForTimeout(200);
                }
                await input.fill('');
                await input.pressSequentially('Blue-Eyes', { delay: 5 });
                await page.waitForTimeout(200);
                await input.press('Backspace');
                await page.waitForTimeout(150);
                await input.fill('Pot of Greed');
                await page.waitForTimeout(250);
                const measured = await page.evaluate(() => structuredClone(searchProbe));
                await input.press('ArrowDown');
                const activeId = await input.getAttribute('aria-activedescendant');
                assert(activeId && await page.locator(`#${activeId}`).count());
                await input.press('Escape');
                assert.equal(await page.locator('.card-search-results').count(), 0);
                assert.equal(await input.inputValue(), 'Pot of Greed');
                assert.equal(await page.evaluate(() => document.activeElement.id), 'cardName0');
                console.log(JSON.stringify({ rate, trial, coldFocus, catalogLoadsBeforeFocus, catalogLoads, ...measured, phases, errors }));
                await input.fill('Ash Blossom');
                await page.locator('.card-search-result').first().click();
                assert.equal(await input.inputValue(), 'Ash Blossom & Joyous Spring');
                await input.fill('Final manual character!');
                await page.waitForTimeout(900);
                const recoveredName = await page.evaluate(() => JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Cards[0].Name);
                assert.equal(recoveredName, 'Final manual character!');
                await input.press('Tab');
                assert.equal(await input.inputValue(), 'Final manual character!');
                await input.focus();
                await input.fill('Blue-Eyes');
                await input.press('ArrowUp');
                await input.press('Enter');
                assert.match(await input.inputValue(), /Blue-Eyes/);
                if (trial === 0 && rate === 1 && process.env.YGO_SEARCH_SCREENSHOTS) {
                    await input.fill('Ash');
                    await page.screenshot({ path: path.join(process.env.YGO_SEARCH_SCREENSHOTS, 'card-search-desktop.png') });
                    await page.setViewportSize({ width: 390, height: 900 });
                    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth), 390);
                    await page.screenshot({ path: path.join(process.env.YGO_SEARCH_SCREENSHOTS, 'card-search-mobile.png') });
                }
                assert.deepEqual(errors, []);
                await page.close();
            }
        }
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
