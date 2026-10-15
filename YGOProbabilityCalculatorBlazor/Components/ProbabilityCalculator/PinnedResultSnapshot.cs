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

public sealed record PinnedCalculationContext(
    int Epoch,
    int HandSize,
    int Copies,
    int CardCount,
    string DeckDefinition,
    ImmutableArray<PinnedComboDefinition> Combos,
    ImmutableDictionary<string, PinnedGroupDefinition> Groups,
    bool DrawEffectsEnabled = true,
    string DrawPriorityDefinition = ""
) {
    public CalculationModel Model => CalculationModelPresentation.FromDrawEffects(DrawEffectsEnabled);
    public string Description => $"Hand size {HandSize} · {Copies} active copies · {CardCount} active cards · {Model.Description()}{(DrawPriorityDefinition.Length > 0 ? " · Custom draw priority" : "")}";

    internal static PinnedCalculationContext Capture(
        int epoch,
        int handSize,
        IReadOnlyList<Card> cards,
        IReadOnlyList<Combo> combos,
        IReadOnlyList<ComboGroup> groups,
        Func<Combo, Guid> comboLineage,
        Func<string, Guid> groupLineage,
        bool drawEffectsEnabled = true,
        IEnumerable<CategoryBase>? categoryDefinitions = null,
        DrawActivationPriority? priority = null
    ) {
        Combo[] active = [.. combos.Where(combo => combo.Active)];
        ImmutableArray<PinnedComboDefinition> definitions = [.. active.Select(combo => new PinnedComboDefinition(comboLineage(combo), Signature(combo)))];

        ImmutableDictionary<string, PinnedGroupDefinition> groupDefinitions = groups.ToImmutableDictionary(
            group => group.Id,
            group => {
                Guid lineage = groupLineage(group.Id);
                string signature = Pack(active
                    .Select((combo, index) => (Combo: combo, Definition: definitions[index]))
                    .Where(entry => entry.Combo.GroupId == group.Id)
                    .Select(entry => Pack([entry.Definition.Lineage.ToString("N"), entry.Definition.Signature]))
                    .Order(StringComparer.Ordinal));

                return new PinnedGroupDefinition(lineage, signature);
            },
            StringComparer.Ordinal
        );

        Card[] deck = [.. cards.Where(card => card.Active).OrderBy(card => card.Id, StringComparer.Ordinal)];
        int copies = deck.Sum(card => card.Copies);

        string deckDefinition = Pack(deck.Select(card => Pack([
            card.Id,
            card.Name ?? "",
            Number(card.Copies),
            card.Search is { } search ? $"search:{search.ObjectiveMetadataOnly}:{card.OncePerTurn}:{search.Selector.Signature}" : "",
            deck.Any(entry => entry.Search is not null) ? Pack([card.ExternalCardId?.ToString() ?? "", .. card.ManualMetadataCategoryKeys.Order(StringComparer.Ordinal)]) : "",
            card.DrawCount is { } drawCount ? Number(drawCount) : "",
            card.DrawCount is not null && !card.DrawOncePerTurn ? "every-copy" : "",
            card.EffectProfile.ExecutionModel != EffectExecutionModel.DrawSequenceV1 ? $"execution-model:{card.EffectProfile.ExecutionModel}" : "",
            card.FirstActivationOnly ? "first-activation-only" : "",
            card.BlocksLaterDraws ? "blocks-later-draws" : "",
            card.PostDrawDeckMillCount > 0 ? $"deck-mill:{Number(card.PostDrawDeckMillCount)}" : "",
            card.PreDrawDeckRemovalCount > 0 ? $"deck-cost:{Number(card.PreDrawDeckRemovalCount)}" : "",
            card.PreDrawRemovalCount > 0 ? $"cost:{Number(card.PreDrawRemovalCount)}:{card.PreDrawSelector.Signature}" : "",
            card.PostDrawDiscardCount > 0 ? $"discard:{Number(card.PostDrawDiscardCount)}" : "",
            card.PostDrawSelector.Kind == CardSelectorKind.Any ? "" : card.PostDrawSelector.Signature,
            card.PostDrawDiscardCount > 0 ? card.PostDrawFailureBehavior.ToString() : "",
            Pack(card.Categories.Select(category => category.Identity).Order(StringComparer.Ordinal))
        ])));

        string[] rules = (categoryDefinitions ?? []).Where(category => category.Rule is not null)
            .Select(category => Pack([category.Identity, category.Rule!.Signature])).Order(StringComparer.Ordinal).ToArray();
        if (rules.Length > 0) {
            deckDefinition = Pack([deckDefinition, Pack(rules)]);
        }

        string priorityDefinition = drawEffectsEnabled && priority?.Mode == DrawActivationMode.CustomPriority
            ? Pack(["custom", .. priority.CardIds.Where(id => deck.Any(card => card.Id == id && card.Copies > 0 && card.DrawCount is not null))]) : "";
        if (priorityDefinition.Length > 0) {
            deckDefinition = Pack([deckDefinition, priorityDefinition]);
        }

        return new(epoch, handSize, copies, deck.Length, deckDefinition, definitions, groupDefinitions, drawEffectsEnabled, priorityDefinition);

        string CardRequirementSignature(ComboCard card) {
            int maximum = card.MaximumMode == RequirementMaximumMode.Fixed ? card.MaxCount : 0;

            return Pack([
                "card",
                card.CardId,
                Number(card.MinCount),
                card.MaximumMode.ToString(),
                Number(maximum)
            ]);
        }

        string Signature(Combo combo) {
            string groupId = combo.GroupId ?? "";

            string categorySignatures = Pack(combo.Categories
                .Select(CategoryRequirementSignature)
                .Order(StringComparer.Ordinal));

            string cardSignatures = Pack(combo.Cards
                .Select(CardRequirementSignature)
                .Order(StringComparer.Ordinal));

            string alternativeGroupSignatures = Pack(combo.AlternativeGroups
                .Select(group => Pack(group.Alternatives
                    .Select(AlternativeSignature)
                    .Order(StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal));

            return Pack([groupId, categorySignatures, cardSignatures, alternativeGroupSignatures]);
        }

        string AlternativeSignature(ComboAlternative alternative) => alternative.Category is { } category
            ? CategoryRequirementSignature(category)
            : CardRequirementSignature(alternative.Card!);

        // Sorted structured JSON avoids delimiter collisions; repeated constraints remain repeated.
        // Counts/active flags are deck context. Category membership is part of a route definition.
        string CategoryRequirementSignature(ComboCategory category) {
            int maximum = category.MaximumMode == RequirementMaximumMode.Fixed ? category.MaxCount : 0;

            string memberCardIds = Pack(cards
                .Where(card => category.Selector.Matches(card))
                .Select(card => card.Id)
                .Order(StringComparer.Ordinal));

            return Pack([
                "category",
                category.Selector.Signature,
                Number(category.MinCount),
                category.MaximumMode.ToString(),
                Number(maximum),
                memberCardIds
            ]);
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Pack(IEnumerable<string> values) => JsonSerializer.Serialize([.. values], PinnedSignatureJsonContext.Default.StringArray);
}

[JsonSerializable(typeof(string[]))]
internal partial class PinnedSignatureJsonContext : JsonSerializerContext;

public sealed record PinnedComboRow(int Index, string Label, double Probability, string? GroupId, PinnedComboDefinition? Definition);

public sealed record PinnedGroupRow(string Id, string Label, double Probability, int ActiveCount, PinnedGroupDefinition? Definition);

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

public sealed record PinnedResultSnapshot(PinnedCalculationContext Context, double Total, ImmutableArray<PinnedComboRow> Combos, ImmutableArray<PinnedGroupRow> Groups) {
    public SearchAvailabilityCertificate? SearchAvailability { get; init; }

    public static PinnedResultSnapshot Capture(PinnedCalculationContext context, ProbabilityCalculationResult result) {
        ImmutableArray<PinnedComboRow> pinnedComboRows = result.ComboProbabilities
            .Select(combo => {
                PinnedComboDefinition? pinnedComboDefinition = combo.ComboIndex >= 0 && combo.ComboIndex < context.Combos.Length
                    ? context.Combos[combo.ComboIndex]
                    : null;

                return new PinnedComboRow(
                    combo.ComboIndex,
                    string.IsNullOrWhiteSpace(combo.ComboName) ? $"Unnamed combo {combo.ComboIndex + 1}" : combo.ComboName,
                    combo.Probability,
                    combo.GroupId,
                    pinnedComboDefinition
                );
            })
            .ToImmutableArray();

        ImmutableArray<PinnedGroupRow> pinnedGroupRows = (result.GroupProbabilities ?? [])
            .Select(group => new PinnedGroupRow(
                group.GroupId,
                group.GroupName,
                group.Probability,
                group.ActiveComboCount,
                context.Groups.GetValueOrDefault(group.GroupId)
            ))
            .ToImmutableArray();

        return new(context, result.TotalProbability, pinnedComboRows, pinnedGroupRows) { SearchAvailability = result.SearchAvailability };
    }

    public bool IsValid => Valid(Total) && Combos.All(combo => Valid(combo.Probability)) && Groups.All(group => Valid(group.Probability));

    private static bool Valid(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    // Lineage is explicitly transferred by supported edits. Never infer continuity from names,
    // indices, identical requirements, or group IDs across accepted session/deck replacements.
    public PinnedResultComparison CompareComboPresentation(PinnedComboRow row, PinnedResultSnapshot other, bool currentSide) {
        if (Context.Epoch != other.Context.Epoch) {
            return PinnedResultComparison.ForStatus("Unrelated session");
        }

        PinnedComboRow[] matches = other.Combos
            .Where(combo => row.Definition is not null && combo.Definition?.Lineage == row.Definition.Lineage)
            .ToArray();

        if (matches.Length != 1) {
            return PinnedResultComparison.ForStatus(currentSide ? "New route" : "Removed or inactive");
        }

        if (row.Definition!.Signature != matches[0].Definition!.Signature) {
            return PinnedResultComparison.ForStatus("Definition changed");
        }

        return currentSide ? CompareDifference(row.Probability, matches[0].Probability) : PinnedResultComparison.ForStatus("Comparable");
    }

    public string CompareCombo(PinnedComboRow row, PinnedResultSnapshot other, bool currentSide) => CompareComboPresentation(row, other, currentSide).Text;

    public PinnedResultComparison CompareGroupPresentation(PinnedGroupRow row, PinnedResultSnapshot other, bool currentSide) {
        if (Context.Epoch != other.Context.Epoch) {
            return PinnedResultComparison.ForStatus("Unrelated session");
        }

        PinnedGroupRow[] matches = other.Groups
            .Where(group => row.Definition is not null && group.Definition?.Lineage == row.Definition.Lineage)
            .ToArray();

        if (matches.Length != 1) {
            return PinnedResultComparison.ForStatus(currentSide ? "New group" : "Removed group");
        }

        if (row.Definition!.Signature != matches[0].Definition!.Signature) {
            return PinnedResultComparison.ForStatus("Composition changed");
        }

        return currentSide ? CompareDifference(row.Probability, matches[0].Probability) : PinnedResultComparison.ForStatus("Comparable");
    }

    public string CompareGroup(PinnedGroupRow row, PinnedResultSnapshot other, bool currentSide) => CompareGroupPresentation(row, other, currentSide).Text;

    public static string Difference(double current, double pinned) => DifferencePresentation(current, pinned)?.Text ?? "Not comparable";

    public static PinnedResultDifference? DifferencePresentation(double current, double pinned) {
        if (!Valid(current) || !Valid(pinned)) {
            return null;
        }

        decimal rounded = Math.Round(((decimal)current - (decimal)pinned) * 100, 2, MidpointRounding.AwayFromZero);
        CultureInfo format = CultureInfo.CurrentCulture;
        string number = Math.Abs(rounded).ToString("0.##", format);
        string symbol = format.NumberFormat.PercentSymbol;

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

    private static PinnedResultComparison CompareDifference(double current, double pinned) {
        PinnedResultDifference? difference = DifferencePresentation(current, pinned);

        return difference is not null
            ? PinnedResultComparison.ForDifference(difference)
            : PinnedResultComparison.ForStatus("Not comparable");
    }
}
