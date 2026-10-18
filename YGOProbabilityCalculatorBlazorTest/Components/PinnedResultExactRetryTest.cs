using Bunit;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public partial class PinnedResultTest {
    private Task RetryExact() => _cut.Find(".exact-retry > button").ClickAsync(new());

    private async Task<PinnedResultSnapshot> AcceptUncertainFast(CalculationAccuracy accuracy = CalculationAccuracy.Bounded) {
        await Upload(new SessionState {
            Cards = ThreeCalculationModesTest.Counterexample("banish"),
            Categories = [ThreeCalculationModesTest.Target],
            Combos = ThreeCalculationModesTest.Combos,
            ComboGroups = ThreeCalculationModesTest.Groups,
            HandSize = 2
        });
        Task calculation = Start();
        int job = _worker.Jobs.Count - 1;
        ProbabilityCalculationResult fast = CalculationWire.ReadResult(CalculationWire.Execute(_worker.Inputs[job].Json));
        Assert.That(fast.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Bounded));
        // Exercise the accepted Uncertified contract too, without inventing a certified bound.
        CalculationResultContract contract = fast.Contract with { Accuracy = accuracy, AutomaticExact = AutomaticExactOutcome.Deadline };
        _worker.Jobs[job].SetResult(fast with {
            Contract = contract,
            SearchAvailability = accuracy == CalculationAccuracy.Uncertified ? null : fast.SearchAvailability
        });
        await calculation;
        return Accepted;
    }

    [TestCase(CalculationAccuracy.Bounded)]
    [TestCase(CalculationAccuracy.Uncertified)]
    public async Task ExactRetryPinsAcceptedFastBeforeDispatchAndPreservesPreferenceAndEveryInput(CalculationAccuracy accuracy) {
        PinnedResultSnapshot fast = await AcceptUncertainFast(accuracy);
        string saved = _sessions.SerializeSession(FieldSession());
        Assert.That(_cut.FindAll(".calculation-feedback-slot .exact-retry > button"), Has.Count.EqualTo(1));
        Assert.That(_cut.Find("#exactRetryWarning").TextContent, Does.Contain("may take longer"));
        Task exact = RetryExact();
        Assert.That(Pinned, Is.SameAs(fast));
        Assert.That(Accepted, Is.SameAs(fast));
        Assert.That(_worker.Inputs[1].RequestedMode, Is.EqualTo(CalculationModel.Exact));
        JsonNode original = JsonNode.Parse(_worker.Inputs[0].Json)!;
        original["Options"]!["Mode"] = (int)CalculationModel.Exact;
        Assert.That(JsonNode.DeepEquals(original, JsonNode.Parse(_worker.Inputs[1].Json)), Is.True,
            "Only the request-local mode changes; Work, Storage, policy and workspace stay intact.");
        Assert.That(_cut.FindAll(".exact-retry > button"), Is.Empty);
        await Call("RequestExactRetry");
        await Call("ConfirmExactRetry");
        Assert.That(_worker.Jobs, Has.Count.EqualTo(2));
        ProbabilityCalculationResult physical = CalculationWire.ReadResult(CalculationWire.Execute(_worker.Inputs[1].Json), CalculationModel.Exact);
        _worker.Jobs[1].SetResult(physical);
        await exact;
        Assert.That(Pinned, Is.SameAs(fast));
        Assert.That(Accepted.Contract, Is.SameAs(physical.Contract));
        Assert.That(Accepted.Context.Model, Is.EqualTo(CalculationModel.Exact));
        Assert.That(_cut.Find(".probability-results .result-accuracy").TextContent, Is.EqualTo("Mode: Effects — exact"));
        Assert.That(_cut.Find(".pinned-result .result-accuracy").TextContent, Is.EqualTo("Mode: Effects — fast"));
        Assert.That(_cut.FindAll(".exact-retry > button"), Is.Empty);
        Assert.That(_sessions.SerializeSession(FieldSession()), Is.EqualTo(saved));
        await _cut.Find("button[title='Copy results']").ClickAsync(new());
        Assert.That(_clipboard.Invocations["copyText"].Last().Arguments[0], Does.Contain("Mode: Effects — exact"));
        Assert.That(fast.Contract!.AutomaticExact, Is.EqualTo(AutomaticExactOutcome.Deadline));
        AssertNoComparisonUi();
        Task normal = Start();
        Assert.That(_worker.Inputs[2].RequestedMode, Is.EqualTo(CalculationModel.Fast));
        _worker.Jobs[2].SetResult(CalculationWire.ReadResult(CalculationWire.Execute(_worker.Inputs[2].Json)));
        await normal;
    }

    [TestCase("work")]
    [TestCase("storage")]
    [TestCase("unsupported")]
    [TestCase("error")]
    [TestCase("cancel")]
    [TestCase("edit")]
    [TestCase("late")]
    [TestCase("dispose")]
    public async Task ExactRetryFailureCancellationAndLateResponsesKeepTheOriginalFastPin(string ending) {
        PinnedResultSnapshot fast = await AcceptUncertainFast();
        Task retry = RetryExact();
        Assert.That(Pinned, Is.SameAs(fast));
        if (ending is "cancel" or "late") {
            await _cut.Find("[aria-label='Cancel calculation']").ClickAsync(new());
        }
        else if (ending == "edit") {
            await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }
        else if (ending == "dispose") {
            await _cut.InvokeAsync(() => _cut.Instance.DisposeAsync().AsTask());
        }

        switch (ending) {
            case "work":
            case "storage":
                _worker.Jobs[1].SetException(new ProbabilityCalculationLimitException(ending == "work"
                    ? ProbabilityCalculationLimitReason.Work : ProbabilityCalculationLimitReason.Storage));
                break;
            case "unsupported":
                _worker.Jobs[1].SetException(new UnsupportedExactCalculationException(fast.Contract! with {
                    RequestedMode = CalculationModel.Exact, ExecutedMode = CalculationModel.Exact,
                    Completion = CalculationCompletion.Unsupported, Accuracy = null
                }));
                break;
            case "error":
                _worker.Jobs[1].SetException(new InvalidOperationException("Exact worker failed"));
                break;
            case "late":
            case "edit":
            case "dispose":
                _worker.Jobs[1].SetResult(CalculationWire.ReadResult(CalculationWire.Execute(_worker.Inputs[1].Json)));
                break;
            default:
                _worker.Jobs[1].SetCanceled();
                break;
        }
        await retry;
        Assert.That(Field<PinnedResultSnapshot>("_pinnedResult"), Is.SameAs(fast));
        Assert.That(Accepted, Is.SameAs(fast));
        if (ending != "dispose") {
            Assert.That(_cut.Find(".probability-results .result-accuracy").TextContent, Is.EqualTo("Mode: Effects — fast"));
            Assert.That(_cut.FindAll(".probability-result-status").Count > 0, Is.EqualTo(ending == "edit"));
            Assert.That(_cut.FindAll(".exact-retry > button").Count, Is.EqualTo(ending == "edit" ? 0 : 1));
        }
    }

    [TestCase("confirm")]
    [TestCase("cancel")]
    [TestCase("edit")]
    [TestCase("draft")]
    [TestCase("calculate")]
    public async Task UnrelatedPinRequiresAValidExplicitReplacementDecision(string decision) {
        await Accept();
        await Pin();
        PinnedResultSnapshot previous = Pinned;
        PinnedResultSnapshot fast = await AcceptUncertainFast();
        await RetryExact();
        Assert.That(Pinned, Is.SameAs(previous));
        Assert.That(_worker.Jobs, Has.Count.EqualTo(2));
        Assert.That(_cut.FindAll(".exact-retry-confirmation"), Has.Count.EqualTo(1));
        if (decision == "confirm") {
            Task retry = _cut.Find(".exact-retry-confirmation .btn-outline-primary").ClickAsync(new());
            Assert.That(Pinned, Is.SameAs(fast));
            Assert.That(_worker.Jobs, Has.Count.EqualTo(3));
            await Call("ConfirmExactRetry");
            Assert.That(_worker.Jobs, Has.Count.EqualTo(3));
            _worker.Jobs[2].SetCanceled();
            await retry;
            return;
        }
        if (decision == "edit") {
            await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }
        else if (decision == "draft") {
            await _cut.Find("#handSize").InputAsync(new() { Value = "3" });
        }
        else if (decision == "calculate") {
            Task calculation = Start();
            _worker.Jobs[2].SetResult(CalculationWire.ReadResult(CalculationWire.Execute(_worker.Inputs[2].Json)));
            await calculation;
        }
        else {
            await _cut.Find(".exact-retry-confirmation .btn-outline-secondary").ClickAsync(new());
        }
        await Call("ConfirmExactRetry");
        Assert.That(_worker.Jobs, Has.Count.EqualTo(decision == "calculate" ? 3 : 2));
        Assert.That(Pinned, Is.SameAs(previous));
        Assert.That(_cut.FindAll(".exact-retry-confirmation"), Is.Empty);
    }

    [Test]
    public async Task AlreadyPinnedFastRetriesWithoutReplacementAndEditedResultsCannotRetry() {
        await AcceptUncertainFast();
        await Pin();
        Task exact = RetryExact();
        Assert.That(_cut.FindAll(".exact-retry-confirmation"), Is.Empty);
        _worker.Jobs[1].SetCanceled();
        await exact;
        await _cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        await Call("RequestExactRetry");
        Assert.That(_worker.Jobs, Has.Count.EqualTo(2));
        Assert.That(_cut.FindAll(".exact-retry > button"), Is.Empty);
    }

    [TestCase(CalculationModel.Fast)]
    [TestCase(CalculationModel.Exact)]
    [TestCase(CalculationModel.OpeningHand)]
    public async Task ProvenExactAndOtherModesNeverOfferRetry(CalculationModel mode) {
        Assert.That(_cut.FindAll(".exact-retry > button"), Is.Empty);
        await _cut.Find("#calculationModel").ChangeAsync(new() { Value = mode.ToString() });
        await AcceptWithAccuracy(CalculationAccuracy.Exact, .5);
        Assert.That(_cut.FindAll(".exact-retry > button"), Is.Empty);
        Assert.That(_cut.Find(".result-accuracy").TextContent, Is.EqualTo($"Mode: {mode.Description()}"));
    }

    [TestCase("invalid-value")]
    [TestCase("missing-contract")]
    [TestCase("missing-accuracy")]
    [TestCase("failed-contract")]
    public async Task InvalidOrIncompleteAcceptedResultsCannotLaunchExactRetry(string failure) {
        await AcceptUncertainFast();
        Task calculation = Start();
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(_worker.Inputs[1].Json));
        CalculationResultContract? contract = failure switch {
            "missing-contract" => null,
            "missing-accuracy" => result.Contract! with { Accuracy = null },
            "failed-contract" => result.Contract! with { Completion = CalculationCompletion.Error },
            _ => result.Contract
        };
        _worker.Jobs[1].SetResult(result with { TotalProbability = failure == "invalid-value" ? double.NaN : result.TotalProbability, Contract = contract });
        await calculation;
        Assert.That(_cut.FindAll(".exact-retry > button"), Is.Empty);
        await Call("RequestExactRetry");
        Assert.That(_worker.Jobs, Has.Count.EqualTo(2));
        Assert.That(_cut.FindAll(".pinned-result"), Is.Empty);
    }
}
