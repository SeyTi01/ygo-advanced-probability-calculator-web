// Check recovery feedback placement and lifecycle against a running local calculator app.
// Run: NODE_PATH=<Playwright node_modules> node scripts/workspace/calculation-feedback-layout-smoke.cjs [base-url]
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const baseUrl = process.argv[2] || 'http://127.0.0.1:5080';
    const samplePath = path.resolve(__dirname, '../../YGOProbabilityCalculatorBlazor/wwwroot/sample-data/example_session_state.json');
    const sampleBytes = await fs.readFile(samplePath);
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage();
    const screenshotDirectory = path.join(os.tmpdir(), 'calculation-feedback-layout-smoke');
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));

    try {
        await fs.mkdir(screenshotDirectory, { recursive: true });
        await page.addInitScript(() => {
            window.showSaveFilePicker = undefined;
            const NativeWorker = window.Worker;
            const probe = window.__calculationLayoutProbe = {
                calculationRequests: 0,
                delayedRequestId: null,
                delayedResponseHeld: false
            };

            window.Worker = class extends NativeWorker {
                constructor(...args) {
                    super(...args);
                    this._layoutOnMessage = null;
                    this._layoutTimers = new Set();
                    this._layoutTerminated = false;
                }

                set onmessage(handler) {
                    this._layoutOnMessage = handler;
                    super.onmessage = handler ? event => {
                        const data = event?.data;
                        if (data?.id === probe.delayedRequestId && typeof data.response === 'string') {
                            probe.delayedResponseHeld = true;
                            const timer = window.setTimeout(() => {
                                this._layoutTimers.delete(timer);
                                if (!this._layoutTerminated && this._layoutOnMessage) {
                                    this._layoutOnMessage.call(this, event);
                                }
                            }, 20000);
                            this._layoutTimers.add(timer);
                            return;
                        }
                        handler.call(this, event);
                    } : null;
                }

                get onmessage() {
                    return this._layoutOnMessage;
                }

                postMessage(message, transfer) {
                    if (message && typeof message.id === 'number' && typeof message.json === 'string'
                        && message.fastStage !== 'upgrade') {
                        probe.calculationRequests++;
                        if (probe.calculationRequests === 2) {
                            probe.delayedRequestId = message.id;
                        }
                    }

                    if (transfer === undefined) {
                        super.postMessage(message);
                    }
                    else {
                        super.postMessage(message, transfer);
                    }
                }

                terminate() {
                    this._layoutTerminated = true;
                    for (const timer of this._layoutTimers) {
                        window.clearTimeout(timer);
                    }
                    this._layoutTimers.clear();
                    super.terminate();
                }
            };
        });
        await page.goto(baseUrl);
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        await page.locator('#sessionFileInput').setInputFiles({
            name: 'workspace.json',
            mimeType: 'application/json',
            buffer: sampleBytes
        });
        await page.waitForFunction(() => document.querySelectorAll('.card-editor').length > 0);
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.locator('.probability-results').waitFor();
        await page.waitForFunction(() => !document.querySelector('.calculate-action button[aria-label="Cancel calculation"]'));
        await page.getByRole('button', { name: 'Pin result', exact: true }).click();
        await page.locator('.pinned-result').waitFor();
        const originalCurrentResult = await page.locator('.probability-results .probability-total-value').textContent();
        const originalPinnedResult = await page.locator('.pinned-result').evaluate(element => element.outerHTML);

        const geometry = async () => page.evaluate(() => {
            const bounds = selector => {
                const element = document.querySelector(selector);
                if (!element) throw new Error(`Missing ${selector}`);
                const rect = element.getBoundingClientRect();
                return { x: rect.x, y: rect.y + scrollY, width: rect.width, height: rect.height };
            };
            return {
                calculate: bounds('.calculate-action button'),
                slot: bounds('.calculation-feedback-slot'),
                toolbar: bounds('.session-controls .calculation-controls'),
                current: bounds('.probability-results'),
                pinned: bounds('.pinned-result'),
                overflow: document.documentElement.scrollWidth - innerWidth
            };
        });
        const addRecovery = async (targetSelector, insertBefore = false, legacy = false) => page.evaluate(({ selector, before, isLegacy }) => {
            const recovery = document.createElement('div');
            recovery.className = 'calculation-recovery mt-3 d-flex align-items-center gap-2';
            recovery.innerHTML = '<button class="btn btn-sm btn-outline-primary">Calculate with no effect simulation</button><button class="btn btn-sm btn-link">Review settings</button>';
            if (isLegacy) {
                recovery.style.flexWrap = 'wrap';
                recovery.style.setProperty('margin-top', '1rem', 'important');
            }
            const target = document.querySelector(selector);
            if (before) target.before(recovery);
            else target.append(recovery);
        }, { selector: targetSelector, before: insertBefore, isLegacy: legacy });
        const removeRecovery = async () => page.locator('.calculation-recovery').evaluate(element => element.remove());
        const baselines = new Map();
        const checkLegacyPlacement = async (width, theme) => {
            await page.getByLabel('Color theme preference').selectOption({ label: theme });
            await page.setViewportSize({ width, height: 1000 });
            const baseline = await geometry();
            assert.ok(baseline.overflow <= 0, `${width}px ${theme} has no baseline overflow`);
            baselines.set(`${width}/${theme}`, baseline);

            await addRecovery('.result-panels', true, true);
            const oldPlacement = await geometry();
            assert.ok(oldPlacement.current.y > baseline.current.y, `${width}px ${theme}: below-button feedback reproduces the result jump`);
            assert.ok(oldPlacement.pinned.y > baseline.pinned.y, `${width}px ${theme}: below-button feedback reproduces the pinned-result jump`);
            await removeRecovery();

            await addRecovery('.calculation-feedback-slot');
            const actionPlacement = await geometry();
            assert.equal(actionPlacement.current.y, baseline.current.y, `${width}px ${theme}: current result stays fixed beside Calculate`);
            assert.equal(actionPlacement.pinned.y, baseline.pinned.y, `${width}px ${theme}: pinned result stays fixed beside Calculate`);
            assert.ok(actionPlacement.overflow <= 0, `${width}px ${theme} has no horizontal overflow`);
            assert.ok(actionPlacement.slot.height >= 40, `${width}px ${theme}: the feedback slot is reserved before reveal`);
            assert.ok(actionPlacement.calculate.y + actionPlacement.calculate.height <= actionPlacement.slot.y || width > 767,
                `${width}px ${theme}: mobile feedback is stacked below Calculate`);
            const feedbackBounds = await page.locator('.calculation-feedback-slot .calculation-recovery').evaluate(element => {
                const slot = element.parentElement.getBoundingClientRect();
                const recovery = element.getBoundingClientRect();
                return { slotLeft: slot.left, slotRight: slot.right, slotBottom: slot.bottom,
                    left: recovery.left, right: recovery.right, bottom: recovery.bottom };
            });
            assert.ok(feedbackBounds.left >= feedbackBounds.slotLeft - 1 && feedbackBounds.right <= feedbackBounds.slotRight + 1,
                `${width}px ${theme}: feedback fits within the reserved slot width`);
            assert.ok(feedbackBounds.bottom <= feedbackBounds.slotBottom + 1,
                `${width}px ${theme}: feedback fits within the reserved slot height`);
            console.log(JSON.stringify({
                width,
                theme,
                simulatedLegacyShift: {
                    current: oldPlacement.current.y - baseline.current.y,
                    pinned: oldPlacement.pinned.y - baseline.pinned.y
                },
                baseline,
                simulatedActionPlacement: actionPlacement
            }));
            await removeRecovery();
        };

        for (const theme of ['Light', 'Dark']) {
            for (const width of [1440, 768, 390, 320]) {
                await checkLegacyPlacement(width, theme);
            }
        }

        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.getByRole('button', { name: 'Cancel calculation', exact: true }).waitFor();
        await page.waitForFunction(() => window.__calculationLayoutProbe.delayedResponseHeld);
        await page.locator('.calculation-feedback-slot .calculation-recovery').waitFor({ state: 'visible' });
        assert.equal(await page.evaluate(() => window.__calculationLayoutProbe.calculationRequests), 2,
            'The delayed result belongs to the second real worker calculation');
        assert.equal((await page.locator('.calculation-recovery .btn-outline-primary').textContent()).trim(),
            'Calculate with no effect simulation', 'The browser uses the longer recovery label');

        for (const theme of ['Light', 'Dark']) {
            for (const width of [1440, 768, 390, 320]) {
                await page.getByLabel('Color theme preference').selectOption({ label: theme });
                await page.setViewportSize({ width, height: 1000 });
                const actualPlacement = await geometry();
                const baseline = baselines.get(`${width}/${theme}`);
                assert.ok(baseline, `A baseline exists for ${width}px ${theme}`);
                assert.equal(actualPlacement.current.y, baseline.current.y,
                    `${width}px ${theme}: actual delayed feedback keeps the current result in place`);
                assert.equal(actualPlacement.pinned.y, baseline.pinned.y,
                    `${width}px ${theme}: actual delayed feedback keeps the pinned result in place`);
                assert.deepEqual(actualPlacement.toolbar, baseline.toolbar,
                    `${width}px ${theme}: the session toolbar keeps its geometry`);
                assert.ok(actualPlacement.overflow <= 0, `${width}px ${theme} has no horizontal overflow`);
                const recoveryBounds = await page.locator('.calculation-feedback-slot .calculation-recovery').evaluate(element => {
                    const slot = element.parentElement.getBoundingClientRect();
                    const recovery = element.getBoundingClientRect();
                    return {
                        slotLeft: slot.left,
                        slotRight: slot.right,
                        slotBottom: slot.bottom,
                        left: recovery.left,
                        right: recovery.right,
                        bottom: recovery.bottom
                    };
                });
                assert.ok(recoveryBounds.left >= recoveryBounds.slotLeft - 1
                    && recoveryBounds.right <= recoveryBounds.slotRight + 1,
                `${width}px ${theme}: actual recovery fits within the slot width`);
                assert.ok(recoveryBounds.bottom <= recoveryBounds.slotBottom + 1,
                    `${width}px ${theme}: actual recovery fits within the reserved slot height`);
                if ((width === 1440 && theme === 'Light') || (width === 390 && theme === 'Dark')) {
                    const screenshotPath = path.join(screenshotDirectory, `${width}px-${theme.toLowerCase()}-recovery.png`);
                    await page.screenshot({ path: screenshotPath, fullPage: true });
                    console.log(`Screenshot: ${screenshotPath}`);
                }
                console.log(JSON.stringify({ width, theme, actualDelayedPlacement: actualPlacement }));
            }
        }

        await page.getByRole('button', { name: 'Cancel calculation', exact: true }).click();
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        await page.locator('.calculation-feedback-slot .calculation-recovery').waitFor({ state: 'detached' });
        assert.equal(await page.locator('.probability-results .probability-total-value').textContent(),
            originalCurrentResult,
            'Cancel keeps the displayed current result available');
        assert.equal(await page.locator('.pinned-result').evaluate(element => element.outerHTML), originalPinnedResult,
            'Cancel keeps the pinned result available');

        assert.deepEqual(errors, [], `No browser errors: ${errors.join('; ')}`);
        console.log(`Calculation feedback layout smoke passed. Screenshot directory: ${screenshotDirectory}`);
    } finally {
        await context.close();
        await browser.close();
    }
})().catch(error => {
    console.error(error);
    process.exitCode = 1;
});
