// Opt-in rendered checks against a local optimized publish.
// NODE_PATH=<Playwright installation> YGO_BROWSER_CHANNEL=msedge node
// scripts/background-calculation/post-draw-smoke.cjs <publish/wwwroot> <screenshots> [realistic-worker-cases.json]
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const root = path.resolve(process.argv[2]);
    const screenshots = path.resolve(process.argv[3]);
    await fs.mkdir(screenshots, { recursive: true });
    const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm',
        '.html': 'text/html', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = new URL(request.url, 'http://localhost').pathname;
            const file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname));
            assert(file.startsWith(root + path.sep));
            response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
            response.end(await fs.readFile(file));
        } catch {
            response.writeHead(404).end();
        }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const browser = await chromium.launch({ headless: true,
        ...(process.env.YGO_BROWSER_CHANNEL ? { channel: process.env.YGO_BROWSER_CHANNEL } : {}) });
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
        // Exercise the production download fallback without a native OS dialog.
        await page.addInitScript(() => {
            window.showSaveFilePicker = undefined;
            window.workerProbe = { sent: 0, terminated: 0 };
            const Original = Worker;
            window.Worker = class extends Original {
                postMessage(...args) { workerProbe.sent++; return super.postMessage(...args); }
                terminate() { workerProbe.terminated++; return super.terminate(); }
            };
        });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => {
            if (message.type() === 'error' && !message.text().includes('Failed to load resource')) {
                errors.push(message.text());
            }
        });
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        const calculate = page.getByRole('button', { name: 'Calculate', exact: true });
        await calculate.waitFor();
        // Prerendered buttons exist before Blazor has installed InputFile events.
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        async function load(session) {
            await page.locator('#sessionFileInput').setInputFiles({ name: 'post-draw.json', mimeType: 'application/json',
                buffer: Buffer.from(JSON.stringify(session)) });
            try {
                await page.waitForFunction(name => document.querySelector('.card-header-name')?.textContent.includes(name), session.Cards[0].Name);
            } catch (error) {
                console.error(await page.locator('.alert').allTextContents());
                throw error;
            }
        }
        async function expand(index) {
            const button = page.locator('.card-editor .accordion-button').nth(index);
            if (await button.getAttribute('aria-expanded') !== 'true') await button.click();
        }
        async function saved() {
            const download = page.waitForEvent('download');
            await page.getByRole('button', { name: 'Save Session', exact: true }).click();
            return JSON.parse(await fs.readFile(await (await download).path(), 'utf8'));
        }
        const emptyCard = name => ({ Id: name, Name: name, Copies: 1, Categories: [] });
        await load({ SchemaVersion: 4, HandSize: 1, Cards: [emptyCard('Selection test')], Combos: [], Categories: [] });
        await expand(0);
        await page.locator('#cardName0').fill('Dark World Dealings');
        await page.getByRole('option', { name: 'Dark World Dealings', exact: true }).click();
        assert.equal(await page.locator('#cardDraw0').inputValue(), '1');
        assert.equal(await page.locator('#cardDiscard0').inputValue(), '1');
        assert.equal(await page.locator('#cardDrawOnce0').isChecked(), false);

        await load({ SchemaVersion: 4, HandSize: 1, Cards: [emptyCard('Graceful selection')], Combos: [], Categories: [] });
        await expand(0);
        await page.locator('#cardName0').fill('Graceful Charity');
        await page.getByRole('option', { name: 'Graceful Charity', exact: true }).click();
        assert.equal(await page.locator('#cardDraw0').inputValue(), '3');
        assert.equal(await page.locator('#cardDiscard0').inputValue(), '2');
        assert.equal(await page.locator('#cardDrawOnce0').isChecked(), false);
        assert.match(await page.locator('.card-editor .badge').first().innerText(), /Draw 3 · Discard 2/);
        await page.locator('#cardDiscard0').selectOption('1');
        assert.equal((await saved()).Cards[0].PostDrawDiscardCount, 1);
        await page.locator('#cardDraw0').selectOption('');
        assert.equal(await page.locator('#cardDiscard0').count(), 0);
        await page.locator('#cardDraw0').selectOption('3');
        assert.equal(await page.locator('#cardDiscard0').inputValue(), '0');

        await page.locator('#fileInput').setInputFiles({ name: 'post-draw.ydk', mimeType: 'text/plain',
            buffer: Buffer.from('#main\n74117290\n79571449\n89631139\n#extra\n!side\n') });
        await page.waitForFunction(() => document.querySelectorAll('.card-editor').length === 3);
        const imported = await saved();
        assert.equal(imported.Cards.find(card => card.ExternalCardId === 74117290).PostDrawDiscardCount, 1);
        assert.equal(imported.Cards.find(card => card.ExternalCardId === 79571449).PostDrawDiscardCount, 2);
        await expand(1);
        for (const width of [1280, 375, 390]) {
            for (const theme of ['light', 'dark']) {
                await page.setViewportSize({ width, height: 1000 });
                await page.getByLabel('Color theme preference').selectOption(theme);
                await page.locator('#cardDiscard1').scrollIntoViewIfNeeded();
                const geometry = await page.evaluate(() => ({ width: document.documentElement.clientWidth,
                    content: document.documentElement.scrollWidth }));
                assert(geometry.content <= geometry.width, JSON.stringify({ width, theme, geometry }));
                await page.screenshot({ path: path.join(screenshots, `${width}-${theme}.png`) });
                console.log(JSON.stringify({ width, theme, geometry }));
            }
        }

        const role = { Name: 'Ordinary', Source: 'User' };
        await load({ SchemaVersion: 4, HandSize: 5, Categories: [role], Cards: [
            { ...emptyCard('Resolution test'), DrawCount: 1, DrawOncePerTurn: false, PostDrawDiscardCount: 1 },
            { ...emptyCard('Ordinary'), Copies: 5, Categories: [role] }
        ], Combos: [{ Name: 'Five ordinary cards', Categories: [{ BaseCategory: role, MinCount: 5, MaxCount: 5 }] }] });
        async function result() {
            await calculate.click();
            await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
            return page.locator('.probability-total-value').innerText();
        }
        assert.match(await result(), /16[.,]67/);
        await expand(0);
        await page.locator('#cardDiscard0').selectOption('0');
        assert.match(await result(), /100[.,]00/);
        await page.locator('#drawEffectsEnabled').uncheck();
        assert.match(await result(), /16[.,]67/);
        await page.locator('#drawEffectsEnabled').check();
        assert.match(await result(), /100[.,]00/);

        if (process.argv[4]) {
            const fixtures = JSON.parse(await fs.readFile(process.argv[4], 'utf8'));
            const fixture = fixtures.find(value => value.Name === 'realistic-three-graceful');
            const off = fixtures.find(value => value.Name === 'realistic-three-graceful-global-off');
            await load(fixture.Session);
            await page.waitForFunction(() => document.querySelectorAll('.card-editor').length > 10);
            function percent(text) { return Number(text.replace('%', '').replace(',', '.').trim()); }
            assert.equal(percent(await result()), Number((fixture.Probability * 100).toFixed(2)));
            await page.locator('#drawEffectsEnabled').uncheck();
            assert.equal(percent(await result()), Number((off.Probability * 100).toFixed(2)));
            const stored = await saved();
            assert.equal(stored.DrawEffectsEnabled, false);
            assert.equal(stored.Cards.find(card => card.ExternalCardId === 79571449).PostDrawDiscardCount, 2);
            await page.locator('#drawEffectsEnabled').check();
            assert.equal(percent(await result()), Number((fixture.Probability * 100).toFixed(2)));

            const before = await page.evaluate(() => ({ ...workerProbe }));
            await calculate.click();
            await page.waitForFunction(sent => workerProbe.sent > sent, before.sent);
            await page.getByRole('button', { name: 'Cancel calculation', exact: true }).click();
            await page.waitForFunction(terminated => workerProbe.terminated > terminated, before.terminated);
            await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
            assert.equal(percent(await result()), Number((fixture.Probability * 100).toFixed(2)));
            console.log('PASS: realistic three-Graceful UI/worker, global off/on restores discard 2, running worker cancellation and recovery.');
        }
        assert.deepEqual(errors, []);
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        console.log('PASS: explicit selections, YDK defaults, badges, manual persistence, None clearing, exact result change, six layouts.');
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
