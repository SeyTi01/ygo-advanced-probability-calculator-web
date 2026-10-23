using Bunit;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class CalculatorEditorMovePositionTest : CalculatorEditorTestBase {
    [TestCase("card")]
    [TestCase("combo")]
    [TestCase("category")]
    [TestCase("group")]
    [TestCase("priority")]
    public async Task DirectMovesPreserveIdentityPlansAndSavedOrder(string kind) {
        CategoryBase[] categories = Enumerable.Range(1, 4).Select(index => new CategoryBase($"Role {index}")).ToArray();
        List<Card> cards = Enumerable.Range(1, 4).Select(index => new Card([categories[index - 1]], 1, $"Card {index}", active: index != 2, id: $"c{index}", drawCount: 1)).ToList();
        List<Combo> combos = Enumerable.Range(1, 4).Select(index => new Combo([new(categories[index - 1], 1, 1)], $"Route {index}", active: index != 2, groupId: $"g{index}")).ToList();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new() {
            Categories = categories.ToList(), Cards = cards, Combos = combos,
            ComboGroups = Enumerable.Range(1, 4).Select(index => new ComboGroup($"g{index}", $"Group {index}")).ToList(),
            DrawActivationPriority = new(DrawActivationMode.CustomPriority, cards.Select(card => card.Id).ToArray()), HandSize = 1
        });
        Dictionary<string, DrawEffectPlan> plans = cut.FindComponents<CardEditor>().ToDictionary(editor => editor.Instance.Card.Id, editor => editor.Instance.Card.EffectPlan);
        if (kind == "category" || kind == "group") {
            await cut.Find(kind == "category" ? "[aria-label='Edit category Role 1']" : "[aria-label='Edit group Group 1']").ClickAsync(new());
            await cut.Find(kind == "category" ? "[aria-label='New name for category Role 1']" : "[aria-label='New name for group Group 1']").InputAsync(new() { Value = "Unsaved rename" });
        }
        if (kind == "card") {
            await cut.FindComponents<CardEditor>()[0].Find(".accordion-button").ClickAsync(new());
            await cut.FindComponents<CardEditor>()[0].Find("#cardCategory0").ChangeAsync(new() { Value = categories[1].Identity });
        }
        if (kind == "combo") {
            await cut.FindComponents<ComboEditor>()[0].Find(".accordion-button").ClickAsync(new());
            await RequirementField(cut.FindComponents<ComboEditor>()[0], "#minCount0").InputAsync(new() { Value = "0" });
        }
        object identity = kind == "card" ? cut.FindComponents<CardEditor>()[0].Instance : cut.FindComponents<ComboEditor>()[0].Instance;
        // First to last, last to first, then middle to middle, using the same keyed entry.
        await Move(cut, kind, 0, "4");
        AssertOrder(cut, kind, [2, 3, 4, 1]);
        await Move(cut, kind, 3, "1");
        AssertOrder(cut, kind, [1, 2, 3, 4]);
        await Move(cut, kind, 0, "2");
        await Move(cut, kind, 1, "3");
        AssertOrder(cut, kind, [2, 3, 1, 4]);
        if (kind == "card") {
            Assert.That(cut.FindComponents<CardEditor>()[2].Instance, Is.SameAs(identity));
            Assert.That(cut.FindComponents<CardEditor>()[2].Find("#cardCategory2").GetAttribute("value"), Is.EqualTo(categories[1].Identity));
        }
        if (kind == "combo") {
            Assert.That(cut.FindComponents<ComboEditor>()[2].Instance, Is.SameAs(identity));
            Assert.That(RequirementField(cut.FindComponents<ComboEditor>()[2], "#minCount2").GetAttribute("value"), Is.EqualTo("0"));
        }
        if (kind == "category" || kind == "group") {
            Assert.That(cut.Find(kind == "category" ? "[aria-label='New name for category Role 1']" : "[aria-label='New name for group Group 1']").GetAttribute("value"), Is.EqualTo("Unsaved rename"));
        }
        foreach (IRenderedComponent<CardEditor> editor in cut.FindComponents<CardEditor>()) {
            Assert.That(editor.Instance.Card.EffectPlan, Is.EqualTo(plans[editor.Instance.Card.Id]));
            Assert.That(editor.Instance.Card.Active, Is.EqualTo(editor.Instance.Card.Id != "c2"));
        }
        Assert.That(cut.FindComponents<ComboEditor>().Single(editor => editor.Instance.Combo.Name == "Route 2").Instance.Combo.Active, Is.False);
        if (kind != "priority") {
            Assert.That(cut.FindComponent<SessionRecovery>().Instance.Session.DrawActivationPriority.CardIds, Is.EqualTo(new[] { "c1", "c2", "c3", "c4" }));
        }
        Assert.That(cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo.GroupId).Order(), Is.EqualTo(new[] { "g1", "g2", "g3", "g4" }));
        await Button(cut, "Save Session").ClickAsync(new());
        ISessionService codec = context.Services.GetRequiredService<ISessionService>();
        string shared = SessionShareCodec.Decode(new Uri(SessionShareCodec.CreateLink("https://example.test/", SavedSessionJson())).Fragment);
        SessionState loaded = await codec.LoadSessionAsync(shared);
        string[] saved = kind switch {
            "card" => loaded.Cards.Select(card => card.Name).ToArray(),
            "combo" => loaded.Combos.Select(combo => combo.Name).ToArray(),
            "category" => loaded.Categories.Select(category => category.Name).ToArray(),
            "group" => loaded.ComboGroups.Select(group => group.Name).ToArray(),
            _ => loaded.DrawActivationPriority.CardIds.Select(id => loaded.Cards.Single(card => card.Id == id).Name).ToArray()
        };
        Assert.That(saved.Select(name => int.Parse(name!.Split(' ')[^1])), Is.EqualTo(new[] { 2, 3, 1, 4 }));
    }

    [TestCase("")]
    [TestCase("0")]
    [TestCase("5")]
    [TestCase("1.5")]
    [TestCase("abc")]
    [TestCase("9999999999999999999999")]
    public async Task InvalidDestinationDoesNotInvokeMove(string value) {
        int moves = 0;
        IRenderedComponent<MoveToPosition> cut = context.RenderComponent<MoveToPosition>(parameters => parameters
            .Add(component => component.Index, 1).Add(component => component.Count, 4).Add(component => component.ItemLabel, "test")
            .Add(component => component.OnMove, _ => moves++));
        await cut.Find("button").ClickAsync(new());
        Assert.That(cut.Find("label").TextContent, Does.Contain("1–4").And.Contain("current: 2"));
        await cut.Find("input").InputAsync(new() { Value = value });
        await cut.Find("input").KeyDownAsync(new() { Key = "Enter" });
        Assert.That(moves, Is.Zero);
        Assert.That(cut.Find("input").GetAttribute("aria-invalid"), Is.EqualTo("true"));
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("1 to 4"));
        await cut.Find("input").KeyDownAsync(new() { Key = "Escape" });
        Assert.That(cut.FindAll("input"), Is.Empty);
        Assert.That(context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task SamePositionAndCancelDoNotInvokeMove() {
        int moves = 0;
        IRenderedComponent<MoveToPosition> cut = context.RenderComponent<MoveToPosition>(parameters => parameters
            .Add(component => component.Count, 4).Add(component => component.OnMove, _ => moves++));
        await cut.Find("button").ClickAsync(new());
        await cut.Find("input").KeyDownAsync(new() { Key = "Enter" });
        Assert.That(cut.FindAll("input"), Is.Empty);
        await cut.Find("button").ClickAsync(new());
        await cut.Find("input").InputAsync(new() { Value = "4" });
        await Button(cut, "Cancel").ClickAsync(new());
        Assert.That(moves, Is.Zero);
        Assert.That(cut.FindAll("input"), Is.Empty);
    }

    private static async Task Move(IRenderedComponent<ProbabilityCalculatorComponent> cut, string kind, int index, string destination) {
        IRenderedComponent<MoveToPosition> control = kind switch {
            "card" => cut.FindComponents<CardEditor>()[index].FindComponent<MoveToPosition>(),
            "combo" => cut.FindComponents<ComboEditor>()[index].FindComponent<MoveToPosition>(),
            "category" => cut.FindComponent<CategoryListEditor>().FindComponent<MoveToPosition>(),
            "group" => cut.FindComponent<ComboListEditor>().FindComponents<ReorderControls>().Single().FindComponent<MoveToPosition>(),
            _ => cut.FindComponent<CalculationSettingsMenu>().FindComponents<MoveToPosition>()[index]
        };
        if (kind == "card" || kind == "combo") {
            IRenderedFragment editor = kind == "card" ? cut.FindComponents<CardEditor>()[index] : cut.FindComponents<ComboEditor>()[index];
            await editor.Find(".action-menu-trigger").ClickAsync(new());
        }
        await control.Find("button").ClickAsync(new());
        await control.Find("input").InputAsync(new() { Value = destination });
        await control.Find("input").KeyDownAsync(new() { Key = "Enter" });
    }

    private static void AssertOrder(IRenderedComponent<ProbabilityCalculatorComponent> cut, string kind, int[] expected) {
        IEnumerable<string> names = kind switch {
            "card" => cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card.Name!),
            "combo" => cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo.Name!),
            "category" => cut.FindComponent<CategoryListEditor>().Instance.CategoryBases.Select(category => category.Name),
            "group" => cut.FindComponent<ComboListEditor>().Instance.ComboGroups.Select(group => group.Name),
            _ => cut.FindAll(".draw-priority-name").Select(row => row.TextContent.Trim())
        };
        Assert.That(names.Select(name => int.Parse(name.Split(' ')[^1])), Is.EqualTo(expected));
    }
}
