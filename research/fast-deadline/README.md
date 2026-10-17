# Fast automatic Exact experiment

Baseline: 63a4780fb3fa937907c1c8be4662550197179f27. This branch contains research inputs and evidence only. Production integration is delivered separately.

Read protocol.md, protocol-amendment.md, then policy-correction.md in that order. The first is the original frozen experiment; the second records the user's clarification that initial Fast must never be timed out automatically; the third records the one focused correction from a six-second to a seven-second combined opportunity. The maximum additional Exact attempt remains five seconds. Original hashes and timestamps are in inputs/*freeze.json. The primary 37-case corpus was frozen before measurement. Six supplemental cases were declared after primary first observations to validate the 3-5 second execution band, before their results were known. They are supplemental validation, not an independent held-out performance estimate. No structural classifier or fitted score is used.

The VS/K9 inputs are maintained equivalents recovered from published research #225, not the user's exact saved session. Hypothetical draw overrides and grouped-filler correctness controls are labeled in the corpus. All ordinary physical-card variants retain normal multiplicities. Work is 150M and Storage Standard except the explicitly low-Work diagnostic.

## Reproduction

Use .NET SDK 10.0.401 with its wasm-tools workload, Node.js, Playwright, and a local Edge executable supplied through YGO_BROWSER_EXECUTABLE. Run heavy builds/tests and browser timing sequentially. The scripts host only localhost and use isolated headless browser contexts.

On this research baseline branch:

~~~powershell
$target = Join-Path (Get-Location) 'research/fast-deadline/benchmark.targets'
dotnet restore YGOProbabilityCalculatorBlazor.sln
dotnet publish YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj -c Release --no-restore -p:CustomAfterMicrosoftCommonTargets=$target -p:RunAOTCompilation=false -o research/fast-deadline/bin/baseline
$env:MODES = 'fast,exact'
node research/fast-deadline/wasm.cjs research/fast-deadline/bin/baseline/wwwroot research/fast-deadline/output/baseline.json
$env:FIXTURES = 'research/fast-deadline/inputs/supplemental.json'
node research/fast-deadline/wasm.cjs research/fast-deadline/bin/baseline/wwwroot research/fast-deadline/output/supplemental.json
~~~

The external Compile include supplies probe.cs and ADAPTIVE_FAST only to the benchmark app. Never use benchmark.targets for a production publish. Native.csproj invokes the same measurement method in the baseline build's native assembly; run it before replacing that assembly with the candidate build.

On the implementation branch, use a normal Release publish with RunAOTCompilation=false and no CustomAfterMicrosoftCommonTargets. Point production.cjs at its wwwroot and inputs/all.json. Point scripts/background-calculation/fast-deadline-smoke.cjs at that publish and inputs/smoke.json. These two scripts use the real production owner, exports and C# wire; no measurement export is compiled into that app.

analyze.cjs compares deterministic reference results and rational interval containment. analyze-native.cjs compares all native outcomes, complete results and charged Work to browser references. analyze-production.cjs checks every completed candidate fallback/upgrade against its unchanged reference, every row, mode provenance, remaining Work forwarding, and no second pass for already Exact or NoTime outcomes. Quantiles use the nearest observed rank; cold observations and two warmups are excluded, and actual warm sample counts are reported. Ten observations are not a cross-device performance guarantee.

## Evidence interpretation

output/baseline.json and supplemental.json are unchanged-mode reference evidence. Baseline measurement was resumed after interruption with a new browser segment; completed Fast/Exact samples were retained and the seed regenerated for the selected modes. The research reference owner waits 2.5 seconds even after normal recycling, so its cold total includes this research-only delay. Compare warm dispatch times and report bootstrap separately; the production owner does not add that delay to normal recycling.

output/wasm.json and wasm-complete.json archive superseded automatic observations from before the user's clarification and before the seven-second correction. Their automatic outcomes are not final acceptance evidence. baseline-resume.json is a checkpoint, not an additional independent sample. production-pre-cleanup-fix.json is also superseded: its helper incorrectly acknowledged typed initial failures. The corrected production run disposes those leases, matching BackgroundCalculator; only the corrected run is final acceptance evidence. The initial run encountered a transient Windows output-file lock; the writer was changed to replace complete checkpoints atomically. Partial raw observations are retained rather than silently discarded.

Allocation bytes are cumulative allocations, not resident memory. Managed heap is an end-request observation, and WASM capacity is the backing buffer size; neither is a peak-live-memory measurement. Work/Storage diagnostics for terminated upgrades cannot be returned by a blocked synchronous worker. The production budget still enforces the remaining original allowance and the original per-structure Storage limits; these are not a total process-memory cap.

Browser worker close events measure execution-context disappearance. Worker.terminate() has no portable completion acknowledgement, and browser suspension/main-thread stalls can delay timers. The 2.5-second cooldown mitigates observed Chromium shutdown grace; it is not a universal quiescence guarantee. Explicit Exact has no automatic calculation deadline. Initial Fast may also exceed seven seconds by the user's explicit clarification.

The final lifecycle review moved the automatic-request marker before worker readiness, so manual cancellation during bootstrap also receives the shutdown grace. The normal ready-worker performance path is unchanged. The matrix was measured before this marker-only lifecycle fix; the final normal publish, explicit-mode parity and full browser smoke including blocked startup replacement all passed, as did 52 JS tests covering the final lifecycle behavior. Candidate manifest records the measured source and the final source separately.

Final validation: 483 automatic-path runs across 43 cases passed 2,921 result/provenance/allowance checks; 43 final explicit-path runs passed 123 checks. The larger supplement upgraded in 8/10 recorded attempts (10/12 with warmups); two attempts reached the real five-second deadline and retained their completed bound. No samples were dropped to hide this variance.

[Desktop result](output/smoke-final.desktop.png) · [Mobile result](output/smoke-final.mobile.png). Final startup cancellation also verified that the old context closed before the replacement was created.

The corrected production matrix resumed after a transient Windows checkpoint rename lock. Its segments and production-resume.json preserve all completed observations; new-runtime samples remain marked cold.

Implementation commit: c5d388aafc50ad8440abfb112e1738b85c368ec1. Final source hashes are normalized to LF to permit comparison with committed Git blobs.
