---
name: ygo-verify
description: "Select and run meaningful YGO calculator verification, assess test coverage, or troubleshoot verification setup. Supports native, JavaScript and published-WASM checks without running every suite for every task."
---

# Verify the relevant behavior

Read [context](../ygo-implement/references/context.md). Before execution use [environment preparation](references/environment.md). For test design or coverage review use [test guidance](references/testing.md).

Choose checks by the affected contract and load only their procedure:

| Check | Procedure |
| --- | --- |
| Native or JavaScript behavior | [Application checks](references/app-checks.md) |
| Engine isolation or package substitution | [Boundary proof](references/engine-boundary.md) |
| Published WASM or browser behavior | [Browser checks](references/browser-checks.md) |
| Published HTML/SEO | [SEO checks](references/seo-checks.md) |
| Issue #295 cold-worker regression | [Frozen worker regression](references/frozen-worker.md) |
| UI appearance or interactions | [UI work](../ygo-ui/SKILL.md) |
| Plugin instructions or packaging | [Plugin checks](references/plugin-checks.md) |
| Performance | [Measurement guidance](../ygo-diagnose/references/measurements.md) |

Run focused checks first; substantial application changes also need solution-level and relevant integration checks. Instruction/document-only changes need link/package/content validation. CI complements local checks; inspect `repo:.github/workflows/tests.yml` for current coverage and status.

Unit tests do not establish published-WASM behavior or visual quality.

Reuse checks for unchanged source/environment. Rerun checks invalidated by edits or integration, not every completed check after every follow-up. Record actual commands, outcomes and unrun/blocked checks for the task; never claim unavailable execution passed. Use [GitHub writing](../ygo-issue/references/github-writing.md) when evidence belongs in an issue/PR, and keep visual limits distinct from test results.
