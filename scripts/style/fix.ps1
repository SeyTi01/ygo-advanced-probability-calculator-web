#Requires -Version 5.1
[CmdletBinding()]
param([string[]] $MSBuildArguments = @())

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Policy.ps1')

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
    $stylePolicy = Get-StylePolicy -RepositoryRoot $repoRoot
    Invoke-Dotnet -Arguments @('tool', 'restore')
    Invoke-Dotnet -Arguments (@('restore', $solution) + $MSBuildArguments)
    Invoke-Dotnet -Arguments (@('build', $solution, '--no-restore') + $MSBuildArguments)

    $styleRules = @($stylePolicy.DiagnosticIds)
    $sources = @(Get-StyleSources -RepositoryRoot $repoRoot)

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

    # ReSharper has no foreach-header closer or receiver-relative chain indentation setting.
    $invocationLayoutArguments = @(
        'run', '--project', 'scripts/style/InvocationLayout/InvocationLayout.csproj',
        '--no-launch-profile', '--'
    ) + @($sources | ForEach-Object { $_.FullName })
    Invoke-Dotnet -Arguments $invocationLayoutArguments

    # CleanupCode preserves existing BOMs; charset=utf-8 requires UTF-8 without one.
    foreach ($source in $sources) {
        if ($stylePolicy.Charset -eq 'utf-8') {
            $bytes = [IO.File]::ReadAllBytes($source.FullName)
            if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
                $content = New-Object byte[] ($bytes.Length - 3)
                [Buffer]::BlockCopy($bytes, 3, $content, 0, $content.Length)
                [IO.File]::WriteAllBytes($source.FullName, $content)
            }
        }
        elseif ($stylePolicy.Charset -ne 'utf-8-bom') {
            throw "Unsupported C# charset policy '$($stylePolicy.Charset)' in .editorconfig."
        }
    }
}
finally {
    Pop-Location
}
