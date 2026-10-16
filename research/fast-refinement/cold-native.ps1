param([string]$TestBin)
$ErrorActionPreference = 'Stop'
$env:DOTNET_TieredCompilation='0'
$runner=Join-Path $PSScriptRoot 'runner/bin/Release/net10.0/runner.dll'
$output=Join-Path $PSScriptRoot 'output'
$corpus=Get-Content (Join-Path $PSScriptRoot 'inputs/corpus.json') -Raw | ConvertFrom-Json
$results=@()
foreach($fixture in $corpus) {
    $inputPath=Join-Path $output 'cold-request.json'
    ConvertTo-Json -InputObject @($fixture) -Depth 100 -Compress | Set-Content $inputPath
    $first=@{}
    foreach($side in @('fast','low','high','exact','primary')) {
        $resultPath=Join-Path $output 'cold-response.json'
        # One fresh process per mode per request; input/materialization excluded by probe.
        & dotnet $runner $TestBin $inputPath $resultPath "cold-$side"
        if($LASTEXITCODE -ne 0) { throw 'Cold process failed' }
        $value=Get-Content $resultPath -Raw | ConvertFrom-Json
        $first[$side]=$value[0].First.$side
    }
    $results+=@{Name=$fixture.Name;First=$first}
    ConvertTo-Json -InputObject $results -Depth 100 -Compress | Set-Content (Join-Path $output 'cold-native.json')
}
