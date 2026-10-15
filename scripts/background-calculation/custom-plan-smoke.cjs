// Opt-in: node custom-plan-smoke.cjs <optimized-wwwroot> <frozen-cases.json> <evidence-directory>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const { installProbe, load, run, serve } = require('./measure-worker-lifecycle.cjs');

async function main() {
    const [root, fixturesPath, directory] = process.argv.slice(2);
    const fixtures = JSON.parse(fs.readFileSync(fixturesPath, 'utf8'));
    fs.mkdirSync(directory, { recursive: true });
    const { server, url } = await serve(root);
    const browser = await chromium.launch({ headless: true, channel: process.env.YGO_BROWSER_CHANNEL || 'msedge' });
    const evidence = [];
    try {
        for (const [identity, control, field, selector] of [
            [38120068, 'cardCost0', 'PreDrawRemovalCount', 'PreDrawSelector'],
            [1475311, 'cardDiscard0', 'PostDrawDiscardCount', 'PostDrawSelector'],
            [35261759, 'cardDeckCost0', 'PreDrawDeckRemovalCount', null]
        ]) {
            const source = structuredClone(fixtures.flatMap(fixture => fixture.Session?.Cards || [])
                .find(card => card.ExternalCardId === identity));
            assert(source, `Maintained fixture ${identity}`);
            const category = key => ({ Name: key, Source: 'Metadata', MetadataKey: key });
            const session = { SchemaVersion: 5, HandSize: 2, DrawEffectsEnabled: true, Categories: [], ComboGroups: [],
                Cards: [{ ...source, Id: 'custom', Copies: 2, Name: 'Same name' },
                    { ...source, Id: 'built-in', Copies: 1, Name: 'Same name' },
                    { Id: 'material', Name: 'Material', Copies: 2, Categories: ['kind:monster', 'level:8', 'attribute:dark'].map(category) },
                    { Id: 'target', Name: 'Target', Copies: 1, Categories: [] }, { Id: 'filler', Name: 'Filler', Copies: 2, Categories: [] }],
                Combos: [{ Name: 'Target', Categories: [], Cards: [{ CardId: 'target', MinCount: 1, MaxCount: 2 }] }] };
            const context = await browser.newContext({ viewport: { width: 1280, height: 1000 } });
            const page = await context.newPage();
            const errors = [];
            page.on('pageerror', error => errors.push(error.message));
            await page.addInitScript(installProbe);
            await page.goto(url);
            await load(page, session);
            const gear = page.getByRole('button', { name: 'Calculation settings', exact: true });
            await gear.click();
            const model = page.locator('#calculationModel');
            assert.deepEqual(await model.locator('option').evaluateAll(options => options.map(option => option.value)), ['Configured', 'OpeningHand']);
            assert.equal(await model.inputValue(), 'Configured');
            assert(await model.evaluate(element => element === document.activeElement));
            await model.press('Escape');
            assert(await gear.evaluate(element => element === document.activeElement));
            const baseline = await run(page, 2);
            await page.getByRole('button', { name: 'Pin result', exact: true }).click();
            const pinned = await page.locator('.pinned-result').innerText();
            const editor = page.locator('.card-editor').first();
            if (await editor.locator('.accordion-button').getAttribute('aria-expanded') !== 'true') {
                await editor.locator('.accordion-button').click();
            }
            await editor.locator('.simulation-edit').click();
            const input = page.locator(`#${control}`);
            async function zero() {
                if (control === 'cardDeckCost0') {
                    if (!await input.isVisible()) await editor.locator('.simulation-advanced summary').click();
                    await input.fill('0');
                }
                else await input.selectOption('0');
            }
            await zero();
            await editor.locator('.simulation-cancel').click();
            await editor.locator('.simulation-edit').click();
            assert.equal(await input.inputValue(), String(source[field]));
            await zero();
            await editor.locator('.simulation-save').click();
            assert.equal(await editor.locator('.simulation-source').innerText(), 'Custom simulation');
            assert.equal(await page.locator('.pinned-result').innerText(), pinned);
            const custom = await run(page, 2);
            const request = JSON.parse(custom.input);
            assert.equal(request.Cards[0][field], 0);
            assert.equal(request.Cards[1][field], source[field]);
            const before = JSON.parse(baseline.input).Cards[0];
            const expected = { ...before, [field]: 0, EffectProfile: request.Cards[0].EffectProfile };
            if (selector) expected[selector] = request.Cards[0][selector];
            assert.deepEqual(request.Cards[0], expected, 'only the selected operation and custom provenance change');
            assert.equal(request.Cards[0].EffectProfile.Source, 2);
            assert.match(await page.locator('.result-model').innerText(), /Configured effects/);
            for (const width of [1280, 375]) {
                await page.setViewportSize({ width, height: 1000 });
                await editor.locator('.card-simulation').scrollIntoViewIfNeeded();
                assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
                await page.screenshot({ path: path.join(directory, `${identity}-${width}.png`) });
            }
            await gear.click();
            await model.selectOption('OpeningHand');
            await model.press('Escape');
            const opening = await run(page, 2);
            assert(JSON.parse(opening.input).Cards.every(card => card.DrawCount === null));
            await gear.click();
            await model.selectOption('Configured');
            await model.press('Escape');
            const resumed = await run(page, 2);
            assert.deepEqual(resumed.response.Result, custom.response.Result);
            await editor.locator('.simulation-restore').click();
            const restored = await run(page, 2);
            assert.deepEqual(restored.response.Result, baseline.response.Result);
            assert.deepEqual(JSON.parse(restored.input).Cards[0], before);
            if (identity === 38120068) {
                for (const options of [undefined, null, {}, { ConditionalEffectsEnabled: false }, { ConditionalEffectsEnabled: true }]) {
                    const direct = JSON.parse(baseline.input);
                    delete direct.WorkUnits;
                    if (options === undefined) delete direct.Options;
                    else direct.Options = options;
                    const response = await page.evaluate(input => new Promise((resolve, reject) => {
                        const worker = new Worker(new URL('./js/calculation-worker.js', location.href), { type: 'module' });
                        const timeout = setTimeout(() => { worker.terminate(); reject(Error('Wire default check timed out')); }, 30000);
                        worker.onerror = event => { clearTimeout(timeout); worker.terminate(); reject(Error(event.message)); };
                        worker.onmessage = ({ data }) => {
                            if (data.ready) worker.postMessage({ id: 'defaults', json: JSON.stringify(input) });
                            else {
                                clearTimeout(timeout);
                                worker.terminate();
                                if (data.error) reject(Error(data.error));
                                else resolve(JSON.parse(data.response));
                            }
                        };
                    }), direct);
                    if (options && 'ConditionalEffectsEnabled' in options) {
                        assert.equal(response.Result, null);
                        assert.equal(response.FailureKind, 1);
                        assert.match(response.Error, /ConditionalEffectsEnabled/);
                    } else assert.deepEqual(response.Result, baseline.response.Result, 'omitted/default options enforce the saved cost');
                }
            }
            assert.deepEqual(errors, []);
            evidence.push({ identity, baseline: baseline.response.Result, custom: custom.response.Result, restored: true,
                duplicatePreserved: true, options: request.Options, widths: [1280, 375] });
            await context.close();
        }
        fs.writeFileSync(path.join(directory, 'evidence.json'), JSON.stringify(evidence, null, 2));
        console.log('PASS: two models, focus, selective zero costs, independent entries, pins, opening-only, restore, narrow layouts');
    } finally {
        await browser.close();
        server.close();
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
