"""Build measurement-only alternatives in an empty scratch checkout.

python physical-enumeration-variants.py SOURCE DESTINATION brute-hidden|streaming|frontier
Use the unchanged physical-search-benchmark-probe.cs and paired-mode runners.
These alternatives must never be published as the application.
"""
import pathlib
import shutil
import sys

source, destination = map(pathlib.Path, sys.argv[1:3])
variant = sys.argv[3]
if variant not in ("brute-hidden", "streaming", "frontier") or destination.exists():
    raise SystemExit("Choose brute-hidden, streaming or frontier and a new destination")
shutil.copytree(source, destination, ignore=shutil.ignore_patterns(".git", "bin", "obj"))
path = destination / "YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/DrawResolution/PostDrawPhysicalEnumeration.cs"
text = path.read_text()
if variant == "brute-hidden":
    old = "                VisitLoss(0, hidden, remaining, suffixWeight);"
    new = """            state.AsSpan(0, n).CopyTo(available);
            VisitHidden(hidden, weight * factorial[remaining - hidden]);

            // Exhaust every weighted hidden entry sequence instead of counting
            // its unordered multiset. Retain the identical visible-prefix policy.
            void VisitHidden(int left, BigInteger mass) {
                budget.Spend(WorkBudget.IntegerCells(mass) + 1);
                CheckStorage(hidden + 1L, (hidden + 1L) * (WorkBudget.IntegerCells(factorial[copies]) + 5), "brute hidden stack");
                if (left == 0) {
                    Record(mass);
                    return;
                }
                for (int row = 0; row < n; row++) {
                    budget.Spend(WorkBudget.IntegerCells(mass) + 1);
                    int count = available[row];
                    if (count == 0) {
                        continue;
                    }
                    available[row]--;
                    VisitHidden(left - 1, mass * count);
                    available[row]++;
                }
            }"""
else:
    old = "PhysicalEnumerationMethod method = PhysicalEnumerationMethod.Auto"
    new = "PhysicalEnumerationMethod method = PhysicalEnumerationMethod." + variant.title()
    path = destination / "YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/DrawResolution/PostDrawSearchCalculation.cs"
    text = path.read_text()
if text.count(old) != 1:
    raise SystemExit("Source changed: review the transformation before benchmarking")
path.write_text(text.replace(old, new))

if variant == "brute-hidden":
    dispatcher = destination / "YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/DrawResolution/PostDrawSearchCalculation.cs"
    dispatcher.write_text(dispatcher.read_text().replace("PhysicalEnumerationMethod method = PhysicalEnumerationMethod.Auto",
        "PhysicalEnumerationMethod method = PhysicalEnumerationMethod.Streaming"))
