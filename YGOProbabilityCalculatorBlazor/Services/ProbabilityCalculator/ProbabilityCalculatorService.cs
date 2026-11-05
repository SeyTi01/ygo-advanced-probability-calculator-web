using CardProbability;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CalculationEngineAdapter;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using CalculationOptions = YGOProbabilityCalculatorBlazor.Services.Interface.CalculationOptions;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Compatibility at the application edge. Every execution crosses the same public
// boundary used by workers and by consumers outside the application assembly.
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
    ) => CalculateCore(deck, combos, handSize, null, workPolicy, storagePolicy, options, out _,
        optimisticScalar: options is null, unionOnly: options?.Mode is null or CalculationModel.Fast).TotalProbability;

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
    ) => CalculateCore(deck, combos, handSize, groups, workPolicy, storagePolicy, options, out _);

    internal ProbabilityCalculationResult CalculateCore(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        IReadOnlyList<ComboGroup>? groups,
        CalculationWorkPolicy workPolicy,
        CalculationStoragePolicy storagePolicy,
        CalculationOptions? options,
        out long remainingWork,
        bool physicalFast = false,
        bool optimisticScalar = false,
        bool unionOnly = false
    ) {
        remainingWork = 0;
        ArgumentNullException.ThrowIfNull(workPolicy);
        ArgumentNullException.ThrowIfNull(storagePolicy);
        if (options is not null && !Enum.IsDefined(options.Mode)) {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown calculation mode.");
        }
        if (options?.CertifySearchAvailability == true && options.Mode != CalculationModel.Fast) {
            throw new ArgumentException("The legacy certification flag is only compatible with Fast mode.", nameof(options));
        }
        if (combos.Count > IProbabilityCalculatorService.MaxComboCount) {
            throw new ArgumentOutOfRangeException(nameof(combos), $"Calculation supports at most {IProbabilityCalculatorService.MaxComboCount} combos.");
        }
        CalculationModel mode = options?.Mode ?? CalculationModel.Fast;
        CalculationRequest request = new CalculationRequestAdapter(deck, mode != CalculationModel.OpeningHand).Create(combos, handSize, groups);
        CalculationMode engineMode = mode switch {
            CalculationModel.OpeningHand => CalculationMode.OpeningHand,
            CalculationModel.Exact => CalculationMode.Exact,
            _ when physicalFast => CalculationMode.Exact,
            _ when optimisticScalar || options?.CertifySearchAvailability == true => CalculationMode.OptimisticSearchProjection,
            _ => CalculationMode.Fast
        };
        DrawActivationPriority? priority = options?.DrawActivationPriority;
        HashSet<string> entryIds = request.Entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        CardProbability.CalculationOptions engineOptions = new(
            engineMode,
            new(workPolicy.IsUnlimited ? null : workPolicy.WorkUnits, storagePolicy.EntryLimit, storagePolicy.CellLimit),
            priority?.Mode == DrawActivationMode.CustomPriority ? ActivationOrder.Priority : ActivationOrder.EncounterOrder,
            priority?.CardIds.Where(entryIds.Contains));
        CalculationResult result = unionOnly
            ? CalculationEngine.CalculateUnion(request, engineOptions)
            : CalculationEngine.Calculate(request, engineOptions);
        remainingWork = result.Metrics.RemainingWork ?? 0;

        return CalculationResultAdapter.Translate(result, combos, groups, mode);
    }
}
