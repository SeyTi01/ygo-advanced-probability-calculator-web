using System.Collections.Immutable;
using CardProbability.Internal;
using CardProbability.Internal.Resources;
using C = CardProbability.Internal.Contracts;
using M = CardProbability.Internal.Model;

namespace CardProbability;

/// <summary>Stateless synchronous entry point. Immutable requests may be reused concurrently.</summary>
public static class CalculationEngine {
    /// <summary>Calculates goal rows, reporting group unions and the total union under one shared keep/removal policy.</summary>
    /// <param name="request">Immutable problem; references and effect plans are validated even in OpeningHand mode.</param>
    /// <param name="options">Execution settings, or null for Exact with unlimited work and default storage limits.</param>
    /// <param name="cancellationToken">Cooperative cancellation; cancellation returns a terminal outcome without rows.</param>
    /// <returns>A terminal result. Only Completed contains probabilities; inspect Accuracy and Certified before interpreting a primary fraction physically.</returns>
    public static CalculationResult Calculate(CalculationRequest request, CalculationOptions? options = null, CancellationToken cancellationToken = default) =>
        CalculateCore(request, options, cancellationToken, false);

    /// <summary>Evaluates the same goal union and keep policy while omitting standalone goal/group rows and optional certificates.</summary>
    /// <param name="request">The full goal set; removing goals can change the keep/removal policy.</param>
    /// <param name="options">Execution settings, or null for the same defaults as Calculate.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A terminal result with empty Goals/Groups and a Total only on completion.</returns>
    /// <remarks>Structural exact proof is retained, but Fast can have weaker Accuracy/Certified information than Calculate
    /// because optional certification is skipped. Equality of primary fractions does not imply equal physical proof.</remarks>
    public static CalculationResult CalculateUnion(CalculationRequest request, CalculationOptions? options = null, CancellationToken cancellationToken = default) =>
        CalculateCore(request, options, cancellationToken, true);

    private static CalculationResult CalculateCore(CalculationRequest request, CalculationOptions? options, CancellationToken cancellationToken, bool unionOnly) {
        options ??= new();
        WorkBudget? budget = null;
        ModelScope scope = options.Mode == CalculationMode.OpeningHand ? ModelScope.OpeningHand : ModelScope.FixedOrderEffects;
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Enum.IsDefined(options.Mode) || !Enum.IsDefined(options.ActivationOrder)) {
                throw new ArgumentException("Unknown calculation mode or activation order.");
            }
            ExecutionPolicy policy = options.Policy;
            C.CalculationWorkPolicy work = policy.WorkUnits is { } units ? new(units) : C.CalculationWorkPolicy.Unlimited;
            budget = new(work, new C.CalculationStoragePolicy(policy.EntryLimit, policy.CellLimit), cancellationToken);
            PreparedCalculation prepared = PreparedCalculation.Create(request, options, budget);
            M.CalculationModel mode = options.Mode switch {
                CalculationMode.OpeningHand => M.CalculationModel.OpeningHand,
                CalculationMode.Exact => M.CalculationModel.Exact,
                _ => M.CalculationModel.Fast
            };
            M.DrawActivationPriority priority = new(
                options.ActivationOrder == ActivationOrder.Priority ? M.DrawActivationMode.CustomPriority : M.DrawActivationMode.EncounterOrder,
                options.PriorityEntryIds
            );
            C.CalculationOptions execution = new(priority, options.Mode == CalculationMode.OptimisticSearchProjection, mode);
            CalculationRuntime runtime = new();
            C.ProbabilityCalculationResult result = unionOnly
                ? runtime.CalculateUnionWithBudget(prepared.Cards, prepared.Combos, request.HandSize, budget, execution)
                : runtime.CalculateWithBudget(prepared.Cards, prepared.Combos, request.HandSize, prepared.Groups, budget, execution);
            cancellationToken.ThrowIfCancellationRequested();
            C.CalculationResultContract contract = result.Contract!;
            C.CalculationFractions fractions = contract.Fractions!;
            C.SearchAvailabilityCertificate? certificate = result.SearchAvailability;
            bool primaryProven = contract.Proof is not null;
            ProbabilityAccuracy accuracy = (ProbabilityAccuracy)contract.Accuracy!.Value;
            ProbabilityValue total = Value(result.TotalProbability, fractions.Total, certificate?.Total);
            ImmutableArray<GoalProbability> goals = result.ComboProbabilities.Select((row, index) => new GoalProbability(
                request.Goals[index].Id,
                Value(row.Probability, fractions.Combos[index], certificate is { Combos.IsDefaultOrEmpty: false } ? certificate.Combos[index] : null)
            )).ToImmutableArray();
            ImmutableArray<GroupProbability> groups = (result.GroupProbabilities ?? []).Select((row, index) => new GroupProbability(
                row.GroupId,
                row.ActiveComboCount,
                Value(row.Probability, fractions.Groups[index], certificate is { Groups.IsDefaultOrEmpty: false } ? certificate.Groups[index] : null)
            )).ToImmutableArray();
            return new(CalculationStatus.Completed, options.Mode, scope, contract.Reason, accuracy, total, goals, groups, Metrics()) {
                CertificationDetail = certificate?.Detail,
                Certification = certificate?.Status switch {
                    C.SearchAvailabilityStatus.Exact => CertificationStatus.Exact,
                    C.SearchAvailabilityStatus.Bounded => CertificationStatus.Bounded,
                    C.SearchAvailabilityStatus.NotApplicable => CertificationStatus.NotApplicable,
                    C.SearchAvailabilityStatus.Limited => CertificationStatus.Limited,
                    C.SearchAvailabilityStatus.Error => CertificationStatus.Error,
                    C.SearchAvailabilityStatus.NotCertified => CertificationStatus.NotCertified,
                    _ => primaryProven ? CertificationStatus.NotRequired : CertificationStatus.NotCertified
                }
            };

