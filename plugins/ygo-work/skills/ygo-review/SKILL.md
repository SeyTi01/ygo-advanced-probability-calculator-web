---
name: ygo-review
description: "Review YGO calculator code or a PR, assess review comments, and address justified feedback when requested. Use for evidence-based review, not routine PR publication."
---

# Review the change

Read [context](../ygo-implement/references/context.md). Inspect the base/head diff, surrounding callers, relevant tests and issue intent. Use [the source map](../ygo-implement/references/source-map.md) when owners or contracts are unclear; read [C# style](../ygo-implement/references/csharp.md) for C#/Razor C# review.

Prioritize actionable defects, contract regressions, state loss and consequential verification gaps. Support findings with exact locations, a concrete trigger and impact. Separate confirmed failures from unresolved hypotheses. Do not manufacture findings or turn preference differences into defects.

For review feedback, evaluate each comment against current source; accept, adapt or explain why it does not apply. If fixes were requested, use [implementation](../ygo-implement/SKILL.md) for those changes, preserving the intended PR scope. A review-only request ends with findings; do not edit or post externally without the applicable [authorization](../ygo-implement/references/authorization.md).

Use [verification](../ygo-verify/SKILL.md) when execution would resolve a material question. Read-only review does not require environment setup merely to begin. For requested GitHub replies, follow [GitHub writing](../ygo-issue/references/github-writing.md).
