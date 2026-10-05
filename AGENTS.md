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

Restore the pinned JetBrains tools with `dotnet tool restore`. `inspectcode` is the non-mutating inspection command. Write its SARIF output outside the repository:

```powershell
dotnet tool run jb -- inspectcode YGOProbabilityCalculatorBlazor.sln -o="$env:TEMP\ygo-inspections.sarif"
```

```sh
dotnet tool run jb -- inspectcode YGOProbabilityCalculatorBlazor.sln -o="${TMPDIR:-/tmp}/ygo-inspections.sarif"
```

`cleanupcode` and `dotnet format style` modify tracked source. Run them only when the task explicitly calls for cleanup/formatting, or in a disposable worktree/copy for validation. Style cleanup is not enforced in CI yet. Keep generated SARIF, logs, and profiler output outside the repository; do not commit them.

For an explicitly requested cleanup, use both stages: JetBrains CleanupCode for ReSharper formatting and syntax style, then Roslyn style fixes at suggestion/Info severity:

```sh
dotnet tool restore
dotnet tool run jb -- cleanupcode YGOProbabilityCalculatorBlazor.sln --profile="Built-in: Reformat & Apply Syntax Style"
dotnet format style YGOProbabilityCalculatorBlazor.sln --severity info --no-restore
```

Review the diff after both commands. Do not use CleanupCode's default Full Cleanup profile.

CA1822 and CA1851 are suggestion-level review findings. Keep them out of a bulk `dotnet format analyzers` cleanup: its CA1822 fix can make public instance members static, while CA1851 has no built-in code fix. Before changing a non-private member to static, check interface and virtual contracts, reflection/API compatibility, and instance call sites. For CA1851, materialize once only when repeated enumeration is unintended and caching preserves the intended sequence behavior.

For a required but unused lambda or delegate parameter, retain the signature slot and rename the parameter to `_` after checking the delegate contract. Do not remove or change a required parameter to silence IDE0060 or Rider's unused-parameter inspection.

At the start of implementation or test work, run `dotnet --info` and `dotnet --list-sdks` before substantial work. If no usable .NET 10 SDK is available, follow the restricted Linux / ChatGPT Work bootstrap below before continuing. Missing .NET 10 is not, by itself, sufficient reason to skip local verification; attempt the documented nonprivileged bootstrap first. Only report .NET verification as blocked after that attempt fails because of a real environment restriction, and include the exact failed command and error. Check CLI Git credentials early when a task needs a command-line push or rebase; GitHub plugin access does not imply terminal Git authentication. Never expose tokens or ask for secrets, and do not claim tests that could not run.

`.github/workflows/tests.yml` runs the full regular test suite on every branch push, including feature branches, `dev`, and `main`. CI complements rather than replaces local verification: run relevant tests locally before pushing and report the exact local commands and results in pull requests. Only if the local environment reports MSBuild parallel-node or reuse errors, retry the affected command with `-m:1` and report that workaround; serial builds are not a general requirement.


### Restricted Linux and ChatGPT Work: .NET 10 SDK bootstrap

Run `dotnet --info` and `dotnet --list-sdks` first. If a usable 10.x SDK is listed, use it normally. If `dotnet` is missing or no 10.x SDK is installed, attempt this nonprivileged bootstrap before giving up:

```sh
mkdir -p /tmp/dotnet10-sdk
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
TAR_OPTIONS=--no-same-owner bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /tmp/dotnet10-sdk --no-path

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
