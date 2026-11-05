using System.Globalization;
using CardProbability;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;

namespace YGOProbabilityCalculatorBlazor.Services.CalculationEngineAdapter;

// Resolve application predicates once. The engine receives only opaque identities
// and explicit entry sets, never catalog metadata or editor/session objects.
internal sealed class CalculationRequestAdapter {
    private readonly Card[] _cards;
    private readonly HashSet<string> _entryIds;
    private readonly Dictionary<string, string> _membershipIds = new(StringComparer.Ordinal);
    private readonly List<Membership> _memberships = [];

    internal CalculationRequestAdapter(IEnumerable<Card> cards, bool effectsEnabled) {
        Card[] projected = cards.Select(card => card.ForCalculation(effectsEnabled)).ToArray();
        HashSet<string> suppliedIds = projected.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
        if (suppliedIds.Count != projected.Length) {
            throw new ArgumentException("Deck card IDs must be unique.", nameof(cards));
        }
        foreach (Card card in projected) {
            CardEffectCompiler.RequireExecutable(card);
            ArgumentOutOfRangeException.ThrowIfNegative(card.Copies);
        }
        // Historical native calls accept every supplied ordinary/draw entry.
        // Only the search/excavation dispatcher projects the deck to active,
        // positive entries. Resolve that host convention before entering the engine.
        bool searchPhase = projected.Any(card => card.Active && card.Copies > 0
            && (card.Search is not null || card.Excavation is not null));
        _cards = searchPhase ? projected.Where(card => card.Active && card.Copies > 0).ToArray() : projected;
        // Preserve the native service's checked population overflow exception.
        // Direct generic callers receive the engine's typed Unsupported result.
        _ = _cards.Sum(card => card.Copies);
        _entryIds = _cards.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
    }

    internal CalculationRequest Create(IReadOnlyList<Combo> combos, int handSize, IReadOnlyList<ComboGroup>? groups = null) {
        HashSet<string> groupIds = (groups ?? []).Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        // The fixed keep/removal policy orders the original atomic goal roles.
        // Resolved predicate tokens can have a different order or collapse several
        // atoms; preserve this application policy independently of solver membership.
        CategoryBase[] policyCategories = combos.SelectMany(combo => combo.AllCategories)
            .SelectMany(requirement => requirement.CategoryBases).Distinct()
            .OrderBy(category => category.Identity, StringComparer.Ordinal).ToArray();
        string[] policyCards = combos.SelectMany(combo => combo.AllCards).Select(requirement => requirement.CardId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        DeckEntry[] entries = _cards.Select(card => new DeckEntry(
            card.Id,
            card.Copies,
            TranslateEffect(card),
            string.Concat(policyCategories.Select(category => card.Categories.Contains(category) ? '1' : '0'))
                + string.Concat(policyCards.Select(id => card.Id == id ? '1' : '0'))
        )).ToArray();
        Goal[] goals = combos.Select((combo, index) => new Goal(
            index.ToString(CultureInfo.InvariantCulture),
            combo.Categories.Select(Translate)
                .Concat(combo.Cards.Select(Translate)),
            combo.AlternativeGroups.Select(group => new Alternative(group.Alternatives.Select(alternative =>
                alternative.Category is { } category ? Translate(category) : Translate(alternative.Card!)))),
            combo.GroupId is { } groupId && groupIds.Contains(groupId) ? groupId : null
        )).ToArray();

        return new(entries, handSize, goals, _memberships, (groups ?? []).Select(group => group.Id));
    }

    private Requirement Translate(ComboCategory requirement) => Requirement.InMembership(
        MembershipFor(requirement.Selector), requirement.MinCount, Maximum(requirement.MaximumMode, requirement.MaxCount));

    private Requirement Translate(ComboCard requirement) => _entryIds.Contains(requirement.CardId)
        ? Requirement.OfEntry(requirement.CardId, requirement.MinCount, Maximum(requirement.MaximumMode, requirement.MaxCount))
        : Requirement.InMembership(ResolveMembership("missing:" + requirement.CardId, []), requirement.MinCount, Maximum(requirement.MaximumMode, requirement.MaxCount));

    private static int? Maximum(RequirementMaximumMode mode, int maximum) => mode == RequirementMaximumMode.HandSize ? null : maximum;

    private string MembershipFor(CardSelector selector) => ResolveMembership(
        "selector:" + selector.Signature, _cards.Where(selector.Matches).Select(card => card.Id));

    private string ResolveMembership(string signature, IEnumerable<string> entryIds) {
        if (_membershipIds.TryGetValue(signature, out string? existing)) {
            return existing;
        }
        string id = "m" + _memberships.Count.ToString(CultureInfo.InvariantCulture);
        _membershipIds.Add(signature, id);
        _memberships.Add(new(id, entryIds));

        return id;
    }

    private Eligibility EligibilityFor(CardSelector selector) => selector.Kind == CardSelectorKind.Any
        ? Eligibility.Any : new(MembershipFor(selector));

    private Eligibility SearchEligibility(SearchEffectPlan search, CardSelector selector) => new(ResolveMembership(
        (search.ObjectiveMetadataOnly ? "objective-search:" : "search:") + selector.Signature,
        _cards.Where(card => search.Matches(card, selector)).Select(card => card.Id)));

    private EffectPlan? TranslateEffect(Card card) {
        if (!card.HasEffect || !card.Active && card.DrawCount is null) {
            return null;
        }
        DrawEffectPlan plan = card.EffectPlan;
        SearchOperation? search = plan.Search is { } savedSearch ? new(
            SearchEligibility(savedSearch, savedSearch.Selector),
            savedSearch.Modes?.Select(mode => new SearchMode(SearchEligibility(savedSearch, mode.Selector), mode.ActivationLimit))) : null;

        return new(
            plan.DrawCount,
            card.MaximumActivations == int.MaxValue ? null : card.MaximumActivations,
            plan.HandCost,
            EligibilityFor(plan.HandCostSelector),
            plan.HiddenDeckCost,
            plan.RemovalCount,
            EligibilityFor(plan.RemovalSelector),
            plan.HiddenPostDrawMill,
            plan.FirstActivationOnly,
            plan.BlocksLaterDraws,
            plan.MinimumHandSize,
            search,
            plan.Excavation is { } excavation ? new(excavation.Count, excavation.RequiresNoPriorEffectDraw) : null
        );
    }
}
