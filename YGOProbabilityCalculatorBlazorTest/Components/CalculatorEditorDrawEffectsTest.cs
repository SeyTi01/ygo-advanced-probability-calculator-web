using Bunit;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorDrawEffectsTest : CalculatorEditorTestBase {
    [Test]
    public async Task DrawSettingPreservesDraftsIdentityAndSessionAndInvalidatesResults() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        await Button(cut, "Calculate").ClickAsync(new());
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        string id = editor.Instance.Card.Id;
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = "user:B" });
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "2" });
        Assert.That(editor.Instance.Card.DrawCount, Is.EqualTo(2));
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(id));
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo("user:B"));
        AssertPreviousResult(cut);
        Assert.That(editor.Find("#cardDraw0").GetAttribute("aria-describedby"), Is.EqualTo("cardDrawHelp0"));
        Assert.That(editor.Find("#cardDrawHelp0").TextContent, Does.Contain("Separate entries resolve independently"));
        await Button(cut, "Save Session").ClickAsync(new());
        SessionState saved = await context.Services.GetRequiredService<ISessionService>().LoadSessionAsync(SavedSessionJson());
        Assert.That(saved.Cards[0].DrawCount, Is.EqualTo(2));
        await cut.Find("[aria-label='Move card First, row 1 down']").ClickAsync(new());
        editor = cut.FindComponents<CardEditor>()[1];
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(id));
        Assert.That(editor.Find("#cardDraw1").GetAttribute("value"), Is.EqualTo("2"));
        Assert.That(editor.Find("#cardCategory1").GetAttribute("value"), Is.EqualTo("user:B"));
        await editor.Find("#cardDraw1").ChangeAsync(new() { Value = "" });
        Assert.That(editor.Instance.Card.DrawCount, Is.Null);
        Assert.That(editor.Instance.Card.Active, Is.True);
        Assert.That(editor.Instance.Card.Copies, Is.EqualTo(2));
    }

    [Test]
    public async Task InvalidSelectionDoesNotReplaceSavedSetting() {
        SessionState session = Session();
        session.Cards[0] = session.Cards[0].WithDrawCount(1);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "4" });
        Assert.That(editor.Instance.Card.DrawCount, Is.EqualTo(1));
        Assert.That(editor.Markup, Does.Contain("Choose None, Draw 1, Draw 2, or Draw 3"));
        await editor.Find("#cardDraw0").ChangeAsync(new() { Value = "3" });
        Assert.That(editor.Instance.Card.DrawCount, Is.EqualTo(3));
        Assert.That(editor.FindAll(".alert"), Is.Empty);
    }
}
