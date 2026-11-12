# Initialize missing Git identity protection

Use only when local commits need a missing or unconfigured guard. Node.js 18+ and Git are required. Reuse an existing policy rather than reinstalling it for each task.

Choose the approved public identity for this checkout:

- In the owner's Codex environment, use the supplied default identity.
- For the owner's web/cloud work, obtain the established public name (`SeyTi01`) and exact GitHub noreply email from existing owner-authored commits, not an arbitrary latest contributor/bot commit or account-profile lookup.
- Contributors and forks use their own approved public identity, never the owner's by default.

From the repository root:

```sh
node scripts/codex/privacy-guard.mjs install --name PUBLIC_LOGIN --email ISSUED_NOREPLY_ADDRESS
node scripts/codex/privacy-guard.mjs check
```

Use the established pair, never personal addresses to make a rejection pass. Explicitly review additional public contributor/bot identities before adding them to the local policy; do not infer, broadly exempt or silently rewrite them. Rerunning setup preserves reviewed entries.

If a custom `core.hooksPath` or hook manager blocks installation, integrate the validator into that manager explicitly; do not disable existing hooks. See `repo:scripts/codex/PRIVACY.md` for policy format, installation behavior and CLI options.
