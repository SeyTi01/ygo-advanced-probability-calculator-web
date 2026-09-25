using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class ActiveEntriesEditorTest {
    private TestContext context = null!;
    private readonly CategoryBase a = new("A");
    private readonly CategoryBase b = new("B");

    [SetUp]
    public void SetUp() {
        context = new TestContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IProbabilityCalculatorService, ProbabilityCalculatorService>();
        context.Services.AddSingleton<ISerializer, JsonSerializer>();
        context.Services.AddSingleton<ISessionService, SessionService>();
        context.Services.AddSingleton<IPendingSessionService, PendingSessionService>();
        context.Services.AddSingleton<IDeckImportService>(Mock.Of<IDeckImportService>());
    }

    [TearDown]
    public void TearDown() => context.Dispose();

    private IRenderedComponent<ProbabilityCalculatorComponent> Render(SessionState session) {
        context.Services.GetRequiredService<IPendingSessionService>().PendingSession = session;
        return context.RenderComponent<ProbabilityCalculatorComponent>();
    }

    private static SessionState Session(List<Card> cards, List<Combo> combos, int handSize = 2) => new() {
        Categories = [new("A"), new("B")],
        Cards = cards,
        Combos = combos,
        HandSize = handSize
    };

    private static void AssertProbability(IRenderedComponent<ProbabilityCalculatorComponent> cut, double value) {
        var activeCards = cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card)
            .Where(card => card.Active).ToList();
        var activeCombos = cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo)
            .Where(combo => combo.Active).ToList();
        var handSize = int.Parse(cut.Find("#handSize").GetAttribute("value") ?? "5");
        var oracleValue = SmallDeckOracleTest.EnumerateProbability(activeCards, activeCombos, handSize);
        Assert.That(value, Is.EqualTo(oracleValue).Within(1e-12),
            "expected probability must match independent physical-hand enumeration");

        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Calculate").Click();
        cut.WaitForAssertion(() => Assert.That(
            cut.Find(".alert-primary").TextContent,
            Does.Contain(value.ToString("P2"))));
    }

    [Test]
    public void InactiveCardCopiesLeaveTheEffectivePopulationAndCanBeRestored() {
        var cut = Render(Session(
            [new([a], 2, "A copies"), new([], 2, "Uncategorized copies")],
            [new([new(a, 1, 2)], "At least one A")]));
        var deckHeading = cut.FindComponent<CardListEditor>().Find("h4");

        AssertProbability(cut, 5.0 / 6.0);
        Assert.That(deckHeading.TextContent, Does.Contain("4 active / 4 total copies"));

        var card = cut.FindComponent<CardEditor>();
        Assert.That(card.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        card.Find("#cardActive0").Change(false);

        Assert.That(card.Find("#cardActive0").HasAttribute("checked"), Is.False);
        Assert.That(card.Find(".accordion-item").ClassList.Contains("entry-inactive"), Is.True);
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Inactive"));
        Assert.That(cut.FindAll(".alert-primary"), Is.Empty, "changing the effective deck must clear the previous result");
        Assert.That(deckHeading.TextContent, Does.Contain("2 active / 4 total copies"));
        AssertProbability(cut, 0.0);

        card.Find("#cardActive0").Change(true);
        Assert.That(cut.FindAll(".alert-primary"), Is.Empty);
        AssertProbability(cut, 5.0 / 6.0);
    }

    [Test]
    public void InactiveCombosAreRemovedFromTheUnionAndAnInactiveIncompleteComboDoesNotBlockCalculation() {
        var cut = Render(Session(
            [new([a], 2, "A"), new([b], 1, "B"), new([], 1, "Blank")],
            [new([new(a, 1, 1)], "Exactly one A"), new([new(b, 1, 1)], "Exactly one B"), new([], "Draft combo")]));

        Assert.That(cut.FindAll("button").Single(button => button.TextContent.Trim() == "Calculate").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find("[role=status]").TextContent, Does.Contain("incomplete active combo"));

        var incompleteCombo = cut.FindComponents<ComboEditor>()[2];
        incompleteCombo.Find("#comboActive2").Change(false);
        Assert.That(cut.FindAll("button").Single(button => button.TextContent.Trim() == "Calculate").HasAttribute("disabled"), Is.False);
        AssertProbability(cut, 5.0 / 6.0);

        cut.FindComponents<ComboEditor>()[0].Find("#comboActive0").Change(false);
        Assert.That(cut.FindAll(".alert-primary"), Is.Empty);
        AssertProbability(cut, 0.5);

        cut.FindComponents<ComboEditor>()[0].Find("#comboActive0").Change(true);
        AssertProbability(cut, 5.0 / 6.0);
    }

    [Test]
    public void EligibilityAndFeedbackUseOnlyActiveCardsAndCombos() {
        var cut = Render(Session(
            [new([a], 2, "A"), new([], 2, "Blank")],
            [new([new(a, 1, 2)], "A combo")], handSize: 3));

        var calculate = () => cut.FindAll("button").Single(button => button.TextContent.Trim() == "Calculate");
        Assert.That(calculate().HasAttribute("disabled"), Is.False);

        cut.Find("#handSize").Change("0");
        Assert.That(calculate().HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find("[role=status]").TextContent, Does.Contain("Hand size must be at least 1"));

        cut.Find("#handSize").Change("3");
        cut.Find("#cardActive0").Change(false);
        Assert.That(calculate().HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find("[role=status]").TextContent, Does.Contain("fewer than the hand size"));

        cut.Find("#cardActive1").Change(false);
        Assert.That(cut.Find("[role=status]").TextContent, Does.Contain("Activate at least one card"));

        cut.Find("#cardActive0").Change(true);
        cut.Find("#cardActive1").Change(true);
        cut.Find("#comboActive0").Change(false);
        Assert.That(calculate().HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find("[role=status]").TextContent, Does.Contain("Activate at least one combo"));
    }

    [Test]
    public void EditingInactiveCollapsedEntriesKeepsThemInactiveAndPreservesTheirDrafts() {
        var cut = Render(Session(
            [new([a], 2, "Card")],
            [new([new(a, 1, 2)], "Combo")]));
        var card = cut.FindComponent<CardEditor>();
        var combo = cut.FindComponent<ComboEditor>();

        Assert.That(card.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        Assert.That(combo.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("false"));
        card.Find("#cardActive0").Change(false);
        combo.Find("#comboActive0").Change(false);

        card.Find(".accordion-button").Click();
        combo.Find(".accordion-button").Click();
        card.Find("select").Change("B");
        combo.Find("select").Change("B");
        combo.Find("#minCount0").Input("0");
        combo.Find("#maxCount0").Input("0");
        card.Find("#cardActive0").Change(true);
        card.Find("#cardActive0").Change(false);
        combo.Find("#comboActive0").Change(true);
        combo.Find("#comboActive0").Change(false);

        Assert.That(card.Find("select").GetAttribute("value"), Is.EqualTo("B"));
        Assert.That(combo.Find("select").GetAttribute("value"), Is.EqualTo("B"));
        Assert.That(combo.Find("#minCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(card.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(combo.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));

        card.Find("input[type=number]").Input("3");
        card.Find("#cardName0").Input("Edited card");
        card.FindAll("button").Single(button => button.TextContent.Trim() == "Add").Click();
        combo.Find("#comboName0").Input("Edited combo");
        combo.FindAll("button").Single(button => button.TextContent.Trim() == "Add").Click();

        Assert.That(card.Find("#cardActive0").HasAttribute("checked"), Is.False);
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Edited card").And.Contain("(3)"));
        Assert.That(card.Find(".accordion-body").TextContent, Does.Contain("A").And.Contain("B"));
        Assert.That(combo.Find("#comboActive0").HasAttribute("checked"), Is.False);
        Assert.That(combo.Find(".accordion-button").TextContent, Does.Contain("Edited combo"));
        Assert.That(combo.Find(".accordion-body").TextContent, Does.Contain("A (1–2)").And.Contain("B (0–0)"));
    }

    [Test]
    public void SessionRoundTripPreservesInactiveEntriesAndLegacyEntriesDefaultActive() {
        var cut = Render(Session(
            [new([a], 2, "Card")],
            [new([new(a, 1, 2)], "Combo")]));
        cut.Find("#cardActive0").Change(false);
        cut.Find("#comboActive0").Change(false);
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Save Session").Click();

        var invocation = context.JSInterop.Invocations["downloadFileFromStream"].Single();
        var savedJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using (var document = System.Text.Json.JsonDocument.Parse(savedJson)) {
            Assert.That(document.RootElement.GetProperty("Cards")[0].GetProperty("Active").GetBoolean(), Is.False);
            Assert.That(document.RootElement.GetProperty("Combos")[0].GetProperty("Active").GetBoolean(), Is.False);
        }

        var sessionInput = cut.FindComponents<InputFile>()[1];
        sessionInput.UploadFiles(InputFileContent.CreateFromText(savedJson, "session.json"));
        cut.WaitForAssertion(() => {
            Assert.That(cut.Find("#cardActive0").HasAttribute("checked"), Is.False);
            Assert.That(cut.Find("#comboActive0").HasAttribute("checked"), Is.False);
        });

        const string legacyJson = """
            {"Categories":[{"Name":"A"}],"Cards":[{"Categories":[{"Name":"A"}],"Copies":2,"Name":"Card"}],"Combos":[{"Categories":[{"BaseCategory":{"Name":"A"},"MinCount":1,"MaxCount":2}],"Name":"Combo"}],"HandSize":2}
            """;
        sessionInput.UploadFiles(InputFileContent.CreateFromText(legacyJson, "legacy-session.json"));
        cut.WaitForAssertion(() => {
            Assert.That(cut.Find("#cardActive0").HasAttribute("checked"), Is.True);
            Assert.That(cut.Find("#comboActive0").HasAttribute("checked"), Is.True);
        });
    }

    [Test]
    public void CategoryRenamePreservesInactiveEntriesAndSelectedEditorDrafts() {
        var cut = Render(Session(
            [new([a], 2, "Disabled A", active: false)],
            [new([new(a, 1, 1)], "Disabled A combo", active: false)]));
        var card = cut.FindComponent<CardEditor>();
        var combo = cut.FindComponent<ComboEditor>();

        card.Find(".accordion-button").Click();
        combo.Find(".accordion-button").Click();
        card.Find("select").Change("A");
        combo.Find("select").Change("A");
        combo.Find("#minCount0").Input("0");
        combo.Find("#maxCount0").Input("0");

        cut.Find("[aria-label='Rename category A']").Click();
        cut.Find("[aria-label='New name for category A']").Input("Renamed A");
        cut.Find("[aria-label='Save category name']").Click();

        Assert.That(card.Find("#cardActive0").HasAttribute("checked"), Is.False);
        Assert.That(card.Find(".accordion-button").TextContent, Does.Contain("Inactive").And.Contain("Renamed A"));
        Assert.That(card.Find("select").GetAttribute("value"), Is.EqualTo("Renamed A"));
        Assert.That(combo.Find("#comboActive0").HasAttribute("checked"), Is.False);
        Assert.That(combo.Find(".accordion-button").TextContent, Does.Contain("Inactive").And.Contain("Renamed A"));
        Assert.That(combo.Find("select").GetAttribute("value"), Is.EqualTo("Renamed A"));
        Assert.That(combo.Find("#minCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(combo.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(card.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        Assert.That(combo.Find(".accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
    }

    [Test]
    public void DeletingEarlierRowsKeepsInactiveStateWithTheRemainingEntry() {
        var cut = Render(Session(
            [new([a], 1, "First"), new([b], 1, "Second", active: false)],
            [new([new(a, 0, 1)], "First combo"), new([new(b, 0, 1)], "Second combo", active: false)]));

        cut.FindComponents<CardEditor>()[0].Find("[title='Remove card']").Click();
        cut.FindComponents<ComboEditor>()[0].Find("[title='Remove combo']").Click();

        Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(1));
        Assert.That(cut.FindComponents<CardEditor>()[0].Find(".accordion-button").TextContent, Does.Contain("Second"));
        Assert.That(cut.Find("#cardActive0").HasAttribute("checked"), Is.False);
        Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(1));
        Assert.That(cut.FindComponents<ComboEditor>()[0].Find(".accordion-button").TextContent, Does.Contain("Second combo"));
        Assert.That(cut.Find("#comboActive0").HasAttribute("checked"), Is.False);
    }
}
