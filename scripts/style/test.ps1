#Requires -Version 5.1
# Exercise the actual solution and canonical fixer without committing a probe source file.
[CmdletBinding()]
param(
    [string[]] $MSBuildArguments = @(),
    [string] $RepositoryRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRepositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $PSScriptRoot 'Policy.ps1')
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path ([IO.Path]::GetTempPath()) ("ygo-style-regression-" + [Guid]::NewGuid().ToString('N'))
    try {
        Copy-StyleValidationTree -SourceRoot $scriptRepositoryRoot -DestinationRoot $RepositoryRoot
        $isolatedScript = Join-Path $RepositoryRoot 'scripts/style/test.ps1'
        & $isolatedScript -MSBuildArguments $MSBuildArguments -RepositoryRoot $RepositoryRoot
        return
    }
    finally {
        if (Test-Path -LiteralPath $RepositoryRoot) {
            Remove-Item -LiteralPath $RepositoryRoot -Recurse -Force
        }
    }
}

$repoRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$styleRoot = Join-Path $repoRoot 'scripts/style'
$probe = Join-Path $repoRoot 'YGOProbabilityCalculatorBlazor/AutofixProbe.cs'
if (Test-Path $probe) { throw "Probe path already exists: $probe" }
$source = @'
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
namespace AutofixValidation;
public class AutofixProbe {
 private int value;
 public int PublicCandidate(int x) => x + 1;
 internal int InternalCandidate(int x) => x + 1;
 private int PrivateCandidate(int x) => x + 1;
 private static void Noop() { }
 public int Call(int x) => this.PrivateCandidate(x);
 public int QualifiedCall() => new AutofixProbe().PrivateCandidate(1);
 public int Property { get; set; }
 public event Action? Changed;
 public bool Test(string kind, object? category, object? card, bool condition, string[] alternatives) {
  var number = (int)1;
  var apparent = new object();
  var elsewhere = alternatives.First();
  System.Collections.Generic.List<int> list = new List<int>();
  List<int> copied = list.ToList();
  List<int> constructed = new List<int>(copied);
  List<int> capacity = new List<int>(5);
  int[] array = copied.ToArray();
  ImmutableArray<int> immutable = copied.ToImmutableArray();
  int[] literal = new int[] { 1, 2 };
  int[] empty = Array.Empty<int>();
  Span<int> stack = stackalloc int[] { 1, 2 };
  ImmutableArray<int> created = ImmutableArray.Create(1, 2);
  var builder = ImmutableArray.CreateBuilder<int>();
  builder.Add(1);
  builder.Add(2);
  ImmutableArray<int> built = builder.ToImmutable();
  Func<int, int> unused = sp => 1;
  bool constant = alternatives.Any(a => a is null);
  int n = default(int);
  object o = new object();
  AutofixProbe.Noop();
  Dictionary<string, string> sourceCategory = new();
  sourceCategory.Add("Source", "old");
  sourceCategory.Add("MetadataKey", "old");
  foreach (string key in sourceCategory.Select(p => p.Key).Where(key => key.Equals("Source", StringComparison.OrdinalIgnoreCase) || key.Equals("MetadataKey", StringComparison.OrdinalIgnoreCase)).ToArray())
  {
   sourceCategory.Remove(key);
  }
  this.Property = this.value;
  this.Changed?.Invoke();
  if (!condition) value = n;
  for (int i = 0; i < 1; i++) value++;
  foreach (int i in list) value += i;
  while (condition) break;
  do value++; while (condition);
  using (MemoryStream stream = new()) value++;
  lock (o) value++;
  int negative = -5;
  int positive = +1;
  void Local() { value++; }
  void OtherLocal() => value++;
  Local();
  OtherLocal();



  return kind == "Category" ? category is null || card is not null : kind == "Card" ? card is null || category is not null : true;
 }
 public unsafe void Fixed(int[] values) { fixed (int* p = values) value = *p; }
 public static void LongParameters(int firstParameterWithAVeryLongName, int secondParameterWithAVeryLongName, int thirdParameterWithAVeryLongName, int fourthParameterWithAVeryLongName, int fifthParameterWithAVeryLongName) { }
 public void LongArguments() { LongParameters(firstParameterWithAVeryLongName: value, secondParameterWithAVeryLongName: value, thirdParameterWithAVeryLongName: value, fourthParameterWithAVeryLongName: value, fifthParameterWithAVeryLongName: value); }
}
'@
function Assert-Pattern {
    param([string] $Text, [string] $Pattern, [string] $Rule)
    if ($Text -notmatch $Pattern) { throw "Automatic fix missing: $Rule ($Pattern)" }
    Write-Host "PASS: $Rule"
}
Push-Location $repoRoot
try {
    # Include CRLF, trailing whitespace and a missing final newline in the dirty input.
    [IO.File]::WriteAllText($probe, $source.Replace("`n", "  `r`n"), [Text.UTF8Encoding]::new($true))
    & (Join-Path $styleRoot 'fix.ps1') -MSBuildArguments $MSBuildArguments
    $fixed = [IO.File]::ReadAllText($probe)
    Write-Host $fixed
    $bytes = [IO.File]::ReadAllBytes($probe)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw 'UTF-8 encoding fix left a BOM.'
    }
    Assert-Pattern $fixed 'int number = 1;' 'explicit types and redundant casts'
    Assert-Pattern $fixed 'object apparent = new\(\);' 'explicit apparent types and target-typed new'
    Assert-Pattern $fixed 'string elsewhere =' 'explicit inferred types'
    Assert-Pattern $fixed 'List<int> list = \[\];' 'qualified names and empty collections'
    Assert-Pattern $fixed 'List<int> copied = \[\.\. list\];' 'ToList collection expression'
    Assert-Pattern $fixed 'List<int> constructed = \[\.\. copied\];' 'enumerable constructor collection expression'
    Assert-Pattern $fixed 'List<int> capacity = new\(5\);' 'capacity constructor preserved'
    Assert-Pattern $fixed 'int\[\] array = \[\.\. copied\];' 'ToArray collection expression'
    Assert-Pattern $fixed 'ImmutableArray<int> immutable = \[\.\. copied\];' 'ToImmutableArray collection expression'
    Assert-Pattern $fixed 'int\[\] literal = \[1, 2\];' 'array collection expression'
    Assert-Pattern $fixed 'int\[\] empty = \[\];' 'Array.Empty collection expression'
    Assert-Pattern $fixed 'Span<int> stack = \[1, 2\];' 'stackalloc collection expression'
    Assert-Pattern $fixed 'ImmutableArray<int> created = \[1, 2\];' 'Create collection expression'
    Assert-Pattern $fixed 'ImmutableArray<int> built = \[1, 2\];' 'builder collection expression'
    Assert-Pattern $fixed 'int n = default;' 'default literal'
    Assert-Pattern $fixed 'private int PrivateCandidate' 'private instance API preserved'
    Assert-Pattern $fixed 'public int PublicCandidate' 'public API preserved'
    Assert-Pattern $fixed '(?m)^[ \t]*public int PublicCandidate\(int x\) => x \+ 1;$' 'short method declaration stays on one line'
    Assert-Pattern $fixed 'internal int InternalCandidate' 'internal API preserved'
    Assert-Pattern $fixed 'new AutofixProbe\(\).PrivateCandidate\(1\)' 'qualified instance call preserved'
    Assert-Pattern $fixed '(?m)^\s*Noop\(\);' 'static qualification simplification'
    $expectedInvocation = @'
        foreach (string key in sourceCategory
            .Select(p => p.Key)
            .Where(key =>
                key.Equals("Source", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("MetadataKey", StringComparison.OrdinalIgnoreCase)
            )
            .ToArray()
        )
        {
            sourceCategory.Remove(key);
        }
'@
    if ($fixed.IndexOf($expectedInvocation, [StringComparison]::Ordinal) -lt 0) {
        throw 'Automatic fix did not match the expected multiline invocation layout.'
    }
    Write-Host 'PASS: chained calls and nested closing delimiters match the golden layout'

    # The custom layout tool must report dirty input without writing to it.
    $layoutProject = Join-Path $styleRoot 'InvocationLayout/InvocationLayout.csproj'
    $layoutCheckArguments = @('run', '--project', $layoutProject, '--no-launch-profile', '--', '--check', $probe)
    & dotnet @layoutCheckArguments
    if ($LASTEXITCODE -ne 0) { throw 'Invocation layout check rejected the canonical fixture.' }
    $canonicalOuterClosing = "        )`n        {`n            sourceCategory.Remove(key);"
    $misalignedOuterClosing = "            )`n        {`n            sourceCategory.Remove(key);"
    $dirtyLayout = $fixed.Replace($canonicalOuterClosing, $misalignedOuterClosing)
    if ($dirtyLayout -ceq $fixed) { throw 'Could not prepare the noncanonical invocation fixture.' }
    [IO.File]::WriteAllText($probe, $dirtyLayout, [Text.UTF8Encoding]::new($false))
    $beforeCheckOnly = [IO.File]::ReadAllBytes($probe)
    & dotnet @layoutCheckArguments
    if ($LASTEXITCODE -eq 0) { throw 'Invocation layout check accepted noncanonical input.' }
    $afterCheckOnly = [IO.File]::ReadAllBytes($probe)
    if ([Convert]::ToBase64String($beforeCheckOnly) -cne [Convert]::ToBase64String($afterCheckOnly)) {
        throw 'Invocation layout check modified its input.'
    }
    $layoutFixArguments = @('run', '--project', $layoutProject, '--no-launch-profile', '--', $probe)
    & dotnet @layoutFixArguments
    if ($LASTEXITCODE -ne 0 -or [IO.File]::ReadAllText($probe) -cne $fixed) {
        throw 'Invocation layout fixer did not restore the golden fixture.'
    }
    Write-Host 'PASS: invocation layout --check detects differences without writing'

    Assert-Pattern $fixed 'Property = value;' 'field/property qualification simplification'
    Assert-Pattern $fixed '(?m)^\s*Changed\?\.Invoke\(\);' 'event qualification simplification'
    Assert-Pattern $fixed 'if \(! condition\)\s*\{' 'logical NOT spacing and braces'
    foreach ($construct in @('for', 'foreach', 'while', 'using', 'lock', 'fixed')) {
        Assert-Pattern $fixed "(?s)\b$construct \([^\r\n]*\)\s*\{" "$construct braces"
    }
    Assert-Pattern $fixed 'do\s*\{' 'do/while braces'
    Assert-Pattern $fixed 'kind != "Card" \|\| card is null \|\| category is not null' 'simplified boolean conditional'
    Assert-Pattern $fixed 'int negative = -5;' 'unrelated unary minus preserved'
    Assert-Pattern $fixed 'int positive = \+1;' 'unrelated unary plus preserved'
    Assert-Pattern $fixed 'Func<int, int> unused = sp => 1;' 'unused delegate signature preserved'
    Assert-Pattern $fixed 'alternatives.Any\(a => a is null\)' 'nullable contract predicate preserved'
    if ($fixed -match 'using System.Text;' -or $fixed -match '\r|[ \t]+\n|\n\n\n' -or -not $fixed.EndsWith("`n")) {
        throw 'Import removal or C# text/blank-line formatting failed.'
    }
    Assert-Pattern $fixed 'LongParameters\(\s*\n' 'parameter wrapping'
    Assert-Pattern $fixed '(?s)LongParameters\([^)]*fifthParameterWithAVeryLongName\r?\n[ \t]+\)\r?\n[ \t]+\{' 'multiline declaration closing parenthesis alignment'
    Assert-Pattern $fixed '(?s)LongParameters\([^;]*\n\s*secondParameterWithAVeryLongName:' 'argument wrapping'
    $canonicalSnapshot = Get-StyleSourceSnapshot -RepositoryRoot $repoRoot

    # Each CI family must reject a dirty probe without writing any source bytes.
    # Keep cases independent so an earlier text/syntax failure cannot mask another family.
    $cases = @(
        @{ Name = 'text'; Pattern = 'trailing whitespace'; Transform = { param($text) $text + ' ' } },
        @{ Name = 'Roslyn'; Pattern = 'Roslyn style verification failed'; Transform = { param($text) $text.Replace('int number = 1;', 'var number = 1;') } },
        @{ Name = 'braces'; Pattern = 'braces required'; Transform = { param($text) [regex]::Replace($text, '(if \(! condition\))\s*\{\s*(value = n;)\s*\}', '$1 $2') } },
        @{ Name = 'NOT spacing'; Pattern = 'space required after logical NOT'; Transform = { param($text) $text.Replace('! condition', '!condition') } },
        @{ Name = 'blank-line cap'; Pattern = 'at most one consecutive blank line'; Transform = { param($text) "`n`n`n" + $text } },
        @{ Name = 'member separation'; Pattern = 'blank line around invocable member'; Transform = { param($text) $text.Replace("private int value;`n`n", "private int value;`n") } },
        @{ Name = 'local method separation'; Pattern = 'blank line around local method'; Transform = { param($text) [regex]::Replace($text, '\n\n(?=[ \t]+void Local\()', "`n") } },
        @{ Name = 'block separation'; Pattern = 'blank line around block statement'; Transform = { param($text) $text.Replace("Changed?.Invoke();`n`n", "Changed?.Invoke();`n") } },
        @{ Name = 'control transfer separation'; Pattern = 'blank line before control transfer'; Transform = { param($text) [regex]::Replace($text, '\n\n(?=[ \t]+return kind)', "`n") } },
        @{ Name = 'wrapped closer'; Pattern = 'closing parenthesis on a separate line'; Transform = { param($text) [regex]::Replace($text, '(int fifthParameterWithAVeryLongName)\n[ \t]+\)', '$1)') } },
        @{ Name = 'wrapped item separation'; Pattern = 'one item per line'; Transform = { param($text) [regex]::Replace($text, '(int firstParameterWithAVeryLongName,)\n[ \t]+(int secondParameterWithAVeryLongName)', '$1 $2') } },
        @{ Name = 'leading chain dot'; Pattern = 'leading dot'; Transform = { param($text) $text.Replace('.Where(key =>', ".`n            Where(key =>") } },
        @{ Name = 'foreach invocation layout'; Pattern = 'Invocation layout would change'; Transform = { param($text) $text.Replace($canonicalOuterClosing, $misalignedOuterClosing) } }
    )
    $combinedDirty = $fixed
    foreach ($case in $cases) {
        $dirty = & $case.Transform $fixed
        if ($dirty -ceq $fixed) { throw "Could not prepare $($case.Name) negative case." }
        [IO.File]::WriteAllText($probe, $dirty, [Text.UTF8Encoding]::new($false))
        $dirtySnapshot = Get-StyleSourceSnapshot -RepositoryRoot $repoRoot
        $checkerMessage = ''
        try { & (Join-Path $styleRoot 'check.ps1') -Full -RepositoryRoot $repoRoot }
        catch { $checkerMessage = $_.Exception.Message }
        if ($checkerMessage -notmatch $case.Pattern) {
            throw "Checker did not reject $($case.Name) for the expected rule: $checkerMessage"
        }
        if ((Get-StyleSourceSnapshot -RepositoryRoot $repoRoot) -cne $dirtySnapshot) {
            throw "Checker modified source in $($case.Name) negative case."
        }
        Write-Host "PASS: $($case.Name) is detected without source writes"
        $combinedDirty = & $case.Transform $combinedDirty
    }
    [IO.File]::WriteAllText($probe, $combinedDirty, [Text.UTF8Encoding]::new($false))

    & (Join-Path $styleRoot 'fix.ps1') -MSBuildArguments $MSBuildArguments
    if ([IO.File]::ReadAllText($probe) -cne $fixed -or
        (Get-StyleSourceSnapshot -RepositoryRoot $repoRoot) -cne $canonicalSnapshot) {
        throw 'Canonical fixer is not idempotent.'
    }
    & (Join-Path $styleRoot 'check.ps1') -Full -RepositoryRoot $repoRoot
    Write-Host 'PASS: second canonical run is unchanged for probe and actual solution'
}
finally {
    if (Test-Path $probe) { Remove-Item $probe }
    Pop-Location
}
