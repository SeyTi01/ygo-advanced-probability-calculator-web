// NODE_PATH=<Playwright modules>; args: optimized wwwroot, modes.json, evidence-dir.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const [root, fixturesPath, evidence] = process.argv.slice(2).map(value => path.resolve(value));
    const fixtures = JSON.parse(await fs.readFile(fixturesPath, 'utf8'));
    await fs.mkdir(evidence, { recursive: true });
    const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            const file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname));
            assert(file.startsWith(root + path.sep));
            response.setHeader('Content-Type', mime[path.extname(file)] || 'application/octet-stream');
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const browser = await chromium.launch({ headless: true, ...(process.env.YGO_BROWSER_EXECUTABLE ? { executablePath: process.env.YGO_BROWSER_EXECUTABLE } : { channel: process.env.YGO_BROWSER_CHANNEL || 'msedge' }) });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.addInitScript(() => {
            window.copiedResults = [];
            Object.defineProperty(navigator, 'clipboard', { value: { writeText: async text => copiedResults.push(text) } });
        });
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        const workerChecks = await page.evaluate(async fixtures => {
            const worker = new Worker('/js/calculation-worker.js', { type: 'module' });
            await new Promise((resolve, reject) => { worker.onmessage = ({ data }) => data.ready ? resolve() : reject(Error(data.error)); });
            const run = input => new Promise((resolve, reject) => {
                worker.onmessage = ({ data }) => data.error ? reject(Error(data.error)) : resolve(JSON.parse(data.response));
                worker.postMessage({ id: 'mode-smoke', json: JSON.stringify(input) });
            });
            const values = {};
            for (const fixture of fixtures) values[fixture.Name] = await run(fixture.Input);
            for (const mode of [999, 'Exact', null]) {
                const input = structuredClone(fixtures[0].Input); input.Options = { Mode: mode };
                values['bad-' + mode] = await run(input);
            }
            const obsolete = structuredClone(fixtures[0].Input); obsolete.Options = { CertifySearchAvailability: false };
            values.obsolete = await run(obsolete);
            worker.terminate();
            return values;
        }, fixtures);
        for (const name of ['opening', 'count-state', 'search-only', 'zero-cost', 'exact-mill2', 'exact-banish10', 'physical-consumed']) {
            const result = workerChecks[name].Result;
            assert.equal(result.Contract.Accuracy, 0, name);
            assert.equal(result.SearchAvailability, undefined, name);
            assert.equal(result.Contract.Fractions.Combos.length, result.ComboProbabilities.length);
            assert.equal(result.Contract.Fractions.Groups.length, result.GroupProbabilities.length);
        }
        assert.deepEqual(workerChecks['physical-consumed'].Result.Contract.Fractions.Combos[0], { Numerator: '1', Denominator: '3' });
        assert.deepEqual(workerChecks['exact-mill2'].Result.Contract.Fractions.Total, { Numerator: '565436063', Denominator: '656253312' });
        assert.deepEqual(workerChecks['exact-banish10'].Result.Contract.Fractions.Total, { Numerator: '693395484057631', Denominator: '805311408021120' });
        assert.equal(workerChecks['fast-bound'].Result.Contract.Accuracy, 1);
        assert.equal(workerChecks.consumed.Result.Contract.Accuracy, 1);
        assert.deepEqual(workerChecks.consumed.Result.SearchAvailability.Combos[0].Lower, { Numerator: '1', Denominator: '3' });
        assert.equal(workerChecks.discard.Result.Contract.Accuracy, 2);
        assert.equal(workerChecks['exact-unsupported'].Result, null);
        assert.equal(workerChecks['exact-unsupported'].FailureKind, 3);
        for (const name of ['bad-999', 'bad-Exact', 'bad-null', 'obsolete']) assert.equal(workerChecks[name].FailureKind, 1, name);
        let sequence = 0;
        async function load(name) {
            const session = structuredClone(fixtures.find(value => value.Name === name).Session);
            const marker = `mode-${name}-${++sequence}`;
            session.Combos[0].Name = marker;
            await page.locator('#sessionFileInput').setInputFiles({ name: 'mode-session.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(session)) });
            await page.waitForFunction(marker => {
                try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Combos[0].Name === marker; }
                catch { return false; }
            }, marker);
        }
        async function calculate() {
            await page.getByRole('button', { name: 'Calculate', exact: true }).click();
            await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        }
        async function mode(value) {
            await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
            await page.locator('#calculationModel').selectOption(value);
            await page.locator('#calculationModel').press('Escape');
        }
        await load('consumed');
        await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
        assert.equal(await page.locator('#calculationModel option').count(), 3);
        assert.equal(await page.getByRole('checkbox', { name: /Certify/ }).count(), 0);
        await page.locator('.calculation-settings-panel').screenshot({ path: path.join(evidence, 'settings-desktop.png') });
        await page.locator('#calculationModel').press('Escape');
        await calculate();
        assert.match(await page.locator('.result-accuracy').innerText(), /Effects — fast.*Bounded/);
        await page.locator('.search-availability summary').click();
        assert.match(await page.locator('.search-availability').innerText(), /width.*pp/);
        await page.getByRole('button', { name: 'Pin result', exact: true }).click();
        await mode('Exact');
        await page.locator('button[title="Copy results"]').click();
        const copied = await page.evaluate(() => copiedResults.at(-1));
        assert.match(copied, /Previous result/);
        assert.match(copied, /Mode: Effects — fast/);
        assert.match(copied, /\[1\/3, 1\/2\]/);
        await calculate();
        assert.match(await page.locator('.probability-results .result-accuracy').innerText(), /Effects — exact.*Exact/);
        assert.match(await page.locator('.pinned-result .result-accuracy').innerText(), /Bounded/);
        assert.equal(await page.locator('.probability-results .search-availability').count(), 0);
        await page.locator('.result-comparison').screenshot({ path: path.join(evidence, 'physical-pinned-desktop.png') });
        await page.getByRole('button', { name: 'Unpin result', exact: true }).click();
        await load('discard');
        await calculate();
        await page.getByRole('button', { name: 'Pin result', exact: true }).click();
        await mode('Exact');
        await calculate();
        assert.match(await page.locator('[role=alert]').innerText(), /Unsupported exact calculation/);
        assert.match(await page.locator('.probability-results .result-accuracy').innerText(), /Uncertified/);
        assert.match(await page.locator('.pinned-result .result-accuracy').innerText(), /Uncertified/);
        await page.locator('.result-comparison').screenshot({ path: path.join(evidence, 'unsupported-pinned-desktop.png') });
        await page.getByRole('button', { name: 'Calculate opening hand only', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.probability-results .result-accuracy')?.textContent.includes('Opening hand only'));
        assert.equal(await page.locator('#calculationModel').inputValue(), 'Exact');
        assert.equal(await page.locator('.probability-results .search-availability').count(), 0);
        assert.equal(await page.locator('.result-difference').count(), 0);
        await calculate();
        assert.match(await page.locator('[role=alert]').innerText(), /Unsupported exact calculation/);
        await page.getByRole('button', { name: 'Unpin result', exact: true }).click();
        await load('exact-mill2');
        await calculate();
        assert.match(await page.locator('.result-accuracy').innerText(), /Effects — exact.*Exact/);
        await page.locator('.probability-results').screenshot({ path: path.join(evidence, 'exact-desktop.png') });
        for (const width of [375, 320]) {
            await page.setViewportSize({ width, height: 900 });
            for (const theme of ['light', 'dark']) {
                await page.emulateMedia({ colorScheme: theme });
                await load('consumed');
                await calculate();
                await page.locator('.search-availability summary').click();
                await page.getByRole('button', { name: 'Pin result', exact: true }).click();
                assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${width}/${theme} page overflow`);
                await page.locator('.result-comparison').screenshot({ path: path.join(evidence, `bounded-pins-${width}-${theme}.png`) });
                await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
                assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${width}/${theme} settings overflow`);
                await page.locator('.calculation-settings-panel').screenshot({ path: path.join(evidence, `settings-${width}-${theme}.png`) });
                await page.locator('#calculationModel').press('Escape');
                assert.equal(await page.getByRole('button', { name: 'Calculation settings', exact: true }).evaluate(node => node === document.activeElement), true);
                await mode('Exact');
                await calculate();
                assert.match(await page.locator('.probability-results .result-accuracy').innerText(), /Effects — exact.*Exact/);
                assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${width}/${theme} physical overflow`);
                await page.locator('.result-comparison').screenshot({ path: path.join(evidence, `physical-pins-${width}-${theme}.png`) });
                await page.getByRole('button', { name: 'Unpin result', exact: true }).click();
            }
        }
        await load('discard'); await calculate();
        assert.match(await page.locator('.result-accuracy').innerText(), /Uncertified/);
        assert.match(await page.locator('.probability-results').innerText(), /hand removal|ordered calculation/i);
        await load('count-state');
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.getByRole('button', { name: 'Cancel calculation', exact: true }).click();
        assert.equal(await page.locator('.probability-results').count(), 0);
        await calculate();
        assert.match(await page.locator('.result-accuracy').innerText(), /Effects — fast.*Exact/);
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await mode('Exact');
        await calculate();
        assert.match(await page.locator('.result-accuracy').innerText(), /Effects — exact.*Exact/);
        assert.deepEqual(errors, []);
        await fs.writeFile(path.join(evidence, 'smoke.json'), JSON.stringify({ browser: browser.version(), errors, workerChecks, copied }));
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
