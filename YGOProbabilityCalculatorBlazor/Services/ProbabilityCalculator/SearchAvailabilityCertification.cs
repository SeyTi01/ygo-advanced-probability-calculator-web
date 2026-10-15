using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Fixed-policy coupling: n_i=h_i+r_i+d_i, D=sum d_i=N-H-R, hence
// max(0,n_i-h_i-D) <= r_i <= n_i-h_i simultaneously for all original rows.
// Every optional lower-capacity assignment is a legal physical assignment with
// the identical final hand (including maxima/exclusions), and likewise for U.
// Each row/group/total is an existential union over the SAME weighted outcomes.
// This proof says nothing about a different draw/discard policy or full gameplay.
internal static class SearchAvailabilityCertification {
    internal static SearchAvailabilityCertificate TryCalculate(
        List<Card> deck,
        int handSize,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        ExactCalculationResult upper,
        WorkBudget budget
    ) {
        try {
            return Calculate(deck, handSize, combos, groups, upper, budget);
        }
        catch (ProbabilityCalculationLimitException exception) {
            return SearchAvailabilityCertificate.Unavailable(SearchAvailabilityStatus.Limited,
                $"Not certified: optional calculation reached the {exception.Reason} limit. Modeled probabilities are complete.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            // A secondary defect must be visible, but cannot erase the completed
            // primary answer or be mislabeled as a resource limit / exact result.
            Console.Error.WriteLine($"Search-availability certification failed: {exception}");
            return SearchAvailabilityCertificate.Unavailable(SearchAvailabilityStatus.Error,
                $"Not certified: certificate calculation failed ({exception.GetType().Name}). Modeled probabilities are complete.");
        }
    }

    private static SearchAvailabilityCertificate Calculate(
        List<Card> deck,
        int handSize,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        ExactCalculationResult upper,
        WorkBudget budget
    ) {
        budget.Spend(4L * deck.Count + combos.Count + (groups?.Count ?? 0) + 1);
        budget.CheckStorage(deck.Count, 4L * deck.Count);
        List<Card> cards = deck.Where(card => card.Active && card.Copies > 0).ToList();
        List<Card> sources = cards.Where(card => card.Search is not null).ToList();
        if (sources.Count == 0) {
            return Complete(upper, SearchAvailabilityStatus.NotApplicable, "No active Deck searches in the executed model.");
        }

        int copies = cards.Sum(card => card.Copies);
        HashSet<Card>? inert = DrawEffectFeasibility.FindInert(cards, copies, handSize, budget);
        List<Card> effects = cards.Where(card => card.DrawCount is not null && inert?.Contains(card) != true).ToList();
        if (effects.Count == 0) {
            return Complete(upper, SearchAvailabilityStatus.Exact, "No copies can leave hand or Deck before searching.");
        }
        budget.Spend(effects.Count + 1L);
        if (effects.Any(card => card.EffectPlan.RequiresHandResolution)
            || !DrawEffectResolver.CanUseCountStateFastPath(effects, copies, handSize)) {
            return SearchAvailabilityCertificate.Unavailable(SearchAvailabilityStatus.NotCertified,
                "Not certified for ordered draws or hand costs/removals. These require a separate survival proof.");
        }

        // Count-state gate proves every possible activation fits and timing/locks
        // are absent or redundant. With no hidden or hand operations, the ONLY
        // missing copies are consumed sources. Check all searches against all
        // possibly activated sources, including unrestricted copies and OPT rows.
        bool noTargetLoss = effects.All(card => card.PreDrawDeckRemovalCount == 0 && card.PostDrawDeckMillCount == 0);
        foreach (Card source in sources) {
            long selectorWork = SelectorWork(source.Search!.Selector);
            foreach (Card effect in effects) {
                budget.Spend(selectorWork
                    * (effect.Categories.Count + 1L));
                if (source.Id != effect.Id && source.Search.Matches(effect)) {
                    noTargetLoss = false;
                }
            }
        }
        if (noTargetLoss) {
            return Complete(upper, SearchAvailabilityStatus.Exact,
                "Only consumed draw sources can be lost; none is eligible for any active search.");
        }

        budget.Spend((long)cards.Count * (effects.Count + 1) + 1);
        List<Card> ordinary = cards.Where(card => !effects.Contains(card)).ToList();
        SearchAccessPlan search = new(cards, budget);
        DrawResolution resolution = DrawEffectResolver.Resolve<SearchComposition>(ordinary.Sum(card => card.Copies), effects, handSize, budget);
        ExactProbability[] sums = Enumerable.Repeat(ExactProbability.Zero, 1 + combos.Count + (groups?.Count ?? 0)).ToArray();
        foreach ((DrawComposition composition, ExactProbability weight) in resolution.Scenarios) {
            budget.Spend(cards.Count + sums.Length + effects.Count + 1L);
            List<Card> retained = effects.Select((card, row) => card.WithCopies(composition.EffectCounts[row]))
                .Where(card => card.Copies > 0).ToList();
            int held = composition.OrdinaryCount + retained.Sum(card => card.Copies);
            int remaining = composition.EffectCounts[^1];
            int lost = copies - held - remaining;
            if (remaining < 0 || lost < 0 || held < 0) {
                throw new InvalidOperationException("Count-state conservation failed.");
            }
            // Elementary loss bound only: no inferred activation count and no
            // artificial split of coalesced identities. Ordinary rows are original
            // identities in a proven uniform pool; retained rows are fixed.
            ExactCalculationResult lower = new SearchProbabilityCalculation(ordinary, composition.OrdinaryCount,
                retained, search, remaining, budget, targetLoss: lost).Calculate(combos, groups, false);
            ExactProbability[] values = [lower.Total, .. lower.Combos, .. lower.Groups];
            for (int row = 0; row < sums.Length; row++) {
                sums[row] = sums[row].Add(weight.Multiply(values[row], budget), budget);
            }
            budget.CheckStorage(sums.Length, sums.Sum(value => value.Cells));
        }

        ExactCalculationResult bound = new(sums[0], sums.Skip(1).Take(combos.Count).ToArray(), sums.Skip(1 + combos.Count).ToArray());
        if (bound.Total.Numerator.IsZero && !upper.Total.Numerator.IsZero) {
            return SearchAvailabilityCertificate.Unavailable(SearchAvailabilityStatus.NotCertified,
                "Only a trivial zero lower bound was proved; no informative interval is available.");
        }
        return Complete(bound, SearchAvailabilityStatus.Bounded,
            "Guaranteed interval under the fixed modeled policy; only Deck-search target survival varies.");

        SearchAvailabilityCertificate Complete(ExactCalculationResult lower, SearchAvailabilityStatus status, string detail) {
            budget.Spend(1L + combos.Count + (groups?.Count ?? 0));
            SearchAvailabilityInterval total = Interval("Any active combo", lower.Total, upper.Total);
            ImmutableArray<SearchAvailabilityInterval> individual = combos.Select((combo, index) =>
                Interval(string.IsNullOrWhiteSpace(combo.Name) ? $"Unnamed combo {index + 1}" : combo.Name,
                    lower.Combos[index], upper.Combos[index])).ToImmutableArray();
            ImmutableArray<SearchAvailabilityInterval> grouped = (groups ?? []).Select((group, index) =>
                Interval(group.Name, lower.Groups[index], upper.Groups[index])).ToImmutableArray();
            if (status == SearchAvailabilityStatus.Bounded && total.IsExact && individual.All(row => row.IsExact) && grouped.All(row => row.IsExact)) {
                status = SearchAvailabilityStatus.Exact;
            }
            return new(status, detail, total, individual, grouped);
        }

        SearchAvailabilityInterval Interval(string label, ExactProbability lower, ExactProbability upperValue) {
            long cells = lower.Cells + upperValue.Cells + 1;
            budget.Spend(cells * cells + label.Length + 1);
            budget.CheckStorage(1 + combos.Count + (groups?.Count ?? 0), cells * (1 + combos.Count + (groups?.Count ?? 0)) * 4);
            if (lower.Denominator <= 0 || upperValue.Denominator <= 0 || lower.Numerator < 0
                || upperValue.Numerator > upperValue.Denominator
                || lower.Numerator * upperValue.Denominator > upperValue.Numerator * lower.Denominator) {
                throw new InvalidOperationException("Search certificate interval invariant failed.");
            }
            return new(label, Fraction(lower), Fraction(upperValue));
        }

        static ProbabilityFraction Fraction(ExactProbability value) {
            BigInteger divisor = BigInteger.GreatestCommonDivisor(value.Numerator, value.Denominator);
            return new((value.Numerator / divisor).ToString(CultureInfo.InvariantCulture),
                (value.Denominator / divisor).ToString(CultureInfo.InvariantCulture));
        }

        long SelectorWork(CardSelector selector) {
            budget.Spend(selector.CategoryIdentities.Count + 1L);
            return selector.CategoryIdentities.Count + 1L + selector.Children.Sum(SelectorWork);
        }
    }
}
