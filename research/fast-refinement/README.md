# Fast certificate refinement feasibility — research only, not for merge

No application/test-project files are changed. `research.targets` externally
includes the probe/prototype into measurement builds and the exporter/oracle
checks into the test assembly. Never deploy a build made with this include.
Use frozen `inputs/corpus.json` for replay; session files are for inspection.
Local attributes preserve evidence bytes across Git line-ending normalization.
`protocol.md` predates baseline measurement; `design.md` records the single
mechanism chosen after baseline diagnosis. No prototype redesign was made.
The report is `REPORT.md`; exact raw fractions and counters live in `output/`.

From repository root, with .NET 10 and its wasm-tools installed:

```powershell
$probe = "$pwd/research/fast-refinement/research.targets"
dotnet restore YGOProbabilityCalculatorBlazor.sln
dotnet build YGOProbabilityCalculatorBlazor.sln -c Release --no-restore "-p:CustomAfterMicrosoftCommonTargets=$probe"
dotnet build research/fast-refinement/runner/runner.csproj -c Release
dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build --filter 'FullyQualifiedName~RefinementResearchOracle|FullyQualifiedName~TargetSurvivalCertificateTest|FullyQualifiedName~PhysicalSearchTest|FullyQualifiedName~SearchAvailabilityCertificateTest'
$env:DOTNET_TieredCompilation='0'
dotnet research/fast-refinement/runner/bin/Release/net10.0/runner.dll YGOProbabilityCalculatorBlazorTest/bin/Release/net10.0 research/fast-refinement/inputs/corpus.json research/fast-refinement/output/native.json
./research/fast-refinement/cold-native.ps1 -TestBin "$pwd/YGOProbabilityCalculatorBlazorTest/bin/Release/net10.0"
dotnet publish YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj -c Release --no-restore -o <local-publish> -p:RunAOTCompilation=false "-p:CustomAfterMicrosoftCommonTargets=$probe"
# Make Playwright resolvable via NODE_PATH; YGO_BROWSER_EXECUTABLE may select installed Edge.
node research/fast-refinement/wasm.cjs <local-publish>/wwwroot research/fast-refinement/inputs/corpus.json research/fast-refinement/output/wasm.json
node research/fast-refinement/wasm.cjs <local-publish>/wwwroot research/fast-refinement/inputs/corpus.json research/fast-refinement/output/cold-wasm.json cold
node research/fast-refinement/analyze.cjs
```

Run native, browser, tests and publish jobs serially. Windows commands requiring
approved execution follow the root AGENTS.md. The observed restricted Release
build failed without diagnostics; approved execution succeeded. Restricted Edge
launch closed during startup; the approved identical command succeeded.

To regenerate instead of replaying the frozen corpus (do not overwrite evidence):
set `YGO_FAST_CERTIFICATE_FIXTURES` to `<new-input-dir>/fast-export.json`, run
`FastCertificateBenchmark.Export`, then set `YGO_RESEARCH` to that directory and
run `RefinementResearchExport.Export`. These are explicit NUnit filters. Archive
new SHA-256 hashes before measuring. Frozen JSON is authoritative for this study.

The probe retains the unchanged public result and emits hypothetical endpoints
separately. Outer Work includes child spending; child attempted storage peaks
are reported separately because a rejected child storage check must not fail
the already completed request. Checkpoints are charged and checked retained data.
JSON decoding/materialization and serialization/transport are outside timing;
primary, public baseline completion, optional endpoint computation and checkpoint
construction are inside. Measurements do not cover UI paint or a production
integration that does not exist. Native GC is outside timing. Heap observations
and WASM linear-memory capacity are not live-heap peaks.

First observations in the warm suite are not all cold. `cold-native.json` uses
one fresh process per request/mode; `cold-wasm.json` uses new module workers per
request/mode and reports bootstrap separately. Cold observations are singletons,
not cold p50/p90 estimates. Warm quantiles use ten samples after two warmups,
nearest-rank p50/p90, seeded randomized interleaving of five modes.

One initial native calibration used the wrong numeric mode discriminator, so its
"Exact" column actually repeated Fast. It was excluded, the harness corrected
before diagnosis/candidate design, and all valid outputs check physical routes.
This is a measurement repair, not a refinement-design iteration.
