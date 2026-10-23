// Opt-in production UI/worker regression; export ProgressiveBrowserFixtureTest first.
// node progressive-failure-smoke.cjs <published-wwwroot> <exported-fixtures.json> <output.json>
// Uses the existing Playwright installation and YGO_BROWSER_EXECUTABLE override.
// Only the upgrade transport is deliberately faulted. No calculation response,
// probability, engine export, or initial Fast execution is replaced.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
const { serve } = require('./measure-worker-lifecycle.cjs');

function observeWorkers() {
    window.progressiveFailure = { workers: [], requests: [], errors: [], orphanResponses: [] };
    addEventListener('unhandledrejection', event => progressiveFailure.errors.push(String(event.reason)));
    const NativeWorker = Worker;
    window.Worker = class extends NativeWorker {
        constructor(...args) {
            super(...args);
            if (!String(args[0]).includes('calculation-worker.js')) return;
            const entry = this.entry = { index: progressiveFailure.workers.length, created: performance.now(), errors: [], messages: [] };
            progressiveFailure.workers.push(entry);
            this.addEventListener('error', event => entry.errors.push({ at: performance.now(), message: event.message }));
            this.addEventListener('message', ({ data }) => {
                entry.messages.push({ at: performance.now(), id: data.id, ready: data.ready, error: data.error });
                if (typeof data.response !== 'string') return;
                const request = progressiveFailure.requests.findLast(item => item.worker === entry.index && item.id === data.id);
                const response = { at: performance.now(), value: JSON.parse(data.response) };
                if (request) request.responses.push(response);
                else progressiveFailure.orphanResponses.push(response);
            });
        }
        postMessage(data, ...rest) {
            if (this.entry) progressiveFailure.requests.push({ worker: this.entry.index, id: data.id,
                stage: data.fastStage, sent: performance.now(), input: data.json, responses: [] });
            return super.postMessage(data, ...rest);
        }
        terminate() {
            if (this.entry) this.entry.terminated = performance.now();
            return super.terminate();
        }
    };
}

function snapshot() {
    const action = document.querySelector('.calculate-action > button');
    return {
        busy: action?.getAttribute('aria-busy'), actionDisabled: action?.disabled,
        marker: document.querySelector('.probability-result-status')?.textContent.trim() || '',
        mode: document.querySelector('.probability-results .result-accuracy')?.textContent.trim(),
        pinDisabled: document.querySelector('.pin-result-action')?.disabled,
        copyDisabled: document.querySelector('.probability-results [aria-label="Copy results"]')?.disabled,
        values: [...document.querySelectorAll('.probability-results .probability-with-accuracy')].map(element => ({
            title: element.title, label: element.getAttribute('aria-label'), text: element.innerText
        }))
    };
}

function fractions(actual, expected) {
    assert(actual, 'real worker must return a result');
    assert.deepEqual(actual.Contract.Fractions, expected.Contract.Fractions);
    assert.deepEqual(actual.SearchAvailability, expected.SearchAvailability);
    // An automatic physical Exact completion retains its requested Fast mode;
    // the exported explicit Exact reference has different mode provenance.
    assert.equal(actual.Contract.RequestedMode, 0);
    assert.equal(actual.Contract.ExecutedMode, 0);
    for (const key of ['Completion', 'Accuracy']) {
        assert.equal(actual.Contract[key], expected.Contract[key], key);
    }
}

function displayed(actual, expected) {
    const ratio = fraction => `${fraction.Numerator}/${fraction.Denominator}`;
    const activeGroups = expected.GroupProbabilities.map((group, index) => group.ActiveComboCount > 0 ? index : -1)
        .filter(index => index >= 0);
    const rows = [expected.Contract.Fractions.Total, ...expected.Contract.Fractions.Combos,
        ...activeGroups.map(index => expected.Contract.Fractions.Groups[index])]
        .map(fraction => `Modeled primary fraction: ${ratio(fraction)}`);
    const titles = actual.values.map(value => value.title);
    assert.equal(titles.length, rows.length);
    for (const row of rows) {
        const index = titles.findIndex(title => title.includes(row));
        assert(index >= 0, `displayed rational evidence missing: ${row}`);
        titles.splice(index, 1);
    }
    assert.equal(actual.busy, 'false');
    assert.equal(actual.actionDisabled, false);
    assert.equal(actual.pinDisabled, false);
    assert.equal(actual.copyDisabled, false);
    assert.equal(actual.marker, '', 'Preliminary/stale status must be removed');
    assert.match(actual.mode, /Progressive/);
}

