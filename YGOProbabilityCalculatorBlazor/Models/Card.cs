using System.Collections.Frozen;

namespace YGOProbabilityCalculatorBlazor.Models;

public class Card {
    public string Id { get; }
    public int Copies { get; }
    public List<CategoryBase> Categories { get; }
    public string? Name { get; }
    public bool Active { get; }
    public int? ExternalCardId { get; }
    public DrawEffectPlan EffectPlan { get; }
    public EffectProfile EffectProfile { get; }
    public bool FirstActivationOnly => EffectPlan.FirstActivationOnly;
    public bool BlocksLaterDraws => EffectPlan.BlocksLaterDraws;
    public int? DrawCount => EffectPlan.DrawCount;
    public bool DrawOncePerTurn => EffectPlan.OncePerTurn;
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
        bool blocksLaterDraws = false
    ) {
        if (drawCount is not null and not (>= 1 and <= 3)) {
            throw new ArgumentOutOfRangeException(nameof(drawCount), "Draw count must be 1, 2, 3, or absent.");
        }

        if (postDrawDiscardCount is < 0 or > 2 || (postDrawDiscardCount > 0 && drawCount is null)) {
            throw new ArgumentOutOfRangeException(nameof(postDrawDiscardCount), "Post-draw discard requires a draw effect and must be 0, 1, or 2.");
        }

        if (!Enum.IsDefined(postDrawFailureBehavior)) {
            throw new ArgumentOutOfRangeException(nameof(postDrawFailureBehavior));
        }
        if (postDrawSelector is { Kind: not CardSelectorKind.Any } && postDrawDiscardCount == 0) {
            throw new ArgumentException("Filtered removal requires a positive post-draw count.", nameof(postDrawSelector));
        }

        if (preDrawRemovalCount is < 0 or > 2 || (preDrawRemovalCount > 0 && drawCount is null)) {
            throw new ArgumentOutOfRangeException(nameof(preDrawRemovalCount), "Pre-draw removal requires a draw effect and must be 0, 1, or 2.");
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

        EffectPlan = new(drawCount, drawOncePerTurn, preDrawRemovalCount, preDrawSelector ?? CardSelector.Any,
            preDrawDeckRemovalCount, postDrawDiscardCount, postDrawSelector ?? CardSelector.Any, postDrawFailureBehavior, postDrawDeckMillCount, firstActivationOnly, blocksLaterDraws);
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
        ExternalCardId = externalCardId;
        ManualMetadataCategoryKeys = ValidateManualKeys(Categories, manualMetadataCategoryKeys);
    }

    public Card Duplicate() => new(
        Categories,
        Copies,
        Name,
        Active,
        externalCardId: ExternalCardId,
        manualMetadataCategoryKeys: ManualMetadataCategoryKeys,
        drawCount: DrawCount,
        drawOncePerTurn: DrawOncePerTurn,
        postDrawDiscardCount: PostDrawDiscardCount,
        postDrawSelector: PostDrawSelector,
        postDrawFailureBehavior: PostDrawFailureBehavior,
        preDrawRemovalCount: PreDrawRemovalCount,
        preDrawSelector: PreDrawSelector,
        preDrawDeckRemovalCount: PreDrawDeckRemovalCount,
        effectProfile: EffectProfile,
        postDrawDeckMillCount: PostDrawDeckMillCount,
        firstActivationOnly: FirstActivationOnly,
        blocksLaterDraws: BlocksLaterDraws
    );

    public Card WithPostDrawDeckMillCount(int count) => WithEffect(EffectPlan with { HiddenPostDrawMill = count }, EffectProfile.AsCustom());

    public Card WithPreDrawDeckRemovalCount(int count) => new(Categories, Copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys, DrawCount, DrawOncePerTurn, PostDrawDiscardCount, PostDrawSelector, PostDrawFailureBehavior, PreDrawRemovalCount, PreDrawSelector, count, EffectProfile.AsCustom(), PostDrawDeckMillCount, FirstActivationOnly, BlocksLaterDraws);

    public Card WithPreDrawRemovalCount(int count) => new(Categories, Copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys, DrawCount, DrawOncePerTurn, PostDrawDiscardCount, PostDrawSelector, PostDrawFailureBehavior, count, count == 0 ? CardSelector.Any : PreDrawSelector, PreDrawDeckRemovalCount, EffectProfile.AsCustom(), PostDrawDeckMillCount, FirstActivationOnly, BlocksLaterDraws);

    public Card WithPreDrawSelector(CardSelector selector) => new(Categories, Copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys, DrawCount, DrawOncePerTurn, PostDrawDiscardCount, PostDrawSelector, PostDrawFailureBehavior, PreDrawRemovalCount, selector, PreDrawDeckRemovalCount, EffectProfile.AsCustom(), PostDrawDeckMillCount, FirstActivationOnly, BlocksLaterDraws);

    public Card WithPostDrawDiscardCount(int count) => new(Categories, Copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys, DrawCount, DrawOncePerTurn, count, count == 0 ? CardSelector.Any : PostDrawSelector, PostDrawFailureBehavior, PreDrawRemovalCount, PreDrawSelector, PreDrawDeckRemovalCount, EffectProfile.AsCustom(), PostDrawDeckMillCount, FirstActivationOnly, BlocksLaterDraws);

    public Card WithPostDrawSelector(CardSelector selector) => new(
        Categories,
        Copies,
        Name,
        Active,
        Id,
        ExternalCardId,
        ManualMetadataCategoryKeys,
        DrawCount,
        DrawOncePerTurn,
        PostDrawDiscardCount,
        selector,
        PostDrawFailureBehavior,
        PreDrawRemovalCount,
        PreDrawSelector,
        PreDrawDeckRemovalCount,
        EffectProfile.AsCustom(),
        PostDrawDeckMillCount,
        FirstActivationOnly,
        BlocksLaterDraws
    );

    public Card WithPostDrawFailureBehavior(PostDrawFailureBehavior behavior) => new(
        Categories,
        Copies,
        Name,
        Active,
        Id,
        ExternalCardId,
        ManualMetadataCategoryKeys,
        DrawCount,
        DrawOncePerTurn,
        PostDrawDiscardCount,
        PostDrawSelector,
        behavior,
        PreDrawRemovalCount,
        PreDrawSelector,
        PreDrawDeckRemovalCount,
        EffectProfile.AsCustom(),
        PostDrawDeckMillCount,
        FirstActivationOnly,
        BlocksLaterDraws
    );

    public Card WithName(string? name) => new(Categories, Copies, name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys, DrawCount, DrawOncePerTurn, PostDrawDiscardCount, PostDrawSelector, PostDrawFailureBehavior, PreDrawRemovalCount, PreDrawSelector, PreDrawDeckRemovalCount, EffectProfile, PostDrawDeckMillCount, FirstActivationOnly, BlocksLaterDraws);

    public Card WithCopies(int copies) => new(Categories, copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys, DrawCount, DrawOncePerTurn, PostDrawDiscardCount, PostDrawSelector, PostDrawFailureBehavior, PreDrawRemovalCount, PreDrawSelector, PreDrawDeckRemovalCount, EffectProfile, PostDrawDeckMillCount, FirstActivationOnly, BlocksLaterDraws);

    public Card WithDrawCount(int? drawCount) => new(
        Categories,
        Copies,
        Name,
        Active,
        Id,
        ExternalCardId,
        ManualMetadataCategoryKeys,
        drawCount,
        DrawOncePerTurn,
        drawCount is null ? 0 : PostDrawDiscardCount,
        drawCount is null ? CardSelector.Any : PostDrawSelector,
        PostDrawFailureBehavior,
        drawCount is null ? 0 : PreDrawRemovalCount,
        drawCount is null ? CardSelector.Any : PreDrawSelector,
        drawCount is null ? 0 : PreDrawDeckRemovalCount,
        EffectProfile.AsCustom(),
        drawCount is null ? 0 : PostDrawDeckMillCount,
        drawCount is not null && FirstActivationOnly,
        drawCount is not null && BlocksLaterDraws
    );

    public Card WithDrawOncePerTurn(bool oncePerTurn) => new(Categories, Copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys, DrawCount, oncePerTurn, PostDrawDiscardCount, PostDrawSelector, PostDrawFailureBehavior, PreDrawRemovalCount, PreDrawSelector, PreDrawDeckRemovalCount, EffectProfile.AsCustom(), PostDrawDeckMillCount, FirstActivationOnly, BlocksLaterDraws);

    public Card WithCategories(IEnumerable<CategoryBase> categories) {
        List<CategoryBase> effective = categories.ToList();

        return new(
            effective,
            Copies,
            Name,
            Active,
            Id,
            ExternalCardId,
            ManualMetadataCategoryKeys.Where(key => effective.Any(category => category.Source == CategorySource.Metadata && category.MetadataKey == key)),
            DrawCount,
            DrawOncePerTurn,
            PostDrawDiscardCount,
            PostDrawSelector,
            PostDrawFailureBehavior,
            PreDrawRemovalCount,
            PreDrawSelector,
            PreDrawDeckRemovalCount,
            EffectProfile,
            PostDrawDeckMillCount,
            FirstActivationOnly,
            BlocksLaterDraws
        );
    }

    public Card WithActive(bool active) => new(Categories, Copies, Name, active, Id, ExternalCardId, ManualMetadataCategoryKeys, DrawCount, DrawOncePerTurn, PostDrawDiscardCount, PostDrawSelector, PostDrawFailureBehavior, PreDrawRemovalCount, PreDrawSelector, PreDrawDeckRemovalCount, EffectProfile, PostDrawDeckMillCount, FirstActivationOnly, BlocksLaterDraws);

    public Card WithManualMetadataCategory(CategoryBase category) {
        if (category.Source != CategorySource.Metadata) {
            throw new ArgumentException("Manual card properties require a metadata category.", nameof(category));
        }

        // An existing objective membership must never become a removable override.
        if (Categories.Contains(category)) {
            return this;
        }

        return new(Categories.Concat([category]), Copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys.Append(category.MetadataKey!), DrawCount, DrawOncePerTurn, PostDrawDiscardCount, PostDrawSelector, PostDrawFailureBehavior, PreDrawRemovalCount, PreDrawSelector, PreDrawDeckRemovalCount, EffectProfile, PostDrawDeckMillCount, FirstActivationOnly, BlocksLaterDraws);
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
            DrawCount,
            DrawOncePerTurn,
            PostDrawDiscardCount,
            PostDrawSelector,
            PostDrawFailureBehavior,
            PreDrawRemovalCount,
            PreDrawSelector,
            PreDrawDeckRemovalCount,
            EffectProfile,
            PostDrawDeckMillCount,
            FirstActivationOnly,
            BlocksLaterDraws
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

        // Once materialized objectively, an overlapping override is no longer removable.
        return new(effective, Copies, Name, Active, Id, externalCardId, ManualMetadataCategoryKeys.Where(key => !objectiveKeys.Contains(key)), DrawCount, DrawOncePerTurn, PostDrawDiscardCount, PostDrawSelector, PostDrawFailureBehavior, PreDrawRemovalCount, PreDrawSelector, PreDrawDeckRemovalCount, EffectProfile, PostDrawDeckMillCount, FirstActivationOnly, BlocksLaterDraws);
    }

    public Card WithEffect(DrawEffectPlan plan, EffectProfile profile) => new(
        Categories, Copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys,
        plan.DrawCount, plan.OncePerTurn, plan.RemovalCount, plan.RemovalSelector,
        plan.FailureBehavior, plan.HandCost, plan.HandCostSelector, plan.HiddenDeckCost, profile, plan.HiddenPostDrawMill, plan.FirstActivationOnly, plan.BlocksLaterDraws);

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
