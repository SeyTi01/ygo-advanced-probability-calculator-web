// node scripts/workspace/browser-smoke.cjs <optimized-wwwroot>
// Browser-only focus, native keyboard/history, download/upload and reload integration.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const { serve, load, usable, launchOptions } = require('../background-calculation/browser-support.cjs');

async function main() {
    assert(process.argv[2], 'Provide a clean optimized published wwwroot directory');
    const { server, url } = await serve(process.argv[2]);
    const browser = await chromium.launch(launchOptions()).catch(error => { server.close(); throw error; });
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, locale: 'en-US' });
        page.setDefaultTimeout(30000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.addInitScript(() => { window.showSaveFilePicker = undefined; });
        await page.goto(url);
        const roles = ['Alpha', 'Beta'].map(Name => ({ Name, Source: 'User' }));
        const session = {
            SchemaVersion: 14, CalculationModel: 'OpeningHand', FastExecutionStrategy: 'Progressive', HandSize: 1, Categories: roles,
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
            SchemaVersion: 14, CalculationModel: 'Fast', FastExecutionStrategy: 'Progressive', HandSize: 1,
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
        await toggle.focus(); await page.keyboard.press('Space');
        await source.getByRole('button', { name: 'Disable simulation', exact: true }).waitFor();
        await source.locator('.simulation-cancel').click();
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') === 'false'
            && document.querySelector('.probability-total-value'));
        await page.getByRole('button', { name: 'Pin result', exact: true }).click();

        // Keep the live workspace (including unsaved editor state) through app links
        // and real browser history, while keeping Help out of the calculator's tab order.
        await source.locator('.simulation-edit').click();
        await source.locator('#cardDraw0').selectOption('3');
        const retainedCombo = page.locator('.combo-editor').first();
        await retainedCombo.locator('.accordion-button').click();
        await retainedCombo.locator('[data-requirement="card:0"]').click();
        await retainedCombo.locator('#minCount0').fill('0');
        await page.evaluate(() => { window.retainedWorkspace = document.querySelector('.calculator-workspace'); });
        await page.waitForFunction(() => JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload)
            .Cards[0].SimulationEnabled === true);
        const acceptedBeforeHelp = await page.evaluate(() => JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload);
        const resultBeforeHelp = await page.locator('.probability-total .probability-with-accuracy').getAttribute('title');
        async function retainedCalculator() {
            await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
            assert.equal(await page.evaluate(() => window.retainedWorkspace === document.querySelector('.calculator-workspace')), true);
            assert.equal(await source.locator('#cardDraw0').inputValue(), '3', 'simulation draft survives Help');
            assert.equal(await retainedCombo.locator('#minCount0').inputValue(), '0', 'requirement draft survives Help');
            assert.equal(await page.locator('.probability-total .probability-with-accuracy').getAttribute('title'), resultBeforeHelp);
            assert.equal(await page.locator('.pinned-result').isVisible(), true);
            assert.equal(await page.evaluate(() => JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload), acceptedBeforeHelp);
            assert.equal(await page.getByRole('button', { name: 'Restore previous session', exact: true }).count(), 0);
        }
        async function retainedHelp() {
            await page.locator('.help-content').waitFor();
            assert.equal(await page.locator('.calculator-workspace').isVisible(), false);
            assert.equal(await page.getByRole('button', { name: 'Calculate', exact: true }).count(), 0, 'hidden workspace is absent from the accessibility tree');
            await usable(page, page.getByRole('link', { name: 'Back', exact: true }));
        }
        await page.getByRole('link', { name: 'Help', exact: true }).click(); await retainedHelp();
        await page.getByRole('link', { name: 'Back', exact: true }).click(); await retainedCalculator();
        await page.goBack(); await retainedHelp();
        await page.goBack(); await retainedCalculator();
        await page.goForward(); await retainedHelp();
        await page.goForward(); await retainedCalculator();
        await source.locator('.simulation-cancel').click();
        await retainedCombo.locator('#minCount0').press('Escape');
        await page.getByRole('link', { name: 'Help', exact: true }).click();
        for (const locale of ['de', 'en']) {
            await Promise.all([page.waitForEvent('load'), page.locator('#language-preference').selectOption(locale)]);
            await page.waitForFunction(() => !document.documentElement.classList.contains('locale-pending')
                && sessionStorage.getItem('ygo-calculator:language-handoff:v1') === null);
            assert.equal(await page.locator('.help-content').isVisible(), true);
            assert.equal(await page.locator('.calculator-workspace').isVisible(), false);
            assert.equal(await page.evaluate(() => JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload), acceptedBeforeHelp);
        }
        await page.getByRole('link', { name: 'Back', exact: true }).click();
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        assert.equal(await page.locator('.probability-total .probability-with-accuracy').getAttribute('title'), resultBeforeHelp);
        assert.equal(await page.locator('.pinned-result').isVisible(), true);
        assert.deepEqual(errors, []);
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        await page.getByRole('link', { name: 'Help', exact: true }).click();
        await page.locator('details.help-topic').first().waitFor();
        assert.equal(await page.locator('details.help-topic[open]').count(), 0);
        const example = page.locator('.example-session-action button');
        await usable(page, example);
        // Native disclosure behavior needs a real browser; topic inventories and
        // markup associations are covered by component/published-output tests.
        const summary = page.locator('#sessions > summary');
        await usable(page, summary);
        await summary.focus();
        await summary.press('Enter');
        assert.equal(await page.locator('#sessions').evaluate(element => element.open), true);
        await usable(page, page.locator('#sessions'));
        await summary.press('Space');
        assert.equal(await page.locator('#sessions').evaluate(element => element.open), false);
        await page.keyboard.press('Tab');
        assert.equal(await page.locator('#example > summary').evaluate(element => element === document.activeElement), true,
            'closed disclosures remove body links from keyboard navigation');

        await page.goto(`${url}/help#example`);
        await page.waitForFunction(() => document.getElementById('example')?.open);
        await page.waitForFunction(() => {
            const box = document.querySelector('#example > summary')?.getBoundingClientRect();
            return box && box.top >= 0 && box.bottom <= innerHeight;
        });
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
        const sample = JSON.parse(fs.readFileSync(path.join(process.argv[2], 'sample-data/example_session_state.json'), 'utf8'));
        await example.click();
        await page.waitForFunction(name => {
            try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Cards[0].Name === name; }
            catch { return false; }
        }, sample.Cards[0].Name);
        assert.equal(await page.locator('.card-editor').count(), sample.Cards.length);
        assert.equal(await page.locator('.combo-editor').count(), sample.Combos.length);
        // A fresh startup has no tab-local live workspace to resume. An older
        // recovery record remains an explicit offer, even when arriving from Help.
        await page.goto(`${url}/help`);
        await page.getByRole('link', { name: 'Back', exact: true }).waitFor();
        assert.equal(await page.locator('#sessionFileInput').count(), 0);
        await page.getByRole('link', { name: 'Back', exact: true }).click();
        await page.getByRole('button', { name: 'Restore previous session', exact: true }).waitFor();
        assert.equal(await page.locator('.card-editor').count(), 0, 'fresh startup does not silently load an older recovery draft');
        assert.deepEqual(errors, []);
        console.log('Published workspace: real keyboard/focus, narrow-screen move and deletion, download/upload round trip passed.');
        console.log('Published Help: retained drafts/results, browser history and locale reload, native disclosure/tab order, fragment navigation, shipped example and fresh-start recovery passed.');
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
