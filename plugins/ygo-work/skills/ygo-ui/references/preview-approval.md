# Styling preview approval

Applies to visible styling changes (layout, spacing, colors, typography, icons or component presentation), including mixed changes and existing-PR updates. Other changes follow normal delivery.

Before CLI push or equivalent GitHub API publication:

1. Prepare evidence for the actual host:
   - **Codex:** provide a [testable localhost preview](preview-server.md), preferably in the in-app browser.
   - **Web/cloud:** present screenshots of the current rendered UI at relevant states/viewports, with a concise change description. Do not substitute mockups or an older/deployed revision; a capture server need not be user-accessible.
2. Ask for explicit approval to push the previewed result. Explain this skill's requirement, link this reference and quote: **"Only publish after the user explicitly approves the previewed styling."**
3. Wait. General implementation/PR authorization, passing checks, elapsed time and silence do not satisfy this gate.
4. After visual feedback, refresh the preview/screenshots and obtain approval of revised styling. Approval remains valid for changes that leave the approved visual result unchanged. Then continue authorized delivery.

If the required preview or screenshots cannot be prepared, report the concrete blocker and keep publication pending until the evidence is available or the user explicitly changes this requirement.
