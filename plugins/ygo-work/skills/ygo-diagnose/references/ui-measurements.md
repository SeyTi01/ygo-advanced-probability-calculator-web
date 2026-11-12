# UI timings

Use [browser preparation](../../ygo-verify/references/browser-runtime.md) and run only the relevant tool against the clean optimized publish:

| Measurement | Command from the checkout root |
| --- | --- |
| UI click to terminal result, bootstrap and request round trips | `node scripts/background-calculation/measure-worker-lifecycle.cjs <optimized-wwwroot> <ui-worker.json> 3` |
| First catalog query and warm search feedback | `node scripts/card-search/measure-search.cjs <optimized-wwwroot> <search.json> 3` |
| File/example action to accepted session snapshot | `node scripts/workspace/measure-session-loading.cjs <optimized-wwwroot> <session-output-directory> 5` |

Tools record metric definitions, cache conditions, browser and failures. Search measures full-query input events and DOM/double-frame observations; `YGO_SEARCH_CPU_RATES` selects Chromium CPU throttle rates (default `1,4`; each 1–20). Session loading deliberately serves gzip/Brotli and measures acceptance before recovery-storage debounce.
