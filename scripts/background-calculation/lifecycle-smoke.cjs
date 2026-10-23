// Opt-in physical idle-owner lifecycle; deliberately waits for the real 60s TTL.
// node scripts/background-calculation/lifecycle-smoke.cjs <optimized-wwwroot>
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const { serve, load, launchOptions } = require('./browser-support.cjs');

async function main() {
    const { server, url } = await serve(process.argv[2]);
    const browser = await chromium.launch(launchOptions()).catch(error => { server.close(); throw error; });
    try {
        const page = await browser.newPage();
        const workers = [];
        page.on('worker', worker => {
            if (!worker.url().includes('calculation-worker.js')) return;
            const entry = { closed: false };
            workers.push(entry);
            worker.on('close', () => { entry.closed = true; });
        });
        await page.goto(url);
        const role = { Name: 'Target', Source: 'User' };
        await load(page, { SchemaVersion: 12, CalculationModel: 'OpeningHand', FastExecutionStrategy: 'Progressive', HandSize: 1, Categories: [role],
            Cards: [{ Id: 'a', Name: 'Lifecycle target', Copies: 1, Categories: [role] }, { Id: 'b', Name: 'Filler', Copies: 1, Categories: [] }],
            Combos: [{ Name: 'Target route', Categories: [{ BaseCategory: role, MinCount: 1, MaxCount: 1 }] }], ComboGroups: [] });
        async function calculate() {
            await page.getByRole('button', { name: 'Calculate', exact: true }).click();
            await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') === 'false'
                && document.querySelector('.probability-total-value'));
            assert.match(await page.locator('.probability-total .probability-with-accuracy').getAttribute('title'), /(?<!\d)1\/2(?!\d)/);
        }
        await calculate(); await calculate();
        assert.equal(workers.length, 1, 'repeat success reuses runtime');
        const idle = workers[0];
        const deadline = Date.now() + 70000;
        while (!idle.closed && Date.now() < deadline) await page.waitForTimeout(250);
        assert.equal(idle.closed, true, 'real idle runtime expires');
        await calculate();
        assert.equal(workers.length, 2, 'expired owner recreates runtime');
        await page.getByRole('link', { name: 'Help', exact: true }).click();
        const disposalDeadline = Date.now() + 10000;
        while (!workers.at(-1).closed && Date.now() < disposalDeadline) await page.waitForTimeout(25);
        assert(workers.every(worker => worker.closed), 'navigation disposes idle owner');
        console.log('Real worker reuse, 60-second idle expiry, recreation and navigation teardown passed.');
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
