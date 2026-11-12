---
name: ygo-issue
description: "Create or refine a YGO calculator issue from a bug, feature idea, investigation or deferred work. Does not implement the issue."
---

# Create or refine an issue

Read [context](../ygo-implement/references/context.md) and [GitHub writing](references/github-writing.md). Inspect relevant current source and existing issues/PRs to avoid duplicates and stale premises. Use the connected GitHub tools; issue work does not need local Git credentials, SDK setup or a build.

Record the concrete problem, observed versus intended behavior, useful reproduction/evidence, and decided acceptance criteria. Keep unresolved design choices explicit rather than inventing requirements. Follow the applicable repository issue template where it adds useful information. Link dependencies and keep deferred work separate from the originating PR.

Use [authorization](../ygo-implement/references/authorization.md) before writing externally. A request to create/update the issue authorizes that write; a draft request ends with draft text. Verify the created/updated issue and return its link. For actual implementation, route to [implement](../ygo-implement/SKILL.md).
