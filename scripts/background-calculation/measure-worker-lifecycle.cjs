// Opt-in production UI/service measurement. No timing thresholds; freeze a baseline
// output and pass it as the final argument for complete response parity on a candidate.
// node measure-worker-lifecycle.cjs <published-wwwroot> <output.json> [trials=3] [baseline.json]
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const { chromium, firefox } = require('playwright');
const [base, output, trialArgument = '3', referencePath] = process.argv.slice(2);
const trials = Number(trialArgument);

const category = Name => ({ Name, Source: 'User' });
const [starter, extender, dark, material] = ['Starter', 'Extender', 'DARK material', 'Return material'].map(category);
const card = (Id, Copies, Categories = [], extra = {}) => ({ Id, Name: null, Copies, Categories, ...extra });
const requirement = (BaseCategory, MinCount) => ({ BaseCategory, MinCount, MaxCount: 0, MaximumMode: 'HandSize' });
const combos = [
    { Name: 'Starter + extender', GroupId: 'g', Categories: [requirement(starter, 1), requirement(extender, 1)] },
    { Name: 'Two starters', GroupId: 'g', Categories: [requirement(starter, 2)] }
];
const session = Cards => ({ SchemaVersion: 5, Cards, Categories: [starter, extender, dark, material],
    Combos: combos, ComboGroups: [{ Id: 'g', Name: 'Routes' }], HandSize: 5 });
const engineCards = [card('a', 6, [starter, dark, material]), card('b', 6, [extender, dark, material]),
    card('c', 3, [dark, material]), card('d', 3, [starter]), card('e', 3, [extender])];
const allure = card('allure', 2, [], { DrawCount: 2, DrawOncePerTurn: false, PostDrawDiscardCount: 1,
    PostDrawSelector: { Kind: 'Category', Categories: ['user:DARK material'] } });
const drawReturn = card('return', 2, [], { DrawCount: 2, DrawOncePerTurn: true, PreDrawRemovalCount: 1,
    PreDrawSelector: { Kind: 'Category', Categories: ['user:Return material'] } });
const fixtures = [
    { name: 'opening-40', session: session([...engineCards, card('blank', 19)]) },
    { name: 'allure-40', session: session([...engineCards, allure, card('blank', 17)]) },
    { name: 'return-allure-40', session: session([...engineCards, drawReturn, allure, card('blank', 15)]) }
];

function installProbe() {
    window.lifecycle = { workers: [], requests: [], runs: [], cancellations: [] };
    const NativeWorker = Worker;
    window.Worker = class extends NativeWorker {
        constructor(...args) {
            super(...args);
            if (!String(args[0]).includes('calculation-worker.js')) return;
            const entry = { created: performance.now(), index: lifecycle.workers.length };
            this.entry = entry;
            lifecycle.workers.push(entry);
            this.addEventListener('message', ({ data }) => {
                if (data.ready) entry.bootstrapMs = performance.now() - entry.created;
                if (typeof data.response === 'string') {
                    const request = lifecycle.requests.findLast(r => r.worker === entry.index && r.id === data.id);
                    request.response = JSON.parse(data.response);
                    request.responseMs = performance.now() - request.sent;
                }
            });
        }
        postMessage(data, ...rest) {
            if (this.entry) lifecycle.requests.push({ worker: this.entry.index, id: data.id,
                sent: performance.now(), input: data.json });
            return super.postMessage(data, ...rest);
        }
        terminate() {
            if (this.entry) this.entry.terminated = performance.now();
            return super.terminate();
        }
    };
    document.addEventListener('click', event => {
        const button = event.target.closest('.calculate-action > button');
        if (!button) return;
        const run = { started: performance.now() };
        if (button.getAttribute('aria-label') === 'Cancel calculation') lifecycle.cancellations.push(run);
        else lifecycle.runs.push(run);
        let busySeen = button.getAttribute('aria-busy') === 'true';
        const observer = new MutationObserver(() => {
            const button = document.querySelector('.calculate-action > button');
            if (button?.getAttribute('aria-busy') === 'true') busySeen = true;
            else if (busySeen && button) {
                run.uiMs = performance.now() - run.started;
                observer.disconnect();
            }
        });
        observer.observe(document.body, { attributes: true, childList: true, subtree: true });
    }, true);
}

