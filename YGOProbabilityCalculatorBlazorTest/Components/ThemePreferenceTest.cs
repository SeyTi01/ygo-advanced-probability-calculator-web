using Bunit;
using Microsoft.AspNetCore.Components;
using YGOProbabilityCalculatorBlazor.Layout;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public class ThemePreferenceTest {
    [TestCase("light", "light")]
    [TestCase("dark", "dark")]
    [TestCase("invalid", "system")]
    [TestCase(null, "system")]
    public void HeaderRestoresBrowserPreference(string? storedPreference, string expectedPreference) {
        using var context = new Bunit.TestContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<string>("ygoTheme.getPreference").SetResult(storedPreference!);

        var cut = context.RenderComponent<MainLayout>();

        cut.WaitForAssertion(() => Assert.That(
            cut.Find("#theme-preference").GetAttribute("value"),
            Is.EqualTo(expectedPreference)));
        Assert.That(cut.Find("#theme-preference").GetAttribute("aria-label"), Is.EqualTo("Color theme preference"));
    }

    [Test]
    public async Task ChangingThemePreferencePersistsThroughJavaScript() {
        using var context = new Bunit.TestContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<string>("ygoTheme.getPreference").SetResult("system");

        var cut = context.RenderComponent<MainLayout>();
        cut.WaitForAssertion(() => Assert.That(cut.Find("#theme-preference").GetAttribute("value"), Is.EqualTo("system")));

        await cut.Find("#theme-preference").ChangeAsync(new ChangeEventArgs { Value = "dark" });

        Assert.That(cut.Find("#theme-preference").GetAttribute("value"), Is.EqualTo("dark"));
        var invocation = context.JSInterop.Invocations["ygoTheme.setPreference"].Single();
        Assert.That(invocation.Arguments.Single(), Is.EqualTo("dark"));
    }
}
