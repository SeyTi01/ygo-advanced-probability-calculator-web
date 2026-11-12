---
name: ygo-plan
description: "Plan a YGO calculator change, assess implementation readiness, or document an existing plan. Also supports proportional planning within implementation without creating unsolicited plan files."
---

# Plan the change

Read [context](../ygo-implement/references/context.md). For technical planning, inspect the task, relevant source and [scope](../ygo-implement/references/authorization.md); use [the source map](../ygo-implement/references/source-map.md) when owners or contracts are unclear. Formatting a supplied plan does not require rediscovering settled decisions.

Establish intended behavior, settled constraints and acceptance criteria. Resolve uncertainty about contracts, affected owners and test integration from available source. Ask only for missing decisions/evidence that block dependent work; keep optional context and unavailable execution separate.

Keep one current plan proportional to the change: baseline, goal, key decisions and rationale, affected files/symbols, contracts/data changes, ordered steps/dependencies, and concrete verification. Distinguish existing behavior from proposals. Include migrations, failure handling or rollout only when relevant. Reuse an applicable plan and revise only decisions invalidated by new evidence.

For multi-step or resumed work, retain a compact internal record of the branch/base, plan progress, settled decisions, working changes, verification and unresolved questions. Do not duplicate the plan or commit task notes by default. When delegation is useful, follow [delegation](references/delegation.md).

A standalone planning request finishes with the requested plan, without source changes. During implementation, keep the plan internal and continue authorized work. Create Markdown/Word or other plan artifacts only when requested, using the appropriate artifact skill.
