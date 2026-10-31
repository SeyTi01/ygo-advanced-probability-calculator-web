// Focused #266 regression, sharing the integration smoke's real worker and gate.
const assert = require('node:assert/strict');
const { load, mode } = require('./browser-support.cjs');

module.exports = async function resultLayout(page, source, { calculate, hold, release }) {
    const session = structuredClone(source);
    session.Cards[0].Name = 'Layout search target';
    session.ComboGroups = [
        { Id: 'g', Name: 'Long search and draw routes with overlapping target requirements and access' },
        { Id: 'later', Name: 'Subsequent target routes with independent group boundaries' }
    ];
    const route = session.Combos[0];
    session.Combos = [
        { ...route, Name: 'Long grouped combo requiring a target after search and draw effects with alternate access' },
        { ...route, Name: 'Later grouped combo requiring the same physical target', GroupId: 'later' },
        { ...route, Name: 'Long ungrouped combo requiring a target after all configured effects', GroupId: null }
    ];
    await page.getByRole('button', { name: 'Unpin result', exact: true }).click();
    await page.evaluate(() => { smoke.hold = null; smoke.held = []; });
    await load(page, session);
    await mode(page, 'FastOnly');
    await calculate();
    await page.getByRole('button', { name: 'Pin result', exact: true }).click();
    const pin = await page.locator('.pinned-result').innerText();
    await mode(page, 'Fast');

    async function geometry() {
        return page.evaluate(() => {
            const rect = element => {
                const box = element.getBoundingClientRect();
                return { x: box.x + scrollX, y: box.y + scrollY, width: box.width, height: box.height };
            };
            const rows = [...document.querySelectorAll('.combo-probability-row, .pinned-row')];
            const panels = [...document.querySelectorAll('.probability-results, .pinned-result')];
            return {
                panels: panels.map(rect),
                rows: rows.map(row => ({ row: rect(row), label: rect(row.firstElementChild), value: rect(row.lastElementChild) })),
                overflow: document.documentElement.scrollWidth > innerWidth + 1,
                metadataOverflow: panels.some(panel => [...panel.querySelectorAll('.result-accuracy, .result-model > span, .accuracy-details summary')]
                    .some(item => item.getBoundingClientRect().right > panel.getBoundingClientRect().right + 1)),
                overlap: rows.some(row => {
                    const label = row.firstElementChild.getBoundingClientRect();
                    const value = row.lastElementChild.getBoundingClientRect();
                    return label.right > value.left + 1 && label.bottom > value.top + 1 && value.bottom > label.top + 1;
                }),
                clipped: rows.some(row => [...row.querySelectorAll('.combo-probability-value, .probability-uncertainty')]
                    .some(value => value.scrollWidth > value.clientWidth + 1))
            };
        });
    }
    const evidence = [];
    // Full acceptance matrix is opt-in; retain two wrap-sensitive cases in the smoke.
    const cases = process.env.YGO_LAYOUT_MATRIX ? [1280, 768, 390, 320].flatMap(width =>
        ['Light', 'Dark'].flatMap(theme => (width >= 768 ? [1, 2] : [1]).map(zoom => ({ width, theme, zoom }))))
        : [{ width: 1280, theme: 'Light', zoom: 1 }, { width: 1280, theme: 'Dark', zoom: 2 }];
    for (const entry of cases) {
        const { width, theme, zoom } = entry;
        // Equivalent CSS viewport to desktop browser zoom (not text-only zoom).
        await page.setViewportSize({ width: width / zoom, height: 900 });
        await page.getByLabel('Color theme preference').selectOption({ label: theme });
        await hold('progressive-exact-v1');
        const fast = await page.evaluate(() => smoke.requests.findLast(request => request.response).response.Result);
        assert.equal(fast.Contract.Accuracy, 1, 'layout samples a real bounded Fast result');
        const before = await geometry();
        await release();
        const exact = await page.evaluate(() => smoke.requests.at(-1).response.Result);
        assert.equal(exact.Contract.Accuracy, 0, 'layout completes physical Exact');
        for (const value of await page.locator('.probability-results .probability-with-accuracy').all()) {
            assert.match(await value.getAttribute('title'), /(?<!\d)23\/30(?!\d)/);
        }
        const after = await geometry();
        assert.equal(await page.locator('.pinned-result').innerText(), pin, 'upgrade preserves manual Fast snapshot');
        const result = { ...entry, before, after, fastContract: fast.Contract, exactContract: exact.Contract };
        evidence.push(result);
        if (process.env.YGO_LAYOUT_EVIDENCE) {
            require('node:fs').writeFileSync(process.env.YGO_LAYOUT_EVIDENCE, JSON.stringify(evidence, null, 2));
        }
        for (const [stage, boxes] of [['Fast', before], ['Exact', after]]) {
            assert.equal(boxes.overflow, false, `${stage}: page fits ${width}px / ${zoom}x`);
            assert.equal(boxes.metadataOverflow, false, `${stage}: metadata fits its current or pinned panel`);
            assert.equal(boxes.overlap, false, `${stage}: labels and values do not overlap`);
            assert.equal(boxes.clipped, false, `${stage}: values remain readable`);
        }
        for (let index = 0; index < before.rows.length; index++) {
            for (const part of ['row', 'label']) {
                for (const dimension of ['y', 'width', 'height']) {
                    assert(Math.abs(before.rows[index][part][dimension] - after.rows[index][part][dimension]) <= 1,
                        `row ${index} ${part}.${dimension} changed at ${width}px / ${zoom}x: `
                        + `${before.rows[index][part][dimension]} -> ${after.rows[index][part][dimension]}`);
                }
            }
        }
        for (let index = 0; index < before.panels.length; index++) {
            assert(Math.abs(before.panels[index].y - after.panels[index].y) <= 1, 'panel position stays stable');
        }
        // Reach the value through the keyboard and check that its evidence
        // wraps inside the panel, keeps right alignment, and never covers labels.
        for (const value of await page.locator('.result-panels .probability-with-accuracy').all()) {
            const panel = value.locator('xpath=ancestor::section[contains(@class,"probability-results") or contains(@class,"pinned-result")]');
            await value.focus();
            await page.keyboard.press('Tab');
            await page.keyboard.press('Shift+Tab');
            assert.equal(await value.evaluate(element => element === document.activeElement), true);
            const proof = value.locator('.probability-proof');
            assert.equal(await proof.isVisible(), true);
            const proofBox = await proof.boundingBox();
            const panelBox = await panel.boundingBox();
            const valueBox = await value.boundingBox();
            assert(proofBox.x >= panelBox.x && proofBox.x + proofBox.width <= panelBox.x + panelBox.width + 1);
            assert(Math.abs(proofBox.x + proofBox.width - valueBox.x - valueBox.width) <= 1, 'proof stays right aligned');
            assert.equal((await geometry()).overflow, false);
            assert.equal((await geometry()).overlap, false, 'focused proof does not cover labels');
            assert.equal(await proof.evaluate(element => element.scrollWidth <= element.clientWidth + 1), true, 'proof wraps legibly');
            await value.press('Escape');
            await value.evaluate(element => element.blur());
        }
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        if (process.env.YGO_LAYOUT_SCREENSHOT && width === 1280 && zoom === 1) {
            await page.locator('.result-panels').screenshot({ path: `${process.env.YGO_LAYOUT_SCREENSHOT}-${theme}.png` });
        }
    }
    console.log(`Result layout: ${cases.length} real Fast → Exact geometry comparisons passed.`);
    return evidence;
};
