# Repository instructions

Use these instructions when changing this repository. Read [README.md](README.md) for the user-facing description; keep issue work focused on the requested behavior.

## Project map

- The .NET 10 Blazor WebAssembly app is in `YGOProbabilityCalculatorBlazor/`; `YGOProbabilityCalculatorBlazor/Pages/Index.razor` renders the main calculator.
- `YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/ProbabilityCalculatorService.cs` implements the probability calculation.
- `YGOProbabilityCalculatorBlazor/Models/` contains cards, categories, combo constraints, and session state.
- `YGOProbabilityCalculatorBlazor/Components/ProbabilityCalculator/` contains the calculator and its card, category, and combo editors. `YGOProbabilityCalculatorBlazor/Components/ProbabilityCalculator/EditorKeys.cs` supports stable editor identity.
- Import and persistence code lives in `YGOProbabilityCalculatorBlazor/Services/DeckImport/`, `YGOProbabilityCalculatorBlazor/Services/Session/`, and `YGOProbabilityCalculatorBlazor/Services/Converter/`.
- The test project is `YGOProbabilityCalculatorBlazorTest/`. It contains bUnit editor tests and focused service tests.

## Local development and verification

The solution's projects target `.NET 10` (`net10.0`); the README identifies C# 14. Install the .NET 10 SDK. Run commands from the repository root:

```sh
dotnet restore YGOProbabilityCalculatorBlazor.sln
dotnet build YGOProbabilityCalculatorBlazor.sln --no-restore
dotnet test YGOProbabilityCalculatorBlazor.sln --no-build
```

For focused regression runs:

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln --filter "FullyQualifiedName~YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator"
dotnet test YGOProbabilityCalculatorBlazor.sln --filter "FullyQualifiedName~CalculatorEditorTest"
```

Run the app locally with `dotnet run --project YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj`. Coverage collection is available through the test project's `coverlet.collector` package:

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln --collect:"XPlat Code Coverage"
```

## Linting and cleanup tools

Before final verification or publication of C# changes, run the canonical fixer from the repository root (PowerShell 5.1+ on Windows, `pwsh` on Linux):

```powershell
pwsh -NoProfile -File scripts/style/fix.ps1
```

Inspect the formatter output and keep its C# changes. Then run the read-only verifier:

```powershell
pwsh -NoProfile -File scripts/style/check.ps1
```

Publish only when the verifier passes. CI is not a formatting service. Agents are responsible for applying cleanup before publication.

The checker reads the existing source directly. It never invokes CleanupCode or the fixer, copies a source tree, writes transformed source, or applies a non-verifying formatter. Its stages are direct UTF-8/BOM/LF/trailing-whitespace/final-newline checks, SDK-local Roslyn syntax/trivia and `InvocationLayout --check`, then `dotnet format style --verify-no-changes` using the EditorConfig-derived diagnostic allowlist. The helper build does not build the application. Roslyn semantic diagnostics require restored project references: existing app/test assets enable `--no-restore`; a fresh checkout lets `dotnet format` restore its workspace. Only ignored build/restore assets are written. Source hashes and Git status are checked before and after, including on failure.

| Accepted policy | CI verification |
| --- | --- |
| IDE0001–0005, IDE0008, IDE0011, IDE0028, IDE0034, IDE0075, IDE0090, IDE0300–0306 | Roslyn style verify mode: simplifications, explicit types, braces, exact-type collections |
| Ordinary control-flow braces (including using/lock/fixed/do) and logical NOT spacing | Direct syntax/token checks derived from JetBrains EditorConfig settings |
| UTF-8/BOM, LF, trailing whitespace, final newline | Direct byte/text checks |
| Maximum one blank line; blank lines around invocable members/local methods, compound statements and before control transfers | Syntax/trivia checks; standalone scopes and consecutive yields retain canonical exceptions; literal contents are excluded |
| Already wrapped arguments/parameters, declaration opening/closing lines, leading method-chain dots | Direct syntax/trivia checks: one item per line, separate closer, declaration closer alignment |
| Multiline foreach lambda chains | Existing trivia-only `InvocationLayout --check`, including receiver-relative dots and nested/outer closers |
| ReSharper decisions about when line length requires wrapping (`chop_if_long`), generic continuation/argument indentation, and remaining general reformatting | Local fixer only: duplicating the formatter's context-sensitive layout engine would defeat lightweight verification |

