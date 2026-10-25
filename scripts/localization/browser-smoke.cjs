// node scripts/localization/browser-smoke.cjs <production-wwwroot> <isolated-test-wwwroot> [baseline-wwwroot]
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const { serve, load, usable, launchOptions } = require('../background-calculation/browser-support.cjs');
const preference = 'ygo-calculator:language:v1';
const handoff = 'ygo-calculator:language-handoff:v1';
const recovery = 'ygo-calculator:session-recovery:v1';

async function interactive(page) {
    await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
    await page.waitForFunction(() => !document.documentElement.classList.contains('locale-pending'));
}

async function change(page, locale) {
    const before = page.url();
    await Promise.all([page.waitForEvent('load', { timeout: 15000 }), page.locator('#language-preference').selectOption(locale)]).catch(async error => {
        console.error('Language switch did not reload:', await page.getByRole('alert').allTextContents(), await page.locator('#language-preference').inputValue());
        throw error;
    });
    await interactive(page);
    assert.equal(page.url(), before, 'language reload keeps the exact URL');
    assert.equal(await page.locator('#language-preference').inputValue(), locale);
    assert.equal(await page.evaluate(key => sessionStorage.getItem(key), handoff), null, 'handoff acknowledged only after restoration');
    assert.equal(await page.locator('.content > .alert[role=alert]').count(), 0, 'no language-handoff error');
}

async function header(page) {
    await usable(page, page.locator('#language-preference'));
    assert.equal(await page.evaluate(() => {
        const title = document.querySelector('.app-title').getBoundingClientRect();
        const controls = document.querySelector('.top-row-controls').getBoundingClientRect();
        return title.bottom <= controls.top || title.right <= controls.left;
    }), true, 'title and language controls do not overlap');
}

