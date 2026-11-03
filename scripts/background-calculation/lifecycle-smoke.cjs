// Opt-in physical idle-owner lifecycle; deliberately waits for the real 60s TTL.
// node scripts/background-calculation/lifecycle-smoke.cjs <optimized-wwwroot>
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const { serve, load, launchOptions } = require('./browser-support.cjs');

async function waitForClose(worker, milliseconds, message) {
    let timeout;
    try {
        await Promise.race([worker.closed, new Promise((resolve, reject) => {
            timeout = setTimeout(() => reject(new Error(message)), milliseconds);
        })]);
    } finally { clearTimeout(timeout); }
}

async function main() {
    const { server, url } = await serve(process.argv[2]);
    const browser = await chromium.launch(launchOptions()).catch(error => { server.close(); throw error; });
    try {
        const page = await browser.newPage({ locale: 'en-US' });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        const workers = [];
        page.on('worker', worker => {
            if (!worker.url().includes('calculation-worker.js')) return;
            const entry = { isClosed: false, closed: new Promise(resolve => {
                worker.once('close', () => { entry.isClosed = true; resolve(); });
            }) };
            workers.push(entry);
        });
        await page.goto(url);
        const role = { Name: 'Target', Source: 'User' };
        await load(page, { SchemaVersion: 14, CalculationModel: 'OpeningHand', FastExecutionStrategy: 'Progressive', HandSize: 1, Categories: [role],
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
        assert.equal(idle.isClosed, false, 'successful requests retain their physical runtime');
        await waitForClose(idle, 70000, 'real idle runtime did not expire');
        await calculate();
        assert.equal(workers.length, 2, 'expired owner recreates runtime');
        const beforeHelp = workers.at(-1);
        // Help keeps editor DOM mounted but deactivates the calculator and
        // disposes its owner. Returning must resolve a fresh usable owner.
        await page.getByRole('link', { name: 'Help', exact: true }).click();
        await waitForClose(beforeHelp, 10000, 'Help did not close the calculator worker');
        assert(workers.every(worker => worker.isClosed), 'Help releases the physical idle owner');
        await page.getByRole('link', { name: 'Back', exact: true }).click();
        await calculate();
        assert.equal(workers.length, 3, 'retained calculator acquires a fresh runtime on return');
        assert.deepEqual(errors, []);
        console.log('Real worker reuse, 60-second idle expiry, recreation, Help teardown and successful return passed.');
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
