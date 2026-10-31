# Repository instructions

Read [README.md](README.md) for the user-facing description. Keep changes focused on the requested behavior.

## Project map

Both projects target .NET 10 (`net10.0`); the app uses Blazor WebAssembly / C# 14. App paths below are relative to `YGOProbabilityCalculatorBlazor/`:

- `Pages/Index.razor`: main calculator.
- `Services/ProbabilityCalculator/ProbabilityCalculatorService.cs`: probability engine.
- `Models/`: cards, categories, combo constraints, sessions.
- `Components/ProbabilityCalculator/`: calculator and card/category/combo editors; `EditorKeys.cs` preserves editor identity.
- `Services/DeckImport/`, `Services/Session/`, `Services/Converter/`: import and persistence.
- `YGOProbabilityCalculatorBlazorTest/` (repository root): bUnit editor and focused service tests.

## Local development and verification

Before implementation or test work, run `dotnet --info` and `dotnet --list-sdks`; use the environment's required execution mode below. Install a usable .NET 10 SDK if missing. On restricted Linux / ChatGPT Work, **read and follow [RESTRICTED-LINUX.md](scripts/codex/RESTRICTED-LINUX.md)** before reporting a missing SDK or WebAssembly task-host failure as blocked: the nonprivileged SDK bootstrap is mandatory when .NET 10 is missing; the task-host workaround requires its documented error and socket-probe conditions. Report exact failed commands/errors and never claim unrun tests.

From the repository root:

```sh
dotnet restore YGOProbabilityCalculatorBlazor.sln
dotnet build YGOProbabilityCalculatorBlazor.sln --no-restore
dotnet test YGOProbabilityCalculatorBlazor.sln --no-build
```

For focused tests, add `--filter "FullyQualifiedName~YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator"` or `--filter "FullyQualifiedName~CalculatorEditorTest"` to `dotnet test YGOProbabilityCalculatorBlazor.sln`. Coverage: add `--collect:"XPlat Code Coverage"` (the test project uses `coverlet.collector`). Run the app with `dotnet run --project YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj`.

`.github/workflows/tests.yml` runs the full regular suite on every branch push, including feature branches, `dev`, and `main`. CI complements local verification; run relevant tests before pushing. Retry with `-m:1` only after MSBuild parallel-node/reuse errors and report the workaround; serial builds are not the default.

### Test maintenance

- Keep a test when it catches a meaningful defect at the simplest reliable layer. Preserve independent probability oracles, compatibility, user state, accessibility and calculation ownership; remove redundant tests and their unused helpers rather than skipping them.
- Treat explanatory prose and incidental styling as editable content. Test the selected data/source, accessible associations and consequential usability instead of full help sentences, rejected old wording, exact CSS lists or subpixel layout matrices. Exact numerical formatting and protocol/serialization contracts remain valid assertions.
- Await terminal results without requiring calculations to stay busy. Control completion explicitly for cancellation, stale replies and progressive transitions; keep a compact optimized-published-WASM integration check. Keep opt-in measurements separate from correctness gates.

### Windows Codex execution requirements

For Windows Codex tasks using the verified restricted environment:

- Use the task's assigned workspace. Before using an additional worktree,
  verify that its path is covered by the task's writable permissions.
- Request scoped approved execution from the outset for:
  - Git commands that write shared repository metadata, including fetch,
    branch creation, checkout, staging, commits, and rebases.
  - `scripts/codex/setup.ps1`.
  - `dotnet --info`, `dotnet restore`, and `dotnet test`.
- These operations have already encountered sandbox restrictions in this
  environment. Do not repeat a restricted attempt merely to reproduce the
  known failure before requesting approval.
- Ordinary source edits, read-only Git inspection, the privacy guard check,
  `dotnet --list-sdks`, and `dotnet build --no-restore` can run restricted.
  A build that implicitly restores should use approved execution.
- Use the supported approval mechanism for each required operation.
  These instructions do not bypass approval or authorize operations outside
  the user's task. If approved execution is unavailable, report that directly.
