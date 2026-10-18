using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Web;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class ProbabilityResultLayoutTest {
    private static readonly CategoryBase requirement = new("Required piece");

    private TestContext _context = null!;
    private ProbabilityCalculationResult _resultToReturn = null!;

    [SetUp]
    public void SetUp() {
        _context = new TestContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<IBackgroundCalculator, BackgroundCalculatorTestAdapter>();

        Mock<IProbabilityCalculatorService> calculator = new();
        _resultToReturn = new ProbabilityCalculationResult(0, []);
        calculator.Setup(service => service.CalculateProbabilityResults(
                It.IsAny<List<Card>>(),
                It.IsAny<List<Combo>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyList<ComboGroup>?>()))
            .Returns(() => _resultToReturn);
        _context.Services.AddSingleton(calculator.Object);

        _context.Services.AddSingleton<ISerializer, JsonSerializer>();
        _context.Services.AddSingleton<ISessionService, SessionService>();
        _context.Services.AddSingleton<IPendingSessionService, PendingSessionService>();
        _context.Services.AddSingleton<IDeckImportService>(Mock.Of<IDeckImportService>());
        Mock<ICardInfoService> cardInfo = new();
        cardInfo.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, CardInfo>(StringComparer.Ordinal));
        _context.Services.AddSingleton(cardInfo.Object);
        _context.Services.AddSingleton<ILegacyCardMetadataEnricher, LegacyCardMetadataEnricher>();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task GroupsNestTheirMembersAndLeaveUngroupedAndOrphanResultsAfterThem() {
        _resultToReturn = new ProbabilityCalculationResult(
            0.8361,
            [
                new ComboProbabilityResult(0, "Shared route", 0.4, "group-first"),
                new ComboProbabilityResult(1, "Loose route", 0.25),
                new ComboProbabilityResult(2, "Second route", 0.382, "group-second"),
                new ComboProbabilityResult(3, "Shared route", 0.51, "group-first"),
                new ComboProbabilityResult(4, null, 0.08, "missing-group")
            ],
            [
                new GroupProbabilityResult("group-second", "Repeated name", 0.382, 1),
                new GroupProbabilityResult("group-first", "Repeated name", 0.7234, 2),
                new GroupProbabilityResult("empty-group", "Empty group", 0, 0)
            ]);

        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        await Calculate(cut);

        IElement results = cut.Find(".probability-results");
        Assert.That(results.QuerySelectorAll(".probability-total-value"), Has.Length.EqualTo(1));
        Assert.That(results.QuerySelector(".probability-total-value")!.TextContent,
            Is.EqualTo(0.8361.ToString("P2")));

        IHtmlCollection<IElement> groups = results.QuerySelectorAll(".probability-group-container");
        Assert.That(groups, Has.Length.EqualTo(2));
        Assert.That(groups.Select(GroupName), Is.EqualTo(new[] { "Repeated name", "Repeated name" }));
        Assert.That(groups.Select(GroupProbability), Is.EqualTo(new[] {
            0.382.ToString("P2"), 0.7234.ToString("P2")
        }));

        IHtmlCollection<IElement> secondGroupMembers = groups[0].QuerySelectorAll(".probability-group-members .combo-probability-item");
        Assert.That(ComboNames(secondGroupMembers), Is.EqualTo(new[] { "Second route" }));
        IHtmlCollection<IElement> firstGroupMembers = groups[1].QuerySelectorAll(".probability-group-members .combo-probability-item");
        Assert.That(ComboNames(firstGroupMembers), Is.EqualTo(new[] { "Shared route", "Shared route" }));
        Assert.That(ComboProbabilities(firstGroupMembers), Is.EqualTo(new[] { 0.4.ToString("P2"), 0.51.ToString("P2") }));
        Assert.That(results.TextContent, Does.Not.Contain("Empty group").And.Not.Contain("(0 active)"));

        Assert.That(results.QuerySelectorAll(".probability-ungrouped-combos .combo-probability-item"), Has.Length.EqualTo(2));
        Assert.That(results.QuerySelector("h6:not(.probability-group-heading)")!.TextContent.Trim(), Is.EqualTo("Ungrouped combos"));
        Assert.That(ComboNames(results.QuerySelectorAll(".probability-ungrouped-combos .combo-probability-item")),
            Is.EqualTo(new[] { "Loose route", "Unnamed combo 5" }));
        Assert.That(results.QuerySelectorAll(".combo-probability-item"), Has.Length.EqualTo(5),
            "grouped rows appear only beneath their group; ungrouped and orphan rows appear once after groups");
        Assert.That(results.QuerySelectorAll(".probability-ungrouped-combos .combo-probability-item")
            .Select(row => row.QuerySelector(".combo-probability-value")!.TextContent),
            Is.EqualTo(new[] { 0.25.ToString("P2"), 0.08.ToString("P2") }));
    }

    [Test]
    public async Task NoGroupsRenderOneIndividualListWithDuplicateAndUnnamedCombos() {
        _resultToReturn = new ProbabilityCalculationResult(
            0.65,
            [
                new ComboProbabilityResult(0, "Duplicate", 0.4),
                new ComboProbabilityResult(1, "Duplicate", 0.3),
                new ComboProbabilityResult(2, null, 0.1)
            ]);

        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        await Calculate(cut);

        IElement results = cut.Find(".probability-results");
        Assert.That(results.QuerySelectorAll(".probability-group-container"), Is.Empty);
        Assert.That(results.QuerySelector("h6:not(.probability-group-heading)")!.TextContent.Trim(), Is.EqualTo("Individual combos"));
        IHtmlCollection<IElement> rows = results.QuerySelectorAll(".probability-ungrouped-combos .combo-probability-item");
        Assert.That(ComboNames(rows), Is.EqualTo(new[] { "Duplicate", "Duplicate", "Unnamed combo 3" }));
        Assert.That(ComboProbabilities(rows), Is.EqualTo(new[] {
            0.4.ToString("P2"), 0.3.ToString("P2"), 0.1.ToString("P2")
        }));
        Assert.That(results.QuerySelectorAll(".probability-total-value"), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task StaleResultsKeepTheirCalculatedGroupSnapshotUntilRecalculation() {
        ComboGroup[] groups = [new("group-a", "Alpha"), new("group-b", "Beta")];
        SessionState session = Session(groups, "group-a", "group-b");
        _resultToReturn = new ProbabilityCalculationResult(
            0.9,
            [new ComboProbabilityResult(0, "Move me", 0.7, "group-a"), new ComboProbabilityResult(1, "Stay", 0.4, "group-b")],
            [new GroupProbabilityResult("group-a", "Alpha", 0.7, 1), new GroupProbabilityResult("group-b", "Beta", 0.4, 1)]);

        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await Calculate(cut);
        Assert.That(GroupNames(cut), Is.EqualTo(new[] { "Alpha", "Beta" }));

        await cut.Find("#comboGroup0").ChangeAsync(new() { Value = "group-b" });
        await cut.Find("[aria-label='Edit group Alpha']").ClickAsync(new());
        IElement groupName = cut.Find("[aria-label='New name for group Alpha']");
        await groupName.InputAsync(new() { Value = "Alpha renamed" });
        await groupName.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.That(cut.Find(".probability-result-status .visually-hidden").TextContent.Trim(),
            Is.EqualTo("Previous result · inputs changed"));
        Assert.That(cut.Find("[aria-label='Group for Editor combo 1']").GetAttribute("value"), Is.EqualTo("group-b"));
        IElement[] staleGroups = ResultGroups(cut);
        Assert.That(staleGroups.Select(GroupName), Is.EqualTo(new[] { "Alpha", "Beta" }));
        Assert.That(ComboNames(staleGroups[0].QuerySelectorAll(".probability-group-members .combo-probability-item")),
            Is.EqualTo(new[] { "Move me" }));
        Assert.That(ComboNames(staleGroups[1].QuerySelectorAll(".probability-group-members .combo-probability-item")),
            Is.EqualTo(new[] { "Stay" }));

        _resultToReturn = new ProbabilityCalculationResult(
            0.95,
            [new ComboProbabilityResult(0, "Move me", 0.7, "group-b"), new ComboProbabilityResult(1, "Stay", 0.4, "group-b")],
            [new GroupProbabilityResult("group-a", "Alpha renamed", 0, 0), new GroupProbabilityResult("group-b", "Beta", 0.8, 2)]);
        await Calculate(cut);

        Assert.That(cut.FindAll(".probability-result-status"), Is.Empty);
        IElement[] recalculatedGroups = ResultGroups(cut);
        Assert.That(recalculatedGroups.Select(GroupName), Is.EqualTo(new[] { "Beta" }));
        Assert.That(ComboNames(recalculatedGroups[0].QuerySelectorAll(".probability-group-members .combo-probability-item")),
            Is.EqualTo(new[] { "Move me", "Stay" }));
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(0.95.ToString("P2")));
    }

    private IRenderedComponent<ProbabilityCalculatorComponent> Render(SessionState? session = null) {
        _context.Services.GetRequiredService<IPendingSessionService>().PendingSession = session ?? Session();
        return _context.RenderComponent<ProbabilityCalculatorComponent>();
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [SetCulture("en-US")]
    public async Task EmptyGroupsAreDisplayOnlyAndOriginalBoundIndicesSurviveInCurrentPinAndCopy(int emptyIndex) {
        List<GroupProbabilityResult> groups = [new("zero", "Active zero", 0, 1), new("bounded", "Bounded group", .9, 1)];
        groups.Insert(emptyIndex, new("empty", "Hidden empty", 0, 0));
        SearchAvailabilityInterval zero = new("Zero", new("0", "1"), new("0", "1"));
        SearchAvailabilityInterval bound = new("Bound", new("1", "4"), new("3", "4"));
        SearchAvailabilityCertificate certificate = new(SearchAvailabilityStatus.Bounded, "Display proof", bound,
            [zero, bound], [.. groups.Select(group => group.GroupId == "bounded" ? bound : zero)]);
        CalculationResultContract contract = ProbabilityPresentationTest.Contract(CalculationAccuracy.Bounded);
        _resultToReturn = new(.9, [new(0, "Zero route", 0, "zero"), new(1, "Bounded route", .9, "bounded")], groups,
            SearchAvailability: certificate, Contract: contract);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        await Calculate(cut);
        await cut.Find(".pin-result-action").ClickAsync(new());
        IElement[] current = ResultGroups(cut);
        IElement[] pinned = cut.FindAll(".pinned-group").ToArray();
        Assert.That(current, Has.Length.EqualTo(2));
        Assert.That(pinned, Has.Length.EqualTo(2));
        foreach (IElement group in new[] { current[0], pinned[0] }) {
            Assert.That(group.TextContent, Does.Contain("Active zero").And.Contain("Zero route").And.Contain("0.00").And.Contain("±0%"));
        }
        foreach (IElement group in new[] { current[1], pinned[1] }) {
            Assert.That(group.TextContent, Does.Contain("Bounded group").And.Contain("Bounded route").And.Contain("±25.00%"));
            Assert.That(group.QuerySelector("[role='note']")!.GetAttribute("aria-label"), Does.Contain("[1/4, 3/4]"));
        }
        Assert.That(cut.Find(".probability-results").TextContent, Does.Not.Contain("Hidden empty").And.Not.Contain("Ungrouped combos"));
        Assert.That(cut.Find(".pinned-result").TextContent, Does.Not.Contain("Hidden empty").And.Not.Contain("Ungrouped combos"));
        Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot.Groups, Has.Length.EqualTo(3));
        Assert.That(cut.FindComponent<PinnedResultPanel>().Instance.Snapshot.SearchAvailability, Is.SameAs(certificate));
        Assert.That(cut.FindComponent<ProbabilityResultExport>().Instance.Result!.GroupProbabilities, Has.Count.EqualTo(3));
        string text = ProbabilityResultSummaryFormatter.Format(_resultToReturn, 1, System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(text, Does.Not.Contain("Hidden empty").And.Not.Contain("Ungrouped combos").And.Contain("**Active zero** — 0.00% ±0%").And.Contain("**Bounded group** — 50.00% ±25.00%"));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task EmptyOnlyOrNoGroupsDoNotCreatePhantomSections(bool emptyGroup, bool hasCombo) {
        _resultToReturn = new(.5,
            hasCombo ? [new(0, "Loose route", .5)] : [],
            emptyGroup ? [new("empty", "Hidden empty", 0, 0)] : []);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render();
        await Calculate(cut);
        await cut.Find(".pin-result-action").ClickAsync(new());
        foreach (IElement panel in new[] { cut.Find(".probability-results"), cut.Find(".pinned-result") }) {
            Assert.That(panel.TextContent, Does.Not.Contain("Hidden empty").And.Not.Contain("Ungrouped combos"));
            Assert.That(panel.QuerySelectorAll("h6"), Has.Length.EqualTo(hasCombo ? 1 : 0));
            Assert.That(panel.QuerySelectorAll("ol"), Has.Length.EqualTo(hasCombo ? 1 : 0));
            if (hasCombo) {
                Assert.That(panel.TextContent, Does.Contain("Individual combos").And.Contain("Loose route"));
            }
        }
        string copied = ProbabilityResultSummaryFormatter.Format(_resultToReturn, 1, System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(copied, Does.Not.Contain("Group probabilities").And.Not.Contain("Ungrouped combos").And.Not.Contain("Hidden empty"));
        Assert.That(copied.Contains("Individual combos:"), Is.EqualTo(hasCombo));
    }

    private static async Task Calculate(IRenderedComponent<ProbabilityCalculatorComponent> cut) =>
        await cut.FindAll("button").Single(button => button.TextContent.Trim() == "Calculate").ClickAsync(new());

    private static SessionState Session(IReadOnlyList<ComboGroup>? groups = null, params string?[] assignments) => new() {
        Cards = [new([requirement], 3, "Required piece")],
        Combos = assignments.Length == 0
            ? [new([new(requirement, 1, 1)], "Valid combo")]
            : assignments.Select((groupId, index) => new Combo([new(requirement, 1, 1)], $"Editor combo {index + 1}", groupId: groupId)).ToList(),
        ComboGroups = groups?.ToList() ?? [],
        HandSize = 1
    };

    private static string GroupName(IElement group) {
        string text = group.QuerySelector(".probability-group-heading .combo-probability-name")!.TextContent.Trim();
        int activeCount = text.LastIndexOf(" (", StringComparison.Ordinal);
        return activeCount < 0 ? text : text[..activeCount];
    }

    private static string GroupProbability(IElement group) =>
        group.QuerySelector(".probability-group-heading .combo-probability-value")!.TextContent;

    private static IElement[] ResultGroups(IRenderedFragment fragment) =>
        fragment.FindAll(".probability-group-container").ToArray();

    private static string[] GroupNames(IRenderedFragment fragment) =>
        ResultGroups(fragment).Select(GroupName).ToArray();

    private static string[] ComboNames(IEnumerable<IElement> rows) =>
        rows.Select(row => row.QuerySelector(".combo-probability-name")!.TextContent.Trim()).ToArray();

    private static string[] ComboProbabilities(IEnumerable<IElement> rows) =>
        rows.Select(row => row.QuerySelector(".combo-probability-value")!.TextContent).ToArray();
}
