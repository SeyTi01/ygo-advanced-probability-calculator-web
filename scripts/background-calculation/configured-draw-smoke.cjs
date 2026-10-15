// Opt-in checks against a normal optimized WASM publish. Request delivery delay is
// injected only for deterministic cancel/recovery interaction checks.
// NODE_PATH=<Playwright modules> node configured-draw-smoke.cjs <wwwroot> <screenshots> [chromium|firefox]
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium, firefox } = require('playwright');

async function main() {
    const root = path.resolve(process.argv[2]);
    const screenshots = path.resolve(process.argv[3]);
    const engine = process.argv[4] || 'chromium';
    const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css' };
    const server = http.createServer(async (request, response) => {
        try {
            const url = new URL(request.url, 'http://localhost');
            const file = path.resolve(root, '.' + (url.pathname === '/' ? '/index.html' : decodeURIComponent(url.pathname)));
            if (!file.startsWith(root + path.sep)) throw Error('Outside publish');
            const bytes = await fs.readFile(file);
            response.writeHead(200, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream' });
            response.end(bytes);
        } catch {
            response.writeHead(404).end();
        }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const browser = await (engine === 'firefox' ? firefox : chromium).launch({ headless: true,
        ...(engine === 'firefox' ? { executablePath: process.env.PLAYWRIGHT_FIREFOX_EXECUTABLE_PATH }
            : { channel: process.env.YGO_BROWSER_CHANNEL || 'msedge' }) });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.addInitScript(() => {
            window.probe = { requests: [], delayNext: 0, live: 0, peak: 0, beats: 0 };
            setInterval(() => probe.beats++, 20);
            const Original = Worker;
            window.Worker = class extends Original {
                constructor(...args) {
                    super(...args);
                    this.delay = probe.delayNext;
                    probe.delayNext = 0;
                    probe.live++;
                    probe.peak = Math.max(probe.peak, probe.live);
                }
                set onmessage(handler) {
                    super.onmessage = handler ? event => {
                        if (event.data?.ready && this.delay) setTimeout(() => handler(event), this.delay);
                        else handler(event);
                    } : null;
                }
                postMessage(message) {
                    if (message.json) probe.requests.push(JSON.parse(message.json));
                    if (message.json && probe.delayRequest) {
                        const delay = probe.delayRequest;
                        probe.delayRequest = 0;
                        setTimeout(() => { if (!this.terminated) super.postMessage(message); }, delay);
                        return;
                    }
                    super.postMessage(message);
                }
                terminate() {
                    if (!this.terminated) { this.terminated = true; probe.live--; }
                    super.terminate();
                }
            };
        });
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        const calculate = page.getByRole('button', { name: 'Calculate', exact: true });
        await calculate.waitFor();
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
        assert.equal(await page.locator('#calculationModel').inputValue(), 'Configured');
        assert.equal(await page.locator('#storageSafetyTier').inputValue(), 'Standard');
        assert.deepEqual(await page.locator('#calculationModel option').evaluateAll(options => options.map(option => option.value)), ['Configured', 'OpeningHand']);
        await page.locator('#calculationModel').press('Escape');
        await page.getByRole('button', { name: 'Calculation settings', exact: true }).evaluate(button => {
            if (document.activeElement !== button) throw Error('Escape did not return focus');
        });
        let sequence = 0;
        async function load(session) {
            const marker = `Configured smoke ${++sequence}`;
            const owned = { ...session, Combos: session.Combos.map((combo, index) => index === 0 ? { ...combo, Name: marker } : combo) };
            await page.locator('#sessionFileInput').setInputFiles({ name: 'configured-smoke.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(owned)) });
            await page.waitForFunction(marker => { try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Combos[0].Name === marker; } catch { return false; } }, marker);
        }
        async function idle() {
            await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true', null, { timeout: 90000 });
        }
        // Keep this draw-only regression on its frozen pre-search deck.
        const example = JSON.parse(await fs.readFile(path.resolve(__dirname, '../../YGOProbabilityCalculatorBlazorTest/Fixtures/example_session_state.json'), 'utf8'));
        const graceful = structuredClone(example);
        Object.assign(graceful.Cards.find(card => card.Name === 'Mulcharmy Fuwalos'), {
            Name: 'Graceful Charity', DrawCount: 3, DrawOncePerTurn: false, PostDrawDiscardCount: 2,
            PostDrawSelector: { Kind: 'Any', Categories: [] }, PreDrawRemovalCount: 0
        });
        await load(graceful);
        assert.equal(await page.locator('.draw-complexity-notice').count(), 0);
        await calculate.click();
        await idle();
        assert.match(await page.locator('.result-model').innerText(), /Configured effects/);
        const proxy = structuredClone(graceful);
        const effect = proxy.Cards.find(card => card.Name === 'Graceful Charity');
        Object.assign(effect, { Name: 'Pot of Desires', Copies: 1, DrawCount: 2, DrawOncePerTurn: true, PostDrawDiscardCount: 0 });
        proxy.Cards.push({ ...effect, Id: 'allure-smoke', Name: 'Allure of Darkness', PostDrawDiscardCount: 1,
            PostDrawSelector: { Kind: 'Category', Categories: ['metadata:attribute:dark'] } });
        proxy.Cards.push({ ...effect, Id: 'graceful-smoke', Name: 'Graceful Charity', DrawCount: 3, DrawOncePerTurn: false, PostDrawDiscardCount: 2 });
        proxy.Cards.push({ Id: 'blank-smoke', Name: 'Blank', Copies: 1, Active: true, Categories: [] });
        await load(proxy);
        assert.equal(await page.locator('.draw-complexity-notice').count(), 1);
        const typing = [];
        for (const mode of ['OpeningHand', 'Configured']) {
            await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
            await page.locator('#calculationModel').selectOption(mode);
            await page.locator('#calculationModel').press('Escape');
            const expand = page.locator('.card-editor .accordion-button').first();
            if (await expand.getAttribute('aria-expanded') !== 'true') await expand.click();
            const name = page.locator('#cardName0');
            const original = await name.inputValue();
            await name.focus();
            await page.evaluate(() => { probe.typingStart = performance.now(); });
            await name.pressSequentially(' abcdefghijklmnopqrstuvwx', { delay: 0 });
            typing.push({ mode, characters: 25, elapsedMs: await page.evaluate(() => performance.now() - probe.typingStart) });
            assert.equal(await name.evaluate(element => element === document.activeElement), true);
            await name.fill(original);
            await name.press('Tab');
        }
        await fs.mkdir(screenshots, { recursive: true });
        for (const width of [1440, 375]) {
            await page.setViewportSize({ width, height: 1000 });
            await calculate.scrollIntoViewIfNeeded();
            await page.screenshot({ path: path.join(screenshots, `${engine}-${width}-notice.png`) });
            assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        }
        await calculate.click();
        await idle();
        const failure = await page.locator('[role="alert"]').innerText();
        assert.match(failure, /work allowance|storage safety limit/);
        assert.match(failure, /No probability was calculated/);
        assert.equal(await page.locator('.calculation-recovery p').count(), 0);
        for (const width of [1440, 375]) {
            await page.setViewportSize({ width, height: 1000 });
            await page.getByLabel('Color theme preference').selectOption('dark');
            const geometry = await page.locator('[role="alert"]').evaluate(alert => {
                const box = alert.getBoundingClientRect();
                const close = alert.querySelector('button.btn-close').getBoundingClientRect();
                const recovery = document.querySelector('.calculation-recovery').getBoundingClientRect();
                const wrapper = document.querySelector('.calculation-error');
                return { topGap: box.top - recovery.bottom, bottomGap: parseFloat(getComputedStyle(wrapper).marginBottom), offset: Math.abs((box.top + box.height / 2) - (close.top + close.height / 2)),
                    textRight: alert.querySelector('span').getBoundingClientRect().right, closeLeft: close.left };
            });
            assert(geometry.offset <= 1, JSON.stringify(geometry));
            assert.equal(geometry.topGap, 16);
            assert.equal(geometry.bottomGap, 16);
            assert(geometry.textRight < geometry.closeLeft, JSON.stringify(geometry));
            await page.locator('.calculation-recovery').scrollIntoViewIfNeeded();
            await page.screenshot({ path: path.join(screenshots, engine + '-' + width + '-error.png') });
        }
        await page.getByRole('button', { name: 'Calculate opening hand only', exact: true }).click();
        await idle();
        assert.match(await page.locator('.result-model').innerText(), /Opening hand only/);
        assert.equal(await page.locator('#calculationModel').inputValue(), 'Configured');
        assert.equal(await calculate.isEnabled(), true);
        const off = await page.evaluate(() => probe.requests.at(-1));
        assert(off.Cards.every(card => card.DrawCount === null && card.PreDrawRemovalCount === 0));
        assert.equal(off.Options.ConditionalEffectsEnabled, undefined);
        await page.getByRole('button', { name: 'Pin result', exact: true }).click();
        assert.match(await page.locator('.pinned-result').innerText(), /Opening hand only/);
        await page.evaluate(() => { probe.delayRequest = 4500; probe.peak = 0; });
        await calculate.click();
        assert.equal(await page.locator('.calculation-elapsed').count(), 0);
        await page.getByRole('button', { name: 'Calculate opening hand only', exact: true }).waitFor();
        assert.equal(await page.getByRole('button', { name: 'Cancel calculation', exact: true }).isEnabled(), true);
        const beats = await page.evaluate(() => probe.beats);
        await page.getByRole('button', { name: 'Calculate opening hand only', exact: true }).click();
        await idle();
        assert.match(await page.locator('.result-model').innerText(), /Opening hand only/);
        assert.equal(await page.evaluate(() => probe.peak), 1, 'recovery overlaps no workers');
        assert(await page.evaluate(previous => probe.beats > previous, beats));
        assert.equal(await page.locator('.calculation-elapsed').count(), 0);
        await page.screenshot({ path: path.join(screenshots, `${engine}-recovery.png`) });
        await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
        await page.locator('#calculationModel').selectOption('Configured');
        assert.match(await page.locator('#drawEffectsHelp').innerText(), /each saved card plan/);
        await page.locator('#calculationModel').press('Escape');
        await load(graceful);
        await calculate.click();
        await idle();
        assert.match(await page.locator('.result-model').innerText(), /Configured effects/);
        assert.match(await page.locator('.comparison-model-difference').innerText(), /different questions/);
        assert.deepEqual(errors, []);
        assert.equal(await page.evaluate(() => probe.live), 1);
        console.log(JSON.stringify({ engine, failure, typing, screenshots, result: 'PASS: default, preflight warning retained, no elapsed message, proxy failure, exact one-shot recovery, cancel controls, keyboard focus, narrow layout, provenance, no worker overlap or JS errors' }));
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
