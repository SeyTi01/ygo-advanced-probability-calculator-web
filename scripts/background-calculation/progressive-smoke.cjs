// Real production worker/UI stages; no engine exports or worker replies are replaced.
// node progressive-smoke.cjs <published-wwwroot> <exported-fixtures.json> <output.json>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
const { serve } = require('./measure-worker-lifecycle.cjs');

function probe() {
    window.progressive = { workers: [], requests: [], runs: [], errors: [] };
    addEventListener('unhandledrejection', event => progressive.errors.push(String(event.reason)));
    const NativeWorker = Worker;
    window.Worker = class extends NativeWorker {
        constructor(...args) {
            super(...args);
            if (!String(args[0]).includes('calculation-worker.js')) return;
            this.entry = { created: performance.now(), index: progressive.workers.length };
            progressive.workers.push(this.entry);
            this.addEventListener('message', ({ data }) => {
                if (data.ready) this.entry.bootstrapMs = performance.now() - this.entry.created;
                if (data.response) {
                    const request = progressive.requests.findLast(r => r.worker === this.entry.index && r.id === data.id);
                    request.response = JSON.parse(data.response);
                    request.received = performance.now();
                }
            });
        }
        postMessage(data, ...rest) {
            if (this.entry) progressive.requests.push({ worker: this.entry.index, id: data.id, stage: data.fastStage,
                sent: performance.now(), remainingWork: data.remainingWork, input: data.json });
            return super.postMessage(data, ...rest);
        }
        terminate() { if (this.entry) this.entry.terminated = performance.now(); return super.terminate(); }
    };
    function snapshot() {
        const selectors = ['.calculation-controls', '.probability-results', '#probabilityResultsHeading',
            '.probability-total', '.probability-group', '.pinned-result', '.probability-results .combo-probability-row'];
        const rects = {};
        for (const selector of selectors) rects[selector] = [...document.querySelectorAll(selector)].map(element => {
            const rect = element.getBoundingClientRect();
            return { top: rect.top + scrollY, left: rect.left, height: rect.height, width: rect.width };
        });
        return { at: performance.now(), rects, scroll: scrollY,
            actionFocused: document.activeElement === document.querySelector('.calculate-action > button'),
            marker: document.querySelector('.probability-result-status')?.textContent.trim() || '',
            values: [...document.querySelectorAll('.probability-results .probability-with-accuracy')].map(element => ({
                label: element.getAttribute('aria-label'), title: element.title, text: element.innerText })),
            mode: document.querySelector('.probability-results .result-accuracy')?.textContent,
            pinDisabled: document.querySelector('.pin-result-action')?.disabled,
            copyDisabled: document.querySelector('.probability-results [aria-label="Copy results"]')?.disabled,
            overflow: document.documentElement.scrollWidth > innerWidth + 1 };
    }
    document.addEventListener('click', event => {
        const button = event.target.closest('.calculate-action > button');
        if (!button || button.getAttribute('aria-busy') === 'true') return;
        const run = { started: performance.now(), frames: 0 };
        progressive.runs.push(run);
        let busy = false;
        function observe() {
            const current = document.querySelector('.calculate-action > button');
            busy ||= current?.getAttribute('aria-busy') === 'true';
            const marker = document.querySelector('.probability-result-status')?.textContent || '';
            if (marker.includes('Preliminary') && !run.initial) run.initial = snapshot();
            if (busy && current?.getAttribute('aria-busy') === 'false' && !run.settled) {
                run.settled = performance.now();
                requestAnimationFrame(() => { run.final = snapshot(); observer.disconnect(); });
            }
        }
        const observer = new MutationObserver(observe);
        observer.observe(document.body, { childList:true, attributes:true, subtree:true });
        function frame() {
            if (run.settled) return;
            if (document.querySelector('.probability-result-status')?.textContent.includes('Preliminary')) run.frames++;
            requestAnimationFrame(frame);
        }
        requestAnimationFrame(frame);
    }, true);
}

