// Opt-in UI-to-worker timing observation, with no performance thresholds.
// node scripts/background-calculation/measure-worker-lifecycle.cjs <published-wwwroot> <output.json> [trials=3]
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const { serve, load, launchOptions } = require('./browser-support.cjs');

const starter = { Name: 'Starter', Source: 'User' };
const extender = { Name: 'Extender', Source: 'User' };
// A small effect-bearing workload exercises the real UI/provider/worker boundary.
// Probability and lifecycle correctness belong to the regular tests and smokes.
const session = { SchemaVersion: 14, CalculationModel: 'Fast', FastExecutionStrategy: 'FastOnly', HandSize: 5,
    Categories: [starter, extender], ComboGroups: [],
    Cards: [
        { Id: 'starter', Name: 'Measurement starter', Copies: 6, Categories: [starter] },
        { Id: 'extender', Name: 'Measurement extender', Copies: 6, Categories: [extender] },
        { Id: 'draw', Name: 'Measurement draw', Copies: 2, Categories: [], DrawCount: 2, DrawOncePerTurn: true },
        { Id: 'blank', Name: 'Measurement filler', Copies: 26, Categories: [] }
    ],
    Combos: [{ Name: 'Starter plus extender', Categories: [starter, extender].map(BaseCategory =>
        ({ BaseCategory, MinCount: 1, MaxCount: 0, MaximumMode: 'HandSize' })) }] };

function installProbe() {
    window.lifecycle = { workers: [], requests: [], runs: [] };
    const NativeWorker = Worker;
    function observeTerminal() {
        const run = lifecycle.runs.at(-1);
        if (!run || run.uiTerminalObservedMs !== undefined) return;
        const requests = lifecycle.requests.slice(run.requestStart);
        if (!requests.length || requests.some(request => request.received === undefined)) return;
        const button = document.querySelector('.calculate-action > button');
        if (button?.getAttribute('aria-busy') !== 'false' || !document.querySelector('.probability-total-value')
            || document.querySelector('.probability-result-status')) return;
        run.uiTerminalObservedMs = performance.now() - run.started;
    }
    window.Worker = class extends NativeWorker {
        constructor(...args) {
            super(...args);
            if (!String(args[0]).includes('calculation-worker.js')) return;
            const entry = { created: performance.now(), index: lifecycle.workers.length };
            this.measurementEntry = entry;
            lifecycle.workers.push(entry);
            this.addEventListener('message', ({ data }) => {
                if (data.ready) entry.bootstrapMs = performance.now() - entry.created;
                if (typeof data.response === 'string' || data.error) {
                    const request = lifecycle.requests.findLast(request => request.worker === entry.index && request.id === data.id
                        && request.received === undefined);
                    if (request) {
                        request.received = performance.now();
                        request.roundTripMs = request.received - request.sent;
                        if (data.error) request.error = data.error;
                        else request.response = JSON.parse(data.response);
                    } else if (data.error) entry.error = data.error;
                    requestAnimationFrame(observeTerminal);
                }
            });
        }
        postMessage(data, ...rest) {
            if (this.measurementEntry) lifecycle.requests.push({ worker: this.measurementEntry.index, id: data.id,
                sent: performance.now(), input: data.json, stage: data.fastStage || data.execution || 'calculate' });
            return super.postMessage(data, ...rest);
        }
        terminate() {
            if (this.measurementEntry) this.measurementEntry.terminated = performance.now();
            return super.terminate();
        }
    };
    document.addEventListener('click', event => {
        if (!event.target.closest('.calculate-action > button')) return;
        lifecycle.runs.push({ started: performance.now(), requestStart: lifecycle.requests.length,
            workersAtStart: lifecycle.workers.length });
    }, true);
    new MutationObserver(observeTerminal).observe(document, { attributes: true, childList: true, subtree: true });
}

async function main() {
    const [root, output, trialArgument = '3'] = process.argv.slice(2);
    const trials = Number(trialArgument);
    assert(root && output && process.argv.length <= 5 && Number.isInteger(trials) && trials > 0 && trials <= 30,
        'Usage: measure-worker-lifecycle.cjs <published-wwwroot> <output.json> [trials=3; 1..30]');
    const evidence = { metadata: { trials, viewport: [1440, 1000], timeoutMs: 60000, fixture: session,
        cache: 'fresh context/app per trial; first then two requests on same app/worker',
        network: 'uncompressed loopback; artwork requests aborted',
        metrics: 'captured UI click to idle result DOM observation; worker creation to ready; postMessage to received response (includes scheduling/serialization)' }, cases: [] };
    await fs.mkdir(path.dirname(path.resolve(output)), { recursive: true });
    const { server, url } = await serve(root);
    let browser;
    try {
        browser = await chromium.launch(launchOptions());
        evidence.metadata.browser = browser.version();
        for (let trial = 0; trial < trials; trial++) {
            const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'en-US' });
            const result = { trial, status: 'running', runs: [], errors: [] };
            evidence.cases.push(result);
            let page;
            try {
                page = await context.newPage();
                page.on('pageerror', error => result.errors.push(error.message));
                await page.route('https://ygo-calculator-artwork.ygo-probability.workers.dev/**', route => route.abort());
                await page.addInitScript(installProbe);
                await page.goto(url);
                await load(page, session);
                for (const temperature of ['first', 'warm1', 'warm2']) {
                    const index = await page.evaluate(() => lifecycle.runs.length);
                    await page.getByRole('button', { name: 'Calculate', exact: true }).click();
                    await page.waitForFunction(index => {
                        const run = lifecycle.runs[index];
                        return run?.uiTerminalObservedMs !== undefined || lifecycle.workers.some(worker => worker.error)
                            || lifecycle.requests.slice(run?.requestStart ?? 0).some(request => request.error)
                            || document.querySelector('.alert-danger');
                    }, index, { timeout: 60000 });
                    const run = await page.evaluate(index => {
                        const run = lifecycle.runs[index];
                        return { ...run, requests: lifecycle.requests.slice(run.requestStart),
                            workersCreatedDuringRun: lifecycle.workers.length - run.workersAtStart,
                            retainedWorkers: lifecycle.workers.filter(worker => worker.terminated === undefined).length };
                    }, index);
                    result.runs.push({ temperature, ...run });
                    assert(run.uiTerminalObservedMs !== undefined, 'calculation failed before a terminal result');
                    assert.equal(await page.locator('.alert-danger').count(), 0);
                    assert.deepEqual(result.errors, []);
                }
                result.workers = await page.evaluate(() => lifecycle.workers);
                result.status = 'complete';
                console.log(JSON.stringify({ trial, status: result.status, workers: result.workers,
                    runs: result.runs.map(({ temperature, uiTerminalObservedMs, workersCreatedDuringRun, retainedWorkers, requests }) =>
                        ({ temperature, uiTerminalObservedMs, workersCreatedDuringRun, retainedWorkers,
                            requestRoundTripsMs: requests.map(request => request.roundTripMs) })) }));
            } catch (error) {
                result.status = error.name === 'TimeoutError' ? 'timeout' : 'error';
                result.error = error.message;
                result.partial = await page?.evaluate(() => lifecycle).catch(() => undefined);
                // A timeout is recorded, never converted to a completed duration.
                throw error;
            } finally { await context.close(); }
        }
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
        await fs.writeFile(output, JSON.stringify(evidence, null, 2));
    }
}

if (require.main === module) main().catch(error => { console.error(error); process.exitCode = 1; });
