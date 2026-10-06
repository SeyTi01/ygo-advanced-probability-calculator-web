function Get-StylePolicy {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $RepositoryRoot)

    $editorConfig = Join-Path $RepositoryRoot '.editorconfig'
    if (-not (Test-Path -LiteralPath $editorConfig -PathType Leaf)) {
        throw "Style policy not found: $editorConfig"
    }

    $inCSharpSection = $false
    $settings = @{}
    $diagnostics = [Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadAllLines($editorConfig)) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^\[(.+)\]$') {
            $inCSharpSection = $Matches[1] -eq '*.cs'
            continue
        }
        if (-not $inCSharpSection -or $trimmed.StartsWith('#') -or $trimmed.Length -eq 0) {
            continue
        }

        if ($trimmed -match '^dotnet_diagnostic\.(IDE\d+)\.severity\s*=\s*suggestion\s*$') {
            $diagnostics.Add($Matches[1])
            continue
        }
        if ($trimmed -match '^([A-Za-z0-9_.-]+)\s*=\s*(.*?)\s*$') {
            $settings[$Matches[1].ToLowerInvariant()] = $Matches[2].ToLowerInvariant()
        }
    }

    $diagnosticIds = @($diagnostics | Sort-Object -Unique)
    if ($diagnosticIds.Count -eq 0) {
        throw 'No automatically fixable Roslyn style diagnostics are configured in .editorconfig.'
    }
    foreach ($key in @('charset', 'end_of_line', 'insert_final_newline', 'trim_trailing_whitespace')) {
        if (-not $settings.ContainsKey($key)) {
            throw "Required C# text policy '$key' is missing from .editorconfig."
        }
    }

    [PSCustomObject]@{
        DiagnosticIds = [string[]]$diagnosticIds
        Charset = [string]$settings['charset']
        EndOfLine = [string]$settings['end_of_line']
        InsertFinalNewline = [string]$settings['insert_final_newline'] -eq 'true'
        TrimTrailingWhitespace = [string]$settings['trim_trailing_whitespace'] -eq 'true'
    }
}

function Get-StyleSources {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $RepositoryRoot)

    $projects = @(
        (Join-Path $RepositoryRoot 'YGOProbabilityCalculatorBlazor'),
        (Join-Path $RepositoryRoot 'YGOProbabilityCalculatorBlazorTest')
    )
    foreach ($project in $projects) {
        if (-not (Test-Path -LiteralPath $project -PathType Container)) {
            throw "C# source directory not found: $project"
        }
    }

    @(Get-ChildItem -LiteralPath $projects -Filter '*.cs' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Sort-Object FullName)
}

function Get-StyleSourceSnapshot {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $RepositoryRoot)

    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $entries = [Collections.Generic.List[string]]::new()
    foreach ($source in Get-StyleSources -RepositoryRoot $root) {
        $relativePath = $source.FullName.Substring($root.Length).TrimStart([char[]]@('\', '/'))
        $hash = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash
        $entries.Add("$relativePath`t$hash")
    }
    $entries -join "`n"
}

function Get-StyleTextViolations {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)] $Policy
    )

    $violations = [Collections.Generic.List[string]]::new()
    $bytes = [IO.File]::ReadAllBytes($Path)
    $hasUtf8Bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $offset = 0
    if ($Policy.Charset -eq 'utf-8') {
        if ($hasUtf8Bom) { $violations.Add('UTF-8 BOM is not allowed') }
    }
    elseif ($Policy.Charset -eq 'utf-8-bom') {
        if (-not $hasUtf8Bom) { $violations.Add('UTF-8 BOM is required') }
        else { $offset = 3 }
    }
    else {
        $violations.Add("Unsupported C# charset policy '$($Policy.Charset)'")
        return $violations.ToArray()
    }

    try {
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes, $offset, $bytes.Length - $offset)
    }
    catch [Text.DecoderFallbackException] {
        $violations.Add('file is not valid UTF-8')
        return $violations.ToArray()
    }

    if ($Policy.EndOfLine -eq 'lf' -and $text.Contains("`r")) {
        $violations.Add('line endings must be LF')
    }
    elseif ($Policy.EndOfLine -eq 'crlf' -and [regex]::IsMatch($text, '(?<!\r)\n|\r(?!\n)')) {
        $violations.Add('line endings must be CRLF')
    }
    elseif ($Policy.EndOfLine -eq 'cr' -and [regex]::IsMatch($text, '\r\n|(?<!\r)\n')) {
        $violations.Add('line endings must be CR')
    }
    elseif ($Policy.EndOfLine -notin @('lf', 'crlf', 'cr')) {
        $violations.Add("Unsupported C# line-ending policy '$($Policy.EndOfLine)'")
    }

    if ($Policy.TrimTrailingWhitespace -and [regex]::IsMatch($text, '[ \t]+(?=\r|\n|\z)')) {
        $violations.Add('trailing whitespace is not allowed')
    }

    $newline = switch ($Policy.EndOfLine) {
        'lf' { "`n" }
        'crlf' { "`r`n" }
        'cr' { "`r" }
        default { $null }
    }
    if ($Policy.InsertFinalNewline -and $newline -and $text.Length -gt 0 -and -not $text.EndsWith($newline, [StringComparison]::Ordinal)) {
        $violations.Add('final newline is required')
    }

    $violations.ToArray()
}

function Copy-StyleValidationTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $SourceRoot,
        [Parameter(Mandatory)][string] $DestinationRoot
    )

    $excludedDirectories = @('.git', 'bin', 'obj', 'node_modules', '.vs', 'TestResults')
    [void](New-Item -ItemType Directory -Path $DestinationRoot -Force)
    foreach ($entry in Get-ChildItem -LiteralPath $SourceRoot -Force) {
        if ($entry.Name -in $excludedDirectories) { continue }
        if ($entry.PSIsContainer) {
            Copy-StyleValidationTree -SourceRoot $entry.FullName -DestinationRoot (Join-Path $DestinationRoot $entry.Name)
        }
        else {
            Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $DestinationRoot $entry.Name) -Force
        }
    }
}
