---
name: ygo-ui
description: "Design, refine or preview YGO calculator UI and editor interactions. Use for visual changes, state-preserving editor behavior and browser feedback; supports concept-only and local-preview requests."
---

# Work on the calculator UI

Read [context](../ygo-implement/references/context.md) and [visual conventions](references/visual-contracts.md). Inspect the current component and adjacent controls.

For concept-only work, describe or mock up the proposed interaction and stop; do not require a server or application tests. For a requested working preview, use [the preview server](references/preview-server.md).

Before standalone code edits, use [implementation preparation](../ygo-implement/SKILL.md#prepare-source-changes), including its scope, branch and environment setup; reuse it when called from implementation. Load [editor contracts](references/editor-contracts.md) for state/interaction changes, [C# style](../ygo-implement/references/csharp.md) for C#/Razor C#, and [compatibility](../ygo-implement/references/compatibility.md) for identity/session changes.

Inspect implemented changes at desktop/narrow widths and relevant themes. Exercise affected keyboard, focus, selection, scrolling, popup anchoring/dismissal, tab state and drafts. Apply feedback to the same implementation and use [verification](../ygo-verify/SKILL.md); report visual checks and limits separately from test results. Exact spacing and incidental markup belong to visual review, not brittle test matrices.

For stylistic changes, follow [preview approval](references/preview-approval.md) before publication. Respect requested preview handoffs; otherwise return to the caller or use [PR delivery](../ygo-pr/SKILL.md) when in scope.
