using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// UI-owned values only: neither a session nor the worker's read-only (possibly mutable) lists.
public sealed record PinnedComboDefinition(Guid Lineage, string Signature);
public sealed record PinnedGroupDefinition(Guid Lineage, string Signature);
public sealed record PinnedCalculationContext(int Epoch, int HandSize, int Copies, int CardCount,
    string DeckDefinition, ImmutableArray<PinnedComboDefinition> Combos,
    ImmutableDictionary<string, PinnedGroupDefinition> Groups) {
    public string Description => $"Hand size {HandSize} · {Copies} active copies · {CardCount} active cards";

    internal static PinnedCalculationContext Capture(int epoch, int handSize, IReadOnlyList<Card> cards,
        IReadOnlyList<Combo> combos, IReadOnlyList<ComboGroup> groups,
        Func<Combo, Guid> comboLineage, Func<string, Guid> groupLineage) {
        // Sorted structured JSON avoids delimiter collisions; repeated constraints remain repeated.
        // Counts/active flags are deck context. Category membership is part of a route definition.
        string Signature(Combo combo) => Pack([combo.GroupId ?? "",
            Pack(combo.Categories.Select(c => Pack(["category", c.BaseCategory.Identity, Number(c.MinCount),
                c.MaximumMode.ToString(), Number(c.MaximumMode == RequirementMaximumMode.Fixed ? c.MaxCount : 0),
                Pack(cards.Where(card => card.Categories.Any(x => x.Identity == c.BaseCategory.Identity))
                    .Select(card => card.Id).Order(StringComparer.Ordinal))])).Order(StringComparer.Ordinal)),
            Pack(combo.Cards.Select(c => Pack(["card", c.CardId, Number(c.MinCount), c.MaximumMode.ToString(),
                Number(c.MaximumMode == RequirementMaximumMode.Fixed ? c.MaxCount : 0)])).Order(StringComparer.Ordinal))]);
        var active = combos.Where(c => c.Active).ToArray();
        var definitions = active.Select(c => new PinnedComboDefinition(comboLineage(c), Signature(c))).ToImmutableArray();
        var groupDefinitions = groups.ToImmutableDictionary(g => g.Id, g => new PinnedGroupDefinition(groupLineage(g.Id),
            Pack(active.Select((c, i) => (Combo: c, Definition: definitions[i]))
                .Where(x => x.Combo.GroupId == g.Id).Select(x => Pack([x.Definition.Lineage.ToString("N"), x.Definition.Signature]))
                .Order(StringComparer.Ordinal))), StringComparer.Ordinal);
        var deck = cards.Where(c => c.Active).OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        return new(epoch, handSize, deck.Sum(c => c.Copies), deck.Length,
            Pack(deck.Select(c => Pack([c.Id, c.Name ?? "", Number(c.Copies),
                Pack(c.Categories.Select(x => x.Identity).Order(StringComparer.Ordinal))]))), definitions, groupDefinitions);
    }
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Pack(IEnumerable<string> values) => JsonSerializer.Serialize(values.ToArray(), PinnedSignatureJsonContext.Default.StringArray);
}

[JsonSerializable(typeof(string[]))]
internal partial class PinnedSignatureJsonContext : JsonSerializerContext;

public sealed record PinnedComboRow(int Index, string Label, double Probability, string? GroupId,
    PinnedComboDefinition? Definition);
public sealed record PinnedGroupRow(string Id, string Label, double Probability, int ActiveCount,
    PinnedGroupDefinition? Definition);
public sealed record PinnedResultDifference(decimal RoundedPercentagePoints, string Text) {
    public string CssClass => RoundedPercentagePoints switch {
        > 0 => "result-difference-positive",
        < 0 => "result-difference-negative",
        _ => "result-difference-neutral"
    };
}
public sealed record PinnedResultComparison(string? Status, PinnedResultDifference? Difference) {
    public string Text => Difference?.Text ?? Status ?? "Not comparable";
    public static PinnedResultComparison ForStatus(string status) => new(status, null);
    public static PinnedResultComparison ForDifference(PinnedResultDifference difference) => new(null, difference);
}

