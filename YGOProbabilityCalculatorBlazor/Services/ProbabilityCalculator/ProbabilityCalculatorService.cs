using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

public class ProbabilityCalculatorService : IProbabilityCalculatorService {
    public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) =>
        CalculateProbabilityForCombos(deck, combos, handSize, CalculationWorkPolicy.Default, CalculationStoragePolicy.Standard);

    public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize, CalculationWorkPolicy workPolicy, CalculationOptions? options = null) =>
        CalculateProbabilityForCombos(deck, combos, handSize, workPolicy, CalculationStoragePolicy.Standard, options);

    public double CalculateProbabilityForCombos(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        CalculationWorkPolicy workPolicy,
        CalculationStoragePolicy storagePolicy,
        CalculationOptions? options = null
    ) {
        ArgumentNullException.ThrowIfNull(workPolicy);
        ArgumentNullException.ThrowIfNull(storagePolicy);
        ValidateMode(options);
        if (options?.Mode is CalculationModel.OpeningHand or CalculationModel.Exact) {
            return CalculateProbabilityResults(deck, combos, handSize, null, workPolicy, storagePolicy, options).TotalProbability;
        }
        ValidateComboCount(combos);
        ValidateCardIds(deck);
        foreach (Card card in deck) {
            CardEffectCompiler.RequireExecutable(card);
        }

        if (!deck.Any(card => card.HasEffect)) {
            return new ProbabilityCalculation(deck, handSize, workPolicy, storagePolicy).CalculateUnion(combos);
        }

        WorkBudget budget = new(workPolicy, storagePolicy);
        return new DrawEffectCalculation(deck, handSize, budget, options?.DrawActivationPriority).Calculate(combos, null, totalOnly: true).Total.ToDouble(budget);
    }

    public ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        IReadOnlyList<ComboGroup>? groups = null
    ) => CalculateProbabilityResults(deck, combos, handSize, groups, CalculationWorkPolicy.Default, CalculationStoragePolicy.Standard);

    public ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        IReadOnlyList<ComboGroup>? groups,
        CalculationWorkPolicy workPolicy,
        CalculationOptions? options = null
    ) => CalculateProbabilityResults(deck, combos, handSize, groups, workPolicy, CalculationStoragePolicy.Standard, options);

    public ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        IReadOnlyList<ComboGroup>? groups,
        CalculationWorkPolicy workPolicy,
        CalculationStoragePolicy storagePolicy,
        CalculationOptions? options = null
    ) {
        ArgumentNullException.ThrowIfNull(workPolicy);
        ArgumentNullException.ThrowIfNull(storagePolicy);
        ValidateMode(options);
        CalculationModel mode = options?.Mode ?? CalculationModel.Fast;
        ValidateComboCount(combos);
        ValidateCardIds(deck);
        if (mode == CalculationModel.OpeningHand) {
            deck = deck.Select(card => new Card(
                card.Categories,
                card.Copies,
                card.Name,
                card.Active,
                card.Id,
                card.ExternalCardId,
                card.ManualMetadataCategoryKeys
            )).ToList();
        }
        foreach (Card card in deck) {
            CardEffectCompiler.RequireExecutable(card);
        }

        bool hasEffects = deck.Any(card => card.HasEffect);
        WorkBudget budget = new(workPolicy, storagePolicy);
        DrawEffectCalculation? execution = hasEffects ? new(deck, handSize, budget, options?.DrawActivationPriority) : null;
        ExactCalculationResult exact = hasEffects
            ? execution!.Calculate(combos, groups)
            : new ProbabilityCalculation(deck, handSize, budget).CalculateExactResults(combos, groups);
        return CompleteResult(combos, groups, mode, exact, budget, execution, options?.CertifySearchAvailability == true);
    }

    internal static ProbabilityCalculationResult CompleteResult(
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        CalculationModel mode,
        ExactCalculationResult exact,
        WorkBudget budget,
        DrawEffectCalculation? execution,
        bool legacyCertificate = false
    ) {
        // Complete and retain every primary row, including conversion, before
        // optional work spends any of the remaining request allowance.
        ProbabilityCalculationResult result = exact.ToPublic(combos, groups, budget);
        CalculationCapability? capability = null;
        try {
            capability = execution?.DescribeCapability() ?? ProbabilityCalculation.Capability;
        }
        catch (ProbabilityCalculationLimitException exception) when (mode == CalculationModel.Fast) {
            result = result with { SearchAvailability = SearchAvailabilityCertificate.Unavailable(SearchAvailabilityStatus.Limited,
                $"Not certified: capability proof reached the {exception.Reason} limit. Modeled probabilities are complete.") };
        }
        catch (Exception exception) when (mode == CalculationModel.Fast && exception is not OperationCanceledException) {
            result = result with { SearchAvailability = SearchAvailabilityCertificate.Unavailable(SearchAvailabilityStatus.Error,
                $"Not certified: capability proof failed ({exception.GetType().Name}). Modeled probabilities are complete.") };
        }
        bool proven = capability?.Accuracy == PhysicalSearchAccuracy.ProvenExact;
        if (mode == CalculationModel.Exact && !proven) {
            // Milestone 2 has no physical-survivor solver. The completed internal
            // optimistic count supplies proof ownership, but is never published.
            CalculationResultContract unsupported = CalculationResultContract.Failure(mode, CalculationCompletion.Unsupported, capability!.Value.Explanation)
                with { SolverRoute = capability.Value.Route.ToString() };
            throw new UnsupportedExactCalculationException(unsupported);
        }
        if (mode == CalculationModel.Fast && capability is not null && (!proven || legacyCertificate)) {
            result = result with { SearchAvailability = SearchAvailabilityCertification.TryCalculate(combos, groups, exact, budget, execution) };
        }

        CalculationAccuracy accuracy = proven ? CalculationAccuracy.Exact : result.SearchAvailability?.Status switch {
            SearchAvailabilityStatus.Exact => CalculationAccuracy.Exact,
            SearchAvailabilityStatus.Bounded => CalculationAccuracy.Bounded,
            _ => CalculationAccuracy.Uncertified
        };
        CalculationFractions fractions = new(
            exact.Total.ToFraction(),
            [.. exact.Combos.Select(value => value.ToFraction())],
            [.. exact.Groups.Select(value => value.ToFraction())]
        );
        CalculationResultContract contract = new(
            CalculationResultContract.CurrentVersion,
            mode,
            mode,
            mode == CalculationModel.OpeningHand ? CalculationResultContract.OpeningReference : CalculationResultContract.EffectsReference,
            mode == CalculationModel.OpeningHand ? CalculationRuleScope.OpeningHand : CalculationRuleScope.SavedEffectsFixedPolicy,
            CalculationCompletion.Success,
            accuracy,
            proven ? capability!.Value.Explanation : result.SearchAvailability!.Detail,
            capability?.Route.ToString(),
            proven ? capability!.Value.Reason.ToString() : null,
            fractions
        );
        return result with { Contract = contract };
    }

    private static void ValidateMode(CalculationOptions? options) {
        if (options is not null && !Enum.IsDefined(options.Mode)) {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown calculation mode.");
        }
        if (options?.CertifySearchAvailability == true && options.Mode != CalculationModel.Fast) {
            throw new ArgumentException("The legacy certification flag is only compatible with Fast mode.", nameof(options));
        }
    }

    private static void ValidateComboCount(List<Combo> combos) {
        if (combos.Count > IProbabilityCalculatorService.MaxComboCount) {
            throw new ArgumentOutOfRangeException(nameof(combos), $"Calculation supports at most {IProbabilityCalculatorService.MaxComboCount} combos.");
        }
    }

    private static void ValidateCardIds(List<Card> deck) {
        bool hasDuplicateCardIds = deck.Select(static card => card.Id).Distinct(StringComparer.Ordinal).Count() != deck.Count;

        if (hasDuplicateCardIds) {
            throw new ArgumentException("Deck card IDs must be unique.", nameof(deck));
        }
    }
}
