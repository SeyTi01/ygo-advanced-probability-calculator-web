using System.Collections.Frozen;

namespace YGOProbabilityCalculatorBlazor.Models;

public class Card {
    public string Id { get; }
    public int Copies { get; }
    public List<CategoryBase> Categories { get; }
    public string? Name { get; }
    public bool Active { get; }
    public bool SimulationEnabled { get; }
    public int? ExternalCardId { get; }
    public DrawEffectPlan EffectPlan { get; }
    public SearchEffectPlan? Search => EffectPlan.Search;
    public bool IsDangerReveal => EffectPlan.Operation == DrawOperation.DangerReveal;
    public bool HasDrawEffect => DrawCount is not null || IsDangerReveal;
    public int DrawBatchSize => IsDangerReveal ? 1 : DrawCount ?? 0;
    public bool HasEffect => HasDrawEffect || Search is not null;
    public EffectProfile EffectProfile { get; }
    public bool FirstActivationOnly => EffectPlan.FirstActivationOnly;
    public bool BlocksLaterDraws => EffectPlan.BlocksLaterDraws;
    public int? DrawCount => EffectPlan.DrawCount;
    public bool OncePerTurn => EffectPlan.OncePerTurn;
    public int MaximumActivations => EffectPlan.MaximumActivations;
    public int MinimumHandSize => EffectPlan.MinimumHandSize;
    // Legacy API/wire name for the shared plan flag.
    public bool DrawOncePerTurn => OncePerTurn;
    public int PreDrawDeckRemovalCount => EffectPlan.HiddenDeckCost;
    public int PostDrawDeckMillCount => EffectPlan.HiddenPostDrawMill;
    public int PreDrawRemovalCount => EffectPlan.HandCost;
    public CardSelector PreDrawSelector => EffectPlan.HandCostSelector;
    public int PostDrawDiscardCount => EffectPlan.RemovalCount;
    public CardSelector PostDrawSelector => EffectPlan.RemovalSelector;
    public PostDrawFailureBehavior PostDrawFailureBehavior => EffectPlan.FailureBehavior;
    public IReadOnlySet<string> ManualMetadataCategoryKeys { get; }

