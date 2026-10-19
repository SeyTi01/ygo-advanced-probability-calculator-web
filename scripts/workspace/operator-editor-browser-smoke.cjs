// Run against a local development app: node operator-editor-browser-smoke.cjs [url]
// Playwright is an external verification tool; screenshots are retained by default.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium, firefox } = require('playwright');

const base = process.argv[2] || 'http://127.0.0.1:5098';
const evidence = process.env.OPERATOR_SCREENSHOTS || path.resolve(__dirname, '../../packages/operator-browser-publish/evidence');
const alpha = { Name: 'Alpha category with a long label for responsive wrapping', Source: 'User' };
const beta = { Name: 'Beta category with another long label', Source: 'User' };
const effect = { Name: 'Effect Monster with a long property label', Source: 'Metadata', MetadataKey: 'monster-trait:effect' };
const fire = { Name: 'Attribute: FIRE with a long property label', Source: 'Metadata', MetadataKey: 'attribute:fire' };
const spellcaster = { Name: 'Monster Type: Spellcaster with a long property label', Source: 'Metadata', MetadataKey: 'monster-race:spellcaster' };
const dark = { Name: 'Attribute: DARK with a long property label', Source: 'Metadata', MetadataKey: 'attribute:dark' };
const gamma = {
    Name: 'Rule category', Source: 'User', Rule: {
        Kind: 'AnyOf', Children: [
            { Kind: 'AllOf', Categories: ['metadata:monster-trait:effect', 'metadata:attribute:fire'] },
            { Kind: 'AllOf', Categories: ['metadata:monster-race:spellcaster', 'metadata:attribute:dark'] }
        ]
    }
};
const allCategories = [alpha, beta, gamma, effect, fire, spellcaster, dark];
const fixedCategory = category => ({ BaseCategory: category, MinCount: 1, MaxCount: 2, MaximumMode: 'Fixed' });
const fixedCard = cardId => ({ CardId: cardId, MinCount: 1, MaxCount: 2, MaximumMode: 'Fixed' });
const session = () => ({
    SchemaVersion: 10,
    CalculationModel: 'Fast',
    Categories: allCategories,
    Cards: [
        { Id: 'valid-card', Name: 'Fixture card', Copies: 2, Active: true, Categories: [alpha, beta, effect, fire] },
        { Id: 'effect-draw-card', Name: 'Draw and remove card', Copies: 1, Active: true, Categories: [effect, fire], DrawCount: 1,
            PostDrawDiscardCount: 1, PostDrawSelector: { Kind: 'AnyOf', Categories: ['metadata:monster-trait:effect', 'metadata:attribute:fire'] } },
        { Id: 'spellcaster-card', Name: 'Spellcaster card', Copies: 1, Active: true, Categories: [spellcaster, dark] },
        { Id: 'no-match-card', Name: 'No rule match card', Copies: 1, Active: true, Categories: [alpha] }
    ],
    Combos: [{
        Name: 'Operator review combo', Active: true,
        Categories: [fixedCategory(alpha), fixedCategory(beta)],
        Cards: [fixedCard('valid-card'), fixedCard('missing-card')],
        AlternativeGroups: []
    }],
    ComboGroups: [],
    HandSize: 5,
    CategoryColorIndices: {}
});

