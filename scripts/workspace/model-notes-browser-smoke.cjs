// Pass an optimized publish wwwroot. Uses real workers for a small frozen mode fixture.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const root = path.resolve(process.argv[2]);
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
    const browser = await chromium.launch({ headless: true, executablePath: process.env.WORKSPACE_BROWSER_PATH || undefined });
    const errors = [];
    const consoleErrors = [];
    const profile = (id, revision = 1, support = 'ModeledWithinRules') => ({
        Source: 'BuiltIn', CanonicalCardId: id, Revision: revision, Summary: 'Saved summary', Support: support,
        Limitations: 'Saved identities are never tracked.', ExecutionModel: 'DrawSequenceV1'
    });
    const card = (id, name, identity, draw, once, revision = 1, support = 'ModeledWithinRules', extra = {}) => ({
        Id: id, Name: name, Copies: 1, Active: true, Categories: [], ManualMetadataCategoryKeys: [],
        ExternalCardId: identity, DrawCount: draw, DrawOncePerTurn: once, EffectProfile: profile(identity, revision, support), ...extra
    });
    const notes = {
        SchemaVersion: 11, CalculationModel: 'OpeningHand', FastExecutionStrategy: 'Progressive', HandSize: 1, Categories: [], Combos: [],
        Cards: [
            card('desires', 'Pot of Desires', 35261759, 2, true, 1, 'ModeledWithinRules', { PreDrawDeckRemovalCount: 10 }),
            card('solar', 'Solar Recharge', 691925, 2, false, 1, 'ModeledWithinRules', {
                PreDrawRemovalCount: 1, PreDrawSelector: { Kind: 'AllOf', Categories: ['metadata:archetype:lightsworn', 'metadata:kind:monster'] }, PostDrawDeckMillCount: 2
            }),
            card('vision', 'Radiant Typhoon Vision', 20508881, 2, true, 2, 'Partial', {
                PostDrawDiscardCount: 1, PostDrawSelector: { Kind: 'AnyOf', Categories: ['metadata:spell-type:quick-play', 'metadata:archetype:radiant%20typhoon'] }
            }),
            card('sekka', "Sekka's Light", 24940422, 2, true, 1, 'Partial'),
            card('greed', 'Pot of Greed', 55144522, 2, false),
            card('old', 'Historical Extravagance', 49238328, 2, true, 1, 'ModeledWithinRules', { FirstActivationOnly: true, BlocksLaterDraws: true }),
            card('custom', 'Custom Desires', 35261759, 2, true, 1, 'ModeledWithinRules', {
                PreDrawDeckRemovalCount: 10, EffectProfile: { Source: 'Custom', CanonicalCardId: 35261759, Revision: null, Summary: null, Support: 'Unmodeled', Limitations: null, ExecutionModel: 'DrawSequenceV1' }
            })
        ]
    };
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
        await page.addInitScript(() => { window.showSaveFilePicker = undefined; });
        await page.goto(`http://127.0.0.1:${server.address().port}`);
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        let loadVersion = 0;
        const load = async session => {
            const payload = structuredClone(session);
            payload.Cards[0].Name += ` (load ${++loadVersion})`;
            await page.locator('#sessionFileInput').setInputFiles({ name: 'model-notes.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(payload)) });
            await page.waitForFunction(expected => document.querySelector('.card-header-name')?.textContent.includes(expected), payload.Cards[0].Name);
        };
        for (const width of [1280, 320]) {
            await page.setViewportSize({ width, height: 1000 });
            for (const theme of ['Light', 'Dark']) {
                await load(notes);
                await page.getByLabel('Color theme preference').selectOption({ label: theme });
                for (const [index, expected] of [[0, 'Banishment-triggered'], [1, 'GY-triggered'], [2, 'Mystical Space Typhoon'], [3, 'Spell/Trap restriction'], [5, 'Saved identities are never tracked.']]) {
                    const editor = page.locator('.card-editor').nth(index);
                    await editor.locator('.accordion-button').click();
                    await editor.getByRole('button', { name: 'Model notes', exact: true }).click();
                    assert.equal(await page.locator("#blazor-error-ui").isVisible(), false, consoleErrors.join("\n"));
                    const text = await editor.locator('.simulation-details').innerText();
                    assert.ok(text.includes(expected), `${index}: ${text}`);
                    if (index < 2) {
                        assert.match(text, /Fast may be exact or uncertain.*completed physical Exact/);
                        assert.doesNotMatch(text, /never tracked|Saved built-in revision/);
                    }
                    if (index === 2 || index === 3) assert.equal(await editor.locator('.simulation-status').innerText(), 'Partial');
                    if (index === 5) assert.match(text, /Saved built-in revision 1/);
                    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
                    if (process.env.WORKSPACE_SCREENSHOTS && index < 2) {
                        await fs.mkdir(process.env.WORKSPACE_SCREENSHOTS, { recursive: true });
                        await editor.screenshot({ path: path.join(process.env.WORKSPACE_SCREENSHOTS, `model-notes-${index}-${width}-${theme.toLowerCase()}.png`) });
                    }
                }
                const quiet = page.locator('.card-editor').nth(4);
                await quiet.locator('.accordion-button').click();
                assert.equal(await quiet.locator('.simulation-details-toggle').count(), 0);
                const custom = page.locator('.card-editor').nth(6);
                await custom.locator('.accordion-button').click();
                assert.equal(await custom.locator('.simulation-source').innerText(), 'Custom simulation');
                const download = page.waitForEvent('download');
                await page.getByRole('button', { name: 'Save Session', exact: true }).click();
                const saved = JSON.parse(await fs.readFile(await (await download).path(), 'utf8'));
                assert.deepEqual(saved.Cards.map(entry => entry.EffectProfile), notes.Cards.map(entry => entry.EffectProfile), 'render/save keeps persisted descriptive metadata unchanged');
                assert.equal(saved.SchemaVersion, 12, 'current schema remains 12');
                assert.equal(saved.Cards[0].PreDrawDeckRemovalCount, 10);
                assert.equal(saved.Cards[1].PostDrawDeckMillCount, 2);
            }
        }
        // Same five-card physical witness as ThreeCalculationModesTest.Counterexample("banish").
        const role = { Name: 'Target', Source: 'User' };
        const witness = {
            SchemaVersion: 11, Categories: [role], HandSize: 2,
            Cards: [
                { Id: 's', Name: 'Search witness', Copies: 1, Categories: [], Search: { Selector: { Kind: 'Category', Categories: ['user:Target'] }, ObjectiveMetadataOnly: false } },
                { Id: 'a', Name: 'Target', Copies: 1, Categories: [role] },
                { Id: 'f', Name: 'Filler', Copies: 2, Categories: [] },
                { Id: 'd', Name: 'Depletion witness', Copies: 1, Categories: [], DrawCount: 1, PreDrawDeckRemovalCount: 1 }
            ],
            Combos: [{ Name: 'Target route', Categories: [{ BaseCategory: role, MinCount: 1, MaxCount: 6 }], Cards: [] }]
        };
        for (const mode of ['OpeningHand', 'Fast', 'Exact']) {
            await load({ ...witness, CalculationModel: mode, FastExecutionStrategy: mode === 'Fast' ? 'FastOnly' : 'Progressive' });
            await page.getByRole('button', { name: 'Calculate', exact: true }).click();
            const result = page.locator('.probability-results .probability-with-accuracy').first();
            await result.waitFor();
            const detail = await result.getAttribute('title');
            if (mode === 'Fast') {
                assert.match(detail, /5\/6/);
                const uncertainty = await result.locator('.probability-uncertainty').innerText();
                assert.match(uncertainty, /^±\s*\d+(?:[.,]\d+)?\s*%$/);
                assert.doesNotMatch(uncertainty, /Uncertified/i);
                assert.ok(Number.parseFloat(uncertainty.slice(1).replace(',', '.')) > 0, `Fast must retain a positive certified radius: ${uncertainty}`);
            } else {
                assert.equal(await result.locator('.probability-uncertainty').innerText(), '±0%');
                if (mode === 'Exact') assert.match(detail, /23\/30/);
            }
        }
        assert.deepEqual(errors, []);
        assert.equal(await page.locator("#blazor-error-ui").isVisible(), false, consoleErrors.join("\n"));
        console.log('Model notes passed: compatible/historical/custom snapshots, unchanged serialized profiles/schema, true Partial notes, 1280/320 light/dark, actual OpeningHand/Fast bounded/physical Exact workers.');
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
