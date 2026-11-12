# Frozen worker regression for issue #295

Use only for the retained cold-worker correctness regression. Read its frozen-input contract in `repo:scripts/background-calculation/README.md#frozen-worker-regression`; a regenerated example cannot replace the original capture.

Prepare [the browser runtime](browser-runtime.md) for separate clean non-AOT, retained-IL AOT and stripped-IL AOT outputs; verify each configuration in publish flags/logs. This opt-in matrix does not change production settings or join routine smoke/CI.

```sh
node scripts/background-calculation/cold-worker-regression.cjs <published-wwwroot> <frozen-worker-inputs.json> <ignored-results.json> 4
```

Keep inputs and evidence local, with distinct output paths per publish. An optional fifth argument supplies a complete reference response; the reproduction wrapper's `reference` is accepted automatically. At least four fresh contexts exercise the first request and two repeats. Use browser-runtime's Chromium selection, or `YGO_BROWSER_ENGINE=firefox` for an installed Playwright Firefox. Worker-cold state is not a cold browser process/network cache; this gate makes no performance claim.
