# Published HTML and SEO checks

Use a matching [published output](published-output.md) and built test project through [application checks](app-checks.md). Set `YGO_PUBLISH_ROOT` to the absolute published directory in the active shell, then run from the repository root:

```sh
dotnet test YGOProbabilityCalculatorBlazorTest/YGOProbabilityCalculatorBlazorTest.csproj --no-build --no-restore --filter FullyQualifiedName~PublishedOutputSeoTest
```

The POSIX wrapper is `bash scripts/verify-published-seo.sh <published-wwwroot>`. For optional HTTP checks, set `YGO_SEO_BASE_URL` to a verified local Pages server or matching public preview (the wrapper also accepts it as argument two). Static checks need no Playwright/browser installation.
