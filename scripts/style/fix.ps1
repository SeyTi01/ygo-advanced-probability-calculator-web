#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Dotnet {
    param([string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $repoRoot
try {
    $solution = 'YGOProbabilityCalculatorBlazor.sln'
    Invoke-Dotnet -Arguments @('tool', 'restore')
    Invoke-Dotnet -Arguments @('restore', $solution)

    # Derive the allowlist from the policy instead of maintaining a second rule list.
    $styleRules = @(
        Get-Content .editorconfig | ForEach-Object {
            if ($_ -match '^dotnet_diagnostic\.(IDE\d+)\.severity\s*=\s*suggestion\s*$') {
                $Matches[1]
            }
        }
    )
    if ($styleRules.Count -eq 0) { throw 'No automatically fixable style rules configured.' }

    Invoke-Dotnet -Arguments (@('format', 'style', $solution, '--severity', 'info', '--no-restore', '--diagnostics') + $styleRules)
    Invoke-Dotnet -Arguments @('format', 'analyzers', $solution, '--severity', 'info', '--no-restore', '--diagnostics', 'CA1822')

    # Run formatting last: Roslyn must not undo JetBrains spacing/blank-line choices.
    # The custom profile contains only formatting and braces, not Full Cleanup.
    Invoke-Dotnet -Arguments @('tool', 'run', 'jb', '--', 'cleanupcode', $solution,
        '--settings=scripts/style/Cleanup.DotSettings', '--profile=YGO Autofix',
        '--include=**/*.cs', '--disable-settings-layers=GlobalAll;GlobalPerProduct;SolutionPersonal;ProjectPersonal',
        '--no-build', '--no-updates')
}
finally {
    Pop-Location
}
