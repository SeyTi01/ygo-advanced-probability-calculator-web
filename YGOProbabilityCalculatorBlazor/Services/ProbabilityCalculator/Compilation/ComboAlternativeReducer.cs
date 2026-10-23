using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;

internal static class ComboAlternativeReducer {
    internal static IReadOnlyList<ComboAlternativeGroup> Reduce(IReadOnlyList<ComboAlternativeGroup> groups, WorkBudget budget) {
        if (groups.Count == 0) {
            return groups;
        }

        // Canonicalize merges equal requirement keys by max(min) and min(max),
        // not by adding their demands. Thus repeating the SAME choice is inert.
        // Every mixed choice across equal groups implies its choice in the first
        // group: deleting other requirements only relaxes bounds/assignment roles.
        // Conversely, repeat the first group's choice to recover the same route.
        // This remains true with a shared conjunction and restrictive maxima.
        // It does NOT permit merging different keys with equal eligible rows.
        Dictionary<AlternativeKey, int> identities = new();
        HashSet<string> seenGroups = new(StringComparer.Ordinal);
        List<ComboAlternativeGroup> reduced = [];
        long identityCells = 0;
        long groupCells = 0;
        budget.CheckStorage(groups.Count, groups.Count);

        foreach (ComboAlternativeGroup group in groups) {
            int count = group.Alternatives.Count;
            // Bound the current scratch buffers alongside retained keys/groups.
            CheckStorage(0, 0);
            HashSet<int> seenAlternatives = [];
            List<int> ids = [];
            List<ComboAlternative> alternatives = [];
            foreach (ComboAlternative alternative in group.Alternatives) {
                AlternativeKey key = alternative.Category is { } category
                    ? new(false, category.Selector.Signature, category.MinCount, category.MaxCount, category.MaximumMode)
                    : new(true, alternative.Card!.CardId, alternative.Card.MinCount, alternative.Card.MaxCount, alternative.Card.MaximumMode);
                budget.Spend(2L * key.Identity.Length + 8);
                if (!identities.TryGetValue(key, out int id)) {
                    CheckStorage(1, key.Identity.Length + 5L);
                    id = identities.Count;
                    identities.Add(key, id);
                    identityCells += key.Identity.Length + 5L;
                }

                if (seenAlternatives.Add(id)) {
                    ids.Add(id);
                    alternatives.Add(alternative);
                }
            }

            // Sort integer identities, not selector strings. The comma-delimited
            // nonnegative IDs form an unambiguous, order-independent set key.
            budget.Spend(2L * ids.Count * (1 + BitOperations.Log2((uint)ids.Count)));
            ids.Sort();
            long signatureCells = 11L * ids.Count;
            CheckStorage(1, signatureCells);
            string signature = string.Join(',', ids);
            budget.Spend(2L * signature.Length + 1);
            if (seenGroups.Add(signature)) {
                reduced.Add(alternatives.Count == count ? group : new(alternatives));
                groupCells += signature.Length + (long)alternatives.Count + 1;
            }

            void CheckStorage(long extraEntries, long extraCells) {
                budget.CheckStorage(
                    identities.Count + (long)seenGroups.Count + groups.Count + count + extraEntries,
                    identityCells + groupCells + groups.Count + 4L * count + extraCells
                );
            }
        }

        return reduced;
    }

    // Keep raw maximum mode/bounds: equivalence is independent of final hand
    // size, including when fixed/Advanced draws invoke the compiler again.
    private readonly record struct AlternativeKey(
        bool IsCard,
        string Identity,
        int Min,
        int Max,
        RequirementMaximumMode Mode
    );
}
