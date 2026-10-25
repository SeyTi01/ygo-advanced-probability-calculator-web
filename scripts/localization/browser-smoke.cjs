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

async function blocked(page, locale, message) {
    const before = page.url();
    const saved = await page.evaluate(key => localStorage.getItem(key), preference);
    await page.locator('#language-preference').selectOption(locale);
    await page.locator('.content > .alert[role=alert]').filter({ hasText: message }).waitFor();
    await page.waitForFunction(() => !document.querySelector('.page').inert);
    assert.equal(page.url(), before);
    assert.equal(await page.evaluate(key => localStorage.getItem(key), preference), saved);
    assert.equal(await page.locator('#language-preference').inputValue(), await page.locator('html').getAttribute('lang'));
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
        await profilePage.route('**/help', route => route.fulfill({ contentType: 'text/html', body: fs.readFileSync(path.join(process.argv[3], 'help.html')) }));
        await profilePage.getByRole('link', { name: 'Help', exact: true }).click();
        assert.equal(await profilePage.locator('#language-preference').inputValue(), 'en');
        await profilePage.getByRole('link', { name: 'Back', exact: true }).click(); await interactive(profilePage);
        assert.equal(await profilePage.locator('#language-preference').inputValue(), 'en', 'English persists across an actual browser process restart');
        await profile.close();

        const role = { Name: 'Target', Source: 'User' };
        const session = { SchemaVersion: 12, CalculationModel: 'Fast', FastExecutionStrategy: 'Progressive', HandSize: 1,
            Categories: [role], Cards: [{ Id: 'a', Name: 'Workspace sentinel', Copies: 2, Categories: [role] },
                { Id: 'b', Name: 'Filler', Copies: 1, Categories: [] }],
            Combos: [{ Name: 'Two thirds', Categories: [{ BaseCategory: role, MinCount: 1, MaxCount: 1 }] }], ComboGroups: [] };
        await load(p, session);
        assert.equal(await p.locator('#language-preference').inputValue(), 'en', 'session load keeps explicit English');
        await p.locator('.card-editor .accordion-button').first().click();
        await p.locator('#cardCopies0').fill('3');
        await p.locator('.combo-editor .accordion-button').click();
        await p.locator('#comboName0').fill('Accepted rename');

        await p.locator('#quickCardName').fill('Unsubmitted card draft');
        await blocked(p, 'fr', /Save\/apply or cancel/);
        assert.equal(await p.locator('#quickCardName').inputValue(), 'Unsubmitted card draft');
        await p.locator('#quickCardName').fill('');
        await p.locator('.simulation-edit').first().click();
        await p.locator('#cardDraw0').selectOption('2');
        await blocked(p, 'fr', /Save\/apply or cancel/);
        assert.equal(await p.locator('#cardDraw0').inputValue(), '2');
        await p.locator('.simulation-editor').getByRole('button', { name: 'Cancel', exact: true }).click();
        await p.locator("button[data-requirement='category:0']").click();
        await p.locator('#minCount0').fill('0');
        await blocked(p, 'fr', /Save\/apply or cancel/);
        await p.locator('.requirement-form').getByRole('button', { name: 'Cancel', exact: true }).click();
        await p.locator('.category-management > summary').click();
        await p.getByRole('textbox', { name: 'Category name', exact: true }).fill('Unsubmitted role');
        await blocked(p, 'fr', /Save\/apply or cancel/);
        await p.getByRole('textbox', { name: 'Category name', exact: true }).fill('');
        await p.locator('.combo-group-management > summary').click();
        await p.getByRole('textbox', { name: 'New combo group name', exact: true }).fill('Unsubmitted group');
        await blocked(p, 'fr', /Save\/apply or cancel/);
        await p.getByRole('textbox', { name: 'New combo group name', exact: true }).fill('');
        await p.locator('.category-management .category-name-trigger').first().click();
        await blocked(p, 'fr', /Save\/apply or cancel/);
        await p.getByRole('button', { name: 'Cancel category edit', exact: true }).click();
        await p.locator('#cardCopies0').fill('');
        await blocked(p, 'fr', /Save\/apply or cancel/);
        await p.locator('#cardCopies0').fill('3');
        assert.equal(await p.evaluate(() => ygoLocale.hasPendingEdits()), false);

        await p.getByRole('button', { name: 'Calculate', exact: true }).click();
        await p.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') === 'false'
            && document.querySelector('.probability-total-value'));
        assert.match(await p.locator('.probability-total .probability-with-accuracy').getAttribute('title'), /(?<!\d)3\/4(?!\d)/);
        await p.getByRole('button', { name: 'Pin result', exact: true }).click();
        const pinned = await p.locator('.pinned-result').innerText();
        await p.waitForFunction(key => {
            const saved = JSON.parse(JSON.parse(localStorage.getItem(key)).payload);
            return saved.Cards[0].Copies === 3 && saved.Combos[0].Name === 'Accepted rename';
        }, recovery);
        const accepted = await p.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery);
        await change(p, 'fr');
        assert.equal(await p.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery), accepted);
        assert.match(await p.locator('.probability-total .probability-with-accuracy').getAttribute('title'), /(?<!\d)3\/4(?!\d)/);
        assert.equal(await p.locator('.pinned-result').innerText(), pinned);
        assert.equal(await p.getByRole('button', { name: 'Restore previous session', exact: true }).count(), 0);
        for (const width of [320, 390, 1280]) { await p.setViewportSize({ width, height: 900 }); await header(p); }
        await p.screenshot({ path: path.resolve('bin/language-desktop.png'), fullPage: true });
        await p.setViewportSize({ width: 390, height: 844 });
        await p.locator('#theme-preference').selectOption('dark'); await header(p);
        await p.screenshot({ path: path.resolve('bin/language-mobile.png'), fullPage: true });

        // A share offer and another tab's recovery are independent of the accepted checkpoint.
        await change(p, 'en');
        const foreign = JSON.stringify({ version: 1, savedAt: '2026-10-25T00:00:00Z', payload: accepted });
        await p.evaluate(([key, value]) => localStorage.setItem(key, value), [recovery, foreign]);
        await p.evaluate(() => { sessionSharing.copy = async text => { window.testShareLink = text; return true; }; });
        await p.getByRole('button', { name: 'Copy share link', exact: true }).click();
        await p.waitForFunction(() => window.testShareLink);
        const shared = new URL(await p.evaluate(() => window.testShareLink)).hash;
        await p.evaluate(fragment => { location.hash = fragment; }, shared);
        await p.getByRole('button', { name: 'Load shared session', exact: true }).waitFor();
        assert.equal(await p.locator('#language-preference').inputValue(), 'en');
        await change(p, 'fr');
        assert.equal(await p.evaluate(key => localStorage.getItem(key), recovery), foreign);
        assert.equal(await p.getByRole('button', { name: 'Load shared session', exact: true }).count(), 1);
        await change(p, 'en');
        await p.evaluate(key => {
            const original = Storage.prototype.setItem;
            Storage.prototype.setItem = function (name, value) { if (name === key) throw new Error('Storage denied for test'); return original.call(this, name, value); };
        }, preference);
        await blocked(p, 'fr', /Language was not changed/);
        assert.equal(await p.evaluate(key => sessionStorage.getItem(key), handoff), null);
        await test.close();

        const work = await browser.newContext({ locale: 'en-US' });
        const pending = await work.newPage(); track(pending);
        await pending.goto(testHost.url); await interactive(pending); await load(pending, session);
        await pending.evaluate(() => {
            const original = Worker.prototype.postMessage;
            Worker.prototype.postMessage = function (message, ...rest) {
                if (message.json) { window.heldCalculation = true; return; }
                return original.call(this, message, ...rest);
            };
        });
        await pending.getByRole('button', { name: 'Calculate', exact: true }).click();
        await pending.waitForFunction(() => window.heldCalculation);
        await blocked(pending, 'fr', /calculation to finish or cancel/);
        await pending.getByRole('button', { name: 'Cancel calculation', exact: true }).click();
        await pending.waitForFunction(() => document.querySelector('.calculate-action > button').getAttribute('aria-busy') === 'false');
        await pending.evaluate(() => {
            const original = Blob.prototype.arrayBuffer;
            Blob.prototype.arrayBuffer = function () {
                const blob = this;
                return new Promise(resolve => { window.releaseLoad = async () => { Blob.prototype.arrayBuffer = original; resolve(await original.call(blob)); }; });
            };
        });
        const replacement = structuredClone(session); replacement.Cards[0].Name = 'Pending replacement';
        await pending.locator('#sessionFileInput').setInputFiles({ name: 'pending.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(replacement)) });
        await pending.waitForFunction(() => typeof releaseLoad === 'function');
        await blocked(pending, 'fr', /session or deck import to finish/);
        await pending.evaluate(() => releaseLoad());
        await pending.waitForFunction(key => JSON.parse(JSON.parse(localStorage.getItem(key)).payload).Cards[0].Name === 'Pending replacement', recovery);
        await change(pending, 'fr');
        await work.close();

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
        console.log('Published locale lifecycle, selective satellites, first frame, one reload, accepted work/results, blocked edits and pending operations, recovery/share ownership, storage failure and keyboard/mobile header passed.');
    } finally {
        await browser.close();
        for (const server of servers) await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
