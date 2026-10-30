using System.Collections.Frozen;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public interface ICardEffectDefinition {
    int CanonicalCardId { get; }
    string Name { get; }
    int Revision { get; }
    EffectSupport Support { get; }
    DrawEffectPlan Compile();
    string Summary { get; }
    // Reserve notes for omissions that change opening-hand draw calculations.
    string Limitations => "";
    IReadOnlyList<int> Aliases => [];
    IReadOnlyList<CategoryBase> FilterProperties => [];
    EffectExecutionModel ExecutionModel => EffectExecutionModel.DrawSequenceV1;
}

public sealed record MaintainedCardEffect(int CanonicalCardId, string Name, DrawEffectPlan Plan, EffectProfile Profile);

public sealed record EffectiveCardEffect(
    MaintainedCardEffect? Maintained,
    DrawEffectPlan Plan,
    EffectSource Source,
    EffectSupport Support,
    string Summary,
    string Limitations,
    EffectExecutionModel ExecutionModel,
    EffectCapabilities Capabilities,
    bool CanExecute,
    bool CanRestore
) {
    public bool UsesCurrentDescription { get; init; }
}

// Definitions compile once at registry construction. Only immutable resolved values
// enter cards, sessions and workers. Calculation executes those saved plans;
// maintained IDs/aliases separately provide verified identity for Danger comparisons.
public sealed class CardEffectRegistry {
    private readonly FrozenDictionary<int, MaintainedCardEffect> _byIdentity;

    public static CardEffectRegistry Default { get; } = new([
        new SolarRechargeEffect(), new TradeInEffect(), new OrcustratedReturnEffect(),
        new PotOfGreedEffect(),
        new RadiantTyphoonVisionEffect(), new PotOfDesiresEffect(),
        new UpstartGoblinEffect(),
        new AllureOfDarknessEffect(),
        new PotOfExtravaganceEffect(),
        new IntoTheVoidEffect(),
        new ChickenGameEffect(),
        new DarkWorldDealingsEffect(),
        new ContractWithDonThousandEffect(),
        new OneDayOfPeaceEffect(),
        new GracefulCharityEffect(),
        new SekkasLightEffect(),
        new LinkAppleEffect(), new ReinforcementOfTheArmyEffect(),
        new ACaseForK9Effect(), new ChaoticElementsEffect(),
        new KetuDracotailEffect(), new MitsurugiPrayersEffect(), new SpentaTheMagistusSealerEffect(),
        new PrimiteLordlyLodeEffect(), new WantedSeekerOfSinfulSpoilsEffect(), new SpellPowerMasteryEffect(),
        new FiendsmithEngraverEffect(), new EarthboundPrisonerStoneSweeperEffect(), new PreparationOfRitesEffect(),
        new FireFormationTenkiEffect(), new BonfireEffect(), new SkyStrikerMobilizeEngageEffect(),
        new DarklordIxchelEffect(), new TraptantalizingTuneEffect(),
        new BrandedOpeningEffect(), new FiendsmithTractEffect(), new KewlTuneSynchroEffect(), new RadiantTyphoonChantEffect(),
        new EmpireOfEndymionEffect(), new FairyTailBallEffect(), new FutureSilenceEffect(),
        new NightTrainBlueTravelerEffect(), new ExceptionalScheduleEffect(), new LunalightBlackSheepEffect(),
        .. DangerBaseEffect.Definitions
    ]);

    public IReadOnlyList<MaintainedCardEffect> All { get; }
    public IReadOnlyList<CategoryBase> FilterProperties { get; }

    public CardEffectRegistry(IEnumerable<ICardEffectDefinition> definitions) {
        Dictionary<int, MaintainedCardEffect> identities = [];
        List<MaintainedCardEffect> profiles = [];
        List<CategoryBase> filterProperties = [];

        foreach (ICardEffectDefinition definition in definitions) {
            EffectProfile profile = new(EffectSource.BuiltIn, definition.CanonicalCardId, definition.Revision, definition.Summary, definition.Support, definition.Limitations, definition.ExecutionModel);
            // The same validation applies to definitions, custom input and decoded snapshots.
            Card validated = new Card([]).WithEffect(definition.Compile(), profile);
            MaintainedCardEffect effect = new(definition.CanonicalCardId, definition.Name, validated.EffectPlan, profile);
            profiles.Add(effect);
            filterProperties.AddRange(definition.FilterProperties);

            foreach (int identity in definition.Aliases.Prepend(definition.CanonicalCardId)) {
                if (identity <= 0 || !identities.TryAdd(identity, effect)) {
                    throw new ArgumentException("Effect identities and aliases must be unique positive IDs.", nameof(definitions));
                }
            }
        }

        All = profiles.AsReadOnly();
        FilterProperties = Array.AsReadOnly(filterProperties.Distinct().ToArray());
        _byIdentity = identities.ToFrozenDictionary();
    }

