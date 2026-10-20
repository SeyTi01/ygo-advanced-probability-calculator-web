// Opt-in real optimized-worker proof. Export PhysicalEnumerationBenchmark first.
// node unlimited-exact-smoke.cjs <published-wwwroot> <enumeration-fixtures.json> <output.json>
// YGO_BROWSER_EXECUTABLE can select an installed browser. The 180-second ceiling
// belongs only to this harness; the application has no calculation timeout.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
const { serve } = require('./measure-worker-lifecycle.cjs');

async function main() {
    const [root, fixturesPath, output] = process.argv.slice(2);
    const fixture = JSON.parse(fs.readFileSync(fixturesPath)).find(value => value.Name === 'selector-edge-9');
    const input = structuredClone(fixture.Input);
    input.WorkUnits = 150000000;
    input.HandSize = 4;
    input.Cards.find(card => card.Id === 'd').Copies = 1;
    input.Cards.find(card => card.Id === 'f').Copies++;
    const reference = { Total: { Numerator: '793381', Denominator: '959595' },
        Combos: [{ Numerator: '51103', Denominator: '73815' }, { Numerator: '851177', Denominator: '1919190' }], Groups: [] };
    const { server, url } = await serve(root);
    const browser = await chromium.launch({ headless: true, executablePath: process.env.YGO_BROWSER_EXECUTABLE })
        .catch(error => { server.close(); throw error; });
    const evidence = { browser: browser.version(), calls: [] };
    try {
        const page = await browser.newPage();
        await page.goto(url);
        await page.evaluate(async () => {
            const module = await import('./js/background-calculation.js');
            window.exactOwner = module.createOwner();
            window.runExactProbe = async (input, interactive) => {
                const job = exactOwner.createJob();
                const started = performance.now();
                // No caller-owned Work fields are changed to grant unlimited work.
                try {
                    const response = JSON.parse(await job.run(JSON.stringify(input), false, interactive));
                    if (response.Result) job.release(); else job.dispose();
                    return { response, milliseconds: performance.now() - started };
                } catch (error) { job.dispose(); throw error; }
            };
        });
        const bounded = await page.evaluate(input => runExactProbe(input, false), input);
        assert.equal(bounded.response.LimitReason, 1);
        evidence.calls.push({ mode: 'finite compatibility', ...bounded });
        // First explicit Exact is cold; second reuses the accepted worker.
        for (let trial = 0; trial < 2; trial++) {
            const result = await page.evaluate(input => Promise.race([
                runExactProbe(input, true),
                new Promise((_, reject) => setTimeout(() => reject(Error('Test-only ceiling')), 180000))
            ]), input);
            assert.equal(result.response.Result.Contract.Accuracy, 0);
            assert.deepEqual(result.response.Result.Contract.Fractions, reference);
            const worker = page.workers().find(worker => worker.url().endsWith('/js/calculation-worker.js'));
            const memory = worker ? await worker.evaluate(() => {
                const runtime = globalThis.getDotnetRuntime?.(0);
                const heap = runtime?.Module?.HEAPU8;
                return { linearMemoryBytes: heap?.buffer.byteLength ?? null };
            }) : null;
            evidence.calls.push({ mode: trial ? 'warm unlimited Exact' : 'cold unlimited Exact', memory, ...result });
            fs.writeFileSync(output, JSON.stringify(evidence, null, 2));
        }
        await page.evaluate(() => exactOwner.dispose());
        // Exercise the same high-work case through the real component, including
        // cancellation and a fresh request. No worker response is substituted.
        const session = structuredClone(fixture.Session);
        session.HandSize = 4;
        session.Cards.find(card => card.Id === 'd').Copies = 1;
        session.Cards.find(card => card.Id === 'f').Copies++;
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        await page.locator('#sessionFileInput').setInputFiles({ name: 'unlimited-exact.json', mimeType: 'application/json',
            buffer: Buffer.from(JSON.stringify(session)) });
        await page.waitForFunction(() => document.querySelector('#handSize')?.value === '4');
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.getByRole('button', { name: 'Cancel calculation', exact: true }).waitFor();
        await page.waitForTimeout(2000);
        const cancelStarted = Date.now();
        await page.getByRole('button', { name: 'Cancel calculation', exact: true }).click();
        await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
        evidence.cancelUiMilliseconds = Date.now() - cancelStarted;
        await page.evaluate(() => {
            window.exactFrames = 0;
            window.countExactFrames = true;
            function frame() { if (countExactFrames) { exactFrames++; requestAnimationFrame(frame); } }
            requestAnimationFrame(frame);
        });
        const uiStarted = Date.now();
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.probability-results .probability-total-value'), null, { timeout: 180000 });
        assert.equal(await page.getByRole('alert').count(), 0);
        assert.equal(await page.locator('.probability-results .probability-result-status').count(), 0);
        const titles = await page.locator('.probability-results .probability-with-accuracy').evaluateAll(elements => elements.map(element => element.title));
        for (const fraction of [reference.Total, ...reference.Combos]) {
            assert(titles.some(title => title.includes(`${fraction.Numerator}/${fraction.Denominator}`)));
        }
        evidence.ui = { milliseconds: Date.now() - uiStarted,
            frames: await page.evaluate(() => { countExactFrames = false; return exactFrames; }), titles };
        assert(evidence.ui.frames > 1, 'Explicit Exact must leave the UI responsive');
        fs.writeFileSync(output, JSON.stringify(evidence, null, 2));
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
