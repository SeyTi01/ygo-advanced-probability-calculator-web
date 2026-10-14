// Opt-in: node trade-in-allure-smoke.cjs <optimized-publish/wwwroot> <frozen-cases.json> <evidence.json>
// Inputs come from TradeInAllureBenchmark.Export and MixedDeferredBenchmark.Replay.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
const { installProbe, load, run, serve } = require('./measure-worker-lifecycle.cjs');

async function main() {
    const [root, casesPath, output] = process.argv.slice(2);
    assert(root && casesPath && output);
    const fixtures = JSON.parse(fs.readFileSync(casesPath, 'utf8'));
    const { server, url } = await serve(root);
    const browser = await chromium.launch({ headless: true,
        ...(process.env.YGO_BROWSER_CHANNEL ? { channel: process.env.YGO_BROWSER_CHANNEL } : {})
    }).catch(error => { server.close(); throw error; });
    const deadline = setTimeout(() => browser.close(), 180000);
    const evidence = [];
    try {
        for (const fixture of fixtures) {
            assert.equal(fixture.Outcome, 'Success');
            assert.equal(fixture.Input.HandSize, 5);
            assert([0, 1, 2].includes(fixture.Input.StorageTier));
            assert.equal(fixture.Input.Cards.reduce((sum, card) => sum + card.Copies, 0), 40);
            assert.equal(fixture.Input.Combos.length, 8);
            assert.equal(fixture.Input.Groups.length, 2);
            const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
            const page = await context.newPage();
            const errors = [];
            page.on('pageerror', error => errors.push(error.message));
            page.on('console', message => {
                if (message.type() === 'error' && message.text().includes('Unhandled exception')) {
                    errors.push(message.text());
                }
            });
            const workers = [];
            page.on('worker', worker => {
                if (!worker.url().includes('calculation-worker.js')) return;
                const entry = { worker, closed: false };
                workers.push(entry);
                worker.on('close', () => { entry.closed = true; entry.closedAt = Date.now(); });
            });
            await page.addInitScript(installProbe);
            await page.addInitScript(() => {
                window.acceptanceBeats = [];
                setInterval(() => acceptanceBeats.push(performance.now()), 20);
            });
            await page.goto(url);
            await load(page, fixture.Session);
            await page.waitForFunction(mode => {
                try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload)
                    .DrawActivationPriority.Mode === mode; } catch { return false; }
            }, fixture.Session.DrawActivationPriority.Mode);
            await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
            await page.locator('#storageSafetyTier').selectOption(['Standard', 'High', 'VeryHigh'][fixture.Input.StorageTier]);
            await page.keyboard.press('Escape');
            const first = await run(page, 5);
            const repeat = await run(page, 5);
            assert.deepEqual(first.response.Result, fixture.ReferenceResults);
            assert.deepEqual(repeat.response, first.response);
            const input = JSON.parse(first.input);
            assert.equal(input.WorkUnits, 150000000, 'normal UI policy, no benchmark override');
            assert.equal(input.StorageTier, fixture.Input.StorageTier);
            assert.deepEqual(input.Options, fixture.Input.Options);
            assert.equal(workers.length, 1, 'cold and reused-worker requests');
            const heapBytes = await workers[0].worker.evaluate(() => globalThis.getDotnetRuntime(0).Module.HEAPU8.buffer.byteLength);
            const previous = await page.evaluate(() => lifecycle.requests.length);
            await page.getByRole('button', { name: 'Calculate', exact: true }).click();
            await page.waitForFunction(count => lifecycle.requests.length > count, previous);
            // An unrelated UI edit and main-thread heartbeat remain live during exact work.
            await page.getByLabel('Color theme preference').selectOption('dark');
            const cancelled = Date.now();
            await page.getByRole('button', { name: 'Cancel calculation', exact: true }).click();
            await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
            const uiCancelMs = Date.now() - cancelled;
            // Restart as soon as the UI allows it, including Chromium's old-worker
            // shutdown grace period. The old request must lose result ownership.
            const restart = await run(page, 5);
            for (let attempt = 0; attempt < 200 && !workers[0].closed; attempt++) await page.waitForTimeout(25);
            assert.equal(workers[0].closed, true);
            const cancelCloseMs = workers[0].closedAt - cancelled;
            assert.equal(workers.length, 2);
            assert.deepEqual(restart.response, first.response, 'cancelled results cannot overwrite restart');
            const invalidations = [];
            for (const reason of ['hand edit', 'session replacement']) {
                const count = await page.evaluate(() => lifecycle.requests.length);
                await page.getByRole('button', { name: 'Calculate', exact: true }).click();
                await page.waitForFunction(n => lifecycle.requests.length > n, count);
                await page.waitForTimeout(100);
                assert.equal(await page.evaluate(() => lifecycle.requests.at(-1).response === undefined), true,
                    'invalidate while the calculation is actually running');
                const active = workers.at(-1);
                if (reason === 'hand edit') {
                    await page.locator('#handSize').fill('4');
                    await page.locator('#handSize').press('Tab');
                } else {
                    await load(page, { ...fixture.Session, HandSize: 4 });
                }
                await page.waitForFunction(() => document.querySelector('#handSize')?.value === '4');
                await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
                for (let attempt = 0; attempt < 200 && !active.closed; attempt++) await page.waitForTimeout(25);
                assert.equal(active.closed, true, reason);
                if (reason === 'session replacement') {
                    assert.equal(await page.locator('.probability-total-value').count(), 0,
                        'the replaced session cannot accept the previous result');
                }
                const recovered = await run(page, 5);
                assert.deepEqual(recovered.response, first.response, reason);
                invalidations.push({ reason, workerClosed: active.closed, restartMs: recovered.responseMs });
            }
            // Exclude session rendering/bootstrap and result rendering from the
            // heartbeat observation of synchronous work on the worker thread.
            const heartbeats = await page.evaluate(runs => runs.map(request => {
                const beats = acceptanceBeats.filter(time => time >= request.sent
                    && time < request.sent + request.responseMs);
                return { count: beats.length, maxGapMs: Math.max(0,
                    ...beats.slice(1).map((time, i) => time - beats[i])) };
            }), [first, repeat, restart]);
            assert(heartbeats.every(sample => sample.count > 5), 'main-thread heartbeat stays live during exact work');
            await page.screenshot({ path: `${output}.${evidence.length}.png` });
            assert.deepEqual(errors, []);
            assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
            evidence.push({ name: fixture.Name, browser: browser.version(), heapBytes, uiCancelMs, cancelCloseMs,
                firstMs: first.responseMs, repeatMs: repeat.responseMs, restartMs: restart.responseMs,
                bootstrapMs: first.bootstrapMs, heartbeats, invalidations });
            console.log(JSON.stringify(evidence.at(-1)));
            await context.close();
        }
        fs.writeFileSync(output, JSON.stringify(evidence, null, 2));
    } finally { clearTimeout(deadline); await browser.close(); server.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
