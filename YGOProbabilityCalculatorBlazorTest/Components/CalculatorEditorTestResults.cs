using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorTestResults : CalculatorEditorTestBase {

    [TestCase(false)]
    [TestCase(true)]
    public async Task SessionLoadClearsPreviousResultsAndDiscardsInFlightCompletion(bool failCalculation) {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(0.9, [new ComboProbabilityResult(0, "Old session", 0.8)]),
            failSecond: failCalculation);
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());
        Assert.That(context.Services.GetRequiredService<IPendingSessionService>().PendingSession, Is.Null);

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            const string nextSession = """
                {
                  "Categories": [{"Name":"Loaded"}],
                  "Cards": [{"Categories":[{"Name":"Loaded"}],"Copies":3,"Name":"Loaded card"}],
                  "Combos": [{"Categories":[{"BaseCategory":{"Name":"Loaded"},"MinCount":0,"MaxCount":0}],"Name":"Loaded combo"}],
                  "HandSize": 1
                }
                """;
            cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(nextSession, "next.json"));
            {
                Assert.That(cut.FindAll(".probability-results"), Is.Empty);
                Assert.That(cut.Find("#handSize").GetAttribute("value"), Is.EqualTo("1"));
                Assert.That(cut.FindComponent<CardEditor>().Instance.Card.Name, Is.EqualTo("Loaded card"));
                Assert.That(cut.FindComponent<ComboEditor>().Instance.Combo.Name, Is.EqualTo("Loaded combo"));
            }
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(cut.FindAll("[role=alert]"), Is.Empty);
    }

    [Test]
    public async Task ResultsShowStandaloneProbabilitiesInOrderForDuplicateAndUnnamedCombos() {
        var session = new SessionState {
            Categories = [a, b],
            Cards = [new([a], 2, "A copies"), new([b], 2, "B copies")],
            Combos = [
                new([new(a, 1, 1)], "Duplicate"),
                new([new(a, 1, 1)], "Duplicate"),
                new([new(b, 0, 0)])
            ],
            HandSize = 2
        };
        var cut = Render(session);
        var expectedTotal = SmallDeckOracle.EnumerateProbability(session.Cards, session.Combos, session.HandSize);
        var expectedStandalone = session.Combos
            .Select(combo => SmallDeckOracle.EnumerateProbability(session.Cards, [combo], session.HandSize))
            .ToArray();

        await Button(cut, "Calculate").ClickAsync(new());
        {
            var result = cut.Find(".probability-results");
            Assert.That(result.GetAttribute("aria-live"), Is.EqualTo("polite"));
            var totalRow = result.QuerySelector(".probability-total")!;
            Assert.That(totalRow.ClassList.Contains("combo-probability-row"), Is.True);
            Assert.That(totalRow.ParentElement!.ClassList.Contains("probability-results"), Is.True,
                "the summary row must sit outside the numbered combo list");
            Assert.That(totalRow.QuerySelector(".combo-probability-name")!.TextContent.Trim(),
                Is.EqualTo("Any active combo"));
            Assert.That(totalRow.QuerySelector(".combo-probability-value")!.TextContent,
                Is.EqualTo(expectedTotal.ToString("P2")));

            var rows = result.QuerySelectorAll(".combo-probability-item");
            Assert.That(rows.Length, Is.EqualTo(3));
            Assert.That(rows.All(row => row.QuerySelector(".combo-probability-row") is not null), Is.True);
            Assert.That(rows.Select(row => row.QuerySelector(".combo-probability-name")!.TextContent),
                Is.EqualTo(new[] { "Duplicate", "Duplicate", "Unnamed combo 3" }));
            for (var index = 0; index < rows.Length; index++) {
                Assert.That(rows[index].QuerySelector(".combo-probability-value")!.TextContent,
                    Is.EqualTo(expectedStandalone[index].ToString("P2")));
            }
        }
    }

    [Test]
    public async Task CategoryRenameInvalidatesAndRecalculatesTheWholeResultSet() {
        var cut = Render(Session());
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));

        await cut.Find("[aria-label='Edit category A']").ClickAsync(new());
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "Renamed A" });
        await cut.Find("[aria-label='Save category name']").ClickAsync(new());
        AssertPreviousResult(cut);

        var cards = cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card)
            .Where(card => card.Active).ToList();
        var combos = cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo)
            .Where(combo => combo.Active).ToList();
        var expected = SmallDeckOracle.EnumerateProbability(cards, combos, 2);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(
            cut.Find(".probability-total").TextContent,
            Does.Contain(expected.ToString("P2")));
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
    }

    [Test]
    public async Task ResultsRemainVisibleAndAreReplacedOnlyWhenRecalculationSucceeds() {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(
                0.75,
                [new ComboProbabilityResult(0, "Updated combo", 0.6)]));
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find(".combo-probability-item").TextContent, Does.Contain("Original combo"));
        var copyButton = cut.Find("button[title='Copy a summary of these results']");
        Assert.That(copyButton.HasAttribute("disabled"), Is.False);

        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        AssertPreviousResult(cut);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.False);
        Assert.That(calculator.CallCount, Is.EqualTo(1), "input edits must not calculate automatically");

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var runningAction = cut.Find(".calculate-action > button");
            Assert.That(runningAction.GetAttribute("aria-label"), Is.EqualTo("Cancel calculation"));
            Assert.That(runningAction.HasAttribute("disabled"), Is.False);
            Assert.That(cut.FindAll(".calculate-action > button"), Has.Count.EqualTo(1));
            Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.True);
            AssertPreviousResult(cut);
            Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.75.ToString("P2")));
        Assert.That(cut.Find(".combo-probability-item").TextContent, Does.Contain("Updated combo"));
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
    }

    [Test]
    public async Task InvalidatedInFlightCalculationCannotReplaceThePreviousResult() {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(
                0.9,
                [new ComboProbabilityResult(0, "Stale completion", 0.8)]));
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
            AssertPreviousResult(cut);
            Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        AssertPreviousResult(cut);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find(".combo-probability-item").TextContent, Does.Contain("Original combo"));
        Assert.That(cut.Markup, Does.Not.Contain("Stale completion"));
    }

    [Test]
    public async Task CalculationErrorKeepsOldNumbersMarkedAsPreviousInputs() {
        var calculator = new SequencedProbabilityCalculator(
            new ProbabilityCalculationResult(
                0.9,
                [new ComboProbabilityResult(0, "Unused result", 0.8)]),
            failSecond: true);
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());

        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        AssertPreviousResult(cut);

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally {
            calculator.ContinueSecond.Set();
            await calculation;
        }

        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        AssertPreviousResult(cut);
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.25.ToString("P2")));
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("Calculation failed: expected test failure"));
    }

    [Test]
    public async Task InputChangeDuringCalculationCannotRestoreStaleTotalOrComboRows() {
        var delayedCalculator = new DelayedProbabilityCalculator();
        context.Services.AddSingleton<IProbabilityCalculatorService>(delayedCalculator);
        var cut = Render(Session());

        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await delayedCalculator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
            Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        }
        finally {
            delayedCalculator.Continue.Set();
            await calculation;
        }

        Assert.That(calculation.IsCompletedSuccessfully, Is.True, "the stale event handler must finish before checking its result");
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ResourceLimitIsExplainedUnlessInputsHaveChanged(bool changeInputs) {
        var calculator = new DelayedProbabilityCalculator { ExceedLimit = true };
        context.Services.AddSingleton<IProbabilityCalculatorService>(calculator);
        var cut = Render(Session());
        var calculation = Button(cut, "Calculate").ClickAsync(new());
        try {
            await calculator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (changeInputs) await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }
        finally {
            calculator.Continue.Set();
            await calculation;
        }
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(cut.Markup.Contains("Calculation stopped"), Is.EqualTo(!changeInputs));
    }

}
