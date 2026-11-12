---
name: ygo-pr
description: "Prepare, publish or update a focused YGO calculator pull request from completed changes. Handles delivery and existing-PR continuation, not a new implementation by itself."
---

# Deliver the PR

Read [context](../ygo-implement/references/context.md), [authorization](../ygo-implement/references/authorization.md) and [GitHub writing](../ygo-issue/references/github-writing.md). Identify the intended task branch and any existing PR; update that PR when continuing its work rather than creating a duplicate.

1. Use [environment preparation](../ygo-verify/references/environment.md) for Git/publication access and `repo:scripts/codex/PRIVACY.md` for identity, hooks and outgoing-object inspection.
2. Inspect the complete diff and relevant recent/concurrent changes. For a task feature branch, fetch current `origin/dev` before finalizing; if behind, use [rebase](../ygo-rebase/SKILL.md), then rerun invalidated [verification](../ygo-verify/SKILL.md). Shared-branch release promotion follows [release](../ygo-release/SKILL.md) instead of task-branch rebasing or commit consolidation.
3. Before a task branch's first publication, combine unpushed corrections/refinements of one logical change into a coherent commit. Keep independently meaningful changes separate. Do not amend/squash published commits or rewrite published history for cosmetic cleanup.
4. For stylistic UI changes, satisfy [preview approval](../ygo-ui/references/preview-approval.md) before publishing. Publish through guarded local Git when authenticated, otherwise follow [API publication](references/api-publication.md). Verify the published commit and tree match the reviewed result. Keep task-generated scratch files out of commits.
5. Create/update the focused PR through GitHub using the established target (`dev` by default, or the release workflow's target), linking its issue and describing the final behavior. Verify the base/head and returned PR. Ordinary delivery stops with the PR open; release merges follow the release workflow.

Report the PR link, outcome and material limits in the task conversation. Do not claim a hosted page represents the branch without checking its deployment provenance.
