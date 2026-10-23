// node scripts/workspace/session-loading-smoke.cjs <optimized-wwwroot>
// A held Blob read proves pending UI behavior; it is not a load-time benchmark.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const { serve, load, usable, launchOptions } = require('../background-calculation/browser-support.cjs');

async function main() {
    const { server, url } = await serve(process.argv[2]);
    const browser = await chromium.launch(launchOptions()).catch(error => { server.close(); throw error; });
    try {
        const page = await browser.newPage({ viewport: { width: 390, height: 900 }, reducedMotion: 'reduce' });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.goto(url);
        const session = JSON.parse(fs.readFileSync(path.join(process.argv[2], 'sample-data/example_session_state.json')));
        session.Cards[0].Name = 'Session smoke initial';
        await load(page, session);
        await page.evaluate(() => {
            const original = Blob.prototype.arrayBuffer;
            Blob.prototype.arrayBuffer = function () {
                const blob = this;
                return new Promise(resolve => {
                    window.releaseLoad = async () => {
                        Blob.prototype.arrayBuffer = original;
                        resolve(await original.call(blob));
                    };
                });
            };
        });
        const replacement = structuredClone(session);
        replacement.Cards[0].Name = 'Session smoke replacement';
        await page.locator('#sessionFileInput').setInputFiles({ name: 'replacement.json', mimeType: 'application/json',
            buffer: Buffer.from(JSON.stringify(replacement)) });
        await page.waitForFunction(() => typeof releaseLoad === 'function', undefined, { timeout: 15000 });
        await page.waitForFunction(() => {
            const status = document.querySelector('.session-loading-content');
            return status && getComputedStyle(status).opacity === '1';
        });
        assert.equal(await page.locator('.session-loading-status').getAttribute('role'), 'status');
        assert.equal(await page.locator('.session-loading-status').getAttribute('aria-live'), 'polite');
        await usable(page, page.getByRole('button', { name: 'Calculate', exact: true }));
        await page.evaluate(() => releaseLoad());
        await page.waitForFunction(() => !document.querySelector('.session-loading-content'));
        await page.waitForFunction(() => {
            try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Cards[0].Name === 'Session smoke replacement'; }
            catch { return false; }
        });
        assert.equal(await page.locator('.card-editor').count(), session.Cards.length);
        // Immediate small completion is terminal-only, with no minimum spinner
        // duration or frame count. Races/supersession are controlled in bUnit.
        await load(page, { SchemaVersion: 12, CalculationModel: 'Fast', FastExecutionStrategy: 'Progressive', HandSize: 1, Cards: [{ Id: 'immediate', Name: 'Immediate sentinel', Copies: 1, Categories: [] }],
            Categories: [], Combos: [], ComboGroups: [] });
        assert.equal(await page.locator('.card-editor').count(), 1);
        assert.equal(await page.locator('.session-loading-content').count(), 0);
        assert.deepEqual(errors, []);
        console.log('Published session load: controlled pending status, delayed replacement, immediate completion.');
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
