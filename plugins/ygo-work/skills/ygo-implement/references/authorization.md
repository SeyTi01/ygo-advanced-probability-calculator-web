# Task scope and standing authorization

The repository owner authorizes the actions necessary to complete the requested task:

- Implementation includes relevant source changes, checks, feature-branch commits/pushes, and creating or updating its PR to `dev`. Routine task-branch rebases and conflict resolution are included, even after publication.
- Issue creation and review-comment responses are authorized when requested as deliverables. Review-only work does not authorize implementing findings or posting unsolicited messages.
- Infrastructure work includes necessary changes to this project's Cloudflare resources, deployment/branch settings, GitHub workflows and deployments when they are part of accomplishing the task. Use [infrastructure](infrastructure.md) for that work.
- A request to perform a release authorizes and requires the complete [release](../../ygo-release/SKILL.md) workflow by default: update the canonical version, merge its PR into `dev`, merge the release PR into `main`, tag the verified latest `main` commit, and publish the latest GitHub Release with concise user-facing notes and the previous release as its comparison baseline. Follow the existing production deployment and verify its provenance. Do not ask for separate routine merge/tag/publication permission. Explicit preparation-only, notes-only, prerelease or other limits still govern. Ordinary feature work ends with an open PR.

Proceed without asking for those authorizations again, subject to [preview approval](../../ygo-ui/references/preview-approval.md) for stylistic UI changes. A narrower instruction—plan only, draft only, notes only, local preview before push, or prepare a release PR—limits the deliverable. Skill selection alone does not expand a task. These grants do not authorize unrelated branches/resources, direct pushes to protected branches, unrelated destructive changes, or bypassing repository protection and publication guards.

Task authorization does not supply credentials or override host/tool approval controls. Use the available approval mechanism when execution requires it; if unavailable, report the specific blocked operation and continue independent work.

Keep changes focused. Framework/dependency upgrades, broad refactors and unrelated documentation need task scope. Update the root README only as a separately requested task, including before releases; verify its claims and keep it useful to first-time visitors. Do not silently add README work to a feature or release.
