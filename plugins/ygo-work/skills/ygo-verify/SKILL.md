---
name: ygo-verify
description: "Select and run meaningful YGO calculator verification, assess test coverage, or troubleshoot verification setup. Supports native, JavaScript and published-WASM checks without running every suite for every task."
---

# Verify the relevant behavior

Read [context](../ygo-implement/references/context.md) and [environment preparation](references/environment.md) before execution. Use [commands](references/commands.md) for reproducible entry points and [test design](references/testing.md) when designing, changing or evaluating tests, including calculation expectations.

Choose checks by the changed contract: engine/calculation, editor interactions, session/import, localization, worker lifecycle, or published output. Run focused regression checks first; substantial application changes also need solution-level checks and the relevant existing integration checks. Instruction/document-only changes need link/package/content validation, not an unrelated application build. CI complements local verification; inspect the current workflow rather than assuming its coverage or status.

Use published-WASM checks for browser/runtime risks and practical visual inspection for UI; a passing unit test does not establish either.

Reuse checks for unchanged source/environment. Rerun checks invalidated by edits or integration, not every completed check after every follow-up. Record actual commands, outcomes and unrun/blocked checks for the task; never claim unavailable execution passed. Use [GitHub writing](../ygo-issue/references/github-writing.md) when evidence belongs in an issue/PR, and keep visual limits distinct from test results.