- Repository setup may install or refresh the documented publication hooks,
  preserving the existing approved identity and policy. Do not change global
  configuration, ACLs, or permission profiles, or bypass publication guards.
- Reinvestigate only when an approved command fails or the execution environment changes.

### Release WebAssembly publishing

Install SDK-scoped `wasm-tools` for the exact publishing SDK, as GitHub Actions and `YGOProbabilityCalculatorBlazor/build.sh` do. Release publishing uses Mono WebAssembly AOT compilation with stripped IL; Debug builds remain non-AOT.

## C# style and readability

Applies to C# and Razor C#, including `@code`; preserve markup/layout during readability work.

- Brace ordinary control flow; put opening braces on declaration/control lines. Keep normal `!condition` spacing.
- Use one blank line between logical blocks/methods, after fields, and before terminal flow when helpful. Keep related declarations compact; avoid multiple blank lines.
- Use explicit local/loop types; reserve `var` for anonymous or impractical-to-name types. Use target-typed `new()` with explicit declared types.
- Name only private instance fields `_camelCase`.
- Name dense conditions with meaningful booleans; prefer switch expressions or local steps to nested ternaries.
- Line length is advisory. Short readable declarations, calls, expression-bodied members, and incidental guard/storage/log calls may stay compact, even slightly over page width.
- When declarations genuinely need wrapping (including constructors, primary constructors, local functions), use one parameter per line and a separate `) {` line aligned with the declaration. For significant wrapped calls, use one argument per line and a separate `)` line; avoid partial wrapping.
- Use descriptive lambda names and readable fluent stages; keep obvious short expressions and conventional numeric indices compact.
- Use modern syntax only when clearer and behavior-preserving. Preserve defensive checks, collection order, exception meaning, serialization, and test expectations.
- Both projects use `Nullable=annotations`: nullable contracts remain; flow warnings are disabled, other diagnostics retained.
- Human judgment governs style. Never automatically run CleanupCode, `dotnet format`, or analyzer quick fixes for cleanup; targeted non-mutating diagnostics are allowed.
- Before analyzer-driven `static`, accessibility, nullability, or defensive-state changes, verify call sites and framework/serialization/DI/build contracts. Never remove defensive validation to satisfy analysis.

## Probability correctness

- Cards may belong to multiple categories. Each physical copy counts toward every matching hand-wide maximum, but fills only one positive requirement per combo. Category/direct-card minima require distinct copies; combos are evaluated independently.
- All category constraints within a combo must match; success is the union of matching combos. Preserve inclusive bounds, count overlaps once, and keep valid `0/0` constraints without clamping/discarding/changing them.
- Calculation changes need targeted tests under `YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/`. Prefer `SmallDeckOracleTest.cs`, which independently enumerates physical hands. Verify expectations by exhaustive enumeration or actual calculation, never intuition/memory; cover relevant overlapping memberships/combos and boundaries.

## Editor state and compatibility

- Preserve drafts and active state through edits, removal/reordering, session loads, and deck imports. Preserve `EditorKeys<T>` and `@key` identity across model replacement.
- Parent changes must not overwrite drafts. Hand-size changes may update untouched defaults, never user-entered/saved constraints.
- Editor behavior changes need bUnit interaction tests in `YGOProbabilityCalculatorBlazorTest/Components/CalculatorEditorTest.cs`; add focused tests for other component behavior changes when useful.
- Unless redesign is requested, preserve UI density, spacing, colors, badges/counters, and icon conventions. Avoid redundant labels/detail in compact controls. Icon-only controls need accessible names, consistent hover titles/tooltips, keyboard access, and reasonable click targets. Inspect rendered UI/previews at practical viewport sizes when possible; report visual limits separately from tests.
- Preserve session JSON and `.ydk` behavior unless explicitly changed. Persistence/import changes need focused tests under the test project's `Services/Session/`, `Services/Converter/`, or `Services/DeckImport/`.