The local-only preferences remain in `.editorconfig` and the canonical fixer; CI verifies the explicit invariants above and does not claim complete CleanupCode equivalence. Wrapped single-item expressions, comments and raw-string contents retain their syntax; wrapping selection stays a local preference. Adding an accepted rule requires both an automatic fix proof and an explicit decision about its verification family.

Ordinary PR checks compare `pull_request.base.sha` with `pull_request.head.sha`; pushes compare `event.before` with `GITHUB_SHA`. Deleted files are skipped and renames include their existing destination. Missing/zero SHAs fall back to a full scan without fetching. No C# changes skip verification. Manual dispatch and formatter infrastructure changes check all app/test C# sources.

For changes to `.editorconfig`, `global.json`, `.config/dotnet-tools.json`, `scripts/style/**`, formatter configuration, or style workflow logic, additionally run the local infrastructure regression suite before publication:

```powershell
pwsh -NoProfile -File scripts/style/test.ps1
```

This suite exercises dirty-source detection, canonical fixing and idempotence in a disposable copy. Report its local result in the PR. It is never an ordinary Actions job. The workflow has exactly two independent jobs: C# style verification and Full test suite.

Windows PowerShell users can run `powershell -NoProfile -File scripts/style/fix.ps1`. The fixer restores the pinned SDK-compatible tools/packages, builds to resolve references, applies the narrow JetBrains explicit-type/braces/formatting profile, repeats the explicitly enabled Roslyn style fixes until the C# file hashes stop changing, then restores JetBrains formatting and removes UTF-8 BOMs. `global.json` pins Roslyn's SDK; the tool manifest pins ReSharper. Only noninteractive, safely auto-fixable rules belong in `.editorconfig`; adding a rule requires proving its fix with this command. C# source is the enforced scope; unrelated file formats have no formatter gate.

If the local environment genuinely blocks the fixer because of the documented managed-workspace Roslyn/MSBuild restriction, use the recovery path below and document the exact failure. CI may help diagnose that environment-specific failure, but must not be treated as the normal way to find or apply formatting fixes.

`inspectcode` is optional diagnostic tooling, never an enforcement gate for manual-only findings. Unused parameter/delegate names, repeated enumeration, nullability-based constant conditions, and all static candidates are outside the explicit lint policy. Do not use the default Full Cleanup profile. After an actual managed-workspace MSBuild failure, pass verification-only flags with `-MSBuildArguments` (for example `-m:1`, `-p:UseSharedCompilation=false`, and the temporary task override described below). If this workspace blocks Roslyn's build-host Unix pipe, report the local limitation and review the output of the identical fixer from CI; do not bypass the restriction or invent replacement source rewrites.

At the start of implementation or test work, run `dotnet --info` and `dotnet --list-sdks` before substantial work. If no usable .NET 10 SDK is available, follow the restricted Linux / ChatGPT Work bootstrap below before continuing. Missing .NET 10 is not, by itself, sufficient reason to skip local verification; attempt the documented nonprivileged bootstrap first. Only report .NET verification as blocked after that attempt fails because of a real environment restriction, and include the exact failed command and error. Check CLI Git credentials early when a task needs a command-line push or rebase; GitHub plugin access does not imply terminal Git authentication. Never expose tokens or ask for secrets, and do not claim tests that could not run.

`.github/workflows/tests.yml` runs PR checks for pull requests targeting `dev`, push checks on `dev` and `main`, and supports manual dispatch. Feature branches do not receive a second push-triggered run. Style verification and the full functional test suite are independent jobs and can run in parallel; the functional job preserves the normal restore, build, .NET tests, and JavaScript tests. CI complements rather than replaces local verification: run relevant tests locally before pushing and report the exact local commands and results in pull requests. Only if the local environment reports MSBuild parallel-node or reuse errors, retry the affected command with `-m:1` and report that workaround; serial builds are not a general requirement.


### Restricted Linux and ChatGPT Work: .NET 10 SDK bootstrap

Run `dotnet --info` and `dotnet --list-sdks` first. Use the exact SDK pinned in `global.json`. If `dotnet` or that SDK is missing, attempt this nonprivileged bootstrap before giving up:

```sh
mkdir -p /tmp/dotnet10-sdk
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
YGO_SDK_VERSION=$(node -p "require('./global.json').sdk.version")
TAR_OPTIONS=--no-same-owner bash /tmp/dotnet-install.sh --version "$YGO_SDK_VERSION" --install-dir /tmp/dotnet10-sdk --no-path

export DOTNET_ROOT=/tmp/dotnet10-sdk
export PATH="$DOTNET_ROOT:$PATH"

dotnet --info
dotnet --list-sdks
```

