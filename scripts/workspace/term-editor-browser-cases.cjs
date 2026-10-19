const assert = require('node:assert/strict');

// Shared by the existing operator smoke so term structure and its interactions
// run against the same app, browser, themes and responsive overflow checks.
module.exports = async ({ page, load, setLayout, saveSession, capture, assertNoOverflow, activate, session }) => {
    const categories = ['A', 'B', 'C', 'D', 'E'].map(letter => ({ Name: `${letter} property with a long label for responsive wrapping`, Source: 'User' }));
    const category = index => ({ BaseCategory: categories[index], MinCount: 1, MaxCount: 3, MaximumMode: 'HandSize' });
    const same = (...indices) => ({ Selector: { Kind: 'AllOf', Categories: indices.map(index => `user:${categories[index].Name}`) }, CategoryBases: indices.map(index => categories[index]), MinCount: 1, MaxCount: 3, MaximumMode: 'HandSize' });
    const leaf = index => ({ Kind: 'Category', Category: category(index) });
    const group = (...indices) => ({ Alternatives: indices.map(leaf) });
    const combos = [
        { Categories: [category(0), category(1), category(2)] },
        { Categories: [same(0, 1, 2)] },
        { AlternativeGroups: [group(0, 1, 2)] },
        { Categories: [same(0, 1)], AlternativeGroups: [group(2, 3)] },
        { Categories: [category(0), category(3)], AlternativeGroups: [group(1, 2)], RequirementOrder: [
            { Kind: 'Category', Index: 0 }, { Kind: 'Group', Index: 0 }, { Kind: 'Category', Index: 1 }
        ] },
        { AlternativeGroups: [group(0, 1), group(2, 3)] },
        { AlternativeGroups: [{ Alternatives: [{ Kind: 'Category', Category: same(0, 1) }, { Kind: 'Category', Category: same(2, 3) }] }] }
    ].map((combo, index) => ({ Name: index === 0 ? 'Operator review combo' : `Term case ${index}`, Active: true, Categories: [], Cards: [], AlternativeGroups: [], ...combo }));
    const property = index => ({ Kind: 'Category', Categories: [`user:${categories[index].Name}`] });
    const and = (...indices) => ({ Kind: 'AllOf', Categories: indices.map(index => `user:${categories[index].Name}`) });
    const or = (...indices) => ({ Kind: 'AnyOf', Categories: indices.map(index => `user:${categories[index].Name}`) });
    const selectors = [and(0, 1, 2), or(0, 1, 2),
        { Kind: 'AllOf', Children: [or(0, 1), or(2, 3)] },
        { Kind: 'AnyOf', Children: [and(0, 1), and(2, 3)] },
        { Kind: 'AnyOf', Children: [property(0), and(1, 2)] },
        { Kind: 'AllOf', Children: [property(0), property(1), or(2, 3)] }
    ];
    const fixture = { ...session(), Categories: categories, Combos: combos, Cards: selectors.map((selector, index) => ({
        Id: `term-card-${index}`, Name: `Selector case ${index}`, Copies: 1, Active: true, Categories: categories,
        DrawCount: 1, PostDrawDiscardCount: 1, PostDrawSelector: selector
    })) };
    const expectedCombos = ['A;B;C', 'ABC', 'ABC', 'AB;CD', 'A;BC;D', 'AB;CD', 'AB;CD'];
    const expectedSelectors = ['ABC', 'ABC', 'AB;CD', 'AB;CD', 'A;BC', 'AB;CD'];
    await load(fixture);
    const original = await saveSession();
    const readTerms = rows => rows.evaluateAll(elements => elements.map(row => [...row.querySelectorAll('.requirement-description .category-tag')]
        .map(chip => chip.textContent.trim().charAt(0)).join('')).join(';'));
    for (const width of [1280, 768, 390, 320]) {
        for (const theme of ['light', 'dark']) {
            await setLayout(width, theme);
            for (let index = 0; index < combos.length; index++) {
                const combo = page.locator('.combo-editor').nth(index);
                if (await combo.locator('.accordion-button').getAttribute('aria-expanded') !== 'true') { await combo.locator('.accordion-button').click(); }
                const rows = combo.locator('.expanded-expression .requirement-row');
                assert.equal(await rows.count(), expectedCombos[index].split(';').length, `combo ${index} at ${width}/${theme}`);
                assert.equal(await readTerms(rows), expectedCombos[index]);
                await activate(rows.first().locator('.requirement-edit'));
                assert.equal(await combo.locator('.requirement-row form').count(), 1);
                await assertNoOverflow(`editing combo ${index}/${width}/${theme}`);
                if ([2, 3, 6].includes(index)) { await capture(`term-combo-${index}-${width}-${theme}`); }
                await combo.locator('.requirement-row form').getByRole('button', { name: 'Cancel', exact: true }).click();
                await combo.locator('.accordion-button').click();
                await assertNoOverflow(`collapsed combo ${index}/${width}/${theme}`);
            }
            for (let index = 0; index < selectors.length; index++) {
                const card = page.locator('.card-editor').filter({ hasText: `Selector case ${index}` });
                if (await card.locator('.accordion-button').getAttribute('aria-expanded') !== 'true') { await card.locator('.accordion-button').click(); }
                await card.locator('.simulation-edit').click();
                const rows = card.locator('.selector-expression .requirement-row');
                assert.equal(await rows.count(), expectedSelectors[index].split(';').length, `selector ${index}/${width}/${theme}`);
                assert.equal(await readTerms(rows), expectedSelectors[index]);
                const outer = card.locator('[data-selector-path="root"] > .requirement-or-group > .requirement-and-separator select, [data-selector-path="root"] > .requirement-or-group > .alternative-separator select');
                assert.deepEqual(await outer.evaluateAll(elements => elements.map(element => element.value)), index < 2 ? [] : [index === 3 || index === 4 ? 'OR' : 'AND']);
                await activate(rows.first().locator('.requirement-edit'));
                assert.equal(await card.locator('.selector-expression form .selector-operand').count(), expectedSelectors[index].split(';')[0].length);
                await assertNoOverflow(`editing selector ${index}/${width}/${theme}`);
                if ([1, 2, 3].includes(index)) { await capture(`term-selector-${index}-${width}-${theme}`); }
                await card.locator('.selector-expression form').getByRole('button', { name: 'Cancel', exact: true }).click();
                await card.locator('.simulation-editor').getByRole('button', { name: 'Cancel', exact: true }).click();
                await card.locator('.accordion-button').click();
            }
        }
    }
    // A flat field becoming nested must focus a live field, rather than the
    // removed row button, and Cancel must still restore the complete simulation.
    const card = page.locator('.card-editor').filter({ hasText: 'Selector case 0' });
    await card.locator('.accordion-button').click();
    await card.locator('.simulation-edit').click();
    await card.locator('.selector-expression .requirement-edit').click();
    await card.locator('.selector-expression form').getByRole('button', { name: 'Add alternative (OR)', exact: true }).click();
    await card.locator('.selector-expression form .selector-operand select').selectOption(`user:${categories[3].Name}`);
    await card.locator('.selector-expression form button[type="submit"]').click();
    assert.equal(await card.locator('.selector-expression .requirement-row').count(), 2);
    assert.equal(await card.locator('.selector-expression .requirement-edit').first().evaluate(element => element === document.activeElement), true);
    await card.locator('.simulation-editor').getByRole('button', { name: 'Cancel', exact: true }).click();
    await card.locator('.accordion-button').click();
    const after = await saveSession();
    assert.deepEqual(after.Combos, original.Combos, 'term presentation does not change combo models');
    assert.deepEqual(after.Cards, original.Cards, 'term presentation does not change saved selectors');
    await setLayout(1280, 'light');
    console.log('PASS term fields: every combo/selector example at 1280/768/390/320 in light/dark, editing and collapsed.');
};