async function main() {
    const [root, fixturesPath, output] = process.argv.slice(2);
    assert(root && fixturesPath && output, 'Provide published-wwwroot, exported-fixtures.json and output.json');
    const fixtures = JSON.parse(fs.readFileSync(fixturesPath));
    const source = fixtures.find(fixture => fixture.Name === 'source');
    const control = fixtures.find(fixture => fixture.Name === 'control');
    // The export names its completed Fast reference Initial, before any upgrade.
    assert(source?.Initial?.SearchAvailability && control?.Exact, 'Expected source/control progressive fixtures');
    const { server, url } = await serve(root);
    const browser = await chromium.launch({ headless: true, executablePath: process.env.YGO_BROWSER_EXECUTABLE })
        .catch(error => { server.close(); throw error; });
    const evidence = { browser: browser.version(), injectedScripts: 0, pageErrors: [] };
    try {
        const context = await browser.newContext({ serviceWorkers: 'block', permissions: ['clipboard-read', 'clipboard-write'] });
        const page = await context.newPage();
        page.on('pageerror', error => evidence.pageErrors.push(String(error)));
        await page.addInitScript(observeWorkers);
        const routePattern = '**/js/calculation-worker.js';
        const fault = 'Injected upgrade transport failure';
        // Deliberate fault injection: an uncaught worker event error, outside
        // production's calculation handler. Initial Fast runs the original script;
        // only the dispatched Progressive Exact message is stopped before execution.
        const injectFailure = async route => {
            const response = await route.fetch();
            assert.equal(response.status(), 200, 'Worker script must load normally');
            const prefix = `self.addEventListener('message', event => {
                if (event.data.fastStage === 'progressive-exact-v1') {
                    event.stopImmediatePropagation();
                    throw Error('${fault}');
                }
            });\n`;
            evidence.injectedScripts++;
            await route.fulfill({ response, body: prefix + await response.text() });
        };
        await page.route(routePattern, injectFailure);
        await page.goto(url);
        let sequence = 0;
        async function load(session) {
            const value = structuredClone(session);
            value.Cards[0].Name = `Progressive failure smoke ${++sequence}`;
            await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
            await page.locator('#sessionFileInput').setInputFiles({ name: 'progressive-failure.json', mimeType: 'application/json',
                buffer: Buffer.from(JSON.stringify(value)) });
            await page.waitForFunction(name => {
                try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Cards[0].Name === name; }
                catch { return false; }
            }, value.Cards[0].Name);
            await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
            await page.locator('#calculationModel').selectOption('Fast');
            await page.locator('#calculationModel').press('Escape');
        }
        async function run(failing) {
            const start = await page.evaluate(() => progressiveFailure.requests.length);
            await page.getByRole('button', { name: 'Calculate', exact: true }).click();
            // The ceiling belongs to this harness, not the production calculation.
            await page.waitForFunction(({ start, failing }) => {
                const requests = progressiveFailure.requests.slice(start);
                const worker = progressiveFailure.workers[requests[0]?.worker];
                return requests.length === 2 && requests[0].responses.length === 1
                    && (failing ? worker?.terminated : requests[1].responses.length === 1)
                    && document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') === 'false'
                    && !document.querySelector('.probability-result-status')
                    && document.querySelector('.pin-result-action')?.disabled === false;
            }, { start, failing }, { timeout: 90000 });
            assert.equal(await page.getByRole('alert').count(), 0);
            const requests = await page.evaluate(start => progressiveFailure.requests.slice(start), start);
            assert.deepEqual(requests.map(request => request.stage), ['initial', 'progressive-exact-v1']);
            assert.equal(requests[0].worker, requests[1].worker);
            assert.equal(requests[0].id, requests[1].id);
            assert(requests[0].responses[0].at <= requests[1].sent, 'Fast must complete before upgrade dispatch');
            return { requests, final: await page.evaluate(snapshot) };
        }
        await load(source.Session);
        evidence.failedUpgrade = await run(true);
        const initial = evidence.failedUpgrade.requests[0].responses[0].value;
        assert.equal(initial.Error, null);
        fractions(initial.Result, source.Initial);
        assert.equal(evidence.failedUpgrade.requests[1].responses.length, 0, 'Injected upgrade must not return a calculation response');
        displayed(evidence.failedUpgrade.final, source.Initial);
        const failedWorker = await page.evaluate(() => progressiveFailure.workers[0]);
        assert.equal(evidence.injectedScripts, 1);
        assert(failedWorker.errors.some(error => error.message.includes(fault)), 'Real worker error event must reach the owner');
        assert(failedWorker.terminated >= evidence.failedUpgrade.requests[1].sent, 'Failed upgrade worker must terminate');
        await page.getByRole('button', { name: 'Copy results', exact: true }).click();
        evidence.retainedSummary = await page.evaluate(() => navigator.clipboard.readText());
        assert.match(evidence.retainedSummary, /The automatic Exact attempt could not complete; the completed Fast result is retained\./);
        fs.writeFileSync(output, JSON.stringify(evidence, null, 2));

        // Restore the original route before creating another worker. A real small
        // calculation must complete, with no response supplied by this harness.
        await page.unroute(routePattern, injectFailure);
        await load(control.Session);
        evidence.recovered = await run(false);
        assert.notEqual(evidence.recovered.requests[0].worker, failedWorker.index);
        fractions(evidence.recovered.requests[0].responses[0].value.Result, control.Initial);
        fractions(evidence.recovered.requests[1].responses[0].value.Result, control.Exact);
        displayed(evidence.recovered.final, control.Exact);
        const beforeGrace = await page.evaluate(() => progressiveFailure);
        // Observe an idle grace period for stray responses or stale UI acceptance;
        // do not manufacture a late message from the terminated worker.
        await page.waitForTimeout(1500);
        assert.deepEqual(await page.evaluate(snapshot), evidence.recovered.final);
        evidence.lifecycle = await page.evaluate(() => progressiveFailure);
        assert.deepEqual(evidence.lifecycle, beforeGrace, 'No late worker response after successful recovery');
        assert.deepEqual(evidence.lifecycle.workers[0], failedWorker, 'Retired worker must remain silent during recovery');
        assert.deepEqual(evidence.lifecycle.requests.slice(0, 2), evidence.failedUpgrade.requests);
        assert.equal(evidence.lifecycle.workers.length, 2);
        assert.equal(evidence.lifecycle.workers[1].terminated, undefined);
        assert.deepEqual(evidence.lifecycle.workers[1].errors, []);
        assert.deepEqual(evidence.lifecycle.errors, []);
        assert.deepEqual(evidence.lifecycle.orphanResponses, []);
        assert(evidence.pageErrors.every(error => error.includes(fault)), 'Unexpected page error');
        assert.equal(evidence.injectedScripts, 1);
        fs.writeFileSync(output, JSON.stringify(evidence, null, 2));
        console.log('Real Fast retained after upgrade transport failure; clean worker request completed with exact reference parity.');
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
