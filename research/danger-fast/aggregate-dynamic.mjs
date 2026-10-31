// Fixture-scoped research, not production. Anonymous ordinary identities are
// revealed only at observed removals or terminal search evaluation.
import fs from 'node:fs';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const sum = a => a.reduce((x, y) => x + y, 0);
const facts = [1n];
for (let i = 1; i <= 80; i++) facts[i] = facts[i - 1] * BigInt(i);
const choose = (n, k) => k < 0 || k > n ? 0n : facts[n] / facts[k] / facts[n - k];
const gcd = (a, b) => { while (b) [a, b] = [b, a % b]; return a; };
const fraction = (n, d) => { const g = gcd(n, d); return `${n / g}/${d / g}`; };
function scale(mass, n, d) {
  const value = mass * BigInt(n), divisor = BigInt(d);
  if (value % divisor) throw Error(`Nonintegral refinement ${n}/${d}`);
  return value / divisor;
}
// Ordinary order: filler, Starter, Extender, Overlap.
// Explicit order: Bigfoot, Nessie, Mothman, Allure, Graceful, Search.
const canonical = [3, 5, 4, 2, 6, 1];
const ordinaryCanonical = [0, 7, 8, 9];
const batch = [1, 1, 1, 2, 3, 0];

export function dynamic({ size = 40, handSize = 5, cap = 8192, ordinary = [size - 28, 6, 6, 3], effects = [3, 3, 2, 2, 1, 2], name = 'heavy', analyticTerminal = true, terminalCacheCap = 4096 } = {}) {
  size = sum(ordinary) + sum(effects);
  let lcm = 1n;
  for (let i = 1n; i <= BigInt(handSize); i++) lcm = lcm / gcd(lcm, i) * i;
  const denominator = facts[size] * lcm ** BigInt(Math.min(handSize, sum(effects.slice(0, 3))));
  // [effect deck, physical ordinary deck, explicit held, anonymous held, FIFO,
  //  draws remaining, completion source(+1), unobserved ordinary inventory,
  //  known ordinary hand]
  let frontier = new Map();
  let residual = 0n, resolved = 0n, failures = 0n, expanded = 0, peak = 1;
  let reveals = 0, terminalSplits = 0, discarded = 0, retainedCellsPeak = 0;
  const terminalCache = new Map();
  let terminalCacheHits = 0, terminalCacheMisses = 0;
  const wins = Array(6).fill(0n), layers = [];
  const start = performance.now();
  let next = frontier;
  insert([effects, sum(ordinary), effects.map(() => 0), 0, [], handSize, 0, ordinary, [0, 0, 0, 0]], denominator);

  function trim() {
    if (next.size <= cap) return;
    const entries = [...next.entries()].sort((a, b) => a[1][1] === b[1][1] ? a[0].localeCompare(b[0], 'en') : a[1][1] > b[1][1] ? -1 : 1);
    next.clear();
    for (let i = 0; i < entries.length; i++) {
      if (i < cap) next.set(...entries[i]);
      else { residual += entries[i][1][1]; discarded++; }
    }
  }
  function insert(state, mass) {
    const key = JSON.stringify(state), previous = next.get(key);
    next.set(key, [state, mass + (previous?.[1] ?? 0n)]);
    peak = Math.max(peak, next.size);
    if (next.size > cap * 2) trim();
  }
  function reveal(state, mass, continuation) {
    const unknown = state[3], inventory = state[7], inventorySize = sum(inventory);
    if (!unknown) { continuation(state, mass); return; }
    reveals++;
    const divisor = choose(inventorySize, unknown), counts = [0, 0, 0, 0];
    function visit(row, left, weight) {
      if (row === 4) {
        if (left) return;
        const child = state.slice(); child[3] = 0;
        child[7] = inventory.map((count, i) => count - counts[i]);
        child[8] = state[8].map((count, i) => count + counts[i]);
        continuation(child, scale(mass, weight, divisor));
        return;
      }
      for (let count = 0; count <= Math.min(left, inventory[row]); count++) {
        counts[row] = count; visit(row + 1, left - count, weight * choose(inventory[row], count));
      }
    }
    visit(0, unknown, 1n);
  }
  function chooseRemoval(state, darkOnly) {
    const [deck, pool, held, , queue, , , , known] = state;
    const remaining = sum(deck) + pool;
    let selected = null, tier = 0, order = -1;
    for (let row = 0; row < held.length; row++) {
      if (!held[row] || darkOnly && row >= 3) continue;
      const usable = row < 5 && queue.includes(row) && remaining >= batch[row];
      // Search targets are Starter/Overlap; their original copy counts matter,
      // even after random losses. This matches SearchKeepPolicy's held-only tier.
      const searchUseful = row === 5 && remaining > 0 && (known[1] < ordinary[1] || known[3] < ordinary[3]);
      const candidateTier = searchUseful ? 1 : usable ? 2 : 4;
      if (candidateTier > tier || candidateTier === tier && canonical[row] >= order) {
        selected = ['effect', row]; tier = candidateTier; order = canonical[row];
      }
    }
    for (let row = 0; row < known.length; row++) {
      if (!known[row] || darkOnly && row !== 1 && row !== 3) continue;
      const candidateTier = row === 0 ? 4 : 1;
      if (candidateTier > tier || candidateTier === tier && ordinaryCanonical[row] >= order) {
        selected = ['ordinary', row]; tier = candidateTier; order = ordinaryCanonical[row];
      }
    }
    return selected;
  }
  function remove(state, selected) {
    const child = state.slice(), [kind, row] = selected;
    if (kind === 'ordinary') { child[8] = state[8].slice(); child[8][row]--; }
    else {
      child[2] = state[2].slice(); child[2][row]--;
      const index = state[4].lastIndexOf(row);
      if (index >= 0) child[4] = [...state[4].slice(0, index), ...state[4].slice(index + 1)];
    }
    return child;
  }
  function complete(state, mass) {
    const source = state[6] - 1;
    let child = state.slice(); child[6] = 0;
    for (let left = source === 3 ? 1 : 2; left; left--) {
      const selected = chooseRemoval(child, source === 3);
      if (!selected) { failures += mass; return; }
      child = remove(child, selected);
    }
    settle(child, mass);
  }
  function settle(state, mass) {
    if (state[5]) { insert(state, mass); return; }
    if (state[6]) {
      // No ordinary DARK row can beat an explicit removable Danger's tier.
      // In this fixture all Danger tokens are queued while draws remain legal.
      if (state[6] === 4 && state[2].slice(0, 3).some(x => x > 0) && sum(state[0]) + state[1] > 0) {
        const selected = [1, 2, 0].find(row => state[2][row] > 0);
        const child = remove(state, ['effect', selected]); child[6] = 0; settle(child, mass);
      } else reveal(state, mass, complete);
      return;
    }
    let [deck, pool, held, unknown, queue, , , inventory, known] = state;
    const remaining = sum(deck) + pool;
    while (queue.length && remaining < batch[queue[0]]) queue = queue.slice(1);
    if (!queue.length) { const child = state.slice(); child[4] = []; terminal(child, mass); return; }
    const source = queue[0];
    if (source >= 3) {
      const nextHeld = held.slice(); nextHeld[source]--;
      insert([deck, pool, nextHeld, unknown, queue.slice(1), batch[source], source + 1, inventory, known], mass);
      return;
    }
    const size = sum(held) + unknown + sum(known);
    for (let token = 0; token < queue.length; token++) {
      const target = queue[token], nextHeld = held.slice(); nextHeld[target]--;
      let nextQueue = [...queue.slice(0, token), ...queue.slice(token + 1)], draws = 0;
      if (target !== source) { nextHeld[source]--; nextQueue = nextQueue.slice(1); draws = 1; }
      settle([deck, pool, nextHeld, unknown, nextQueue, draws, 0, inventory, known], scale(mass, 1, size));
    }
    // Skipped unactivatable effect tokens still exist in hand. This handles
    // deck exhaustion without pretending all held effects have FIFO tokens.
    for (let row = 0; row < held.length; row++) {
      const unqueued = held[row] - queue.filter(x => x === row).length;
      if (!unqueued) continue;
      const nextHeld = held.slice(); nextHeld[row]--;
      let nextQueue = queue, draws = 0;
      if (row !== source) { nextHeld[source]--; nextQueue = queue.slice(1); draws = 1; }
      settle([deck, pool, nextHeld, unknown, nextQueue, draws, 0, inventory, known], scale(mass, unqueued, size));
    }
    if (unknown) {
      const nextHeld = held.slice(); nextHeld[source]--;
      settle([deck, pool, nextHeld, unknown - 1, queue.slice(1), 1, 0, inventory, known], scale(mass, unknown, size));
    }
    for (let row = 0; row < known.length; row++) {
      if (!known[row]) continue;
      const nextKnown = known.slice(), nextHeld = held.slice(); nextKnown[row]--; nextHeld[source]--;
      settle([deck, pool, nextHeld, unknown, queue.slice(1), 1, 0, inventory, nextKnown], scale(mass, known[row], size));
    }
  }
  function terminal(state, mass) {
    if (!terminalCacheCap) { terminalUncached(state, mass); return; }
    const key = JSON.stringify([state[1], state[2][5], state[3], state[7], state[8]]);
    let continuation = terminalCache.get(key);
    if (continuation) terminalCacheHits++;
    else {
      terminalCacheMisses++;
      const previous = wins.slice(), previousResolved = resolved;
      terminalUncached(state, denominator);
      if (resolved - previousResolved !== denominator) throw Error('Conditional terminal mass');
      const numerators = wins.map((win, row) => win - previous[row]);
      let divisor = denominator;
      for (const numerator of numerators) divisor = gcd(divisor, numerator);
      continuation = [numerators.map(n => n / divisor), denominator / divisor];
      for (let row = 0; row < wins.length; row++) wins[row] = previous[row];
      resolved = previousResolved;
      if (terminalCache.size < terminalCacheCap) terminalCache.set(key, continuation);
    }
    continuation[0].forEach((numerator, row) => { wins[row] += scale(mass, numerator, continuation[1]); });
    resolved += mass;
  }
  function terminalUncached(state, mass) {
    reveal(state, mass, (child, revealedMass) => {
      const inventory = child[7], known = child[8], physical = child[1], search = child[2][5];
      if (!search) { evaluate(known, 0, 0, 0, revealedMass); return; }
      const inventorySize = sum(inventory), other = inventory[0] + inventory[2], divisor = choose(inventorySize, physical);
      if (analyticTerminal && handSize <= 5) {
        // Optional-search event truth table; all fixed maxima are inactive at H<=5.
        // These are conditional event probabilities over the SAME deck assignment.
        const anyOverlap = divisor - choose(inventorySize - inventory[3], physical);
        const anyTarget = divisor - choose(inventorySize - inventory[1] - inventory[3], physical);
        const twoWithOverlap = anyOverlap - BigInt(inventory[3]) * choose(inventorySize - inventory[1] - inventory[3], physical - 1);
        const s = known[1], e = known[2], o = known[3];
        let distinct;
        if (s + o > 0 && e + o > 0 && s + e + o >= 2) distinct = divisor;
        else if (o > 0 || e > 0) distinct = anyTarget;
        else if (s > 0) distinct = anyOverlap;
        else distinct = search >= 2 ? twoWithOverlap : 0n;
        const overlap = o > 0 ? divisor : anyOverlap;
        const exclusion = s + o === 0 && e > 0;
        const positive = o > 0 || s > 0 && e > 0 ? divisor : e > 0 ? anyTarget : anyOverlap;
        [exclusion ? divisor : positive, distinct, overlap, exclusion ? divisor : 0n, positive, exclusion ? divisor : 0n].forEach((numerator, row) => { wins[row] += scale(revealedMass, numerator, divisor); });
        terminalSplits++; resolved += revealedMass;
        return;
      }
      for (let starter = 0; starter <= Math.min(inventory[1], physical); starter++) {
        for (let overlap = 0; overlap <= Math.min(inventory[3], physical - starter); overlap++) {
          const rest = physical - starter - overlap;
          if (rest > other) continue;
          const numerator = choose(inventory[1], starter) * choose(inventory[3], overlap) * choose(other, rest);
          evaluate(known, search, starter, overlap, scale(revealedMass, numerator, divisor));
        }
      }
    });
  }
  function evaluate(known, search, availableStarter, availableOverlap, mass) {
    terminalSplits++; resolved += mass;
    const success = [false, false, false];
    for (let starter = 0; starter <= Math.min(search, availableStarter); starter++) {
      for (let overlap = 0; overlap <= Math.min(search - starter, availableOverlap); overlap++) {
        const s = known[1] + starter, e = known[2], o = known[3] + overlap;
        success[0] ||= s + o > 0 && e + o > 0 && s + e + o >= 2 && s + o <= 5 && e + o <= 5;
        success[1] ||= o > 0 && o <= 5;
        success[2] ||= s + o === 0 && e + o > 0 && e + o <= 5;
      }
    }
    [success.some(Boolean), ...success, success[0] || success[1], success[2]].forEach((yes, row) => { if (yes) wins[row] += mass; });
  }
  for (let layer = 0; frontier.size; layer++) {
    // Visible-depth layers never revisit earlier layers; release old cache keys
    // instead of letting the first layer monopolize the bounded cache forever.
    terminalCache.clear();
    next = new Map();
    for (const [state, mass] of frontier.values()) {
      expanded++;
      const [deck, pool, held, unknown, queue, draws, completion, inventory, known] = state, remaining = sum(deck) + pool;
      if (!draws || !remaining) throw Error('Invalid pending state');
      if (pool) settle([deck, pool - 1, held, unknown + 1, queue, draws - 1, completion, inventory, known], scale(mass, pool, remaining));
      for (let row = 0; row < deck.length; row++) {
        if (!deck[row]) continue;
        const nextDeck = deck.slice(), nextHeld = held.slice(); nextDeck[row]--; nextHeld[row]++;
        settle([nextDeck, pool, nextHeld, unknown, row === 5 ? queue : [...queue, row], draws - 1, completion, inventory, known], scale(mass, deck[row], remaining));
      }
    }
    trim();
    const pending = [...next.values()].reduce((total, [, mass]) => total + mass, 0n);
    if (pending + residual + resolved + failures !== denominator) throw Error('Incomplete mass at layer ' + layer);
    retainedCellsPeak = Math.max(retainedCellsPeak, [...next.values()].reduce((total, [state, mass]) => total + 27 + state[4].length + Math.ceil(mass.toString(2).length / 32), 0));
    layers.push({ layer, pending_entries: next.size, pending_mass: fraction(pending, denominator), residual: fraction(residual, denominator), expanded });
    frontier = next;
  }
  return { name, size, hand_size: handSize, cap, analytic_terminal: analyticTerminal, terminal_cache_cap: terminalCacheCap, terminal_cache_hits: terminalCacheHits, terminal_cache_misses: terminalCacheMisses, terminal_cache_entries: terminalCache.size, source_sha256: crypto.createHash('sha256').update(fs.readFileSync(fileURLToPath(import.meta.url))).digest('hex'), expanded, peak_entries: peak, retained_cells_lower_bound: retainedCellsPeak, reveals, terminal_splits: terminalSplits, discarded_contributions: discarded,
    elapsed_seconds: (performance.now() - start) / 1000, residual: fraction(residual, denominator), width_percentage_points: Number(residual) / Number(denominator) * 100,
    resolved_mass: fraction(resolved, denominator), failure_mass: fraction(failures, denominator),
    bounds: wins.map(win => ({ lower: fraction(win, denominator), upper: fraction(win + residual, denominator), lower_decimal: Number(win) / Number(denominator), upper_decimal: Number(win + residual) / Number(denominator) })), layers };
}
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const size = Number(process.argv[2] ?? 40), cap = Number(process.argv[3] ?? 8192);
  const result = dynamic({ size, cap, name: `dynamic-heavy-${size}` });
  fs.appendFileSync(fileURLToPath(new URL('aggregate-dynamic-results.jsonl', import.meta.url)), JSON.stringify(result) + '\n');
  console.log(JSON.stringify(result));
}
