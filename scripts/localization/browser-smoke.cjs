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
        const measure = document.createElement('canvas').getContext('2d');
        return ['language-preference', 'theme-preference'].every(id => {
            const select = document.getElementById(id);
            if (select.value === 'eo') { return true; } // The isolated pseudo label is deliberately artificial.
            const style = getComputedStyle(select);
            measure.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
            const available = select.clientWidth - parseFloat(style.paddingInlineStart) - parseFloat(style.paddingInlineEnd);
            return measure.measureText(select.selectedOptions[0].label).width <= available;
        });
    }), true, 'selected language and theme labels remain readable beside the native arrow');
    const language = await page.locator('#language-preference').boundingBox();
    const theme = await page.locator('#theme-preference').boundingBox();
    assert(Math.abs(language.width - theme.width) < 1 && Math.abs(language.height - theme.height) < 1,
        'language and theme selectors have the same size');
    assert.equal(await page.evaluate(() => {
        const title = document.querySelector('.app-title').getBoundingClientRect();
        const controls = document.querySelector('.top-row-controls').getBoundingClientRect();
        return title.bottom <= controls.top || title.right <= controls.left;
    }), true, 'title and language controls do not overlap');
}

async function germanProduction(browser, url, track) {
    const context = await browser.newContext({ locale: 'de-DE', viewport: { width: 390, height: 844 } });
    const page = await context.newPage(); track(page);
    page.setDefaultTimeout(15000);
    const requests = [];
    page.on('request', request => requests.push(request.url()));
    await page.goto(url + '/help');
    await page.waitForFunction(() => document.documentElement.lang === 'de' && !document.documentElement.classList.contains('locale-pending'));
    assert.equal(await page.locator('#language-preference').inputValue(), 'de');
    assert.equal(await page.locator('details.help-topic').count(), 9);
    assert.equal(requests.some(value => /card-names\./.test(value)), false, 'Help must not load names');
    assert.equal(requests.some(value => /\/fr\/.*resources/.test(value)), false);
    assert(requests.some(value => /\/de\/.*resources/.test(value)), 'German satellite selected');
    assert.match(await page.locator('meta[name=description]').getAttribute('content'), /^Help and usage guide/);
    assert.equal(await page.locator('link[rel=canonical]').getAttribute('href'), 'https://ygo-calculator.pages.dev/help');
    const topics = ['getting-started', 'properties', 'requirements', 'simulations', 'effect-resolution', 'modes', 'results', 'sessions', 'example'];
    const headings = await page.locator('details.help-topic > summary').allTextContents();
    assert(headings.every(text => !/How |What |Where |When /.test(text)), 'all nine FAQ headings translated');
    for (const width of [1280, 390]) {
        await page.setViewportSize({ width, height: 900 });
        for (const theme of ['light', 'dark']) {
            await page.locator('#theme-preference').selectOption(theme);
            await header(page);
            for (const id of topics) {
                const summary = page.locator(`#${id} > summary`);
                await summary.focus(); await summary.press('Enter');
                assert.equal(await page.locator(`#${id}`).evaluate(element => element.open), true);
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true);
                await summary.press('Space');
                assert.equal(await page.locator(`#${id}`).evaluate(element => element.open), false);
            }
            await page.locator('#requirements > summary').click();
            await page.screenshot({ path: path.resolve(`bin/german-help-${width}-${theme}.png`), fullPage: true });
            await page.locator('#requirements > summary').click();
        }
    }
    for (const id of topics) {
        await page.goto(url + '/help#' + id);
        await page.waitForFunction(id => document.getElementById(id)?.open, id);
    }
    await page.evaluate(() => {
        const link = document.createElement('a'); link.href = '/help#requirements'; link.id = 'german-fragment-test'; link.textContent = 'Fragment';
        document.querySelector('.help-content').prepend(link);
    });
    await page.locator('#german-fragment-test').click();
    await page.waitForFunction(() => document.getElementById('requirements').open);
    await page.locator('#requirements > summary').click();
    await page.goBack();
    await page.waitForFunction(() => location.hash === '#example' && document.getElementById('example').open);
    await page.goForward();
    await page.waitForFunction(() => location.hash === '#requirements' && document.getElementById('requirements').open);
    await page.locator('#requirements > summary').click();
    await page.locator('#german-fragment-test').click();
    await page.waitForFunction(() => document.getElementById('requirements').open);
    await page.goto(url + '/help');
    await page.waitForFunction(() => document.documentElement.lang === 'de' && !document.documentElement.classList.contains('locale-pending'));
    await page.locator('.example-session-action > button').click();
    await page.waitForFunction(() => document.querySelectorAll('.card-editor').length === 24);
    await interactive(page);
    assert.equal(await page.locator('.combo-editor').count(), 8);
    await page.waitForFunction(() => [...document.querySelectorAll('.card-header-name')].some(node => node.textContent.includes('Aschenblüte')));
    assert.equal(requests.filter(value => /card-names\./.test(value)).length, 1, 'populated German deck loads display names once');
    await context.close();

    const searchContext = await browser.newContext({ locale: 'de-DE' });
    const search = await searchContext.newPage(); track(search);
    const nameRequests = [];
    search.on('request', request => { if (/card-names\./.test(request.url())) nameRequests.push(request.url()); });
    await search.goto(url); await interactive(search);
    assert.equal(nameRequests.length, 0, 'empty German calculator does not load names');
    const initial = await search.evaluate(() => performance.getEntriesByType('resource').reduce((sum, item) => sum + item.encodedBodySize, 0));
    const cold = Date.now();
    await search.locator('#quickCardName').fill('Topf der Begierden');
    await search.locator('#quickCardOption-35261759').waitFor();
    console.log(`German startup encoded bodies: ${initial} bytes; cold name search: ${Date.now() - cold} ms.`);
    assert.equal(nameRequests.length, 1);
    const germanName = await search.locator('#quickCardOption-35261759').innerText();
    assert.equal(germanName, 'Topf der Begierden');
    await search.locator('#quickCardName').fill('');
    await search.locator('#quickCardOption-35261759').waitFor({ state: 'detached' });
    const warm = Date.now();
    await search.locator('#quickCardName').fill('Pot of Desires');
    await search.locator('#quickCardOption-35261759').waitFor();
    assert.equal(await search.locator('#quickCardOption-35261759').innerText(), germanName);
    console.log(`Warm English alias search: ${Date.now() - warm} ms; name requests: ${nameRequests.length}.`);
    assert.equal(nameRequests.length, 1);
    await search.locator('#quickCardOption-35261759').click();
    await search.waitForFunction(key => {
        try { return JSON.parse(JSON.parse(localStorage.getItem(key)).payload).Cards[0].Name === 'Pot of Desires'; } catch { return false; }
    }, recovery);
    const imported = await search.evaluate(key => JSON.parse(JSON.parse(localStorage.getItem(key)).payload).Cards[0], recovery);
    assert.equal(imported.ExternalCardId, 35261759);
    assert.equal(imported.DrawCount, 2, 'canonical selection retains maintained draw plan');
    assert.equal(imported.PreDrawDeckRemovalCount, 10);
    assert.equal(imported.EffectProfile.CanonicalCardId, 35261759);
    assert.equal(await search.locator('.card-header-name').first().innerText(), 'Topf der Begierden (1)');
    const canonicalPayload = await search.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery);
    await change(search, 'en');
    assert.equal(await search.locator('.card-header-name').first().innerText(), 'Pot of Desires (1)');
    await change(search, 'de');
    await search.locator('.card-header-name').filter({ hasText: 'Topf der Begierden' }).waitFor();
    assert.equal(await search.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery), canonicalPayload);
    await search.locator('.card-editor .accordion-button').first().click();
    assert.equal(await search.locator('#cardName0').inputValue(), germanName);
    await search.locator('#cardName0').focus();
    await search.locator('#cardName0').blur();
    assert.equal(await search.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery), canonicalPayload);
    await search.setViewportSize({ width: 390, height: 900 });
    await header(search);
    await search.screenshot({ path: path.resolve('bin/german-card-names-mobile.png'), fullPage: true });
    await search.locator('#cardName0').fill('Mein eigener Name');
    await search.locator('#cardName0').press('Escape');
    for (const width of [1280, 390]) {
        await search.setViewportSize({ width, height: 900 });
        for (const theme of ['light', 'dark']) {
            await search.locator('#theme-preference').selectOption(theme); await header(search);
            await search.screenshot({ path: path.resolve(`bin/german-calculator-${width}-${theme}.png`), fullPage: true });
        }
    }
    await change(search, 'en');
    assert.equal(await search.locator('.card-header-name').first().innerText(), 'Mein eigener Name (1)');
    await change(search, 'de');
    assert.equal(await search.locator('.card-header-name').first().innerText(), 'Mein eigener Name (1)');
    await searchContext.close();

    const failureContext = await browser.newContext({ locale: 'de-DE' });
    const failure = await failureContext.newPage();
    await failure.route('**/card-names.de.v1.json', route => route.fulfill({ status: 503, body: '' }));
    await failure.goto(url); await interactive(failure);
    await failure.locator('#quickCardName').fill('Pot of Desires');
    await failure.locator('#quickCardOption-35261759').waitFor();
    assert.equal(await failure.locator('#quickCardOption-35261759').innerText(), 'Pot of Desires');
    await failure.locator('#quickCardName').fill('Eigene Karte ß');
    await failure.locator('.quick-add-row > button').click();
    await failure.locator('.card-header-name').filter({ hasText: 'Eigene Karte ß' }).waitFor();
    await failureContext.close();

    for (const saved of ['en', 'de', 'stale', 'denied']) {
        const startup = await browser.newContext({ locale: 'de-DE' });
        await startup.addInitScript(({ preference, saved }) => {
            if (saved === 'denied') {
                Storage.prototype.getItem = () => { throw new Error('Storage denied'); };
            } else { localStorage.setItem(preference, saved); }
        }, { preference, saved });
        const p = await startup.newPage(); track(p); await p.goto(url); await interactive(p);
        assert.equal(await p.locator('#language-preference').inputValue(), saved === 'en' ? 'en' : 'de');
        await startup.close();
    }
    console.log('Real German production Help, native keyboard/history, example, demand-loaded bilingual search, canonical selection, authored names, fallback and desktop/mobile themes passed.');
}