The `TAR_OPTIONS` setting prevents GNU tar from restoring archive owners, which can fail in restricted filesystems. You may also invoke `/tmp/dotnet10-sdk/dotnet` directly. After confirming a 10.x SDK, run the normal restore, build, and test commands above. If the download is blocked or execution is prohibited, report the exact bootstrap command and error; do not claim .NET verification was blocked without attempting the bootstrap.


### Restricted sandbox: WebAssembly task-host failures

This is a separate build-time issue, unrelated to a missing SDK. After obtaining a usable .NET 10 SDK, run the normal restore, build, and test commands above first; a successful restore alone does not prove the app or tests build. Use the `-m:1`/node-reuse workaround above only if the relevant MSBuild parallel-node or reuse error occurs. If a build or test fails with `MSB4216`/`MSB4027` or `SocketException (13): Permission denied` while MSBuild starts an out-of-process task host, and a socket probe confirms Unix sockets are blocked while loopback TCP works, use the following verification-only workaround rather than declaring the suite un-runnable:

- Use writable temporary `DOTNET_CLI_HOME` and `NUGET_PACKAGES` locations.
- Disable MSBuild/node reuse and compiler/Razor build servers.
- Create a temporary MSBuild targets file **outside the repository** that re-registers the unchanged official WebAssembly and ILLink tasks with the default in-process task factory by using `<UsingTask ... AssemblyFile="..." Override="true" />`.
- For the current .NET 10 toolchain, the affected tasks are `GenerateWasmBootJson`, `ComputeWasmBuildAssets`, `ComputeWasmPublishAssets`, `ConvertDllsToWebCil`, `ComputeManagedAssemblies`, and `ILLink`. Resolve their DLL paths from the actually restored NuGet packages; do not hard-code an old package version.
- Pass the temporary file with `-p:CustomBeforeMicrosoftCommonTargets=<path>` to build/publish. This must only change task process placement; do not skip targets or replace task assemblies.
- After a successful build, run the normal full test suite with `--no-build`. For publish verification, inspect diagnostic logs to confirm the WebAssembly/ILLink tasks actually executed.
- Never commit the temporary override file or sandbox-specific paths. Cloudflare/local builds do not require this workaround.

The .NET 10 migration PR #46 contains one verified example of this workaround and the exact commands used. Prefer reproducing the technique with the currently restored SDK/package versions over copying its temporary paths verbatim.

## Probability correctness

- A card may belong to multiple categories. Each copy counts toward every matching hand-wide maximum, but can fill only one positive requirement within a combo. Positive category and direct-card minima need distinct physical copies; separate combos are evaluated independently.
- Each combo requires all of its category constraints; success means at least one combo matches. Preserve inclusive minimum and maximum bounds and count overlapping combos only once.
- A `0/0` constraint is valid. Do not clamp, discard, or silently change valid constraints.
- When changing calculation behavior, add targeted cases to `YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/`. Prefer the existing `YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/SmallDeckOracleTest.cs`, which enumerates physical hands and evaluates constraints independently of the production aggregation algorithm.
- Keep oracle expectations independent of the implementation under test. Verify expected values with exhaustive enumeration or another actual calculation tool; never select numbers by intuition or memory. Cover overlapping memberships/combos and boundary constraints when relevant.

## Editor state and compatibility

- Preserve each card and combo editor's draft and active state through edits, list removal/reordering, session loading, and deck import. The list editors use `EditorKeys<T>` and Blazor `@key` to carry UI identity across model replacement; preserve this behavior.
- Do not overwrite an in-progress draft when a parent value changes. In particular, hand-size changes may update untouched defaults but must not alter user-entered or saved constraints.
- Add bUnit interaction tests in `YGOProbabilityCalculatorBlazorTest/Components/CalculatorEditorTest.cs` for editor behavior changes.
- For user-facing component changes, preserve established density, spacing, colors, badge/counter presentation, and icon conventions unless a redesign is requested. Avoid redundant labels or extra detail in compact controls; icon-only controls need accessible names, consistent hover titles/tooltips, keyboard access, and reasonable click targets. Add focused bUnit interaction tests when useful and inspect rendered UI or a preview at practical viewport sizes when tooling permits; report visual-check limits separately from test results.
- Preserve existing saved-session JSON and `.ydk` import behavior unless the issue explicitly changes it. For persistence or import changes, add focused coverage under `YGOProbabilityCalculatorBlazorTest/Services/Session/`, `YGOProbabilityCalculatorBlazorTest/Services/Converter/`, or `YGOProbabilityCalculatorBlazorTest/Services/DeckImport/` as appropriate.

