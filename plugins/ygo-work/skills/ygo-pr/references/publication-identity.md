# Publication identity

Before local commits, reuse the installed guard; read [Git identity setup](git-identity-setup.md) only if it is missing or unconfigured. Check the resolved author and committer:

```sh
node scripts/codex/privacy-guard.mjs check
```

If the check fails, inspect worktree, environment, conditional-include and author/committer overrides. Fix the default rather than hiding it with per-command attribution. Never bypass guards with `--no-verify` or disabled hooks.

Before pushes or API publication, inspect all newly outgoing raw authors/committers, identity trailers, annotated taggers, messages, paths and changed content. Apply [public-content review](public-content.md). The guard's publication boundary and hook behavior are documented in `repo:scripts/codex/PRIVACY.md`; if inspection lacks advertised objects or complete history, fetch the remote's branches/tags and missing history before retrying. These checks do not authorize rewriting published history.

Verify published identity metadata. Before authorized merges, inspect incoming identities; GitHub rebase/merge does not sanitize their authors.
