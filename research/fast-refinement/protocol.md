# Frozen research protocol

Base: 63a4780fb3fa937907c1c8be4662550197179f27 (fresh origin/dev, clean detached workspace).
Frozen before measuring baseline or designing a candidate, 2026-10-16 UTC.
Research only; no production changes, merge, deployment or implementation PR.
Six-hour maximum from approximately 22:04 UTC. Stop early for a decisive failed gate.

Eight representative cases: four design (vsk9-three-desires, redundant-two,
overlap-or, multiple-draw-sources), four held out (maxima-exclusion,
direct-plus-category, ordered-target-loss-40, deferred-target-loss-40).
Held-out candidate measurements are read only after the mechanism is fixed.
These are structural coverage, not a random sample of player decks.
All representative decks have 40 cards and <=3 copies per identity.
The real target is a maintained equivalent: bundled VS/K9 with its three
Mulcharmy Fuwalos replaced by three Desires, with no old monster properties.
No separate user session was supplied; this is not asserted identical to it.
Other representative cases are hypothetical custom-effect configurations.
Ordered/deferred cases come from FastCertificateBenchmark.Export, with pooled
28-card filler split into ordinary <=3-copy entries. This preserves semantics
but deliberately tests identity-count overhead. The split occurs before timing.
Artificial 32-search and 30-search/2-blank cases are diagnostic only.
Controls: unchanged maintained VS/K9, no-search draw, opening-only, Work=1000.

Freeze individual input/session files and SHA-256 manifest before baseline.
Inputs use Interactive 150m Work/Standard Storage except explicit Work control.
Exact uses identical options/cards/limits, changing only Fast to Exact.
Meaningfully uncertain = baseline total OR any group width >=2 percentage points.
Case improvement requires narrower total/group, not merely an individual row.
Magnitude pass requires >=50% width reduction in total or a qualifying group;
report all other rows and require every row nested and containing Exact.
Report numerator/denominator over ALL uncertain representative cases, plus held-out
and eligible subsets. Zero-width cases excluded from improvement denominators.
Breadth >=half; magnitude strict majority; at least one multi-route group/total.
Runtime <=1.25x full Fast warm p50 on substantial improvements; inspect p90,
allocations, heap, checked storage and Work separately. Practical Exact advantage
required on costly workloads. No statistical significance claim from ten samples.

At most one mechanism and one focused correction after diagnosis. Two additional
refinement Work caps: 100,000 and 1,000,000, carved from remaining request allowance
with 4096 units reserved. No increased Storage tier or uncharged side cache.
Native Release, tiered compilation off; optimized non-AOT browser WASM.
Separate runtime bootstrap/first observations from two warmups and ten warmed
samples, seeded randomized interleaving of Fast / low / high / Exact / primary.
Primary-only is diagnostic; full Fast includes primary, certificate, public result.
Exclude input decoding and output JSON/transport equally. Raw fractions retained.
Count failed/unsupported/skipped requests. No successful reference from a limit.
Allocations are thread allocations; heap readings are observations, not peaks.

If breadth/precision fails, stop with NO-GO for the tested mechanism. If environment,
references or proof block meaningful evaluation, INCONCLUSIVE. Do not invent a
second mechanism or specialize to the artificial target family to rescue results.
