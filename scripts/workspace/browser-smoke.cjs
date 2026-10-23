// node scripts/workspace/browser-smoke.cjs <optimized-wwwroot>
// Representative real keyboard, draft, persistence and responsive interactions.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
const { serve, load, usable, launchOptions } = require('../background-calculation/browser-support.cjs');

async function main() {
    assert(process.argv[2], 'Provide a clean optimized published wwwroot directory');
    const { server, url } = await serve(process.argv[2]);
    const browser = await chromium.launch(launchOptions()).catch(error => { server.close(); throw error; });
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
        page.setDefaultTimeout(30000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.addInitScript(() => { window.showSaveFilePicker = undefined; });
        await page.goto(url);
        const roles = ['Alpha', 'Beta'].map(Name => ({ Name, Source: 'User' }));
        const session = {
            SchemaVersion: 12, CalculationModel: 'OpeningHand', FastExecutionStrategy: 'Progressive', HandSize: 1, Categories: roles,
            Cards: roles.map((category, index) => ({ Id: `c${index}`, Name: `Card ${index}`, Copies: 1, Categories: [category] })),
            Combos: roles.map((category, index) => ({ Name: `Route ${index}`, Categories: [{ BaseCategory: category, MinCount: 1, MaxCount: 1 }] })),
            ComboGroups: []
        };
        await load(page, session);
        const editor = page.locator('.combo-editor').first();
        await editor.locator('.accordion-button').click();
        const requirement = editor.locator('[data-requirement="category:0"]');
        await requirement.click();
        const minimum = editor.locator('#minCount0');
        await minimum.fill('0');
        await minimum.press('Escape');
        assert.equal(await requirement.evaluate(element => element === document.activeElement), true);
        await requirement.click();
        assert.equal(await minimum.inputValue(), '1', 'Escape discards requirement draft');
        await minimum.press('Escape');

        const categories = page.locator('details.category-management');
        await categories.locator('summary').click();
        await categories.getByRole('button', { name: 'Edit category Alpha', exact: true }).click();
        const rename = categories.getByRole('textbox', { name: 'New name for category Alpha', exact: true });
        await rename.fill('Unsaved sentinel');
        await page.setViewportSize({ width: 390, height: 900 });
        await page.getByLabel('Color theme preference').selectOption({ label: 'Dark' });
        const move = categories.getByRole('button', { name: 'Move category Alpha to position', exact: true });
        await usable(page, move); await move.click();
        const destination = categories.locator('.move-position-panel').getByRole('textbox');
        await page.waitForFunction(() => document.activeElement?.matches('.move-position-panel input'));
        await destination.fill('0'); await destination.press('Enter');
        await categories.locator('.move-position-panel').getByRole('alert').waitFor();
        await destination.press('Escape');
        assert.equal(await rename.inputValue(), 'Unsaved sentinel');
        assert.equal(await move.evaluate(element => element === document.activeElement), true);
        await move.click(); await destination.fill('2'); await destination.press('Enter');
        await page.waitForFunction(() => !document.querySelector('.move-position-panel'));
        assert.equal(await rename.inputValue(), 'Unsaved sentinel', 'moving keeps category draft');

        await categories.getByRole('button', { name: 'Remove category Alpha', exact: true }).click();
        const dialog = page.getByRole('alertdialog', { name: 'Confirm category deletion' });
        await dialog.waitFor();
        await usable(page, dialog.getByRole('button', { name: 'Cancel', exact: true }));
        await page.waitForFunction(() => document.activeElement?.textContent?.trim() === 'Cancel');
        await page.keyboard.press('Escape'); await dialog.waitFor({ state: 'detached' });
        assert.equal(await rename.inputValue(), 'Unsaved sentinel', 'cancelled deletion preserves draft');
        await categories.getByRole('button', { name: 'Cancel category edit', exact: true }).click();

        const downloadPromise = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Save Session', exact: true }).click();
        const saved = JSON.parse(fs.readFileSync(await (await downloadPromise).path(), 'utf8'));
        assert.deepEqual(saved.Categories.map(category => category.Name), ['Beta', 'Alpha']);
        assert.deepEqual(saved.Combos.map(combo => combo.Name), ['Route 0', 'Route 1']);
        assert(!JSON.stringify(saved).includes('Unsaved sentinel'));
        saved.Cards[0].Name = 'Reloaded sentinel';
        await load(page, saved);
        assert.equal(await page.locator('.card-editor').count(), 2);
        assert.equal(await page.locator('.combo-editor').count(), 2);
        await usable(page, page.getByRole('button', { name: 'Calculate', exact: true }));
        assert.deepEqual(errors, []);
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        console.log('Published workspace: keyboard editing, movement, deletion cancellation, drafts and file round trip passed at desktop/mobile sizes.');
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
