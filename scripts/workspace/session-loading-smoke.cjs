// node scripts/workspace/session-loading-smoke.cjs <optimized-wwwroot>
// A held Blob read proves pending UI behavior; it is not a load-time benchmark.
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const { serve, load, usable, launchOptions } = require('../background-calculation/browser-support.cjs');

async function main() {
    const { server, url } = await serve(process.argv[2]);
    const browser = await chromium.launch(launchOptions()).catch(error => { server.close(); throw error; });
    try {
        const page = await browser.newPage({ viewport: { width: 390, height: 900 }, locale: 'en-US', reducedMotion: 'reduce' });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.goto(url);
        const session = {
            SchemaVersion: 14, CalculationModel: 'OpeningHand', FastExecutionStrategy: 'Progressive', HandSize: 1,
            Cards: [{ Id: 'target', Name: 'Session smoke initial', Copies: 1, Categories: [] }],
            Combos: [{ Name: 'Target', Categories: [], Cards: [{ CardId: 'target', MinCount: 1, MaxCount: 1 }] }]
        };
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
        await page.locator('.session-loading-content').waitFor({ state: 'visible' });
        assert.equal(await page.locator('.session-loading-status').getAttribute('role'), 'status');
        assert.equal(await page.locator('.session-loading-status').getAttribute('aria-live'), 'polite');
        assert.equal(await page.evaluate(() => JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Cards[0].Name),
            'Session smoke initial', 'pending browser read keeps the accepted workspace');
        await usable(page, page.getByRole('button', { name: 'Calculate', exact: true }));
        await page.evaluate(() => releaseLoad());
        await page.waitForFunction(() => !document.querySelector('.session-loading-content'));
        await page.waitForFunction(() => {
            try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Cards[0].Name === 'Session smoke replacement'; }
            catch { return false; }
        });
        assert.equal(await page.locator('.card-editor').count(), session.Cards.length);
        // Immediate completion and controlled supersession are covered in bUnit;
        // this smoke adds the actual browser Blob/InputFile/optimized-WASM boundary.
        assert.deepEqual(errors, []);
        console.log('Published session load: real Blob-read handshake, accessible pending status and accepted replacement.');
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