## Contribution workflow and boundaries

### Workspace and publication access

- For Git ownership errors, use command-scoped `-c safe.directory=<repo>`, never global config. For guards invoking Git, use equivalent `GIT_CONFIG_COUNT`, `GIT_CONFIG_KEY_0`, and `GIT_CONFIG_VALUE_0` environment settings.
- Check CLI Git credentials and authenticated GitHub integration/API paths early for publication tasks. Plugin access does not imply CLI authentication; missing CLI/auth setup does not block implementation. Never expose tokens or request secrets. Integration writes require task authorization and published commit/tree verification.

### Publication privacy

- Before owner-authored commits, run `node scripts/codex/privacy-guard.mjs check` on resolved author/committer. In Codex, use the provided default identity; in web sessions, use the identity from existing commits.
- Windows setup installs shared publication hooks; fresh clones block until configured. Follow [PRIVACY.md](scripts/codex/PRIVACY.md) with the established public identity. Contributors/forks must use their own identity. Preserve existing hooks/stdin; never bypass guards with `--no-verify` or disabled hooks.
- Before pushes/API writes, inspect all newly outgoing raw authors/committers, identity trailers, annotated taggers, messages, paths, and changed content. Local config does not set API identities: use explicit approved API controls or guarded local Git; never publish objects to probe defaults. Inspect incoming author metadata before web/API merges.
- Never derive public hostnames/resources/docs/screenshots from personal names/emails, machine profiles, or billing data. Inspect text and images separately; hooks cannot detect all identifying content.
- These preemptive checks validate new publication. Changes to existing published history require separate authorization except for the task-branch rebases authorized below; rebased commits still require all publication checks.

### Linear PR delivery

- Read the issue/task brief, dependencies, applicable agent instructions, and recent code before editing. Branch from current `origin/dev`; open a PR to `dev`. Never push to protected branches or merge PRs.
- Fetch the latest `origin/dev` before finalizing a pull request and check recent or concurrent changes for overlap. Whenever the task's feature branch is behind `origin/dev`, automatically rebase it and resolve conflicts, preserving the intended behavior of both upstream and task changes. This is standing user authorization, including for already-published or shared feature branches being worked on for the task; do not ask for rebase or routine conflict-resolution confirmation. Rerun affected tests and the full suite when feasible. Do not use `git merge dev`, `git merge origin/dev`, or an implicit `git pull` that creates an upstream-integration merge commit; keep the PR history linear for GitHub's Rebase and merge workflow.
- Before the first push, review local commits. If an unpushed commit merely corrects or refines an earlier unpushed commit for the same logical change, amend or squash them into one coherent commit. Keep independently meaningful changes in separate commits, grouped by purpose rather than by pull request or file.
  Never amend or squash already-pushed commits, or rewrite published history for cosmetic cleanup.
- Before rebasing a published task branch, record its exact remote SHA and preserve the old head in a local backup ref. Publish the rebase with explicit `--force-with-lease=<ref>:<expected-sha>` and verify the remote commit and tree. If the lease fails, fetch and reconcile concurrent changes before retrying; never overwrite them with unguarded force. This authorization does not extend to unrelated branches or protected branches.
- An authenticated GitHub plugin may be used as a push fallback only when that specific remote operation is authorized. A forced plugin ref update is not an atomic Git lease: recheck the exact remote SHA immediately before updating, verify the intended commit and tree were published, and abort if the remote changed unexpectedly. Never use unguarded CLI force or infer CLI Git authentication from plugin access.
- Keep changes/PRs focused; add relevant regression tests above and run solution checks for substantial changes. Omit routine verification results, including tests, CI, and privacy checks, from PRs and issues; report only verification blocked and not reconciled.
- Unless explicitly requested, do not change GitHub Actions workflows, Cloudflare deployment/branch policies, frameworks/dependencies, or unrelated docs, or perform broad refactors. Do not intentionally trigger deployments or modify releases without authorization.
- Verify which branch a hosted site represents before claiming it.
