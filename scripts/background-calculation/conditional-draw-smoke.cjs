// ExportConditionalCases supplies the genuine 40/5 example and native references.
// NODE_PATH=<playwright installation> node conditional-draw-smoke.cjs <published-wwwroot> <conditional-worker-cases.json> <output.json>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium, firefox } = require('playwright');
const { installProbe, load, run, serve } = require('./measure-worker-lifecycle.cjs');

async function main() {
    const [root, casesPath, output] = process.argv.slice(2);
    assert(root && casesPath && output);
    const fixtures = JSON.parse(fs.readFileSync(casesPath, 'utf8')).filter(fixture => fixture.Name.endsWith('-configured'));
    assert.equal(fixtures.length, 2);
    const { server, url } = await serve(root);
    const engine = process.env.YGO_BROWSER_ENGINE || 'chromium';
    const browser = await ({ chromium, firefox }[engine]).launch({ headless: true }).catch(error => { server.close(); throw error; });
    const evidence = [];
    const deadline = setTimeout(() => browser.close(), 120000);
    try {
        for (const fixture of fixtures) {
            assert.equal(fixture.Outcome, 'Success');
            const context = await browser.newContext();
            const page = await context.newPage();
            const workers = [];
            page.on('worker', worker => {
                if (!worker.url().includes('calculation-worker.js')) return;
                const entry = { worker, closed: false };
                workers.push(entry);
                worker.on('close', () => { entry.closed = true; });
            });
            await page.addInitScript(installProbe);
            await page.goto(url);
            const input = fixture.Input;
            assert.equal(input.HandSize, 5);
            assert.equal(input.Cards.reduce((sum, card) => sum + card.Copies, 0), 40);
            assert.equal(input.Combos.length, 8);
            assert.equal(input.Groups.length, 2);
            assert(fixture.Session, 'Export the session using the native session codec.');
            await load(page, fixture.Session);
            const first = await run(page, 5);
            const repeat = await run(page, 5);
            assert.deepEqual(first.response.Result, fixture.ReferenceResults);
            assert.deepEqual(repeat.response, first.response);
            assert.equal(workers.length, 1, 'successful requests reuse the same worker');
            const heapBytes = await workers[0].worker.evaluate(() => globalThis.getDotnetRuntime(0).Module.HEAPU8.buffer.byteLength);
            const previous = await page.evaluate(() => lifecycle.requests.length);
            await page.getByRole('button', { name: 'Calculate', exact: true }).click();
            await page.waitForFunction(count => lifecycle.requests.length > count, previous);
            const cancelled = Date.now();
            await page.getByRole('button', { name: 'Cancel calculation', exact: true }).click();
            await page.getByRole('button', { name: 'Calculate', exact: true }).waitFor();
            for (let attempt = 0; attempt < 200 && !workers[0].closed; attempt++) await page.waitForTimeout(25);
            assert.equal(workers[0].closed, true, 'cancellation destroys the running context');
            const cancelCloseMs = Date.now() - cancelled;
            const restart = await run(page, 5);
            assert.equal(workers.length, 2);
            assert.deepEqual(restart.response, first.response, 'cancelled request cannot replace restarted result');
            evidence.push({ name: fixture.Name, engine, version: browser.version(), heapBytes, cancelCloseMs,
                firstMs: first.responseMs, repeatMs: repeat.responseMs, restartMs: restart.responseMs,
                startupMs: first.bootstrapMs, workersCreated: workers.length });
            await context.close();
        }
        fs.writeFileSync(output, JSON.stringify(evidence, null, 2));
        console.log(JSON.stringify(evidence));
    } finally {
        clearTimeout(deadline);
        await browser.close();
        server.close();
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
