# Infrastructure work

Use only when deployment, hosting, card-data infrastructure or CI is relevant to the requested outcome. [Authorization](authorization.md) owns task scope; it includes the necessary project-specific writes, not unrelated account changes.

Read current repository configuration and inspect the actual Cloudflare/GitHub resources before choosing an action. Use connected Cloudflare tools and the applicable Cloudflare product skill when available; discover the needed capability rather than inventing endpoints. Prefer native GitHub tools for workflows/PR state; when tools lack a required capability, use a supported browser under [computer-use authorization](computer-use.md). For Wrangler-managed code, use the applicable Wrangler/Workers guidance. If a connector is absent, discover/connect it through the supported mechanism; do not ask for raw secrets or assume CLI credentials.

Identify the account, resource, branch and deployment channel from evidence. Apply [public-content review](../../ygo-pr/references/public-content.md) before naming resources or publishing. Prepare and locally verify the change where possible, then use the authenticated route. Preserve unrelated settings and enough prior source/configuration/version/rollback state to recover. Follow tool approvals without repeating task-authorization questions.

For card-data refresh/replay or name baselines, use [card-data maintenance](card-data.md). For artwork allowlists, storage or Worker operations, use [artwork operations](artwork.md).

Check the resulting resource/deployment and its commit/branch/version. A familiar hostname or successful local build does not prove which revision is live. Do not intentionally deploy as an incidental consequence of unrelated work. For analytics/SEO-only questions, use existing GSC/Cloudflare capabilities without modifying resources or adding a permanent analytics workflow.
