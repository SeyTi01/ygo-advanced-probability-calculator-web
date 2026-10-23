// Run against a local app, for example http://127.0.0.1:5090.
// WORKSPACE_SCREENSHOTS may point to a directory for rendered evidence.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');

const category = name => ({ Name: name, Source: 'User' });
const categoryRequirement = name => ({
    BaseCategory: category(name),
    MinCount: 1,
    MaxCount: 2,
    MaximumMode: 'Fixed'
});

const sessionFixture = () => ({
    SchemaVersion: 9,
    Categories: [category('A'), category('B')],
    Cards: [
        { Categories: [category('A')], Copies: 2, Name: 'Alpha', Active: true, Id: 'alpha', ManualMetadataCategoryKeys: [] },
        { Categories: [category('B')], Copies: 2, Name: 'Beta', Active: true, Id: 'beta', ManualMetadataCategoryKeys: [] },
        {
            Categories: [category('A'), category('B')],
            Copies: 1,
            Name: 'Inactive draw',
            Active: false,
            Id: 'inactive-draw',
            ManualMetadataCategoryKeys: [],
            DrawCount: 1,
            PreDrawRemovalCount: 1,
            PreDrawSelector: {
                Kind: 'AllOf',
                Children: [
                    { Kind: 'Category', Categories: ['user:A'] },
                    { Kind: 'AnyOf', Categories: ['user:A', 'user:B'] }
                ]
            },
            PostDrawDiscardCount: 1,
            PostDrawSelector: { Kind: 'Category', Categories: ['user:A'] }
        }
    ],
    Combos: [
        { Categories: [categoryRequirement('A')], Name: 'A route', Active: true, GroupId: 'routes' },
        { Categories: [categoryRequirement('A')], Name: 'Inactive A route', Active: false, GroupId: 'routes' },
        { Categories: [categoryRequirement('B')], Name: 'B route', Active: true, GroupId: 'routes' }
    ],
    ComboGroups: [{ Id: 'routes', Name: 'Routes' }],
    HandSize: 2,
    CalculationModel: 'Fast'
});