function stable(first, final) {
    assert.equal(first.scroll, final.scroll, 'scroll position');
    assert.equal(first.actionFocused, final.actionFocused, 'calculation action focus');
    assert(!first.overflow && !final.overflow, 'horizontal overflow');
    for (const [selector, rects] of Object.entries(first.rects)) {
        assert.equal(final.rects[selector].length, rects.length, selector);
        rects.forEach((rect, index) => assert(Math.abs(rect.top - final.rects[selector][index].top) < 1, `${selector} top moved`));
    }
}

function fractions(actual, expected) {
    assert.deepEqual(actual.Contract.Fractions, expected.Contract.Fractions);
    const intervals = certificate => certificate && [certificate.Total, ...certificate.Combos, ...certificate.Groups]
        .map(row => row && [row.Lower, row.Upper]);
    assert.deepEqual(intervals(actual.SearchAvailability), intervals(expected.SearchAvailability));
}

function displayed(snapshot, expected) {
    const ratio = fraction => `${fraction.Numerator}/${fraction.Denominator}`;
    const activeGroups = expected.GroupProbabilities.map((group,index) => group.ActiveComboCount > 0 ? index : -1).filter(index => index >= 0);
    const rows = expected.SearchAvailability
        ? [expected.SearchAvailability.Total, ...expected.SearchAvailability.Combos, ...activeGroups.map(index => expected.SearchAvailability.Groups[index])]
            .map(interval => `rational endpoints [${ratio(interval.Lower)}, ${ratio(interval.Upper)}]`)
        : [expected.Contract.Fractions.Total, ...expected.Contract.Fractions.Combos, ...activeGroups.map(index => expected.Contract.Fractions.Groups[index])]
            .map(fraction => `Modeled primary fraction: ${ratio(fraction)}.`);
    const titles = snapshot.values.map(value => value.title);
    assert.equal(titles.length,rows.length);
    for (const row of rows) {
        const index = titles.findIndex(title => title.includes(row));
        assert(index >= 0, `displayed rational evidence missing: ${row}`);
        titles.splice(index,1);
    }
}

