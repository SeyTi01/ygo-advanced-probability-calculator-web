const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

// Run against this branch's running app; Playwright is an external verification tool.
const base = process.argv[2] || 'http://127.0.0.1:5104';
const evidence = process.env.COMBO_SCREENSHOTS;
const categories = ['Orcust', 'Monster', 'FIRE', 'DARK'].map(Name => ({ Name, Source: 'User' }));
const category = (index, min = 1, max = 5, mode = 'HandSize') => ({ BaseCategory: categories[index], MinCount: min, MaxCount: max, MaximumMode: mode });
const fixture = {
    SchemaVersion: 5, Categories: categories,
    Cards: [
        { Id: 'razen', Name: 'Vanquish Soul Razen', Copies: 3, Categories: [categories[1], categories[2]] },
        { Id: 'orcust', Name: 'Orcust Harp Horror', Copies: 3, Categories: [categories[0], categories[1], categories[3]] },
        { Id: 'blank', Name: 'Other card', Copies: 3, Categories: [] }
    ],
    Combos: [{ Name: 'Browser route', Categories: [], Cards: [], AlternativeGroups: [] }],
    ComboGroups: [{ Id: 'routes', Name: 'Routes' }], HandSize: 5
};

(async () => {
    const browser = await chromium.launch({ headless: true, channel: process.env.PLAYWRIGHT_CHROMIUM_CHANNEL || undefined });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, hasTouch: true });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.addInitScript(() => { window.showSaveFilePicker = undefined; });
        await page.goto(base);
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        async function load(session) {
            await page.locator('#sessionFileInput').setInputFiles({ name: 'combo-smoke.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(session)) });
            await page.waitForFunction(name => document.querySelector('.combo-header-name')?.textContent === name, session.Combos[0].Name);
            const header = page.locator('.combo-editor .accordion-button').first();
            if (await header.getAttribute('aria-expanded') !== 'true') await header.click();
        }
        const editor = page.locator('.combo-editor').first();
        const picker = () => editor.locator('#comboTarget0');
        const click = name => editor.getByRole('button', { name, exact: true }).click();
        const edit = key => editor.locator(`[data-requirement="${key}"]`).click();
        async function choose(value, submit = 'Add requirement') {
            await picker().selectOption(value);
            await click(submit);
        }
        async function saved() {
            const download = page.waitForEvent('download');
            await page.getByRole('button', { name: 'Save Session', exact: true }).click();
            return JSON.parse(fs.readFileSync(await (await download).path(), 'utf8'));
        }
        async function capture(name) {
            if (!evidence) return;
            fs.mkdirSync(evidence, { recursive: true });
            await editor.screenshot({ path: path.join(evidence, `${name}.png`), animations: 'disabled' });
        }
        await load(fixture);
        await capture('after-blank-1440-light');
        assert.equal(await editor.locator('[data-action]').count(), 0);
        await picker().focus();
        await picker().press('Tab');
        assert.equal(await editor.locator('#minCount0').evaluate(element => element === document.activeElement), true);
        await editor.locator('#minCount0').press('Tab');
        assert.equal(await editor.getByRole('button', { name: 'Add requirement', exact: true }).evaluate(element => element === document.activeElement), true);
        await choose('card:razen');
        await choose('user:FIRE');
        await edit('card:0');
        await editor.locator('#minCount0').fill('2');
        await editor.locator('summary').click();
        await editor.locator('#maxCount0').fill('3');
        await editor.locator('#maxCount0').press('Enter');
        await edit('card:0');
        assert.equal(await editor.locator('#minCount0').inputValue(), '2');
        assert.equal(await editor.locator('#maxCount0').inputValue(), '3');
        await picker().press('Escape');
        assert.equal(await editor.locator('[data-requirement="card:0"]').evaluate(element => element === document.activeElement), true);
        await edit('category:0');
        await editor.locator('[data-action="alternative"]').click();
        await choose('user:DARK', 'Add alternative');
        assert.equal(await editor.locator('.requirement-or-group').count(), 1);
        await edit('alternative:0:1');
        await picker().selectOption('card:orcust');
        await editor.locator('#minCount0').fill('0');
        await editor.locator('summary').click();
        await editor.locator('#maxCount0').fill('0');
        await click('Save changes');
        await edit('alternative:0:0');
        await editor.locator('[data-action="property"]').click();
        await choose('user:Monster', 'Add property');
        await edit('alternative:0:0');
        await editor.getByLabel('Same-card property 2', { exact: true }).selectOption('user:Orcust');
        await click('Save changes');
        await editor.locator('#comboGroup0').selectOption('routes');
        const advanced = await saved();
        assert.equal(advanced.Combos[0].AlternativeGroups[0].Alternatives[1].Card.MaxCount, 0);
        assert.equal(advanced.Combos[0].GroupId, 'routes');
        await load({ ...advanced, Combos: advanced.Combos.map(combo => ({ ...combo, Name: 'Reloaded route' })) });
        const reloaded = await saved();
        assert.deepEqual(reloaded.Combos[0].AlternativeGroups, advanced.Combos[0].AlternativeGroups);
        assert.deepEqual(reloaded.Combos[0].Cards, advanced.Combos[0].Cards);
        for (const width of [320, 375, 1280, 1440]) {
            for (const theme of ['light', 'dark']) {
                await page.setViewportSize({ width, height: 1000 });
                await page.getByLabel('Color theme preference').selectOption(theme);
                await capture(`after-complex-${width}-${theme}`);
                await editor.locator('[data-requirement="alternative:0:0"]').tap();
                await capture(`after-edit-${width}-${theme}`);
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `${width}/${theme} overflow`);
                const clipped = await editor.locator('button, select, input').evaluateAll(elements => elements.filter(element => {
                    const box = element.getBoundingClientRect();
                    return box.width > 0 && (box.left < 0 || box.right > innerWidth + 1);
                }).map(element => element.outerHTML));
                assert.deepEqual(clipped, [], `${width}/${theme} clipped controls`);
                await click('Cancel');
            }
        }
        await edit('alternative:0:1');
        await click('Remove alternative');
        assert.equal(await editor.locator('.requirement-or-group').count(), 0);
        await edit('category:0');
        await editor.getByRole('button', { name: 'Remove property 2', exact: true }).click();
        await click('Save changes');
        await edit('category:0');
        await click('Remove requirement');
        assert.equal(await editor.locator('[data-requirement^="category:"]').count(), 0);

        // Same two labeled physical hands used by the independent .NET oracle test.
        const tiny = { ...fixture, HandSize: 1, Cards: [
            { Id: 'both', Name: 'Both properties', Copies: 1, Categories: [categories[0], categories[1]] },
            { Id: 'blank', Name: 'Neither', Copies: 1, Categories: [] }
        ], Combos: [{ Name: 'Exact same-card check', Categories: [category(0, 1, 1, 'Fixed')] }] };
        await load(tiny);
        await edit('category:0');
        await editor.locator('[data-action="property"]').click();
        await choose('user:Monster', 'Add property');
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.locator('.probability-total-value').waitFor();
        assert.match(await page.locator('.probability-total-value').innerText(), /50[.,]00/);
        await page.getByRole('button', { name: 'Pin result', exact: true }).click();
        await edit('category:0');
        await editor.getByRole('button', { name: 'Remove property 2', exact: true }).click();
        await click('Save changes');
        await choose('user:Orcust');
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.probability-total-value')?.textContent.match(/^\s*0[.,]00/));
        const example = JSON.parse(fs.readFileSync(path.join(__dirname, '../../YGOProbabilityCalculatorBlazor/wwwroot/sample-data/example_session_state.json'), 'utf8'));
        await load(example);
        await capture('after-vs-k9-1440-dark');
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.probability-total-value')?.textContent.match(/83[.,]61/));
        const second = page.locator('.combo-editor').nth(1);
        await second.locator('.accordion-button').click();
        await second.locator('.requirement-edit').first().click();
        await second.locator('#minCount1').fill('3');
        await editor.locator('.accordion-button').click();
        await second.locator('.accordion-button').click();
        assert.equal(await second.locator('#minCount1').inputValue(), '3');
        await editor.getByRole('button', { name: 'Remove combo', exact: true }).click();
        assert.equal(await page.locator('.combo-editor').first().locator('#minCount0').inputValue(), '3', 'Deleting an earlier combo retains the surviving editor draft');
        assert.deepEqual(errors, []);
        console.log(JSON.stringify({ scenarios: 'add/edit, contextual OR, same-card, mixed alternatives, 0/0, save/reload, group cleanup, keyboard, touch, exact 50% vs 0%, VS/K9 83.61%', widths: [320, 375, 1280, 1440], themes: ['light', 'dark'], pageErrors: errors }));
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
