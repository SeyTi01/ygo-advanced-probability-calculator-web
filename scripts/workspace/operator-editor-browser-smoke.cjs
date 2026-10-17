// Run against a local development app: node operator-editor-browser-smoke.cjs [url]
// Playwright is an external verification tool; screenshots are retained by default.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');

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
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, hasTouch: true });
        const pageErrors = [];
        page.on('pageerror', error => pageErrors.push(error.message));
        await page.addInitScript(() => { window.showSaveFilePicker = undefined; });
        await page.goto(base);
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);

        const capture = async name => {
            await fs.mkdir(evidence, { recursive: true });
            await page.screenshot({ path: path.join(evidence, `${name}.png`), fullPage: true, animations: 'disabled' });
        };
        const load = async value => {
            await page.locator('#sessionFileInput').setInputFiles({
                name: 'operator-review.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(value))
            });
            await page.getByText('Operator review combo', { exact: true }).waitFor();
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

        let combo = await load(session());
        assert.equal(await page.locator('.combo-editor [role="status"]').filter({ hasText: 'Missing card reference' }).count(), 1,
            'missing card references are visible in the combo editor');
        const operators = combo.getByRole('combobox', { name: /Change .* operator/ });
        assert.ok(await operators.count() >= 3, 'combo connectors expose native operator choices');

        // A missing target remains visible and keyboard focus can return to its row.
        const missingRow = combo.locator('[data-requirement="card:1"]');
        await missingRow.click();
        const missingTarget = combo.locator('#comboTarget0');
        assert.equal(await missingTarget.inputValue(), 'card:missing-card');
        assert.equal(await missingTarget.locator('option').filter({ hasText: 'Missing card' }).count(), 1);
        assert.equal(await missingTarget.evaluate(element => element === document.activeElement), true, 'editing a missing card focuses its target');
        await missingTarget.press('Escape');
        assert.equal(await missingRow.evaluate(element => element === document.activeElement), true, 'Escape restores focus to the edited missing-card row');

        // Change the first native + selector by keyboard. AND combines only the two category requirements.
        const firstConnector = combo.locator('.requirement-list > .requirement-and-separator select').first();
        await firstConnector.focus();
        await firstConnector.press('ArrowDown');
        const grouped = combo.locator('.bounded-condition-group');
        const groupedForm = () => grouped.locator('xpath=..');
        await grouped.waitFor();
        assert.equal(await grouped.locator('.bounded-condition').count(), 2);
        const initialFocus = await page.evaluate(() => document.activeElement?.getAttribute('aria-label'));
        console.log(`same-card group initial focus: ${initialFocus}`);
        assert.equal(initialFocus, 'Condition 1 target', 'same-card group opens with focus in its first operand');
        await setLayout(1280, 'light');
        await capture('01-same-card-group-1280-light');
        await setLayout(390, 'dark');
        await capture('02-same-card-group-390-dark');

        // Cancel and Escape discard the entire bounded edit without changing saved requirements.
        await grouped.getByLabel('Condition 1 minimum', { exact: true }).fill('2');
        await groupedForm().getByRole('button', { name: 'Cancel', exact: true }).click();
        assert.equal(await combo.locator('.bounded-condition-group').count(), 0);
        let saved = await saveSession();
        assert.equal(saved.Combos[0].Categories.length, 2, 'Cancel leaves saved category requirements untouched');
        assert.equal(saved.Combos[0].Categories[0].BaseCategory.Name, alpha.Name);
        const firstConnectorAgain = combo.locator('.requirement-list > .requirement-and-separator select').first();
        await firstConnectorAgain.selectOption('AND');
        await grouped.waitFor();
        await grouped.getByLabel('Condition 1 minimum', { exact: true }).fill('2');
        await grouped.getByLabel('Condition 1 target', { exact: true }).press('Escape');
        assert.equal(await combo.locator('.bounded-condition-group').count(), 0, 'Escape closes and cancels the full grouped form');
        saved = await saveSession();
        assert.equal(saved.Combos[0].Categories.length, 2, 'Escape leaves saved requirements untouched');

        // Save the same-card conversion and verify header parentheses and bounds.
        await combo.locator('.requirement-list > .requirement-and-separator select').first().selectOption('AND');
        await grouped.waitFor();
        await grouped.getByLabel('Condition 1 minimum', { exact: true }).fill('2');
        await groupedForm().getByRole('button', { name: 'Save group', exact: true }).click();
        await combo.getByRole('alert').filter({ hasText: 'Same-card AND requires matching bounds' }).waitFor();
        assert.equal(await combo.locator('.bounded-condition-group').count(), 1,
            'mismatched same-card bounds stay in the editor and do not mutate the combo');
        await grouped.getByLabel('Condition 2 minimum', { exact: true }).fill('2');
        await groupedForm().getByRole('button', { name: 'Save group', exact: true }).click();
        await combo.locator('.combo-header-requirements').waitFor();
        saved = await saveSession();
        const sameCardBadge = combo.locator('.combo-header-requirements .same-card-group').first();
        assert.match(await sameCardBadge.innerText(), /Alpha category.*Beta category/s);
        assert.ok(await combo.locator('.same-card-group .expression-parenthesis').count() >= 2, 'same-card header badge shows its parentheses');
        assert.equal(saved.Combos[0].Categories.length, 1);
        assert.equal(saved.Combos[0].Categories[0].MinCount, 2);
        assert.deepEqual(saved.Combos[0].Categories[0].CategoryBases.map(category => category.Name), [alpha.Name, beta.Name]);

        // A direct-card AND conversion is visible and makes no model change.
        const directCardConnector = combo.locator('.requirement-list > .requirement-and-separator select').nth(1);
        await directCardConnector.tap();
        await directCardConnector.selectOption('AND');
        await grouped.waitFor();
        await groupedForm().getByRole('button', { name: 'Save group', exact: true }).click();
        await combo.getByRole('alert').filter({ hasText: 'Direct-card conditions can use + or OR' }).waitFor();
        assert.equal(await combo.locator('.bounded-condition-group').count(), 1,
            'unsupported direct-card conversion stays visible in the editor and does not commit');
        await groupedForm().getByRole('button', { name: 'Cancel', exact: true }).click();
        saved = await saveSession();
        assert.equal(saved.Combos[0].Cards.length, 2, 'unsupported direct-card AND leaves both card requirements unchanged');
        assert.equal(saved.Combos[0].AlternativeGroups?.length ?? 0, 0);
        await capture('03-unsupported-card-and-1280-light');

        // Convert the composite category plus a card to OR, retain category drafts through rename,
        // and preserve the missing-reference warning on the remaining direct-card requirement.
        const categoryCardConnector = combo.locator('.requirement-list > .requirement-and-separator select').first();
        await categoryCardConnector.press('End');
        await grouped.waitFor();
        assert.equal(await grouped.getByLabel('Condition 2 target', { exact: true }).inputValue(), 'card:valid-card');
        await grouped.getByLabel('Condition 1 minimum', { exact: true }).fill('2');
        await ensureCategoryManagementOpen();
        await page.getByRole('button', { name: `Edit category ${alpha.Name}`, exact: true }).click();
        const rename = page.getByRole('textbox', { name: `New name for category ${alpha.Name}`, exact: true });
        await rename.fill('Alpha category renamed while editing');
        await page.locator('details.category-management > summary').click();
        await page.locator('details.category-management > summary').click();
        assert.equal(await page.getByRole('textbox', { name: `New name for category ${alpha.Name}`, exact: true }).inputValue(),
            'Alpha category renamed while editing', 'category rename draft survives closing and reopening its editor');
        await page.getByRole('button', { name: 'Save category name', exact: true }).click();
        await groupedForm().getByRole('button', { name: 'Save group', exact: true }).click();
        await combo.locator('.requirement-or-group').waitFor();
        saved = await saveSession();
        const renamedCategory = saved.Categories.find(category => category.Name === 'Alpha category renamed while editing');
        assert.ok(renamedCategory, 'renamed category is committed');
        assert.equal(saved.Combos[0].AlternativeGroups[0].Alternatives[0].Category.MinCount, 2,
            'renaming a category retains the bounded group draft');
        assert.deepEqual(saved.Combos[0].AlternativeGroups[0].Alternatives[0].Category.CategoryBases.map(category => category.Name),
            ['Alpha category renamed while editing', beta.Name]);
        await setLayout(390, 'dark');
        await capture('06-renamed-long-composite-390-dark');
        assert.equal(saved.Combos[0].Cards.length, 1);
        assert.equal(saved.Combos[0].Cards[0].CardId, 'missing-card');
        assert.match(await combo.getByRole('status').innerText(), /Missing card reference/);

        // Existing OR group edits are full-form transactions: Cancel and Escape do not leak drafts.
        const editGroup = combo.getByRole('button', { name: 'Edit group', exact: true });
        await editGroup.click();
        await grouped.waitFor();
        await grouped.getByLabel('Condition 1 minimum', { exact: true }).fill('1');
        await setLayout(1280, 'dark');
        await capture('04-existing-or-group-1280-dark');
        await setLayout(390, 'light');
        await capture('05-existing-or-group-390-light');
        await groupedForm().getByRole('button', { name: 'Cancel', exact: true }).click();
        saved = await saveSession();
        assert.equal(saved.Combos[0].AlternativeGroups[0].Alternatives[0].Category.MinCount, 2);
        await combo.getByRole('button', { name: 'Edit group', exact: true }).click();
        await grouped.waitFor();
        await grouped.getByLabel('Condition 1 minimum', { exact: true }).press('Escape');
        assert.equal(await combo.locator('.bounded-condition-group').count(), 0);
        saved = await saveSession();
        assert.equal(saved.Combos[0].AlternativeGroups[0].Alternatives[0].Category.MinCount, 2,
            'Escape leaves the existing OR group unchanged');

        // Post-draw selector connector opens the same bounded selector editor on touch.
        const card = page.locator('.card-editor').filter({ hasText: 'Draw and remove card' });
        if (await card.locator('.accordion-button').getAttribute('aria-expanded') !== 'true') {
            await card.locator('.accordion-button').click();
        }
        await card.locator('.simulation-edit').click();
        const simulation = card.locator('.simulation-editor');
        const postDrawOr = simulation.getByLabel('Change OR operator', { exact: true }).first();
        await postDrawOr.tap();
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

        assert.deepEqual(pageErrors, [], `browser page errors: ${pageErrors.join('; ')}`);
        console.log(`PASS #230 browser interactions. Screenshots: ${evidence}`);
    }
    finally {
        await browser.close();
    }
})().catch(error => {
    console.error(error);
    process.exitCode = 1;
});
