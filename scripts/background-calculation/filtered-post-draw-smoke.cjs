// Opt-in against the optimized publish and ExportFilteredPostDrawWorkerCases output.
// NODE_PATH=<Playwright modules> YGO_BROWSER_CHANNEL=msedge node
// scripts/background-calculation/filtered-post-draw-smoke.cjs <wwwroot> <cases.json> <screenshots>
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const root = path.resolve(process.argv[2]);
    const cases = JSON.parse(await fs.readFile(process.argv[3], 'utf8'));
    const screenshots = path.resolve(process.argv[4]);
    await fs.mkdir(screenshots, { recursive: true });
    const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm',
        '.html': 'text/html', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = new URL(request.url, 'http://localhost').pathname;
            const file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname));
            assert(file.startsWith(root + path.sep));
            response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
            response.end(await fs.readFile(file));
        } catch {
            response.writeHead(404).end();
        }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const browser = await chromium.launch({ headless: true,
        ...(process.env.YGO_BROWSER_CHANNEL ? { channel: process.env.YGO_BROWSER_CHANNEL } : {}) });
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
        await page.addInitScript(() => { window.showSaveFilePicker = undefined; });
        const errors = [];
        page.on('pageerror', error => { errors.push(error.message); console.error(error.message); });
        page.on('console', message => {
            if (message.type() === 'error' && !message.text().includes('Failed to load resource')) { errors.push(message.text()); console.error(message.text()); }
        });
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        const ready = () => page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        await ready();
        const gear = page.getByRole('button', { name: 'Calculation settings', exact: true });
        const model = page.locator('#calculationModel');
        const storage = page.locator('#storageSafetyTier');
        async function settings() {
            if (await gear.getAttribute('aria-expanded') !== 'true') await gear.click();
        }
        async function closeSettings() {
            await storage.focus();
            await page.keyboard.press('Escape');
        }
        async function load(session) {
            await page.locator('#sessionFileInput').setInputFiles({ name: 'filtered.json', mimeType: 'application/json',
                buffer: Buffer.from(JSON.stringify(session)) });
            await page.waitForFunction(expected => {
                try {
                    const card = JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Cards[0];
                    return card.Name === expected.name && (card.PostDrawDiscardCount || 0) === expected.removal;
                } catch { return false; }
            }, { name: session.Cards[0].Name, removal: session.Cards[0].PostDrawDiscardCount || 0 });
        }
        async function expand(index) {
            const button = page.locator('.card-editor .accordion-button').nth(index);
            if (await button.getAttribute('aria-expanded') !== 'true') await button.click();
            const editor = page.locator('.card-editor').nth(index);
            if (!await editor.locator('.simulation-editor').count()) await editor.locator('.simulation-edit').click();
        }
        async function saveSimulation(index) {
            await page.locator('.card-editor').nth(index).locator('.simulation-save').click();
            await expand(index);
        }
        async function saved() {
            const download = page.waitForEvent('download');
            await page.getByRole('button', { name: 'Save Session', exact: true }).click();
            return JSON.parse(await fs.readFile(await (await download).path(), 'utf8'));
        }
        async function result() {
            await page.getByRole('button', { name: 'Calculate', exact: true }).click();
            await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
            return Number((await page.locator('.probability-total-value').innerText()).replace('%', '').replace(',', '.').trim());
        }
        await settings();
        assert.equal(await model.inputValue(), 'Configured');
        await closeSettings();
        for (const [id, name, identities, opt] of [
            [1475311, 'Allure of Darkness', ['metadata:attribute:dark'], false],
            [20508881, 'Radiant Typhoon Vision', ['metadata:archetype:radiant%20typhoon', 'metadata:spell-type:quick-play'], true]
        ]) {
            await load({ SchemaVersion: 4, HandSize: 1, Cards: [{ Id: 'selection', Name: `Select ${id}`, Copies: 1, Categories: [] }] });
            await expand(0);
            await page.locator('#cardName0').fill(name);
            await page.getByRole('option', { name, exact: true }).click();
            await expand(0);
            assert.equal(await page.locator('#cardDraw0').inputValue(), '2');
            assert.equal(await page.locator('#cardDiscard0').inputValue(), '1');
            assert.equal(await page.locator('#cardDrawOnce0').isChecked(), opt);
            const selection = await saved();
            assert.deepEqual(selection.Cards[0].PostDrawSelector.Categories, identities);
            assert.equal(selection.Cards[0].PostDrawFailureBehavior, 'FailBranch');
            await page.locator('#cardEligibility0').selectOption('any');
            await saveSimulation(0);
            assert.equal((await saved()).Cards[0].PostDrawSelector, undefined);
            for (const identity of identities) {
                await page.locator('#cardEligibility0').selectOption(identity);
                await saveSimulation(0);
            }
            const restored = await saved();
            assert.deepEqual(restored.Cards[0].PostDrawSelector.Categories, identities);
            restored.Cards[0].Name = `Round trip ${id}`;
            await load(restored);
            await expand(0);
            assert.deepEqual((await saved()).Cards[0].PostDrawSelector.Categories, identities);
            await page.locator('#cardEligibility0').focus();
            assert.equal(await page.locator('#cardEligibility0').evaluate(element => element === document.activeElement), true);
            await page.keyboard.press('Tab');
            assert.match(await page.evaluate(() => document.activeElement.getAttribute('aria-label')), /Remove eligibility/);
            await page.keyboard.press('Enter');
            await saveSimulation(0);
            assert.equal((await saved()).Cards[0].PostDrawSelector?.Categories.length || 0, identities.length - 1);
            await page.locator('#cardEligibility0').selectOption(identities[0]);
            await saveSimulation(0);
        }
        await page.locator('#fileInput').setInputFiles({ name: 'filtered.ydk', mimeType: 'text/plain',
            buffer: Buffer.from('#main\n1475311\n20508881\n89631139\n#extra\n!side\n') });
        await page.waitForFunction(() => document.querySelectorAll('.card-editor').length === 3);
        const imported = await saved();
        assert.deepEqual(imported.Cards.find(card => card.ExternalCardId === 1475311).PostDrawSelector.Categories, ['metadata:attribute:dark']);
        assert.equal(imported.Cards.find(card => card.ExternalCardId === 20508881).PostDrawSelector.Kind, 'AnyOf');
        await expand(1);
        for (const width of [1280, 768, 390, 375]) {
            for (const theme of ['light', 'dark']) {
                await page.setViewportSize({ width, height: 1000 });
                await page.getByLabel('Color theme preference').selectOption(theme);
                await page.locator('#cardEligibility1').scrollIntoViewIfNeeded();
                const geometry = await page.evaluate(() => ({ width: document.documentElement.clientWidth, content: document.documentElement.scrollWidth }));
                assert(geometry.content <= geometry.width, JSON.stringify({ width, theme, geometry }));
                await page.screenshot({ path: path.join(screenshots, `filtered-${width}-${theme}.png`) });
                console.log(JSON.stringify({ width, theme, geometry }));
            }
        }
        await page.setViewportSize({ width: 1280, height: 1000 });
        for (const fixture of cases.filter(value => value.Name.startsWith('small-') && value.Input.Cards.some(card => card.PostDrawDiscardCount > 0))) {
            fixture.Session.Cards[0].Name = fixture.Name;
            await load(fixture.Session);
            await settings();
            await model.selectOption('Configured');
            await closeSettings();
            const custom = structuredClone(fixture.Session);
            custom.Cards[0].PostDrawDiscardCount = 0;
            custom.Cards[0].PostDrawSelector = { Kind: 'Any', Categories: [] };
            await load(custom);
            assert.equal(await result(), 100, `${fixture.Name}: custom removal-free`);
            await load(fixture.Session);
            await settings();
            await model.selectOption('Configured');
            await closeSettings();
            assert.equal(await result(), Number((fixture.Probability * 100).toFixed(2)), `${fixture.Name}: Advanced`);
            await settings();
            await model.selectOption('OpeningHand');
            assert.equal(await model.inputValue(), 'OpeningHand');
            await closeSettings();
            assert.equal(await result(), 100, `${fixture.Name}: global off`);
        }
        const representative = cases.find(value => value.Name === 'filtered-20508881-advanced');
        await load(representative.Session);
        await settings();
        await model.selectOption('Configured');
        for (const tier of ['Standard', 'High', 'VeryHigh']) {
            await storage.selectOption(tier);
            await closeSettings();
            assert.equal(await result(), Number((representative.Probability * 100).toFixed(2)));
            await settings();
        }
        await closeSettings();
        const persisted = await saved();
        assert.equal(persisted.ConditionalEffectsEnabled, undefined);
        assert.equal(persisted.StorageTier, undefined);
        await page.reload();
        await ready();
        await settings();
        assert.equal(await model.inputValue(), 'Configured');
        assert.equal(await storage.inputValue(), 'Standard');
        assert.deepEqual(errors, []);
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        console.log('PASS: curated search/import, clear/edit/recreate, save/load, keyboard removal, 8 layouts, configured/custom/failure/opening-only, storage tiers, reload defaults, no Blazor/JS errors.');
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