(async () => {
    const useFirefox = process.env.OPERATOR_BROWSER === 'firefox';
    const browser = await (useFirefox ? firefox.launch({ headless: true }) : chromium.launch({ headless: true, ...(process.env.OPERATOR_EXECUTABLE ? { executablePath: process.env.OPERATOR_EXECUTABLE } : { channel: 'msedge' }) }));
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, hasTouch: !useFirefox });
        const activate = locator => useFirefox ? locator.click() : locator.tap();
        const pageErrors = [];
        page.on('pageerror', error => pageErrors.push(error.message));
        await page.addInitScript(() => { window.showSaveFilePicker = undefined; });
        await page.goto(base);
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor({ timeout: 60000 }).catch(async error => {
            console.error('App startup diagnostics:', pageErrors, await page.locator('body').innerText());
            throw error;
        });
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);

        const capture = async name => {
            await fs.mkdir(evidence, { recursive: true });
            if (name.startsWith('term-combo-')) {
                await page.locator('.combo-editor').nth(Number(name.split('-')[2])).screenshot({ path: path.join(evidence, `${name}.png`), animations: 'disabled' });
            }
            else if (name.startsWith('term-selector-')) {
                await page.locator('.card-editor').filter({ hasText: `Selector case ${name.split('-')[2]}` }).screenshot({ path: path.join(evidence, `${name}.png`), animations: 'disabled' });
            }
            else if (name.startsWith('inline-combo')) {
                await page.locator('.combo-editor').first().screenshot({ path: path.join(evidence, `${name}.png`), animations: 'disabled' });
            }
            else {
                await page.screenshot({ path: path.join(evidence, `${name}.png`), fullPage: true, animations: 'disabled' });
            }
        };
        const load = async value => {
            await page.locator('#sessionFileInput').setInputFiles({
                name: 'operator-review.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(value))
            });
            await page.getByText('Operator review combo', { exact: true }).waitFor().catch(async error => { console.error('Session load diagnostics:', await page.locator('body').innerText()); throw error; });
            await page.waitForTimeout(150);
            const combo = page.locator('.combo-editor').first();
            if (await combo.locator('.accordion-button').getAttribute('aria-expanded') !== 'true') {
                await combo.locator('.accordion-button').click();
            }
            return combo;
        };
        const saveSession = async () => {
            const download = page.waitForEvent('download');
            await page.getByRole('button', { name: 'Save Session', exact: true }).click();
            return JSON.parse(await fs.readFile(await (await download).path(), 'utf8'));
        };
        const categoryManagement = page.locator('details.category-management');
        const ensureCategoryManagementOpen = async () => {
            if (!await categoryManagement.evaluate(element => element.open)) {
                await categoryManagement.locator('summary').click();
            }
        };
        const assertNoOverflow = async label => {
            const sizes = await page.evaluate(() => ({
                viewport: innerWidth,
                document: document.documentElement.scrollWidth,
                clipped: [...document.querySelectorAll('button, select, input')].filter(element => {
                    const box = element.getBoundingClientRect();
                    return box.width > 0 && (box.left < -1 || box.right > innerWidth + 1);
                }).map(element => element.outerHTML.slice(0, 180)),
                overflowers: [...document.querySelectorAll('body *')].filter(element => {
                    const box = element.getBoundingClientRect();
                    return box.width > 0 && (box.left < -1 || box.right > innerWidth + 1);
                }).slice(0, 12).map(element => {
                    const box = element.getBoundingClientRect();
                    return { tag: element.tagName, class: element.className?.toString().slice(0, 90), left: box.left, right: box.right, width: box.width };
                })
            }));
            assert.ok(sizes.document <= sizes.viewport, `${label}: document overflow ${JSON.stringify(sizes)}`);
            assert.deepEqual(sizes.clipped, [], `${label}: clipped control`);
        };
        const setLayout = async (width, theme) => {
            await page.setViewportSize({ width, height: 900 });
            await page.getByLabel('Color theme preference').selectOption(theme);
            if (width === 390) { await capture(`debug-390-${theme}`); }
            await assertNoOverflow(`${width}/${theme}`);
        };

        await require('./term-editor-browser-cases.cjs')({ page, load, setLayout, saveSession, capture, assertNoOverflow, activate, session });
        let combo = await load(session());
        assert.equal(await page.locator('.combo-editor [role="status"]').filter({ hasText: 'Missing card reference' }).count(), 1,
            'missing card references are visible in the combo editor');
        const operators = combo.getByRole('combobox', { name: /Change .* operator/ });
        assert.ok(await operators.count() >= 3, 'combo connectors expose native operator choices');
        assert.equal(await combo.locator('.requirement-form').count(), 0);

        // A missing target remains visible and keyboard focus can return to its row.
        const missingRow = combo.locator('[data-requirement="card:1"]');
        await missingRow.click();
        const missingTarget = combo.locator('#comboTarget0');
        assert.equal(await missingTarget.inputValue(), 'card:missing-card');
        assert.equal(await missingTarget.locator('option').filter({ hasText: 'Missing card' }).count(), 1);
        assert.equal(await missingTarget.evaluate(element => element === document.activeElement), true, 'editing a missing card focuses its target');
        await missingTarget.press('Escape');
        assert.equal(await missingRow.evaluate(element => element === document.activeElement), true, 'Escape restores focus to the edited missing-card row');

        // Native connector changes commit immediately, with no secondary form.
        const firstConnector = combo.locator('.requirement-list > .requirement-and-separator select').first();
        await firstConnector.focus();
        await firstConnector.press('ArrowDown');
        await combo.locator('.requirement-interactive-summary').waitFor();
        assert.equal(await combo.locator('.requirement-row form').count(), 0);
        assert.equal(await combo.getByRole('button', { name: 'Edit group', exact: true }).count(), 0);
        let saved = await saveSession();
        assert.equal(saved.Combos[0].Categories.length, 1);
        assert.deepEqual(saved.Combos[0].Categories[0].CategoryBases.map(category => category.Name), [alpha.Name, beta.Name]);
        const summaryBadge = combo.locator('.requirement-interactive-summary .category-tag').first();
        await summaryBadge.click();
        await combo.locator('#minCount0').fill('2');
        assert.deepEqual(await combo.locator('.requirement-interactive-summary .expression-operator option').allTextContents(), ['AND'],
            'an unsaved affected row hides unavailable conversions');
        await summaryBadge.click();
        assert.equal(await combo.locator('.requirement-row form').count(), 0, 'repeat clicking the same-card summary discards its row draft');
        assert.deepEqual((await saveSession()).Combos, saved.Combos);

        // Direct-card AND is absent; the existing expression keeps every bound.
        const directConnector = combo.locator('.requirement-list > .requirement-and-separator select').last();
        await directConnector.focus();
        assert.deepEqual(await directConnector.locator('option').allTextContents(), ['+', 'OR']);
        assert.equal(await combo.getByRole('alert').count(), 0);
        assert.equal(await directConnector.inputValue(), '+');
        assert.equal(await directConnector.evaluate(element => element === document.activeElement), true);
        const refused = await saveSession();
        assert.deepEqual(refused.Combos, saved.Combos);

        // A same-card AND can become OR directly from its visible expression.
        await combo.locator('.requirement-interactive-summary .expression-operator').selectOption('OR');
        await combo.locator('.requirement-or-group').waitFor();
        assert.equal(await combo.locator('.requirement-row form').count(), 0);
        assert.equal(await combo.locator('.requirement-or-group .expression-operator').first().evaluate(element => element === document.activeElement), true);
        const group = combo.locator('.requirement-or-group').first();
        const innerChoices = await group.locator('.expression-operator option').allTextContents();
        assert.ok(!innerChoices.includes('+'));
        assert.equal(await combo.locator('.inline-member-picker, .expression-row-controls, .expanded-expression .expression-parenthesis').count(), 0);
        assert.equal(await combo.locator('.requirement-form').count(), 0, 'adding is closed by default');
        await group.locator('[data-requirement="alternative:0:0"]').click();
        await combo.locator('[data-action="alternative"]').click();
        await combo.locator('#comboTarget0').selectOption('metadata:attribute:fire');
        await combo.getByRole('button', { name: 'Add alternative', exact: true }).click();
        saved = await saveSession();
        assert.equal(saved.Combos[0].AlternativeGroups[0].Alternatives.length, 3);
        assert.equal(saved.Combos[0].AlternativeGroups[0].Alternatives[2].Category.MaximumMode, 'HandSize');
        assert.equal(await combo.locator('.requirement-form').count(), 0);

        // Repeat click, Escape and sibling switching discard row drafts.
        const firstRow = combo.locator('[data-requirement="alternative:0:0"]');
        await activate(firstRow);
        await combo.locator('#minCount0').fill('0');
        await activate(firstRow);
        assert.equal(await combo.locator('.requirement-row form').count(), 0);
        assert.equal(await firstRow.evaluate(element => element === document.activeElement), true);
        await firstRow.focus();
        await firstRow.press('Enter');
        await combo.locator('#minCount0').fill('0');
        await combo.locator('[data-requirement="alternative:0:1"]').click();
        assert.equal(await combo.locator('.requirement-row form').count(), 1);
        await combo.locator('#minCount0').press('Escape');
        assert.equal(await combo.locator('[data-requirement="alternative:0:1"]').evaluate(element => element === document.activeElement), true);
        assert.deepEqual((await saveSession()).Combos, saved.Combos);

        // Add a same-card property in place, then introduce a top-level exclusion.
        await combo.locator('[data-requirement="alternative:0:0"]').click();
        await combo.locator('[data-action="property"]').click();
        await combo.locator('#comboTarget0').selectOption('metadata:monster-trait:effect');
        await combo.getByRole('button', { name: 'Add property', exact: true }).click();
        assert.equal(await combo.locator('.requirement-row form').count(), 0);
        const compositeRow = combo.locator('[data-requirement="alternative:0:0"]');
        await compositeRow.click();
        await combo.locator('#minCount0').press('Escape');
        assert.equal(await compositeRow.evaluate(element => element === document.activeElement), true, 'a newly nested term restores focus to its visible field');
        const bottomPlus = combo.getByRole('button', { name: '+ Add requirement', exact: true });
        await bottomPlus.focus();
        await bottomPlus.press('Enter');
        assert.equal(await combo.locator('#comboTarget0').evaluate(element => element === document.activeElement), true);
        await combo.locator('#comboTarget0').selectOption('metadata:attribute:dark');
        await combo.locator('#minCount0').fill('0');
        await combo.locator('.requirement-bounds summary').click();
        await combo.locator('#maxCount0').fill('0');
        await combo.getByRole('button', { name: 'Add', exact: true }).click();
        saved = await saveSession();
        const exclusion = saved.Combos[0].Categories.at(-1);
        assert.equal(exclusion.MinCount, 0);
        assert.equal(exclusion.MaxCount, 0);
        assert.equal(exclusion.MaximumMode, 'Fixed');
        assert.equal(saved.Combos[0].RequirementOrder.at(-1).Kind, 'Category');

        // Responsive geometry, both themes, and the borderless collapsed OR header.
        for (const width of [320, 390, 768, 1280]) {
            for (const theme of ['light', 'dark']) {
                await setLayout(width, theme);
                const border = await combo.locator('.combo-header-requirements .alternative-group').evaluate(element => {
                    const style = getComputedStyle(element);
                    return [style.borderTopWidth, style.borderRightWidth, style.backgroundColor];
                });
                assert.deepEqual(border, ['0px', '0px', 'rgba(0, 0, 0, 0)']);
                await capture(`inline-combo-${width}-${theme}`);
                await combo.locator('.accordion-button').click();
                await assertNoOverflow(`collapsed combo ${width} ${theme}`);
                await capture(`inline-combo-collapsed-${width}-${theme}`);
                await combo.locator('.accordion-button').click();
            }
        }
        await setLayout(1280, 'light');
        // Post-draw selector connector opens the same bounded selector editor on touch.
        const card = page.locator('.card-editor').filter({ hasText: 'Draw and remove card' });
        if (await card.locator('.accordion-button').getAttribute('aria-expanded') !== 'true') {
            await card.locator('.accordion-button').click();
        }
        await card.locator('.simulation-edit').click();
        const simulation = card.locator('.simulation-editor');
        const postDrawOr = simulation.getByLabel('Change OR operator', { exact: true }).first();
        await activate(postDrawOr);
        await postDrawOr.selectOption('AND');
        const selectorForm = simulation.locator('.selector-condition-row');
        const selectorFormContainer = () => selectorForm.locator('xpath=..');
        await selectorForm.waitFor();
        assert.equal(await selectorForm.locator('.selector-operand').count(), 2);
        await capture('06-post-draw-selector-touch-1280-light');
        await selectorFormContainer().getByRole('button', { name: 'Cancel', exact: true }).click();
        assert.equal(await simulation.locator('.selector-condition-row').count(), 0, 'selector Cancel closes the grouped edit');
        await simulation.getByRole('button', { name: 'Cancel', exact: true }).click();

        // Saved category-rule expression is distinct from its draft; Apply and Remove are explicit.
        await ensureCategoryManagementOpen();
        await page.getByRole('button', { name: 'Edit category Rule category', exact: true }).click();
        await page.getByRole('button', { name: 'Edit rule', exact: true }).click();
        const rule = page.locator('.category-rule-editor');
        await rule.waitFor();
        assert.equal(await rule.locator('.alternative-separator select').count(), 1, 'saved OR is visible');
        assert.equal(await rule.locator('.requirement-and-separator select').count(), 2, 'saved AND branches are visible');
        await setLayout(1280, 'light');
        await capture('07-category-rule-saved-1280-light');
        await setLayout(390, 'dark');
        await capture('08-category-rule-saved-390-dark');
        await rule.getByLabel('Change AND operator', { exact: true }).first().selectOption('OR');
        assert.equal(await rule.locator('.alternative-separator select').count(), 2, 'operator change edits the local rule draft');
        saved = await saveSession();
        const savedRuleBeforeApply = saved.Categories.find(category => category.Name === 'Rule category').Rule;
        assert.equal(savedRuleBeforeApply.Children.length, 2, 'rule draft does not auto-save to session');
        assert.equal(saved.Cards.find(cardRow => cardRow.Id === 'no-match-card').Categories.some(category => category.Name === 'Rule category'), false,
            'editing a rule does not change explicit card membership');
        await rule.getByRole('button', { name: 'Cancel', exact: true }).click();
        await page.getByRole('button', { name: 'Edit category Rule category', exact: true }).click();
        await page.getByRole('button', { name: 'Edit rule', exact: true }).click();
        const ruleAfterCancel = page.locator('.category-rule-editor');
        assert.equal(await ruleAfterCancel.locator('.alternative-separator select').count(), 1, 'Cancel restores the saved rule expression');
        await ruleAfterCancel.getByLabel('Add AND property to alternative 1', { exact: true }).selectOption('metadata:attribute:dark');
        await ruleAfterCancel.getByRole('button', { name: 'Apply rule', exact: true }).click();
        saved = await saveSession();
        const appliedRule = saved.Categories.find(category => category.Name === 'Rule category').Rule;
        assert.ok(appliedRule, 'Apply commits the category rule');
        await page.getByRole('button', { name: 'Edit category Rule category', exact: true }).click();
        await page.getByRole('button', { name: 'Edit rule', exact: true }).click();
        await page.locator('.category-rule-editor').getByRole('button', { name: 'Remove rule', exact: true }).click();
        saved = await saveSession();
        assert.equal(saved.Categories.find(category => category.Name === 'Rule category').Rule, undefined, 'Remove explicitly clears the saved rule');

        await load(saved);
        assert.deepEqual((await saveSession()).Combos, saved.Combos, 'save/load retains the inline expression and authored order');
        assert.deepEqual(pageErrors, [], `browser page errors: ${pageErrors.join('; ')}`);
        console.log(`PASS #247 browser interactions. Screenshots: ${evidence}`);
    }
    finally {
        await browser.close();
    }
})().catch(error => {
    console.error(error);
    process.exitCode = 1;
});
