# Testable local preview

Use the actual host's [environment preparation](../../ygo-verify/references/environment.md). From the checkout root:

```sh
dotnet run --project YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj
```

Reuse a server only after confirming its checkout and served changes. Prefer the in-app browser in Codex; otherwise provide the usable localhost URL. Verify completed loading and affected interactions, then keep the server available during the user's review. If a server reports listening but the browser cannot connect, follow the host's launch guidance rather than presenting an untested URL.