## Contribution workflow and boundaries

### Managed worktrees and publication access

- Managed/cloud worktrees can report Git ownership or safe-directory errors. Apply `-c safe.directory=<repo>` to the individual Git command; do not change global Git configuration. For a privacy/publication guard that invokes Git internally, pass the equivalent setting to that process with Git's `GIT_CONFIG_COUNT`, `GIT_CONFIG_KEY_0`, and `GIT_CONFIG_VALUE_0` variables.
- Check CLI Git credentials and available authenticated GitHub integration/API publication paths early when a task requires publication. A missing GitHub CLI or this setup issue alone does not block implementation. Use an authenticated integration only when that specific remote write is authorized, and verify the published commit and tree.

### Publication privacy

- Before an owner-authored commit, verify the raw resolved author and committer with `node scripts/codex/privacy-guard.mjs check`. The owner's approved public name is `SeyTi01`; obtain the issued GitHub noreply address from authenticated **Settings → Emails**, never from profile, billing, machine identity, or an inferred numerical ID.
- Windows Codex setup installs the shared local publication hooks. A fresh clone starts with a blocking, unconfigured policy; configure your explicitly approved public identity using the [guard setup instructions](scripts/codex/PRIVACY.md). Other contributors and forks must use their own approved identity, never silently adopt the owner's identity. Keep existing hooks and their stdin behavior intact; do not bypass guards with `--no-verify` or disabled hooks.
- Before pushing or making an API write, inspect every newly outgoing raw author/committer, identity trailer, annotated tagger, message, path and changed content. Local config does not configure API commits. Use explicit approved API identity controls or the verified local Git path; do not publish an object to probe API defaults. Inspect incoming author metadata before web/API merge operations.
- Never derive public hostnames, resources, documentation or screenshots from personal names, emails, machine profiles or billing information. Inspect text and images separately: these metadata hooks cannot detect every identifying string or screenshot.
- These preemptive checks validate new publication. Changes to existing published history require separate authorization.

- Start from an up-to-date `dev` branch. Read the issue, relevant dependencies, applicable agent instructions, and recent code before editing.
- Before finalizing a pull request, fetch the latest `origin/dev` and check recent or concurrent changes for overlap. Rebase the feature branch onto `origin/dev`, resolve conflicts without losing behavior, then rerun affected tests and the full suite when feasible. Do not use `git merge dev`, `git merge origin/dev`, or an implicit `git pull` that creates an upstream-integration merge commit; keep the PR history linear for GitHub's Rebase and merge workflow.
- Before the first push, review local commits. If an unpushed commit merely corrects or refines an earlier unpushed commit for the same logical change, amend or squash them into one coherent commit. Keep independently meaningful changes in separate commits, grouped by purpose rather than by pull request or file.
  Never amend or squash already-pushed commits, or rewrite published history for cosmetic cleanup. Do not rebase or rewrite a published or shared feature branch without explicit user authorization. If integration needs a rebase but is not authorized, report the need and request authorization. For an authorized rebase, record the expected remote SHA and use an explicit `--force-with-lease`.
- An authenticated GitHub plugin may be used as a push fallback only when that specific remote operation is authorized. A forced plugin ref update is not an atomic Git lease: recheck the exact remote SHA immediately before updating, verify the intended commit and tree were published, and abort if the remote changed unexpectedly. Never use unguarded CLI force or infer CLI Git authentication from plugin access.
- Keep each change and pull request focused; add the most relevant regression tests and run the solution-level checks for substantial changes.
- Create a feature branch and open a pull request targeting `dev`. Never push directly to protected branches or merge a pull request.
- In the pull request, report commands and pass/fail results, manual checks, and any limits on verification. Put verification results in the pull request discussion, not in an extra report file.
- Do not modify GitHub Actions workflows unless the task explicitly requires it. Do not change Cloudflare deployment or branch policies, upgrade frameworks/dependencies, perform broad refactors, or add unrelated documentation unless explicitly requested. Do not intentionally trigger deployments or modify releases without authorization.
- Do not claim that the hosted site represents a particular branch unless you have verified it.
