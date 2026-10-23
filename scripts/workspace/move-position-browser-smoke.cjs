// Pass an optimized publish wwwroot directory or served URL; exercises real keyboard focus and compact layouts.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const http = require('node:http');
const { chromium } = require('playwright');

(async () => {
    let baseUrl = process.argv[2] || 'http://127.0.0.1:5096';
    let server;
    if (!/^https?:/.test(baseUrl)) {
        const root = path.resolve(baseUrl);
        const types = { '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.html': 'text/html', '.css': 'text/css' };
        server = http.createServer(async (request, response) => {
            try {
                const pathname = new URL(request.url, 'http://localhost').pathname;
                const file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : decodeURIComponent(pathname)));
                if (!file.startsWith(root + path.sep)) return response.writeHead(403).end();
                response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
                response.end(await fs.readFile(file));
            } catch { response.writeHead(404).end(); }
        });
        await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
        baseUrl = `http://127.0.0.1:${server.address().port}`;
    }
    const browser = await chromium.launch({ headless: true, executablePath: process.env.WORKSPACE_BROWSER_PATH || undefined });
    const errors = [];
    let page;
    try {
        page = await browser.newPage();
        page.on('pageerror', error => errors.push(error.message));
        const categories = [1, 2, 3, 4].map(index => ({ Name: `Role ${index}`, Source: 'User' }));
        const session = {
            SchemaVersion: 11, CalculationModel: 'OpeningHand', FastExecutionStrategy: 'Progressive', HandSize: 1,
            Categories: categories,
            Cards: categories.map((category, index) => ({
                Name: `Card ${index + 1}`, Id: `c${index + 1}`, Copies: 1, Active: true,
                Categories: [category], ManualMetadataCategoryKeys: [], DrawCount: 1,
                EffectProfile: { Source: 'Custom', ExecutionModel: 'DrawSequenceV1' }
            })),
            Combos: categories.map((category, index) => ({
                Name: `Route ${index + 1}`, Active: true, GroupId: `g${index + 1}`,
                Categories: [{ BaseCategory: category, MinCount: 1, MaxCount: 1 }], Cards: []
            })),
            ComboGroups: [1, 2, 3, 4].map(index => ({ Id: `g${index}`, Name: `Group ${index}` })),
            DrawActivationPriority: { Mode: 'CustomPriority', CardIds: ['c1', 'c2', 'c3', 'c4'] }
        };
        const move = async (scope, name, destination) => {
            const trigger = scope.getByRole('button', { name: `Move ${name} to position`, exact: true });
            await trigger.click();
            const panel = scope.locator('.move-position-panel');
            await assertInViewport(panel);
            const input = panel.getByRole('textbox');
            await page.waitForFunction(() => document.activeElement?.matches('.move-position-panel input'));
            assert.equal(await input.evaluate(element => document.activeElement === element), true);
            await input.fill('0');
            await input.press('Enter');
            await panel.getByRole('alert').waitFor();
            await assertInViewport(panel);
            await input.fill(destination);
            await input.press('Enter');
        };
        const assertInViewport = async locator => {
            const box = await locator.boundingBox();
            assert.ok(box && box.x >= -1 && box.x + box.width <= page.viewportSize().width + 1 && box.y >= -1 && box.y + box.height <= page.viewportSize().height + 1, `move panel fits: ${JSON.stringify(box)}`);
        };
        for (const width of [1280, 390, 320]) {
            for (const theme of ['Light', 'Dark']) {
                await page.setViewportSize({ width, height: 1000 });
                await page.goto(baseUrl);
                await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
                await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
                await page.locator('#sessionFileInput').setInputFiles({ name: 'move-position.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(session)) });
                await page.getByRole('button', { name: 'Actions for card Card 1, row 1', exact: true }).waitFor().catch(async error => {
                    console.error(await page.locator('body').innerText());
                    throw error;
                });
                await page.getByLabel('Color theme preference').selectOption({ label: theme });

                for (const [kind, label] of [['card', 'Card 1'], ['combo', 'Route 1']]) {
                    const rows = page.locator(`.${kind}-editor`);
                    await rows.first().locator('.action-menu-trigger').click();
                    if (width === 1280 && theme === 'Light') {
                        const menu = rows.first().locator('.action-menu-panel');
                        await menu.getByRole('button', { name: 'Move down', exact: true }).press('ArrowDown');
                        const command = menu.locator('.move-position > button');
                        assert.equal(await command.evaluate(element => document.activeElement === element), true);
                        await command.press('Escape');
                        assert.equal(await menu.isVisible(), false);
                        await rows.first().locator('.action-menu-trigger').click();
                    }
                    await move(rows.first(), `${kind} ${label}, row 1`, '4');
                    await rows.last().locator('.accordion-button').filter({ hasText: label }).waitFor();
                    assert.match(await rows.last().locator('.accordion-button').innerText(), new RegExp(label));
                    await page.waitForFunction(expected => document.activeElement?.getAttribute('aria-label') === expected, `Actions for ${kind} ${label}, row 4`, { timeout: 5000 }).catch(async error => {
                        console.error('Focus after move', await page.evaluate(() => ({ active: document.activeElement?.outerHTML.slice(0, 240), triggers: [...document.querySelectorAll('.action-menu-trigger')].map(element => element.getAttribute('aria-label')) })));
                        throw error;
                    });
                    assert.equal(await rows.last().locator('.action-menu-trigger').evaluate(element => document.activeElement === element), true);
                    if (width === 1280 && theme === 'Light') {
                        await rows.last().locator('.action-menu-trigger').click();
                        await rows.last().getByRole('button', { name: 'Move up', exact: true }).click();
                        await rows.nth(2).locator('.accordion-button').filter({ hasText: label }).waitFor();
                        await rows.nth(2).locator('.action-menu-trigger').click();
                        await rows.nth(2).getByRole('button', { name: 'Move down', exact: true }).click();
                        await rows.last().locator('.accordion-button').filter({ hasText: label }).waitFor();
                    }
                }
                for (const [selector, kind, label] of [['details.category-management', 'category', 'Role 1'], ['details.combo-group-management', 'group', 'Group 1']]) {
                    const section = page.locator(selector);
                    if (!await section.evaluate(element => element.open)) await section.locator('summary').click();
                    await section.getByRole('button', { name: `Edit ${kind} ${label}`, exact: true }).click();
                    const draft = section.getByRole('textbox', { name: `New name for ${kind} ${label}`, exact: true });
                    await draft.fill('Unsaved rename');
                    await move(section, `${kind} ${label}`, '4');
                    assert.equal(await draft.inputValue(), 'Unsaved rename');
                    const trigger = section.getByRole('button', { name: `Move ${kind} ${label} to position`, exact: true });
                    await page.waitForFunction(expected => document.activeElement?.getAttribute('aria-label') === expected, `Move ${kind} ${label} to position`);
                    assert.equal(await trigger.evaluate(element => document.activeElement === element), true);
                    await trigger.click();
                    const panel = section.locator('.move-position-panel');
                    await panel.getByRole('textbox').fill('0');
                    await panel.getByRole('textbox').press('Enter');
                    await panel.getByRole('alert').waitFor();
                    await assertInViewport(panel);
                    await panel.getByRole('textbox').press('Escape');
                    await panel.waitFor({ state: 'hidden' });
                    assert.equal(await panel.count(), 0);
                    assert.equal(await draft.inputValue(), 'Unsaved rename');
                    await trigger.click();
                    await section.locator('.move-position-panel').waitFor();
                    await draft.click();
                    await section.locator('.move-position-panel').waitFor({ state: 'hidden' });
                    assert.equal(await draft.evaluate(element => document.activeElement === element), true);
                    await section.getByRole('button', { name: kind === 'category' ? 'Cancel category edit' : 'Cancel group edit', exact: true }).click();
                }
                await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
                const settings = page.locator('#calculationSettingsPanel');
                await move(settings, 'Card 1', '4');
                await page.waitForFunction(() => document.activeElement?.getAttribute('aria-label') === 'Move Card 1 to position');
                assert.deepEqual(await page.locator('.draw-priority-list li').evaluateAll(rows => rows.map(row => row.dataset.priorityId)), ['c2', 'c3', 'c4', 'c1']);
                if (process.env.WORKSPACE_SCREENSHOTS) {
                    await settings.getByRole('button', { name: 'Move Card 1 to position', exact: true }).click();
                    await assertInViewport(settings.locator('.move-position-panel'));
                    await fs.mkdir(process.env.WORKSPACE_SCREENSHOTS, { recursive: true });
                    await page.screenshot({ path: path.join(process.env.WORKSPACE_SCREENSHOTS, `move-position-${width}-${theme.toLowerCase()}.png`) });
                    await settings.locator('.move-position-panel').getByRole('button', { name: 'Cancel', exact: true }).click();
                }
                await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `no overflow at ${width} ${theme}`);
            }
        }
        assert.deepEqual(errors, []);
        console.log('Move to position: all five lists, keyboard focus, rename drafts, invalid input, Escape, 1280/390/320px, light/dark passed.');
    } catch (error) {
        if (page && process.env.WORKSPACE_SCREENSHOTS) {
            await fs.mkdir(process.env.WORKSPACE_SCREENSHOTS, { recursive: true });
            await page.screenshot({ path: path.join(process.env.WORKSPACE_SCREENSHOTS, 'move-position-failure.png') });
        }
        throw error;
    } finally {
        await browser.close();
        if (server) await new Promise(resolve => server.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
