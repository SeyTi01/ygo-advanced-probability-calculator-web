---
name: ygo-diagnose
description: "Investigate wrong probabilities, runtime or worker failures, and performance regressions in the YGO calculator. Use for reproduction, causal analysis, benchmark audits and measured optimization."
---

# Diagnose the behavior

Read [context](../ygo-implement/references/context.md). Establish expected versus observed behavior or the benchmark question, revisions, input, mode and execution host. Inspect the smallest relevant call path; use [the source map](../ygo-implement/references/source-map.md) when owners or contracts are unclear.

Reproduce at the simplest reliable layer, using [verification](../ygo-verify/SKILL.md) for runtime preparation and checks. Distinguish a product failure from a fixture, environment or measurement failure. Trace ownership, cancellation, state and boundary transitions when relevant; do not extrapolate a Node/WASM observation to browser AOT or another runtime without evidence.

For performance investigations or benchmark audits, use [measurements](references/measurements.md). For numerical failures, use [calculation contracts](../ygo-implement/references/calculation.md) and independent enumeration before changing expectations.

Explain the supported cause, evidence and remaining uncertainty. If repair is requested, continue through [implementation](../ygo-implement/SKILL.md); diagnosis-only work stops with findings. Avoid a broad optimization/refactor campaign unrelated to the observed defect.
