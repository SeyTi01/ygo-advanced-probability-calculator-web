using System.Globalization;
using System.Reflection;
using System.Text;
using Bunit;
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
public class PinnedResultTest {
    private TestContext context = null!;
    private ControlledCalculator worker = null!;
    private IRenderedComponent<ProbabilityCalculatorComponent> cut = null!;
    private ISessionService sessions = null!;
    private BunitJSModuleInterop clipboard = null!;
    private Mock<IDeckImportService> imports = null!;
    private static readonly CategoryBase role = new("Role");
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp] public void Setup() {
        context = new(); context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<ISerializer, JsonSerializer>();
        context.Services.AddSingleton<ISessionService, SessionService>();
        context.Services.AddSingleton(Mock.Of<ILegacyCardMetadataEnricher>());
        context.Services.AddSingleton(Mock.Of<ICardArtworkService>());
        imports = new(); context.Services.AddSingleton(imports.Object);
        worker = new(); context.Services.AddSingleton<IBackgroundCalculator>(worker);
        context.Services.AddSingleton<IPendingSessionService>(new PendingSessionService { PendingSession = Workspace() });
        clipboard = context.JSInterop.SetupModule("./js/probabilityResultExport.js");
        clipboard.Mode = JSRuntimeMode.Loose; clipboard.Setup<bool>("copyText", _ => true).SetResult(true);
        sessions = context.Services.GetRequiredService<ISessionService>();
        cut = context.RenderComponent<ProbabilityCalculatorComponent>();
    }
    [TearDown] public void Cleanup() { cut.Dispose(); context.Dispose(); }
    private static SessionState Workspace(int handSize = 2) => new() {
        Cards = [new([role], 4, "A", id: "a"), new([], 4, "B", id: "b")],
        Categories = [role], HandSize = handSize,
        ComboGroups = [new("g", "Group")],
        Combos = [new([], "Duplicate", groupId: "g", cards: [new("a", 1, 5)]),
            new([new(role, 1, 5)], "Duplicate"), new([], null, cards: [new("b", 1, 5)]),
            new([], "Inactive", false, cards: [new("b", 1, 5)])]
    };
    private T Field<T>(string name) => (T)typeof(ProbabilityCalculatorComponent).GetField(name, Private)!.GetValue(cut.Instance)!;
    private Task Call(string name, params object[] args) => cut.InvokeAsync(async () => {
        var result = typeof(ProbabilityCalculatorComponent).GetMethod(name, Private)!.Invoke(cut.Instance, args);
        if (result is Task task) await task;
        typeof(Microsoft.AspNetCore.Components.ComponentBase).GetMethod("StateHasChanged", Private)!.Invoke(cut.Instance, null);
    });
    private Task Start() => cut.Find(".calculate-action > button").ClickAsync(new());
    private Task Pin() => cut.Find(".pin-result-action").ClickAsync(new());
    private Task Clear() => cut.Find(".pinned-result button").ClickAsync(new());
    private PinnedResultSnapshot Pinned => cut.FindComponent<PinnedResultPanel>().Instance.Snapshot;
    private ProbabilityCalculationResult Result(int job, double total) {
        var input = System.Text.Json.JsonSerializer.Deserialize<CalculationInput>(worker.Inputs[job].Json)!;
        return new(total, input.Combos.Select((c, i) => new ComboProbabilityResult(i, c.Name, total, c.GroupId)).ToList(),
            input.Groups.Select(g => new GroupProbabilityResult(g.Id, g.Name, total, input.Combos.Count(c => c.GroupId == g.Id))).ToList());
    }
    private async Task Accept(double probability = .814) {
        var action = Start(); var job = worker.Jobs.Count - 1;
        worker.Jobs[job].SetResult(Result(job, probability)); await action;
    }
    private Task Upload(SessionState session) => cut.InvokeAsync(() => cut.FindComponents<InputFile>()[1].Instance.OnChange.InvokeAsync(
        new InputFileChangeEventArgs([new SessionFile(sessions.SerializeSession(session))])));

    [Test] public async Task PinReplaceClearAreExplicitAndDoNotChangeSessionAutosaveOrCalculationVersion() {
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        Assert.That(cut.FindAll(".pinned-result"), Is.Empty);
        await Accept();
        Assert.That(cut.FindAll(".pinned-result"), Is.Empty);
        var before = sessions.SerializeSession(FieldSession());
        var version = Field<long>("calculationVersion");
        var writes = context.JSInterop.Invocations["sessionRecovery.update"].Count;
        await Pin(); var first = Pinned;
        await Accept(.842);
        Assert.That(Pinned, Is.SameAs(first));
        Assert.That(cut.Find(".result-difference").TextContent, Is.EqualTo("+2.8 pp"));
        await Pin();
        Assert.That(Pinned.Total, Is.EqualTo(.842));
        Assert.That(cut.Find(".result-difference").TextContent, Is.EqualTo("0 pp"));
        await Clear();
        Assert.That(cut.FindAll(".pinned-result"), Is.Empty);
        Assert.That(cut.FindAll(".result-difference"), Is.Empty);
        Assert.That(sessions.SerializeSession(FieldSession()), Is.EqualTo(before));
        Assert.That(Field<long>("calculationVersion"), Is.EqualTo(version));
        Assert.That(context.JSInterop.Invocations["sessionRecovery.update"], Has.Count.EqualTo(writes));
    }
    private SessionState FieldSession() => (SessionState)typeof(ProbabilityCalculatorComponent).GetMethod("CaptureSession", Private)!.Invoke(cut.Instance, null)!;

    [Test] public async Task PinOwnsRequestContextAndCopiesMutableResultsAndNestedDefinitions() {
        var action = Start();
        var result = Result(0, .814);
        // An unannounced edit specifically verifies capture timing, independent of invalidation.
        Field<List<Card>>("cards")[0].Categories.Clear();
        Field<List<Combo>>("combos")[0].Cards.Clear();
        typeof(ProbabilityCalculatorComponent).GetField("handSize", Private)!.SetValue(cut.Instance, 3);
        worker.Jobs[0].SetResult(result); await action; await Pin();
        var frozen = Pinned;
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

    [TestCase(.842, "+2.8 pp")] [TestCase(.786, "-2.8 pp")] [TestCase(.814, "0 pp")]
    [TestCase(.813999999, "0 pp")] [TestCase(.81405, "+0.01 pp")]
    public async Task AcceptedDeltasUsePercentagePointsWithoutNegativeZero(double probability, string expected) {
        await Accept(); await Pin(); await Accept(probability);
        Assert.That(cut.Find(".result-difference").TextContent, Is.EqualTo(expected));
    }
    [Test] public void DifferenceUsesCultureAndRejectsNonfiniteProbabilities() {
        using var culture = new CultureScope("de-DE");
        Assert.That(PinnedResultSnapshot.Difference(.842, .814), Is.EqualTo("+2,8 pp"));
        Assert.That(PinnedResultSnapshot.Difference(double.NaN, .814), Is.EqualTo("Not comparable"));
        Assert.That(PinnedResultSnapshot.Difference(double.PositiveInfinity, .814), Is.EqualTo("Not comparable"));
    }
    [Test] public async Task InvalidResultCannotBePinnedOrPresentedAsAnImprovement() {
        await Accept(); await Pin(); await Accept(double.NaN);
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        Assert.That(cut.FindAll(".result-difference"), Is.Empty);
        Assert.That(Pinned.Total, Is.EqualTo(.814));
    }

    [TestCase("late-success")] [TestCase("late-error")] [TestCase("cancelled")] [TestCase("failure")]
    public async Task BusyStaleCancelledFailedAndLateResultsCannotReplacePinOrAcceptedComparison(string completion) {
        await Accept(); await Pin(); var baseline = Pinned;
        await cut.Find("#handSize").ChangeAsync(new() { Value = "3" });
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        Assert.That(cut.FindAll(".result-difference"), Is.Empty);
        var action = Start();
        await Call("PinCurrentResult");
        Assert.That(Pinned, Is.SameAs(baseline), "the event handler also rejects busy/stale pinning");
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        Assert.That(cut.FindAll(".result-difference"), Is.Empty);
        if (completion.StartsWith("late")) {
            await cut.Find("[aria-label='Cancel calculation']").ClickAsync(new());
            await Accept(.842);
            if (completion == "late-success") worker.Jobs[1].SetResult(Result(1, .1));
            else worker.Jobs[1].SetException(new Exception("Obsolete error"));
        }
        else if (completion == "cancelled") worker.Jobs[1].SetCanceled();
        else worker.Jobs[1].SetException(new Exception("Current failure"));
        await action;
        Assert.That(Pinned, Is.SameAs(baseline));
        if (completion.StartsWith("late")) {
            Assert.That(cut.Find(".result-difference").TextContent, Is.EqualTo("+2.8 pp"));
            Assert.That(cut.Markup, Does.Not.Contain("Obsolete error"));
        }
        else {
            Assert.That(cut.FindAll(".result-difference"), Is.Empty);
            Assert.That(cut.Find(".probability-result-status").TextContent, Does.Contain("Previous result"));
        }
    }

    [Test] public async Task ReorderRenameAndCountChangesPreserveDistinctLineageForDuplicateAndUnnamedRows() {
        await Accept(); await Pin();
        var original = Pinned;
        await Call("MoveCombo", (0, 2));
        await Call("ReplaceCombo", (2, Field<List<Combo>>("combos")[2].WithName("Renamed")));
        await Call("RenameGroup", ("g", "Renamed group"));
        await Call("ReplaceCard", (0, Field<List<Card>>("cards")[0].WithCopies(3)));
        await Accept(.842);
        var current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(current.Combos[2].Definition!.Lineage, Is.EqualTo(original.Combos[0].Definition!.Lineage));
        Assert.That(current.CompareCombo(current.Combos[2], original, true), Is.EqualTo("+2.8 pp"));
        Assert.That(current.CompareCombo(current.Combos[0], original, true), Is.EqualTo("+2.8 pp"));
        Assert.That(current.CompareCombo(current.Combos[1], original, true), Is.EqualTo("+2.8 pp"));
        Assert.That(current.CompareGroup(current.Groups[0], original, true), Is.EqualTo("+2.8 pp"));
        Assert.That(cut.Find(".probability-results").TextContent, Does.Contain("Different deck composition"));
        Assert.That(Pinned.Combos.Select(c => c.Label), Is.EqualTo(new[] { "Duplicate", "Duplicate", "Unnamed combo 3" }));
    }
    [TestCase("requirement")] [TestCase("mode")] [TestCase("multiplicity")] [TestCase("kind")] [TestCase("membership")] [TestCase("regroup")]
    public async Task StructuralChangesPreventRouteAndGroupDelta(string edit) {
        await Accept(); await Pin();
        var route = Field<List<Combo>>("combos")[0];
        var replacement = edit switch {
            "requirement" => route.WithCards([new("a", 0, 0)]),
            "mode" => route.WithCards([new("a", 1, 5, RequirementMaximumMode.HandSize)]),
            "multiplicity" => route.WithCards([new("a", 1, 5), new("a", 1, 5)]),
            "kind" => route.WithCards([]).WithCategories([new(role, 1, 5)]),
            "regroup" => route.WithGroup(null),
            _ => route.WithCategories([new(role, 1, 5)]).WithCards([])
        };
        await Call("ReplaceCombo", (0, replacement));
        if (edit == "membership") await Call("ReplaceCard", (1, Field<List<Card>>("cards")[1].WithCategories([role])));
        await Accept(.842);
        var current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(current.CompareCombo(current.Combos[0], Pinned, true), Is.EqualTo("Definition changed"));
        Assert.That(current.CompareGroup(current.Groups[0], Pinned, true), Is.EqualTo("Composition changed"));
        Assert.That(cut.Find(".result-difference").TextContent, Is.EqualTo("+2.8 pp"));
    }
    [Test] public async Task RemovedRecreatedAndInactiveRowsAreNotGuessedFromNamesOrDefinitions() {
        await Accept(); await Pin(); var baseline = Pinned;
        await Call("RemoveCombo", 0);
        await Call("AddNewCombo");
        await Call("ReplaceCombo", (3, new Combo([], "Duplicate", groupId: "g", cards: [new("a", 1, 5)])));
        await Call("ReplaceCombo", (0, Field<List<Combo>>("combos")[0].WithActive(false)));
        await Accept(.842);
        var current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(baseline.CompareCombo(baseline.Combos[0], current, false), Is.EqualTo("Removed or inactive"));
        Assert.That(baseline.CompareCombo(baseline.Combos[1], current, false), Is.EqualTo("Removed or inactive"));
        Assert.That(current.CompareCombo(current.Combos.Last(), baseline, true), Is.EqualTo("New route"));
        Assert.That(current.CompareCombo(current.Combos[0], baseline, true), Is.EqualTo("+2.8 pp"));
        Assert.That(Pinned.Combos, Has.Length.EqualTo(3));
    }
    [Test] public async Task RecreatedGroupIdDoesNotEstablishGroupContinuity() {
        await Accept(); await Pin();
        await Call("RemoveGroup", "g");
        Field<List<ComboGroup>>("comboGroups").Add(new("g", "Group"));
        await Call("ReplaceCombo", (0, Field<List<Combo>>("combos")[0].WithGroup("g")));
        await Accept(.842);
        var current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(current.CompareGroup(current.Groups[0], Pinned, true), Is.EqualTo("New group"));
    }

    [Test] public async Task CategoryMembershipChangesOnlyInvalidateTheAffectedEventAndKeepPinFrozen() {
        await Accept(); await Pin();
        await Call("ReplaceCard", (1, Field<List<Card>>("cards")[1].WithCategories([role])));
        await Accept(.842);
        var current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(current.CompareCombo(current.Combos[1], Pinned, true), Is.EqualTo("Definition changed"));
        Assert.That(current.CompareCombo(current.Combos[0], Pinned, true), Is.EqualTo("+2.8 pp"));
        Assert.That(current.CompareGroup(current.Groups[0], Pinned, true), Is.EqualTo("+2.8 pp"));
        Assert.That(current.Context.DeckDefinition, Is.Not.EqualTo(Pinned.Context.DeckDefinition));
    }
    [Test] public async Task InactiveMemberChangesGroupCompositionWithoutInvalidatingOtherRouteContinuity() {
        await Accept(); await Pin();
        await cut.Find("[aria-label='Active combo Duplicate']").ChangeAsync(new() { Value = false });
        await Accept(.786);
        var current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(current.CompareGroup(current.Groups[0], Pinned, true), Is.EqualTo("Composition changed"));
        Assert.That(Pinned.CompareCombo(Pinned.Combos[0], current, false), Is.EqualTo("Removed or inactive"));
        Assert.That(current.CompareCombo(current.Combos[0], Pinned, true), Is.EqualTo("-2.8 pp"));
    }

    [TestCase("file")] [TestCase("recovery")] [TestCase("accepted-example-path")] [TestCase("ydk")]
    public async Task AcceptedLoadsClearOnlyCurrentAndBreakRowContinuityEvenWithIdenticalIdsAndLabels(string load) {
        await Accept(); await Pin(); var pin = Pinned;
        var replacement = Workspace(3);
        if (load == "file") await Upload(replacement);
        else if (load == "ydk") {
            imports.Setup(x => x.ImportDeckFromYdkAsync(It.IsAny<IBrowserFile>())).ReturnsAsync(replacement.Cards);
            await cut.InvokeAsync(() => cut.FindComponents<InputFile>()[0].Instance.OnChange.InvokeAsync(
                new InputFileChangeEventArgs([new SessionFile("fixture")])));
        }
        else if (load == "recovery") await Call("ApplyRecoveryAsync", replacement);
        else await Call("RestoreSessionDataAsync", replacement, Field<long>("sessionLoadVersion"), sessions.SerializeSession(FieldSession()), Field<long>("workspaceEditVersion"));
        Assert.That(Pinned, Is.SameAs(pin));
        Assert.That(cut.FindAll(".probability-results"), Is.Empty);
        Assert.That(cut.FindAll(".result-difference"), Is.Empty);
        Assert.That(cut.Find(".pin-result-action").HasAttribute("disabled"), Is.True);
        await Accept(.842);
        var current = Field<PinnedResultSnapshot>("acceptedComparison");
        Assert.That(current.CompareCombo(current.Combos[0], pin, true), Is.EqualTo("Unrelated session"));
        Assert.That(current.CompareGroup(current.Groups[0], pin, true), Is.EqualTo("Unrelated session"));
        Assert.That(cut.Find(".result-difference").TextContent, Is.EqualTo("+2.8 pp"));
        if (load != "ydk") Assert.That(cut.Find(".probability-results").TextContent, Does.Contain("Different hand size"));
    }
    [Test] public async Task CopyStillExportsOnlyTheAcceptedCurrentResultAndCapturedHandSize() {
        await Accept(); await Pin(); await cut.Find("#handSize").ChangeAsync(new() { Value = "3" }); await Accept(.842);
        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new());
        var text = (string)clipboard.Invocations["copyText"].Single().Arguments[0]!;
        Assert.That(text, Does.Contain(.842.ToString("P2")).And.Not.Contain(.814.ToString("P2")));
        Assert.That(text, Does.Contain("Hand size: 3").And.Not.Contain("Pinned").And.Not.Contain("\\n"));
        Assert.That(cut.FindAll(".probability-result-copy-success-icon"), Has.Count.EqualTo(1));
    }
    private sealed class SessionFile(string json) : IBrowserFile {
        public string Name => "fixture.json";
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => Encoding.UTF8.GetByteCount(json);
        public string ContentType => "application/json";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => new MemoryStream(Encoding.UTF8.GetBytes(json));
    }
    private sealed class ControlledCalculator : IBackgroundCalculator {
        public List<TaskCompletionSource<ProbabilityCalculationResult>> Jobs { get; } = [];
        public List<CalculationSnapshot> Inputs { get; } = [];
        public Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken token) {
            var source = new TaskCompletionSource<ProbabilityCalculationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Inputs.Add(snapshot); Jobs.Add(source); return source.Task;
        }
    }
    private sealed class CultureScope : IDisposable {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;
        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() => CultureInfo.CurrentCulture = previous;
    }
}