    public Card(
        IEnumerable<CategoryBase> categories,
        int copies = 1,
        string? name = null,
        bool active = true,
        string? id = null,
        int? externalCardId = null,
        IEnumerable<string>? manualMetadataCategoryKeys = null,
        int? drawCount = null,
        bool drawOncePerTurn = true,
        int postDrawDiscardCount = 0,
        CardSelector? postDrawSelector = null,
        PostDrawFailureBehavior postDrawFailureBehavior = PostDrawFailureBehavior.FailBranch,
        int preDrawRemovalCount = 0,
        CardSelector? preDrawSelector = null,
        int preDrawDeckRemovalCount = 0,
        EffectProfile? effectProfile = null,
        int postDrawDeckMillCount = 0,
        bool firstActivationOnly = false,
        bool blocksLaterDraws = false,
        SearchEffectPlan? search = null,
        bool? oncePerTurn = null,
        int? activationLimit = null,
        int minimumHandSize = 0,
        bool simulationEnabled = true,
        DrawOperation drawOperation = DrawOperation.Ordinary
    ) {
        if (!Enum.IsDefined(drawOperation)) {
            throw new ArgumentOutOfRangeException(nameof(drawOperation), "Unsupported saved draw operation.");
        }
        if (drawOperation == DrawOperation.DangerReveal && (drawCount is not null || search is not null
            || (oncePerTurn ?? drawOncePerTurn) || activationLimit is not null || minimumHandSize != 0
            || preDrawRemovalCount != 0 || postDrawDiscardCount != 0 || preDrawDeckRemovalCount != 0
            || postDrawDeckMillCount != 0 || firstActivationOnly || blocksLaterDraws)) {
            throw new ArgumentException("Danger reveal is a repeatable base operation and cannot contain ordinary draw/search settings.", nameof(drawOperation));
        }
        if (drawCount is not null and not (>= 1 and <= 3)) {
            throw new ArgumentOutOfRangeException(nameof(drawCount), "Draw count must be 1, 2, 3, or absent.");
        }

        if (postDrawDiscardCount is < 0 or > 2 || (postDrawDiscardCount > 0 && drawCount is null && search is null)) {
            throw new ArgumentOutOfRangeException(nameof(postDrawDiscardCount), "Hand removal requires a draw or search effect and must be 0, 1, or 2.");
        }

        if (!Enum.IsDefined(postDrawFailureBehavior)) {
            throw new ArgumentOutOfRangeException(nameof(postDrawFailureBehavior));
        }
        if (postDrawSelector is { Kind: not CardSelectorKind.Any } && postDrawDiscardCount == 0) {
            throw new ArgumentException("Filtered removal requires a positive post-draw count.", nameof(postDrawSelector));
        }

        if (preDrawRemovalCount is < 0 or > 2 || (preDrawRemovalCount > 0 && drawCount is null && search is null)) {
            throw new ArgumentOutOfRangeException(nameof(preDrawRemovalCount), "Hand removal requires a draw or search effect and must be 0, 1, or 2.");
        }
        if (preDrawSelector is { Kind: not CardSelectorKind.Any } && preDrawRemovalCount == 0) {
            throw new ArgumentException("Filtered removal requires a positive pre-draw count.", nameof(preDrawSelector));
        }

        if (preDrawDeckRemovalCount is < 0 or > 60 || (preDrawDeckRemovalCount > 0 && drawCount is null)) {
            throw new ArgumentOutOfRangeException(nameof(preDrawDeckRemovalCount), "Hidden deck removal requires a draw effect and must be between 0 and 60.");
        }

        if (postDrawDeckMillCount is < 0 or > 60 || (postDrawDeckMillCount > 0 && drawCount is null)) {
            throw new ArgumentOutOfRangeException(nameof(postDrawDeckMillCount), "Post-draw milling requires a draw effect and must be between 0 and 60.");
        }

        if (drawCount is null && (firstActivationOnly || blocksLaterDraws)) {
            throw new ArgumentException("Activation restrictions require a draw effect.");
        }

        if (search is not null && (drawCount is not null || search.Selector is null)) {
            throw new ArgumentException("Search must have a selector and cannot share a source with a draw operation.", nameof(search));
        }

        if (activationLimit is < 1 or > 60 || minimumHandSize is < 0 or > 60
            || ((activationLimit is not null || minimumHandSize > 0) && drawCount is null && search is null)) {
            throw new ArgumentException("Activation limits require an effect and must fit the supported range.");
        }
        search?.Validate();

        // The old draw flag was ignored for searches. Only an explicit shared
        // flag opts a search snapshot into OPT; absent values stay unrestricted.
        EffectPlan = new(drawCount, oncePerTurn ?? (search is null && drawOncePerTurn), preDrawRemovalCount, preDrawSelector ?? CardSelector.Any,
            preDrawDeckRemovalCount, postDrawDiscardCount, postDrawSelector ?? CardSelector.Any, postDrawFailureBehavior, postDrawDeckMillCount, firstActivationOnly, blocksLaterDraws, search, activationLimit, minimumHandSize, drawOperation);
        EffectProfile = effectProfile ?? EffectProfile.Legacy;
        if (!Enum.IsDefined(EffectProfile.Source) || !Enum.IsDefined(EffectProfile.Support)
            || EffectProfile.CanonicalCardId is <= 0 || EffectProfile.Revision is <= 0
            || (EffectProfile.Source == EffectSource.BuiltIn && (EffectProfile.CanonicalCardId is null
                || EffectProfile.Revision is null || string.IsNullOrWhiteSpace(EffectProfile.Summary)))) {
            throw new ArgumentException("Invalid effect profile provenance.", nameof(effectProfile));
        }

        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        Copies = copies;
        Categories = categories.ToList();
        Name = name;
        Active = active;
        SimulationEnabled = simulationEnabled;
        ExternalCardId = externalCardId;
        ManualMetadataCategoryKeys = ValidateManualKeys(Categories, manualMetadataCategoryKeys);
    }

    // Internal edits carry a complete plan instead of independently forwarding
    // every legacy effect field. Keep the public constructor as the validation
    // boundary, including its exception names and legacy search-OPT semantics.
    private Card(
        IEnumerable<CategoryBase> categories,
        int copies,
        string? name,
        bool active,
        string? id,
        int? externalCardId,
        IEnumerable<string> manualMetadataCategoryKeys,
        DrawEffectPlan plan,
        EffectProfile profile,
        bool simulationEnabled
    ) : this(
        categories,
        copies,
        name,
        active,
        id,
        externalCardId,
        manualMetadataCategoryKeys,
        plan.DrawCount,
        plan.OncePerTurn,
        plan.RemovalCount,
        plan.RemovalSelector,
        plan.FailureBehavior,
        plan.HandCost,
        plan.HandCostSelector,
        plan.HiddenDeckCost,
        profile,
        plan.HiddenPostDrawMill,
        plan.FirstActivationOnly,
        plan.BlocksLaterDraws,
        plan.Search,
        plan.OncePerTurn,
        plan.ActivationLimit,
        plan.MinimumHandSize,
        simulationEnabled,
        plan.Operation
    ) { }

