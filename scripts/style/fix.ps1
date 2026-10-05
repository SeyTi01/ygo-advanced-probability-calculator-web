#Requires -Version 5.1
[CmdletBinding()]
param([string[]] $MSBuildArguments = @())

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Dotnet {
    param([string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-CleanupCode {
    Invoke-Dotnet -Arguments @('tool', 'run', 'jb', '--', 'cleanupcode', 'YGOProbabilityCalculatorBlazor.sln',
        '--settings=scripts/style/Cleanup.DotSettings', '--profile=YGO Autofix',
        '--include=**/*.cs', '--disable-settings-layers=GlobalAll;GlobalPerProduct;SolutionPersonal;ProjectPersonal',
        '--no-build', '--no-updates')
}

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $repoRoot
try {
    $solution = 'YGOProbabilityCalculatorBlazor.sln'
    Invoke-Dotnet -Arguments @('tool', 'restore')
    Invoke-Dotnet -Arguments (@('restore', $solution) + $MSBuildArguments)
    Invoke-Dotnet -Arguments (@('build', $solution, '--no-restore') + $MSBuildArguments)

    # Derive the allowlist from the policy instead of maintaining a second rule list.
    $styleRules = @(
        Get-Content .editorconfig | ForEach-Object {
            if ($_ -match '^dotnet_diagnostic\.(IDE\d+)\.severity\s*=\s*suggestion\s*$') {
                $Matches[1]
            }
        }
    )
    if ($styleRules.Count -eq 0) { throw 'No automatically fixable style rules configured.' }

    $sources = Get-ChildItem YGOProbabilityCalculatorBlazor, YGOProbabilityCalculatorBlazorTest -Filter '*.cs' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName

    # Establish explicit target types before Roslyn's exact-type collection fixes.
    Invoke-CleanupCode
    # Roslyn can expose another fix (for example new T() -> new() -> []).
    do {
        $before = ($sources | Get-FileHash -Algorithm SHA256).Hash -join ''
        Invoke-Dotnet -Arguments (@('format', 'style', $solution, '--severity', 'info', '--no-restore', '--diagnostics') + $styleRules)
        $after = ($sources | Get-FileHash -Algorithm SHA256).Hash -join ''
    } while ($before -cne $after)

    # Restore JetBrains spacing after Roslyn's code fixes.
    Invoke-CleanupCode

    # CleanupCode preserves existing BOMs; charset=utf-8 requires UTF-8 without one.
    foreach ($source in $sources) {
        $bytes = [IO.File]::ReadAllBytes($source.FullName)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            $content = New-Object byte[] ($bytes.Length - 3)
            [Buffer]::BlockCopy($bytes, 3, $content, 0, $content.Length)
            [IO.File]::WriteAllBytes($source.FullName, $content)
        }
    }
}
finally {
    Pop-Location
}
