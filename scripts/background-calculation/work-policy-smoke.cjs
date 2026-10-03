// Opt-in: node work-policy-smoke.cjs <origin> chromium|firefox [CPU slowdown] [all|wire|ui]
// The controller closes the entire isolated browser after three minutes.
const assert = require('node:assert/strict');
const { chromium, firefox } = require('playwright');
const base = process.argv[2];
const engine = process.argv[3] || 'chromium';
const slowdown = Number(process.argv[4] || 1);
const phase = process.argv[5] || 'all';
assert.ok(['all', 'wire', 'ui', 'comparison'].includes(phase), 'unknown smoke phase');
function fixture(count, hand = 5) {
 const categories = Array.from({ length: count }, (_, i) => ({ Name: `Role${i}`, Source: 'User' }));
 return { SchemaVersion: 2, Categories: categories, HandSize: hand,
  Cards: categories.map((c, i) => ({ Id: `c${i}`, Name: null, Copies: 2, Categories: [c] }))
   .concat([{ Id: 'blank', Name: null, Copies: 60 - 2 * count, Categories: [] }]),
  Combos: categories.map((c, i) => ({ Name: `Exactly one ${c.Name}`, GroupId: `g${i % 2}`, Categories: [{ BaseCategory: c, MinCount: 1, MaxCount: 1 }] })),
  ComboGroups: [{ Id: 'g0', Name: 'Even' }, { Id: 'g1', Name: 'Odd' }] };
}
function wire(session, units) {
 return JSON.stringify({ Cards: session.Cards.map(c => ({ ...c, ExternalCardId: null, ManualMetadataCategoryKeys: [],
  Categories: c.Categories.map(c => ({ ...c, Source: 0, MetadataKey: null })) })),
  Combos: session.Combos.map(c => ({ ...c, Cards: [], Categories: c.Categories.map(r => ({ ...r, MaximumMode: 0,
   BaseCategory: { ...r.BaseCategory, Source: 0, MetadataKey: null } })) })), Groups: session.ComboGroups, HandSize: session.HandSize,
  ...(units === undefined ? {} : { WorkUnits: units }) });
}
function choose(n, k) { let value = 1; for (let i = 1; i <= k; i++) value = value * (n - i + 1) / i; return value; }
function probability(count, hand = 5) {
 let failed = 0;
 for (let pairs = 0; pairs <= Math.min(count, Math.floor(hand / 2)); pairs++) {
  const blanks = hand - 2 * pairs;
  if (blanks <= 60 - 2 * count) failed += choose(count, pairs) * choose(60 - 2 * count, blanks);
 }
 return 1 - failed / choose(60, hand);
}
(async () => {
 const options = { headless: true, timeout: 20000 };
 if (engine === 'chromium' && process.env.PLAYWRIGHT_CHROMIUM_CHANNEL) options.channel = process.env.PLAYWRIGHT_CHROMIUM_CHANNEL;
 if (engine === 'firefox' && process.env.PLAYWRIGHT_FIREFOX_EXECUTABLE_PATH) options.executablePath = process.env.PLAYWRIGHT_FIREFOX_EXECUTABLE_PATH;
 const browser = await ({ chromium, firefox }[engine]).launch(options);
 const deadline = setTimeout(() => {
  console.error(`Work-policy ${phase} smoke reached its three-minute browser deadline.`);
  browser.close();
 }, 180000);
 try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  page.setDefaultTimeout(45000);
  let live = 0, peak = 0, closed = 0;
  page.on('worker', worker => { live++; peak = Math.max(peak, live); worker.on('close', () => { live--; closed++; }); });
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await page.addInitScript(() => {
   window.workProbe = { beats: [], inputs: [], terminations: [], clicks: [], creations: [] };
   document.addEventListener('click', event => {
    const button = event.target.closest('.calculate-action > button');
    if (button) workProbe.clicks.push({ time: performance.now(), label: button.getAttribute('aria-label'), busy: button.getAttribute('aria-busy'), detail: event.detail });
   }, true);
   setInterval(() => workProbe.beats.push(performance.now()), 20);
   const Original = Worker;
   window.Worker = class extends Original {
    constructor(...args) { super(...args); workProbe.creations.push(performance.now()); }
    postMessage(message) { workProbe.inputs.push(message.json); super.postMessage(message); }
    terminate() { workProbe.terminations.push(performance.now()); super.terminate(); }
   };
  });
  await page.goto(base);
  const calculate = page.getByRole('button', { name: 'Calculate', exact: true });
  await calculate.waitFor();
  const cdp = engine === 'chromium' ? await page.context().newCDPSession(page) : null;
  if (slowdown !== 1) { assert.ok(cdp, 'CPU slowdown requires Chromium'); await cdp.send('Emulation.setCPUThrottlingRate', { rate: slowdown }); }
  async function request(json) {
   return page.evaluate(async json => {
    const { createJob } = await import('./js/background-calculation.js');
    const job = createJob(); const started = performance.now();
    try { return { response: JSON.parse(await job.run(json)), ms: performance.now() - started }; }
    finally { job.dispose(); }
   }, json);
  }
  let wireEvidence;
  if (phase !== 'ui' && phase !== 'comparison') {
  const expensive = fixture(21);
  const old = await request(wire(expensive, 10_000_000));
  assert.equal(old.response.LimitReason, 1);
  const missing = await request(wire(expensive));
  assert.equal(missing.response.LimitReason, 1, 'omitted wire policy retains bounded synchronous default');
  const selected = await request(wire(expensive, 50_000_000));
  const ample = await request(wire(expensive, 100_000_000));
  assert.deepEqual(selected.response.Result, ample.response.Result);
  assert.ok(Math.abs(selected.response.Result.TotalProbability - probability(21)) < 1e-12);
  for (let i = 0; i < 2; i++) {
   const count = expensive.Combos.filter(c => c.GroupId === `g${i}`).length;
   assert.ok(Math.abs(selected.response.Result.GroupProbabilities[i].Probability - probability(count)) < 1e-12);
  }
  for (const combo of selected.response.Result.ComboProbabilities)
   assert.ok(Math.abs(combo.Probability - 2 * choose(58, 4) / choose(60, 5)) < 1e-12);
  const storage = await request(wire(fixture(22), 50_000_000));
  assert.equal(storage.response.LimitReason, 2);
  console.log(JSON.stringify({ engine, slowdown, phase: 'wire', oldMs: old.ms, selectedMs: selected.ms, ampleMs: ample.ms, storageMs: storage.ms }));
  for (const units of [1, 0, -1, 'unlimited']) {
   const failure = await request(wire(fixture(1), units));
   assert.equal(failure.response.Result, null);
   assert.equal(failure.response.LimitReason, units === 1 ? 1 : null);
   if (units !== 1) assert.ok(failure.response.FailureKind);
  }
  const malformedInput = await request('not JSON');
  assert.equal(malformedInput.response.LimitReason, null);
  assert.equal(malformedInput.response.FailureKind, 2);
  wireEvidence = { old: { ms: old.ms, reason: old.response.LimitReason }, selected: { ms: selected.ms, probability: selected.response.Result.TotalProbability },
   ampleMs: ample.ms, storage: { ms: storage.ms, reason: storage.response.LimitReason } };
  if (phase === 'wire') {
   console.log(JSON.stringify({ engine, browser: browser.version(), slowdown, phase, ...wireEvidence, errors }));
   assert.deepEqual(errors, []);
   return;
  }
  }
  let loadSequence = 0;
  async function load(session) {
   // A unique saved marker proves this replacement finished even when its
   // model otherwise equals the prior session. An idle button cannot prove it.
   const marker = `${session.Combos[0].Name} / smoke load ${++loadSequence}`;
   const owned = { ...session, Combos: session.Combos.map((combo, i) => i === 0 ? { ...combo, Name: marker } : combo) };
   await page.locator('#sessionFileInput').setInputFiles({ name: 'work-policy.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(owned)) });
   await page.waitForFunction(marker => {
    try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Combos[0].Name === marker; }
    catch { return false; }
   }, marker);
  }
  async function start() {
   await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
   const before = await page.evaluate(() => workProbe.inputs.length);
   await calculate.click();
   try { await page.waitForFunction(before => workProbe.inputs.length > before, before); }
   catch (error) {
    console.error(JSON.stringify({ engine, slowdown, live, peak, before,
     action: await page.locator('.calculate-action').innerText(), alerts: await page.getByRole('alert').allTextContents(),
     trace: await page.evaluate(() => ({ clicks: workProbe.clicks.slice(-12), creations: workProbe.creations.length, sent: workProbe.inputs.length, terminated: workProbe.terminations.length })) }));
    throw error;
   }
  }
  async function settle() {
   const started = Date.now();
   while (live && Date.now() - started < 5000) await page.waitForTimeout(25);
   assert.equal(live, 0, 'worker execution contexts must all shut down');
   return Date.now() - started;
  }
  async function comparisons() {
  // Verify the policy and accepted comparison/export context together through
  // the ordinary UI, using the same request that exceeded the old wire budget.
  await load(fixture(21)); await start();
  assert.equal(JSON.parse(await page.evaluate(() => workProbe.inputs.at(-1))).WorkUnits, 50_000_000);
  await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
  await page.locator('.probability-total-value').waitFor();
  assert.equal(await page.locator('.combo-probability-item').count(), 21);
  const acceptedOdds = await page.locator('.probability-total-value').innerText();
  if (engine === 'chromium') await page.context().grantPermissions(['clipboard-read', 'clipboard-write'], { origin: new URL(base).origin });
  await page.evaluate(() => {
   const write = navigator.clipboard.writeText.bind(navigator.clipboard);
   navigator.clipboard.writeText = async text => {
    if (window.denyPolicyClipboard) throw new DOMException('Smoke clipboard denial', 'NotAllowedError');
    await write(text); window.policyCopiedText = text;
   };
  });
  const comparisonEvidence = [];
  for (const pinned of [true, false]) {
   if (pinned) await page.locator('.pin-result-action').click();
   else await page.locator('.pinned-result button').click();
   for (const [width, theme] of [[1440, 'light'], [1440, 'dark'], [390, 'light'], [390, 'dark']]) {
   await page.setViewportSize({ width, height: 900 });
   await page.getByLabel('Color theme preference').selectOption(theme);
    await page.locator('#handSize').fill('5'); await page.locator('#handSize').press('Tab');
    await page.locator('#handSize').fill('6'); await page.locator('#handSize').press('Tab');
    assert.equal(await page.getByRole('button', { name: 'Copy results', exact: true }).isDisabled(), false);
    assert.equal(await page.locator('.pin-result-action').isDisabled(), true);
    await page.getByRole('button', { name: 'Copy results', exact: true }).click();
    await page.locator('.probability-result-copy-success-icon').waitFor();
    const copied = await page.evaluate(() => policyCopiedText);
    assert.ok(copied.startsWith('Previous result — current inputs have changed.\nProbability results\nHand size: 5\n'));
    assert.ok(copied.includes(acceptedOdds) && copied.includes('Even') && copied.includes('Odd'));
    assert.ok(!copied.includes('Pinned') && !copied.includes('pp'));
    if (engine === 'chromium') assert.equal((await page.evaluate(() => navigator.clipboard.readText())).replace(/\r\n/g, '\n'), copied);
    await page.evaluate(() => { window.denyPolicyClipboard = true; });
    await page.getByRole('button', { name: 'Copy results', exact: true }).click();
    const fallback = page.locator('textarea.probability-result-copy-text'); await fallback.waitFor();
    assert.equal(await fallback.inputValue(), copied);
    await page.getByRole('button', { name: 'Close copy fallback', exact: true }).click();
    await page.evaluate(() => { window.denyPolicyClipboard = false; });
    const summary = pinned ? await page.locator('.probability-results > p.small').innerText() : null;
    if (pinned) {
     assert.equal(summary, 'Hand size 5 · 60 active copies · 22 active cards');
     assert.equal(await page.locator('.pinned-result > p.small').innerText(), summary);
    }
    comparisonEvidence.push({ width, theme, pinned, copiedCharacters: copied.length, summary });
   }
  }
  // Fresh current/pin deltas remain inline, with captured context on each side.
  const deltaEvidence = [];
  for (const [width, theme] of [[1440, 'light'], [1440, 'dark'], [390, 'light'], [390, 'dark']]) {
  await page.setViewportSize({ width, height: 900 });
  await page.getByLabel('Color theme preference').selectOption(theme);
  await load(fixture(1)); await start();
  await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
  await page.locator('.pin-result-action').click();
  await page.locator('#handSize').fill('6'); await page.locator('#handSize').press('Tab');
  const measureAction = () => page.locator('.calculate-action').evaluate(e => { const r = e.getBoundingClientRect(); return { top: r.top + scrollY, height: r.height, width: r.width }; });
  const idle = await measureAction();
  await start(); const busy = await measureAction();
  for (const dimension of ['top', 'height', 'width'])
   assert.ok(Math.abs(busy[dimension] - idle[dimension]) <= .1, `Calculate/Cancel ${dimension} stays fixed with a pin (allowing browser subpixel precision)`);
  assert.equal(await page.getByRole('button', { name: 'Copy results', exact: true }).isDisabled(), true);
  await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
  assert.match(await page.locator('.probability-total .result-difference').innerText(), /^\+\d+(?:[.,]\d+)?\s*%$/);
  const inline = await page.locator('.probability-total .result-difference').evaluate(e => {
   const odds = e.previousElementSibling.getBoundingClientRect(), delta = e.getBoundingClientRect();
   return { parentDisplay: getComputedStyle(e.parentElement).display, color: getComputedStyle(e).color, className: e.className,
    beside: delta.left >= odds.right && delta.top < odds.bottom && delta.bottom > odds.top };
  });
  assert.ok(inline.className.includes('result-difference-positive')); assert.ok(['flex', 'inline-flex'].includes(inline.parentDisplay)); assert.equal(inline.beside, true);
  assert.equal(await page.locator('.probability-results > p.small').innerText(), 'Hand size 6 · 60 active copies · 2 active cards');
  assert.equal(await page.locator('.pinned-result > p.small').innerText(), 'Hand size 5 · 60 active copies · 2 active cards');
  const summaries = await page.evaluate(() => ['.probability-results > p.small', '.pinned-result > p.small'].map(selector => { const e = document.querySelector(selector), s = getComputedStyle(e); return { className: e.className, font: s.fontSize, line: s.lineHeight, margin: s.marginBottom, children: e.children.length }; }));
  assert.deepEqual(summaries[0], summaries[1]); assert.equal(summaries[0].children, 0);
  await page.locator('#handSize').fill('4'); await page.locator('#handSize').press('Tab');
  await start(); await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
  const negative = await page.locator('.probability-total .result-difference').evaluate(e => ({ text: e.textContent, color: getComputedStyle(e).color, className: e.className }));
  assert.match(negative.text, /^-\d+(?:[.,]\d+)?\s*%$/); assert.ok(negative.className.includes('result-difference-negative'));
  assert.notEqual(negative.color, inline.color);
  await page.locator('.pin-result-action').click();
  const neutral = await page.locator('.probability-total .result-difference').evaluate(e => ({ text: e.textContent, className: e.className }));
  assert.match(neutral.text, /^0\s*%$/); assert.ok(neutral.className.includes('result-difference-neutral'));
  const viewport = await page.evaluate(() => ({ client: document.documentElement.clientWidth, scroll: document.documentElement.scrollWidth }));
  assert.equal(viewport.client, viewport.scroll);
  deltaEvidence.push({ width, theme, idle, busy, inline, negative, neutral, summaries, viewport });
  await page.locator('.pinned-result button').click();
  }
  console.log(JSON.stringify({ engine, phase: 'comparison', comparisonEvidence, deltaEvidence }));
  }
  if (phase === 'comparison') { await comparisons(); assert.deepEqual(errors, []); return; }
  const cancellation = [];
  for (const width of [1440, 390]) {
   await page.setViewportSize({ width, height: 900 });
   await load(fixture(15, 15));
   await page.evaluate(() => { workProbe.beats = []; });
   await start(); await page.waitForTimeout(200);
   await page.getByLabel('Color theme preference').selectOption('dark');
   const sent = JSON.parse(await page.evaluate(() => workProbe.inputs.at(-1)));
   assert.equal(sent.WorkUnits, 50_000_000, 'ordinary Calculate selects the reviewed allowance once');
   const began = Date.now(); await page.getByRole('button', { name: 'Cancel calculation', exact: true }).click();
   const closedMs = await settle();
   const metrics = await page.evaluate(() => ({ width: innerWidth, client: document.documentElement.clientWidth,
    scroll: document.documentElement.scrollWidth, beats: workProbe.beats.length,
    maxHeartbeatGap: Math.max(...workProbe.beats.slice(1).map((t, i) => t - workProbe.beats[i])) }));
   assert.equal(metrics.width, width); assert.equal(metrics.client, metrics.scroll); assert.ok(metrics.beats > 5);
   assert.equal(await page.getByRole('alert').count(), 0);
   cancellation.push({ ...metrics, cancelAndCloseMs: Date.now() - began, closedMs });
   console.log(JSON.stringify({ engine, slowdown, phase: 'cancel', evidence: cancellation.at(-1) }));
   // Restart immediately after cancellation, without reloading or replacing input.
   await start(); await page.getByRole('button', { name: 'Cancel calculation', exact: true }).click();
   await settle();
  }
  // Five rapid restart cycles retain at most this finite burst, then release all contexts.
  await load(fixture(15, 15)); const closedBefore = closed; peak = live;
  for (let i = 0; i < 5; i++) {
   await start(); await page.getByRole('button', { name: 'Cancel calculation', exact: true }).click();
   console.log(JSON.stringify({ engine, slowdown, phase: 'rapid-cancel', iteration: i + 1, live, peak }));
  }
  const rapidClosedMs = await settle(); assert.equal(closed - closedBefore, 5); assert.ok(peak <= 5);
  await start(); await page.locator('#handSize').fill('14'); await page.locator('#handSize').press('Tab'); await settle();
  await page.waitForFunction(() => { try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).HandSize === 14; } catch { return false; } });
  await start(); await load(fixture(1)); await settle();
  await calculate.click(); await page.locator('.probability-total-value').waitFor();
  assert.equal(await page.getByRole('alert').count(), 0);
  // Exercise the trimmed UI reader with malformed/generic response envelopes.
  // The real runtime/wire work above is separate from these transport injections.
  for (const response of ['{}', '{"Result":{}}', '{"Error":"ordinary worker failure","FailureKind":2}', '{"Error":"invalid reason","LimitReason":999}']) {
   await page.evaluate(response => {
    window.savedPolicyWorker = Worker;
    window.policyInjectedResponses = 0;
    window.Worker = class {
     constructor() { queueMicrotask(() => this.onmessage?.({ data: { ready: true } })); }
     postMessage() { window.policyInjectedResponses++; queueMicrotask(() => this.onmessage?.({ data: { id: 1, response } })); }
     terminate() {}
    };
   }, response);
   try {
    await calculate.click();
    await page.waitForFunction(() => window.policyInjectedResponses === 1);
    await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
    await page.getByRole('alert').filter({ hasText: 'Calculation failed:' }).waitFor();
   } finally { await page.evaluate(() => { window.Worker = window.savedPolicyWorker; }); }
  }
  await start();
  await page.waitForFunction(() => document.querySelector('.calculate-action > button')?.getAttribute('aria-busy') !== 'true');
  assert.equal(await page.getByRole('alert').count(), 0); assert.deepEqual(errors, []);
  await comparisons();
  console.log(JSON.stringify({ engine, browser: browser.version(), slowdown, phase, ...wireEvidence,
   cancellation, rapid: { peak, rapidClosedMs, closed }, errors }));
 } finally { clearTimeout(deadline); await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
