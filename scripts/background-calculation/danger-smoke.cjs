// Opt-in Danger base-effect editor and shipped-worker acceptance.
// node danger-smoke.cjs <clean-stripped-AOT-wwwroot> [native-benchmark.json] [output.json]
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
const { serve, load, mode, usable, launchOptions } = require('./browser-support.cjs');

function observe() {
    window.dangerSmoke = { requests: [] };
    window.showSaveFilePicker = undefined;
    const NativeWorker = Worker;
    window.Worker = class extends NativeWorker {
        constructor(...args) {
            super(...args);
            if (!String(args[0]).includes('calculation-worker.js')) return;
            this.addEventListener('message', ({ data }) => {
                if (!data.response) return;
                const request = dangerSmoke.requests.findLast(item => item.id === data.id);
                if (request) request.response = JSON.parse(data.response);
            });
        }
        postMessage(message, ...rest) {
            if (message.json) dangerSmoke.requests.push({ id: message.id, input: JSON.parse(message.json) });
            return super.postMessage(message, ...rest);
        }
    };
}

const a = { Name: 'Oracle A', Source: 'User' };
const b = { Name: 'Oracle B', Source: 'User' };
// Independently enumerated by TwoPhaseSearchPriorityTest:
// every random physical discard and every labeled Deck permutation.
const session = {
    SchemaVersion: 14, CalculationModel: 'Exact', FastExecutionStrategy: 'Progressive', HandSize: 2,
    Categories: [a, b], Cards: [
        { Id: 'danger', Name: 'Danger smoke source', Copies: 1, Categories: [], ExternalCardId: 43316238,
            DrawOperation: 'DangerReveal', DrawOncePerTurn: false },
        { Id: 'a', Name: 'Oracle A card', Copies: 1, Categories: [a] },
        { Id: 'b', Name: 'Oracle B card', Copies: 1, Categories: [b] }
    ],
    Combos: [
        { Name: 'Every outcome', Categories: [], Cards: [{ CardId: 'a', MinCount: 0, MaxCount: 1 }], GroupId: 'g' },
        { Name: 'No source remains', Categories: [], Cards: [{ CardId: 'danger', MinCount: 0, MaxCount: 0 }], GroupId: 'g' },
        { Name: 'A survives', Categories: [{ BaseCategory: a, MinCount: 1, MaxCount: 1 }] },
        { Name: 'B survives', Categories: [{ BaseCategory: b, MinCount: 1, MaxCount: 1 }] }
    ], ComboGroups: [{ Id: 'g', Name: 'Mass and no-draw outcomes' }]
};

