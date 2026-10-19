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
    const stableCombo = await load({ ...fixture, Combos: [{ ...combos[0], Categories: [],
        AlternativeGroups: [{ Alternatives: [leaf(0), leaf(1)] }, { Alternatives: [leaf(2), leaf(3)] }] }] });
    await page.waitForFunction(() => document.querySelectorAll('.combo-editor .requirement-or-group').length === 2);
    if (await stableCombo.locator('.accordion-button').getAttribute('aria-expanded') !== 'true') { await stableCombo.locator('.accordion-button').click(); }
    const stableRows = stableCombo.locator('.expanded-expression .requirement-row');
    const stableContents = () => stableRows.evaluateAll(rows => rows.map(row => [...row.querySelectorAll('.requirement-description .category-tag')]
        .map(chip => chip.textContent.trim().charAt(0)).join('')));
    await stableCombo.locator('.expanded-expression > .requirement-and-separator select').selectOption('OR');
    assert.equal(await stableRows.count(), 2, '+ to OR retains existing fields');
    assert.deepEqual(await stableContents(), ['AB', 'CD']);
    await stableCombo.locator('.requirement-or-group > .alternative-separator select').selectOption('+');
    assert.deepEqual(await stableContents(), ['AB', 'CD'], 'OR to + retains each original group');
    assert.equal(await stableCombo.locator('.expanded-expression > .requirement-and-separator select').evaluate(element => element === document.activeElement), true,
        'returning to + keeps keyboard focus on the resulting separator');
    await stableCombo.locator('.expanded-expression > .requirement-and-separator select').selectOption('OR');
    await stableCombo.locator('[data-requirement="alternative:0:0"]').click();
    await stableCombo.getByRole('button', { name: 'Remove alternative', exact: true }).click();
    assert.deepEqual(await stableContents(), ['B', 'CD'], 'a partially deleted row remains separate');
    await stableCombo.locator('[data-requirement="alternative:0:0"]').click();
    await stableCombo.getByRole('button', { name: 'Remove alternative', exact: true }).click();
    assert.deepEqual(await stableContents(), ['CD'], 'only an emptied row disappears');
    console.log('PASS stable authored rows through +/OR round trips and partial/full row deletion.');
    // Measure actual chip/control geometry: inherited font sizes and margins
    // can look correct in markup while still differing between AND/OR fields.
    const shortCategories = categories.map((value, index) => ({ ...value, Name: String.fromCharCode(65 + index) }));
    const shortCategory = index => ({ ...category(index), BaseCategory: shortCategories[index] });
    const geometryCards = ['AllOf', 'AnyOf'].map((Kind, index) => ({
        Id: `geometry-${index}`, Name: `Selector geometry ${index}`, Copies: 1, Active: true, Categories: shortCategories,
        DrawCount: 1, PostDrawDiscardCount: 1, PostDrawSelector: { Kind, Categories: ['user:B', 'user:C'] }
    }));
    const geometryFixture = { ...fixture, Categories: shortCategories, Cards: geometryCards, Combos: [{
        Name: 'Operator review combo', Active: true,
        Categories: [shortCategory(0), { ...same(1, 2), CategoryBases: shortCategories.slice(1, 3), Selector: { Kind: 'AllOf', Categories: ['user:B', 'user:C'] } }],
        Cards: [], AlternativeGroups: [{ Alternatives: [3, 4].map(index => ({ Kind: 'Category', Category: shortCategory(index) })) }]
    }] };
    const geometryCombo = await load(geometryFixture);
    const measureRows = rows => rows.evaluateAll(elements => elements.map(row => {
        const box = element => { const rect = element.getBoundingClientRect(); return { left: rect.left, right: rect.right, center: rect.top + rect.height / 2, height: rect.height }; };
        const surface = row.querySelector('.requirement-interactive-summary') || row.querySelector('.requirement-edit');
        return {
            center: box(surface).center,
            badges: [...row.querySelectorAll('.category-tag')].map(element => ({ ...box(element), font: getComputedStyle(element).fontSize, margin: getComputedStyle(element).marginBottom })),
            operators: [...row.querySelectorAll('.expression-operator')].map(element => ({ ...box(element), style: Object.fromEntries(
                ['fontSize', 'fontWeight', 'lineHeight', 'padding', 'minHeight', 'minWidth'].map(property => [property, getComputedStyle(element)[property]])) }))
        };
    }));
    for (const width of [1280, 768]) {
        for (const theme of ['light', 'dark']) {
            await setLayout(width, theme);
            const measurements = await measureRows(geometryCombo.locator('.expanded-expression .requirement-row'));
            for (let index = 0; index < geometryCards.length; index++) {
                const card = page.locator('.card-editor').filter({ hasText: `Selector geometry ${index}` });
                await card.locator('.accordion-button').click();
                await card.locator('.simulation-edit').click();
                measurements.push(...await measureRows(card.locator('.selector-expression .requirement-row')));
                await capture(`shared-selector-${index}-${width}-${theme}`);
                await card.locator('.simulation-editor').getByRole('button', { name: 'Cancel', exact: true }).click();
                await card.locator('.accordion-button').click();
            }
            const reference = measurements[0].badges[0];
            const gaps = [];
            const referenceOperator = measurements[1].operators[0];
            for (const row of measurements) {
                for (const badge of row.badges) {
                    assert.equal(badge.font, reference.font, `badge font at ${width}/${theme}`);
                    assert.equal(badge.height, reference.height, `badge height at ${width}/${theme}`);
                    assert.equal(badge.margin, '0px', `badge margin at ${width}/${theme}`);
                    assert.ok(Math.abs(badge.center - row.center) < 1, `badge centered in field at ${width}/${theme}`);
                }
                if (row.operators.length) {
                    const operator = row.operators[0];
                    assert.deepEqual(operator.style, referenceOperator.style, `same combo/simulation AND/OR control styles at ${width}/${theme}`);
                    assert.equal(operator.height, referenceOperator.height, `same combo/simulation control heights at ${width}/${theme}`);
                    assert.ok(Math.abs(operator.center - row.center) < 1, `operator centered at ${width}/${theme}`);
                    gaps.push(operator.left - row.badges[0].right, row.badges[1].left - operator.right);
                }
            }
            assert.ok(gaps.every(gap => Math.abs(gap - gaps[0]) < 1), `equal space on both sides of AND/OR: ${gaps}`);
            await capture(`inline-combo-geometry-${width}-${theme}`);
            await geometryCombo.locator('.accordion-button').click();
            const headerBadges = await geometryCombo.locator('.combo-header-requirements .category-tag').evaluateAll(elements => elements.map(element => ({ font: getComputedStyle(element).fontSize, height: element.getBoundingClientRect().height })));
            assert.equal(headerBadges.length, 5);
            assert.ok(headerBadges.every(badge => badge.font === headerBadges[0].font && badge.height === headerBadges[0].height), `equal header badge sizes at ${width}/${theme}`);
            await geometryCombo.locator('.combo-header-requirements .expression-separator').last().click();
            assert.equal(await geometryCombo.locator('.accordion-button').getAttribute('aria-expanded'), 'true', 'static header operators let the header open');
        }
    }
    console.log('PASS shared combo/simulation geometry: equal AND/OR control styles, centered chips/operators, equal gaps and header badge sizes.');
    await load(fixture);
    await page.waitForFunction(count => document.querySelectorAll('.combo-editor').length === count, combos.length);
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
                assert.equal(await combo.locator('.expanded-expression .expression-parenthesis, .inline-member-picker, .requirement-form').count(), 0);
                try {
                    await activate(rows.first().locator('.requirement-edit'));
                }
                catch (error) {
                    console.error('Term activation diagnostics', { index, width, theme, expanded: await combo.locator('.accordion-button').getAttribute('aria-expanded'),
                        rows: await rows.evaluateAll(elements => elements.map(element => ({ text: element.textContent, box: element.getBoundingClientRect().toJSON() }))) });
                    await capture(`term-combo-${index}-failure-${width}-${theme}`);
                    throw error;
                }
                assert.equal(await combo.locator('.requirement-row form').count(), 1);
                await assertNoOverflow(`editing combo ${index}/${width}/${theme}`);
                if ([2, 3, 6].includes(index)) { await capture(`term-combo-${index}-${width}-${theme}`); }
                await combo.locator('.requirement-row form').getByRole('button', { name: 'Cancel', exact: true }).click();
                await combo.locator('.accordion-button').click();
                const closingGaps = await combo.locator('.combo-header-requirements .expression-parenthesis').evaluateAll(elements => elements
                    .filter(element => element.textContent.trim() === ')').map(element => {
                        let previous = element.previousElementSibling;
                        if (previous.getBoundingClientRect().width === 0) { previous = [...previous.querySelectorAll('.expression-badge')].at(-1); }
                        return element.getBoundingClientRect().left - previous.getBoundingClientRect().right;
                    }));
                assert.ok(closingGaps.every(gap => Math.abs(gap - 4) < 1), `closing parentheses hug the expression at ${width}/${theme}: ${closingGaps}`);
                await assertNoOverflow(`collapsed combo ${index}/${width}/${theme}`);
            }
            for (let index = 0; index < selectors.length; index++) {
                const card = page.locator('.card-editor').filter({ hasText: `Selector case ${index}` });
                if (await card.locator('.accordion-button').getAttribute('aria-expanded') !== 'true') { await card.locator('.accordion-button').click(); }
                await card.locator('.simulation-edit').click();
                const rows = card.locator('.selector-expression .requirement-row');
                assert.equal(await rows.count(), expectedSelectors[index].split(';').length, `selector ${index}/${width}/${theme}`);
                assert.equal(await readTerms(rows), expectedSelectors[index]);
                assert.deepEqual(await card.locator('.selector-nested-term').evaluateAll(terms => terms.map(term => [getComputedStyle(term, '::before').content, getComputedStyle(term, '::after').content])), index < 2 ? [] : Array(index === 4 || index === 5 ? 1 : 2).fill(['none', 'none']));
                assert.deepEqual(await card.locator('.selector-expression .expression-operator').evaluateAll(elements => [...new Set(elements.map(element => getComputedStyle(element).fontSize))]), ['12.8px']);
                const outer = card.locator('[data-selector-path="root"] > .requirement-or-group > .requirement-and-separator select, [data-selector-path="root"] > .requirement-or-group > .alternative-separator select');
                assert.deepEqual(await outer.evaluateAll(elements => elements.map(element => element.value)), index < 2 ? [] : [index === 3 || index === 4 ? 'OR' : 'AND']);
                await activate(rows.first().locator('.requirement-edit'));
                assert.equal(await card.locator('.selector-expression form .requirement-operand').count(), expectedSelectors[index].split(';')[0].length);
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
    await card.locator('.selector-expression form .requirement-operand .requirement-property-picker > select').selectOption(`user:${categories[3].Name}`);
    await card.locator('.selector-expression form button[type="submit"]').click();
    assert.equal(await card.locator('.selector-expression .requirement-row').count(), 2);
    assert.equal(await card.locator('.selector-expression .requirement-edit').first().evaluate(element => element === document.activeElement), true);
    await card.locator('.simulation-editor').getByRole('button', { name: 'Cancel', exact: true }).click();
    await card.locator('.accordion-button').click();
    const after = await saveSession();
    assert.deepEqual(after.Combos, original.Combos, 'term presentation does not change combo models');
    assert.deepEqual(after.Cards, original.Cards, 'term presentation does not change saved selectors');
    const empty = { ...fixture, Combos: [{ Name: 'Operator review combo', Active: true, Categories: [], Cards: [], AlternativeGroups: [] }] };
    const emptyCombo = await load(empty);
    assert.equal(await emptyCombo.locator('.requirement-row').count(), 1);
    assert.equal(await emptyCombo.locator('.requirement-form, .add-top-level-group').count(), 0);
    await emptyCombo.locator('[data-new-requirement]').click();
    await emptyCombo.locator('#comboTarget0').selectOption(`user:${categories[0].Name}`);
    await emptyCombo.getByRole('button', { name: 'Add', exact: true }).click();
    assert.equal(await emptyCombo.locator('.requirement-form, [data-new-requirement]').count(), 0);
    await emptyCombo.getByRole('button', { name: '+ Add requirement', exact: true }).click();
    assert.equal(await emptyCombo.locator('.requirement-row').count(), 2);
    await emptyCombo.getByRole('button', { name: 'Cancel', exact: true }).click();
    assert.equal(await emptyCombo.locator('.requirement-row').count(), 1);
    assert.equal(await emptyCombo.locator('.requirement-form').count(), 0);
    await setLayout(1280, 'light');
    console.log('PASS term fields: every combo/selector example at 1280/768/390/320 in light/dark, editing and collapsed.');
};
