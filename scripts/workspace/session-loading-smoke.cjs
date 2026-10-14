// Opt-in Release WASM acceptance: NODE_PATH=<Playwright> node <script> <publish/wwwroot> <screenshots>
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const zlib = require('node:zlib');
const { chromium } = require('playwright');

(async () => {
    const root = path.resolve(process.argv[2]);
    const evidence = path.resolve(process.argv[3]);
    await fs.mkdir(evidence, { recursive: true });
    const sampleBytes = await fs.readFile(path.join(root, 'sample-data/example_session_state.json'));
    const catalog = zlib.gzipSync(await fs.readFile(path.join(root, 'data/card-catalog.v1.json')));
    const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.html': 'text/html', '.css': 'text/css' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = new URL(request.url, 'http://localhost').pathname;
            if (pathname === '/favicon.png') return response.writeHead(204).end();
            const file = path.resolve(root, '.' + (['/', '/help'].includes(pathname) ? '/index.html' : decodeURIComponent(pathname)));
            if (!file.startsWith(root + path.sep)) return response.writeHead(403).end();
            response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
            if (pathname === '/data/card-catalog.v1.json') {
                response.setHeader('Content-Encoding', 'gzip');
                return response.end(catalog);
            }
            if (request.headers['accept-encoding']?.includes('br')) {
                try {
                    const bytes = await fs.readFile(file + '.br');
                    response.setHeader('Content-Encoding', 'br');
                    return response.end(bytes);
                } catch {}
            }
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const origin = `http://127.0.0.1:${server.address().port}`;
    let browser;
    const results = [];
    const errors = [];
    const milestone = async promise => {
        let timeout;
        try {
            return await Promise.race([promise, new Promise((_, reject) => { timeout = setTimeout(() => reject(new Error('Held request did not start within 15 seconds')), 15000); })]);
        } finally { clearTimeout(timeout); }
    };
    const gate = () => {
        let release;
        let started;
        const pending = new Promise(resolve => { release = resolve; });
        const requested = new Promise(resolve => { started = resolve; });
        return { pending, requested, release, started };
    };
    const hold = async (page, url) => {
        const control = gate();
        await page.route(url, async route => {
            control.started();
            await control.pending;
            await route.continue();
        });
        return control;
    };
    const visible = page => page.waitForFunction(() => {
        const content = document.querySelector('.session-loading-content');
        return content && getComputedStyle(content).opacity === '1';
    });
    const idle = page => page.waitForFunction(() => !document.querySelector('.session-loading-content'));
    const ready = async page => {
        await page.waitForFunction(() => document.querySelectorAll('.card-editor').length === 25 && document.querySelectorAll('.combo-editor').length === 8 && document.querySelector('.card-editor') !== window.previousSessionRow);
        await idle(page);
    };
    const inputReady = page => page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
    const upload = async (page, buffer = sampleBytes) => {
        await page.evaluate(() => { window.previousSessionRow = document.querySelector('.card-editor'); });
        await page.locator('#sessionFileInput').setInputFiles({ name: 'session.json', mimeType: 'application/json', buffer });
    };
    const geometry = page => page.evaluate(() => {
        const bounds = selector => {
            const rect = document.querySelector(selector).getBoundingClientRect();
            return [rect.x, rect.y, rect.width, rect.height];
        };
        return { controls: bounds('.session-controls'), toolbar: bounds('.session-toolbar'), deckHeader: bounds('.deck-header'), overflow: document.documentElement.scrollWidth - innerWidth };
    });
    const deckStatusSpacing = async page => {
        const spacing = await page.locator('.session-loading-content').evaluate(content => {
            const box = content.getBoundingClientRect();
            const header = content.closest('.deck-header');
            const row = header.getBoundingClientRect();
            const title = header.querySelector('h4').getBoundingClientRect();
            const sort = header.querySelector('button').getBoundingClientRect();
            const controls = document.querySelector('.session-controls').getBoundingClientRect();
            return { headingGap: box.left - title.right, sortGap: sort.left - box.right, separatorGap: box.top - controls.bottom, sameRow: box.top >= row.top && box.bottom <= row.bottom && box.top < title.bottom && sort.top < box.bottom };
        });
        assert(spacing.headingGap >= 4 && spacing.sortGap >= 4 && spacing.separatorGap >= 8 && spacing.sameRow, `Loader must fit to the right of Deck without overlap: ${JSON.stringify(spacing)}`);
        return spacing;
    };
    const newPage = async options => {
        const context = await browser.newContext({ viewport: { width: 1280, height: 1000 }, locale: 'en-US', ...options });
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => { if (message.type() === 'error' && !message.text().includes('ERR_FAILED')) errors.push(message.text()); });
        await page.route('**/card-artwork/**', route => route.abort());
        await page.addInitScript(() => { window.showSaveFilePicker = undefined; });
        return { context, page };
    };
    try {
        browser = await chromium.launch({ channel: 'msedge', headless: true });
        for (const entry of ['example', 'json']) {
            for (const [width, colorScheme, reducedMotion] of [[390, 'light', 'reduce'], [390, 'dark', 'no-preference'], [1280, 'light', 'no-preference'], [1280, 'dark', 'reduce'], [320, 'light', 'no-preference']]) {
                console.log(JSON.stringify({ checking: entry, width, colorScheme, reducedMotion }));
                const { context, page } = await newPage({ viewport: { width, height: 1000 }, colorScheme, reducedMotion });
                const catalogHold = await hold(page, '**/data/card-catalog.v1.json');
                await page.goto(origin + (entry === 'example' ? '/help' : '/'));
                if (entry === 'example') {
                    const fileHold = await hold(page, '**/sample-data/example_session_state.json');
                    await page.getByRole('button', { name: 'Try It with Example Data', exact: true }).click();
                    await milestone(fileHold.requested);
                    await visible(page);
                    assert.equal(new URL(page.url()).pathname, '/help');
                    if (width === 390) await page.screenshot({ path: path.join(evidence, `help-pending-${colorScheme}.png`) });
                    fileHold.release();
                } else {
                    await inputReady(page);
                    await page.getByRole('button', { name: 'Save Session', exact: true }).focus();
                    await upload(page);
                }
                await milestone(catalogHold.requested);
                await visible(page);
                await page.evaluate(() => scrollTo(0, 0));
                assert.equal(await page.locator('.card-editor').count(), 0, 'status paints before session cards appear');
                assert.equal(await page.locator('.session-loading-status').count(), 1);
                assert.equal(await page.locator('.session-loading-status').textContent().then(text => text.trim()), 'Loading session…');
                assert.equal(await page.locator('.session-loading-content .spinner-border').evaluate(element => getComputedStyle(element).animationName), reducedMotion === 'reduce' ? 'none' : 'spinner-border');
                assert.equal(await page.locator('html').getAttribute('data-bs-theme'), colorScheme);
                const before = await geometry(page);
                assert(before.overflow <= 0);
                const spacing = await deckStatusSpacing(page);
                if (entry === 'json') assert.equal(await page.evaluate(() => document.activeElement?.getAttribute('aria-label')), 'Save Session');
                await page.screenshot({ path: path.join(evidence, `${entry}-${width}-${colorScheme}-pending.png`) });
                catalogHold.release();
                await ready(page);
                const after = await geometry(page);
                assert.deepEqual(after, before, 'toolbar geometry remains unchanged through completion');
                assert.equal(await page.locator('#sessionFileInput').isEnabled(), true);
                // Warm repeat retains exactly the same full example; status must finish.
                await page.evaluate(() => {
                    const original = Blob.prototype.arrayBuffer;
                    Blob.prototype.arrayBuffer = function () {
                        const blob = this;
                        return new Promise(resolve => {
                            window.releaseWarmLoad = async () => {
                                Blob.prototype.arrayBuffer = original;
                                resolve(await original.call(blob));
                            };
                        });
                    };
                });
                await upload(page);
                await visible(page);
                await deckStatusSpacing(page);
                assert.deepEqual(await geometry(page), after, 'populated Deck (40) header must retain its geometry while loading');
                await page.screenshot({ path: path.join(evidence, `${entry}-${width}-${colorScheme}-populated-pending.png`) });
                await page.evaluate(() => releaseWarmLoad());
                await ready(page);
                // A malformed newer file cannot erase the restored workspace.
                await upload(page, Buffer.from('{ malformed'));
                await page.waitForFunction(() => document.body.textContent.includes('Failed to load session'));
                await idle(page);
                assert.equal(await page.locator('.card-editor').count(), 25);
                assert.equal(await page.locator('#blazor-error-ui').isVisible(), false, JSON.stringify(errors));
                results.push({ entry, width, colorScheme, reducedMotion, before, after, spacing, heldStatusPainted: true });
                await context.close();
            }
        }
        // Supersede a pending full session with a fast empty session, then release A.
        {
            const { context, page } = await newPage({});
            const catalogHold = await hold(page, '**/data/card-catalog.v1.json');
            await page.goto(origin);
            await inputReady(page);
            await upload(page);
            await milestone(catalogHold.requested);
            await visible(page);
            await upload(page, Buffer.from('{"SchemaVersion":5,"HandSize":4}'));
            await idle(page);
            catalogHold.release();
            await page.waitForTimeout(1500);
            assert.equal(await page.locator('.card-editor').count(), 0);
            assert.equal(await page.locator('#handSize').inputValue(), '4');
            await idle(page);
            results.push({ race: 'new empty session supersedes held catalog load' });
            await context.close();
        }
        // Recovery accepts the saved draft through the same owner and common region.
        {
            const { context, page } = await newPage({ viewport: { width: 390, height: 1000 }, colorScheme: 'dark' });
            await page.addInitScript(payload => localStorage.setItem('ygo-calculator:session-recovery:v1', JSON.stringify({ version: 1, payload, savedAt: '2026-10-14T18:00:00Z' })), sampleBytes.toString());
            const catalogHold = await hold(page, '**/data/card-catalog.v1.json');
            await page.goto(origin);
            await page.getByRole('button', { name: 'Restore previous session', exact: true }).click();
            await milestone(catalogHold.requested);
            await visible(page);
            await page.screenshot({ path: path.join(evidence, 'recovery-390-dark-pending.png') });
            catalogHold.release();
            await ready(page);
            results.push({ recovery: 'common status painted and cleared' });
            await context.close();
        }
        // A small successful load finishes before its delayed reveal can flash.
        {
            const { context, page } = await newPage({});
            await page.goto(origin);
            await inputReady(page);
            await page.evaluate(() => {
                window.loaderPaints = [];
                const check = () => {
                    const content = document.querySelector('.session-loading-content');
                    if (content) loaderPaints.push(Number(getComputedStyle(content).opacity));
                    window.loaderFrame = requestAnimationFrame(check);
                };
                window.loaderFrame = requestAnimationFrame(check);
            });
            await upload(page, Buffer.from('{"SchemaVersion":5,"HandSize":4}'));
            await page.waitForFunction(() => document.querySelector('#handSize').value === '4');
            await idle(page);
            const paints = await page.evaluate(() => { cancelAnimationFrame(loaderFrame); return loaderPaints; });
            assert(paints.every(opacity => opacity === 0), `fast load became visible: ${paints}`);
            results.push({ fastLoadPaintOpacities: paints });
            await context.close();
        }
        assert.deepEqual(errors, []);
        await fs.writeFile(path.join(evidence, 'acceptance.json'), JSON.stringify({ browser: browser.version(), results, errors }, null, 2));
        console.log(JSON.stringify({ browser: browser.version(), cases: results.length, errors, evidence }));
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
