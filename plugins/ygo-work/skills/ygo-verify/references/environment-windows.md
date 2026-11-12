# Initialized Windows Codex

This repository's Windows Codex environment normally initializes through `repo:scripts/codex/setup.ps1`. Reuse the assigned workspace, supplied default identity, installed guard/hooks, SDK and restored dependencies. Do not rerun setup for every skill or infer initialization merely from the word Codex. If a prerequisite is missing, repair that prerequisite; privacy setup is documented in `repo:scripts/codex/PRIVACY.md`.

In the known restricted Windows environment, request scoped approved execution from the outset for Git metadata writes (fetch/branch/checkout/staging/commit/rebase), setup, `dotnet --info`, restore/test/run, builds that restore, and all local preview-server launches. Do not repeat restricted attempts just to rediscover known failures. Ordinary source edits, read-only Git, privacy checks, SDK listing and `dotnet build --no-restore` can run restricted.

Even `dotnet run --no-build --no-restore` needs approved execution here. A sandboxed server may report listening while host browsers time out. Restart it with approved execution on the same port and verify browser loading. This requirement applies to each launch. Reinvestigate when approved execution fails or the environment changes.

Verify any additional worktree lies within writable permissions. Preserve global configuration, ACLs, permission profiles and existing publication hooks. If approval execution is unavailable, report that limitation rather than bypassing it.