            ProbabilityValue Value(double probability, C.ProbabilityFraction primary, C.SearchAvailabilityInterval? interval) {
                RationalProbability fraction = Fraction(primary);
                CertifiedInterval? certified = primaryProven ? new(fraction, fraction)
                    : interval is null ? null : new(Fraction(interval.Lower), Fraction(interval.Upper));
                ProbabilityAccuracy rowAccuracy = certified is null ? ProbabilityAccuracy.Uncertified
                    : certified.Lower == certified.Upper ? ProbabilityAccuracy.Exact : ProbabilityAccuracy.Bounded;
                return new(probability, fraction, rowAccuracy, certified);
            }
        }
        catch (OperationCanceledException) {
            return Failure(CalculationStatus.Cancelled, "Calculation cancelled.");
        }
        catch (C.ProbabilityCalculationLimitException exception) {
            return Failure(exception.Reason == C.ProbabilityCalculationLimitReason.Work ? CalculationStatus.WorkLimit : CalculationStatus.StorageLimit, exception.Message);
        }
        catch (C.UnsupportedExactCalculationException exception) {
            return Failure(CalculationStatus.Unsupported, exception.Contract.Reason);
        }
        catch (NotSupportedException exception) {
            return Failure(CalculationStatus.Unsupported, exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException) {
            return Failure(CalculationStatus.InvalidInput, exception.Message);
        }

        CalculationMetrics Metrics() => new(budget?.Spent ?? 0, budget?.Remaining, budget?.PeakEntries ?? 0, budget?.PeakCells ?? 0);
        CalculationResult Failure(CalculationStatus status, string detail) => new(status, options.Mode, scope, detail, null, null, [], [], Metrics());
    }

    /// <summary>Produces conservative structural risk guidance using a separate bounded assessment budget.</summary>
    /// <param name="request">Problem to assess using the fixed-effect model.</param>
    /// <returns>A warning flag, not validation or a prediction of completion, work units or elapsed time.</returns>
    /// <remarks>ElevatedRisk = false means no warning was produced. Invalid, unsupported and assessment-budget-limited
    /// inputs also return false. Always handle the terminal result from Calculate or CalculateUnion.</remarks>
    public static CalculationAssessment Assess(CalculationRequest request) {
        CalculationOptions options = new();
        WorkBudget budget = new(new C.CalculationWorkPolicy(100_000));
        try {
            PreparedCalculation prepared = PreparedCalculation.Create(request, options, budget);
            return new(DrawExecutionAssessment.Assess(prepared.Cards, prepared.Combos, request.HandSize, true).ElevatedRisk);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or C.ProbabilityCalculationLimitException) {
            return new(false);
        }
    }

    private static RationalProbability Fraction(C.ProbabilityFraction fraction) => new(fraction.Numerator, fraction.Denominator);
}