async function main() {
    const browser = await chromium.launch(launchOptions());
    const servers = [];
    const errors = [];
    const track = page => {
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => {
            if (message.type() === 'error' && !message.text().startsWith('Failed to load resource:')) errors.push(message.text().slice(0, 1500));
        });
    };
    try {
        const prod = await serve(process.argv[2]); servers.push(prod.server);
        const context = await browser.newContext({ locale: 'de-DE', viewport: { width: 390, height: 844 } });
        const page = await context.newPage();
        track(page);
        await page.goto(prod.url); await interactive(page);
        assert.equal(await page.locator('#language-preference').count(), 0);
        assert.equal(await page.locator('#quickCardName').getAttribute('placeholder'), 'Search cards or enter a name');
        const transfer = await page.evaluate(() => performance.getEntriesByType('resource').reduce((sum, item) => sum + item.encodedBodySize, 0));
        const productionRequests = await page.evaluate(() => performance.getEntriesByType('resource').map(item => item.name));
        assert.equal(productionRequests.some(url => /\/(fr|de)\/.*resources/.test(url)), false);
        console.log(`Production English first visit encoded resource bodies: ${transfer} bytes.`);
        await usable(page, page.locator('#theme-preference'));
        await context.close();

        const testHost = await serve(process.argv[3]); servers.push(testHost.server);
        const test = await browser.newContext({ locale: 'de-DE', viewport: { width: 390, height: 844 }, reducedMotion: 'reduce' });
        await test.addInitScript(key => {
            if (!localStorage.getItem(key)) localStorage.setItem(key, 'en');
            Object.defineProperty(window, 'showSaveFilePicker', { value: undefined });
            // Observe the first visible interactive card input, not only the settled language.
            window.firstInteractivePlaceholder = null;
            const observe = () => {
                const input = document.querySelector('#quickCardName');
                if (!window.firstInteractivePlaceholder && input && getComputedStyle(input).visibility === 'visible') {
                    window.firstInteractivePlaceholder = input.placeholder;
                }
                requestAnimationFrame(observe);
            };
            requestAnimationFrame(observe);
        }, preference);
        const p = await test.newPage(); p.setDefaultTimeout(12000);
        track(p);
        let documents = 0;
        const requests = [];
        p.on('request', request => { requests.push(request.url()); if (request.isNavigationRequest() && request.frame() === p.mainFrame()) documents++; });
        await p.goto(testHost.url); await interactive(p);
        assert.equal(await p.locator('#language-preference').inputValue(), 'en', 'explicit English overrides supported de-DE');
        console.log('Satellite requests on English:', requests.filter(url => /\/(fr|de)\//.test(url)));
        assert.equal(requests.some(url => /\/(fr|de)\/.*resources/.test(url)), false, 'English downloads no pseudo satellites');
        await usable(p, p.locator('#language-preference'));
        const box = await p.locator('#language-preference').boundingBox(); assert(box.height >= 44);
        const beforeEmpty = documents;
        await change(p, 'fr');
        assert.equal(documents - beforeEmpty, 1, 'exactly one document reload');
        assert.equal(await p.locator('#quickCardName').getAttribute('placeholder'), '⟦Search cards⟧');
        assert.equal(await p.evaluate(() => window.firstInteractivePlaceholder), '⟦Search cards⟧');
        assert(requests.some(url => /\/fr\/.*resources/.test(url)), 'selected satellite requested');
        assert.equal(requests.some(url => /\/de\/.*resources/.test(url)), false, 'unselected satellite not requested');

        // A supported German browser still keeps explicitly selected English after a reload and a new context.
        await change(p, 'en');
        const beforeKeyboard = documents;
        await Promise.all([p.waitForEvent('load'), (async () => {
            await p.locator('#language-preference').focus();
            await p.keyboard.press('ArrowDown');
            await p.keyboard.press('Enter');
        })()]);
        await interactive(p);
        assert.equal(await p.locator('#language-preference').inputValue(), 'fr');
        assert.equal(documents - beforeKeyboard, 1, 'keyboard selection takes one reload');
        await change(p, 'en');
        await p.reload(); await interactive(p);
        assert.equal(await p.locator('#language-preference').inputValue(), 'en');
        const profilePath = path.resolve('bin/language-browser-profile');
        let profile = await chromium.launchPersistentContext(profilePath, { ...launchOptions(), locale: 'de-DE' });
        let profilePage = await profile.newPage(); track(profilePage);
        await profilePage.goto(testHost.url); await interactive(profilePage);
        await profilePage.evaluate(key => localStorage.setItem(key, 'de'), preference);
        await profilePage.reload(); await interactive(profilePage);
        await change(profilePage, 'en');
        await profile.close();
        profile = await chromium.launchPersistentContext(profilePath, { ...launchOptions(), locale: 'de-DE' });
        profilePage = await profile.newPage(); track(profilePage);
        await profilePage.goto(testHost.url); await interactive(profilePage);
        assert.equal(await profilePage.locator('#language-preference').inputValue(), 'en', 'English persists across an actual browser process restart');
        await profile.close();

        const role = { Name: 'Target', Source: 'User' };
        const session = { SchemaVersion: 12, CalculationModel: 'Fast', FastExecutionStrategy: 'Progressive', HandSize: 1,
            Categories: [role], Cards: [{ Id: 'a', Name: 'Workspace sentinel', Copies: 2, Categories: [role] },
                { Id: 'b', Name: 'Filler', Copies: 1, Categories: [] }],
            Combos: [{ Name: 'Two thirds', Categories: [{ BaseCategory: role, MinCount: 1, MaxCount: 1 }] }], ComboGroups: [] };
        await load(p, session);
        await p.getByRole('button', { name: 'Calculate', exact: true }).click();
        await p.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') === 'false'
            && document.querySelector('.probability-total-value'));
        assert.match(await p.locator('.probability-total .probability-with-accuracy').getAttribute('title'), /(?<!\d)2\/3(?!\d)/);
        assert.match(await p.locator('.probability-total-value').innerText(), /66,67/, 'German numeric formatting is retained');
        await p.getByRole('button', { name: 'Pin result', exact: true }).click();
        const pinned = await p.locator('.pinned-result').innerText();
        const accepted = await p.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery);
        await p.locator('.card-editor .accordion-button').first().click();
        await p.locator('.simulation-edit').first().click();
        await p.locator('#cardDraw0').selectOption('2');
        await p.locator('.combo-editor .accordion-button').click();
        await p.locator("button[data-requirement='category:0']").click();
        await p.locator('#minCount0').fill('0');
        await p.locator('#quickCardName').fill('Unsubmitted card draft');
        await p.locator('.category-management > summary').click();
        await p.getByRole('textbox', { name: 'Category name', exact: true }).fill('Unsubmitted category draft');
        await p.locator('.combo-group-management > summary').click();
        await p.getByRole('textbox', { name: 'New combo group name', exact: true }).fill('Unsubmitted group draft');
        await p.locator('.card-editor .action-menu-trigger').first().click();
        await p.locator('.card-editor .move-position > button').first().click();
        await p.locator('.move-position-panel input').fill('2');
        await p.locator('#language-preference').focus();
        assert.equal(await p.locator('.move-position-panel input').count(), 1, 'language focus does not dismiss a pending move');
        await change(p, 'fr');
        assert.equal(await p.locator('#quickCardName').inputValue(), 'Unsubmitted card draft');
        assert.equal(await p.locator('#cardDraw0').inputValue(), '2');
        assert.equal(await p.locator('#minCount0').inputValue(), '0');
        assert.equal(await p.locator('.move-position-panel input').inputValue(), '2');
        await p.locator('.move-position-panel input').press('Escape');
        assert.equal(await p.getByRole('textbox', { name: 'New combo group name', exact: true }).inputValue(), 'Unsubmitted group draft');
        assert.equal(await p.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery), accepted, 'drafts never commit during restoration');
        assert.match(await p.locator('.probability-total .probability-with-accuracy').getAttribute('title'), /(?<!\d)2\/3(?!\d)/);
        assert.equal(await p.locator('.pinned-result').innerText(), pinned, 'manual pin retained');
        assert.equal(await p.getByRole('button', { name: 'Restore previous session', exact: true }).count(), 0);
        await p.screenshot({ path: path.resolve('bin/language-mobile.png'), fullPage: true });
        await p.setViewportSize({ width: 1280, height: 900 });
        await header(p);
        await p.screenshot({ path: path.resolve('bin/language-desktop.png'), fullPage: true });
        for (const width of [320, 390, 768, 1024, 1920]) {
            await p.setViewportSize({ width, height: 900 });
            await header(p);
        }
        await p.locator('#theme-preference').selectOption('dark');
        await p.setViewportSize({ width: 390, height: 844 });
        await header(p);

        // A foreign recovery record and a pending shared offer survive without replacement.
        const foreign = JSON.stringify({ version: 1, savedAt: '2026-10-25T00:00:00Z', payload: accepted });
        await p.evaluate(([key, value]) => localStorage.setItem(key, value), [recovery, foreign]);
        await p.evaluate(() => { location.hash = 'ygo-session=invalid-offer'; });
        await p.getByRole('button', { name: 'Dismiss shared link', exact: true }).waitFor();
        await change(p, 'en');
        assert.equal(await p.evaluate(key => localStorage.getItem(key), recovery), foreign);
        assert.equal(await p.getByRole('button', { name: 'Dismiss shared link', exact: true }).count(), 1);

        // Denied browser storage must retain the entire live workspace and avoid navigation.
        await p.evaluate(key => {
            const original = Storage.prototype.setItem;
            Storage.prototype.setItem = function (name, value) { if (name === key) throw new Error('Storage denied for test'); return original.call(this, name, value); };
        }, preference);
        const beforeFailure = documents;
        await p.locator('#language-preference').selectOption('fr');
        await p.getByRole('alert').filter({ hasText: 'Language was not changed' }).waitFor();
        assert.equal(documents, beforeFailure);
        assert.equal(await p.locator('#quickCardName').inputValue(), 'Unsubmitted card draft');
        assert.equal(await p.evaluate(key => sessionStorage.getItem(key), handoff), null);
        assert.equal(await p.evaluate(key => localStorage.getItem(key), preference), 'en');
        await test.close();

        const busyContext = await browser.newContext({ locale: 'en-US' });
        const busy = await busyContext.newPage();
        track(busy);
        await busy.goto(testHost.url); await interactive(busy); await load(busy, session);
        await busy.evaluate(() => {
            const original = Worker.prototype.postMessage;
            Worker.prototype.postMessage = function (message, ...rest) {
                if (message.json) { window.heldCalculation = true; return; }
                return original.call(this, message, ...rest);
            };
            window.confirm = message => { window.languageConfirmation = message; return window.allowLanguageCancel === true; };
        });
        await busy.getByRole('button', { name: 'Calculate', exact: true }).click();
        await busy.waitForFunction(() => window.heldCalculation);
        await busy.locator('#language-preference').selectOption('fr');
        await busy.waitForFunction(() => window.languageConfirmation);
        assert.match(await busy.evaluate(() => window.languageConfirmation), /cancels the active calculation/);
        assert.equal(await busy.evaluate(key => sessionStorage.getItem(key), handoff), null);
        assert.equal(await busy.locator('.calculate-action > button').getAttribute('aria-busy'), 'true');
        await busy.waitForFunction(() => !document.querySelector('.page').inert
            && !document.querySelector('#language-preference').disabled && document.querySelector('#language-preference').value === 'en');
        await busy.evaluate(() => { window.allowLanguageCancel = true; });
        await change(busy, 'fr');
        assert.equal(await busy.locator('.calculate-action > button').getAttribute('aria-busy'), 'false');
        await busyContext.close();

        const nestedContext = await browser.newContext({ locale: 'en-US' });
        const nested = await nestedContext.newPage(); track(nested);
        await nested.goto(testHost.url); await interactive(nested); await load(nested, session);
        await nested.locator('.card-editor .accordion-button').first().click();
        await nested.locator('.simulation-edit').first().click();
        await nested.locator('#cardDraw0').selectOption('2');
        await nested.locator('#cardCost0').selectOption('1');
        await nested.locator('#cardCostEligibility0').selectOption('user:Target');
        await nested.locator('#cardDiscard0').selectOption('1');
        await nested.locator('#cardEligibility0').selectOption('user:Target');
        await nested.locator('.selector-expression .requirement-edit').first().click();
        await nested.locator('.selector-expression form .requirement-property-picker > select').selectOption('numeric:level');
        await nested.locator('.selector-expression form .requirement-numeric-fields input').fill('');
        await change(nested, 'fr');
        assert.equal(await nested.locator('.selector-expression form .requirement-numeric-fields input').inputValue(), '');
        assert.equal(await nested.locator('.simulation-save').isDisabled(), true, 'invalid nested draft remains unsubmitted');
        await nestedContext.close();

        // Browser detection, unsupported saved preference, and canonical Help transition.
        const automatic = await browser.newContext({ locale: 'fr-FR' });
        const help = await automatic.newPage();
        track(help);
        await help.route('**/help', route => route.fulfill({ contentType: 'text/html', body: fs.readFileSync(path.join(process.argv[3], 'help.html')) }));
        await help.goto(testHost.url + '/help');
        await help.locator('#language-preference').waitFor();
        await help.waitForFunction(() => !document.documentElement.classList.contains('locale-pending'));
        assert.equal(await help.locator('#language-preference').inputValue(), 'fr');
        assert.equal(await help.locator('html').getAttribute('lang'), 'fr');
        await help.getByRole('link', { name: 'Back', exact: true }).click(); await interactive(help);
        assert.equal(await help.locator('#quickCardName').getAttribute('placeholder'), '⟦Search cards⟧');
        await help.evaluate(key => localStorage.setItem(key, 'stale'), preference);
        await help.reload(); await interactive(help);
        assert.equal(await help.locator('#language-preference').inputValue(), 'fr');
        await automatic.close();

        if (process.argv[4]) {
            const baseline = await serve(process.argv[4]); servers.push(baseline.server);
            const b = await browser.newPage({ locale: 'de-DE' }); await b.goto(baseline.url);
            await b.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
            const old = await b.evaluate(() => performance.getEntriesByType('resource').reduce((sum, item) => sum + item.encodedBodySize, 0));
            console.log(`Frozen dev English resource bodies: ${old} bytes; change: ${transfer - old} bytes (uncompressed local HTTP).`);
        }
        assert.deepEqual(errors, []);
        console.log('Published locale lifecycle, selective satellites, first frame, one reload, drafts/results, recovery/share ownership, storage failure, keyboard/native select and mobile header passed.');
    } finally {
        await browser.close();
        for (const server of servers) await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