async function main() {
    assert(process.argv[2], 'Provide a clean optimized published wwwroot directory');
    const workerTimeoutMs = Number(process.env.YGO_DANGER_WORKER_TIMEOUT_MS ?? 600000);
    assert(Number.isSafeInteger(workerTimeoutMs) && workerTimeoutMs > 0,
        'YGO_DANGER_WORKER_TIMEOUT_MS must be a positive finite safe integer');
    const { server, url } = await serve(process.argv[2]);
    const browser = await chromium.launch(launchOptions()).catch(error => { server.close(); throw error; });
    const evidence = { browser: browser.version(), workerTimeoutMs, terminal: [], benchmark: [], errors: [] };
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, locale: 'en-US' });
        page.setDefaultTimeout(30000);
        page.on('pageerror', error => evidence.errors.push(error.message));
        await page.addInitScript(observe);
        await page.goto(url);
        await load(page, session);
        const source = page.locator('.card-editor').first();
        await source.locator('.accordion-button').click();
        await source.locator('.simulation-edit').click();
        assert.equal(await source.locator('#cardDraw0').inputValue(), 'danger');
        assert.equal(await source.locator('#cardCost0, #cardDiscard0, #cardDeckCost0').count(), 0);
        assert.equal(await source.getByRole('switch', { name: 'Once per turn', exact: true }).count(), 0);
        await source.locator('#cardDraw0').selectOption('1');
        await source.locator('.simulation-cancel').click();
        await source.locator('.simulation-edit').click();
        assert.equal(await source.locator('#cardDraw0').inputValue(), 'danger', 'Cancel retains the special operation');
        await source.locator('#cardDraw0').selectOption('1');
        await source.locator('.simulation-save').click();
        await source.locator('.simulation-restore').click();
        await source.locator('.simulation-edit').click();
        assert.equal(await source.locator('#cardDraw0').inputValue(), 'danger', 'Restore installs the maintained special operation');
        await source.locator('.simulation-cancel').click();
        await page.setViewportSize({ width: 390, height: 900 });
        await page.getByLabel('Color theme preference').selectOption({ label: 'Dark' });
        const toggle = source.locator('.simulation-toggle');
        await usable(page, toggle);
        await toggle.focus(); await page.keyboard.press('Enter');
        await source.getByRole('button', { name: 'Enable simulation', exact: true }).waitFor();
        const downloadPromise = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Save Session', exact: true }).click();
        const saved = JSON.parse(fs.readFileSync(await (await downloadPromise).path(), 'utf8'));
        assert.equal(saved.SchemaVersion, 14);
        assert.equal(saved.Cards[0].DrawOperation, 'DangerReveal');
        assert.equal(saved.Cards[0].SimulationEnabled, false);
        assert.equal(saved.Cards[0].DrawCount, undefined);
        saved.Cards[0].Name = 'Reloaded Danger smoke source';
        await load(page, saved);
        await source.locator('.accordion-button').click();
        await source.getByRole('button', { name: 'Enable simulation', exact: true }).click();
        for (const [selection, numericMode] of [['Exact', 2], ['FastOnly', 0]]) {
            await mode(page, selection);
            const start = await page.evaluate(() => dangerSmoke.requests.length);
            await page.getByRole('button', { name: 'Calculate', exact: true }).click();
            await page.waitForFunction(start => dangerSmoke.requests.length > start
                && dangerSmoke.requests.slice(start).every(request => request.response), start);
            await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') === 'false');
            const requests = await page.evaluate(start => dangerSmoke.requests.slice(start), start);
            assert.equal(requests.length, 1);
            const { input, response } = requests[0];
            assert.equal(input.Options.Mode, numericMode);
            assert.equal(input.DrawOperationVersion, 1);
            assert.equal(input.Cards[0].DrawOperation, 1);
            assert.equal(input.Cards[0].DrawCount, null);
            assert.equal(response.Error, null);
            const result = response.Result;
            assert.equal(result.Contract.Accuracy, 0);
            assert.equal(result.Contract.Completion, 0);
            assert.equal(result.Contract.RequestedMode, numericMode);
            const fractions = result.Contract.Fractions;
            assert.deepEqual([fractions.Total, ...fractions.Combos, ...fractions.Groups]
                .map(value => `${value.Numerator}/${value.Denominator}`), ['1/1', '1/1', '1/1', '2/3', '2/3', '1/1']);
            assert.deepEqual(result.ComboProbabilities.map(row => row.ComboName), session.Combos.map(combo => combo.Name));
            assert.deepEqual(result.GroupProbabilities.map(row => row.GroupId), ['g']);
            evidence.terminal.push({ selection, requests });
        }
        if (process.argv[3]) {
            const cases = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
            for (const fixture of cases) {
                const started = performance.now();
                const actual = await page.evaluate(({ input, timeoutMs }) => new Promise((resolve, reject) => {
                    const worker = new Worker('/js/calculation-worker.js', { type: 'module' });
                    const timer = setTimeout(() => { worker.terminate(); reject(Error('Danger worker timeout')); }, timeoutMs);
                    worker.onerror = event => { clearTimeout(timer); worker.terminate(); reject(Error(event.message)); };
                    worker.onmessage = ({ data }) => {
                        if (data.ready) return worker.postMessage({ id: 'danger-parity', json: JSON.stringify(input),
                            ...(input.Options.Mode === 2 ? { execution: 'interactive-exact-v1' } : {}) });
                        clearTimeout(timer); worker.terminate();
                        if (data.error) reject(Error(data.error)); else resolve(JSON.parse(data.response));
                    };
                }), { input: fixture.Input, timeoutMs: workerTimeoutMs });
                const milliseconds = performance.now() - started;
                assert.deepEqual(actual, fixture.Response, fixture.Name + ': full result, fraction, row order and proof metadata');
                evidence.benchmark.push({ Name: fixture.Name, Milliseconds: milliseconds, Response: actual });
                console.log(`${fixture.Name}: ${fixture.Outcome}, ${milliseconds.toFixed(2)} ms`);
            }
        }
        assert.deepEqual(evidence.errors, []);
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        if (process.argv[4]) fs.writeFileSync(process.argv[4], JSON.stringify(evidence));
        console.log('Danger oracle fractions, worker contract, editor drafts, disable/restore and desktop/mobile acceptance passed.');
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
