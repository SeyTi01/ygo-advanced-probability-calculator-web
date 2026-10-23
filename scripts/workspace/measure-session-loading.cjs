// Opt-in: NODE_PATH=<Playwright packages> node scripts/workspace/measure-session-loading.cjs <publish/wwwroot> <evidence-directory> [trials]
// Each cold trial uses a fresh context. Warm trials reuse that app and catalog.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const zlib = require('node:zlib');
const { chromium } = require('playwright');

(async () => {
    const root = path.resolve(process.argv[2]);
    const evidence = path.resolve(process.argv[3]);
    const trials = Number(process.argv[4] || 5);
    assert(Number.isInteger(trials) && trials > 0 && trials <= 20);
    await fs.mkdir(evidence, { recursive: true });
    const sampleBytes = await fs.readFile(path.join(root, 'sample-data/example_session_state.json'));
    const sample = JSON.parse(sampleBytes);
    assert.equal(sample.Cards.length, 25);
    assert.equal(sample.Cards.reduce((sum, card) => sum + card.Copies, 0), 40);
    assert.equal(sample.Combos.length, 8);
    assert.equal(sample.ComboGroups.length, 2);
    const catalogBytes = await fs.readFile(path.join(root, 'data/card-catalog.v1.json'));
    const compressedCatalog = zlib.gzipSync(catalogBytes);
    const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.html': 'text/html', '.css': 'text/css' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = new URL(request.url, 'http://localhost').pathname;
            // The current publish omits its referenced favicon; keep that unrelated 404 out of console checks.
            if (pathname === '/favicon.png') return response.writeHead(204).end();
            let file = path.resolve(root, '.' + (pathname === '/' || pathname === '/help' ? '/index.html' : decodeURIComponent(pathname)));
            if (!file.startsWith(root + path.sep)) return response.writeHead(403).end();
            response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
            response.setHeader('Cache-Control', 'public, max-age=3600');
            if (pathname === '/data/card-catalog.v1.json') {
                response.setHeader('Content-Encoding', 'gzip');
                return response.end(compressedCatalog);
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
    let browser;
    const results = [];
    let frozen = process.env.YGO_SESSION_EXPECTED
        ? JSON.parse(await fs.readFile(process.env.YGO_SESSION_EXPECTED, 'utf8')) : undefined;
    try {
        browser = await chromium.launch({ channel: process.env.YGO_BROWSER_CHANNEL || 'msedge', headless: true });
        const metadata = { browser: browser.version(), trials, viewport: [1440, 1000], cpuThrottle: 1,
            catalogCards: JSON.parse(catalogBytes).cards.length, catalogJsonBytes: catalogBytes.length,
            catalogGzipBytes: compressedCatalog.length, cache: 'fresh contexts for cold, same live app for warm',
            artwork: 'image requests aborted; metadata requests observed', network: 'gzip catalog / Brotli framework, loopback, no throttling' };
        console.log(JSON.stringify(metadata));
        for (const entry of ['example', 'json']) {
            for (let trial = 0; trial < trials; trial++) {
                const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
                const page = await context.newPage();
                const errors = [];
                const phases = [];
                const api = [];
                page.on('pageerror', error => errors.push(error.message));
                page.on('console', message => {
                    if (message.text().startsWith('SESSION_PROFILE:')) phases.push(JSON.parse(message.text().slice(16)));
                    if (message.type() === 'error' && !message.text().includes('ERR_FAILED')) errors.push({ message: message.text(), location: message.location() });
                });
                page.on('request', request => { if (request.url().includes('db.ygoprodeck.com')) api.push(request.url()); });
                await page.route('**/card-artwork/**', route => route.abort());
                await page.addInitScript(() => {
                    window.showSaveFilePicker = undefined;
                    window.sessionProbe = null;
                    const begin = () => {
                        window.sessionProbe = { start: performance.now(), old: document.querySelector('.card-editor'), ready: null };
                    };
                    document.addEventListener('click', event => {
                        if (event.target.closest('button')?.textContent.trim() === 'Try It with Example Data') begin();
                    }, true);
                    document.addEventListener('change', event => { if (event.target.id === 'sessionFileInput') begin(); }, true);
                    new MutationObserver(() => {
                        const probe = window.sessionProbe;
                        if (!probe || probe.ready !== null || probe.waiting) return;
                        if (document.querySelectorAll('.card-editor').length !== 24 || document.querySelectorAll('.combo-editor').length !== 8 ||
                            document.querySelector('.card-editor') === probe.old) return;
                        probe.dom = performance.now() - probe.start;
                        probe.waiting = true;
                        requestAnimationFrame(() => requestAnimationFrame(() => { probe.ready = performance.now() - probe.start; }));
                    }).observe(document, { childList: true, subtree: true, characterData: true });
                });
                const origin = `http://127.0.0.1:${server.address().port}`;
                await page.goto(origin + (entry === 'example' ? '/help' : '/'));
                const waitInput = () => page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
                if (entry === 'example') await page.getByRole('button', { name: 'Try It with Example Data', exact: true }).waitFor();
                else await waitInput();
                for (const temperature of ['cold', 'warm1', 'warm2']) {
                    if (entry === 'example' && temperature !== 'cold') {
                        await page.getByRole('link', { name: 'Help', exact: true }).click();
                        await page.getByRole('button', { name: 'Try It with Example Data', exact: true }).waitFor();
                    }
                    phases.length = 0;
                    api.length = 0;
                    await page.evaluate(() => { sessionProbe = null; performance.clearResourceTimings(); });
                    if (entry === 'example') await page.getByRole('button', { name: 'Try It with Example Data', exact: true }).click();
                    else await page.locator('#sessionFileInput').setInputFiles({ name: 'session.json', mimeType: 'application/json', buffer: sampleBytes });
                    await page.waitForFunction(() => sessionProbe?.ready !== null && sessionProbe?.ready !== undefined, null, { timeout: 30000 });
                    const timing = await page.evaluate(() => ({ dom: sessionProbe.dom, ready: sessionProbe.ready,
                        catalog: performance.getEntriesByType('resource').filter(resource => resource.name.endsWith('/data/card-catalog.v1.json'))
                            .map(resource => ({ duration: resource.duration, transferSize: resource.transferSize, encodedBodySize: resource.encodedBodySize, decodedBodySize: resource.decodedBodySize })) }));
                    await waitInput();
                    const download = page.waitForEvent('download');
                    await page.getByRole('button', { name: 'Save Session', exact: true }).click();
                    const restored = JSON.parse(await fs.readFile(await (await download).path(), 'utf8'));
                    if (!frozen) frozen = restored;
                    assert.deepEqual(restored, frozen, 'cold and warm complete restored sessions must agree');
                    assert.equal(await page.locator('.alert-danger').count(), 0);
                    assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
                    assert.deepEqual(errors, []);
                    const result = { entry, trial, temperature, ...timing, phases: [...phases], metadataApiRequests: [...api] };
                    results.push(result);
                    console.log(JSON.stringify(result));
                    await page.waitForTimeout(100);
                }
                await context.close();
            }
        }
        await fs.writeFile(path.join(evidence, 'restored-session.json'), JSON.stringify(frozen, null, 2));
        await fs.writeFile(path.join(evidence, 'measurements.json'), JSON.stringify({ metadata, results }, null, 2));
        for (const entry of ['example', 'json']) {
            for (const temperature of ['cold', 'warm']) {
                const values = results.filter(result => result.entry === entry && result.temperature.startsWith(temperature)).map(result => result.ready).sort((a, b) => a - b);
                const middle = Math.floor(values.length / 2);
                console.log(JSON.stringify({ entry, temperature, n: values.length,
                    median: values.length % 2 ? values[middle] : (values[middle - 1] + values[middle]) / 2,
                    min: values[0], max: values.at(-1) }));
            }
        }
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