    public Card Duplicate() => new(Categories, Copies, Name, Active, null, ExternalCardId, ManualMetadataCategoryKeys, EffectPlan, EffectProfile, SimulationEnabled);

    public Card WithPostDrawDeckMillCount(int count) => WithEffect(EffectPlan with { HiddenPostDrawMill = count }, EffectProfile.AsCustom());

    public Card WithPreDrawDeckRemovalCount(int count) => WithEffect(EffectPlan with { HiddenDeckCost = count }, EffectProfile.AsCustom());

    public Card WithPreDrawRemovalCount(int count) => WithEffect(EffectPlan with {
        HandCost = count,
        HandCostSelector = count == 0 ? CardSelector.Any : PreDrawSelector
    }, EffectProfile.AsCustom());

    public Card WithPreDrawSelector(CardSelector selector) => WithEffect(EffectPlan with { HandCostSelector = selector }, EffectProfile.AsCustom());

    public Card WithPostDrawDiscardCount(int count) => WithEffect(EffectPlan with {
        RemovalCount = count,
        RemovalSelector = count == 0 ? CardSelector.Any : PostDrawSelector
    }, EffectProfile.AsCustom());

    public Card WithPostDrawSelector(CardSelector selector) => WithEffect(EffectPlan with { RemovalSelector = selector }, EffectProfile.AsCustom());

    public Card WithPostDrawFailureBehavior(PostDrawFailureBehavior behavior) => WithEffect(EffectPlan with { FailureBehavior = behavior }, EffectProfile.AsCustom());

    public Card WithName(string? name) => new(Categories, Copies, name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys, EffectPlan, EffectProfile, SimulationEnabled);

    // Counting repeatedly changes multiplicities on already validated snapshots.
    // Copy mutable categories, but reuse immutable plans and frozen provenance;
    // revalidating and normalizing those on every scenario only adds allocations.
    private Card(Card source, int copies, bool? simulationEnabled = null) {
        Id = source.Id;
        Copies = copies;
        Categories = [.. source.Categories];
        Name = source.Name;
        Active = source.Active;
        SimulationEnabled = simulationEnabled ?? source.SimulationEnabled;
        ExternalCardId = source.ExternalCardId;
        EffectPlan = source.EffectPlan;
        EffectProfile = source.EffectProfile;
        ManualMetadataCategoryKeys = source.ManualMetadataCategoryKeys;
        foreach (string key in ManualMetadataCategoryKeys) {
            if (!Categories.Any(category => category.Source == CategorySource.Metadata && category.MetadataKey == key)) {
                throw new ArgumentException("Manual card properties must refer to effective metadata categories.", "keys");
            }
        }
    }

    public Card WithSimulationEnabled(bool enabled) => new(this, Copies, enabled);

    // Calculation inputs own an effect-free projection; saved plans stay authoritative.
    internal Card ForCalculation(bool effectsEnabled = true) => effectsEnabled && SimulationEnabled
        ? this
        : new(Categories, Copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys);

    public Card WithCopies(int copies) => new(this, copies);

    public Card WithDrawCount(int? drawCount) {
        bool hasEffect = drawCount is not null || Search is not null;
        return WithEffect(EffectPlan with {
            DrawCount = drawCount,
            RemovalCount = hasEffect ? PostDrawDiscardCount : 0,
            RemovalSelector = hasEffect ? PostDrawSelector : CardSelector.Any,
            HandCost = hasEffect ? PreDrawRemovalCount : 0,
            HandCostSelector = hasEffect ? PreDrawSelector : CardSelector.Any,
            HiddenDeckCost = drawCount is null ? 0 : PreDrawDeckRemovalCount,
            HiddenPostDrawMill = drawCount is null ? 0 : PostDrawDeckMillCount,
            FirstActivationOnly = drawCount is not null && FirstActivationOnly,
            BlocksLaterDraws = drawCount is not null && BlocksLaterDraws,
            Search = drawCount is null ? Search : null,
            ActivationLimit = hasEffect ? EffectPlan.ActivationLimit : null,
            MinimumHandSize = hasEffect ? MinimumHandSize : 0
        }, EffectProfile.AsCustom());
    }

    public Card WithOncePerTurn(bool oncePerTurn) => WithEffect(EffectPlan with { OncePerTurn = oncePerTurn, ActivationLimit = null }, EffectProfile.AsCustom());

    public Card WithDrawOncePerTurn(bool oncePerTurn) => WithOncePerTurn(oncePerTurn);

