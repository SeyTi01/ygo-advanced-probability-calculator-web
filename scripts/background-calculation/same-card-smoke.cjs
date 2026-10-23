// Opt-in: published wwwroot, ExportSameCardWorkerCases JSON, screenshot directory.
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
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const browser = await chromium.launch({ headless: true,
        ...(process.env.YGO_BROWSER_CHANNEL ? { channel: process.env.YGO_BROWSER_CHANNEL } : {}) });
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
        await page.addInitScript(() => { window.showSaveFilePicker = undefined; });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => {
            if (message.type() === 'error' && !message.text().includes('Failed to load resource')) errors.push(message.text());
        });
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        async function load(session) {
            await page.locator('#sessionFileInput').setInputFiles({ name: 'same-card.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(session)) });
            await page.waitForFunction(name => document.querySelector('.card-header-name')?.textContent.includes(name), session.Cards[0].Name);
        }
        async function saved() {
            const download = page.waitForEvent('download');
            await page.getByRole('button', { name: 'Save Session', exact: true }).click();
            return JSON.parse(await fs.readFile(await (await download).path(), 'utf8'));
        }
        async function expand(selector) {
            const button = page.locator(selector).first();
            if (await button.getAttribute('aria-expanded') !== 'true') await button.click();
        }
        await page.getByRole('link', { name: 'Help', exact: true }).click();
        await page.getByRole('button', { name: 'Try It with Example Data', exact: true }).click();
        await page.waitForFunction(() => document.querySelectorAll('.combo-editor').length === 8
            || getComputedStyle(document.querySelector('#blazor-error-ui')).display !== 'none');
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false, errors.join('\n'));
        assert.equal(await page.locator('.card-editor').count(), 24);
        assert.equal(await page.locator('.combo-editor').count(), 8);
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
        const exampleResult = Number((await page.locator('.probability-total-value').innerText()).replace('%', '').replace(',', '.').trim());
        assert.equal(exampleResult, 85.92, 'Bundled example total');

        await load(cases.find(item => item.Name === 'atomic').Session);
        await expand('.combo-editor .accordion-button');
        await page.getByRole('button', { name: 'Add AND', exact: true }).click();
        await page.locator('[data-and-target="category:0"]').click();
        await page.locator('#comboCategory0').selectOption('metadata:card-type:monster');
        await page.locator('#minCount0').fill('2');
        await page.locator('#maxCount0').fill('');
        await page.getByRole('button', { name: 'Add AND', exact: true }).click();
        let session = await saved();
        assert.equal(session.Combos[0].Categories[0].Selector.Kind, 'AllOf');
        assert.equal(session.Combos[0].Categories[0].MinCount, 2);
        assert.equal(session.Combos[0].Categories[0].MaximumMode, 'HandSize');

        await page.getByRole('button', { name: 'Add OR', exact: true }).click();
        await page.locator('[data-alternative-target="category:0"]').click();
        await page.locator('#comboCategory0').selectOption('metadata:archetype:world%20legacy');
        await page.getByRole('button', { name: 'Add alternative', exact: true }).click();
        await page.getByRole('button', { name: 'Add AND', exact: true }).click();
        await page.locator('[data-and-target="alternative:0:1"]').click();
        await page.locator('#comboCategory0').selectOption('metadata:card-type:monster');
        await page.getByRole('button', { name: 'Add AND', exact: true }).click();
        session = await saved();
        assert(session.Combos[0].AlternativeGroups[0].Alternatives.every(leaf => leaf.Category.Selector.Kind === 'AllOf'));

        await page.getByRole('button', { name: /Remove conjunct .*\(Monster\).*alternative 1 in group 1/ }).click();
        await page.getByRole('button', { name: 'Add AND', exact: true }).click();
        await page.locator('[data-and-target="alternative:0:0"]').click();
        await page.locator('#comboCategory0').selectOption('metadata:card-type:monster');
        await page.locator('#maxCount0').fill('3');
        await page.getByRole('button', { name: 'Add AND', exact: true }).click();
        session = await saved();
        assert(session.Combos[0].AlternativeGroups[0].Alternatives.every(leaf => leaf.Category.Selector.Kind === 'AllOf'));
        session.Cards[0].Name = 'Reloaded same-card combo';
        await load(session);
        assert.deepEqual((await saved()).Combos, session.Combos);

        await expand('.card-editor .accordion-button');
        await page.locator('#cardDraw0').selectOption('2');
        await page.locator('#cardDiscard0').selectOption('1');
        await page.locator('#cardEligibility0').selectOption('metadata:archetype:orcust');
        await page.getByLabel('Add AND eligibility to alternative 1', { exact: true }).selectOption('metadata:card-type:monster');
        await page.locator('#cardEligibility0').selectOption('metadata:archetype:world%20legacy');
        await page.getByLabel('Add AND eligibility to alternative 2', { exact: true }).selectOption('metadata:card-type:monster');
        session = await saved();
        assert(session.Cards[0].PostDrawSelector.Children.every(child => child.Kind === 'AllOf'));
        session.Cards[0].Name = 'Reloaded composite draw selector';
        await load(session);
        assert.deepEqual((await saved()).Cards[0].PostDrawSelector, session.Cards[0].PostDrawSelector);
        await expand('.card-editor .accordion-button');
        await expand('.combo-editor .accordion-button');
        await page.getByRole('button', { name: 'Add AND', exact: true }).focus();
        await page.keyboard.press('Enter');
        await page.locator('[data-and-target="alternative:0:0"]').focus();
        await page.keyboard.press('Enter');
        await page.locator('#comboCategory0').focus();
        await page.keyboard.press('Tab');
        assert.equal(await page.evaluate(() => document.activeElement.id), 'minCount0');
        for (const width of [1280, 768, 390, 375]) {
            for (const theme of ['light', 'dark']) {
                await page.setViewportSize({ width, height: 1000 });
                await page.getByLabel('Color theme preference').selectOption(theme);
                for (const [name, target] of [['combo', '.same-card-category'], ['draw', '#cardEligibility0']]) {
                    await page.locator(target).first().scrollIntoViewIfNeeded();
                    const geometry = await page.evaluate(() => ({ width: document.documentElement.clientWidth, content: document.documentElement.scrollWidth }));
                    assert(geometry.content <= geometry.width, JSON.stringify({ width, theme, geometry }));
                    await page.screenshot({ path: path.join(screenshots, `${name}-${width}-${theme}.png`) });
                }
                console.log(JSON.stringify({ width, theme, overflow: false }));
            }
        }
        await page.setViewportSize({ width: 1280, height: 1000 });
        for (const fixture of cases) {
            fixture.Session.Cards[0].Name = fixture.Name;
            await load(fixture.Session);
            const gear = page.getByRole('button', { name: 'Calculation settings', exact: true });
            if (await gear.getAttribute('aria-expanded') !== 'true') await gear.click();
            await page.locator('#calculationModel').selectOption('Configured');
            await page.locator('#storageSafetyTier').focus();
            await page.keyboard.press('Escape');
            await page.getByRole('button', { name: 'Calculate', exact: true }).click();
            await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
            const result = Number((await page.locator('.probability-total-value').innerText()).replace('%', '').replace(',', '.').trim());
            assert.equal(result, Number((fixture.Probability * 100).toFixed(2)), fixture.Name);
        }
        assert.deepEqual(errors, []);
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        console.log('PASS: bundled Help example load/calculation, AND create/remove/re-add, shared bounds, OR-of-AND alternatives, composite draw filter, save/reload, keyboard, 16 responsive/theme captures, four UI-worker results, no Blazor/console errors.');
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
