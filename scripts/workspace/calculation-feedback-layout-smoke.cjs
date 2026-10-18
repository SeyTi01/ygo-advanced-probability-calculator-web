// Run against the local app: NODE_PATH=<Playwright modules> node scripts/workspace/calculation-feedback-layout-smoke.cjs [url]
// Test-only response holds exercise production Fast deadlines and explicit Exact without changing solver output.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    const page = await browser.newPage({ locale: 'en-US', viewport: { width: 1440, height: 1000 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const evidence = path.join(os.tmpdir(), 'issue248-exact-retry');
    await fs.mkdir(evidence, { recursive: true });
    try {
        await page.addInitScript(() => {
            const NativeWorker = window.Worker;
            const probe = window.__exactRetryProbe = { requests: [], holdExact: true, pending: null, copied: [] };
            Object.defineProperty(navigator, 'clipboard', { value: { writeText: async text => probe.copied.push(text) } });
            window.Worker = class extends NativeWorker {
                set onmessage(handler) {
                    this._handler = handler;
                    super.onmessage = handler ? event => {
                        if (typeof event.data?.response === 'string') {
                            if (this._upgrade) return; // Production five-second watchdog retains real initial Fast.
                            if (this._explicitExact && probe.holdExact) {
                                probe.pending = () => { if (this._handler) handler.call(this, event); };
                                return;
                            }
                        }
                        handler.call(this, event);
                    } : null;
                }
                get onmessage() { return this._handler; }
                postMessage(message, transfer) {
                    if (typeof message?.json === 'string') {
                        this._upgrade = message.fastStage === 'upgrade';
                        this._explicitExact = !message.fastStage && JSON.parse(message.json).Options.Mode === 2;
                        if (!this._upgrade) probe.requests.push(JSON.parse(message.json));
                    }
                    if (transfer === undefined) super.postMessage(message);
                    else super.postMessage(message, transfer);
                }
            };
        });
        await page.goto(process.argv[2] || 'http://127.0.0.1:5080');
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        const target = { Name: 'Target', Source: 'User' };
        const card = (id, name, copies, extra = {}) => ({ Id: id, Name: name, Copies: copies, Categories: [], Active: true, ...extra });
        const session = {
            SchemaVersion: 10, CalculationModel: 'Fast', HandSize: 2, Categories: [target],
            Cards: [card('s', 'Search target', 1, { Search: { Selector: { Kind: 'Category', Categories: ['user:Target'] }, ObjectiveMetadataOnly: false } }),
                card('a', 'Target', 1, { Categories: [target] }), card('f', 'Filler', 2),
                card('d', 'Draw and banish', 1, { DrawCount: 1, PreDrawDeckRemovalCount: 1 })],
            ComboGroups: [{ Id: 'empty-first', Name: 'Empty first' }, { Id: 'g', Name: 'Target routes' },
                { Id: 'empty-middle', Name: 'Empty middle' }, { Id: 'zero', Name: 'Active zero' }, { Id: 'empty-last', Name: 'Empty last' }],
            Combos: [{ Name: 'Target route', Active: true, GroupId: 'g', Categories: [{ BaseCategory: target, MinCount: 1, MaxCount: 6 }] },
                { Name: 'Impossible route', Active: true, GroupId: 'zero', Categories: [], Cards: [{ CardId: 'd', MinCount: 3, MaxCount: 6 }] }]
        };
        await page.locator('#sessionFileInput').setInputFiles({ name: 'result-layout.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(session)) });
        await page.waitForFunction(() => document.querySelectorAll('.card-editor').length === 4);
        const calculate = () => page.getByRole('button', { name: 'Calculate', exact: true }).click();
        const retry = () => page.getByRole('button', { name: 'Recalculate using Exact', exact: true });
        await calculate();
        await retry().waitFor({ timeout: 30000 });
        await page.getByRole('button', { name: 'Pin result', exact: true }).click();
        await calculate();
        await retry().waitFor({ timeout: 30000 });
        const fastPin = await page.locator('.pinned-result').innerText();
        const fastCurrent = await page.locator('.probability-results .probability-total-value').innerText();
        for (const selector of ['.probability-results', '.pinned-result']) {
            const text = await page.locator(selector).innerText();
            assert(!text.includes('Empty first') && !text.includes('Empty middle') && !text.includes('Empty last'));
            assert(text.includes('Active zero') && text.includes('0.00%'));
            assert(text.includes('Mode: Effects — fast'));
        }
        const geometry = () => page.evaluate(() => {
            const bounds = selector => {
                const r = document.querySelector(selector).getBoundingClientRect();
                return { x: r.x, y: r.y + scrollY, width: r.width, height: r.height };
            };
            return { current: bounds('.probability-results'), pinned: bounds('.pinned-result'),
                toolbar: bounds('.session-controls .calculation-controls'), slot: bounds('.calculation-feedback-slot'),
                overflow: document.documentElement.scrollWidth - innerWidth };
        });
        const baselines = new Map();
        const states = [];
        for (const theme of ['Light', 'Dark']) {
            for (const width of [1440, 768, 390, 320]) {
                await page.getByLabel('Color theme preference').selectOption({ label: theme });
                await page.setViewportSize({ width, height: 1000 });
                const before = await geometry();
                baselines.set(`${width}/${theme}`, before);
                assert(before.overflow <= 0);
                assert.equal(await page.locator('.exact-retry-confirmation, #exactRetryWarning').count(), 0);
                assert.equal(await page.locator('.pinned-result').innerText(), fastPin);
                if ((width === 1440 && theme === 'Light') || (width === 390 && theme === 'Dark')) {
                    await page.locator('.results-section').screenshot({ path: path.join(evidence, `${width}-${theme}-fast.png`) });
                }
                states.push({ width, theme, before });
            }
        }
        await retry().click();
        await page.getByRole('button', { name: 'Cancel calculation', exact: true }).waitFor();
        await page.waitForFunction(() => !!__exactRetryProbe.pending);
        assert.equal(await retry().count(), 0);
        const requests = await page.evaluate(() => __exactRetryProbe.requests);
        assert.equal(requests.length, 3);
        assert.equal(requests[2].Options.Mode, 2);
        requests[0].Options.Mode = 2;
        assert.deepEqual(requests[2], requests[0]);
        assert.equal(await page.locator('.probability-results .probability-total-value').innerText(), fastCurrent);
        const pinnedFast = await page.locator('.pinned-result').innerText();
        assert.equal(pinnedFast, fastPin, 'Exact leaves the existing manual pin unchanged');
        assert.equal(await page.locator('.calculate-action > button').evaluate(element => element === document.activeElement), true);
        await page.waitForTimeout(7200);
        assert.equal(await page.getByRole('button', { name: 'Cancel calculation', exact: true }).count(), 1,
            'Explicit Exact stays running beyond the automatic opportunity');
        assert.equal(await page.getByRole('button', { name: 'Calculate with no effect simulation', exact: true }).count(), 0);
        for (const theme of ['Light', 'Dark']) {
            for (const width of [1440, 768, 390, 320]) {
                await page.getByLabel('Color theme preference').selectOption({ label: theme });
                await page.setViewportSize({ width, height: 1000 });
                const running = await geometry();
                const before = baselines.get(`${width}/${theme}`);
                assert.equal(running.current.y, before.current.y, `${width}/${theme}: current stays fixed`);
                assert.equal(running.pinned.y, before.pinned.y, `${width}/${theme}: pin stays fixed`);
                assert.deepEqual(running.toolbar, before.toolbar);
                assert(running.overflow <= 0);
                states.push({ width, theme, running });
            }
        }
        await page.evaluate(() => { __exactRetryProbe.holdExact = false; __exactRetryProbe.pending(); });
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        assert.equal((await page.locator('.probability-results .result-accuracy').innerText()).trim(), 'Mode: Effects — exact');
        assert.equal(await page.locator('.pinned-result').innerText(), pinnedFast);
        assert.equal(await retry().count(), 0);
        assert.equal(await page.locator('#calculationModel').inputValue(), 'Fast');
        await page.getByRole('button', { name: 'Copy results', exact: true }).click();
        const copied = await page.evaluate(() => __exactRetryProbe.copied.at(-1));
        assert(copied.includes('Mode: Effects — exact') && !copied.includes('Empty first') && copied.includes('Active zero'));
        for (const [width, theme] of [[1440, 'Light'], [390, 'Dark']]) {
            await page.setViewportSize({ width, height: 1000 });
            await page.getByLabel('Color theme preference').selectOption({ label: theme });
            await page.locator('.results-section').screenshot({ path: path.join(evidence, `${width}-${theme}-exact.png`) });
        }
        await page.getByRole('button', { name: 'Unpin result', exact: true }).click();
        await calculate();
        await retry().waitFor({ timeout: 30000 });
        await retry().click();
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        assert.equal(await page.locator('.pinned-result').count(), 0, 'Exact does not create a pin');
        assert.deepEqual(errors, []);
        await fs.writeFile(path.join(evidence, 'geometry.json'), JSON.stringify(states, null, 2));
        console.log(`Exact retry browser smoke passed: real Fast deadline fallback, unchanged manual pin and no automatic pin, explicit Exact beyond seven seconds, success/copy, 8 viewport/theme combinations, no shifts/overflow. Evidence: ${evidence}`);
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
