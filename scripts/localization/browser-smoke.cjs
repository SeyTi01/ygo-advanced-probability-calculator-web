// node scripts/localization/browser-smoke.cjs <production-wwwroot>
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

async function blocked(page, locale) {
    const before = page.url();
    const saved = await page.evaluate(key => localStorage.getItem(key), preference);
    const selected = await page.locator('#language-preference').inputValue();
    await page.locator('#language-preference').selectOption(locale);
    await page.locator('.content > .alert[role=alert]').waitFor();
    await page.waitForFunction(() => !document.querySelector('.page').inert);
    assert.equal(page.url(), before);
    assert.equal(await page.evaluate(key => localStorage.getItem(key), preference), saved);
    assert.equal(await page.locator('#language-preference').inputValue(), selected);
}

async function translatedProduction(browser, url, track, locale, root, englishPlaceholder) {
    const nameAsset = locale.cardNames?.asset;
    const names = nameAsset
        ? new Map(JSON.parse(fs.readFileSync(path.join(root, nameAsset), 'utf8')).names)
        : null;
    const translatedName = names?.get(35261759);
    if (nameAsset) assert(translatedName, `${locale.id}: the representative maintained card has a verified label`);
    const region = { fr: 'fr-FR', it: 'it-IT', pt: 'pt-BR', ja: 'ja-JP' }[locale.id] ?? locale.id;
    const context = await browser.newContext({ locale: region, viewport: { width: 390, height: 844 } });
    await context.addInitScript(() => {
        window.firstInteractivePlaceholder = null;
        const observe = () => {
            const input = document.querySelector('#quickCardName');
            if (!window.firstInteractivePlaceholder && input && getComputedStyle(input).visibility === 'visible') {
                window.firstInteractivePlaceholder = input.placeholder;
            }
            requestAnimationFrame(observe);
        };
        requestAnimationFrame(observe);
    });
    const page = await context.newPage(); track(page);
    const requests = [];
    page.on('request', request => requests.push(request.url()));
    await page.goto(url + '/help');
    await page.waitForFunction(id => document.documentElement.lang === id && !document.documentElement.classList.contains('locale-pending'), locale.id);
    assert.equal(await page.locator('#language-preference').inputValue(), 'system');
    assert.equal(requests.some(value => /card-names\./.test(value)), false, 'Help loads no card names');
    const satellites = requests.filter(value => /\/[^/]+\/[^/]*resources/.test(value));
    assert(satellites.some(value => value.includes(`/${locale.id}/`)), `${locale.id}: selected satellite loads`);
    assert(satellites.every(value => value.includes(`/${locale.id}/`)), `${locale.id}: unrelated satellites stay unloaded`);
    await usable(page, page.locator('#language-preference'));
    const summary = page.locator('#requirements > summary');
    await summary.focus(); await summary.press('Enter');
    assert.equal(await page.locator('#requirements').evaluate(element => element.open), true);
    await page.goto(url); await interactive(page);
    const placeholder = await page.locator('#quickCardName').getAttribute('placeholder');
    assert(placeholder && placeholder !== englishPlaceholder, 'selected locale renders a translated input');
    assert.equal(await page.evaluate(() => window.firstInteractivePlaceholder), placeholder, 'first visible input uses selected language');
    assert.equal(requests.some(value => /card-names\./.test(value)), false, 'empty calculator defers card names');
    if (!nameAsset) { await context.close(); return; }
    await page.locator('#quickCardName').fill(translatedName);
    await page.locator('#quickCardOption-35261759').waitFor();
    assert.equal(await page.locator('#quickCardOption-35261759').innerText(), translatedName);
    assert.equal(await page.locator('#quickCardOption-35261759').count(), 1, 'localized matches deduplicate by identity');
    await page.locator('#quickCardName').fill('Pot of Desires');
    await page.locator('#quickCardOption-35261759').waitFor();
    assert.equal(await page.locator('#quickCardOption-35261759').innerText(), translatedName);
    const nameAssetPath = `/${nameAsset}`;
    assert.deepEqual(requests.filter(value => /card-names\./.test(value)).map(value => new URL(value).pathname), [nameAssetPath]);
    await page.locator('#quickCardOption-35261759').click();
    await page.waitForFunction(key => {
        try { return JSON.parse(JSON.parse(localStorage.getItem(key)).payload).Cards[0].Name === 'Pot of Desires'; } catch { return false; }
    }, recovery);
    const accepted = await page.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery);
    const card = JSON.parse(accepted).Cards[0];
    assert.equal(card.ExternalCardId, 35261759);
    assert.equal(card.DrawCount, 2);
    assert.equal(card.PreDrawDeckRemovalCount, 10);
    await change(page, 'en'); await change(page, locale.id);
    await page.locator('.card-header-name').filter({ hasText: translatedName }).waitFor();
    assert.equal(await page.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery), accepted);
    await context.close();
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
        const context = await browser.newContext({ locale: 'en-US', viewport: { width: 390, height: 844 } });
        const page = await context.newPage();
        track(page);
        await page.goto(prod.url); await interactive(page);
        assert.equal(await page.locator('#language-preference').inputValue(), 'system');
        assert.equal(await page.locator('#language-preference option').first().getAttribute('value'), 'system');
        const englishPlaceholder = await page.locator('#quickCardName').getAttribute('placeholder');
        assert(englishPlaceholder);
        const productionRequests = await page.evaluate(() => performance.getEntriesByType('resource').map(item => item.name));
        assert.equal(productionRequests.some(url => /\/[^/]+\/[^/]*resources/.test(url)), false);
        assert.equal(productionRequests.some(url => /card-names\./.test(url)), false);
        await context.close();
        const productionLocales = JSON.parse(fs.readFileSync(path.join(process.argv[2], 'localization/locales.json'), 'utf8'));
        for (const locale of productionLocales.filter(locale => locale.id !== 'en')) {
            await translatedProduction(browser, prod.url, track, locale, process.argv[2], englishPlaceholder);
        }

        const test = await browser.newContext({ locale: 'de-DE', viewport: { width: 390, height: 844 }, reducedMotion: 'reduce' });
        await test.addInitScript(key => {
            if (!sessionStorage.getItem('language-smoke:seeded')) {
                localStorage.setItem(key, 'en');
                sessionStorage.setItem('language-smoke:seeded', 'true');
            }
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
        await p.goto(prod.url); await interactive(p);
        assert.equal(await p.locator('#language-preference').inputValue(), 'en', 'explicit English overrides supported de-DE');
        assert.equal(requests.some(url => /\/[^/]+\/[^/]*resources/.test(url)), false, 'explicit English loads no satellite assemblies');
        await usable(p, p.locator('#language-preference'));
        const box = await p.locator('#language-preference').boundingBox(); assert(box.height >= 44);
        const beforeEmpty = documents;
        await change(p, 'de');
        assert.equal(documents - beforeEmpty, 1, 'exactly one document reload');
        await change(p, 'en');

        const role = { Name: 'Target', Source: 'User' };
        const session = { SchemaVersion: 14, CalculationModel: 'Fast', FastExecutionStrategy: 'Progressive', HandSize: 1,
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
        await blocked(p, 'de');
        assert.equal(await p.locator('#quickCardName').inputValue(), 'Unsubmitted card draft');
        await p.locator('#quickCardName').fill('');
        await p.locator('.simulation-edit').first().click();
        await p.locator('#cardDraw0').selectOption('2');
        await blocked(p, 'de');
        assert.equal(await p.locator('#cardDraw0').inputValue(), '2');
        await p.locator('.simulation-editor').getByRole('button', { name: 'Cancel', exact: true }).click();
        await p.locator('#cardCopies0').fill('');
        await blocked(p, 'de');
        await p.locator('#cardCopies0').fill('3');
        assert.equal(await p.evaluate(() => ygoLocale.hasPendingEdits()), false);

        await p.getByRole('button', { name: 'Calculate', exact: true }).click();
        await p.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') === 'false'
            && document.querySelector('.probability-total-value'));
        assert.match(await p.locator('.probability-total .probability-with-accuracy').getAttribute('title'), /(?<!\d)3\/4(?!\d)/);
        const numericResult = await p.locator('.probability-results .probability-total-value').innerText();
        assert.match(numericResult, /75,00\s*%/, 'English UI retains the German browser numeric culture');
        await p.getByRole('button', { name: 'Pin result', exact: true }).click();
        const pinned = await p.locator('.pinned-result').innerText();
        await p.waitForFunction(key => {
            const saved = JSON.parse(JSON.parse(localStorage.getItem(key)).payload);
            return saved.Cards[0].Copies === 3 && saved.Combos[0].Name === 'Accepted rename';
        }, recovery);
        const accepted = await p.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery);
        await change(p, 'system');
        assert.equal(await p.locator('html').getAttribute('lang'), 'de');
        assert.equal(await p.evaluate(key => localStorage.getItem(key), preference), null, 'System clears the explicit override');
        assert.equal(await p.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery), accepted);
        assert.equal(await p.locator('.probability-results .probability-total-value').innerText(), numericResult, 'language reload preserves numeric formatting');
        assert.match(await p.locator('.probability-total .probability-with-accuracy').getAttribute('title'), /(?<!\d)3\/4(?!\d)/);
        assert.equal(await p.locator('.pinned-result').count(), 1);
        await change(p, 'en');
        assert.equal(await p.locator('.pinned-result').innerText(), pinned);
        assert.equal(await p.getByRole('button', { name: 'Restore previous session', exact: true }).count(), 0);

        // A share offer and another tab's recovery are independent of the accepted checkpoint.
        const foreign = JSON.stringify({ version: 1, savedAt: '2026-10-25T00:00:00Z', payload: accepted });
        await p.evaluate(([key, value]) => localStorage.setItem(key, value), [recovery, foreign]);
        await p.evaluate(() => { sessionSharing.copy = async text => { window.testShareLink = text; return true; }; });
        await p.getByRole('button', { name: 'Copy share link', exact: true }).click();
        await p.waitForFunction(() => window.testShareLink);
        const shared = new URL(await p.evaluate(() => window.testShareLink)).hash;
        await p.evaluate(fragment => { location.hash = fragment; }, shared);
        await p.getByRole('button', { name: 'Load shared session', exact: true }).waitFor();
        assert.equal(await p.locator('#language-preference').inputValue(), 'en');
        await change(p, 'de');
        assert.equal(await p.evaluate(key => localStorage.getItem(key), recovery), foreign);
        assert.equal(new URL(p.url()).hash, shared, 'language reload keeps the incoming share');
        await change(p, 'en');
        await p.getByRole('button', { name: 'Load shared session', exact: true }).waitFor();
        await p.getByRole('button', { name: 'Restore previous session', exact: true }).waitFor();
        await p.evaluate(key => {
            const original = Storage.prototype.setItem;
            Storage.prototype.setItem = function (name, value) { if (name === key) throw new Error('Storage denied for test'); return original.call(this, name, value); };
        }, preference);
        await blocked(p, 'de');
        assert.equal(await p.evaluate(key => sessionStorage.getItem(key), handoff), null);
        await test.close();

        const work = await browser.newContext({ locale: 'en-US' });
        const pending = await work.newPage(); track(pending);
        await pending.goto(prod.url); await interactive(pending); await load(pending, session);
        await pending.evaluate(() => {
            const original = Worker.prototype.postMessage;
            Worker.prototype.postMessage = function (message, ...rest) {
                if (message.json) { window.heldCalculation = true; return; }
                return original.call(this, message, ...rest);
            };
        });
        await pending.getByRole('button', { name: 'Calculate', exact: true }).click();
        await pending.waitForFunction(() => window.heldCalculation);
        await blocked(pending, 'de');
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
        await blocked(pending, 'de');
        await pending.evaluate(() => releaseLoad());
        await pending.waitForFunction(key => JSON.parse(JSON.parse(localStorage.getItem(key)).payload).Cards[0].Name === 'Pending replacement', recovery);
        await change(pending, 'de');
        await work.close();

        assert.deepEqual(errors, []);
        console.log('Published satellites, first visible language, card names and reload workspace ownership passed.');
    } finally {
        await browser.close();
        for (const server of servers) await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
