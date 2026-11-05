using RuntimeOptions = CardProbability.Internal.Contracts.CalculationOptions;
using CardProbability.Internal.Model;
using CardProbability.Internal.Contracts;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Certification;

namespace CardProbability.Internal;

internal sealed class CalculationRuntime {
    // Worker entry points select bounded Fast or unlimited physical Exact.
    // Native callers retain their explicitly selected synchronous work policy.
    internal ProbabilityCalculationResult CalculateWithBudget(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        IReadOnlyList<ComboGroup>? groups,
        WorkBudget budget,
        RuntimeOptions? options,
        bool physicalFast = false
    ) {
        ValidateMode(options);
        CalculationModel mode = options?.Mode ?? CalculationModel.Fast;

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

    internal ProbabilityCalculationResult CalculateUnionWithBudget(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        WorkBudget budget,
        RuntimeOptions? options,
        bool physicalFast = false
    ) {
        ValidateMode(options);
        CalculationModel mode = options?.Mode ?? CalculationModel.Fast;
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
            ? execution!.Calculate(combos, null, totalOnly: true)
            : new(new ProbabilityCalculation(deck, handSize, budget).CalculateUnionExact(combos), [], []);
        double total = exact.Total.ToDouble(budget);
        CalculationCapability? capability = null;
        string? unavailableReason = null;
        try {
            capability = execution?.DescribeCapability() ?? ProbabilityCalculation.Capability;
        }
        catch (ProbabilityCalculationLimitException exception) when (mode == CalculationModel.Fast) {
            unavailableReason = $"Not certified: capability proof reached the {exception.Reason} limit. Modeled probability is complete.";
        }
        catch (Exception exception) when (mode == CalculationModel.Fast && exception is not OperationCanceledException) {
            unavailableReason = $"Not certified: capability proof failed ({exception.GetType().Name}). Modeled probability is complete.";
        }

        bool proven = capability?.Accuracy == PhysicalSearchAccuracy.ProvenExact;
        if (mode == CalculationModel.Exact && !proven) {
            CalculationResultContract unsupported = CalculationResultContract.Failure(mode, CalculationCompletion.Unsupported, capability!.Value.Explanation)
                with { SolverRoute = capability.Value.Route.ToString() };
            throw new UnsupportedExactCalculationException(unsupported);
        }

        CalculationResultContract contract = new(
            CalculationResultContract.CurrentVersion,
            mode,
            mode,
            mode == CalculationModel.OpeningHand ? CalculationResultContract.OpeningReference : CalculationResultContract.EffectsReference,
            mode == CalculationModel.OpeningHand ? CalculationRuleScope.OpeningHand : CalculationRuleScope.SavedEffectsFixedPolicy,
            CalculationCompletion.Success,
            proven ? CalculationAccuracy.Exact : CalculationAccuracy.Uncertified,
            capability?.Explanation ?? unavailableReason!,
            capability?.Route.ToString(),
            proven ? capability!.Value.Reason.ToString() : null,
            new(exact.Total.ToFraction(), [], [])
        );

        return new(total, [], [], Contract: contract);
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

    private static void ValidateMode(RuntimeOptions? options) {
        if (options is not null && !Enum.IsDefined(options.Mode)) {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown calculation mode.");
        }

        if (options?.CertifySearchAvailability == true && options.Mode != CalculationModel.Fast) {
            throw new ArgumentException("The legacy certification flag is only compatible with Fast mode.", nameof(options));
        }
    }

}
