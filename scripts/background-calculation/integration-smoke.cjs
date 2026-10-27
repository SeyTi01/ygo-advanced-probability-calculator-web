// Run on a clean optimized Release publish: node integration-smoke.cjs <wwwroot> [output.json]
// Ungated cases use real production workers throughout. Controlled cases hold
// outgoing transport only; they prove UI/ownership behavior, not CPU latency.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
const { serve, load, mode, usable, launchOptions } = require('./browser-support.cjs');

function observe() {
    window.smoke = { requests: [], workers: [], held: [], hold: null, copied: [] };
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: async text => smoke.copied.push(text) } });
    const NativeWorker = Worker;
    window.Worker = class extends NativeWorker {
        constructor(...args) {
            super(...args);
            if (!String(args[0]).includes('calculation-worker.js')) return;
            this.record = { index: smoke.workers.length };
            smoke.workers.push(this.record);
            this.addEventListener('message', ({ data }) => {
                if (data.ready) this.record.ready = true;
                if (data.response) {
                    const request = smoke.requests.findLast(item => item.worker === this.record.index && item.id === data.id);
                    request.response = JSON.parse(data.response);
                }
            });
        }
        postMessage(message, ...rest) {
            if (!this.record) return super.postMessage(message, ...rest);
            const request = { worker: this.record.index, id: message.id, stage: message.fastStage,
                execution: message.execution, input: JSON.parse(message.json) };
            smoke.requests.push(request);
            const send = () => { request.sent = true; super.postMessage(message, ...rest); };
            if (smoke.hold === 'all' || smoke.hold === message.fastStage) {
                smoke.held.push({ request, release: send, fail: () => this.dispatchEvent(
                    new ErrorEvent('error', { message: 'Injected transport failure' })) });
            } else send();
        }
        terminate() {
            if (this.record) this.record.terminated = true;
            return super.terminate();
        }
    };
}

const target = { Name: 'Target', Source: 'User' };
const card = (Id, Name, Copies, extra = {}) => ({ Id, Name, Copies, Categories: [], Active: true, ...extra });
// Labeled-copy oracle in ThreeCalculationModesTest independently establishes:
// dealt Target = 2/5; physical search/draw/banish = 23/30; Fast primary = 5/6.
const effectSession = {
    SchemaVersion: 12, CalculationModel: 'Fast', FastExecutionStrategy: 'Progressive', HandSize: 2,
    Categories: [target], Cards: [
        card('s', 'Search target', 1, { Search: { Selector: { Kind: 'Category', Categories: ['user:Target'] }, ObjectiveMetadataOnly: false } }),
        card('a', 'Target', 1, { Categories: [target] }), card('f', 'Filler', 2),
        card('d', 'Draw and banish', 1, { DrawCount: 1, PreDrawDeckRemovalCount: 1 })
    ],
    ComboGroups: [{ Id: 'g', Name: 'Target routes' }, { Id: 'empty', Name: 'Empty group sentinel' }],
    Combos: [{ Name: 'Target route', Active: true, GroupId: 'g', Categories: [{ BaseCategory: target, MinCount: 1, MaxCount: 6 }] },
        { Name: 'Impossible route', Active: true, Categories: [], Cards: [{ CardId: 'd', MinCount: 3, MaxCount: 6 }] }]
};

