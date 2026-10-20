// Production worker replies are held only at the UI transport boundary, never replaced.
// NODE_PATH=<playwright> YGO_BROWSER_EXECUTABLE=<chromium> node scripts/workspace/numeric-result-polish-smoke.cjs <published-wwwroot> <evidence-directory>
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const { serve } = require('../background-calculation/measure-worker-lifecycle.cjs');

async function main() {
    const [root, destination] = process.argv.slice(2);
    const output = path.resolve(destination);
    await fs.mkdir(output, { recursive: true });
    const { server, url } = await serve(root);
    let browser;
    try {
        browser = await chromium.launch({ headless: true, executablePath: process.env.YGO_BROWSER_EXECUTABLE });
        const page = await browser.newPage({ viewport: { width: 1280, height: 1000 }, locale: 'en-US' });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.addInitScript(() => {
            window.polish = { pending: null, copied: [], hold: true, replies: [] };
            Object.defineProperty(navigator, 'clipboard', { value: { writeText: async value => polish.copied.push(value) } });
            const NativeWorker = Worker;
            window.Worker = class extends NativeWorker {
                postMessage(message, ...rest) {
                    this.upgrade = message.fastStage === 'upgrade' || message.fastStage === 'progressive-exact-v1';
                    return super.postMessage(message, ...rest);
                }
                set onmessage(handler) {
                    this.handler = handler;
                    super.onmessage = handler ? event => {
                        if (typeof event.data?.response === 'string') {
                            polish.replies.push(JSON.parse(event.data.response));
                            if (this.upgrade && polish.hold) {
                                polish.pending = () => this.handler?.call(this, event);
                                return;
                            }
                        }
                        handler.call(this, event);
                    } : null;
                }
                get onmessage() { return this.handler; }
            };
        });
        await page.goto(url);
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        const target = { Name: 'Target', Source: 'User' };
        const metadata = key => ({ Name: key, Source: 'Metadata', MetadataKey: key });
        const card = (Id, Name, Copies, extra = {}) => ({ Id, Name, Copies, Categories: [], Active: true, ...extra });
        const session = {
            SchemaVersion: 12, CalculationModel: 'Fast', FastExecutionStrategy: 'Progressive', HandSize: 2,
            Categories: [target], Cards: [
                card('s', 'Searcher', 1, { Search: { Selector: { Kind: 'Category', Categories: ['user:Target'] }, ObjectiveMetadataOnly: false } }),
                card('t', 'Target', 1, { Categories: [target, metadata('kind:monster'), metadata('level:4')] }),
                card('f', 'Filler', 2), card('d', 'Draw and banish', 1, { DrawCount: 1, PreDrawDeckRemovalCount: 1 })
            ],
            ComboGroups: [{ Id: 'g', Name: 'Target routes' }],
            Combos: [{ Name: 'Target route', Active: true, GroupId: 'g', Categories: [{ BaseCategory: target, MinCount: 1, MaxCount: 6 }] },
                { Name: 'Impossible route', Active: true, Categories: [], Cards: [{ CardId: 'd', MinCount: 3, MaxCount: 6 }] }]
        };
        await page.locator('#sessionFileInput').setInputFiles({ name: 'numeric-result-polish.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(session)) });
        await page.waitForFunction(() => document.querySelectorAll('.card-editor').length === 4);
        // Obtain one accepted Exact result as a stable sibling pin.
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.waitForFunction(() => !!polish.pending);
        await page.evaluate(() => { polish.pending(); polish.pending = null; });
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        await page.getByRole('button', { name: 'Pin result', exact: true }).click();
        const pin = await page.locator('.pinned-result').innerText();
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.waitForFunction(() => !!polish.pending);
        const geometry = () => page.evaluate(() => {
            const box = element => { const r = element.getBoundingClientRect(); return { x: r.x, y: r.y + scrollY, width: r.width, height: r.height }; };
            const anchors = ['#probabilityResultsHeading', '.pin-result-action', '.probability-results .probability-total', '.probability-group', '.pinned-result', '.probability-result-status-slot'];
            const compact = [...document.querySelectorAll('.probability-zero-uncertainty')].map(element => {
                const value = element.querySelector('.combo-probability-value');
                const accuracy = element.querySelector('.probability-uncertainty');
                const vr = value.getBoundingClientRect(), ar = accuracy.getBoundingClientRect();
                const valueText = document.createRange(), accuracyText = document.createRange();
                valueText.selectNodeContents(value); accuracyText.selectNodeContents(accuracy);
                const textGap = accuracyText.getBoundingClientRect().left - valueText.getBoundingClientRect().right;
                return { ...box(element), textGap, gap: ar.x - vr.right, expectedGap: parseFloat(getComputedStyle(element).columnGap), valueMin: getComputedStyle(value).minWidth, uncertaintyMin: getComputedStyle(accuracy).minWidth };
            });
            const marker = document.querySelector('.probability-result-status-preliminary');
            const style = marker && getComputedStyle(marker);
            const rgb = value => value.match(/[\d.]+/g).slice(0, 3).map(Number);
            const luminance = color => rgb(color).map(value => { value /= 255; return value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4; }).reduce((sum, value, index) => sum + value * [.2126, .7152, .0722][index], 0);
            let warning = null;
            if (style) {
                const a = luminance(style.color), b = luminance(style.backgroundColor);
                warning = { color: style.color, background: style.backgroundColor, contrast: (Math.max(a, b) + .05) / (Math.min(a, b) + .05) };
            }
            return { anchors: Object.fromEntries(anchors.map(selector => [selector, [...document.querySelectorAll(selector)].map(box)])),
                compact, warning, overflow: document.documentElement.scrollWidth - innerWidth, scroll: scrollY };
        });
        const records = [];
        for (const theme of ['Light', 'Dark']) for (const width of [320, 390, 768, 1280]) {
            await page.setViewportSize({ width, height: 1000 });
            await page.getByLabel('Color theme preference').selectOption({ label: theme });
            await page.locator('.results-section').scrollIntoViewIfNeeded();
            const before = await geometry();
            assert(before.overflow <= 0, `${width}/${theme}: overflow`);
            assert(before.warning?.contrast >= 4.5, `${width}/${theme}: warning contrast`);
            assert(before.compact.length >= 3);
            for (const row of before.compact) {
                assert.equal(row.valueMin, '0px'); assert.equal(row.uncertaintyMin, '0px');
                assert(row.textGap >= 0 && Math.abs(row.textGap - row.expectedGap) < 1, 'zero-uncertainty visible text gap and no overlap');
            }
            await page.locator('.results-section').screenshot({ path: path.join(output, `${width}-${theme}-preliminary.png`) });
            records.push({ width, theme, before });
        }
        await page.evaluate(() => { polish.pending(); polish.pending = null; });
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        assert.equal(await page.locator('.probability-result-status-preliminary').count(), 0);
        assert.equal(await page.locator('.pinned-result').innerText(), pin);
        for (const record of records) {
            await page.setViewportSize({ width: record.width, height: 1000 });
            await page.getByLabel('Color theme preference').selectOption({ label: record.theme });
            await page.locator('.results-section').scrollIntoViewIfNeeded();
            const final = await geometry();
            assert(final.overflow <= 0);
            record.final = final;
            await fs.writeFile(path.join(output, 'geometry-in-progress.json'), JSON.stringify(records, null, 2));
            await page.locator('.results-section').screenshot({ path: path.join(output, `${record.width}-${record.theme}-exact.png`) });
            for (const [selector, boxes] of Object.entries(record.before.anchors)) boxes.forEach((box, index) => {
                const after = final.anchors[selector][index];
                assert(Math.abs(box.y - after.y) < 1, `${record.width}/${record.theme}: ${selector} moved vertically`);
                if (selector.includes('Heading') || selector.includes('status-slot') || selector.includes('pin-result')) {
                    assert(Math.abs(box.x - after.x) < 1, `${selector} shifted horizontally`);
                }
            });
            await page.locator('.results-section').screenshot({ path: path.join(output, `${record.width}-${record.theme}-exact.png`) });
            const previousSizing = await page.addStyleTag({ content: '.probability-with-accuracy .combo-probability-value { min-width: 7.5ch !important; } .probability-with-accuracy .probability-uncertainty { min-width: 7ch !important; } .probability-total .result-value-group { flex: 0 0 45% !important; }' });
            record.previousSizing = await geometry();
            if (record.width === 1280 || record.width === 320) await page.locator('.results-section').screenshot({ path: path.join(output, `${record.width}-${record.theme}-previous-sizing.png`) });
            await previousSizing.evaluate(element => element.remove());
            record.final = final;
        }
        await page.getByRole('button', { name: 'Copy results', exact: true }).click();
        const copied = await page.evaluate(() => polish.copied.at(-1));
        assert(copied.includes('Target route') && copied.includes('±0%') && !copied.includes('Preliminary'));
        await page.evaluate(() => document.documentElement.style.zoom = '2');
        assert((await geometry()).overflow <= 0, '200% zoom overflow');
        await page.evaluate(() => document.documentElement.style.zoom = '');
        const proof = page.locator('.probability-results .probability-zero-uncertainty').first();
        await proof.focus();
        assert(await proof.locator('.probability-proof').isVisible());
        assert((await geometry()).overflow <= 0, 'focused proof overflow');
        await page.getByRole('button', { name: 'Calculate', exact: true }).focus();
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.waitForFunction(() => !!polish.pending);
        await page.getByRole('button', { name: 'Cancel Exact upgrade', exact: true }).click();
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        assert.equal(await page.locator('.probability-result-status-preliminary').count(), 0);
        assert.equal(await page.locator('.pinned-result').innerText(), pin);
        // The category-rule property picker shares the same authoring choices.
        await page.locator('details.category-management > summary').click();
        await page.locator('[aria-label="Edit category Target"]').click();
        await page.getByRole('button', { name: 'Define by properties', exact: true }).click();
        const rule = page.locator('.category-rule-editor');
        await rule.getByLabel('Add rule alternative', { exact: true }).selectOption('numeric:level');
        const numeric = rule.locator('.category-rule-numeric');
        await numeric.getByLabel('Comparison', { exact: true }).selectOption('range');
        await numeric.getByLabel('Minimum Level', { exact: true }).fill('3');
        await numeric.getByLabel('Maximum Level', { exact: true }).fill('5');
        await rule.getByLabel('Add AND property to alternative 1', { exact: true }).selectOption('metadata:attribute:fire');
        await numeric.getByLabel('Maximum Level', { exact: true }).fill('2');
        assert(await rule.getByRole('button', { name: 'Apply rule', exact: true }).isDisabled());
        await rule.getByRole('button', { name: /Remove property.*FIRE.*alternative 1/i }).click();
        assert.equal(await numeric.getByLabel('Maximum Level', { exact: true }).inputValue(), '2');
        await numeric.getByLabel('Maximum Level', { exact: true }).fill('5');
        assert.equal(await rule.locator('option[value^="metadata:level:"], option[value^="metadata:rank:"], option[value^="metadata:link:"]').count(), 0);
        for (const width of [320, 390, 768, 1280]) {
            await page.setViewportSize({ width, height: 1000 });
            assert((await geometry()).overflow <= 0, `numeric rule at ${width}`);
            await rule.screenshot({ path: path.join(output, `${width}-numeric-rule.png`) });
        }
        await rule.getByRole('button', { name: 'Apply rule', exact: true }).click();
        await page.locator('[aria-label="Edit category Target"]').click();
        assert((await page.locator('.category-rule-summary').innerText()).includes('Level 3–5'));
        assert.deepEqual(errors, []);
        await fs.writeFile(path.join(output, 'geometry.json'), JSON.stringify({ browser: browser.version(), records, errors }, null, 2));
        console.log(`Numeric/result polish smoke passed; ${records.length} viewport/theme pairs, real Exact/pin/copy, compact gaps, warning contrast, stable anchors, numeric draft/removal/apply and 200% zoom. Evidence: ${output}`);
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
