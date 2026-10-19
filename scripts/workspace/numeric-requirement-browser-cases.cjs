const assert = require('node:assert/strict');

module.exports = async ({ page, load, setLayout, saveSession, capture, assertNoOverflow, session }) => {
    for (const property of ['Level', 'Rank', 'Link']) {
        const fixture = { ...session(), Categories: [], Combos: [{ Name: 'Operator review combo', Active: true, Categories: [], Cards: [], AlternativeGroups: [] }], Cards: [{
            Id: 'numeric-review', Name: 'Numeric review card', Copies: 1, Active: true, Categories: [], DrawCount: 1, PostDrawDiscardCount: 1,
            PostDrawSelector: { Kind: 'LevelAtLeast', Minimum: 5 }
        }] };
        const combo = await load(fixture);
        await combo.locator('[data-new-requirement]').waitFor();
        await combo.locator('[data-new-requirement]').click();
        const card = page.locator('.card-editor').filter({ hasText: 'Numeric review card' });
        await card.locator('.accordion-button').click();
        await card.locator('.simulation-edit').click();
        await card.locator('.selector-expression .requirement-edit').click();
        const comboForm = combo.locator('.requirement-form');
        const simulationForm = card.locator('.selector-expression .requirement-form');
        for (const form of [comboForm, simulationForm]) {
            const options = await form.locator('.requirement-property-picker > select option').evaluateAll(elements => elements.map(element => element.value));
            for (const key of ['level', 'rank', 'link']) {
                assert.equal(options.filter(value => value === `numeric:${key}`).length, 1);
                assert.equal(options.some(value => value.startsWith(`metadata:${key}:`)), false);
            }
            await form.locator('.requirement-property-picker > select').selectOption(`numeric:${property.toLowerCase()}`);
            assert.equal(await form.getByLabel('Comparison', { exact: true }).inputValue(), 'exact');
            await form.getByLabel(property, { exact: true }).fill('5');
            await form.getByLabel('Comparison', { exact: true }).selectOption('maximum');
            await form.getByLabel(`Maximum ${property}`, { exact: true }).fill('5');
            await form.getByLabel('Comparison', { exact: true }).selectOption('minimum');
            await form.getByLabel(`Minimum ${property}`, { exact: true }).fill('3');
            await form.getByLabel('Comparison', { exact: true }).selectOption('range');
            await form.getByLabel(`Minimum ${property}`, { exact: true }).fill('3');
            await form.getByLabel(`Maximum ${property}`, { exact: true }).fill('5');
        }
        assert.equal(await simulationForm.locator('.requirement-quantity').count(), 0);
        assert.equal(await comboForm.locator('.requirement-quantity').count(), 1);
        await comboForm.locator('#minCount0').fill('2');
        for (const width of [1280, 768, 390, 320]) {
            for (const theme of ['light', 'dark']) {
                await setLayout(width, theme);
                const styles = await page.locator('.requirement-form .requirement-property-picker > select').evaluateAll(elements => elements.map(element => {
                    const style = getComputedStyle(element);
                    return [style.fontSize, style.padding, element.getBoundingClientRect().height];
                }));
                assert.equal(styles.length, 2);
                assert.deepEqual(styles[0], styles[1], 'combo and simulation use the same property field styling');
                await assertNoOverflow(`numeric ${property}/${width}/${theme}`);
                if (width === 1280 || width === 320) { await capture(`numeric-${property}-${width}-${theme}`); }
            }
        }
        await simulationForm.getByLabel(`Maximum ${property}`, { exact: true }).fill('2');
        assert.equal(await simulationForm.locator('.requirement-submit button').isDisabled(), true);
        await simulationForm.getByLabel(`Maximum ${property}`, { exact: true }).fill('5');
        await simulationForm.locator('.requirement-submit button').click();
        await card.locator('.simulation-save').click();
        await comboForm.locator('.requirement-submit button').click();
        const saved = await saveSession();
        const expected = { Kind: 'NumericRange', Property: property, Minimum: 3, Maximum: 5, ObjectiveOnly: false };
        assert.deepEqual(saved.Combos[0].Categories[0].Selector, expected);
        assert.equal(saved.Combos[0].Categories[0].MinCount, 2);
        assert.deepEqual(saved.Cards[0].PostDrawSelector, expected);
        assert.equal(saved.SchemaVersion, 12);
        await setLayout(1280, 'light');
    }
    console.log('PASS shared edit form: Level/Rank/Link ranges, single numeric options, optional combo quantities, invalid bounds, session saves, matching styles at 1280/768/390/320 in light/dark.');
};