async function load(page, fixture) {
    await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
    await page.locator('#sessionFileInput').setInputFiles({ name: 'lifecycle.json', mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify(fixture)) });
    await page.waitForFunction(() => { try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Cards.length > 0; } catch { return false; } });
}

async function run(page, hand) {
    await page.locator('#handSize').fill(String(hand));
    await page.locator('#handSize').press('Tab');
    const index = await page.evaluate(() => lifecycle.runs.length);
    await page.getByRole('button', { name: 'Calculate', exact: true }).click();
    await page.waitForFunction(index => lifecycle.runs[index]?.uiMs !== undefined, index, { timeout: 60000 });
    await page.locator('.probability-total-value').waitFor();
    assert.equal(await page.getByRole('alert').count(), 0);
    return page.evaluate(index => ({ ...lifecycle.runs[index], ...lifecycle.requests[index],
        bootstrapMs: lifecycle.workers[lifecycle.requests[index].worker].bootstrapMs,
        workersCreated: lifecycle.workers.length,
        retained: lifecycle.workers.filter(w => !w.terminated).length }), index);
}

async function serve(rootArgument) {
    const root = path.resolve(rootArgument);
    const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.html': 'text/html', '.css': 'text/css' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            let file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname));
            if (!file.startsWith(root + path.sep)) return response.writeHead(403).end();
            if (!path.extname(file) && !fs.existsSync(file)) file = path.join(root, 'index.html');
            response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
            response.end(await fs.promises.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    return { server, url: `http://127.0.0.1:${server.address().port}` };
}

async function main() {
    assert(base && output && Number.isInteger(trials) && trials > 0 && trials <= 30);
    const { server, url } = await serve(base);
    const engine = process.env.YGO_BROWSER_ENGINE || 'firefox';
    const browser = await ({ chromium, firefox }[engine]).launch({ headless: true }).catch(error => { server.close(); throw error; });
    const deadline = setTimeout(() => browser.close(), 300000);
    const evidence = { engine, version: browser.version(), trials, cases: [] };
    const reference = referencePath ? JSON.parse(fs.readFileSync(referencePath, 'utf8')) : null;
    try {
        for (const fixture of fixtures) {
            for (let trial = 0; trial < trials; trial++) {
                const context = await browser.newContext();
                const page = await context.newPage();
                await page.addInitScript(installProbe);
                await page.goto(url);
                await load(page, fixture.session);
                const runs = [];
                for (const hand of [5, 6, 5, 5]) runs.push(await run(page, hand));
                const sent = JSON.parse(runs[0].input);
                assert.equal(sent.Cards.filter(card => card.DrawCount).length,
                    fixture.session.Cards.filter(card => card.DrawCount).length, 'Imported effects must survive migration');
                assert.deepEqual(runs[0].response, runs[2].response);
                assert.deepEqual(runs[0].response, runs[3].response);
                if (reference) {
                    const previous = reference.cases.find(c => c.name === fixture.name && c.trial === trial);
                    for (let i = 0; i < runs.length; i++) {
                        assert.equal(runs[i].input, previous.runs[i].input);
                        assert.deepEqual(runs[i].response, previous.runs[i].response);
                    }
                }
                evidence.cases.push({ name: fixture.name, trial, runs });
                console.log(JSON.stringify({ name: fixture.name, trial, runs: runs.map(({ uiMs, responseMs, bootstrapMs, workersCreated, retained }) =>
                    ({ uiMs, responseMs, bootstrapMs, workersCreated, retained })) }));
                await context.close();
            }
        }
        fs.writeFileSync(output, JSON.stringify(evidence, null, 2));
    } finally { clearTimeout(deadline); await browser.close(); server.close(); }
}
if (require.main === module) main().catch(error => { console.error(error); process.exitCode = 1; });
module.exports = { fixtures, installProbe, load, run, serve };
