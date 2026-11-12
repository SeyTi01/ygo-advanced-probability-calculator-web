# Styling preview approval

This gate applies when the change affects visible UI styling, including layout, spacing, colors, typography, icons or component presentation. It also applies to mixed changes containing styling work and to updates of an existing PR. Changes without a styling impact retain their normal workflow.

Before publishing the affected changes:

1. Prepare the review evidence for the actual host, using the appropriate [environment](../../ygo-verify/references/environment.md):
   - **Codex:** start a testable localhost server serving the current changes. Prefer opening it in the in-app browser when available; otherwise provide the localhost URL in a usable browser. Verify that the page finishes loading and the affected UI can be exercised. Keep the server available while the user reviews it.
   - **Web/cloud:** render the current changes and capture screenshots of the affected UI, including relevant states and practical viewports. Present the screenshots to the user before publishing; include a concise description of what changed. Use actual rendered output, not a mockup or an older/deployed revision. A local server may support capture, but the user reviews screenshots rather than needing access to that server.
2. Give the user the host-appropriate preview and ask for their explicit okay to push the previewed result. Explain that this skill requires approval before publishing styling changes, linking this reference and quoting: **"Only publish after the user explicitly approves the previewed styling."**
3. Wait for that approval. General implementation/PR authorization, passing checks, elapsed time or silence do not satisfy this gate. It covers both CLI pushes and equivalent GitHub API publication; do not publish the changes through another route while waiting.
4. Apply requested feedback, refresh the localhost preview or screenshots, and obtain approval of the revised styling before publication. Existing approval remains valid for changes that do not alter the approved visual result. Once approved, continue the authorized delivery workflow.

If the required preview or screenshots cannot be prepared, report the concrete blocker and keep publication pending until the evidence is available or the user explicitly changes this requirement.
