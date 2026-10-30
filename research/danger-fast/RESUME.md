# Danger Fast research checkpoint

## Objective and contract

Produce deterministic, mathematically enclosing Fast intervals for the existing fixed-policy, base-only Danger model, with Exact preserved. Initial experimental goals: <=2 percentage-point full width for total/principal groups and about five seconds published stripped-AOT worker latency on frozen heavy 40/60 inputs. These are research goals, not achieved results or release thresholds. No sampling, changed model, raised budgets, or fabricated primary fraction.

## Recoverable baseline and ownership (2026-10-30)

- PR #317 is open/draft; issue #215 retains the base-only boundary.
- Starting remote feature SHA: `bd75460e4bb3d9f990d8bc04bd1fcd915bcabc71`.
- Starting dev/workspace SHA: `a2afd969cdf5479b8d653bd3a1ab0104b439c487`; dev is an ancestor of feature.
- Local backup ref: `codex/backup-danger-fast-start` at the feature baseline.
- Active branch: `codex/issue-215-danger-base-effects`, in the assigned workspace; lead owns integration and Git operations. No extra worktree.
- Last verified pushed SHA: `bd75460e4bb3d9f990d8bc04bd1fcd915bcabc71`. Guarded local Git fetch and dry-run push succeeded. GitHub connector read access verified. `gh` is unavailable; not required.
- Existing publication policy validates resolved author/committer. Do not reconfigure identity or bypass hooks.

## Established facts / verification

- Workspace was clean before checkout; no unrelated edits.
- Read repository README, AGENTS, privacy policy, live PR metadata/discussion and issue #215.
- `dotnet --info` (approved execution): SDK 10.0.401, installed wasm-tools workload; `dotnet --list-sdks` (restricted) confirms .NET 10.
- No implementation tests, benchmarks or AOT publish run in this continuation yet.
- Historical heavy Work/runtime values in PR are not new measurements.

## Research status

No method selected; no enclosure proof or new performance claim yet. Independent investigations will write separate persistent notes; only the lead integrates production code. Freeze inputs/references before tuning. Persist each measurement separately; never depend on the existing all-at-end aggregate writer.

## Next concrete step

Freeze MeasureDanger fixture construction and six fractions without running heavy Exact. Inspect transition/state/result contracts. Investigate aggregate partial exploration and semantic state reduction independently; review the CP 2024 primary paper. Checkpoint before expensive work and at most 10–15 minutes apart. After every push, verify remote SHA/tree and record it at the next checkpoint (a document cannot contain its own commit SHA).