(async () => {
    const baseUrl = process.env.WORKSPACE_BASE_URL || 'http://127.0.0.1:5090';
    const evidence = process.env.WORKSPACE_SCREENSHOTS;
    const browser = await chromium.launch({ headless: true, channel: process.env.WORKSPACE_BROWSER_CHANNEL || 'msedge' });
    const page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
    const pageErrors = [];
    const consoleErrors = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    page.on('console', message => {
        if (message.type() === 'error') consoleErrors.push(message.text());
    });
    await page.addInitScript(() => { window.showSaveFilePicker = undefined; });

    const categoryDetails = page.locator('details.category-management');
    const groupDetails = page.locator('details.combo-group-management');
    const setExpanded = async (details, expanded) => {
        if (await details.evaluate(element => element.open) !== expanded) {
            await details.locator(':scope > summary').click();
        }
    };
    const setLayout = async (width, theme) => {
        await page.setViewportSize({ width, height: 1000 });
        await page.getByLabel('Color theme preference').selectOption(theme.toLowerCase());
    };
    const assertNoOverflow = async label => {
        const dimensions = await page.evaluate(() => ({
            viewport: innerWidth,
            document: document.documentElement.scrollWidth,
            body: document.body.scrollWidth
        }));
        assert.ok(dimensions.document <= dimensions.viewport + 1, `${label}: document overflow ${JSON.stringify(dimensions)}`);
        assert.ok(dimensions.body <= dimensions.viewport + 1, `${label}: body overflow ${JSON.stringify(dimensions)}`);
        const dialog = page.locator('[role="alertdialog"]');
        if (await dialog.count()) {
            const box = await dialog.boundingBox();
            assert.ok(box, `${label}: confirmation has a rendered box`);
            assert.ok(box.x >= -1 && box.x + box.width <= dimensions.viewport + 1, `${label}: confirmation exceeds viewport width`);
        }
    };
    const capture = async name => {
        if (!evidence) return;
        await fs.mkdir(evidence, { recursive: true });
        await page.locator('[role="alertdialog"]').scrollIntoViewIfNeeded().catch(() => {});
        await page.screenshot({ path: path.join(evidence, `${name}.png`) });
    };
    const load = async session => {
        await page.locator('#sessionFileInput').setInputFiles({
            name: 'category-deletion-fixture.json',
            mimeType: 'application/json',
            buffer: Buffer.from(JSON.stringify(session))
        });
        await page.waitForFunction(expected => {
            const categoryNames = [...document.querySelectorAll('details.category-management .category-chip')]
                .map(chip => chip.textContent.trim());
            const groupNames = [...document.querySelectorAll('.combo-group-chip')]
                .map(chip => chip.textContent.trim());
            return document.querySelector('.card-header-name')?.textContent.includes(expected.cardName)
                && document.querySelectorAll('.card-editor').length === expected.cardCount
                && document.querySelectorAll('.combo-editor').length === expected.comboCount
                && expected.categoryNames.every(name => categoryNames.some(actual => actual.includes(name)))
                && groupNames.length === expected.groupNames.length
                && expected.groupNames.every(name => groupNames.some(actual => actual.includes(name)));
        }, {
            cardName: session.Cards[0].Name,
            cardCount: session.Cards.length,
            comboCount: session.Combos.length,
            categoryNames: session.Categories.map(item => item.Name),
            groupNames: session.ComboGroups.map(item => item.Name)
        });
    };
    const saveSession = async () => {
        const downloadPromise = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Save Session', exact: true }).click();
        const download = await downloadPromise;
        const downloadPath = await download.path();
        assert.ok(downloadPath, 'session download completes');
        return JSON.parse(await fs.readFile(downloadPath, 'utf8'));
    };
    const openConfirmation = async (button, label) => {
        await button.click();
        const dialog = page.getByRole('alertdialog', { name: label });
        await dialog.waitFor();
        return dialog;
    };
    const cancelWithEscape = async dialog => {
        await page.waitForFunction(() => document.activeElement?.textContent?.trim() === 'Cancel');
        await page.keyboard.press('Escape');
        await dialog.waitFor({ state: 'detached' });
    };

    try {
        await page.goto(baseUrl, { waitUntil: 'domcontentloaded' });
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor({ timeout: 90000 });
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined, { timeout: 90000 });
        await load(sessionFixture());

        // A populated group requires confirmation. Cancel and Escape leave every
        // assignment, order, and inactive state intact at each tested layout.
        await setExpanded(groupDetails, true);
        const groupLayouts = [
            [1280, 'Light'], [1280, 'Dark'], [390, 'Light'], [390, 'Dark']
        ];
        for (let index = 0; index < groupLayouts.length; index++) {
            const [width, theme] = groupLayouts[index];
            await setLayout(width, theme);
            const dialog = await openConfirmation(page.getByRole('button', { name: 'Remove group Routes' }), 'Confirm group deletion');
            assert.match(await dialog.innerText(), /All member combos, including inactive ones, remain in their current order and become ungrouped/);
            await assertNoOverflow(`group confirmation ${width}px ${theme}`);
            await capture(`group-confirm-${width}-${theme.toLowerCase()}`);
            if (index % 2 === 0) {
                await page.getByRole('button', { name: 'Cancel', exact: true }).click();
                await dialog.waitFor({ state: 'detached' });
            }
            else {
                await cancelWithEscape(dialog);
            }
            assert.equal(await page.locator('.combo-group-chip').count(), 1);
            const preserved = await saveSession();
            assert.deepEqual(preserved.Combos.map(combo => combo.GroupId), ['routes', 'routes', 'routes']);
            assert.deepEqual(preserved.Combos.map(combo => combo.Active), [true, false, true]);
            await assertNoOverflow(`group cancel ${width}px ${theme}`);
        }

        const groupDialog = await openConfirmation(page.getByRole('button', { name: 'Remove group Routes' }), 'Confirm group deletion');
        await page.getByRole('button', { name: 'Delete', exact: true }).click();
        await groupDialog.waitFor({ state: 'detached' });
        assert.equal(await page.locator('.combo-group-chip').count(), 0);
        assert.equal(await page.locator('.combo-group-membership').count(), 0);
        const ungrouped = await saveSession();
        assert.deepEqual(ungrouped.Combos.map(combo => combo.Name), ['A route', 'Inactive A route', 'B route']);
        assert.deepEqual(ungrouped.Combos.map(combo => combo.Active), [true, false, true]);
        assert.deepEqual(ungrouped.Combos.map(combo => combo.GroupId ?? null), [null, null, null]);
        process.stdout.write('Group Cancel/Escape/confirm passed.\n');

        // Editing a category name creates a draft. Both Cancel and Escape on
        // deletion confirmation preserve that draft; final confirmation removes
        // every matching reference, including inactive nested effect filters.
        await load(sessionFixture());
        await setExpanded(categoryDetails, true);
        await page.getByRole('button', { name: 'Edit category A' }).click();
        const renameDraft = page.getByRole('textbox', { name: 'New name for category A' });
        await renameDraft.fill('Unsaved');
        const categoryLayouts = [
            [1280, 'Light'], [1280, 'Dark'], [390, 'Light'], [390, 'Dark']
        ];
        for (let index = 0; index < categoryLayouts.length; index++) {
            const [width, theme] = categoryLayouts[index];
            await setLayout(width, theme);
            const dialog = await openConfirmation(page.getByRole('button', { name: 'Remove category A' }), 'Confirm category deletion');
            assert.match(await dialog.innerText(), /including inactive entries/);
            assert.match(await dialog.innerText(), /Emptied combos become inactive/);
            assert.match(await dialog.innerText(), /emptied effect filters match no cards/);
            await assertNoOverflow(`category confirmation ${width}px ${theme}`);
            await capture(`category-confirm-${width}-${theme.toLowerCase()}`);
            if (index % 2 === 0) {
                await page.getByRole('button', { name: 'Cancel', exact: true }).click();
                await dialog.waitFor({ state: 'detached' });
            }
            else {
                await cancelWithEscape(dialog);
            }
            assert.equal(await page.getByRole('textbox', { name: 'New name for category A' }).inputValue(), 'Unsaved');
            await assertNoOverflow(`category cancel ${width}px ${theme}`);
        }

        await setLayout(1280, 'Light');
        const deletionDialog = await openConfirmation(page.getByRole('button', { name: 'Remove category A' }), 'Confirm category deletion');
        await page.getByRole('button', { name: 'Delete', exact: true }).click();
        await deletionDialog.waitFor({ state: 'detached' });
        await page.waitForFunction(() => document.querySelectorAll('.category-chip').length >= 1
            && ![...document.querySelectorAll('.category-chip')].some(chip => chip.textContent.includes('A')));
        const newCategoryInput = page.getByRole('textbox', { name: 'Category name', exact: true });
        await page.waitForFunction(() => document.activeElement === document.querySelector('input[aria-label="Category name"]'));
        assert.equal(await newCategoryInput.evaluate(element => element === document.activeElement), true,
            'confirming deletion while editing focuses the new-category input');
        await page.waitForTimeout(300);
        const appErrorAfterDelete = await page.locator('#blazor-error-ui').isVisible().catch(() => false);
        if (appErrorAfterDelete) {
            process.stderr.write(`Browser errors after category delete: ${JSON.stringify({ pageErrors, consoleErrors })}\n`);
            await capture('category-delete-error');
        }
        assert.equal(appErrorAfterDelete, false, 'category deletion does not show the Blazor unhandled-error banner');

        const deleted = await saveSession();
        assert.deepEqual(deleted.Categories.map(item => item.Name), ['B']);
        assert.deepEqual(deleted.Cards[2].Categories.map(item => item.Name), ['B']);
        assert.equal(deleted.Cards[2].Active, false);
        assert.deepEqual(deleted.Cards[2].PreDrawSelector, { Kind: 'Category', Categories: ['user:B'] });
        assert.deepEqual(deleted.Cards[2].PostDrawSelector, { Kind: 'Not', Children: [{ Kind: 'Any', Categories: [] }] });
        assert.equal(JSON.stringify(deleted).includes('user:A'), false);
        assert.deepEqual(deleted.Combos.map(combo => combo.Name), ['A route', 'Inactive A route', 'B route']);
        assert.deepEqual(deleted.Combos.map(combo => combo.Active), [false, false, true]);
        assert.deepEqual(deleted.Combos.map(combo => combo.Categories.length), [0, 0, 1]);
        assert.equal(await page.locator('.combo-editor').nth(0).evaluate(element => element.classList.contains('entry-inactive')), true);

        const inactiveDraw = page.locator('.card-editor').nth(2);
        await inactiveDraw.locator('.accordion-button').click();
        assert.match(await inactiveDraw.locator('.simulation-summary').innerText(), /no eligible cards/);
        await assertNoOverflow('confirmed category removal 1280px light');
        await capture('category-deleted-1280-light');
        process.stdout.write('Category Cancel/Escape/draft/reference cleanup passed.\n');

        // Pin a real calculated result, delete a category, and verify the
        // current result becomes stale while the accepted pin stays identical.
        await load(sessionFixture());
        await setExpanded(categoryDetails, true);
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.locator('.probability-results').waitFor({ timeout: 90000 });
        await page.getByRole('button', { name: 'Pin result' }).waitFor({ state: 'visible', timeout: 90000 });
        await page.getByRole('button', { name: 'Pin result' }).click();
        await page.locator('.pinned-result').waitFor();
        const pinnedBefore = await page.locator('.pinned-result').innerText();
        const pinnedMarkupBefore = await page.locator('.pinned-result').innerHTML();
        await page.getByRole('button', { name: 'Remove category A' }).click();
        const pinDialog = page.getByRole('alertdialog', { name: 'Confirm category deletion' });
        await pinDialog.waitFor();
        await page.getByRole('button', { name: 'Delete', exact: true }).click();
        await pinDialog.waitFor({ state: 'detached' });
        await page.waitForFunction(() => ![...document.querySelectorAll('details.category-management .category-chip')]
            .some(chip => chip.textContent.includes('A')));
        const staleMarked = await page.waitForFunction(() =>
            [...document.querySelectorAll('.probability-result-status')]
                .some(status => status.textContent.trim() === 'Inputs changed'),
        { timeout: 10000 }).then(() => true).catch(() => false);
        if (!staleMarked) {
            const diagnosis = await page.evaluate(() => ({
                results: document.querySelector('.probability-results')?.innerText,
                staleStatus: document.querySelector('.probability-result-status')?.innerText,
                categories: [...document.querySelectorAll('details.category-management .category-chip')].map(chip => chip.innerText),
                pinned: document.querySelector('.pinned-result')?.innerText
            }));
            process.stderr.write(`Stale-result diagnostic: ${JSON.stringify(diagnosis)}\n`);
            await capture('pinned-stale-diagnostic');
        }
        assert.equal(staleMarked, true, 'category deletion marks the displayed calculation stale');
        assert.equal(await page.locator('.pinned-result').innerText(), pinnedBefore);
        assert.equal(await page.locator('.pinned-result').innerHTML(), pinnedMarkupBefore);
        for (const [width, theme] of [[1280, 'Light'], [1280, 'Dark'], [390, 'Light'], [390, 'Dark']]) {
            await setLayout(width, theme);
            await assertNoOverflow(`pinned stale result ${width}px ${theme}`);
        }
        await capture('pinned-stale-390-dark');

        assert.deepEqual(pageErrors, [], `browser page errors: ${pageErrors.join('; ')}`);
        const rendererFailures = consoleErrors.filter(message =>
            message.includes('Unhandled exception rendering component')
                || message.includes('crit: Microsoft.AspNetCore.Components.WebAssembly.Rendering.WebAssemblyRenderer'));
        assert.deepEqual(rendererFailures, [], `Blazor rendering failures: ${rendererFailures.join('; ')}`);
        process.stdout.write('Category deletion browser smoke passed: category Cancel/Escape draft preservation, confirmed nested-reference cleanup, emptied combo deactivation, no-eligible effect presentation, group confirmation/unassignment, pinned-result stability, and 1280/390 light/dark overflow checks.\n');
        if (evidence) process.stdout.write(`Screenshots: ${evidence}\n`);
    }
    finally {
        await browser.close();
    }
})().catch(error => {
    console.error(error);
    process.exitCode = 1;
});
