# Browser frozen replay

Use [browser preparation](../../ygo-verify/references/browser-runtime.md) and the shared [frozen fixture contract](frozen-fixtures.md). Replay the same baseline fixture file against clean optimized baseline and candidate publishes:

```sh
node scripts/background-calculation/measure-worker.cjs <optimized-wwwroot> <fixtures.json> <worker-output.json> 3 60000
```

The final arguments select warm samples (default 3) and an external cutoff in milliseconds (default 60000), not a production calculation deadline. No probe build is needed. If fixtures must first be generated, use [native export](native-replay.md) or [excavation export](excavation-measurements.md). An unavailable browser runtime remains a verification limit; native timing does not substitute for it.
