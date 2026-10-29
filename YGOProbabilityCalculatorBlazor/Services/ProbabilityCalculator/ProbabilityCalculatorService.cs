using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Certification;

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
        if (options?.Mode is CalculationModel.OpeningHand or CalculationModel.Exact) {
            return CalculateProbabilityResults(deck, combos, handSize, null, workPolicy, storagePolicy, options).TotalProbability;
        }
        ArgumentNullException.ThrowIfNull(workPolicy);
        ArgumentNullException.ThrowIfNull(storagePolicy);
        ValidateMode(options);
        deck = deck.Select(card => card.ForCalculation()).ToList();
        ValidateComboCount(combos);
        ValidateCardIds(deck);
        foreach (Card card in deck) {
            CardEffectCompiler.RequireExecutable(card);
        }

        WorkBudget budget = new(workPolicy, storagePolicy);
        if (!deck.Any(card => card.HasEffect)) {
            return new ProbabilityCalculation(deck, handSize, budget).CalculateUnionExact(combos).ToDouble(budget);
        }

        // Omitted options retain the historical scalar optimistic projection.
        // Explicit Fast uses the same adaptive policy as all-row results, without
        // paying for per-row results or optional certification.
        return new DrawEffectCalculation(
            deck,
            handSize,
            budget,
            options?.DrawActivationPriority,
            adaptiveFast: options is { Mode: CalculationModel.Fast, CertifySearchAvailability: false }
        ).Calculate(combos, null, totalOnly: true).Total.ToDouble(budget);
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
        return CalculateWithBudget(deck, combos, handSize, groups, new WorkBudget(workPolicy, storagePolicy), options);
    }

    // Worker entry points select bounded Fast or unlimited physical Exact.
    // Native callers retain their explicitly selected synchronous work policy.
    internal ProbabilityCalculationResult CalculateWithBudget(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        IReadOnlyList<ComboGroup>? groups,
        WorkBudget budget,
        CalculationOptions? options,
        bool physicalFast = false
    ) {
        ValidateMode(options);
        CalculationModel mode = options?.Mode ?? CalculationModel.Fast;
        ValidateComboCount(combos);
        ValidateCardIds(deck);
        deck = deck.Select(card => card.ForCalculation(mode != CalculationModel.OpeningHand)).ToList();
        foreach (Card card in deck) {
            CardEffectCompiler.RequireExecutable(card);
        }

        bool hasEffects = deck.Any(card => card.HasEffect);
        DrawEffectCalculation? execution = hasEffects ? new(
            deck,
            handSize,
            budget,
            options?.DrawActivationPriority,
            physicalSearch: mode == CalculationModel.Exact || physicalFast,
            adaptiveFast: mode == CalculationModel.Fast && options?.CertifySearchAvailability != true
        ) : null;
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
            // Defensive boundary: an unproven completed execution can never
            // publish optimistic values under the Exact contract.
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
