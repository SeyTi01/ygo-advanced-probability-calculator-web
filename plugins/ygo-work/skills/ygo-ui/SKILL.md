---
name: ygo-ui
description: "Design, refine or preview YGO calculator UI and editor interactions. Use for visual changes, state-preserving editor behavior and browser feedback; supports concept-only and local-preview requests."
---

# Work on the calculator UI

Read [context](../ygo-implement/references/context.md) and [editor contracts](references/editor-contracts.md). Inspect the current component and adjacent controls before designing; show user-facing concepts instead of catalog IDs or implementation details.

For concepts, describe or preview the proposed interaction without assuming permission to publish code. Before standalone code edits, use [implementation preparation](../ygo-implement/SKILL.md#prepare-source-changes), including its scope, branch and environment setup, then return here. If called from implementation, reuse that preparation. Read [C# style](../ygo-implement/references/csharp.md) for C# or Razor C# edits and [compatibility](../ygo-implement/references/compatibility.md) when identity/session behavior is affected.

Inspect the actual rendered state at representative desktop and narrow widths, and relevant themes. Exercise the changed keyboard, focus, scrolling, popup and draft behavior. Stylistic UI changes require [preview approval](references/preview-approval.md) before publication, using the reference's Codex or web/cloud branch. For other requested local previews, use the host-appropriate environment path, verify browser loading, provide the usable URL, and respect any requested handoff.

Apply feedback to the same implementation, then use [verification](../ygo-verify/SKILL.md). Report what was visually checked and any limits. Return to the calling workflow or use [PR delivery](../ygo-pr/SKILL.md) when publication is in scope.
