// Args: clean optimized published wwwroot, native excavation export, output.json.
// Uses the 40-both-pots fixture, real production workers, and one desktop/mobile
// viewport pair. Run separately from throughput measurements.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
const { serve, load, mode, usable, launchOptions } = require('./browser-support.cjs');

function observe() {
    window.excavationUiRequests = [];
    window.showSaveFilePicker = undefined;
    const NativeWorker = Worker;
    window.Worker = class extends NativeWorker {
        constructor(...args) {
            super(...args);
            if (!String(args[0]).includes('calculation-worker.js')) return;
            this.requests = [];
            this.addEventListener('message', ({ data }) => {
                if (!data.response) return;
                const request = this.requests.findLast(value => value.id === data.id);
                if (request) request.response = JSON.parse(data.response);
            });
        }
        postMessage(message, ...rest) {
            if (this.requests && message.json) {
                const request = { id: message.id, input: JSON.parse(message.json) };
                this.requests.push(request);
                window.excavationUiRequests.push(request);
            }
            return super.postMessage(message, ...rest);
        }
    };
}

async function main() {
    assert(process.argv[2] && process.argv[3] && process.argv[4],
        'Usage: node excavation-ui-smoke.cjs <published-wwwroot> <native-export.json> <output.json>');
    const fixtures = JSON.parse(await fs.readFile(process.argv[3], 'utf8'));
    const fixture = fixtures.find(value => value.Name === '40-both-pots' && value.Mode === 'Fast'
        && value.ReferenceResults);
    assert(fixture?.Session, 'Export a completed 40-both-pots/Fast fixture with a session');
    const session = structuredClone(fixture.Session);
    session.CalculationModel = 'Fast';
    session.FastExecutionStrategy = 'FastOnly';
    const source = session.Cards.find(card => card.Excavation?.Count === 6);
    const second = session.Cards.find(card => card.Excavation?.Count === 3);
    assert(source && second, 'Fixture needs both excavation presets');
    const outputFile = path.resolve(process.argv[4]);
    await fs.mkdir(path.dirname(outputFile), { recursive: true });
    const { server, url } = await serve(process.argv[2]);
    let browser;
    const evidence = { Browser: null, PageErrors: [], Screenshots: [], Results: [] };
    try {
        browser = await chromium.launch(launchOptions());
        evidence.Browser = browser.version();
        const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, locale: 'en-US' });
        page.setDefaultTimeout(30000);
        page.on('pageerror', error => evidence.PageErrors.push(error.message));
        await page.addInitScript(observe);
        await page.goto(url);
        await load(page, session);
        await mode(page, 'FastOnly');
        const action = page.locator('.calculate-action > button');
        const sourceRow = () => page.locator('.card-editor').filter({
            has: page.locator('.card-header-name').filter({ hasText: source.Name })
        });
        async function expandSource() {
            const trigger = sourceRow().locator('.accordion-button');
            if (await trigger.getAttribute('aria-expanded') !== 'true') await trigger.click();
        }
        async function calculate(label) {
            const start = await page.evaluate(() => excavationUiRequests.length);
            await action.click();
            await page.waitForFunction(start => excavationUiRequests.length > start
                && excavationUiRequests.slice(start).every(request => request.response), start);
            await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') === 'false'
                && document.querySelector('.probability-total-value'));
            const requests = await page.evaluate(start => excavationUiRequests.slice(start), start);
            assert.equal(requests.length, 1, 'Fast only uses one worker request');
            const response = requests[0].response;
            assert.equal(response.Error, null, label);
            assert.equal(response.Result.Contract.Accuracy, 0, label);
            evidence.Results.push({ Label: label, Request: requests[0] });
            return response.Result.Contract.Fractions;
        }
        const originalFractions = await calculate('Both presets enabled');
        assert.deepEqual(originalFractions, fixture.ReferenceResults.Contract.Fractions, 'UI fixture exact fractions');
        await expandSource();
        assert.match(await sourceRow().locator('.simulation-summary').innerText(), /\b6\b/);
        const notes = sourceRow().getByRole('button', { name: 'Model notes', exact: true });
        const target = await notes.getAttribute('aria-controls');
        assert(target, 'Model notes has an accessible target');
        await notes.focus();
        await notes.press('Space');
        assert.equal(await notes.getAttribute('aria-expanded'), 'true');
        const note = page.locator(`#${target} .simulation-excavation-note`);
        await note.waitFor({ state: 'visible' });
        const phaseNote = (await note.innerText()).trim();
        assert(phaseNote.length > 0, 'Excavation model restriction is visible');
        evidence.ModelNote = phaseNote;
        for (const [label, width] of [['desktop', 1280], ['mobile', 390]]) {
            await page.setViewportSize({ width, height: 900 });
            await usable(page, notes);
            await usable(page, sourceRow().getByRole('button', { name: 'Disable simulation', exact: true }));
            const screenshot = outputFile.replace(/\.json$/i, '') + `-${label}.png`;
            await sourceRow().scrollIntoViewIfNeeded();
            await page.screenshot({ path: screenshot });
            evidence.Screenshots.push(screenshot);
        }
        await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
        await page.locator('#calculationSettingsPanel').getByText(phaseNote, { exact: true }).waitFor({ state: 'visible' });
        await usable(page, page.locator('#calculationModel'));
        await page.locator('#calculationModel').press('Escape');
        const toggle = sourceRow().getByRole('button', { name: 'Disable simulation', exact: true });
        await toggle.focus();
        await toggle.press('Enter');
        await sourceRow().getByRole('button', { name: 'Enable simulation', exact: true }).waitFor();
        const disabledFractions = await calculate('Prosperity disabled');
        assert.notDeepEqual(disabledFractions, originalFractions, 'Disabling excavation changes the calculation');
        const downloadPromise = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Save Session', exact: true }).click();
        const saved = JSON.parse(await fs.readFile(await (await downloadPromise).path(), 'utf8'));
        const savedSource = saved.Cards.find(card => card.Id === source.Id);
        assert.equal(savedSource.SimulationEnabled, false, 'Saved simulation toggle');
        assert.deepEqual(savedSource.Excavation, source.Excavation, 'Saved excavation plan is preserved');
        assert.deepEqual(savedSource.EffectProfile, source.EffectProfile, 'Saved built-in provenance is preserved');
        // The loader waits for this marker, so an unchanged recovery record
        // cannot make the round-trip assertion race ahead of file acceptance.
        saved.Cards[0].Name = 'Reloaded ' + saved.Cards[0].Name;
        await load(page, saved);
        await expandSource();
        await sourceRow().getByRole('button', { name: 'Enable simulation', exact: true }).waitFor();
        const reloadedFractions = await calculate('Disabled session reloaded');
        assert.deepEqual(reloadedFractions, disabledFractions, 'Disabled session result round trip');
        await sourceRow().getByRole('button', { name: 'Enable simulation', exact: true }).click();
        const restoredFractions = await calculate('Prosperity enabled again');
        assert.deepEqual(restoredFractions, originalFractions, 'Reenabled preset result');
        assert.deepEqual(evidence.PageErrors, []);
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        evidence.Passed = true;
        console.log('Excavation UI smoke: preset notes, responsive controls, toggle, session round trip and exact results passed.');
    } finally {
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
        await fs.writeFile(outputFile, JSON.stringify(evidence, null, 2));
    }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
