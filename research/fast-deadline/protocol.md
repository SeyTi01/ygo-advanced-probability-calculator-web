# Frozen Fast deadline experiment

Baseline: 63a4780fb3fa937907c1c8be4662550197179f27 (fresh origin/dev).
Policy frozen before measurement; no complexity predictor or fitted cutoff.

One worker computes normal Fast once. If it completes with every row Exact,
finish immediately. Otherwise retain its complete response outside the worker,
then attempt physical Exact with only the unused part of the original Work
allowance. Use the original Storage tier. Select an upgrade only when every row
completes with proven Exact; preserve requested/executed Fast provenance in a
production implementation. Any upgrade failure returns the retained Fast response.

Main-thread watchdog: six seconds from dispatch to a ready worker, with at most
five seconds for the upgrade. Thus the upgrade deadline is min(dispatch + 6000,
upgrade dispatch + 5000). Startup retains the separate 30-second ceiling. No
provisional UI results: the completed fallback is internal until selection ends.
If initial Fast has not completed by six seconds, terminate with an honest time
limit and no values. Explicit Exact and OpeningHand have no automatic deadline.
After forced termination, wait 2500 ms before starting another worker. Measure
context destruction separately; this delay is a measured mitigation, not a
portable browser shutdown acknowledgement. Background tabs/main-thread stalls
can delay timers; no hardware-independent end-to-end SLA is claimed.

Rejected alternatives: an input-only score cannot enforce a deadline; two heavy
workers create CPU/RAM competition; Exact-first without a checkpoint can consume
the entire request allowance and prevent fallback; always giving both solvers
five seconds can make an affordable four-second pair take eight seconds. The
chosen cap can miss a four-second upgrade after a four-second Fast: disclose
this opportunity loss instead of claiming every sub-five-second Exact wins.

Acceptance: ordinary ~0.8, ~1.5 and ~3-5 second physical results should upgrade;
stop pathological upgrades regardless of input shape, keep completed fallback
fractions/certificates unchanged, preserve original explicit mode results and
one cumulative Work allowance. Verify blocked synchronous worker termination,
request ownership, cancellation, late replies, deadline/response ties, repeated
recovery, startup and quiescence before production integration. At most one
serious design plus a focused correction; failure of genuine safety/correctness
or recovery means NO-GO with reproducible evidence, no partial product edits.

Corpus: preserve all 14 published #225 inputs, add #226 hidden-loss 3/4 overrides,
VS/K9 hand-size and 60-card variants, current catalog search-cost/mode fixtures,
ordered/hand-cost/deferred controls, and Work/Storage failures. All ordinary
variants have <=3 copies per identity; grouped-filler controls are labeled.
The exact user session was not supplied. The VS/K9 inputs are maintained
equivalents, and synthetic custom overrides are not printed-card claims.
Reserve hidden-loss-4, 60-card, and catalog fixtures as held-out policy checks.

Optimized non-AOT Release WASM, real Edge module worker, Interactive Work and
Standard Storage except the existing explicitly low-Work diagnostic. Record
full raw outcomes/rows/counters, worker bootstrap, transport-inclusive request
times and engine time separately; two warmups and ten seeded interleaved samples
for successful modes, one observation for typed failures. Cold observations are
singletons. Native diagnostics supplement, never substitute for browser evidence.
No relative-slowdown or millisecond-delta rejection gates. Allocation totals,
end-request managed heap and WASM capacity are not peak live memory.
