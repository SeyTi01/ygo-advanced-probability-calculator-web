#Requires -Version 5.1
[CmdletBinding()]
param([switch] $Verify)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Restrict fixes to the accepted IDE/Roslynator diagnostics, excluding unrelated test analyzers.
$diagnostics = @(
    'IDE0001', 'IDE0002', 'IDE0003', 'IDE0004', 'IDE0005', 'IDE0008',
    'IDE0011', 'IDE0028', 'IDE0034', 'IDE0075', 'IDE0090',
    'IDE0300', 'IDE0301', 'IDE0302', 'IDE0303', 'IDE0304', 'IDE0305', 'IDE0306',
    'RCS0008', 'RCS0010', 'RCS0021', 'RCS0053', 'RCS0054', 'RCS0058', 'RCS0063'
)
$solution = 'YGOProbabilityCalculatorBlazor.sln'
$timer = [Diagnostics.Stopwatch]::StartNew()
Push-Location (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)
try {
    # Refresh analyzer/reference assets; no application build or tool restore is needed.
    & dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw 'Style dependency restore failed.' }

    foreach ($stage in @('whitespace', 'style', 'analyzers', 'whitespace')) {
        $arguments = @('format', $stage, $solution, '--no-restore', '--verbosity', 'minimal')
        if ($Verify) { $arguments += '--verify-no-changes' }
        if ($stage -ne 'whitespace') {
            $prefix = if ($stage -eq 'style') { 'IDE' } else { 'RCS' }
            $arguments += @('--severity', 'info', '--diagnostics') + @($diagnostics | Where-Object { $_.StartsWith($prefix) })
        }
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "C# $stage command failed. Run pwsh -NoProfile -File scripts/style/fix.ps1, then re-run check.ps1."
        }
    }
}
finally {
    Pop-Location
    $timer.Stop()
    Write-Host ("C# style {0} elapsed: {1:N1}s" -f $(if ($Verify) { 'verification' } else { 'fix' }), $timer.Elapsed.TotalSeconds)
}
Write-Host $(if ($Verify) { 'C# style verification passed.' } else { 'C# style fixes applied.' })