async function main() {
    const [root, fixturesPath, output] = process.argv.slice(2);
    const fixtures = JSON.parse(fs.readFileSync(fixturesPath));
    const source = fixtures.find(f => f.Name === 'source');
    const control = fixtures.find(f => f.Name === 'control');
    const { server, url } = await serve(root);
    const browser = await chromium.launch({headless:true, executablePath:process.env.YGO_BROWSER_EXECUTABLE})
        .catch(error => { server.close(); throw error; });
    const evidence = { browser:browser.version(), runs:[], errors:[] };
    try {
        const context = await browser.newContext({permissions:['clipboard-read','clipboard-write']});
        const page = await context.newPage();
        page.on('pageerror', error => evidence.errors.push(String(error)));
        await page.addInitScript(probe);
        await page.goto(url);
        let sequence = 0;
        async function load(session, original = false) {
            const value = structuredClone(session);
            if (!original) value.Cards[0].Name = `Progressive smoke ${++sequence}`;
            await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
            await page.locator('#sessionFileInput').setInputFiles({name:'progressive.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(value))});
            await page.waitForFunction(name => {
                try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Cards[0].Name === name; }
                catch { return false; }
            }, value.Cards[0].Name);
        }
        async function mode(value) {
            await page.getByRole('button',{name:'Calculation settings',exact:true}).click();
            await page.locator('#calculationModel').selectOption(value);
            await page.locator('#calculationModel').press('Escape');
        }
        async function start() {
            const index = await page.evaluate(() => progressive.runs.length);
            await page.getByRole('button',{name:'Calculate',exact:true}).click();
            return index;
        }
        async function settle(index, name) {
            await page.waitForFunction(i => progressive.runs[i]?.final, index, {timeout:90000});
            const run = await page.evaluate(i => ({...progressive.runs[i],
                requests:progressive.requests.filter(r => r.sent >= progressive.runs[i].started)}),index);
            assert.equal(await page.getByRole('alert').count(),0);
            assert(!run.final.marker.includes('Preliminary'));
            fs.writeFileSync(output + ".latest.json",JSON.stringify(run,null,2));
            if (run.initial) {
                stable(run.initial,run.final);
                assert(run.initial.pinDisabled && run.initial.copyDisabled);
                assert(run.frames > 0, 'preliminary had a real browser frame');
            }
            assert(!run.final.pinDisabled && !run.final.copyDisabled);
            evidence.runs.push({name,...run});
            console.log(`${name}: first ${run.initial ? (run.initial.at-run.started).toFixed(1) : 'single-stage'} ms; settled ${(run.settled-run.started).toFixed(1)} ms; frames ${run.frames}`);
            fs.writeFileSync(output,JSON.stringify(evidence,null,2));
            return run;
        }
        // Original source: cold request followed by three requests on this owner.
        await load(source.Session,true); await mode('Fast');
        for (let sample=0;sample<4;sample++) {
            const index = await start();
            await page.waitForTimeout(7200);
            assert(await page.locator('.probability-result-status').innerText().then(text => text.includes('Preliminary')));
            const run = await settle(index,`source-${sample}`);
            assert(run.initial, 'initial Fast must be shown while Exact runs');
            fractions(run.requests[0].response.Result,source.Initial);
            displayed(run.initial,source.Initial); displayed(run.final,source.Exact);
            fractions(run.requests[1].response.Result,source.Exact);
            assert(run.settled-run.started > 7000);
            assert(run.frames > 100);
            assert.equal(run.requests[0].worker,run.requests[1].worker);
            assert(run.initial.at < run.settled);
            assert.match(run.initial.mode,/Progressive/);
        }
        assert.equal(await page.evaluate(() => progressive.workers.length),1, 'long upgrades reuse the same worker');
        await mode('FastOnly');
        const fastOnly = await settle(await start(),'source-fast-only');
        assert(!fastOnly.initial); assert.equal(fastOnly.requests.length,1);
        displayed(fastOnly.final,source.Initial); assert.match(fastOnly.final.mode,/Fast only/);
        await page.getByRole('button',{name:'Copy results',exact:true}).click();
        assert.match(await page.evaluate(() => navigator.clipboard.readText()),/Mode: Fast only/);
        evidence.beforeRecovery = await page.evaluate(() => progressive);
        await page.reload();
        await page.getByRole('button',{name:'Restore previous session',exact:true}).click();
        const replaceRecovery = page.getByRole('button',{name:'Replace current work and restore',exact:true});
        if (await replaceRecovery.count()) await replaceRecovery.click();
        await page.waitForFunction(() => document.querySelector('#calculationModel')?.value === 'FastOnly');
        const recovered = await settle(await start(),'recovered-fast-only');
        assert(!recovered.initial); assert.equal(recovered.requests.length,1);
        displayed(recovered.final,source.Initial);
        await page.locator('.pin-result-action').click();
        const pin = await page.locator('.pinned-result').innerText();
        await mode('Exact');
        const sourceExactIndex = await start();
        await page.waitForTimeout(7200);
        evidence.explicitStillBusyAfterAutomaticWindow = await page.locator('.calculate-action > button').getAttribute('aria-busy') === 'true';
        const sourceExact = await settle(sourceExactIndex,'source-explicit-exact');
        assert(!sourceExact.initial); fractions(sourceExact.requests[0].response.Result,source.Exact);
        displayed(sourceExact.final,source.Exact);
        // Actual physical Exact completion and explicit Exact parity.
        await load(control.Session); await mode('Fast');
        const completed = await settle(await start(),'small-automatic-exact');
        fractions(completed.requests[0].response.Result,control.Initial);
        fractions(completed.requests[1].response.Result,control.Exact);
        displayed(completed.final,control.Exact);
        assert.match(completed.final.mode,/Progressive/);
        await page.getByRole('button',{name:'Copy results',exact:true}).click();
        assert.match(await page.evaluate(() => navigator.clipboard.readText()),/completed a physical Exact/);
        await mode('Exact');
        const exact = await settle(await start(),'small-explicit-exact');
        assert(!exact.initial); fractions(exact.requests[0].response.Result,control.Exact);
        assert.deepEqual(exact.final.values,completed.final.values);
        assert.equal(await page.locator('.pinned-result').innerText(),pin);
        // Desktop/mobile, both themes and pin states; long group/combo labels.
        for (const width of [1280,390]) for (const theme of ['light','dark']) for (const pinned of [false,true]) {
            await page.setViewportSize({width,height:900});
            await page.evaluate(theme => document.documentElement.setAttribute('data-bs-theme',theme),theme);
            if (!pinned && await page.locator('.pinned-result').count()) await page.getByRole('button',{name:'Unpin result',exact:true}).click();
            if (pinned && !await page.locator('.pinned-result').count()) await page.locator('.pin-result-action').click();
            const long = structuredClone(source.Session);
            long.Combos[0].Name = 'Long route name with multiple requirements and overlapping categories';
            long.ComboGroups[0].Name = 'A long group label for responsive probability result geometry';
            await load(long); await mode('Fast');
            const index = await start();
            await page.waitForFunction(i => progressive.runs[i]?.initial,index,{timeout:30000});
            if (pinned) await page.screenshot({path:output.replace(/\.json$/,`.${width}-${theme}.png`),fullPage:true});
            const run = await settle(index,`geometry-${width}-${theme}-pin-${pinned}`);
            assert(run.initial); stable(run.initial,run.final);
            await load(control.Session); await mode('Fast');
            const upgraded = await settle(await start(),`exact-geometry-${width}-${theme}-pin-${pinned}`);
            fractions(upgraded.requests.at(-1).response.Result,control.Exact);
            if (upgraded.initial) stable(upgraded.initial,upgraded.final);
        }
        await page.setViewportSize({width:780,height:900});
        await page.evaluate(() => document.documentElement.style.zoom = '2');
        await load(source.Session); await mode('Fast');
        const zoomed = await settle(await start(),'zoom-200-percent');
        assert(zoomed.initial); stable(zoomed.initial,zoomed.final);
        await page.evaluate(() => document.documentElement.style.zoom = '');
        // Cancellation after valid progress, immediately followed by a new request.
        await load(source.Session); await mode('Fast');
        const cancelled = await start();
        await page.waitForFunction(i => progressive.runs[i]?.initial,cancelled,{timeout:30000});
        await page.getByRole('button',{name:'Cancel Exact upgrade',exact:true}).click();
        const retained = await settle(cancelled,'cancel-after-progress');
        assert.deepEqual(retained.initial.values,retained.final.values);
        const restarted = await settle(await start(),'immediate-restart');
        fractions(restarted.requests[0].response.Result,source.Initial);
        // Editing makes the accepted preliminary stale; a load clears current results.
        const edited = await start();
        await page.waitForFunction(i => progressive.runs[i]?.initial,edited,{timeout:30000});
        await page.locator('#handSize').fill('6'); await page.locator('#handSize').press('Tab');
        await page.waitForFunction(() => document.querySelector('.probability-result-status')?.textContent.includes('Stale'));
        assert(!await page.getByRole('button',{name:'Cancel Exact upgrade',exact:true}).count());
        await load(control.Session);
        assert.equal(await page.locator('.probability-results').count(),0);
        await mode('OpeningHand');
        const opening = await settle(await start(),'opening-hand'); assert(!opening.initial);
        evidence.lifecycle = await page.evaluate(() => progressive);
        assert.deepEqual(evidence.errors,[]); assert.deepEqual(evidence.lifecycle.errors,[]);
        fs.writeFileSync(output,JSON.stringify(evidence,null,2));
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
}
main().catch(error => { console.error(error); process.exitCode=1; });
