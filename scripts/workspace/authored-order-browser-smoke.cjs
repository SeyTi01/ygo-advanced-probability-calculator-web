// Rendered-browser verification for authored selector and combo requirement order.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const baseUrl = process.argv[2] || 'http://127.0.0.1:5096';
    const evidence = process.env.WORKSPACE_SCREENSHOTS;
    const errors = [];
    const browser = await chromium.launch({ headless: true, timeout: 20000, ...(process.env.OPERATOR_EXECUTABLE ? { executablePath: process.env.OPERATOR_EXECUTABLE } : { channel: 'msedge' }) });
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 1000 }, hasTouch: true });
        page.on('pageerror', error => errors.push(error.message));
        await page.addInitScript(() => { window.showSaveFilePicker = undefined; });
        await page.goto(baseUrl);
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);

        const session = {
            SchemaVersion: 10,
            CalculationModel: 'Fast',
            Categories: ['A', 'B', 'C', 'D', 'E'].map(Name => ({ Name, Source: 'User' })),
            Cards: [{
                Categories: [{ Name: 'A', Source: 'User' }],
                Copies: 2,
                Name: 'Physical',
                Active: true,
                Id: 'physical',
                ManualMetadataCategoryKeys: [],
                Search: {
                    Selector: { Kind: 'AllOf', Categories: ['user:A', 'user:B', 'user:C', 'user:D'] },
                    ObjectiveMetadataOnly: false
                },
                EffectProfile: { Source: 'Custom', ExecutionModel: 'DrawSequenceV1' }
            }],
            Combos: [],
            ComboGroups: [],
            HandSize: 2,
            CategoryColorIndices: {}
        };
        await page.locator('#sessionFileInput').setInputFiles({
            name: 'authored-order-browser.json',
            mimeType: 'application/json',
            buffer: Buffer.from(JSON.stringify(session))
        });
        await page.locator('.card-editor').waitFor();
        await page.getByRole('button', { name: 'Add combo' }).click();
        const combo = page.locator('.combo-editor').first();
        await combo.locator('[data-new-requirement]').click();
        await page.locator('#comboTarget0').selectOption('card:physical');
        await combo.locator('.requirement-submit button').click();
        await combo.getByRole('button', { name: '+ Add requirement', exact: true }).click();
        await page.locator('#comboTarget0').selectOption('user:A');
        await combo.locator('.requirement-submit button').click();
        const rowOrder = async () => combo.locator('.requirement-list button[data-requirement]').evaluateAll(rows => rows.map(row => row.dataset.requirement));
        assert.deepEqual(await rowOrder(), ['card:0', 'category:0'], 'new direct-card and category requirements retain their authored order');
        await combo.locator('.accordion-button').click();
        const header = await combo.locator('.combo-header-requirements').innerText();
        assert.ok(header.indexOf('Physical') < header.indexOf('A'), `header keeps card before category: ${header}`);
        await combo.locator('.accordion-button').click();

        await combo.locator("button[data-requirement='card:0']").click();
        await page.locator('#comboTarget0').selectOption('user:B');
        await page.locator('#minCount0').fill('0');
        await combo.locator('details.requirement-bounds summary').click();
        await page.locator('#maxCount0').fill('0');
        await combo.locator('.requirement-submit button').click();
        assert.deepEqual(await rowOrder(), ['category:1', 'category:0'], 'type-changing edit occupies the direct card’s original slot');

        await combo.locator("button[data-requirement='category:1']").click();
        await combo.locator('[data-action="alternative"]').click();
        await page.locator('#comboTarget0').selectOption('card:physical');
        await combo.locator('.requirement-submit button').click();
        assert.deepEqual(await rowOrder(), ['alternative:0:0', 'alternative:0:1', 'category:0'], 'OR promotion preserves the first requirement’s slot');
        await combo.locator("button[data-requirement='alternative:0:1']").click();
        await combo.getByRole('button', { name: 'Remove alternative', exact: true }).click();
        assert.deepEqual(await rowOrder(), ['category:1', 'category:0'], 'unwrapping a single surviving OR leaf restores its original slot');

        const card = page.locator('.card-editor').first();
        const cardToggle = card.locator('.accordion-button');
        if (await cardToggle.getAttribute('aria-expanded') !== 'true') await cardToggle.click();
        const simulation = card.locator('.card-simulation');
        await simulation.locator('.simulation-edit').click();
        const selectorRows = () => simulation.locator('.selector-expression[data-selector-path="root"] .requirement-edit');
        const authoredLabels = async () => selectorRows().evaluateAll(rows => rows.map(row =>
            [...row.closest('.requirement-row').querySelectorAll('.expression-term-part')].map((term, index) =>
                `${index > 0 ? term.querySelector('select').value + ' ' : ''}${term.querySelector('.draw-selector-chip').textContent.trim()}`).join(' ')));

        assert.deepEqual(await authoredLabels(), ['A AND B AND C AND D'], 'selector editor groups all flat leaves in authored order');
        await simulation.locator('.selector-expression[data-selector-path="root"] .requirement-edit').click();
        const selectorInput = simulation.locator('.requirement-operand .requirement-property-picker > select').first();
        await selectorInput.selectOption('user:E');

        const categoryManagement = page.locator('details.category-management');
        if (await categoryManagement.evaluate(element => !element.open)) await categoryManagement.locator('summary').click();
        await categoryManagement.getByRole('button', { name: 'Edit category A', exact: true }).click();
        const renameInput = categoryManagement.getByRole('textbox', { name: 'New name for category A', exact: true });
        await renameInput.waitFor();
        assert.equal(await renameInput.evaluate(element => document.activeElement === element), true, 'rename puts focus in the category draft');
        await renameInput.fill('Z');
        await categoryManagement.getByRole('button', { name: 'Save category name', exact: true }).click();
        await categoryManagement.getByRole('button', { name: 'Edit category Z', exact: true }).waitFor();
        assert.equal(await categoryManagement.getByRole('button', { name: 'Edit category Z', exact: true }).evaluate(element => document.activeElement === element), true, 'saving a rename returns focus to its category trigger');
        assert.equal(await selectorInput.inputValue(), 'user:E', 'selector operand draft survives category rename');
        assert.deepEqual(await authoredLabels(), ['Z AND B AND C AND D'], 'rename preserves authored order within the flat selector term');
        await simulation.locator('.selector-expression[data-selector-path="root"] .requirement-form button[type="submit"]').click();
        assert.deepEqual(await authoredLabels(), ['E AND B AND C AND D'], 'saved selector operand replaces only its original authored slot');
        await simulation.locator('.simulation-save').click();
        await simulation.locator('.simulation-edit').click();
        assert.deepEqual(await authoredLabels(), ['E AND B AND C AND D'], 'saved selector display remains in authored order when reopened');
        const accessibleLabels = await selectorRows().evaluateAll(rows => rows.map(row => row.getAttribute('aria-label')));
        assert.deepEqual(accessibleLabels, ['Edit E AND B AND C AND D'], 'accessible row labels match the visible authored term order');

        const capture = async name => {
            if (!evidence) return;
            await fs.mkdir(evidence, { recursive: true });
            await page.screenshot({ path: path.join(evidence, `${name}.png`), fullPage: true });
        };
        for (const theme of ['Light', 'Dark']) {
            await page.getByLabel('Color theme preference').selectOption({ label: theme });
            for (const width of [1280, 390]) {
                await page.setViewportSize({ width, height: 1000 });
                await page.evaluate(() => window.scrollTo(0, 0));
                const overflow = await page.evaluate(() => ({
                    viewport: innerWidth,
                    document: document.documentElement.scrollWidth,
                    body: document.body.scrollWidth
                }));
                assert.ok(overflow.document <= width && overflow.body <= width, `no horizontal page overflow at ${width}px ${theme}: ${JSON.stringify(overflow)}`);
                const outOfBounds = await page.locator('.requirement-list .requirement-edit, .selector-expression .requirement-edit, .requirement-edit-grid .requirement-operand, .requirement-inputs > .expression-separator').evaluateAll(elements => elements.map(element => {
                    const rect = element.getBoundingClientRect();
                    return { selector: element.className, left: rect.left, right: rect.right };
                }).filter(rect => rect.left < -1 || rect.right > innerWidth + 1));
                assert.deepEqual(outOfBounds, [], `authoring controls remain inside ${width}px ${theme} viewport`);
                await capture(`authored-order-${width}-${theme.toLowerCase()}`);
            }
        }
        assert.deepEqual(errors, [], `browser errors: ${errors.join('; ')}`);
        console.log(JSON.stringify({ browser: browser.version(), port: 5096, widths: [1280, 390], themes: ['Light', 'Dark'], assertions: ['card/category order', 'type-change slot', 'OR promotion/unwrapping', 'selector term replacement in place', 'rename focus and draft retention', 'responsive overflow'], visibleSelectorOrder: await authoredLabels(), accessibleSelectorLabels: accessibleLabels, result: 'passed' }));
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
