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

        await load(page, {
            SchemaVersion: 13, CalculationModel: 'Fast', FastExecutionStrategy: 'Progressive', HandSize: 1,
            Cards: [{ Id: 'draw', Name: 'Toggle source', Copies: 1, Categories: [], DrawCount: 1 },
                { Id: 'target', Name: 'Toggle target', Copies: 1, Categories: [] },
                { Id: 'blank', Name: 'Toggle blank', Copies: 1, Categories: [] }],
            Combos: [{ Name: 'Target route', Categories: [], Cards: [{ CardId: 'target', MinCount: 1, MaxCount: 3 }] }]
        });
        const source = page.locator('.card-editor').first();
        await source.locator('.accordion-button').click();
        await source.locator('.simulation-edit').click();
        await source.locator('#cardDraw0').selectOption('2');
        const toggle = source.locator('.simulation-toggle');
        await usable(page, toggle);
        await toggle.focus(); await page.keyboard.press('Enter');
        await source.getByRole('button', { name: 'Enable simulation', exact: true }).waitFor();
        assert.equal(await source.locator('#cardDraw0').inputValue(), '2', 'toggle retains the editor draft');
        await source.locator('.simulation-cancel').click();
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.probability-total-value')?.closest('[aria-label]')?.getAttribute('aria-label').includes('1/3'));
        const toggleDownload = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Save Session', exact: true }).click();
        const disabled = JSON.parse(fs.readFileSync(await (await toggleDownload).path(), 'utf8'));
        assert.equal(disabled.Cards[0].SimulationEnabled, false);
        assert.equal(disabled.Cards[0].DrawCount, 1, 'draft was not saved by the toggle');
        disabled.Cards[0].Name = 'Reloaded toggle source';
        await load(page, disabled);
        await source.locator('.accordion-button').click();
        await source.getByRole('button', { name: 'Enable simulation', exact: true }).waitFor();
        await toggle.focus(); await page.keyboard.press('Space');
        await source.getByRole('button', { name: 'Disable simulation', exact: true }).waitFor();
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.probability-total-value')?.closest('[aria-label]')?.getAttribute('aria-label').includes('1/2'));
        assert.deepEqual(errors, []);
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        await page.getByRole('link', { name: 'Help', exact: true }).click();
        const topics = ['getting-started', 'requirements', 'simulations', 'modes', 'results', 'sessions', 'example'];
        await page.locator('details.help-topic').first().waitFor();
        assert.equal(await page.locator('details.help-topic[open]').count(), 0);
        const example = page.getByRole('button', { name: 'Try It with Example Data', exact: true });
        assert.equal(await example.count(), 1);
        for (const width of [1280, 390]) {
            await page.setViewportSize({ width, height: 900 });
            for (const theme of ['Light', 'Dark']) {
                await page.getByLabel('Color theme preference').selectOption({ label: theme });
                await usable(page, example);
                const actionBox = await example.boundingBox();
                assert(actionBox.y + actionBox.height < 900, 'example is prominent in the initial view');
                for (const id of topics) {
                    const summary = page.locator(`#${id} > summary`);
                    await usable(page, summary);
                    await summary.focus();
                    await summary.press('Enter');
                    assert.equal(await page.locator(`#${id}`).evaluate(element => element.open), true);
                    await summary.press('Space');
                    assert.equal(await page.locator(`#${id}`).evaluate(element => element.open), false);
                }
                // Native closed disclosures remove body links from keyboard navigation.
                await page.locator('#sessions > summary').focus();
                await page.keyboard.press('Tab');
                assert.equal(await page.locator('#example > summary').evaluate(element => element === document.activeElement), true);
            }
        }
        for (const id of topics) {
            await page.goto('about:blank');
            await page.goto(`${url}/help#${id}`);
            await page.waitForFunction(id => document.getElementById(id)?.open, id);
            assert.equal(await page.locator('details.help-topic[open]').count(), 1);
            await page.waitForFunction(id => {
                const box = document.querySelector(`#${id} > summary`)?.getBoundingClientRect();
                return box && box.top >= 0 && box.bottom <= innerHeight;
            }, id);
        }
        // Real same-page links go through Blazor navigation; history must reveal closed topics again.
        await page.evaluate(() => {
            const link = document.createElement('a');
            link.href = '/help#requirements'; link.textContent = 'Fragment test'; link.id = 'fragment-test';
            document.querySelector('.help-content').prepend(link);
        });
        await page.locator('#fragment-test').click();
        await page.waitForFunction(() => document.getElementById('requirements').open);
        await page.locator('#requirements > summary').click();
        await page.goBack();
        await page.waitForFunction(() => location.hash === '#example' && document.getElementById('example').open);
        await page.goForward();
        await page.waitForFunction(() => location.hash === '#requirements' && document.getElementById('requirements').open);
        await page.locator('#requirements > summary').click();
        await page.locator('#fragment-test').click();
        await page.waitForFunction(() => document.getElementById('requirements').open);
        await page.goto(`${url}/help`);
        await example.click();
        await page.waitForFunction(() => document.querySelectorAll('.card-editor').length === 24);
        assert.equal(await page.locator('.combo-editor').count(), 8);
        assert.deepEqual(errors, []);
        console.log('Published Help: seven native disclosures, Enter/Space, hidden-link tab order, all direct fragments, links/back/forward, one real example load, desktop/mobile and light/dark passed.');
        console.log('Published workspace: keyboard editing, movement, drafts, per-entry simulation toggle, real worker results and file round trip passed at desktop/mobile sizes.');
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
