using System.Globalization;
using System.Reflection;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
[SetCulture("en-US")]
public class PinnedResultTest
{
    private TestContext context = null!;
    private ControlledCalculator worker = null!;
    private IRenderedComponent<ProbabilityCalculatorComponent> cut = null!;
    private ISessionService sessions = null!;
    private BunitJSModuleInterop clipboard = null!;
    private Mock<IDeckImportService> imports = null!;
    private static readonly CategoryBase role = new("Role");
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp]
    public void Setup()
    {
        context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<ISerializer, JsonSerializer>();
        context.Services.AddSingleton<ISessionService, SessionService>();
        context.Services.AddSingleton(Mock.Of<ILegacyCardMetadataEnricher>());
        context.Services.AddSingleton(Mock.Of<ICardArtworkService>());
        imports = new();
        context.Services.AddSingleton(imports.Object);
        worker = new();
        context.Services.AddSingleton<IBackgroundCalculator>(worker);
        context.Services.AddSingleton<IPendingSessionService>(
            new PendingSessionService { PendingSession = Workspace() }
        );
        clipboard = context.JSInterop.SetupModule("./js/probabilityResultExport.js");
        clipboard.Mode = JSRuntimeMode.Loose;
        clipboard.Setup<bool>("copyText", _ => true).SetResult(true);
        sessions = context.Services.GetRequiredService<ISessionService>();
        cut = context.RenderComponent<ProbabilityCalculatorComponent>();
    }

    [TearDown]
    public void Cleanup()
    {
        cut.Dispose();
        context.Dispose();
    }

    private static SessionState Workspace(int handSize = 2) => new()
    {
        Cards = [new([role], 4, "A", id: "a"), new([], 4, "B", id: "b")],
        Categories = [role], HandSize = handSize,
        ComboGroups = [new("g", "Group")],
        Combos =
        [
            new([], "Duplicate", groupId: "g", cards: [new("a", 1, 5)]),
            new([new(role, 1, 5)], "Duplicate"), new([], null, cards: [new("b", 1, 5)]),
            new([], "Inactive", false, cards: [new("b", 1, 5)])
        ]
    };

    private T Field<T>(string name) =>
        (T)typeof(ProbabilityCalculatorComponent).GetField(name, Private)!.GetValue(cut.Instance)!;

    private Task Call(string name, params object[] args) => cut.InvokeAsync(async () =>
        {
            object? result =
                typeof(ProbabilityCalculatorComponent).GetMethod(name, Private)!.Invoke(cut.Instance, args);

            if (result is Task task)
            {
                await task;
            }

            typeof(ComponentBase).GetMethod("StateHasChanged", Private)!.Invoke(
                cut.Instance,
                null
            );
        }
    );

    private Task Start() => cut.Find(".calculate-action > button").ClickAsync(new());

    [Test]
    public async Task OrSignatureFreezesStructureAndIgnoresAlternativeAndGroupOrder()
    {
        Combo original = Field<List<Combo>>("combos")[0];
        ComboAlternativeGroup first = new([
                ComboAlternative.For(new ComboCard("a", 1, 5)), ComboAlternative.For(new ComboCategory(role, 1, 5))
            ]
        );
        ComboAlternativeGroup second = new([
                ComboAlternative.For(new ComboCard("b", 0, 0)), ComboAlternative.For(new ComboCard("a", 0, 0))
            ]
        );
        Combo grouped = original.WithCards([]).WithAlternativeGroups([first, second]);
        await Call("ReplaceCombo", (0, grouped));
        await Accept();
        await Pin();
        string signature = Pinned.Combos[0].Definition!.Signature;
        await Call("ReplaceCombo",
            (0, grouped.WithAlternativeGroups([second, new(first.Alternatives.Reverse().ToArray())]))
        );
        await Accept();
        Assert.That(cut.Find(".probability-results").TextContent, Does.Not.Contain("Definition changed"));
        Combo edited = grouped.WithAlternativeGroups([new([first.Alternatives[0]]), second]);
        await Call("ReplaceCombo", (0, edited));
        Assert.That(Pinned.Combos[0].Definition!.Signature, Is.EqualTo(signature));
        await Accept();
        Assert.That(cut.Find(".probability-results").TextContent, Does.Contain("Definition changed"));
    }

    private Task Pin() => cut.Find(".pin-result-action").ClickAsync(new());

    private Task Clear() => cut.Find(".pinned-result button").ClickAsync(new());

    private PinnedResultSnapshot Pinned => cut.FindComponent<PinnedResultPanel>().Instance.Snapshot;

    private (string Current, string Pinned) ContextDescriptions() => (
        cut.Find(".probability-results > p.small").TextContent.Trim(),
        cut.FindComponent<PinnedResultPanel>().Find(".pinned-result > p.small").TextContent.Trim());

    private void AssertContextDescriptions(string current, string pinned)
    {
        (string Current, string Pinned) actual = ContextDescriptions();
        Assert.That(actual.Current, Is.EqualTo(current));
        Assert.That(actual.Pinned, Is.EqualTo(pinned));
        Assert.That(cut.Find(".probability-results > p.small").GetAttribute("class"),
            Is.EqualTo("small text-body-secondary mb-2")
        );
        Assert.That(cut.FindComponent<PinnedResultPanel>().Find(".pinned-result > p.small").GetAttribute("class"),
            Is.EqualTo("small text-body-secondary mb-2")
        );
        Assert.That(actual.Current,
            Does.Not.Contain("Different hand size").And.Not.Contain("Different deck composition")
        );
        Assert.That(actual.Pinned,
            Does.Not.Contain("Different hand size").And.Not.Contain("Different deck composition")
        );
    }

    private ProbabilityCalculationResult Result(int job, double total)
    {
        CalculationInput input =
            System.Text.Json.JsonSerializer.Deserialize<CalculationInput>(worker.Inputs[job].Json)!;

        return new(total,
            input.Combos.Select((c, i) => new ComboProbabilityResult(i, c.Name, total, c.GroupId)).ToList(),
            input
                .Groups.Select(g =>
                    new GroupProbabilityResult(g.Id, g.Name, total, input.Combos.Count(c => c.GroupId == g.Id))
                )
                .ToList()
        );
    }

    private async Task Accept(double probability = .814)
    {
        Task action = Start();
        int job = worker.Jobs.Count - 1;
        worker.Jobs[job].SetResult(Result(job, probability));
        await action;
    }

    private void AssertAllComparisonRows(string text, string cssClass)
    {
        IElement[] differences = [.. cut.FindAll(".result-difference")];
        Assert.That(differences, Has.Length.EqualTo(5));

        foreach (IElement difference in differences)
        {
            Assert.That(difference.TextContent, Is.EqualTo(text));
            Assert.That(difference.ClassList, Does.Contain(cssClass));
            Assert.That(difference.GetAttribute("aria-label"),
                Is.EqualTo($"Absolute change from pinned result: {text}")
            );
        }
    }

    private Task Upload(SessionState session) => cut.InvokeAsync(() => cut.FindComponents<InputFile>()[1]
        .Instance
        .OnChange.InvokeAsync(
            new InputFileChangeEventArgs([new SessionFile(sessions.SerializeSession(session))])
        )
    );

    [Test]
    public async Task PinReplaceClearAreExplicitAndDoNotChangeSessionAutosaveOrCalculationVersion()
    {
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        Assert.That(cut.FindAll(".pinned-result"), Is.Empty);
        await Accept();
        Assert.That(cut.FindAll(".pinned-result"), Is.Empty);
        string before = sessions.SerializeSession(FieldSession());
        long version = Field<long>("calculationVersion");
        int writes = context.JSInterop.Invocations["sessionRecovery.update"].Count;
        await Pin();
        PinnedResultSnapshot first = Pinned;
        AssertContextDescriptions("Hand size 2 · 8 active copies · 2 active cards",
            "Hand size 2 · 8 active copies · 2 active cards"
        );
        await Accept(.842);
        Assert.That(Pinned, Is.SameAs(first));
        Assert.That(cut.Find(".result-difference").TextContent, Is.EqualTo("+2.8%"));
        await Pin();
        Assert.That(Pinned.Total, Is.EqualTo(.842));
        Assert.That(cut.Find(".result-difference").TextContent, Is.EqualTo("0%"));
        await Clear();
        Assert.That(cut.FindAll(".pinned-result"), Is.Empty);
        Assert.That(cut.FindAll(".result-difference"), Is.Empty);
        Assert.That(sessions.SerializeSession(FieldSession()), Is.EqualTo(before));
        Assert.That(Field<long>("calculationVersion"), Is.EqualTo(version));
        Assert.That(context.JSInterop.Invocations["sessionRecovery.update"], Has.Count.EqualTo(writes));
    }

    private SessionState FieldSession() =>
        (SessionState)typeof(ProbabilityCalculatorComponent).GetMethod("CaptureSession", Private)!.Invoke(cut.Instance,
            null
        )!;

    [Test]
    public async Task PinOwnsRequestContextAndCopiesMutableResultsAndNestedDefinitions()
    {
        Task action = Start();
        CalculationInput input = System.Text.Json.JsonSerializer.Deserialize<CalculationInput>(worker.Inputs[0].Json)!;
        Assert.That(input.WorkUnits,
            Is.EqualTo(50_000_000),
            "ordinary Calculate selects the interactive policy on its first request"
        );
        Assert.That(input.HandSize, Is.EqualTo(2));
        ProbabilityCalculationResult result = Result(0, .814);
        // An unannounced edit specifically verifies capture timing, independent of invalidation.
        Field<List<Card>>("cards")[0].Categories.Clear();
        Field<List<Combo>>("combos")[0].Cards.Clear();
        typeof(ProbabilityCalculatorComponent).GetField("handSize", Private)!.SetValue(cut.Instance, 3);
        worker.Jobs[0].SetResult(result);
        await action;
        await Pin();
        PinnedResultSnapshot frozen = Pinned;
        ((List<ComboProbabilityResult>)result.ComboProbabilities).Clear();
        ((List<GroupProbabilityResult>)result.GroupProbabilities!).Clear();
        await Call("RenameGroup", ("g", "Changed group"));
        Assert.That(Pinned, Is.SameAs(frozen));
        Assert.That(Pinned.Context.HandSize, Is.EqualTo(2));
        Assert.That(Pinned.Context.Copies, Is.EqualTo(8));
        Assert.That(Pinned.Context.DeckDefinition, Does.Contain("user:Role"));
        Assert.That(Pinned.Combos, Has.Length.EqualTo(3));
        Assert.That(Pinned.Combos[2].Label, Is.EqualTo("Unnamed combo 3"));
        Assert.That(Pinned.Groups[0].Label, Is.EqualTo("Group"));
        Assert.That(Pinned.Combos[0].Definition!.Signature, Does.Contain("card"));
    }

    [TestCase(.842, "+2.8%")]
    [TestCase(.786, "-2.8%")]
    [TestCase(.814, "0%")]
    [TestCase(.813999999, "0%")]
    [TestCase(.814049, "0%")]
    [TestCase(.81405, "+0.01%")]
    [TestCase(.81395, "-0.01%")]
    public async Task AcceptedDeltasUsePercentagePointsWithoutNegativeZero(double probability, string expected)
    {
        await Accept();
        await Pin();
        await Accept(probability);
        Assert.That(cut.Find(".result-difference").TextContent, Is.EqualTo(expected));
    }

    [Test]
    public async Task DisplayedDeltasStayInlineAndUseRoundedDirectionForEveryResultRow()
    {
        await Accept();
        await Pin();
        await Accept(.842);

        Assert.That(cut.Find(".probability-total .result-difference").TextContent, Is.EqualTo("+2.8%"));
        Assert.That(cut.Find(".probability-group-heading .result-difference").TextContent, Is.EqualTo("+2.8%"));
        Assert.That(cut.Find(".probability-group-members .result-difference").TextContent, Is.EqualTo("+2.8%"));
        Assert.That(cut.FindAll(".probability-ungrouped-combos .result-difference"), Has.Count.EqualTo(2));
        IRefreshableElementCollection<IElement> positive = cut.FindAll(".result-difference");
        Assert.That(positive, Has.Count.EqualTo(5));

        foreach (IElement difference in positive)
        {
            Assert.That(difference.ClassList, Does.Contain("result-difference-positive"));
            Assert.That(difference.ParentElement!.ClassList, Does.Contain("result-value-group"));
            Assert.That(difference.ParentElement.QuerySelector(".combo-probability-value"), Is.Not.Null);
            Assert.That(difference.GetAttribute("aria-label"), Does.Contain("Absolute change from pinned result"));
        }

        await Accept(.786);
        Assert.That(cut.FindAll(".result-difference-negative"), Has.Count.EqualTo(5));
        Assert.That(cut.FindAll(".result-difference-positive"), Is.Empty);
        await Pin();
        Assert.That(cut.FindAll(".result-difference-neutral"), Has.Count.EqualTo(5));
        Assert.That(cut.FindAll(".result-difference-negative"), Is.Empty);
    }

    [Test]
    public void DifferenceUsesCultureAndRejectsNonfiniteProbabilities()
    {
        using CultureScope culture = new("de-DE");
        Assert.That(PinnedResultSnapshot.Difference(.842, .814), Is.EqualTo("+2,8 %"));
        Assert.That(PinnedResultSnapshot.Difference(.786, .814), Is.EqualTo("-2,8 %"));
        Assert.That(PinnedResultSnapshot.Difference(.814, .814), Is.EqualTo("0 %"));
        Assert.That(PinnedResultSnapshot.Difference(double.NaN, .814), Is.EqualTo("Not comparable"));
        Assert.That(PinnedResultSnapshot.Difference(double.PositiveInfinity, .814), Is.EqualTo("Not comparable"));
    }

    [Test]
    public async Task InvalidResultCannotBePinnedOrPresentedAsAnImprovement()
    {
        await Accept();
        await Pin();
        await Accept(double.NaN);
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        Assert.That(cut.FindAll(".result-difference"), Is.Empty);
        Assert.That(Pinned.Total, Is.EqualTo(.814));
    }

    [TestCase(.842, "+2.8%", "result-difference-positive")]
    [TestCase(.786, "-2.8%", "result-difference-negative")]
    public async Task AcceptedComparisonRowsRemainVisibleWhenDisplayedResultBecomesStale(
        double probability,
        string delta,
        string cssClass
    )
    {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        await Accept(probability);
        PinnedResultSnapshot accepted = Field<PinnedResultSnapshot>("acceptedComparison");
        AssertAllComparisonRows(delta, cssClass);

        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });

        Assert.That(cut.Find(".probability-result-status").TextContent, Does.Contain("Previous result"));
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(Field<PinnedResultSnapshot>("acceptedComparison"), Is.SameAs(accepted));
        AssertAllComparisonRows(delta, cssClass);

        Task action = Start();
        Assert.That(cut.Find(".calculate-action > button").GetAttribute("aria-busy"), Is.EqualTo("true"));
        AssertAllComparisonRows(delta, cssClass);
        worker.Jobs[^1].SetCanceled();
        await action;
        Assert.That(Field<PinnedResultSnapshot>("acceptedComparison"), Is.SameAs(accepted));
        AssertAllComparisonRows(delta, cssClass);
    }

    [Test]
    public async Task SuccessfulStaleRecalculationReplacesAcceptedComparisonRows()
    {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        await Accept(.842);
        PinnedResultSnapshot previous = Field<PinnedResultSnapshot>("acceptedComparison");
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        Task action = Start();
        AssertAllComparisonRows("+2.8%", "result-difference-positive");

        worker.Jobs[^1].SetResult(Result(worker.Jobs.Count - 1, .786));
        await action;

        PinnedResultSnapshot accepted = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(accepted, Is.Not.SameAs(previous));
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.False);
        AssertAllComparisonRows("-2.8%", "result-difference-negative");
    }

    [TestCase("late-success")]
    [TestCase("late-error")]
    [TestCase("cancelled")]
    [TestCase("failure")]
    [TestCase("work-limit")]
    [TestCase("storage-limit")]
    public async Task BusyStaleCancelledFailedAndLateResultsCannotReplacePinOrAcceptedComparison(string completion)
    {
        await Accept();
        await Pin();
        PinnedResultSnapshot baseline = Pinned;
        await Accept(.842);
        PinnedResultSnapshot accepted = Field<PinnedResultSnapshot>("acceptedComparison");
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        AssertAllComparisonRows("+2.8%", "result-difference-positive");
        Task action = Start();
        await Call("PinCurrentResult");
        Assert.That(Pinned, Is.SameAs(baseline), "the event handler also rejects busy/stale pinning");
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        AssertAllComparisonRows("+2.8%", "result-difference-positive");

        if (completion.StartsWith("late"))
        {
            await cut.Find("[aria-label='Cancel calculation']").ClickAsync(new());
            await Accept(.842);

            if (completion == "late-success")
            {
                worker.Jobs[2].SetResult(Result(2, .1));
            }
            else
            {
                worker.Jobs[2].SetException(new Exception("Obsolete error"));
            }
        }
        else if (completion == "cancelled")
        {
            worker.Jobs[2].SetCanceled();
        }
        else if (completion.EndsWith("limit"))
        {
            worker
                .Jobs[2]
                .SetException(new ProbabilityCalculationLimitException(
                        completion == "work-limit"
                            ? ProbabilityCalculationLimitReason.Work
                            : ProbabilityCalculationLimitReason.Storage
                    )
                );
        }
        else
        {
            worker.Jobs[2].SetException(new Exception("Current failure"));
        }

        await action;
        Assert.That(Pinned, Is.SameAs(baseline));

        if (completion.StartsWith("late"))
        {
            AssertAllComparisonRows("+2.8%", "result-difference-positive");
            Assert.That(cut.Markup, Does.Not.Contain("Obsolete error"));
        }
        else
        {
            Assert.That(Field<PinnedResultSnapshot>("acceptedComparison"), Is.SameAs(accepted));
            AssertAllComparisonRows("+2.8%", "result-difference-positive");
            Assert.That(cut.Find(".probability-result-status").TextContent, Does.Contain("Previous result"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ObsoleteCompletionCannotUnlockNewBusyRequestOrReplaceItsAcceptedContext(bool limit)
    {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        PinnedResultSnapshot accepted = Field<PinnedResultSnapshot>("acceptedComparison");
        Task old = Start();
        await cut.Find("[aria-label='Cancel calculation']").ClickAsync(new());
        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        Task current = Start();
        Assert.That(System.Text.Json.JsonSerializer.Deserialize<CalculationInput>(worker.Inputs[2].Json)!.WorkUnits,
            Is.EqualTo(50_000_000)
        );

        if (limit)
        {
            worker
                .Jobs[1]
                .SetException(new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Storage));
        }
        else
        {
            worker.Jobs[1].SetResult(Result(1, .1));
        }

        await old;
        Assert.That(Field<PinnedResultSnapshot>("acceptedComparison"), Is.SameAs(accepted));
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(cut.Find(".calculate-action > button").GetAttribute("aria-busy"), Is.EqualTo("true"));
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.True);
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
        worker.Jobs[2].SetResult(Result(2, .842));
        await current;
        Assert.That(Field<PinnedResultSnapshot>("acceptedComparison").Context.HandSize, Is.EqualTo(6));
        Assert.That(Pinned.Context.HandSize, Is.EqualTo(2));
    }

    [Test]
    public async Task ReorderRenameAndCountChangesPreserveDistinctLineageForDuplicateAndUnnamedRows()
    {
        await Accept();
        await Pin();
        PinnedResultSnapshot original = Pinned;
        await Call("MoveCombo", (0, 2));
        await Call("ReplaceCombo", (2, Field<List<Combo>>("combos")[2].WithName("Renamed")));
        await Call("RenameGroup", ("g", "Renamed group"));
        await Call("ReplaceCard", (0, Field<List<Card>>("cards")[0].WithCopies(3)));
        await Accept(.842);
        PinnedResultSnapshot current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(current.Combos[2].Definition!.Lineage, Is.EqualTo(original.Combos[0].Definition!.Lineage));
        Assert.That(current.CompareCombo(current.Combos[2], original, true), Is.EqualTo("+2.8%"));
        Assert.That(current.CompareCombo(current.Combos[0], original, true), Is.EqualTo("+2.8%"));
        Assert.That(current.CompareCombo(current.Combos[1], original, true), Is.EqualTo("+2.8%"));
        Assert.That(current.CompareGroup(current.Groups[0], original, true), Is.EqualTo("+2.8%"));
        AssertContextDescriptions("Hand size 2 · 7 active copies · 2 active cards",
            "Hand size 2 · 8 active copies · 2 active cards"
        );
        Assert.That(Pinned.Combos.Select(c => c.Label),
            Is.EqualTo(new[] { "Duplicate", "Duplicate", "Unnamed combo 3" })
        );
    }

    [Test]
    public async Task CurrentAndPinnedContextSummariesKeepTheSameCapturedFieldsAcrossEditsAndRecalculation()
    {
        await Accept(.814);
        await Pin();
        AssertContextDescriptions("Hand size 2 · 8 active copies · 2 active cards",
            "Hand size 2 · 8 active copies · 2 active cards"
        );

        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        await Accept(.842);
        AssertContextDescriptions("Hand size 3 · 8 active copies · 2 active cards",
            "Hand size 2 · 8 active copies · 2 active cards"
        );

        await Call("ReplaceCard", (0, Field<List<Card>>("cards")[0].WithCopies(3)));
        await Call("ReplaceCard", (1, Field<List<Card>>("cards")[1].WithActive(false)));
        // Pending edits do not leak into the last accepted result or the explicit pin.
        AssertContextDescriptions("Hand size 3 · 8 active copies · 2 active cards",
            "Hand size 2 · 8 active copies · 2 active cards"
        );
        await Accept(.786);
        AssertContextDescriptions("Hand size 3 · 3 active copies · 1 active cards",
            "Hand size 2 · 8 active copies · 2 active cards"
        );

        await Pin();
        AssertContextDescriptions("Hand size 3 · 3 active copies · 1 active cards",
            "Hand size 3 · 3 active copies · 1 active cards"
        );
        Assert.That(cut.FindAll(".result-difference-neutral"),
            Has.Count.EqualTo(5),
            "removing the context note does not alter the inline comparison rows"
        );
    }

    [TestCase("requirement")]
    [TestCase("mode")]
    [TestCase("multiplicity")]
    [TestCase("kind")]
    [TestCase("membership")]
    [TestCase("regroup")]
    public async Task StructuralChangesPreventRouteAndGroupDelta(string edit)
    {
        await Accept();
        await Pin();
        Combo route = Field<List<Combo>>("combos")[0];
        Combo replacement = edit switch
        {
            "requirement" => route.WithCards([new("a", 0, 0)]),
            "mode" => route.WithCards([new("a", 1, 5, RequirementMaximumMode.HandSize)]),
            "multiplicity" => route.WithCards([new("a", 1, 5), new("a", 1, 5)]),
            "kind" => route.WithCards([]).WithCategories([new(role, 1, 5)]),
            "regroup" => route.WithGroup(null),
            _ => route.WithCategories([new(role, 1, 5)]).WithCards([])
        };
        await Call("ReplaceCombo", (0, replacement));

        if (edit == "membership")
        {
            await Call("ReplaceCard", (1, Field<List<Card>>("cards")[1].WithCategories([role])));
        }

        await Accept(.842);
        PinnedResultSnapshot current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(current.CompareCombo(current.Combos[0], Pinned, true), Is.EqualTo("Definition changed"));
        Assert.That(current.CompareGroup(current.Groups[0], Pinned, true), Is.EqualTo("Composition changed"));
        Assert.That(cut.Find(".result-difference").TextContent, Is.EqualTo("+2.8%"));
        Assert.That(cut.FindAll(".result-row-comparison").Select(x => x.TextContent),
            Does.Contain("Definition changed").And.Contain("Composition changed")
        );
    }

    [Test]
    public async Task RemovedRecreatedAndInactiveRowsAreNotGuessedFromNamesOrDefinitions()
    {
        await Accept();
        await Pin();
        PinnedResultSnapshot baseline = Pinned;
        await Call("RemoveCombo", 0);
        await Call("AddNewCombo");
        await Call("ReplaceCombo", (3, new Combo([], "Duplicate", groupId: "g", cards: [new("a", 1, 5)])));
        await Call("ReplaceCombo", (0, Field<List<Combo>>("combos")[0].WithActive(false)));
        await Accept(.842);
        PinnedResultSnapshot current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(baseline.CompareCombo(baseline.Combos[0], current, false), Is.EqualTo("Removed or inactive"));
        Assert.That(baseline.CompareCombo(baseline.Combos[1], current, false), Is.EqualTo("Removed or inactive"));
        Assert.That(current.CompareCombo(current.Combos.Last(), baseline, true), Is.EqualTo("New route"));
        Assert.That(current.CompareCombo(current.Combos[0], baseline, true), Is.EqualTo("+2.8%"));
        Assert.That(cut.FindAll(".result-row-comparison").Any(x => x.TextContent == "New route"), Is.True);
        Assert.That(Pinned.Combos, Has.Length.EqualTo(3));
    }

    [Test]
    public async Task RecreatedGroupIdDoesNotEstablishGroupContinuity()
    {
        await Accept();
        await Pin();
        await Call("RemoveGroup", "g");
        Field<List<ComboGroup>>("comboGroups").Add(new("g", "Group"));
        await Call("ReplaceCombo", (0, Field<List<Combo>>("combos")[0].WithGroup("g")));
        await Accept(.842);
        PinnedResultSnapshot current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(current.CompareGroup(current.Groups[0], Pinned, true), Is.EqualTo("New group"));
    }

    [Test]
    public async Task CategoryMembershipChangesOnlyInvalidateTheAffectedEventAndKeepPinFrozen()
    {
        await Accept();
        await Pin();
        await Call("ReplaceCard", (1, Field<List<Card>>("cards")[1].WithCategories([role])));
        await Accept(.842);
        PinnedResultSnapshot current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(current.CompareCombo(current.Combos[1], Pinned, true), Is.EqualTo("Definition changed"));
        Assert.That(current.CompareCombo(current.Combos[0], Pinned, true), Is.EqualTo("+2.8%"));
        Assert.That(current.CompareGroup(current.Groups[0], Pinned, true), Is.EqualTo("+2.8%"));
        Assert.That(current.Context.DeckDefinition, Is.Not.EqualTo(Pinned.Context.DeckDefinition));
    }

    [Test]
    public async Task InactiveMemberChangesGroupCompositionWithoutInvalidatingOtherRouteContinuity()
    {
        await Accept();
        await Pin();
        await cut.Find("[aria-label='Active combo Duplicate']").ChangeAsync(new() { Value = false });
        await Accept(.786);
        PinnedResultSnapshot current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(current.CompareGroup(current.Groups[0], Pinned, true), Is.EqualTo("Composition changed"));
        Assert.That(Pinned.CompareCombo(Pinned.Combos[0], current, false), Is.EqualTo("Removed or inactive"));
        Assert.That(current.CompareCombo(current.Combos[0], Pinned, true), Is.EqualTo("-2.8%"));
    }

    [TestCase("file")]
    [TestCase("recovery")]
    [TestCase("accepted-example-path")]
    [TestCase("ydk")]
    public async Task AcceptedLoadsClearOnlyCurrentAndBreakRowContinuityEvenWithIdenticalIdsAndLabels(string load)
    {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        SessionState replacement = Workspace(3);

        if (load == "file")
        {
            await Upload(replacement);
        }
        else if (load == "ydk")
        {
            imports.Setup(x => x.ImportDeckFromYdkAsync(It.IsAny<IBrowserFile>())).ReturnsAsync(replacement.Cards);
            await cut.InvokeAsync(() => cut.FindComponents<InputFile>()[0]
                .Instance.OnChange.InvokeAsync(
                    new InputFileChangeEventArgs([new SessionFile("fixture")])
                )
            );
        }
        else if (load == "recovery")
        {
            await Call("ApplyRecoveryAsync", replacement);
        }
        else
        {
            object request =
                typeof(ProbabilityCalculatorComponent).GetMethod("BeginSessionLoad", Private)!.Invoke(cut.Instance,
                    null
                )!;
            await Call("RestoreSessionDataAsync", replacement, request);
        }

        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(cut.FindAll(".result-difference"), Is.Empty);
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        await Accept(.842);
        PinnedResultSnapshot current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(current.CompareCombo(current.Combos[0], pin, true), Is.EqualTo("Unrelated session"));
        Assert.That(current.CompareGroup(current.Groups[0], pin, true), Is.EqualTo("Unrelated session"));
        Assert.That(cut.Find(".result-difference").TextContent, Is.EqualTo("+2.8%"));
        AssertContextDescriptions(load == "ydk"
                ? "Hand size 2 · 8 active copies · 2 active cards"
                : "Hand size 3 · 8 active copies · 2 active cards",
            "Hand size 2 · 8 active copies · 2 active cards"
        );
    }

    [Test]
    public async Task CopyStillExportsOnlyTheAcceptedCurrentResultAndCapturedHandSize()
    {
        await Accept();
        await Pin();
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        await Accept(.842);
        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new());
        string text = (string)clipboard.Invocations["copyText"].Single().Arguments[0]!;
        Assert.That(text, Does.Contain(.842.ToString("P2")).And.Not.Contain(.814.ToString("P2")));
        Assert.That(text, Does.Contain("Hand size: 3").And.Not.Contain("Pinned").And.Not.Contain("\\n"));
        Assert.That(cut.FindAll(".probability-result-copy-success-icon"), Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StaleCopyOwnsHandSizeRowsAndGroupsWithoutChangingEditedSession(bool pin)
    {
        await cut.Find("#handSize").ChangeAsync(new() { Value = "5" });
        Task calculation = Start();
        ProbabilityCalculationResult original = Result(0, .814);
        worker.Jobs[0].SetResult(original);
        await calculation;

        if (pin)
        {
            await Pin();
        }

        // Even mutation of the worker's returned lists cannot alter accepted display/export rows.
        ((List<ComboProbabilityResult>)original.ComboProbabilities).Clear();
        ((List<GroupProbabilityResult>)original.GroupProbabilities!).Clear();
        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        await Call("MoveCombo", (0, 2));
        await Call("ReplaceCombo", (2, Field<List<Combo>>("combos")[2].WithName("Edited route").WithGroup(null)));
        await Call("RenameGroup", ("g", "Edited group"));
        await Call("RemoveCombo", 0);
        await Call("ReplaceCard", (0, Field<List<Card>>("cards")[0].WithCopies(3)));
        string session = sessions.SerializeSession(FieldSession());
        int writes = context.JSInterop.Invocations["sessionRecovery.update"].Count;
        IElement copy = cut.Find("button[title='Copy a summary of these results']");
        Assert.That(copy.HasAttribute("disabled"), Is.False);
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find("#pinUnavailableReason").ClassList, Does.Contain("visually-hidden"));
        await copy.ClickAsync(new());
        const string expected =
            "Previous result — current inputs have changed.\nProbability results\nHand size: 5\nAny active combo: 81.40%\n\nGroup probabilities:\n- **Group** — 81.40% (1 active)\n  - **Duplicate** — 81.40%\n\nUngrouped combos:\n- **Duplicate** — 81.40%\n- **Unnamed combo 3** — 81.40%";
        Assert.That(clipboard.Invocations["copyText"].Last().Arguments[0], Is.EqualTo(expected));
        Assert.That(sessions.SerializeSession(FieldSession()), Is.EqualTo(session));
        Assert.That(cut.FindComponent<SessionShareButton>().Instance.Snapshot, Is.EqualTo(session));
        Assert.That(context.JSInterop.Invocations["sessionRecovery.update"], Has.Count.EqualTo(writes));
        Task running = Start();
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.True);
        await cut.Find("[aria-label='Cancel calculation']").ClickAsync(new());
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.False);
        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new());
        Assert.That(clipboard.Invocations["copyText"].Last().Arguments[0], Is.EqualTo(expected));
        worker.Jobs[1].SetResult(Result(1, .1));
        await running;
        await Accept(.842);
        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new());
        string fresh = (string)clipboard.Invocations["copyText"].Last().Arguments[0]!;
        Assert.That(fresh, Does.StartWith("Probability results\nHand size: 6\nAny active combo: 84.20%"));
        Assert.That(fresh, Does.Contain("Edited route").And.Not.Contain("Previous result"));
    }

    [Test]
    public async Task SharedOfferDismissalAndGatedAcceptanceKeepPinAndFenceOldClipboardAndWorker()
    {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        NavigationManager navigation = context.Services.GetRequiredService<NavigationManager>();
        SessionState replacement = Workspace(6);
        replacement.Combos[0] = replacement.Combos[0].WithName("Shared route");
        string link = SessionShareCodec.CreateLink(navigation.BaseUri, sessions.SerializeSession(replacement));
        string before = sessions.SerializeSession(FieldSession());
        await cut.InvokeAsync(() => navigation.NavigateTo(link));
        await cut.Find("section[aria-label='Shared session'] .btn-secondary").ClickAsync(new());
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(sessions.SerializeSession(FieldSession()), Is.EqualTo(before));
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        JSRuntimeInvocationHandler<bool> delayed = clipboard.Setup<bool>("copyText", _ => true);
        Task copying = cut.Find("button[title='Copy a summary of these results']").ClickAsync(new());
        Task oldWork = Start();
        await cut.InvokeAsync(() => navigation.NavigateTo(link));
        IRenderedComponent<SessionRecovery> recovery = cut.FindComponent<SessionRecovery>();
        cut.WaitForState(() => recovery.Instance.InspectionComplete);
        await Call("LoadSharedSessionAsync");
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(
            typeof(ProbabilityCalculatorComponent).GetField("acceptedComparison", Private)!.GetValue(cut.Instance),
            Is.Null
        );
        worker.Jobs[1].SetResult(Result(1, .1));
        await oldWork;
        delayed.SetResult(false);
        await copying;
        Assert.That(cut.FindAll(".probability-result-copy-fallback"), Is.Empty);
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        await Accept(.842);
        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new());
        Assert.That(clipboard.Invocations["copyText"].Last().Arguments[0],
            Does.StartWith("Probability results\nHand size: 6\nAny active combo: 84.20%")
        );
        string shared = cut.FindComponent<SessionShareButton>().Instance.Snapshot;
        Assert.That(shared, Is.EqualTo(sessions.SerializeSession(FieldSession())));
        Assert.That(shared,
            Does
                .Not.Contain("acceptedComparison")
                .And.Not.Contain("pinnedResult")
                .And.Not.Contain("Previous result")
                .And.Not.Contain("84.20%")
        );
        Assert.That(cut.FindAll(".result-row-comparison").Select(x => x.TextContent),
            Does.Contain("Unrelated session")
        );
    }

    [TestCase("edit")]
    [TestCase("dismiss")]
    [TestCase("failure")]
    public async Task ObsoleteOrFailedSharedLoadRetainsPinAndAcceptedExportContext(string action)
    {
        await Accept();
        await Pin();
        PinnedResultSnapshot pin = Pinned;
        PinnedResultSnapshot accepted = Field<PinnedResultSnapshot>("acceptedComparison");
        NavigationManager navigation = context.Services.GetRequiredService<NavigationManager>();
        SessionState replacement = Workspace(6);
        replacement.Combos[0] = replacement.Combos[0].WithName("Obsolete shared route");
        await cut.InvokeAsync(() =>
            navigation.NavigateTo(SessionShareCodec.CreateLink(navigation.BaseUri,
                    sessions.SerializeSession(replacement)
                )
            )
        );
        cut.WaitForState(() => cut.FindComponent<SessionRecovery>().Instance.InspectionComplete);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock
            .Get(context.Services.GetRequiredService<ILegacyCardMetadataEnricher>())
            .Setup(x => x.EnrichAsync(It.IsAny<SessionState>()))
            .Returns(() =>
                {
                    started.SetResult();

                    return gate.Task;
                }
            );
        Task loading = Call("LoadSharedSessionAsync");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        if (action == "edit")
        {
            await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        }
        else if (action == "dismiss")
        {
            await Call("DismissSharedSession");
        }
        // Enrichment failures are recoverable and intentionally do not block a valid load.
        // A failed session decode exercises the actual failure path before acceptance.
        else
        {
            await cut.InvokeAsync(() => navigation.NavigateTo(navigation.BaseUri + "#ygo-session=v1.%ZZ"));
        }

        gate.SetResult();
        await loading;
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(Field<PinnedResultSnapshot>("acceptedComparison"), Is.SameAs(accepted));
        Assert.That(cut.FindAll(".probability-results"), Has.Count.EqualTo(1));
        Assert.That(sessions.SerializeSession(FieldSession()), Does.Not.Contain("Obsolete shared route"));
        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new());
        Assert.That(clipboard.Invocations["copyText"].Last().Arguments[0],
            Does.Contain("Hand size: 2").And.Not.Contain("Obsolete shared route")
        );
    }

    [Test]
    public async Task MissingAcceptedContextFailsClosedRatherThanExportingLiveHandSize()
    {
        await Accept();
        typeof(ProbabilityCalculatorComponent).GetField("acceptedComparison", Private)!.SetValue(cut.Instance, null);
        await cut.Find("#handSize").ChangeAsync(new() { Value = "6" });
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.True);
        Assert.That(clipboard.Invocations["copyText"], Is.Empty);
    }

    private sealed class SessionFile(string json) : IBrowserFile
    {
        public string Name => "fixture.json";
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => Encoding.UTF8.GetByteCount(json);
        public string ContentType => "application/json";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private sealed class ControlledCalculator : IBackgroundCalculator
    {
        public List<TaskCompletionSource<ProbabilityCalculationResult>> Jobs { get; } = [];
        public List<CalculationSnapshot> Inputs { get; } = [];

        public Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken token)
        {
            TaskCompletionSource<ProbabilityCalculationResult> source =
                new(TaskCreationOptions
                    .RunContinuationsAsynchronously
                );
            Inputs.Add(snapshot);
            Jobs.Add(source);

            return source.Task;
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = previous;
    }
}