    public MaintainedCardEffect? Find(int identity) => _byIdentity.GetValueOrDefault(identity);

    public MaintainedCardEffect? Find(CardInfo info) => Find(CanonicalIdentity(info));

    private static int CanonicalIdentity(CardInfo info) => info.CanonicalCardId is > 0 ? info.CanonicalCardId.Value : info.Id;

    public Card CreateImportedCard(CardInfo info, int copies, int importedId) =>
        ApplyCurrent(new(CardPropertyProvider.GetCategories(info), copies, info.Name, externalCardId: importedId), info);

    public Card ApplyForExplicitSelection(Card replacement, Card previous, CardInfo selected) {
        int canonical = Find(selected)?.CanonicalCardId ?? CanonicalIdentity(selected);
        int? previousCanonical = previous.EffectProfile.CanonicalCardId ?? (previous.ExternalCardId is int id ? Find(id)?.CanonicalCardId ?? id : null);
        bool same = previousCanonical == canonical || previous.ExternalCardId == selected.Id || (previous.ExternalCardId is int external && selected.ArtworkImageIds.Contains(external));

        if (same) {
            return replacement;
        }

        Card reset = replacement.WithEffect(new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any), new(EffectSource.Custom, canonical));

        return ApplyCurrent(reset, selected);
    }

    internal Card ApplyCurrent(Card card, CardInfo info) {
        if (Find(info) is { } effect) {
            return card.WithEffect(effect.Plan, effect.Profile);
        }

        int canonical = CanonicalIdentity(info);

        return card.WithEffect(card.EffectPlan, new(EffectSource.Custom, canonical > 0 ? canonical : null));
    }

    public Card RestoreBuiltIn(Card card) {
        MaintainedCardEffect effect = MaintainedFor(card) ?? throw new InvalidOperationException("No maintained effect is available for this card.");

        return card.WithEffect(effect.Plan, effect.Profile);
    }

    public EffectiveCardEffect Describe(Card card) {
        MaintainedCardEffect? maintained = MaintainedFor(card);
        EffectProfile profile = card.EffectProfile;
        bool builtin = profile.Source == EffectSource.BuiltIn;
        bool executable = profile.ExecutionModel == EffectExecutionModel.DrawSequenceV1;
        // Text-only corrections do not change semantic revisions or saved plans.
        // Refresh presentation only for an otherwise identical current definition.
        bool currentDescription = builtin && maintained is not null
            && profile.CanonicalCardId == maintained.Profile.CanonicalCardId
            && profile.Revision == maintained.Profile.Revision
            && profile.ExecutionModel == maintained.Profile.ExecutionModel
            && profile.Support == maintained.Profile.Support
            && card.EffectPlan == maintained.Plan;

        EffectProfile description = currentDescription ? maintained!.Profile : profile;

        return new(maintained, card.EffectPlan, profile.Source,
            executable && builtin ? profile.Support : EffectSupport.Unmodeled,
            builtin ? description.Summary! : card.EffectPlan.Describe(),
            builtin ? description.Limitations ?? "" : "",
            profile.ExecutionModel, card.EffectPlan.Capabilities,
            executable, maintained?.Profile.ExecutionModel == EffectExecutionModel.DrawSequenceV1) {
            UsesCurrentDescription = currentDescription
        };
    }

    private MaintainedCardEffect? MaintainedFor(Card card) => (card.EffectProfile.CanonicalCardId ?? card.ExternalCardId) is int identity ? Find(identity) : null;
}

// Only this bounded execution model is supported by the current exact interpreters.
// A future model must add an explicit compiler and solver capability/routing proof.
// An unavailable definition can still execute its saved V1 snapshot; an unavailable
// execution model cannot fall through to Draw N.
public static class CardEffectCompiler {
    public static void RequireExecutable(Card card) {
        if (!Enum.IsDefined(card.EffectPlan.Operation)) {
            throw new ArgumentException($"Unsupported saved draw operation: {card.EffectPlan.Operation}.");
        }
        if (card.IsDangerReveal) {
            DangerCardIdentity.RequireSource(card);
        }
        if (card.EffectProfile.ExecutionModel != EffectExecutionModel.DrawSequenceV1) {
            throw new ArgumentException($"Unsupported saved effect execution model: {card.EffectProfile.ExecutionModel}.");
        }
    }
}
