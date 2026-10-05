#Requires -Version 5.1
# Exercise the actual solution and canonical fixer without committing a probe source file.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$probe = Join-Path $repoRoot 'YGOProbabilityCalculatorBlazor/AutofixProbe.cs'
if (Test-Path $probe) { throw "Probe path already exists: $probe" }
$source = @'
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
    & "$PSScriptRoot/fix.ps1"
    $fixed = [IO.File]::ReadAllText($probe)
    Write-Host $fixed
    Assert-Pattern $fixed 'int number = 1;' 'explicit types and redundant casts'
    Assert-Pattern $fixed 'object apparent = new\(\);' 'explicit apparent types and target-typed new'
    Assert-Pattern $fixed 'string elsewhere =' 'explicit inferred types'
    Assert-Pattern $fixed 'List<int> list = \[\];' 'qualified names and empty collections'
    Assert-Pattern $fixed 'List<int> copied = \[\.\. list\];' 'ToList collection expression'
    Assert-Pattern $fixed 'int\[\] array = \[\.\. copied\];' 'ToArray collection expression'
    Assert-Pattern $fixed 'ImmutableArray<int> immutable = \[\.\. copied\];' 'ToImmutableArray collection expression'
    Assert-Pattern $fixed 'int\[\] literal = \[1, 2\];' 'array collection expression'
    Assert-Pattern $fixed 'int\[\] empty = \[\];' 'Array.Empty collection expression'
    Assert-Pattern $fixed 'Span<int> stack = \[1, 2\];' 'stackalloc collection expression'
    Assert-Pattern $fixed 'ImmutableArray<int> created = \[1, 2\];' 'Create collection expression'
    Assert-Pattern $fixed 'ImmutableArray<int> built = \[1, 2\];' 'builder collection expression'
    Assert-Pattern $fixed 'int n = default;' 'default literal'
    Assert-Pattern $fixed 'private static int PrivateCandidate' 'private-only static candidate'
    Assert-Pattern $fixed 'public int PublicCandidate' 'public API preserved'
    Assert-Pattern $fixed 'internal int InternalCandidate' 'internal API preserved'
    Assert-Pattern $fixed '=> PrivateCandidate\(1\);' 'static call site updated and simplified'
    Assert-Pattern $fixed '(?m)^\s*Noop\(\);' 'static qualification simplification'
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
    Assert-Pattern $fixed 'LongParameters\(\s*\n' 'parameter and argument wrapping'
    $before = git -c "safe.directory=$repoRoot" diff --binary
    if ($LASTEXITCODE -ne 0) { throw 'Could not snapshot tracked cleanup output.' }
    & "$PSScriptRoot/fix.ps1"
    $after = git -c "safe.directory=$repoRoot" diff --binary
    if ($LASTEXITCODE -ne 0) { throw 'Could not check tracked cleanup output.' }
    if ($fixed -cne [IO.File]::ReadAllText($probe) -or ($before -join "`n") -cne ($after -join "`n")) {
        throw 'Canonical fixer is not idempotent.'
    }
    Write-Host 'PASS: second canonical run is unchanged for probe and actual solution'
}
finally {
    if (Test-Path $probe) { Remove-Item $probe }
    Pop-Location
}
