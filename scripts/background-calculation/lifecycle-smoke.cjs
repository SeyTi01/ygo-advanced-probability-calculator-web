// Published UI/service lifecycle checks, including the real 60-second idle expiry.
// node lifecycle-smoke.cjs <published-wwwroot> [output.json]
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { firefox, chromium } = require('playwright');
const { fixtures, installProbe, load, run, serve } = require('./measure-worker-lifecycle.cjs');
async function main() {
    const { server, url } = await serve(process.argv[2]);
    const engine = process.env.YGO_BROWSER_ENGINE || 'firefox';
    const browser = await ({ firefox, chromium }[engine]).launch({ headless: true,
        ...(engine === 'chromium' && process.env.YGO_BROWSER_CHANNEL ? { channel: process.env.YGO_BROWSER_CHANNEL } : {})
    }).catch(error => { server.close(); throw error; });
    const deadline = setTimeout(() => browser.close(), 180000);
    const reuse = process.env.YGO_EXPECT_REUSE !== '0';
    const evidence = { engine, version: browser.version(), reuse };
    try {
        const page = await browser.newPage();
        const contexts = [], errors = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('worker', worker => {
            if (!worker.url().includes('calculation-worker.js')) return;
            const entry = { worker, closed: false };
            contexts.push(entry);
            worker.on('close', () => { entry.closed = true; });
        });
        await page.addInitScript(installProbe);
        await page.goto(url);
        await load(page, fixtures[0].session);
        evidence.first = await run(page, 5);
        evidence.repeat = await run(page, 6);
        assert.equal(contexts.length, reuse ? 1 : 2);
        if (reuse) assert.equal(contexts[0].closed, false);
        assert.equal(evidence.repeat.workersCreated, reuse ? 1 : 2);
        evidence.runtimeMemory = !reuse ? null : await contexts[0].worker.evaluate(() => {
            const runtime = globalThis.getDotnetRuntime?.(0);
            return { wasmHeapBytes: runtime?.Module?.HEAPU8?.buffer?.byteLength ?? null };
        });
        // Same UI owner, changed session; ensure the file replacement completed.
        const roles = Array.from({ length: 15 }, (_, i) => ({ Name: `R${i}`, Source: 'User' }));
        const heavy = { SchemaVersion: 5, HandSize: 15, Categories: roles,
            Cards: roles.map((c, i) => ({ Id: `r${i}`, Name: null, Copies: 2, Categories: [c] }))
                .concat([{ Id: 'blank', Name: null, Copies: 30, Categories: [] }]),
            Combos: roles.map(c => ({ Name: c.Name, Categories: [{ BaseCategory: c, MinCount: 1, MaxCount: 1 }] })), ComboGroups: [] };
        await load(page, heavy);
        await page.waitForFunction(() => document.querySelector('#handSize')?.value === '15');
        const previous = await page.evaluate(() => lifecycle.requests.length);
        await page.getByRole('button', { name: 'Calculate', exact: true }).click();
        await page.waitForFunction(n => lifecycle.requests.length > n, previous);
        await page.waitForTimeout(100);
        const cpuContext = contexts.at(-1);
        const cancelled = Date.now();
        await page.getByRole('button', { name: 'Cancel calculation', exact: true }).click();
        assert.equal(await page.getByRole('button', { name: 'Calculate', exact: true }).count(), 1);
        evidence.cancelAutomationMs = Date.now() - cancelled;
        const cancelTrace = await page.evaluate(() => ({
            uiMs: lifecycle.cancellations.at(-1).uiMs,
            terminateMs: lifecycle.workers.at(-1).terminated - lifecycle.cancellations.at(-1).started
        }));
        evidence.cancelUiMs = cancelTrace.uiMs;
        evidence.cancelTerminateMs = cancelTrace.terminateMs;
        for (let i = 0; i < 200 && !cpuContext.closed; i++) await page.waitForTimeout(25);
        assert.equal(cpuContext.closed, true, 'cancel must destroy the reused CPU context');
        evidence.cancelCloseMs = Date.now() - cancelled;
        assert.equal(await page.getByRole('alert').count(), 0);
        await load(page, fixtures[0].session);
        await page.waitForFunction(() => document.querySelector('#handSize')?.value === '5');
        evidence.restart = await run(page, 5);
        assert.equal(contexts.length, reuse ? 2 : 4);
        assert.deepEqual(evidence.restart.response, evidence.first.response);
        // Actual expiry, without shortening production timers or replacing Worker.
        const idleContext = contexts.at(-1);
        const idleStarted = Date.now();
        for (let i = 0; i < 130 && !idleContext.closed; i++) await page.waitForTimeout(500);
        assert.equal(idleContext.closed, true, 'idle runtime must close after sixty seconds');
        evidence.idleCloseMs = Date.now() - idleStarted;
        evidence.afterExpiry = await run(page, 5);
        assert.equal(contexts.length, reuse ? 3 : 5);
        assert.deepEqual(evidence.afterExpiry.response, evidence.first.response);
        // Navigation tears down an IDLE owner, not just a running request.
        await page.getByRole('link', { name: 'Help', exact: true }).click();
        for (let i = 0; i < 200 && !contexts.at(-1).closed; i++) await page.waitForTimeout(25);
        assert.ok(contexts.every(context => context.closed));
        evidence.closedContexts = contexts.length;
        assert.deepEqual(errors, []);
        if (process.argv[3]) fs.writeFileSync(process.argv[3], JSON.stringify(evidence, null, 2));
        console.log(JSON.stringify({ engine, version: browser.version(),
            firstMs: evidence.first.uiMs, repeatMs: evidence.repeat.uiMs, restartMs: evidence.restart.uiMs,
            afterExpiryMs: evidence.afterExpiry.uiMs, cancelUiMs: evidence.cancelUiMs,
            cancelCloseMs: evidence.cancelCloseMs, cancelTerminateMs: evidence.cancelTerminateMs, idleCloseMs: evidence.idleCloseMs,
            memory: evidence.runtimeMemory, closedContexts: evidence.closedContexts }));
    } finally { clearTimeout(deadline); await browser.close(); server.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
