// Run against a Release publish directory. Playwright is an external verification tool.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium, firefox } = require('playwright');

(async () => {
    const root = path.resolve(process.argv[2]);
    const evidence = process.env.WORKSPACE_SCREENSHOTS;
    const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.html': 'text/html', '.css': 'text/css' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = new URL(request.url, 'http://localhost').pathname;
            const file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : decodeURIComponent(pathname)));
            if (!file.startsWith(root + path.sep)) return response.writeHead(403).end();
            response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    let browser;
    try {
        const engine = process.env.WORKSPACE_BROWSER === 'firefox' ? firefox : chromium;
        browser = await engine.launch({ headless: true, timeout: 15000, executablePath: process.env.WORKSPACE_BROWSER_PATH || undefined });
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, hasTouch: true });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.addInitScript(() => { window.showSaveFilePicker = undefined; });
        await page.goto(`http://127.0.0.1:${server.address().port}`);
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        const capture = async name => {
            if (!evidence) return;
            await fs.mkdir(evidence, { recursive: true });
            await page.screenshot({ path: path.join(evidence, `${name}.png`) });
        };
        const noOverflow = async () => assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
        const quick = page.getByRole('combobox', { name: 'Add card by name' });
        const addManual = async name => { await quick.fill(name); await quick.press('Escape'); await page.getByRole('button', { name: 'Add card', exact: true }).click(); };
        const row = (kind, index = 0) => page.locator(`.${kind}-editor`).nth(index);
        const open = async scope => { await scope.locator('.action-menu-trigger').click(); return scope.locator('.action-menu-panel'); };
        const command = async (kind, index, name) => { const panel = await open(row(kind, index)); await panel.getByRole('button', { name, exact: true }).click(); };
        const load = async session => {
            await page.locator('#sessionFileInput').setInputFiles({ name: 'workspace.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(session)) });
            await page.waitForFunction(name => document.querySelector('.card-header-name')?.textContent.includes(name), session.Cards[0].Name);
        };
        const saved = async () => {
            const download = page.waitForEvent('download');
            await page.getByRole('button', { name: 'Save Session', exact: true }).click();
            return JSON.parse(await fs.readFile(await (await download).path(), 'utf8'));
        };
        await capture('empty-desktop');
        await quick.focus();
        assert.equal(await page.locator('.card-editor').count(), 0);
        await addManual('Manual card');
        assert.equal(await quick.inputValue(), '');
        assert.equal(await quick.evaluate(element => element === document.activeElement), true);
        await quick.fill('Pot of Greed');
        await page.getByRole('option', { name: 'Pot of Greed', exact: true }).waitFor();
        await quick.press('ArrowDown');
        await quick.press('Enter');
        assert.equal(await page.locator('.card-editor').count(), 2);
        let session = await saved();
        assert.equal(session.Cards[1].ExternalCardId, 55144522);
        assert.equal(session.Cards[1].DrawCount, 2);
        for (const touch of [false, true]) {
            await quick.fill('Upstart Goblin');
            const suggestion = page.getByRole('option', { name: 'Upstart Goblin', exact: true });
            if (touch) await suggestion.tap();
            else await suggestion.click();
            assert.equal(await page.locator('.card-editor').count(), 3);
            assert.match(await row('card', 2).innerText(), /Upstart Goblin/);
            await command('card', 2, 'Remove');
        }
        await command('card', 1, 'Duplicate');
        session = await saved();
        assert.equal(session.Cards.length, 3);
        assert.notEqual(session.Cards[1].Id, session.Cards[2].Id);
        assert.equal(session.Cards[2].DrawCount, 2);
        await row('card', 1).locator('.accordion-button').click();
        await command('card', 1, 'Move up');
        assert.equal(await row('card', 0).locator('.accordion-button').getAttribute('aria-expanded'), 'true');
        assert.equal(await row('card', 0).locator('.action-menu-trigger').evaluate(element => element === document.activeElement), true);
        await command('card', 0, 'Deactivate');
        assert.equal(await row('card', 0).locator('.accordion-button').innerText().then(text => text.includes('Inactive')), true);
        await command('card', 0, 'Activate');
        await command('card', 2, 'Remove');
        assert.equal(await page.locator('.card-editor').count(), 2);
        assert.equal(await row('card', 1).locator('.action-menu-trigger').evaluate(element => element === document.activeElement), true);

        await page.locator('.category-management > summary').click();
        await page.getByRole('textbox', { name: 'Category name', exact: true }).fill('Starter');
        await page.getByRole('textbox', { name: 'Category name', exact: true }).press('Enter');
        await page.getByRole('button', { name: 'Edit category Starter', exact: true }).click();
        await page.getByRole('textbox', { name: 'New name for category Starter', exact: true }).fill('Renamed starter');
        await page.locator('.category-management > summary').click();
        await page.getByLabel('Hand Size:').fill('1');
        await page.getByLabel('Hand Size:').press('Tab');
        await page.locator('.category-management > summary').click();
        assert.equal(await page.getByRole('textbox', { name: 'New name for category Starter', exact: true }).inputValue(), 'Renamed starter');
        await page.getByRole('textbox', { name: 'New name for category Starter', exact: true }).press('Enter');

        const groups = page.locator('.combo-group-management');
        assert.equal(await groups.getAttribute('open'), null);
        assert.equal(await groups.evaluate(element => Boolean(document.querySelector('#addCombo').compareDocumentPosition(element) & Node.DOCUMENT_POSITION_FOLLOWING)), true);
        await groups.locator('summary').focus();
        await groups.locator('summary').press('Enter');
        const groupName = page.getByRole('textbox', { name: 'New combo group name', exact: true });
        await groupName.fill('Routes');
        await groupName.press('Enter');
        await page.getByRole('button', { name: 'Edit group Routes', exact: true }).click();
        const groupRename = page.getByRole('textbox', { name: 'New name for group Routes', exact: true });
        await groupRename.fill('Main routes');
        await groupName.fill('Draft group');
        await groups.locator('summary').click();
        await page.getByLabel('Hand Size:').fill('2');
        await page.getByLabel('Hand Size:').press('Tab');
        await groups.locator('summary').focus();
        await groups.locator('summary').press('Space');
        await groupRename.waitFor();
        assert.equal(await groupRename.inputValue(), 'Main routes');
        assert.equal(await groupName.inputValue(), 'Draft group');
        await page.getByRole('button', { name: 'Save group name', exact: true }).click();
        await page.getByRole('button', { name: 'Edit group Main routes', exact: true }).waitFor();
        await page.getByRole('button', { name: 'Remove group Main routes', exact: true }).click();
        await groups.locator('summary').click();

        const sample = JSON.parse(await fs.readFile(path.join(root, 'sample-data/example_session_state.json'), 'utf8'));
        await load(sample);
        for (const kind of ['card', 'combo']) {
            const scope = row(kind);
            const collapsed = await scope.locator('.accordion-button').getAttribute('aria-expanded');
            const panel = await open(scope);
            assert.deepEqual(await panel.locator('button').allTextContents(), ['Move up', 'Move down', 'Duplicate', 'Deactivate', 'Remove']);
            assert.equal(await panel.getByRole('button', { name: 'Move up', exact: true }).isDisabled(), true);
            await panel.getByRole('button', { name: 'Move down', exact: true }).press('ArrowDown');
            assert.equal(await panel.getByRole('button', { name: 'Duplicate', exact: true }).evaluate(element => element === document.activeElement), true);
            await panel.getByRole('button', { name: 'Duplicate', exact: true }).press('Escape');
            assert.equal(await panel.isVisible(), false);
            assert.equal(await scope.locator('.action-menu-trigger').evaluate(element => element === document.activeElement), true);
            assert.equal(await scope.locator('.accordion-button').getAttribute('aria-expanded'), collapsed);
            await open(scope);
            await page.getByRole('heading', { name: /^Deck/ }).click();
            assert.equal(await panel.isVisible(), false);
            await open(scope);
            await panel.getByRole('button', { name: 'Remove', exact: true }).press('Tab');
            assert.equal(await panel.isVisible(), false);
        }
        const comboCount = await page.locator('.combo-editor').count();
        await command('combo', 0, 'Duplicate');
        assert.equal(await page.locator('.combo-editor').count(), comboCount + 1);
        await command('combo', 1, 'Deactivate');
        await command('combo', 1, 'Move down');
        await command('combo', 2, 'Remove');
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.locator('.probability-total-value').waitFor();
        assert.match(await page.locator('.probability-total-value').innerText(), /83\.61/);

        await page.getByRole('button', { name: 'Pin result', exact: true }).click();
        await page.getByRole('button', { name: 'Unpin result', exact: true }).waitFor();

        for (const theme of ['Light', 'Dark']) {
            await page.getByLabel('Color theme preference').selectOption({ label: theme });
            for (const width of [1440, 768, 390, 320]) {
                await page.setViewportSize({ width, height: 1000 });
                await page.evaluate(() => window.scrollTo(0, 0));
                await noOverflow();
                const sortBox = await page.getByRole('button', { name: 'Sort deck A–Z' }).boundingBox();
                const addBox = await page.getByRole('button', { name: 'Add card', exact: true }).boundingBox();
                const cardsBox = await page.locator('#cardsAccordion').boundingBox();
                assert.ok(Math.abs((addBox.y - sortBox.y - sortBox.height) - (cardsBox.y - addBox.y - addBox.height)) <= 1);
                const results = page.locator('.probability-results');
                await results.scrollIntoViewIfNeeded();
                const pinBox = await results.getByRole('button', { name: 'Replace pin with current result' }).boundingBox();
                const copyBox = await results.getByRole('button', { name: 'Copy results', exact: true }).boundingBox();
                const headingBox = await results.locator('h5').boundingBox();
                assert.ok(pinBox.x > headingBox.x && copyBox.x >= pinBox.x + pinBox.width);
                assert.ok(Math.abs(pinBox.y - copyBox.y) <= 1);
                assert.ok(pinBox.width >= 44 && copyBox.width >= 44);
                await capture(`results-${width}-${theme.toLowerCase()}`);
                await page.evaluate(() => window.scrollTo(0, 0));
                await page.mouse.move(0, 0);
                const toolbarColors = await page.locator('.session-toolbar :is(.session-action, .session-share-compact, .action-menu-trigger)').evaluateAll(elements => elements.map(element => getComputedStyle(element).color));
                assert.equal(new Set(toolbarColors).size, 1, `Toolbar text colors in ${theme}: ${toolbarColors}`);
                const labelCenters = await page.locator('.session-toolbar > .session-action').evaluateAll(elements => elements.map(element => {
                    const text = [...element.childNodes].find(node => node.nodeType === Node.TEXT_NODE && node.textContent.trim());
                    const range = document.createRange();
                    range.selectNodeContents(text);
                    const rect = range.getBoundingClientRect();
                    return rect.y + rect.height / 2;
                }));
                assert.ok(Math.abs(labelCenters[0] - labelCenters[1]) <= 1, `Save/Load label alignment at ${width}: ${labelCenters}`);
                await capture(`populated-${width}-${theme.toLowerCase()}`);
                const last = row('combo', (await page.locator('.combo-editor').count()) - 1);
                const panel = await open(last);
                const box = await panel.boundingBox();
                assert.ok(box.x >= 0 && box.x + box.width <= width + 1 && box.y >= 0 && box.y + box.height <= 1001);
                await capture(`last-menu-${width}-${theme.toLowerCase()}`);
                await panel.getByRole('button', { name: 'Duplicate', exact: true }).press('Escape');
            }
        }
        await page.getByRole('button', { name: 'Copy results', exact: true }).click();
        await page.getByRole('group', { name: 'Result summary', exact: true }).waitFor();
        await noOverflow();
        await capture('copy-fallback-320-dark');
        await page.getByRole('button', { name: 'Close copy fallback', exact: true }).click();
        await page.getByRole('button', { name: 'Unpin result', exact: true }).click();
        assert.equal(await page.locator('.pinned-result').count(), 0);
        await page.getByRole('button', { name: 'Import deck', exact: true }).click();
        await page.getByRole('button', { name: 'Import YDKe', exact: true }).click();
        await page.getByRole('textbox', { name: 'YDKe deck code' }).fill('invalid');
        await page.getByRole('button', { name: 'Import', exact: true }).click();
        assert.equal(await page.locator('.ydke-import-form').isVisible(), true);
        await page.locator('.ydke-import-form').getByRole('button', { name: 'Cancel', exact: true }).click();
        await page.getByRole('button', { name: 'Copy share link' }).click();
        await page.getByRole('button', { name: 'More workspace actions' }).click();
        await page.getByRole('button', { name: 'Clear workspace', exact: true }).click();
        await page.locator('.clear-workspace-confirmation').getByRole('button', { name: 'Cancel', exact: true }).click();
        assert.equal(await page.locator('.card-editor').count(), sample.Cards.length);
        await page.getByRole('button', { name: 'More workspace actions' }).click();
        await page.getByRole('button', { name: 'Clear workspace', exact: true }).click();
        await page.locator('.clear-workspace-confirmation').getByRole('button', { name: 'Clear', exact: true }).click();
        assert.equal(await page.locator('.card-editor').count(), 0);
        assert.equal(await quick.isVisible(), true);
        await page.getByRole('button', { name: 'Import deck', exact: true }).click();
        await page.locator('#fileInput').setInputFiles({ name: 'quick.ydk', mimeType: 'text/plain', buffer: Buffer.from('#main\n55144522\n55144522\n#extra\n!side\n') });
        await page.getByRole('heading', { name: 'Deck (2)', exact: true }).waitFor();
        assert.equal(await page.locator('.card-editor').count(), 1);
        await page.getByRole('button', { name: 'Import deck', exact: true }).click();
        await page.getByRole('button', { name: 'Import YDKe', exact: true }).click();
        const cardBytes = Buffer.alloc(4);
        cardBytes.writeUInt32LE(70368879);
        await page.getByRole('textbox', { name: 'YDKe deck code' }).fill(`ydke://${cardBytes.toString('base64')}!!!`);
        await page.locator('.ydke-import-form').getByRole('button', { name: 'Import', exact: true }).click();
        await page.getByRole('heading', { name: 'Deck (1)', exact: true }).waitFor();
        assert.match(await row('card').innerText(), /Upstart Goblin/);
        await noOverflow();
        assert.deepEqual(errors, []);
        console.log(JSON.stringify({ browser: browser.version(), widths: [1440, 768, 390, 320], themes: ['Light', 'Dark'], result: 'passed' }));
    } finally {
        await browser?.close();
        server.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
