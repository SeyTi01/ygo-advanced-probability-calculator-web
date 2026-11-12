# Infrastructure work

Use only when deployment, hosting, card-data infrastructure or CI is relevant to the requested outcome. [Authorization](authorization.md) owns task scope; it includes the necessary project-specific writes, not unrelated account changes.

Read current repository configuration and inspect the actual Cloudflare/GitHub resources before choosing an action. Use connected Cloudflare tools and the applicable Cloudflare product skill when available; discover the needed capability rather than inventing endpoints. Keep native GitHub tools for workflows/PR state. For Wrangler-managed code, use the applicable Wrangler/Workers guidance. If a connector is absent, discover/connect it through the supported mechanism; do not ask for raw secrets or assume CLI credentials.

Identify the account, project/resource, branch and deployment channel from evidence. Before naming resources or publishing content, apply the Public content section of `repo:scripts/codex/PRIVACY.md`. Prepare the concrete configuration/code change and verify it locally where possible, then apply the task-relevant change through the authenticated route. Preserve unrelated settings and record enough prior state to recover a changed configuration. Follow required tool approvals without adding another task-authorization question.

Check the resulting resource/deployment and its commit/branch/version. A familiar hostname or successful local build does not prove which revision is live. Do not intentionally deploy as an incidental consequence of unrelated work. For analytics/SEO-only questions, use existing GSC/Cloudflare capabilities without modifying resources or adding a permanent analytics workflow.
