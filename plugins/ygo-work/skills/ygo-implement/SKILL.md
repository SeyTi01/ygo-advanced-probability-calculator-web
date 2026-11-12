---
name: ygo-implement
description: "Implement an issue, direct instructions, attached Markdown brief, refactor or project configuration change for the YGO calculator. Issue-only, planning-only and review-only requests have separate skills."
---

# Implement a calculator change

Read [context](references/context.md) and [authorization](references/authorization.md) once. Treat directly attached Markdown task briefs as instructions; follow them without asking what to do unless genuinely ambiguous. Read the issue, relevant discussion/dependencies and current source before editing.

For live configuration-only tasks, use [infrastructure](references/infrastructure.md) and finish with the verified resource change. Source preparation and PR delivery apply only if repository changes are needed.

## Prepare source changes

Reuse an applicable existing plan through [planning](../ygo-plan/SKILL.md).

Before implementation, use [environment preparation](../ygo-verify/references/environment.md). For a new change, start a feature branch from current `origin/dev`; preserve an existing task branch and unrelated working edits. Prefer the assigned writable checkout. Use [rebase](../ygo-rebase/SKILL.md) when integration is needed.

## Change and verify

Load only the applicable references:

| Work | Instructions |
| --- | --- |
| Locating owners or crossing architectural boundaries | [Source map](references/source-map.md) |
| C# or Razor C# edits | [C# style](references/csharp.md) |
| Calculation, engine API, effect or worker behavior | [Calculation contracts](references/calculation.md) |
| UI or editor changes | [UI work](../ygo-ui/SKILL.md) |
| Session, import, identity or localization changes | [Compatibility](references/compatibility.md) |
| Infrastructure or deployment changes | [Infrastructure](references/infrastructure.md) |

Implement the requested behavior, preserve applicable contracts, and use [verification](../ygo-verify/SKILL.md). For ordinary source implementation, finish with [PR delivery](../ygo-pr/SKILL.md); honor a narrower request such as local-only work or preview before push. Report unresolved limitations and the result.