async function translatedProduction(browser, url, track, locale, root) {
    const names = new Map(JSON.parse(fs.readFileSync(path.join(root, `data/card-names.${locale.id}.v1.json`), 'utf8')).names);
    const translatedName = names.get(35261759);
    assert(translatedName, `${locale.id}: the representative maintained card has a verified label`);
    const region = { fr: 'fr-FR', it: 'it-IT', pt: 'pt-BR', ja: 'ja-JP' }[locale.id];
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
    assert.equal(await page.locator('#language-preference').inputValue(), locale.id);
    assert.equal(await page.locator('details.help-topic').count(), 9);
    assert.equal(requests.some(value => /card-names\./.test(value)), false, 'Help loads no card names');
    const satellites = requests.filter(value => /\/[^/]+\/[^/]*resources/.test(value));
    assert(satellites.some(value => value.includes(`/${locale.id}/`)), `${locale.id}: selected satellite loads`);
    assert(satellites.every(value => value.includes(`/${locale.id}/`)), `${locale.id}: unrelated satellites stay unloaded`);
    const uiSatelliteBytes = await page.evaluate(id => performance.getEntriesByType('resource')
        .filter(item => item.name.includes(`/${id}/`) && /resources/.test(item.name))
        .reduce((sum, item) => sum + item.encodedBodySize, 0), locale.id);
    assert.match(await page.locator('meta[name=description]').getAttribute('content'), /^Help and usage guide/);
    for (const width of [1280, 390]) {
        await page.setViewportSize({ width, height: 900 });
        for (const theme of ['light', 'dark']) {
            await page.locator('#theme-preference').selectOption(theme);
            await header(page);
            const summary = page.locator('#requirements > summary');
            await summary.focus(); await summary.press('Enter');
            assert.equal(await page.locator('#requirements').evaluate(element => element.open), true);
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true);
            await page.screenshot({ path: path.resolve(`bin/${locale.id}-help-${width}-${theme}.png`), fullPage: true });
            await summary.press('Space');
        }
    }
    await page.goto(url); await interactive(page);
    const placeholder = await page.locator('#quickCardName').getAttribute('placeholder');
    assert.notEqual(placeholder, 'Search cards or enter a name');
    assert.equal(await page.evaluate(() => window.firstInteractivePlaceholder), placeholder, 'first visible input uses selected language');
    assert.equal(requests.some(value => /card-names\./.test(value)), false, 'empty calculator defers card names');
    const initial = await page.evaluate(() => performance.getEntriesByType('resource').reduce((sum, item) => sum + item.encodedBodySize, 0));
    await page.locator('#quickCardName').fill(translatedName);
    await page.locator('#quickCardOption-35261759').waitFor();
    assert.equal(await page.locator('#quickCardOption-35261759').innerText(), translatedName);
    assert.equal(await page.locator('#quickCardOption-35261759').count(), 1, 'localized matches deduplicate by identity');
    await page.locator('#quickCardName').fill('Pot of Desires');
    await page.locator('#quickCardOption-35261759').waitFor();
    assert.equal(await page.locator('#quickCardOption-35261759').innerText(), translatedName);
    assert.deepEqual(requests.filter(value => /card-names\./.test(value)).map(value => new URL(value).pathname), [`/data/card-names.${locale.id}.v1.json`]);
    const nameAssetBytes = await page.evaluate(id => performance.getEntriesByType('resource')
        .filter(item => item.name.includes(`/card-names.${id}.v1.json`))
        .reduce((sum, item) => sum + item.encodedBodySize, 0), locale.id);
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
    await page.locator('.card-editor .accordion-button').first().click();
    await page.locator('#cardName0').fill('Authored name 日本語 ç');
    await page.locator('#cardName0').press('Escape');
    for (const width of [1280, 390]) {
        await page.setViewportSize({ width, height: 900 });
        for (const theme of ['light', 'dark']) {
            await page.locator('#theme-preference').selectOption(theme); await header(page);
            await page.screenshot({ path: path.resolve(`bin/${locale.id}-calculator-${width}-${theme}.png`), fullPage: true });
        }
    }
    await change(page, 'en'); await change(page, locale.id);
    assert.equal(await page.locator('.card-header-name').first().innerText(), 'Authored name 日本語 ç (1)');
    if (locale.id === 'ja') {
        await page.locator('#quickCardName').fill(names.get(29302858));
        await page.locator('#quickCardOption-29302858').click();
        await page.waitForFunction(key => {
            try { return JSON.parse(JSON.parse(localStorage.getItem(key)).payload).Cards.some(card => card.ExternalCardId === 29302858); }
            catch { return false; }
        }, recovery);
        const session = JSON.parse(await page.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery));
        const razen = session.Cards.find(card => card.ExternalCardId === 29302858);
        const archetype = razen.Categories.find(category => category.MetadataKey === 'archetype:vanquish%20soul');
        assert.equal(archetype.Name, 'Archetype: Vanquish Soul');
        session.Cards = [razen]; session.Categories = []; session.HandSize = 1; session.ComboGroups = [];
        session.Combos = [{ Name: 'VS archetype', Categories: [{ BaseCategory: archetype, MinCount: 1, MaxCount: 1 }] }];
        await load(page, session);
        const comboToggle = page.locator('.combo-editor .accordion-button').first();
        if (await comboToggle.getAttribute('aria-expanded') === 'true') { await comboToggle.click(); }
        await page.locator('.combo-header-requirements').filter({ hasText: 'テーマ: ＶＳ' }).waitFor();
        assert.match(await page.locator('.combo-header-requirements').innerText(), /テーマ: ＶＳ/);
        const cardToggle = page.locator('.card-editor .accordion-button').first();
        if (await cardToggle.getAttribute('aria-expanded') !== 'true') { await cardToggle.click(); }
        const inspector = page.locator('.card-property-inspector').first();
        if (!await inspector.evaluate(element => element.open)) { await inspector.locator('summary').click(); }
        await page.locator('.detected-card-properties').filter({ hasText: 'テーマ: ＶＳ' }).waitFor();
        assert.match(await page.locator('.detected-card-properties').innerText(), /テーマ: ＶＳ/);
        for (const [width, theme] of [[1280, 'light'], [390, 'dark']]) {
            await page.setViewportSize({ width, height: 900 });
            await page.locator('#theme-preference').selectOption(theme); await header(page);
            await page.screenshot({ path: path.resolve(`bin/ja-archetype-${width}-${theme}.png`), fullPage: true });
        }
        const stored = JSON.parse(await page.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery));
        assert.equal(stored.Cards[0].Categories.find(category => category.MetadataKey === archetype.MetadataKey).Name, archetype.Name);
    }
    console.log(`${locale.id} selected UI satellite encoded body: ${uiSatelliteBytes} bytes; calculator resource bodies: ${initial} bytes; selected name asset: ${nameAssetBytes} bytes; translated Help, bilingual canonical selection and preserved authored state passed.`);
    await context.close();

    const fallback = await browser.newContext({ locale: region });
    const missing = await fallback.newPage(); track(missing);
    await missing.route(`**/card-names.${locale.id}.v1.json`, route => route.fulfill({ contentType: 'application/json', body: JSON.stringify({ version: 1, locale: locale.id, names: [] }) }));
    await missing.goto(url); await interactive(missing);
    await missing.locator('#quickCardName').fill('Pot of Desires');
    await missing.locator('#quickCardOption-35261759').waitFor();
    assert.equal(await missing.locator('#quickCardOption-35261759').innerText(), 'Pot of Desires', 'missing selected-language name retains canonical English');
    await fallback.close();

    for (const saved of ['en', 'stale', 'denied']) {
        const startup = await browser.newContext({ locale: region });
        await startup.addInitScript(({ preference, saved }) => {
            if (saved === 'denied') { Storage.prototype.getItem = () => { throw new Error('Storage denied'); }; }
            else { localStorage.setItem(preference, saved); }
        }, { preference, saved });
        const cold = await startup.newPage(); track(cold); await cold.goto(url); await interactive(cold);
        assert.equal(await cold.locator('#language-preference').inputValue(), saved === 'en' ? 'en' : locale.id);
        await startup.close();
    }
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
        assert.equal(await page.locator('#language-preference').inputValue(), 'en');
        assert.equal(await page.locator('#quickCardName').getAttribute('placeholder'), 'Search cards or enter a name');
        const transfer = await page.evaluate(() => performance.getEntriesByType('resource').reduce((sum, item) => sum + item.encodedBodySize, 0));
        const productionRequests = await page.evaluate(() => performance.getEntriesByType('resource').map(item => item.name));
        assert.equal(productionRequests.some(url => /\/(de|fr|it|pt|ja)\/.*resources/.test(url)), false);
        assert.equal(productionRequests.some(url => /card-names\./.test(url)), false);
        console.log(`Production English first visit encoded resource bodies: ${transfer} bytes.`);
        await usable(page, page.locator('#theme-preference'));
        await context.close();
        await germanProduction(browser, prod.url, track);
        const productionLocales = JSON.parse(fs.readFileSync(path.join(process.argv[2], 'localization/locales.json'), 'utf8'));
        for (const locale of productionLocales.filter(locale => !['en', 'de'].includes(locale.id))) {
            await translatedProduction(browser, prod.url, track, locale, process.argv[2]);
        }

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
        console.log('Satellite requests on English:', requests.filter(url => /\/(eo|de)\//.test(url)));
        assert.equal(requests.some(url => /\/(eo|de)\/.*resources/.test(url)), false, 'English downloads no pseudo satellites');
        await usable(p, p.locator('#language-preference'));
        const box = await p.locator('#language-preference').boundingBox(); assert(box.height >= 44);
        const beforeEmpty = documents;
        await change(p, 'eo');
        assert.equal(documents - beforeEmpty, 1, 'exactly one document reload');
        assert.equal(await p.locator('#quickCardName').getAttribute('placeholder'), '⟦Search cards⟧');
        assert.equal(await p.evaluate(() => window.firstInteractivePlaceholder), '⟦Search cards⟧');
        assert(requests.some(url => /\/eo\/.*resources/.test(url)), 'selected satellite requested');
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
        assert.equal(await p.locator('#language-preference').inputValue(), 'eo');
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
        await blocked(p, 'eo', /Save\/apply or cancel/);
        assert.equal(await p.locator('#quickCardName').inputValue(), 'Unsubmitted card draft');
        await p.locator('#quickCardName').fill('');
        await p.locator('.simulation-edit').first().click();
        await p.locator('#cardDraw0').selectOption('2');
        await blocked(p, 'eo', /Save\/apply or cancel/);
        assert.equal(await p.locator('#cardDraw0').inputValue(), '2');
        await p.locator('.simulation-editor').getByRole('button', { name: 'Cancel', exact: true }).click();
        await p.locator("button[data-requirement='category:0']").click();
        await p.locator('#minCount0').fill('0');
        await blocked(p, 'eo', /Save\/apply or cancel/);
        await p.locator('.requirement-form').getByRole('button', { name: 'Cancel', exact: true }).click();
        await p.locator('.category-management > summary').click();
        await p.getByRole('textbox', { name: 'Category name', exact: true }).fill('Unsubmitted role');
        await blocked(p, 'eo', /Save\/apply or cancel/);
        await p.getByRole('textbox', { name: 'Category name', exact: true }).fill('');
        await p.locator('.combo-group-management > summary').click();
        await p.getByRole('textbox', { name: 'New combo group name', exact: true }).fill('Unsubmitted group');
        await blocked(p, 'eo', /Save\/apply or cancel/);
        await p.getByRole('textbox', { name: 'New combo group name', exact: true }).fill('');
        await p.locator('.category-management .category-name-trigger').first().click();
        await blocked(p, 'eo', /Save\/apply or cancel/);
        await p.getByRole('button', { name: 'Cancel category edit', exact: true }).click();
        await p.locator('#cardCopies0').fill('');
        await blocked(p, 'eo', /Save\/apply or cancel/);
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
        await change(p, 'de');
        assert.equal(await p.evaluate(key => JSON.parse(localStorage.getItem(key)).payload, recovery), accepted);
        assert.match(await p.locator('.probability-total .probability-with-accuracy').getAttribute('title'), /(?<!\d)3\/4(?!\d)/);
        assert.equal(await p.locator('.pinned-result').count(), 1);
        for (const width of [1280, 390]) {
            await p.setViewportSize({ width, height: 900 });
            for (const theme of ['light', 'dark']) {
                await p.locator('#theme-preference').selectOption(theme); await header(p);
                await p.screenshot({ path: path.resolve(`bin/german-results-${width}-${theme}.png`), fullPage: true });
            }
        }
        await p.locator('.calculation-settings-trigger').click();
        await usable(p, p.locator('#calculationModel'));
        await p.screenshot({ path: path.resolve('bin/german-settings-mobile.png'), fullPage: true });
        await p.locator('#calculationModel').press('Escape');
        await change(p, 'en');
        assert.equal(await p.locator('.pinned-result').innerText(), pinned);
        await change(p, 'eo');
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
        await change(p, 'eo');
        assert.equal(await p.evaluate(key => localStorage.getItem(key), recovery), foreign);
        assert.equal(await p.getByRole('button', { name: 'Load shared session', exact: true }).count(), 1);
        await change(p, 'en');
        await p.evaluate(key => {
            const original = Storage.prototype.setItem;
            Storage.prototype.setItem = function (name, value) { if (name === key) throw new Error('Storage denied for test'); return original.call(this, name, value); };
        }, preference);
        await blocked(p, 'eo', /Language was not changed/);
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
        await blocked(pending, 'eo', /calculation to finish or cancel/);
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
        await blocked(pending, 'eo', /session or deck import to finish/);
        await pending.evaluate(() => releaseLoad());
        await pending.waitForFunction(key => JSON.parse(JSON.parse(localStorage.getItem(key)).payload).Cards[0].Name === 'Pending replacement', recovery);
        await change(pending, 'eo');
        await work.close();

        // Browser detection, unsupported saved preference, and canonical Help transition.
        const automatic = await browser.newContext({ locale: 'eo' });
        const help = await automatic.newPage();
        track(help);
        await help.route('**/help', route => route.fulfill({ contentType: 'text/html', body: fs.readFileSync(path.join(process.argv[3], 'help.html')) }));
        await help.goto(testHost.url + '/help');
        await help.locator('#language-preference').waitFor();
        await help.waitForFunction(() => !document.documentElement.classList.contains('locale-pending'));
        assert.equal(await help.locator('#language-preference').inputValue(), 'eo');
        assert.equal(await help.locator('html').getAttribute('lang'), 'eo');
        await help.getByRole('link', { name: 'Back', exact: true }).click(); await interactive(help);
        assert.equal(await help.locator('#quickCardName').getAttribute('placeholder'), '⟦Search cards⟧');
        await help.evaluate(key => localStorage.setItem(key, 'stale'), preference);
        await help.reload(); await interactive(help);
        assert.equal(await help.locator('#language-preference').inputValue(), 'eo');
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
