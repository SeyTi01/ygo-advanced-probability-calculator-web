// Isolated exact-rational anonymous ordinary-card experiment, not production.
// Execute: node research/danger-fast/aggregate-prototype.mjs
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
const gcd = (a, b) => { while (b) [a, b] = [b, a % b]; return a; };
class Q {
  constructor(n = 0n, d = 1n) { const g = gcd(n, d); this.n = n / g; this.d = d / g; }
  add(x) { return new Q(this.n * x.d + x.n * this.d, this.d * x.d); }
  mul(n, d = 1) { return new Q(this.n * BigInt(n), this.d * BigInt(d)); }
  times(x) { return new Q(this.n * x.n, this.d * x.d); }
  toString() { return `${this.n}/${this.d}`; }
  number() { return Number(this.n) / Number(this.d); }
}
const zero = () => new Q();
const sum = a => a.reduce((x, y) => x + y, 0);
const fact = n => n < 2 ? 1n : BigInt(n) * fact(n - 1);
const choose = (n, k) => k < 0 || k > n ? 0n : fact(n) / fact(k) / fact(n - k);
function add(map, state, mass) {
  const key = JSON.stringify(state);
  const old = map.get(key);
  map.set(key, [state, old ? old[1].add(mass) : mass]);
}
function skeleton(ordinary, effects, handSize, observers = [], name = 'small', delayAllure = false) {
  const search = observers.length ? effects.length - 1 : -1;
  let frontier = new Map(), terminal = new Map(), censored = zero();
  add(frontier, [effects, ordinary, effects.map(() => 0), 0, [], handSize, false], new Q(1n));
  let expanded = 0, peak = 0;
  const start = performance.now();
  function settle(state, mass, output) {
    let [deck, pool, held, unknown, queue, draws, completion] = state;
    const remaining = sum(deck) + pool;
    if (draws) { add(output, state, mass); return; }
    if (completion) {
      // Heavy fixture only: all ordinary DARK rows are relevant tier1. A held
      // queued Danger is eligible tier2, so Allure discards an explicit Danger
      // without observing anonymous labels. Canonical effect ID order chooses
      // Nessie (70711847), then Mothman (52350806), then Bigfoot (43316238).
      const selected = [1, 2, 0].find(row => held[row] > 0);
      if (selected === undefined) { censored = censored.add(mass); return; }
      held = held.slice(); held[selected]--;
      const token = queue.lastIndexOf(selected);
      if (token < 0) throw Error('This isolated proof assumes queued Danger');
      queue = [...queue.slice(0, token), ...queue.slice(token + 1)];
    }
    while (queue.length) {
      const source = queue[0];
      const required = observers.includes(source) ? (source === 3 ? 2 : 3) : 1;
      if (remaining < required) { queue = queue.slice(1); continue; }
      if (observers.includes(source)) {
        if (source === 3 && delayAllure) {
          const nextHeld = held.slice(); nextHeld[source]--;
          settle([deck, pool, nextHeld, unknown, queue.slice(1), 2, true], mass, output);
        } else censored = censored.add(mass);
        return;
      }
      const size = sum(held) + unknown;
      for (let token = 0; token < queue.length; token++) {
        const discarded = queue[token], nextHeld = held.slice();
        nextHeld[discarded]--;
        let nextQueue = [...queue.slice(0, token), ...queue.slice(token + 1)], nextDraws = 0;
        if (discarded !== source) { nextHeld[source]--; nextQueue = nextQueue.slice(1); nextDraws = 1; }
        settle([deck, pool, nextHeld, unknown, nextQueue, nextDraws, false], mass.mul(1, size), output);
      }
      if (search >= 0 && held[search]) {
        const nextHeld = held.slice(); nextHeld[search]--; nextHeld[source]--;
        settle([deck, pool, nextHeld, unknown, queue.slice(1), 1, false], mass.mul(held[search], size), output);
      }
      if (unknown) {
        const nextHeld = held.slice(); nextHeld[source]--;
        settle([deck, pool, nextHeld, unknown - 1, queue.slice(1), 1, false], mass.mul(unknown, size), output);
      }
      return;
    }
    add(terminal, [unknown, held], mass);
  }
  while (frontier.size) {
    peak = Math.max(peak, frontier.size);
    const following = new Map();
    for (const [state, mass] of frontier.values()) {
      expanded++;
      const [deck, pool, held, unknown, queue, draws, completion] = state, remaining = sum(deck) + pool;
      if (pool) settle([deck, pool - 1, held, unknown + 1, queue, draws - 1, completion], mass.mul(pool, remaining), following);
      for (let row = 0; row < deck.length; row++) {
        if (!deck[row]) continue;
        const nextDeck = deck.slice(), nextHeld = held.slice(); nextDeck[row]--; nextHeld[row]++;
        settle([nextDeck, pool, nextHeld, unknown, row === search ? queue : [...queue, row], draws - 1, completion], mass.mul(deck[row], remaining), following);
      }
    }
    frontier = following;
  }
  const terminalMass = [...terminal.values()].reduce((a, [, b]) => a.add(b), zero());
  if (terminalMass.add(censored).toString() !== '1/1') throw Error('Mass accounting');
  return [terminal, { name, censored: censored.toString(), censored_decimal: censored.number(), terminal_mass: terminalMass.toString(), expanded, peak_frontier: peak, elapsed_seconds: (performance.now() - start) / 1000 }];
}
function ordinaryEvents([a, b], unknown) {
  const result = [zero(), zero(), zero()];
  for (let x = Math.max(0, unknown - b); x <= Math.min(a, unknown); x++) {
    const y = unknown - x, mass = new Q(choose(a, x) * choose(b, y), choose(a + b, unknown));
    [x > 0, x === 0 && y > 0, x > 0 && y > 0].forEach((yes, row) => { if (yes) result[row] = result[row].add(mass); });
  }
  return result;
}
function* permutations(a, start = 0) {
  if (start === a.length) { yield a.slice(); return; }
  for (let i = start; i < a.length; i++) {
    [a[start], a[i]] = [a[i], a[start]];
    yield* permutations(a, start + 1);
    [a[start], a[i]] = [a[i], a[start]];
  }
}
function oracle(colors, danger, handSize) {
  const labels = [...danger, ...colors].flatMap((count, kind) => Array.from({ length: count }, (_, copy) => ({ kind, copy })));
  const wins = [zero(), zero(), zero()];
  function run(order, cursor, hand, mass) {
    const source = hand.find(card => card.kind < danger.length);
    if (!source || cursor === order.length) {
      const a = hand.filter(card => card.kind === danger.length).length, b = hand.filter(card => card.kind === danger.length + 1).length;
      [a > 0, a === 0 && b > 0, a > 0 && b > 0].forEach((yes, row) => { if (yes) wins[row] = wins[row].add(mass); });
      return;
    }
    hand.forEach((discarded, index) => {
      let nextHand = [...hand.slice(0, index), ...hand.slice(index + 1)], nextCursor = cursor;
      if (discarded.kind !== source.kind) { nextHand = [...nextHand.filter(card => card !== source), order[cursor]]; nextCursor++; }
      run(order, nextCursor, nextHand, mass.mul(1, hand.length));
    });
  }
  for (const order of permutations(labels)) run(order, handSize, order.slice(0, handSize), new Q(1n, fact(labels.length)));
  return wins;
}
const output = fileURLToPath(new URL('aggregate-results.jsonl', import.meta.url));
fs.writeFileSync(output, '');
function record(value) { const line = JSON.stringify(value); fs.appendFileSync(output, line + '\n'); console.log(line); }
for (const [colors, danger, hand] of [[[1, 2], [1], 3], [[1, 1], [2, 1], 3], [[2, 1], [1, 1], 2], [[1, 1], [2], 1]]) {
  const [terminal, result] = skeleton(sum(colors), danger, hand, [], `oracle-${colors}-${danger}-${hand}`);
  const aggregate = [zero(), zero(), zero()];
  for (const [[unknown], mass] of terminal.values()) ordinaryEvents(colors, unknown).forEach((value, row) => { aggregate[row] = aggregate[row].add(mass.times(value)); });
  const independent = oracle(colors, danger, hand);
  if (aggregate.some((q, i) => q.toString() !== independent[i].toString())) throw Error(`Oracle mismatch ${aggregate} ${independent}`);
  result.oracle_fractions = aggregate.map(String); record(result);
}
for (const size of [40, 60]) {
  const [, result] = skeleton(size - 13, [3, 3, 2, 2, 1, 2], 5, [3, 4], `heavy-${size}-first-observer`);
  record(result);
}
for (const size of [40, 60]) {
  const [, result] = skeleton(size - 13, [3, 3, 2, 2, 1, 2], 5, [3, 4], `heavy-${size}-deferred-allure-observer`, true);
  record(result);
}