public sealed record PinnedResultSnapshot(PinnedCalculationContext Context, double Total,
    ImmutableArray<PinnedComboRow> Combos, ImmutableArray<PinnedGroupRow> Groups) {
    public static PinnedResultSnapshot Capture(PinnedCalculationContext context, ProbabilityCalculationResult result) => new(
        context, result.TotalProbability,
        result.ComboProbabilities.Select(c => new PinnedComboRow(c.ComboIndex,
            string.IsNullOrWhiteSpace(c.ComboName) ? $"Unnamed combo {c.ComboIndex + 1}" : c.ComboName,
            c.Probability, c.GroupId, c.ComboIndex >= 0 && c.ComboIndex < context.Combos.Length
                ? context.Combos[c.ComboIndex] : null)).ToImmutableArray(),
        (result.GroupProbabilities ?? []).Select(g => new PinnedGroupRow(g.GroupId, g.GroupName, g.Probability,
            g.ActiveComboCount, context.Groups.GetValueOrDefault(g.GroupId))).ToImmutableArray());

    public bool IsValid => Valid(Total) && Combos.All(c => Valid(c.Probability)) && Groups.All(g => Valid(g.Probability));
    private static bool Valid(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    // Lineage is explicitly transferred by supported edits. Never infer continuity from names,
    // indices, identical requirements, or group IDs across accepted session/deck replacements.
    public PinnedResultComparison CompareComboPresentation(PinnedComboRow row, PinnedResultSnapshot other, bool currentSide) {
        if (Context.Epoch != other.Context.Epoch) return PinnedResultComparison.ForStatus("Unrelated session");
        var matches = other.Combos.Where(c => row.Definition is not null && c.Definition?.Lineage == row.Definition.Lineage).ToArray();
        if (matches.Length != 1) return PinnedResultComparison.ForStatus(currentSide ? "New route" : "Removed or inactive");
        if (row.Definition!.Signature != matches[0].Definition!.Signature) return PinnedResultComparison.ForStatus("Definition changed");
        return currentSide ? CompareDifference(row.Probability, matches[0].Probability) : PinnedResultComparison.ForStatus("Comparable");
    }

    public string CompareCombo(PinnedComboRow row, PinnedResultSnapshot other, bool currentSide) => CompareComboPresentation(row, other, currentSide).Text;

    public PinnedResultComparison CompareGroupPresentation(PinnedGroupRow row, PinnedResultSnapshot other, bool currentSide) {
        if (Context.Epoch != other.Context.Epoch) return PinnedResultComparison.ForStatus("Unrelated session");
        var matches = other.Groups.Where(g => row.Definition is not null && g.Definition?.Lineage == row.Definition.Lineage).ToArray();
        if (matches.Length != 1) return PinnedResultComparison.ForStatus(currentSide ? "New group" : "Removed group");
        if (row.Definition!.Signature != matches[0].Definition!.Signature) return PinnedResultComparison.ForStatus("Composition changed");
        return currentSide ? CompareDifference(row.Probability, matches[0].Probability) : PinnedResultComparison.ForStatus("Comparable");
    }

    public string CompareGroup(PinnedGroupRow row, PinnedResultSnapshot other, bool currentSide) => CompareGroupPresentation(row, other, currentSide).Text;

    public static string Difference(double current, double pinned) => DifferencePresentation(current, pinned)?.Text ?? "Not comparable";

    public static PinnedResultDifference? DifferencePresentation(double current, double pinned) {
        if (!Valid(current) || !Valid(pinned)) return null;
        var rounded = Math.Round(((decimal)current - (decimal)pinned) * 100, 2, MidpointRounding.AwayFromZero);
        var format = CultureInfo.CurrentCulture;
        var number = Math.Abs(rounded).ToString("0.##", format);
        var symbol = format.NumberFormat.PercentSymbol;
        string text = rounded switch {
            > 0 => format.NumberFormat.PercentPositivePattern switch {
                0 => $"+{number} {symbol}",
                1 => $"+{number}{symbol}",
                2 => $"{symbol}+{number}",
                _ => $"{symbol} +{number}"
            },
            < 0 => format.NumberFormat.PercentNegativePattern switch {
                0 => $"-{number} {symbol}",
                1 => $"-{number}{symbol}",
                2 => $"-{symbol}{number}",
                3 => $"{symbol}-{number}",
                4 => $"{symbol}{number}-",
                5 => $"{number}-{symbol}",
                6 => $"{number}{symbol}-",
                7 => $"-{number}{symbol}",
                8 => $"{number} {symbol}-",
                9 => $"{symbol} {number}-",
                10 => $"{symbol} -{number}",
                _ => $"{number}- {symbol}"
            },
            _ => format.NumberFormat.PercentPositivePattern switch {
                0 => $"{number} {symbol}",
                1 => $"{number}{symbol}",
                2 => $"{symbol}{number}",
                _ => $"{symbol} {number}"
            }
        };
        return new(rounded, text);
    }

    private static PinnedResultComparison CompareDifference(double current, double pinned) =>
        DifferencePresentation(current, pinned) is { } difference
            ? PinnedResultComparison.ForDifference(difference)
            : PinnedResultComparison.ForStatus("Not comparable");
}
