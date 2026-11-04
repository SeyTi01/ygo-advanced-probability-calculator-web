// Opt-in timing observation; no performance thresholds.
// node scripts/workspace/measure-session-loading.cjs <published-wwwroot> <evidence-directory> [trials=5]
// Cold means a fresh browser context, not a cold server/OS cache. Warm reuses the live app.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const zlib = require('node:zlib');
const { chromium } = require('playwright');
const { launchOptions } = require('../background-calculation/browser-support.cjs');

// Deliberately compressed: transfer observations would change with the smoke server.
async function serveCompressed(root, compressedCatalog) {
    await fs.access(path.join(root, '_framework', 'dotnet.js'));
    const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.html': 'text/html', '.css': 'text/css' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            let file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname));
            if (!file.startsWith(root + path.sep)) return response.writeHead(403).end();
            // Start Help from the app shell so its button appears after WASM attaches
            // handlers, rather than mistaking prerendered markup for an interactive app.
            if (!path.extname(file)) file = path.join(root, 'index.html');
            response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
            response.setHeader('Cache-Control', 'public, max-age=3600');
            response.setHeader('Vary', 'Accept-Encoding');
            if (pathname === '/data/card-catalog.v1.json' && /\bgzip\b/.test(request.headers['accept-encoding'] || '')) {
                response.setHeader('Content-Encoding', 'gzip');
                return response.end(compressedCatalog);
            }
            if (/\bbr\b/.test(request.headers['accept-encoding'] || '')) {
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
    return { server, url: `http://127.0.0.1:${server.address().port}` };
}

function installProbe() {
    window.sessionProbe = null;
    const begin = () => { window.sessionProbe = { started: performance.now() }; };
    document.addEventListener('click', event => {
        if (event.target.closest('button')?.textContent.trim() === 'Try It with Example Data') begin();
    }, true);
    document.addEventListener('change', event => { if (event.target.id === 'sessionFileInput') begin(); }, true);
    const update = sessionRecovery.update;
    sessionRecovery.update = function (...args) {
        const result = update.apply(this, args);
        const probe = window.sessionProbe;
        if (probe && args[3] === true && probe.acceptedSnapshotMs === undefined) {
            const accepted = JSON.parse(args[2]);
            probe.acceptedSnapshotMs = performance.now() - probe.started;
            probe.cards = accepted.Cards.length;
            probe.combos = accepted.Combos.length;
            // Snapshot acceptance is observable before the debounced localStorage write.
            // Double-frame is a browser scheduling observation, not a paint guarantee.
            requestAnimationFrame(() => requestAnimationFrame(() => {
                probe.snapshotDoubleFrameMs = performance.now() - probe.started;
            }));
        }
        return result;
    };
}

async function main() {
    const [rootArgument, evidenceArgument, trialArgument = '5'] = process.argv.slice(2);
    const trials = Number(trialArgument);
    assert(rootArgument && evidenceArgument && process.argv.length <= 5 && Number.isInteger(trials) && trials > 0 && trials <= 20,
        'Usage: measure-session-loading.cjs <published-wwwroot> <evidence-directory> [trials=5; 1..20]');
    const root = path.resolve(rootArgument);
    const evidenceDirectory = path.resolve(evidenceArgument);
    await fs.mkdir(evidenceDirectory, { recursive: true });
    const sampleBytes = await fs.readFile(path.join(root, 'sample-data/example_session_state.json'));
    const sample = JSON.parse(sampleBytes);
    assert(sample.Cards.length && sample.Combos.length, 'published sample must contain cards and combos');
    const catalogBytes = await fs.readFile(path.join(root, 'data/card-catalog.v1.json'));
    const compressedCatalog = zlib.gzipSync(catalogBytes);
    const evidence = { metadata: { trials, viewport: [1440, 1000], cpuThrottle: 1, timeoutMs: 30000,
        sampleCards: sample.Cards.length, sampleCombos: sample.Combos.length,
        sampleDeckCopies: sample.Cards.reduce((sum, card) => sum + card.Copies, 0), handSize: sample.HandSize,
        catalogCards: JSON.parse(catalogBytes).cards.length, catalogJsonBytes: catalogBytes.length,
        catalogGzipBytes: compressedCatalog.length, cache: 'fresh contexts for cold, same live app for warm',
        network: 'gzip catalog / available Brotli framework, loopback, no throttling; artwork aborted',
        metrics: 'captured action event to accepted recovery snapshot (before storage debounce), then double frame; excludes app navigation/bootstrap' }, results: [] };
    const { server, url } = await serveCompressed(root, compressedCatalog);
    let browser;
    try {
        browser = await chromium.launch(launchOptions());
        evidence.metadata.browser = browser.version();
        for (const entry of ['example', 'json']) {
            for (let trial = 0; trial < trials; trial++) {
                const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'en-US' });
                let active;
                let page;
                try {
                    page = await context.newPage();
                    const errors = [];
                    let metadataRequests = [];
                    page.on('pageerror', error => errors.push(error.message));
                    page.on('request', request => { if (request.url().includes('db.ygoprodeck.com')) metadataRequests.push(request.url()); });
                    await page.route('https://ygo-calculator-artwork.ygo-probability.workers.dev/**', route => route.abort());
                    await page.goto(url + (entry === 'example' ? '/help' : '/'));
                    if (entry === 'example') {
                        await page.getByRole('button', { name: 'Try It with Example Data', exact: true }).waitFor();
                    } else {
                        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
                    }
                    await page.evaluate(installProbe);
                    // Repeated acceptance uses each action's new snapshot call, even when the session is unchanged.
                    for (const temperature of ['cold', 'warm1', 'warm2']) {
                        active = { entry, trial, temperature, status: 'running' };
                        evidence.results.push(active);
                        if (entry === 'example' && temperature !== 'cold') {
                            await page.getByRole('link', { name: 'Help', exact: true }).click();
                        }
                        metadataRequests = [];
                        await page.evaluate(() => { sessionProbe = null; performance.clearResourceTimings(); });
                        if (entry === 'example') await page.getByRole('button', { name: 'Try It with Example Data', exact: true }).click();
                        else await page.locator('#sessionFileInput').setInputFiles({ name: 'session.json', mimeType: 'application/json', buffer: sampleBytes });
                        await page.waitForFunction(() => sessionProbe?.snapshotDoubleFrameMs !== undefined
                            && document.querySelectorAll('.card-editor').length === sessionProbe.cards
                            && document.querySelectorAll('.combo-editor').length === sessionProbe.combos
                            && !document.querySelector('.session-loading-content'), undefined, { timeout: 30000 });
                        Object.assign(active, await page.evaluate(() => ({ ...sessionProbe,
                            catalogResources: performance.getEntriesByType('resource').filter(resource => new URL(resource.name).pathname === '/data/card-catalog.v1.json')
                                .map(resource => ({ duration: resource.duration, transferSize: resource.transferSize, encodedBodySize: resource.encodedBodySize, decodedBodySize: resource.decodedBodySize })) })));
                        active.metadataApiRequests = [...metadataRequests];
                        active.errors = [...errors];
                        assert.equal(active.cards, sample.Cards.length);
                        assert.equal(active.combos, sample.Combos.length);
                        assert.deepEqual(errors, []);
                        active.status = 'complete';
                        console.log(JSON.stringify(active));
                    }
                } catch (error) {
                    if (!active) { active = { entry, trial, temperature: 'setup' }; evidence.results.push(active); }
                    active.status = error.name === 'TimeoutError' ? 'timeout' : 'error';
                    active.error = error.message;
                    active.partial = await page?.evaluate(() => sessionProbe).catch(() => undefined);
                    throw error;
                } finally { await context.close(); }
            }
        }
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
        await fs.writeFile(path.join(evidenceDirectory, 'measurements.json'), JSON.stringify(evidence, null, 2));
    }
}

if (require.main === module) main().catch(error => { console.error(error); process.exitCode = 1; });