    public Card WithCategories(IEnumerable<CategoryBase> categories) {
        List<CategoryBase> effective = categories.ToList();
        IEnumerable<string> retainedKeys = ManualMetadataCategoryKeys
            .Where(key => effective.Any(category => category.Source == CategorySource.Metadata && category.MetadataKey == key));

        return new(effective, Copies, Name, Active, Id, ExternalCardId, retainedKeys, EffectPlan, EffectProfile, SimulationEnabled);
    }

    public Card WithActive(bool active) => new(Categories, Copies, Name, active, Id, ExternalCardId, ManualMetadataCategoryKeys, EffectPlan, EffectProfile, SimulationEnabled);

    public Card WithManualMetadataCategory(CategoryBase category) {
        if (category.Source != CategorySource.Metadata) {
            throw new ArgumentException("Manual card properties require a metadata category.", nameof(category));
        }

        // An existing objective membership must never become a removable override.
        if (Categories.Contains(category)) {
            return this;
        }

        return new(
            Categories.Concat([category]),
            Copies,
            Name,
            Active,
            Id,
            ExternalCardId,
            ManualMetadataCategoryKeys.Append(category.MetadataKey!),
            EffectPlan,
            EffectProfile,
            SimulationEnabled
        );
    }

    public Card WithoutManualMetadataCategory(string key) {
        if (!ManualMetadataCategoryKeys.Contains(key)) {
            return this;
        }

        return new(
            Categories.Where(category => category.Source != CategorySource.Metadata || category.MetadataKey != key),
            Copies,
            Name,
            Active,
            Id,
            ExternalCardId,
            ManualMetadataCategoryKeys.Where(metadataKey => metadataKey != key),
            EffectPlan,
            EffectProfile,
            SimulationEnabled
        );
    }

    public Card WithObjectiveMetadata(IEnumerable<CategoryBase> properties, int externalCardId) {
        List<CategoryBase> objective = properties.ToList();
        if (objective.Any(static category => category.Source != CategorySource.Metadata)) {
            throw new ArgumentException("Objective card properties require metadata categories.", nameof(properties));
        }

        HashSet<string> objectiveKeys = objective.Select(static category => category.MetadataKey!).ToHashSet(StringComparer.Ordinal);
        IEnumerable<CategoryBase> effective = Categories
            .Where(category => category.Source == CategorySource.User || ManualMetadataCategoryKeys.Contains(category.MetadataKey!))
            .Concat(objective)
            .DistinctBy(static category => category.Identity);
        IEnumerable<string> retainedKeys = ManualMetadataCategoryKeys.Where(key => !objectiveKeys.Contains(key));

        // Once materialized objectively, an overlapping override is no longer removable.
        return new(effective, Copies, Name, Active, Id, externalCardId, retainedKeys, EffectPlan, EffectProfile, SimulationEnabled);
    }

    public Card WithEffect(DrawEffectPlan plan, EffectProfile profile) => new(Categories, Copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys, plan, profile, SimulationEnabled);

    public Card WithSearch(SearchEffectPlan? search) => WithEffect(EffectPlan with {
        Search = search, OncePerTurn = search is not null && Search is null ? false : OncePerTurn,
        HandCost = search is null && DrawCount is null ? 0 : PreDrawRemovalCount,
        HandCostSelector = search is null && DrawCount is null ? CardSelector.Any : PreDrawSelector,
        RemovalCount = search is null && DrawCount is null ? 0 : PostDrawDiscardCount,
        RemovalSelector = search is null && DrawCount is null ? CardSelector.Any : PostDrawSelector,
        ActivationLimit = search is null && DrawCount is null ? null : EffectPlan.ActivationLimit,
        MinimumHandSize = search is null && DrawCount is null ? 0 : MinimumHandSize
    }, EffectProfile.AsCustom());

    private static IReadOnlySet<string> ValidateManualKeys(IEnumerable<CategoryBase> categories, IEnumerable<string>? keys) {
        FrozenSet<string> result = (keys ?? []).ToFrozenSet(StringComparer.Ordinal);
        HashSet<string?> metadataKeys = categories
            .Where(static category => category.Source == CategorySource.Metadata)
            .Select(static category => category.MetadataKey)
            .ToHashSet(StringComparer.Ordinal);

        bool hasInvalidManualKey = result.Any(key => string.IsNullOrWhiteSpace(key) || !metadataKeys.Contains(key));

        if (hasInvalidManualKey) {
            throw new ArgumentException("Manual card properties must refer to effective metadata categories.", nameof(keys));
        }

        return result;
    }
}
