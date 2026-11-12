# Native benchmark setup

Use the selected [environment](../../ygo-verify/references/environment.md), reusing valid initialization. Run commands from the checkout root. Restore as needed and build the solution in Release using [application checks](../../ygo-verify/references/app-checks.md).

Set `DOTNET_TieredCompilation=0` consistently for native comparisons. Invoke explicit benchmark tests with `-c Release --no-build --no-restore` so compilation stays outside the measured process.
