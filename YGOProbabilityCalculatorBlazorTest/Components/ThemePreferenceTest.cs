using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Layout;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public class ThemePreferenceTest {
    [TestCase("light", "light")]
    [TestCase("dark", "dark")]
    [TestCase("invalid", "system")]
    [TestCase(null, "system")]
    public void HeaderRestoresBrowserPreference(string? storedPreference, string expectedPreference) {
        using Bunit.TestContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
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
