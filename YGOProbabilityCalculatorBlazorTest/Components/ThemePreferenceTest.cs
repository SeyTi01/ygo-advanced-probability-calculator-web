using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Layout;
using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazorTest.Localization;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public class ThemePreferenceTest {
    [TestCase(null, "system")]
    [TestCase("system", "system")]
    [TestCase("invalid", "system")]
    [TestCase("ja", "ja")]
    public void HeaderRestoresTheLanguagePreferenceSeparatelyFromTheResolvedLanguage(string? stored, string expected) {
        using Bunit.TestContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.ComponentFactories.AddStub<CalculatorWorkspace>();
        context.JSInterop.Setup<string>("ygoLocale.getPreference").SetResult(stored!);

        IRenderedComponent<MainLayout> cut = context.RenderComponent<MainLayout>();

        cut.WaitForAssertion(() => Assert.That(cut.Find("#language-preference").GetAttribute("value"), Is.EqualTo(expected)));
        Assert.That(cut.Find("#language-preference option").GetAttribute("value"), Is.EqualTo("system"));
        Assert.That(cut.Find("#language-preference").GetAttribute("aria-label"), Is.EqualTo(UiText.Get("Language.Label")));
        Assert.That(cut.FindAll(".preference-icon[aria-hidden='true']"), Has.Count.EqualTo(2));
    }

    [TestCase("system", "en")]
    [TestCase("ja", "system")]
    public async Task LanguagePreferenceChangesPersistEvenWhenTheResolvedLanguageIsUnchanged(string stored, string next) {
        using PresentationLocale locale = new("en", production: true);
        using Bunit.TestContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.ComponentFactories.AddStub<CalculatorWorkspace>();
        context.JSInterop.Setup<string>("ygoLocale.getPreference").SetResult(stored);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/help");
        IRenderedComponent<MainLayout> cut = context.RenderComponent<MainLayout>();
        cut.WaitForAssertion(() => Assert.That(cut.Find("#language-preference").GetAttribute("value"), Is.EqualTo(stored)));

        await cut.Find("#language-preference").ChangeAsync(new ChangeEventArgs { Value = next });

        Assert.That(context.JSInterop.Invocations["ygoLocale.change"].Single().Arguments, Is.EqualTo(new object?[] { next, null }));
        Assert.That(cut.Find("#language-preference").GetAttribute("value"), Is.EqualTo(next));
    }

    [Test]
    public async Task AnUnfinishedEditKeepsThePreviousLanguagePreferenceSelected() {
        using Bunit.TestContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.ComponentFactories.AddStub<CalculatorWorkspace>();
        context.JSInterop.Setup<string>("ygoLocale.getPreference").SetResult("ja");
        context.JSInterop.Setup<bool>("ygoLocale.hasPendingEdits").SetResult(true);
        IRenderedComponent<MainLayout> cut = context.RenderComponent<MainLayout>();
        cut.WaitForAssertion(() => Assert.That(cut.Find("#language-preference").GetAttribute("value"), Is.EqualTo("ja")));

        await cut.Find("#language-preference").ChangeAsync(new ChangeEventArgs { Value = "system" });

        Assert.That(cut.Find("#language-preference").GetAttribute("value"), Is.EqualTo("ja"));
        Assert.That(cut.Find("[role='alert']").TextContent, Does.Contain(UiText.Get("Language.UnfinishedEdit")));
        Assert.That(context.JSInterop.Invocations["ygoLocale.change"], Is.Empty);
        Assert.That(cut.Find(".page").HasAttribute("inert"), Is.False);
    }

    [TestCase("light", "light")]
    [TestCase("dark", "dark")]
    [TestCase("invalid", "system")]
    [TestCase(null, "system")]
    public void HeaderRestoresBrowserPreference(string? storedPreference, string expectedPreference) {
        using Bunit.TestContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.ComponentFactories.AddStub<CalculatorWorkspace>();
        context.JSInterop.Setup<string>("ygoTheme.getPreference").SetResult(storedPreference!);

        IRenderedComponent<MainLayout> cut = context.RenderComponent<MainLayout>();

        cut.WaitForAssertion(() => Assert.That(
            cut.Find("#theme-preference").GetAttribute("value"),
            Is.EqualTo(expectedPreference)));
        Assert.That(cut.Find("#theme-preference").GetAttribute("aria-label"), Is.EqualTo("Color theme preference"));
    }

    [Test]
    public async Task ChangingThemePreferencePersistsThroughJavaScript() {
        using Bunit.TestContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.ComponentFactories.AddStub<CalculatorWorkspace>();
        context.JSInterop.Setup<string>("ygoTheme.getPreference").SetResult("system");

        IRenderedComponent<MainLayout> cut = context.RenderComponent<MainLayout>();
        cut.WaitForAssertion(() => Assert.That(cut.Find("#theme-preference").GetAttribute("value"), Is.EqualTo("system")));

        await cut.Find("#theme-preference").ChangeAsync(new ChangeEventArgs { Value = "dark" });

        Assert.That(cut.Find("#theme-preference").GetAttribute("value"), Is.EqualTo("dark"));
        Assert.That(context.JSInterop.Invocations["ygoTheme.setPreference"].Single().Arguments.Single(), Is.EqualTo("dark"));
    }

    [Test]
    public void CalculatorHeaderUsesItsSinglePageHeading() {
        using Bunit.TestContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.ComponentFactories.AddStub<CalculatorWorkspace>();

        IRenderedComponent<MainLayout> cut = context.RenderComponent<MainLayout>();

        Assert.That(cut.Find(".top-row .app-title").TagName, Is.EqualTo("H1"));
        Assert.That(cut.FindAll("h1"), Has.Count.EqualTo(1));
        Assert.That(cut.Find("#theme-preference").GetAttribute("aria-label"), Is.EqualTo("Color theme preference"));
        Assert.That(cut.Find("a[href='/help']").TextContent.Trim(), Is.EqualTo("Help"));
    }

    [TestCase("/help")]
    [TestCase("/help/")]
    [TestCase("/help/?from=search")]
    [TestCase("/help#examples")]
    public void HelpHeaderDoesNotDuplicateItsContentHeading(string path) {
        using Bunit.TestContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.ComponentFactories.AddStub<CalculatorWorkspace>();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(path);

        IRenderedComponent<MainLayout> cut = context.RenderComponent<MainLayout>(parameters => parameters.Add(
            layout => layout.Body,
            builder => builder.AddMarkupContent(0, "<h1>Help page heading</h1>")));

        Assert.That(cut.Find(".top-row .app-title").TagName, Is.EqualTo("DIV"));
        Assert.That(cut.FindAll("h1"), Has.Count.EqualTo(1));
        Assert.That(cut.Find("h1").TextContent, Is.EqualTo("Help page heading"));
        Assert.That(cut.Find("#theme-preference").GetAttribute("aria-label"), Is.EqualTo("Color theme preference"));
        Assert.That(cut.Find("a[href='/']").TextContent.Trim(), Is.EqualTo("Back"));
    }
}
