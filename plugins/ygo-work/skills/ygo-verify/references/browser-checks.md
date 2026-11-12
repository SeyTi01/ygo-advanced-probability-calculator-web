# Published browser checks

Prepare [the browser runtime](browser-runtime.md), then select checks for the changed contract. From the repository root:

| Scope | Command |
| --- | --- |
| Routine published integration set | `node scripts/smoke.cjs <published-wwwroot>` |
| Physical idle-owner reuse, expiry, recreation and Help deactivation | `node scripts/background-calculation/lifecycle-smoke.cjs <published-wwwroot>` |
| Focused localization | `node --test scripts/localization/locale.test.mjs`, then `node scripts/localization/browser-smoke.cjs <published-wwwroot>` |

The serial smoke set fails on the first failed child and covers production calculation modes, editor/workspace interactions, session loading and localization. Use existing child entry points for narrower risks; `repo:scripts/background-calculation/README.md` owns their coverage and limitations. The optional idle-owner check waits for the production 60-second timer; do not add expensive matrices to every push.

Localization changes also need the relevant .NET/editor tests and [published SEO checks](seo-checks.md) against the same output. Pseudo-resource checks remain in regular tests. Browser checks do not replace native/JavaScript regression coverage.
