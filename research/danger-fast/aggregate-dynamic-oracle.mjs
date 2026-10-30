// Independent labeled-permutation oracle for fixture-scoped dynamic prototype.
import { dynamic } from './aggregate-dynamic.mjs';
import fs from 'node:fs';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const sum = a => a.reduce((x, y) => x + y, 0);
const factorial = n => n < 2 ? 1n : BigInt(n) * factorial(n - 1);
const gcd = (a, b) => { while (b) [a, b] = [b, a % b]; return a; };
const fraction = (n, d) => { const g = gcd(n, d); return `${n / g}/${d / g}`; };
function* permutations(a, start = 0) {
  if (start === a.length) { yield a.slice(); return; }
  for (let i = start; i < a.length; i++) {
    [a[start], a[i]] = [a[i], a[start]]; yield* permutations(a, start + 1); [a[start], a[i]] = [a[i], a[start]];
  }
}
function oracle(ordinary, effects, handSize) {
  const cards = [];
  ordinary.forEach((count, row) => { for (let i = 0; i < count; i++) cards.push({ kind: 'ordinary', row, id: cards.length }); });
  effects.forEach((count, row) => { for (let i = 0; i < count; i++) cards.push({ kind: 'effect', row, id: cards.length }); });
  let lcm = 1n;
  for (let i = 1n; i <= BigInt(handSize); i++) lcm = lcm / gcd(lcm, i) * i;
  const unit = lcm ** BigInt(Math.min(handSize, sum(effects.slice(0, 3)))), denominator = factorial(cards.length) * unit;
  const wins = Array(6).fill(0n);
  const batch = card => card.kind === 'ordinary' || card.row === 5 ? 0 : card.row < 3 ? 1 : card.row === 3 ? 2 : 3;
  function finished(hand, remaining, mass) {
    const held = [0, 0, 0, 0], available = [0, 0, 0, 0];
    let search = 0;
    for (const card of hand) { if (card.kind === 'ordinary') held[card.row]++; else if (card.row === 5) search++; }
    for (const card of remaining) if (card.kind === 'ordinary') available[card.row]++;
    const success = [false, false, false];
    function witnesses(left, takenStarter, takenOverlap) {
      const s = held[1] + takenStarter, e = held[2], o = held[3] + takenOverlap;
      success[0] ||= s + o >= 1 && e + o >= 1 && s + e + o >= 2 && s + o <= 5 && e + o <= 5;
      success[1] ||= o >= 1 && o <= 5;
      success[2] ||= s + o === 0 && e + o >= 1 && e + o <= 5;
      if (!left) return;
      if (takenStarter < available[1]) witnesses(left - 1, takenStarter + 1, takenOverlap);
      if (takenOverlap < available[3]) witnesses(left - 1, takenStarter, takenOverlap + 1);
    }
    witnesses(search, 0, 0);
    [success.some(Boolean), ...success, success[0] || success[1], success[2]].forEach((yes, row) => { if (yes) wins[row] += mass; });
  }
  function removal(hand, queue, remaining, darkOnly) {
    const ordinaryOrder = [0, 7, 8, 9], effectOrder = [3, 5, 4, 2, 6, 1];
    const eligible = hand.filter(card => !darkOnly || card.kind === 'ordinary' ? !darkOnly || card.row === 1 || card.row === 3 : card.row < 3);
    const targetCounts = [hand.filter(card => card.kind === 'ordinary' && card.row === 1).length, hand.filter(card => card.kind === 'ordinary' && card.row === 3).length];
    function rank(card) {
      let tier;
      if (card.kind === 'ordinary') tier = card.row === 0 ? 4 : 1;
      else if (card.row === 5 && remaining > 0 && (targetCounts[0] < ordinary[1] || targetCounts[1] < ordinary[3])) tier = 1;
      else tier = queue.some(token => token.id === card.id) && remaining >= batch(card) ? 2 : 4;
      return tier * 100 + (card.kind === 'ordinary' ? ordinaryOrder : effectOrder)[card.row];
    }
    // Choose last physical token of the selected row, matching FIFO removal.
    eligible.sort((a, b) => rank(b) - rank(a) || b.id - a.id);
    if (!eligible.length) return null;
    const selected = eligible[0];
    const latestQueued = queue.findLast(card => card.kind === selected.kind && card.row === selected.row);
    return latestQueued ?? selected;
  }
  function run(hand, queue, deck, cursor, mass) {
    queue = queue.filter(card => deck.length - cursor >= batch(card));
    const source = queue[0];
    if (!source) { finished(hand, deck.slice(cursor), mass); return; }
    if (source.row < 3) {
      if (mass % BigInt(hand.length)) throw Error('Nonintegral oracle random choice');
      const branchMass = mass / BigInt(hand.length);
      for (const discarded of hand) {
        let nextHand = hand.filter(card => card.id !== discarded.id), nextQueue = queue.filter(card => card.id !== discarded.id), nextCursor = cursor;
        if (discarded.kind !== source.kind || discarded.row !== source.row) {
          nextHand = nextHand.filter(card => card.id !== source.id); nextQueue = nextQueue.filter(card => card.id !== source.id);
          const drawn = deck[nextCursor++]; nextHand.push(drawn); if (batch(drawn)) nextQueue.push(drawn);
        }
        run(nextHand, nextQueue, deck, nextCursor, branchMass);
      }
      return;
    }
    let nextHand = hand.filter(card => card.id !== source.id), nextQueue = queue.slice(1), nextCursor = cursor;
    for (let draw = 0; draw < batch(source); draw++) { const drawn = deck[nextCursor++]; nextHand.push(drawn); if (batch(drawn)) nextQueue.push(drawn); }
    for (let discard = 0; discard < (source.row === 3 ? 1 : 2); discard++) {
      const selected = removal(nextHand, nextQueue, deck.length - nextCursor, source.row === 3);
      if (!selected) return; // FailBranch remains in original denominator.
      nextHand = nextHand.filter(card => card.id !== selected.id); nextQueue = nextQueue.filter(card => card.id !== selected.id);
    }
    run(nextHand, nextQueue, deck, nextCursor, mass);
  }
  for (const deck of permutations(cards)) { const hand = deck.slice(0, handSize); run(hand, hand.filter(card => batch(card)), deck, handSize, unit); }
  return wins.map(win => fraction(win, denominator));
}
const cases = [
  { ordinary: [1, 1, 1, 1], effects: [1, 0, 0, 1, 0, 1], handSize: 3 },
  { ordinary: [0, 1, 1, 1], effects: [1, 0, 0, 0, 1, 1], handSize: 3 },
  { ordinary: [0, 1, 1, 1], effects: [1, 1, 0, 1, 0, 0], handSize: 3 },
  { ordinary: [1, 1, 0, 1], effects: [2, 0, 0, 0, 1, 0], handSize: 3 },
  { ordinary: [0, 1, 1, 1], effects: [1, 0, 0, 1, 1, 0], handSize: 2 },
];
for (const [index, fixture] of cases.entries()) {
  const name = `mixed-oracle-${index}`, start = performance.now();
  const actual = dynamic({ ...fixture, cap: 100000, name });
  const enumerated = dynamic({ ...fixture, cap: 100000, name, analyticTerminal: false, terminalCacheCap: 0 });
  if (JSON.stringify(actual.bounds) !== JSON.stringify(enumerated.bounds)) throw Error('Analytic terminal mismatch');
  const expected = oracle(fixture.ordinary, fixture.effects, fixture.handSize);
  const lowBudgetWidths = [];
  const compare = (left, right) => { const [a, b] = left.split('/').map(BigInt), [c, d] = right.split('/').map(BigInt); return a * d <= c * b; };
  for (const cap of [1, 2, 8]) {
    const bounded = dynamic({ ...fixture, cap, name });
    const repeated = dynamic({ ...fixture, cap, name });
    if (bounded.bounds.some((bound, row) => !compare(bound.lower, expected[row]) || !compare(expected[row], bound.upper))) throw Error('Bounded containment');
    if (JSON.stringify(bounded.bounds) !== JSON.stringify(repeated.bounds)) throw Error('Nonrepeatable result');
    lowBudgetWidths.push({ cap, width: bounded.width_percentage_points });
  }
  const result = { name, fixture, source_sha256: actual.source_sha256, oracle_source_sha256: crypto.createHash('sha256').update(fs.readFileSync(fileURLToPath(import.meta.url))).digest('hex'), expected, actual: actual.bounds.map(x => x.lower), width: actual.width_percentage_points, lowBudgetWidths, elapsed_seconds: (performance.now() - start) / 1000 };
  fs.appendFileSync(fileURLToPath(new URL('aggregate-dynamic-oracle-results.jsonl', import.meta.url)), JSON.stringify(result) + '\n');
  console.log(JSON.stringify(result));
  if (actual.width_percentage_points || expected.some((fraction, row) => fraction !== actual.bounds[row].lower)) throw Error('Mixed oracle mismatch');
}
