// Opt-in timing observation, with no performance thresholds.
// node scripts/card-search/measure-search.cjs <published-wwwroot> <output.json> [trials=3]
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const { serve, launchOptions } = require('../background-calculation/browser-support.cjs');

function installProbe() {
    window.searchProbe = { events: [], longTasks: [] };
    if (PerformanceObserver.supportedEntryTypes.includes('longtask')) {
        new PerformanceObserver(list => searchProbe.longTasks.push(...list.getEntries().map(entry =>
            ({ startTime: entry.startTime, duration: entry.duration })))).observe({ type: 'longtask' });
    }
    document.addEventListener('input', event => {
        if (event.target.id !== 'quickCardName' || !event.target.value) return;
        const record = { query: event.target.value, started: performance.now() };
        searchProbe.events.push(record);
        requestAnimationFrame(() => requestAnimationFrame(() => {
            record.inputDoubleFrameMs = performance.now() - record.started;
        }));
    }, true);
    new MutationObserver(() => {
        const record = searchProbe.events.at(-1);
        const options = [...document.querySelectorAll('#quickCardResults [role="option"]')];
        if (!record || record.suggestionsObservedMs !== undefined || !options.length) return;
        record.suggestionsObservedMs = performance.now() - record.started;
        record.names = options.map(option => option.textContent.trim());
        requestAnimationFrame(() => requestAnimationFrame(() => {
            record.suggestionsDoubleFrameMs = performance.now() - record.started;
        }));
    }).observe(document, { childList: true, subtree: true, characterData: true });
}

async function main() {
    const [root, output, trialArgument = '3'] = process.argv.slice(2);
    const trials = Number(trialArgument);
    const cpuRates = (process.env.YGO_SEARCH_CPU_RATES || '1,4').split(',').map(Number);
    assert(root && output && process.argv.length <= 5 && Number.isInteger(trials) && trials > 0 && trials <= 20,
        'Usage: measure-search.cjs <published-wwwroot> <output.json> [trials=3; 1..20]');
    assert(cpuRates.length && cpuRates.every(rate => Number.isFinite(rate) && rate >= 1 && rate <= 20),
        'YGO_SEARCH_CPU_RATES must be comma-separated CPU throttle rates from 1 to 20');
    const evidence = { metadata: { trials, viewport: [1440, 1000], cpuRates, timeoutMs: 30000,
        cache: 'fresh context per trial/rate; first query then same-app warm queries',
        network: 'uncompressed loopback; artwork requests aborted',
        metrics: 'full-query input event to suggestion DOM observation and double animation frame; not engine-only time or paint' }, results: [] };
    await fs.mkdir(path.dirname(path.resolve(output)), { recursive: true });
    const catalog = JSON.parse(await fs.readFile(path.join(root, 'data/card-catalog.v1.json'), 'utf8'));
    evidence.metadata.catalogCards = catalog.cards.length;
    const { server, url } = await serve(root);
    let browser;
    try {
        browser = await chromium.launch(launchOptions());
        evidence.metadata.browser = browser.version();
        for (const rate of evidence.metadata.cpuRates) {
            for (let trial = 0; trial < trials; trial++) {
                const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'en-US' });
                const result = { rate, trial, status: 'running', events: [], errors: [] };
                evidence.results.push(result);
                let page;
                try {
                    page = await context.newPage();
                    let catalogRequests = 0;
                    page.on('pageerror', error => result.errors.push(error.message));
                    page.on('request', request => { if (new URL(request.url()).pathname === '/data/card-catalog.v1.json') catalogRequests++; });
                    await page.route('https://ygo-calculator-artwork.ygo-probability.workers.dev/**', route => route.abort());
                    await page.addInitScript(installProbe);
                    await page.goto(url);
                    await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
                    const input = page.locator('#quickCardName');
                    await input.focus();
                    const cdp = await context.newCDPSession(page);
                    await cdp.send('Emulation.setCPUThrottlingRate', { rate });
                    result.catalogRequestsBeforeQueries = catalogRequests;
                    await page.evaluate(() => { searchProbe.longTasks = []; });
                    for (const query of ['Ash Blossom', 'Ash Blossom', 'Blue-Eyes']) {
                        await input.fill('');
                        await page.locator('#quickCardResults').waitFor({ state: 'hidden' });
                        await input.fill(query);
                        await page.waitForFunction(query => {
                            const event = searchProbe.events.at(-1);
                            return event?.query === query && event.suggestionsDoubleFrameMs !== undefined && event.inputDoubleFrameMs !== undefined;
                        }, query, { timeout: 30000 });
                    }
                    Object.assign(result, await page.evaluate(() => ({ ...searchProbe,
                        catalogResources: performance.getEntriesByType('resource').filter(entry => new URL(entry.name).pathname === '/data/card-catalog.v1.json')
                            .map(entry => ({ duration: entry.duration, transferSize: entry.transferSize, decodedBodySize: entry.decodedBodySize })) })));
                    result.catalogRequests = catalogRequests;
                    result.events.forEach((event, index) => { event.temperature = index === 0 ? 'first' : 'warm'; });
                    assert.deepEqual(result.errors, []);
                    result.status = 'complete';
                    console.log(JSON.stringify(result));
                } catch (error) {
                    result.status = error.name === 'TimeoutError' ? 'timeout' : 'error';
                    result.error = error.message;
                    result.partial = await page?.evaluate(() => searchProbe).catch(() => undefined);
                    // Failed/time-limited trials are not included as successful durations.
                    throw error;
                } finally { await context.close(); }
            }
        }
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
        await fs.writeFile(output, JSON.stringify(evidence, null, 2));
    }
}

if (require.main === module) main().catch(error => { console.error(error); process.exitCode = 1; });
