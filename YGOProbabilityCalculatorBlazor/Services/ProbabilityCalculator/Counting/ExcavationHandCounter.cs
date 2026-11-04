using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;

// One random revealed pool precedes every optional search. For a fixed physical
// hand/Deck and one result row, let G be the copies whose addition admits a legal
// search witness. An excavation succeeds exactly when its k-subset meets G:
// C(D,k) - C(D-G,k). Evaluate target identities once, not every revealed subset.
// The same union is formed independently for total, combo and group rows.
internal sealed class ExcavationHandCounter {
    private readonly List<Card> _cards;
    private readonly WorkBudget _budget;
    private readonly CalculationResultRows _rows;
    private readonly bool _totalOnly;
    private readonly PhysicalSearchHandCounter? _simple;
    private readonly BoundedSearchHandCounter? _bounded;
    private readonly int[] _sources;
    private readonly int[] _hand;
    private readonly int[] _activationSources;
    private readonly int[] _successfulCopies;

    internal ExcavationHandCounter(
        List<Card> cards,
        WorkBudget budget,
        CalculationResultRows rows,
        bool totalOnly,
        PhysicalSearchHandCounter? simple,
        BoundedSearchHandCounter? bounded
    ) {
        _cards = cards;
        _budget = budget;
        _rows = rows;
        _totalOnly = totalOnly;
        _simple = simple;
        _bounded = bounded;
        budget.Spend(cards.Count * (cards.Count + 1L));
        _sources = Enumerable.Range(0, cards.Count).Where(row => cards[row].Excavation is not null)
            .OrderByDescending(row => cards[row].Excavation!.Count)
            .ThenBy(row => cards[row].Id, StringComparer.Ordinal).ToArray();
        budget.CheckStorage(3L * cards.Count + rows.Count, 3L * cards.Count + rows.Count);
        _hand = new int[cards.Count];
        _activationSources = new int[cards.Count];
        _successfulCopies = new int[rows.Count];
    }

    // Physical terminal mass includes D!, so C(D,k) divides it exactly. The
    // caller retains its common original-deck factorial denominator throughout.
    // Return false when no source is eligible, preserving ordinary search only.
    internal bool TryAccumulate(
        ReadOnlySpan<int> held,
        int[] available,
        bool effectDrawOccurred,
        IReadOnlyList<int>? encounters,
        BigInteger mass,
        BigInteger[] wins
    ) {
        _budget.Spend(held.Length + _sources.Length + 1L);
        int capacity = available.Sum();
        int source = -1;

        foreach (int candidate in _sources) {
            ExcavationEffectPlan plan = _cards[candidate].Excavation!;

            if (held[candidate] > 0 && capacity >= plan.Count
                && (!plan.RequiresNoPriorEffectDraw || !effectDrawOccurred)) {
                source = candidate;
                break;
            }
        }

        if (source < 0) {
            return false;
        }

        held.CopyTo(_hand);
        _hand[source]--;
        _hand.CopyTo(_activationSources, 0);
        Array.Clear(_successfulCopies);

        for (int target = 0; target < available.Length; target++) {
            _budget.Spend(_rows.Count + 1L);
            int copies = available[target];

            if (copies == 0) {
                continue;
            }

            available[target]--;
            _hand[target]++;
            bool[] success = _bounded is null
                ? _simple!.Evaluate(_hand, available, _activationSources)
                : _bounded.Evaluate(_hand, available, encounters, _activationSources);
            _hand[target]--;
            available[target]++;

            if (success.Any(value => value)) {
                _successfulCopies[0] += copies;
            }

            if (!_totalOnly) {
                for (int combo = 0; combo < _rows.ComboCount; combo++) {
                    if (success[combo]) {
                        _successfulCopies[_rows.ComboRow(combo)] += copies;
                    }
                }

                for (int group = 0; group < _rows.GroupCount; group++) {
                    _budget.Spend(_rows.ComboCount + 1L);

                    if (_rows.GroupMatches(group, success)) {
                        _successfulCopies[_rows.GroupRow(group)] += copies;
                    }
                }
            }
        }

        int count = _cards[source].Excavation!.Count;
        BigInteger pools = Choose(capacity, count);
        _budget.Spend((WorkBudget.IntegerCells(mass) + 1) * (WorkBudget.IntegerCells(pools) + 1));
        BigInteger arrangements = BigInteger.DivRem(mass, pools, out BigInteger remainder);

        if (!remainder.IsZero) {
            throw new InvalidOperationException("Excavation requires complete physical suffix mass.");
        }

        for (int row = 0; row < _successfulCopies.Length; row++) {
            if (_successfulCopies[row] == 0) {
                continue;
            }

            BigInteger successfulPools = pools - Choose(capacity - _successfulCopies[row], count);
            _budget.Spend((WorkBudget.IntegerCells(arrangements) + 1) * (WorkBudget.IntegerCells(successfulPools) + 1)
                + WorkBudget.IntegerCells(wins[row]));
            wins[row] += arrangements * successfulPools;
        }

        _budget.CheckStorage(wins.Length, wins.Sum(WorkBudget.IntegerCells) + 3L * _cards.Count + _rows.Count);

        return true;
    }

    private BigInteger Choose(int n, int k) {
        if (n < k) {
            return BigInteger.Zero;
        }

        k = Math.Min(k, n - k);
        BigInteger result = BigInteger.One;

        for (int index = 1; index <= k; index++) {
            _budget.Spend(3 * WorkBudget.IntegerCells(result) + 1);
            result = result * (n - index + 1) / index;
        }

        return result;
    }
}
