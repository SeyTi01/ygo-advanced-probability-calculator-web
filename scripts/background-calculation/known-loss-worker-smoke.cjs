// Opt-in normal UI/worker timing: wwwroot, original session, native measurements, output.
// Optional fifth/sixth args: exported fixtures and one control variant name.
// Use the same optimized build and browser on both sides. First Fast request and
// three subsequent observations are separate; Exact is a standalone request.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const crypto = require('node:crypto');
const { chromium } = require('playwright');
const { serve, installProbe } = require('./measure-worker-lifecycle.cjs');

async function main() {
    const [root, source, measurements, output] = process.argv.slice(2);
    const bytes = fs.readFileSync(source);
    assert.equal(bytes.length, 38305);
    assert.equal(crypto.createHash('sha256').update(bytes).digest('hex'),
        '6ea9f5e1689854aaf19e904909c638ea92eebf329fe140c177f8ac9fbaeedac5');
    const variant = process.argv[7] || 'source';
    const control = process.argv[6]
        ? JSON.parse(fs.readFileSync(process.argv[6])).find(fixture => fixture.Name === `${variant}-Fast`) : null;
    if (process.argv[6]) assert(control?.Session, 'Exported control session required');
    if (control) assert.notEqual(variant, 'source', 'The source must use the original uploaded bytes');
    const sessionBytes = control ? Buffer.from(JSON.stringify(control.Session)) : bytes;
    if (control) assert.equal(control.Session.Cards.reduce((sum, card) => sum + card.Copies, 0), 40);
    const expected = JSON.parse(fs.readFileSync(measurements));
    const { server, url } = await serve(root);
    const browser = await chromium.launch({ headless: true, executablePath: process.env.YGO_BROWSER_EXECUTABLE });
    const evidence = { browser: browser.version(), variant, runs: [] };
    try {
        const page = await browser.newPage();
        await page.addInitScript(installProbe);
        await page.addInitScript(() => {
            const ProbedWorker = Worker;
            window.Worker = class extends ProbedWorker {
                postMessage(data, ...rest) {
                    super.postMessage(data, ...rest);
                    if (this.entry) {
                        Object.assign(lifecycle.requests.at(-1), { fastStage: data.fastStage, remainingWork: data.remainingWork });
                    }
                }
            };
        });
        await page.goto(url);
        await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
        await page.locator('#sessionFileInput').setInputFiles({ name: 'source.json', mimeType: 'application/json', buffer: sessionBytes });
        await page.waitForFunction(() => {
            try {
                return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Cards.length === 22;
            } catch { return false; }
        });
        for (const mode of control ? ['Fast', 'Fast', 'Fast'] : ['Fast', 'Fast', 'Fast', 'Fast', 'Exact']) {
            await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
            await page.locator('#calculationModel').selectOption(mode);
            await page.locator('#calculationModel').press('Escape');
            const index = await page.evaluate(() => lifecycle.runs.length);
            await page.getByRole('button', { name: 'Calculate', exact: true }).click();
            await page.waitForFunction(index => lifecycle.runs[index]?.uiMs !== undefined, index, { timeout: 120000 });
            assert.equal(await page.getByRole('alert').count(), 0);
            const result = await page.evaluate(index => {
                const run = lifecycle.runs[index];
                return { ...run, requests: lifecycle.requests.filter(request => request.sent >= run.started),
                    workers: lifecycle.workers, accuracy: document.querySelector('.result-accuracy')?.textContent,
                    details: document.querySelector('.accuracy-details')?.textContent,
                    displayedTotal: document.querySelector('.probability-total-value')?.textContent,
                    resultText: document.querySelector('.probability-results')?.textContent };
            }, index);
            const samples = expected.find(fixture => fixture.Name === `${variant}-${mode}`).Samples;
            const measured = Array.isArray(samples[0]) ? samples[1][0] : samples[0];
            const completed = result.requests.find(request => request.response?.Result)?.response.Result;
            assert(completed, 'A completed initial worker result is required');
            assert.deepEqual(completed.Contract.Fractions, measured.Result.Contract.Fractions);
            assert.deepEqual(completed.SearchAvailability, measured.Result.SearchAvailability);
            assert.equal(completed.Contract.RequestedMode, mode === 'Fast' ? 0 : 2);
            if (mode === 'Exact') {
                assert.equal(completed.Contract.Accuracy, 0);
                assert.equal(completed.SearchAvailability, undefined);
            }
            const upgrade = result.requests.find(request => request.fastStage === 'upgrade')?.response?.Result;
            if (upgrade?.Contract.Accuracy === 0) {
                const exactSamples = expected.find(fixture => fixture.Name === `${variant}-Exact`).Samples;
                const physical = Array.isArray(exactSamples[0]) ? exactSamples[1][0] : exactSamples[0];
                assert.deepEqual(upgrade.Contract.Fractions, physical.Result.Contract.Fractions);
                assert.equal(upgrade.SearchAvailability, undefined);
            }
            evidence.runs.push({ mode, ...result });
            fs.writeFileSync(output, JSON.stringify(evidence));
            console.log(`${mode}: ${result.uiMs.toFixed(1)} ms; ${result.requests.length} worker request(s)`);
        }
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
