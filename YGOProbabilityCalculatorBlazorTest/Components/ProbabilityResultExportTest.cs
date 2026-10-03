using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class ProbabilityResultExportTest {
    private TestContext context = null!;
    private ClipboardInterop clipboard = null!;

    [SetUp]
    public void SetUp() {
        context = new TestContext();
        clipboard = new ClipboardInterop();
        context.Services.AddSingleton<IJSRuntime>(clipboard);
    }

    [TearDown]
    public void TearDown() => context.Dispose();

    [Test]
    public void UngroupedSummaryUsesExactHandSizeComboOrderAndUnnamedFallback() {
        var result = new ProbabilityCalculationResult(
            0.123456,
            [
                new ComboProbabilityResult(2, null, 0.0, "orphan"),
                new ComboProbabilityResult(0, "Second route", 1.0),
                new ComboProbabilityResult(1, "First route", 0.25)
            ]);

        var summary = ProbabilityResultSummaryFormatter.Format(result, 7, CultureInfo.GetCultureInfo("en-US"));

        Assert.That(summary, Does.StartWith("Probability results\nHand size: 7\nAny active combo: 12.35%"));
        Assert.That(summary, Does.Contain("Individual combos:"));
        Assert.That(summary, Does.Contain("Unnamed combo 3"));
        Assert.That(summary.IndexOf("Unnamed combo 3", StringComparison.Ordinal), Is.LessThan(summary.IndexOf("Second route", StringComparison.Ordinal)));
        Assert.That(summary.IndexOf("Second route", StringComparison.Ordinal), Is.LessThan(summary.IndexOf("First route", StringComparison.Ordinal)));
        Assert.That(summary, Does.Contain("100.00%"));
        Assert.That(summary, Does.Contain("25.00%"));
    }

    [Test]
    public void GroupedSummaryPreservesStableMembershipOrderCountsDuplicatesAndEmptyGroups() {
        var result = new ProbabilityCalculationResult(
            0.87,
            [
                new ComboProbabilityResult(0, "Same route", 0.75, "left"),
                new ComboProbabilityResult(1, "Orphan route", 0.25, "missing"),
                new ComboProbabilityResult(2, "Second group route", 0.4, "right"),
                new ComboProbabilityResult(3, "Same route", 0.5, "left")
            ],
            [
                new GroupProbabilityResult("right", "Route group", 0.61, 1),
                new GroupProbabilityResult("left", "Route group", 0.5, 2),
                new GroupProbabilityResult("empty", "Empty group", 0.0, 0)
            ]);

        var summary = ProbabilityResultSummaryFormatter.Format(result, 6, CultureInfo.GetCultureInfo("en-US"));

        Assert.That(summary, Does.Contain("Hand size: 6"));
        Assert.That(summary, Does.Contain("Any active combo: 87.00%"));
        Assert.That(Occurrences(summary, "**Route group**"), Is.EqualTo(2));
        Assert.That(summary, Does.Contain("**Route group** — 61.00% (1 active)\n  - **Second group route** — 40.00%"));
        Assert.That(summary, Does.Contain("**Route group** — 50.00% (2 active)\n  - **Same route** — 75.00%\n  - **Same route** — 50.00%"));
        Assert.That(summary, Does.Contain("**Empty group** — 0.00% (0 active)"));
        Assert.That(summary, Does.Contain("Ungrouped combos:\n- **Orphan route** — 25.00%"));
        Assert.That(summary.IndexOf("Second group route", StringComparison.Ordinal), Is.LessThan(summary.IndexOf("Same route", StringComparison.Ordinal)));
        Assert.That(summary.IndexOf("Empty group", StringComparison.Ordinal), Is.LessThan(summary.IndexOf("Orphan route", StringComparison.Ordinal)));
    }

    [Test]
    public void FormatterUsesExplicitEnglishAndGermanCulturesAndRoundsFractionalValues() {
        var result = new ProbabilityCalculationResult(
            0.125,
            [
                new ComboProbabilityResult(0, "Below rounding boundary", 0.123449),
                new ComboProbabilityResult(1, "Above rounding boundary", 0.123451),
                new ComboProbabilityResult(2, "Zero", 0.0),
                new ComboProbabilityResult(3, "One", 1.0),
                new ComboProbabilityResult(4, "Small", 0.00006)
            ]);

        var english = ProbabilityResultSummaryFormatter.Format(result, 5, CultureInfo.GetCultureInfo("en-US"));
        var german = ProbabilityResultSummaryFormatter.Format(result, 5, CultureInfo.GetCultureInfo("de-DE"));

        Assert.That(english, Does.Contain("Any active combo: 12.50%"));
        Assert.That(english, Does.Contain("Below rounding boundary** — 12.34%"));
        Assert.That(english, Does.Contain("Above rounding boundary** — 12.35%"));
        Assert.That(english, Does.Contain("**Zero** — 0.00%"));
        Assert.That(english, Does.Contain("**One** — 100.00%"));
        Assert.That(english, Does.Contain("**Small** — 0.01%"));
        Assert.That(german, Does.Contain("Any active combo: 12,50 %"));
        Assert.That(german, Does.Contain("Below rounding boundary** — 12,34 %"));
        Assert.That(german, Does.Contain("Above rounding boundary** — 12,35 %"));
    }

    [Test]
    public void UserLabelsCannotAddMarkdownHeadingsOrBreakTheSummaryStructure() {
        var result = new ProbabilityCalculationResult(
            0.5,
            [new ComboProbabilityResult(0, "Café 🌟\r\n# forged\n*header* <b>x</b>\u0001", 0.5, "group")],
            [new GroupProbabilityResult("group", "Tier\n## Other\r\nSecond\u0000", 0.5, 1)]);

        var summary = ProbabilityResultSummaryFormatter.Format(result, 4, CultureInfo.GetCultureInfo("en-US"));

        Assert.That(summary, Does.Contain("**Tier \\#\\# Other Second**"));
        Assert.That(summary, Does.Contain("Café 🌟 \\# forged \\*header\\* \\<b\\>x\\</b\\>"));
        Assert.That(summary.Split('\n').Any(line => line.StartsWith('#')), Is.False);
        Assert.That(summary.Split('\n').Any(line => line == "Other" || line == "Second" || line == "forged"), Is.False);
    }

    [Test]
    public async Task CopyUsesCapturedCurrentSummaryAndAnnouncesSuccess() {
        var result = Result(0.42, "First result");
        var cut = Render(result, handSize: 5);

        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());

        Assert.That(clipboard.CopyAttempts, Is.EqualTo(1));
        Assert.That(clipboard.LastCopiedText, Is.EqualTo(
            ProbabilityResultSummaryFormatter.Format(result, 5, CultureInfo.CurrentCulture)));
        Assert.That(cut.Find("[role='status']").TextContent, Does.Contain("Results copied."));
    }

    [Test]
    public async Task ClipboardDenialShowsSelectableFallbackWhichCanBeDismissed() {
        clipboard.Copy = _ => Task.FromResult(false);
        var cut = Render(Result(0.42, "Fallback combo"), handSize: 5);

        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());

        var textArea = cut.Find("textarea[readonly]");
        Assert.That(textArea.TextContent, Does.Contain("Hand size: 5"));
        Assert.That(textArea.TextContent, Does.Contain("Fallback combo"));
        Assert.That(cut.Find("label[for]").TextContent, Is.EqualTo("Result summary"));
        Assert.That(cut.Find("#" + textArea.Id).GetAttribute("aria-describedby"), Does.Contain("copy-fallback-instruction"));
        Assert.That(clipboard.FocusAttempts, Is.EqualTo(1));

        await cut.Find("[aria-label='Close copy fallback']").ClickAsync(new MouseEventArgs());

        Assert.That(cut.FindAll("textarea[readonly]"), Is.Empty);
        Assert.That(clipboard.FocusAttempts, Is.EqualTo(2));
    }

    [Test]
    public async Task ClipboardExceptionsAndUnavailableJavaScriptBothRevealFallback() {
        clipboard.FailCopy = true;
        var failedCopy = Render(Result(0.42, "Denied"), handSize: 5);
        await failedCopy.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());
        Assert.That(failedCopy.Find("textarea[readonly]").TextContent, Does.Contain("Denied"));

        failedCopy.Dispose();
        clipboard.FailCopy = false;
        clipboard.FailImport = true;
        var unavailableClipboard = Render(Result(0.35, "Unavailable"), handSize: 4);
        await unavailableClipboard.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());
        Assert.That(unavailableClipboard.Find("textarea[readonly]").TextContent, Does.Contain("Unavailable"));
        Assert.That(clipboard.CopyAttempts, Is.EqualTo(1));
    }

    [Test]
    public async Task StaleBusyAndNullResultsCannotStartCopy() {
        var result = Result(0.42, "Not current");
        var stale = Render(result, isStale: true);
        var staleButton = stale.Find("button[title='Copy a summary of these results']");
        Assert.That(staleButton.HasAttribute("disabled"), Is.True);
        Assert.That(staleButton.GetAttribute("aria-describedby"), Is.Not.Null);
        Assert.That(stale.Markup, Does.Contain("Recalculate to copy current results."));
        await staleButton.ClickAsync(new MouseEventArgs());

        var busy = Render(result, isCalculating: true);
        var busyButton = busy.Find("button[title='Copy a summary of these results']");
        Assert.That(busyButton.HasAttribute("disabled"), Is.True);
        Assert.That(busy.Markup, Does.Contain("Wait for the calculation to finish"));
        await busyButton.ClickAsync(new MouseEventArgs());

        var empty = Render(null);
        Assert.That(empty.FindAll("button"), Is.Empty);
        Assert.That(clipboard.CopyAttempts, Is.EqualTo(0));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task LateClipboardCompletionAfterInputChangeCannotReportForCurrentResults(bool copied) {
        var pendingCopy = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboard.Copy = _ => pendingCopy.Task;
        var result = Result(0.42, "Captured old result");
        var cut = Render(result, handSize: 5);

        var clickTask = cut.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());
        Assert.That(clipboard.CopyAttempts, Is.EqualTo(1));
        Assert.That(clipboard.LastCopiedText, Does.Contain("Hand size: 5"));
        Assert.That(clipboard.LastCopiedText, Is.EqualTo(
            ProbabilityResultSummaryFormatter.Format(result, 5, CultureInfo.CurrentCulture)));

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.HandSize, 6)
            .Add(component => component.IsStale, true));
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.True);

        pendingCopy.SetResult(copied);
        await clickTask;

        Assert.That(cut.FindAll("[role='status']"), Is.Empty);
        Assert.That(cut.FindAll("textarea[readonly]"), Is.Empty);
        Assert.That(clipboard.LastCopiedText, Does.Not.Contain("Hand size: 6"));
    }

    [Test]
    public async Task ANewerResultClearsPreviousCopyFallback() {
        clipboard.Copy = _ => Task.FromResult(false);
        var cut = Render(Result(0.42, "Old result"), handSize: 5);
        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());
        Assert.That(cut.Find("textarea[readonly]").TextContent, Does.Contain("Old result"));

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.Result, Result(0.87, "New result")));

        Assert.That(cut.FindAll("textarea[readonly]"), Is.Empty);
        Assert.That(cut.FindAll("[role='status']"), Is.Empty);
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.False);
    }

    private IRenderedComponent<ProbabilityResultExport> Render(
        ProbabilityCalculationResult? result,
        int handSize = 5,
        bool isStale = false,
        bool isCalculating = false) =>
        context.RenderComponent<ProbabilityResultExport>(parameters => parameters
            .Add(component => component.Result, result)
            .Add(component => component.HandSize, handSize)
            .Add(component => component.IsStale, isStale)
            .Add(component => component.IsCalculating, isCalculating));

    private static ProbabilityCalculationResult Result(double total, string comboName) =>
        new(total, [new ComboProbabilityResult(0, comboName, total)]);

    private static int Occurrences(string value, string search) =>
        value.Split(search, StringSplitOptions.None).Length - 1;

    private sealed class ClipboardInterop : IJSRuntime {
        private readonly ClipboardModule module;

        public ClipboardInterop() => module = new ClipboardModule(this);

        public Func<string, Task<bool>> Copy { get; set; } = _ => Task.FromResult(true);
        public bool FailImport { get; set; }
        public bool FailCopy { get; set; }
        public int CopyAttempts { get; private set; }
        public int FocusAttempts { get; private set; }
        public string? LastCopiedText { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) {
            if (identifier == "import") {
                if (FailImport)
                    return ValueTask.FromException<TValue>(new JSException("Module load failed."));

                return ValueTask.FromResult((TValue)(object)module);
            }

            return ValueTask.FromException<TValue>(new JSException("Unexpected JavaScript invocation."));
        }

        private ValueTask<TValue> InvokeModuleAsync<TValue>(string identifier, object?[]? args) {

            if (identifier == "copyText") {
                var text = (string)args![0]!;
                CopyAttempts++;
                LastCopiedText = text;
                var copyTask = FailCopy
                    ? Task.FromException<bool>(new JSException("Clipboard access was denied."))
                    : Copy(text);
                return (ValueTask<TValue>)(object)new ValueTask<bool>(copyTask);
            }

            if (identifier == "focusElementById") {
                FocusAttempts++;
                return ValueTask.FromResult(default(TValue)!);
            }

            return ValueTask.FromException<TValue>(new JSException("Unexpected JavaScript invocation."));
        }

        private sealed class ClipboardModule(ClipboardInterop owner) : IJSObjectReference {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
                InvokeAsync<TValue>(identifier, CancellationToken.None, args);

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
                owner.InvokeModuleAsync<TValue>(identifier, args);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
