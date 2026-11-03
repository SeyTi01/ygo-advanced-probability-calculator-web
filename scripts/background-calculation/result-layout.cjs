// Browser-only readability checks for the shared current and pinned result rows.
// Probability, accuracy transitions and pin ownership are covered by the caller.
const assert = require('node:assert/strict');
const { load, mode } = require('./browser-support.cjs');

module.exports = async function resultLayout(page, source, { calculate }) {
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
    await load(page, session);
    await mode(page, 'FastOnly');
    await calculate();
    await page.getByRole('button', { name: 'Pin result', exact: true }).click();
    await mode(page, 'Exact');
    await calculate();

    async function readability() {
        return page.evaluate(() => {
            const rows = [...document.querySelectorAll('.combo-probability-row, .pinned-row')];
            const panels = [...document.querySelectorAll('.probability-results, .pinned-result')];
            return {
                rows: rows.length,
                panels: panels.length,
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

    function checkReadable(result, width) {
        assert.equal(result.panels, 2, 'current and pinned panels participate');
        assert(result.rows > 0, 'populated result rows participate');
        assert.equal(result.overflow, false, `page fits ${width}px`);
        assert.equal(result.metadataOverflow, false, 'metadata fits its panel');
        assert.equal(result.overlap, false, 'labels and values do not overlap');
        assert.equal(result.clipped, false, 'probability and uncertainty remain readable');
    }

    const evidence = [];
    for (const width of [1280, 390]) {
        await page.setViewportSize({ width, height: 900 });
        const result = await readability();
        checkReadable(result, width);
        // A representative disclosure in each panel checks browser keyboard/CSS
        // behavior; bUnit owns the accessible evidence text for every row kind.
        for (const selector of ['.probability-results .probability-with-accuracy', '.pinned-result .probability-with-accuracy']) {
            const value = page.locator(selector).first();
            await value.focus();
            await page.keyboard.press('Tab');
            await page.keyboard.press('Shift+Tab');
            assert.equal(await value.evaluate(element => element === document.activeElement), true);
            const proof = value.locator('.probability-proof');
            assert.equal(await proof.isVisible(), true, 'keyboard focus reveals evidence');
            assert.equal(await proof.evaluate(element => {
                const panel = element.closest('.probability-results, .pinned-result').getBoundingClientRect();
                const box = element.getBoundingClientRect();
                return box.left >= panel.left - 1 && box.right <= panel.right + 1
                    && element.scrollWidth <= element.clientWidth + 1;
            }), true, 'evidence wraps inside its panel');
            checkReadable(await readability(), width);
            await value.evaluate(element => element.blur());
        }
        evidence.push({ width, ...result });
    }
    console.log('Result layout: current/pinned rows and keyboard evidence remain readable on desktop/mobile.');
    return evidence;
};