async function main() {
    assert(process.argv[2], 'Provide a clean optimized published wwwroot directory');
    const { server, url } = await serve(process.argv[2]);
    const browser = await chromium.launch(launchOptions()).catch(error => { server.close(); throw error; });
    const evidence = { browser: browser.version(), terminal: [], controlled: [], pageErrors: [] };
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, locale: 'en-US' });
        page.setDefaultTimeout(30000);
        const contexts = [];
        page.on('worker', worker => {
            if (!worker.url().includes('calculation-worker.js')) return;
            const context = { closed: false };
            contexts.push(context);
            worker.on('close', () => { context.closed = true; });
        });
        page.on('pageerror', error => evidence.pageErrors.push(error.message));
        await page.addInitScript(observe);
        await page.goto(url);
        await load(page, effectSession);
        const action = page.locator('.calculate-action > button');
        const values = () => page.locator('.probability-results .probability-with-accuracy').allTextContents();
        async function settled() {
            await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') === 'false'
                && document.querySelector('.probability-results .probability-total-value'));
            assert.equal(await page.getByRole('alert').count(), 0);
        }
        async function calculate() {
            const start = await page.evaluate(() => smoke.requests.length);
            await action.click();
            await page.waitForFunction(start => smoke.requests.length > start
                && smoke.requests.slice(start).every(request => request.response), start);
            await settled();
        }
        async function checkRows(numerator, denominator) {
            const ratio = `${numerator}/${denominator}`;
            const token = new RegExp(`(?<!\\d)${ratio}(?!\\d)`);
            const rows = page.locator('.probability-results');
            assert.match(await rows.locator('.probability-total .probability-with-accuracy').getAttribute('title'), token, 'total rational binding');
            const targetRow = rows.locator('.combo-probability-item').filter({ hasText: 'Target route' });
            assert.match(await targetRow.locator('.probability-with-accuracy').getAttribute('title'), token, 'combo rational binding');
            const impossible = rows.locator('.combo-probability-item').filter({ hasText: 'Impossible route' });
            assert.match(await impossible.locator('.probability-with-accuracy').getAttribute('title'), /(?<!\d)0\/1(?!\d)/, 'zero row binding');
            assert.equal(await rows.locator('.probability-group').count(), 1, 'only populated group renders');
            assert.match(await rows.locator('.probability-group .probability-with-accuracy').getAttribute('title'), token, 'group rational binding');
            assert(!(await rows.innerText()).includes('Empty group sentinel'));
            const proofValue = targetRow.locator('.probability-with-accuracy');
            await proofValue.focus();
            assert.equal(await proofValue.locator('.probability-proof').isVisible(), true, 'keyboard reveals mathematical evidence');
            assert.match(await proofValue.getAttribute('aria-label'), token, 'accessible rational evidence');
            await usable(page, page.locator('.pin-result-action'));
        }
        // No spinner/Cancel/Preliminary sampling: faster correct engines pass.
        for (const [selected, expectedMode, fraction] of [['OpeningHand', 1, [2, 5]], ['Exact', 2, [23, 30]], ['Fast', 0, [23, 30]]]) {
            await mode(page, selected);
            const start = await page.evaluate(() => smoke.requests.length);
            await calculate();
            await checkRows(...fraction);
            const requests = await page.evaluate(start => smoke.requests.slice(start), start);
            assert.equal(requests[0].input.Options.Mode, expectedMode, 'selected mode reaches the real worker');
            assert.equal(requests.at(-1).response.Result.Contract.Accuracy, 0);
            if (selected === 'Exact') assert.equal(requests[0].execution, 'interactive-exact-v1');
            evidence.terminal.push({ selected, requests });
        }
        assert.equal(contexts.length, 1, 'successful calculations reuse one runtime');
        await mode(page, 'FastOnly');
        const fastOnlyStart = await page.evaluate(() => smoke.requests.length);
        await calculate();
        const fastOnly = await page.evaluate(start => smoke.requests.slice(start), fastOnlyStart);
        assert.equal(fastOnly.length, 1, 'Fast only never dispatches automatic Exact');
        assert.equal(fastOnly[0].input.Options.Mode, 0);
        const fastResult = fastOnly[0].response.Result;
        if (fastResult.Contract.Accuracy === 0) {
            await checkRows(23, 30);
        } else {
            assert.equal(fastResult.Contract.Accuracy, 1, 'fixture must provide certified bounds or proven Exact');
            const interval = fastResult.SearchAvailability.Combos[0];
            assert(BigInt(interval.Lower.Numerator) * 30n <= 23n * BigInt(interval.Lower.Denominator));
            assert(23n * BigInt(interval.Upper.Denominator) <= BigInt(interval.Upper.Numerator) * 30n);
            await checkRows(5, 6);
        }
        evidence.terminal.push({ selected: 'FastOnly', requests: fastOnly });
        await mode(page, 'Fast');
        await calculate();
        await page.getByRole('button', { name: 'Pin result', exact: true }).click();
        const pin = await page.locator('.pinned-result').innerText();
        await page.getByRole('button', { name: 'Copy results', exact: true }).click();
        assert.match(await page.evaluate(() => smoke.copied.at(-1)), /(?<!\d)23\/30(?!\d)/);

        async function hold(stage) {
            await page.evaluate(stage => { smoke.hold = stage; smoke.held = []; }, stage);
            await action.click();
            await page.waitForFunction(() => smoke.held.length === 1);
            assert.equal(await action.getAttribute('aria-busy'), 'true');
            if (stage === 'progressive-exact-v1') {
                // Dispatch precedes .NET presentation. Wait for acceptance of
                // this real Fast result before observing or cancelling it.
                await page.locator('.probability-result-status-preliminary').waitFor();
                assert.match(await page.locator('.probability-results .probability-total .probability-with-accuracy')
                    .getAttribute('title'), /(?<!\d)5\/6(?!\d)/);
            }
        }
        async function release(fail = false) {
            await page.evaluate(fail => {
                smoke.hold = null;
                const pending = smoke.held.shift();
                if (fail) pending.fail(); else pending.release();
            }, fail);
            await settled();
        }
        // Explicit delayed completion is a transport gate, with a required
        // handshake. It does not rely on the real calculation taking any time.
        await mode(page, 'Exact');
        await hold('all');
        await usable(page, action);
        await release();
        await checkRows(23, 30);
        evidence.controlled.push('delayed Exact release');

        // Gate the upgrade before execution, after a completed real Fast pass.
        // This fixture intentionally exercises the pending-upgrade branch;
        // ordinary terminal cases above allow immediately proven Exact results.
        await mode(page, 'Fast');
        await hold('progressive-exact-v1');
        await page.waitForFunction(() => document.querySelector('.pin-result-action')?.disabled === true);
        await release();
        await checkRows(23, 30);
        evidence.controlled.push('delayed Progressive success');
        await hold('progressive-exact-v1');
        await page.waitForFunction(() => document.querySelector('.pin-result-action')?.disabled === true);
        const preliminary = await values();
        assert.equal(await page.getByRole('button', { name: 'Copy results', exact: true }).isDisabled(), true);
        await page.setViewportSize({ width: 390, height: 900 });
        await usable(page, action);
        const closing = contexts.at(-1);
        await action.click();
        await settled();
        assert.deepEqual(await values(), preliminary, 'cancel retains accepted Fast');
        await assertClose(closing);
        assert.equal(await page.locator('.pinned-result').innerText(), pin, 'cancellation preserves manual pin');
        evidence.controlled.push('cancel upgrade and real worker close');

        await page.evaluate(() => { smoke.hold = null; smoke.held = []; });
        await calculate(); await checkRows(23, 30);
        await hold('progressive-exact-v1');
        const failed = contexts.at(-1);
        const fast = await values();
        await release(true);
        assert.deepEqual(await values(), fast, 'transport failure retains accepted Fast');
        await assertClose(failed);
        evidence.controlled.push('injected transport failure');

        await page.evaluate(() => { smoke.hold = null; smoke.held = []; });
        await calculate(); await checkRows(23, 30);
        await hold('all');
        const edited = contexts.at(-1);
        await page.locator('#handSize').fill('1'); await page.locator('#handSize').press('Tab');
        await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') === 'false');
        await assertClose(edited);
        assert.equal(await page.locator('.pinned-result').innerText(), pin, 'editing preserves manual pin');
        evidence.controlled.push('edit revokes held request');
        evidence.layout = await require('./result-layout.cjs')(page, effectSession, { calculate, hold, release });
        // Independent labeled-card fractions in TwoPhaseSearchPriorityTest.
        const a = { Name: 'A', Source: 'User' }, b = { Name: 'B', Source: 'User' }, cost = { Name: 'Cost', Source: 'User' };
        const selector = name => ({ Kind: 'Category', Categories: ['user:' + name] });
        const prioritySession = {
            SchemaVersion: 13, CalculationModel: 'Exact', FastExecutionStrategy: 'Progressive', HandSize: 3, Categories: [a, b, cost],
            DrawActivationPriority: { Mode: 'CustomPriority', CardIds: ['s', 't'] },
            Cards: [card('s', 'Paid search', 1, { Search: { Selector: selector('A'), ObjectiveMetadataOnly: false },
                PreDrawRemovalCount: 1, PreDrawSelector: selector('Cost') }),
                card('t', 'Minimum hand search', 1, { Search: { Selector: selector('B'), ObjectiveMetadataOnly: false }, MinimumHandSize: 3 }),
                card('cost', 'Payment', 1, { Categories: [cost] }), card('a', 'A', 1, { Categories: [a] }),
                card('b', 'B', 1, { Categories: [b] })],
            Combos: [{ Name: 'A plus B', Active: true, Categories: [], Cards: [
                { CardId: 'a', MinCount: 1, MaxCount: 1 }, { CardId: 'b', MinCount: 1, MaxCount: 1 }] }]
        };
        await page.evaluate(() => { smoke.hold = null; smoke.held = []; });
        await load(page, prioritySession);
        await calculate();
        assert.deepEqual(await page.evaluate(() => smoke.requests.at(-1).response.Result.Contract.Fractions.Total),
            { Numerator: '3', Denominator: '5' });
        evidence.terminal.push({ selected: 'search paid first' });
        await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
        assert.equal(await page.locator('[data-priority-id="s"] > button').first().isDisabled(), true);
        await page.getByRole('button', { name: 'Move Minimum hand search up', exact: true }).focus();
        await page.keyboard.press('Enter');
        assert.equal(await page.locator('[data-priority-id="t"]').evaluate(element => element === document.activeElement), true);
        await page.keyboard.press('Escape');
        await calculate();
        assert.deepEqual(await page.evaluate(() => smoke.requests.at(-1).response.Result.Contract.Fractions.Total),
            { Numerator: '7', Denominator: '10' });
        evidence.terminal.push({ selected: 'search minimum first' });
        const separated = structuredClone(prioritySession);
        separated.HandSize = 2;
        separated.Cards = [card('s', 'Flexible search', 1, { Search: {
            Selector: { Kind: 'AnyOf', Children: [selector('A'), selector('B')] }, ObjectiveMetadataOnly: false } }),
            card('d', 'Draw one', 1, { DrawCount: 1 }), card('a', 'A', 1, { Categories: [a] }),
            card('b', 'B', 1, { Categories: [b] }), card('f', 'Filler', 1)];
        separated.DrawActivationPriority.CardIds = ['s', 'd'];
        await load(page, separated);
        await calculate();
        assert.deepEqual(await page.evaluate(() => smoke.requests.at(-1).response.Result.Contract.Fractions.Total),
            { Numerator: '1', Denominator: '2' });
        await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
        assert.deepEqual(await page.locator('.draw-priority-list li').evaluateAll(rows => rows.map(row => row.dataset.priorityId)), ['d', 's']);
        assert.equal(await page.locator('.draw-priority-list li > button:enabled').count(), 0, 'movement cannot cross phases');
        evidence.terminal.push({ selected: 'draw before search despite cross-phase saved ranks' });
        assert.deepEqual(evidence.pageErrors, []);
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        if (process.argv[3]) fs.writeFileSync(process.argv[3], JSON.stringify(evidence, null, 2));
        console.log(`Published WASM: ${evidence.terminal.length} real terminal cases; ${evidence.controlled.length} controlled lifecycle cases; no natural-latency claim.`);

        async function assertClose(context) {
            // The browser's close event is physical execution-context evidence;
            // a cleared busy button or terminate() call alone is insufficient.
            const deadline = Date.now() + 10000;
            while (!context.closed && Date.now() < deadline) await page.waitForTimeout(25);
            assert.equal(context.closed, true, 'retired worker execution context must close');
        }
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
