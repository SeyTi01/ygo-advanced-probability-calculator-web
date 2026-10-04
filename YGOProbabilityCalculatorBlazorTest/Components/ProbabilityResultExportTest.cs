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
    public async Task DisposalDuringImportReleasesTheLateModuleExactlyOnce() {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboard.ImportDelay = completion.Task;
        var cut = Render(Result(0.42, "Late import"));
        await cut.Instance.DisposeAsync();
        completion.SetResult();
        await clipboard.ModuleDisposed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await cut.Instance.DisposeAsync();
        Assert.That(clipboard.DisposalCount, Is.EqualTo(1));
        Assert.That(clipboard.CopyAttempts, Is.Zero);
        Assert.That(clipboard.FocusAttempts, Is.Zero);
    }

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
            [new ComboProbabilityResult(0, "Café 🌟\r\n# forged\n*header* <b>x</b> A+B.! (C) [D] _E_ \"F\" \\path\u0001", 0.5, "group")],
            [new GroupProbabilityResult("group", "Tier\n## Other\r\nSecond\u0000", 0.5, 1)]);

        var summary = ProbabilityResultSummaryFormatter.Format(result, 4, CultureInfo.GetCultureInfo("en-US"));

        Assert.That(summary, Does.Contain("**Tier ## Other Second**"));
        Assert.That(summary, Does.Contain("Café 🌟 # forged *header* <b>x</b> A+B.! (C) [D] _E_ \"F\" \\path"));
        Assert.That(summary.Split('\n').Any(line => line.StartsWith('#')), Is.False);
        Assert.That(summary.Split('\n').Any(line => line == "Other" || line == "Second" || line == "forged"), Is.False);
    }

    [Test]
    public async Task ManualFallbackKeepsMarkupLikeNamesAsPlainText() {
        clipboard.Copy = _ => Task.FromResult(false);
        var result = Result(0.5, "<b>route</b> + [name]");
        var cut = Render(result);

        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());

        Assert.That(cut.Find("textarea[readonly]").TextContent, Does.Contain("**<b>route</b> + [name]**"));
        Assert.That(cut.FindAll("b"), Is.Empty);
    }

    [Test]
    public void FormatterPreservesRepresentativeNamesAndGermanPercentagesExactly() {
        var result = GermanExampleResult();

        var summary = ProbabilityResultSummaryFormatter.Format(result, 5, CultureInfo.GetCultureInfo("de-DE"));

        Assert.That(summary, Is.EqualTo(ExpectedGermanSummary()));
        Assert.That(summary, Does.Not.Contain("\\+"));
        Assert.That(summary, Does.Not.Contain("\\."));
    }

    [Test]
    public async Task CopyUsesCapturedCurrentPlainTextAndShowsVisibleIconWithHiddenAnnouncement() {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try {
            var cut = Render(GermanExampleResult(), handSize: 5);
            var button = cut.Find("button[title='Copy a summary of these results']");

            Assert.That(button.TextContent.Trim(), Is.EqualTo("Copy results"));
            Assert.That(cut.FindAll(".probability-result-copy-rest-icon").Count, Is.EqualTo(1));
            Assert.That(cut.FindAll(".probability-result-copy-success-icon"), Is.Empty);

            await button.ClickAsync(new MouseEventArgs());

            Assert.That(clipboard.CopyAttempts, Is.EqualTo(1));
            Assert.That(clipboard.LastCopiedText, Is.EqualTo(ExpectedGermanSummary()));
            Assert.That(cut.Find("button[title='Copy a summary of these results']").TextContent.Trim(), Is.EqualTo("Copy results"));
            Assert.That(cut.FindAll(".probability-result-copy-rest-icon"), Is.Empty);
            Assert.That(cut.FindAll(".probability-result-copy-success-icon").Count, Is.EqualTo(1));
            Assert.That(cut.Find(".probability-result-copy-icon-slot").GetAttribute("aria-hidden"), Is.EqualTo("true"));
            Assert.That(cut.Find("[role='status']").TextContent, Does.Contain("Results copied."));
            Assert.That(cut.Find("[role='status']").GetAttribute("class"), Does.Contain("visually-hidden"));
            Assert.That(button.TextContent, Does.Not.Contain("Results copied."));
        }
        finally {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ClipboardAndManualFallbackContainTheSameIndependentlyExpectedText(bool stale) {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try {
            var result = GermanExampleResult();
            var copied = Render(result, handSize: 5, isStale: stale);
            await copied.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());
            Assert.That(clipboard.LastCopiedText, Is.EqualTo((stale ? "Previous result — current inputs have changed.\n" : "") + ExpectedGermanSummary()));

            copied.Dispose();
            clipboard.Copy = _ => Task.FromResult(false);
            var fallback = Render(result, handSize: 5, isStale: stale);
            await fallback.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());

            Assert.That(fallback.Find("textarea[readonly]").TextContent, Is.EqualTo((stale ? "Previous result — current inputs have changed.\n" : "") + ExpectedGermanSummary()));
            Assert.That(fallback.FindAll("img"), Is.Empty, "summary labels remain plain text in the textarea");
            Assert.That(fallback.FindAll(".probability-result-copy-success-icon"), Is.Empty);
        }
        finally {
            CultureInfo.CurrentCulture = previousCulture;
        }
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
        Assert.That(cut.FindAll(".probability-result-copy-success-icon"), Is.Empty);

        await cut.Find("[aria-label='Close copy fallback']").ClickAsync(new MouseEventArgs());

        Assert.That(cut.FindAll("textarea[readonly]"), Is.Empty);
        Assert.That(clipboard.FocusAttempts, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ClipboardExceptionsAndUnavailableJavaScriptBothRevealFallback(bool stale) {
        clipboard.FailCopy = true;
        var failedCopy = Render(Result(0.42, "Denied"), handSize: 5, isStale: stale);
        await failedCopy.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());
        Assert.That(failedCopy.Find("textarea[readonly]").TextContent, Does.Contain("Denied"));
        Assert.That(failedCopy.FindAll(".probability-result-copy-success-icon"), Is.Empty);

        failedCopy.Dispose();
        clipboard.FailCopy = false;
        clipboard.FailImport = true;
        var unavailableClipboard = Render(Result(0.35, "Unavailable"), handSize: 4, isStale: stale);
        await unavailableClipboard.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());
        Assert.That(unavailableClipboard.Find("textarea[readonly]").TextContent, Does.Contain("Unavailable"));
        Assert.That(unavailableClipboard.FindAll(".probability-result-copy-success-icon"), Is.Empty);
        Assert.That(clipboard.CopyAttempts, Is.EqualTo(1));
    }

    [Test]
    public async Task BusyCopyDescriptionStaysNonvisualAndStaleCopyRemainsAvailable() {
        var result = Result(0.42, "Not current");
        var cut = Render(result);
        const string buttonSelector = "button[title='Copy a summary of these results']";
        var button = cut.Find(buttonSelector);

        Assert.That(button.HasAttribute("disabled"), Is.False);
        Assert.That(button.TextContent.Trim(), Is.EqualTo("Copy results"));
        await button.ClickAsync(new MouseEventArgs());
        Assert.That(clipboard.CopyAttempts, Is.EqualTo(1));
        Assert.That(cut.FindAll(".probability-result-copy-success-icon"), Has.Count.EqualTo(1));

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.IsCalculating, true));

        button = cut.Find(buttonSelector);
        AssertDisabledDescription(cut, button, "Wait for the calculation to finish before copying results.");
        Assert.That(button.TextContent.Trim(), Is.EqualTo("Copy results"));
        Assert.That(cut.FindAll(".probability-result-copy-success-icon"), Is.Empty);
        await button.ClickAsync(new MouseEventArgs());
        Assert.That(clipboard.CopyAttempts, Is.EqualTo(1));

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.IsCalculating, false));

        button = cut.Find(buttonSelector);
        Assert.That(button.HasAttribute("disabled"), Is.False);
        Assert.That(cut.FindAll(".probability-result-export-toolbar > small:not(.visually-hidden)"), Is.Empty);

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.IsStale, true));

        button = cut.Find(buttonSelector);
        Assert.That(button.HasAttribute("disabled"), Is.False);
        await button.ClickAsync(new MouseEventArgs());
        Assert.That(clipboard.CopyAttempts, Is.EqualTo(2));
        Assert.That(clipboard.LastCopiedText, Does.StartWith("Previous result — current inputs have changed.\nProbability results\nHand size: 5"));

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.Result, Result(0.87, "Recalculated"))
            .Add(component => component.IsStale, false));

        button = cut.Find(buttonSelector);
        Assert.That(button.HasAttribute("disabled"), Is.False);
        Assert.That(button.TextContent.Trim(), Is.EqualTo("Copy results"));
        Assert.That(cut.FindAll(".probability-result-export-toolbar > small:not(.visually-hidden)"), Is.Empty);

        var empty = Render(null);
        Assert.That(empty.FindAll("button"), Is.Empty);
        Assert.That(clipboard.CopyAttempts, Is.EqualTo(2));
        var unknown = Render(result, handSize: 0, isStale: true);
        AssertDisabledDescription(unknown, unknown.Find(buttonSelector), "The original result context is unavailable.");
    }

    private static void AssertDisabledDescription(
        IRenderedComponent<ProbabilityResultExport> cut,
        AngleSharp.Dom.IElement button,
        string expectedDescription) {
        Assert.That(button.HasAttribute("disabled"), Is.True);
        Assert.That(button.TextContent.Trim(), Is.EqualTo("Copy results"));

        var descriptionId = button.GetAttribute("aria-describedby");
        Assert.That(descriptionId, Is.Not.Null.And.Not.Empty);
        var description = cut.Find("#" + descriptionId);
        Assert.That(description.TagName, Is.EqualTo("SMALL"));
        Assert.That(description.GetAttribute("class"), Is.EqualTo("visually-hidden"));
        Assert.That(description.TextContent.Trim(), Is.EqualTo(expectedDescription));
        Assert.That(cut.FindAll(".probability-result-export-toolbar > small:not(.visually-hidden)"), Is.Empty,
            "the unavailable description is retained for assistive technology without a visible toolbar item");
    }

    [TestCase(true)] [TestCase(false)]
    public async Task DelayedStaleCopyKeepsCapturedBytesAndFeedbackForTheSameSnapshot(bool copied) {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboard.Copy = _ => pending.Task;
        var cut = Render(Result(.42, "Old route"), handSize: 5, isStale: true);
        var action = cut.Find("button").ClickAsync(new());
        var probability = .42.ToString("P2", CultureInfo.CurrentCulture);
        var expected = $"Previous result — current inputs have changed.\nProbability results\nHand size: 5\nAny active combo: {probability}\n\nIndividual combos:\n- **Old route** — {probability}";
        Assert.That(clipboard.LastCopiedText, Is.EqualTo(expected));
        // Re-rendering an unchanged historical snapshot must not invalidate its copy feedback.
        cut.SetParametersAndRender(p => p.Add(x => x.IsStale, true));
        pending.SetResult(copied); await action;
        if (copied) Assert.That(cut.FindAll(".probability-result-copy-success-icon"), Has.Count.EqualTo(1));
        else Assert.That(cut.Find("textarea[readonly]").TextContent, Is.EqualTo(expected));
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
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.False);

        pendingCopy.SetResult(copied);
        await clickTask;

        Assert.That(cut.FindAll("[role='status']"), Is.Empty);
        Assert.That(cut.FindAll("textarea[readonly]"), Is.Empty);
        Assert.That(clipboard.LastCopiedText, Does.Not.Contain("Hand size: 6"));
        Assert.That(cut.FindAll(".probability-result-copy-success-icon"), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ANewerResultClearsPreviousCopyFallback(bool stale) {
        clipboard.Copy = _ => Task.FromResult(false);
        var cut = Render(Result(0.42, "Old result"), handSize: 5, isStale: stale);
        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());
        Assert.That(cut.Find("textarea[readonly]").TextContent, Does.Contain("Old result"));

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.Result, Result(0.87, "New result")));

        Assert.That(cut.FindAll("textarea[readonly]"), Is.Empty);
        Assert.That(cut.FindAll("[role='status']"), Is.Empty);
        Assert.That(cut.Find("button[title='Copy a summary of these results']").HasAttribute("disabled"), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SuccessfulFeedbackResetsAndARepeatedCopyRestartsItsInterval(bool stale) {
        var clock = new ManualTimeProvider();
        var cut = Render(Result(0.42, "Repeated result"), feedbackTimeProvider: clock, isStale: stale);
        var buttonSelector = "button[title='Copy a summary of these results']";

        await cut.Find(buttonSelector).ClickAsync(new MouseEventArgs());
        Assert.That(cut.FindAll(".probability-result-copy-success-icon").Count, Is.EqualTo(1));

        clock.Advance(TimeSpan.FromMilliseconds(1500));
        await cut.Find(buttonSelector).ClickAsync(new MouseEventArgs());
        Assert.That(cut.FindAll(".probability-result-copy-success-icon").Count, Is.EqualTo(1));

        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.That(cut.FindAll(".probability-result-copy-success-icon").Count, Is.EqualTo(1),
            "the first timeout must not erase the restarted success feedback");

        clock.Advance(TimeSpan.FromMilliseconds(1450));
        cut.WaitForAssertion(
            () => Assert.That(cut.FindAll(".probability-result-copy-success-icon"), Is.Empty),
            TimeSpan.FromSeconds(2));
        Assert.That(cut.Find("button[title='Copy a summary of these results']").TextContent.Trim(), Is.EqualTo("Copy results"));
    }

    [Test]
    public async Task AFailedRetryClearsEarlierSuccessAndShowsTheExistingFallback() {
        var copyCount = 0;
        clipboard.Copy = _ => Task.FromResult(++copyCount == 1);
        var cut = Render(Result(0.42, "Retry route"));
        var buttonSelector = "button[title='Copy a summary of these results']";

        await cut.Find(buttonSelector).ClickAsync(new MouseEventArgs());
        Assert.That(cut.FindAll(".probability-result-copy-success-icon").Count, Is.EqualTo(1));

        await cut.Find(buttonSelector).ClickAsync(new MouseEventArgs());

        Assert.That(cut.FindAll(".probability-result-copy-success-icon"), Is.Empty);
        Assert.That(cut.Find("textarea[readonly]").TextContent, Does.Contain("Retry route"));
        Assert.That(cut.FindAll("[role='status']"), Is.Empty);
        Assert.That(cut.Find(buttonSelector).TextContent.Trim(), Is.EqualTo("Copy results"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AnOlderPendingCopyCannotOverrideANewerFailedAttempt(bool stale) {
        var earlierCopy = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var copyCount = 0;
        clipboard.Copy = _ => ++copyCount == 1 ? earlierCopy.Task : Task.FromResult(false);
        var cut = Render(Result(0.42, "Newest attempt"), isStale: stale);
        var buttonSelector = "button[title='Copy a summary of these results']";

        var earlierClick = cut.Find(buttonSelector).ClickAsync(new MouseEventArgs());
        await cut.Find(buttonSelector).ClickAsync(new MouseEventArgs());
        var expectedFallback = cut.Find("textarea[readonly]").TextContent;
        Assert.That(expectedFallback, Does.Contain("Newest attempt"));

        earlierCopy.SetResult(true);
        await earlierClick;

        Assert.That(cut.FindAll(".probability-result-copy-success-icon"), Is.Empty);
        Assert.That(cut.Find("textarea[readonly]").TextContent, Is.EqualTo(expectedFallback));
    }

    [Test]
    public async Task ChangingInputsOrAcceptedResultClearsVisibleSuccess() {
        var changedInputs = Render(Result(0.42, "Previous inputs"), handSize: 5);
        await changedInputs.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());
        changedInputs.SetParametersAndRender(parameters => parameters
            .Add(component => component.HandSize, 6)
            .Add(component => component.IsStale, true));
        Assert.That(changedInputs.FindAll(".probability-result-copy-success-icon"), Is.Empty);
        Assert.That(changedInputs.FindAll("[role='status']"), Is.Empty);

        var changedResult = Render(Result(0.42, "Previous result"));
        await changedResult.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());
        changedResult.SetParametersAndRender(parameters => parameters
            .Add(component => component.Result, Result(0.87, "New accepted result")));
        Assert.That(changedResult.FindAll(".probability-result-copy-success-icon"), Is.Empty);
        Assert.That(changedResult.FindAll("[role='status']"), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisposingTheComponentCancelsItsPendingFeedbackReset(bool stale) {
        var clock = new ManualTimeProvider();
        var cut = Render(Result(0.42, "Dispose reset"), feedbackTimeProvider: clock, isStale: stale);
        await cut.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());
        Assert.That(clock.ActiveTimerCount, Is.EqualTo(1));

        await cut.Instance.DisposeAsync();
        Assert.That(clock.ActiveTimerCount, Is.Zero);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.That(clock.CallbackCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisposingDuringClipboardWriteDiscardsItsLateSuccess(bool stale) {
        var pendingCopy = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboard.Copy = _ => pendingCopy.Task;
        var cut = Render(Result(0.42, "Disposed write"), isStale: stale);
        var clickTask = cut.Find("button[title='Copy a summary of these results']").ClickAsync(new MouseEventArgs());

        await cut.Instance.DisposeAsync();
        pendingCopy.SetResult(true);
        await clickTask;

        Assert.That(clipboard.CopyAttempts, Is.EqualTo(1));
        Assert.That(clickTask.IsCompletedSuccessfully, Is.True);
        Assert.That(cut.FindAll(".probability-result-copy-success-icon"), Is.Empty);
        Assert.That(cut.FindAll("textarea[readonly]"), Is.Empty);
    }

    private IRenderedComponent<ProbabilityResultExport> Render(
        ProbabilityCalculationResult? result,
        int handSize = 5,
        bool isStale = false,
        bool isCalculating = false,
        TimeProvider? feedbackTimeProvider = null) =>
        context.RenderComponent<ProbabilityResultExport>(parameters => {
            parameters
                .Add(component => component.Result, result)
                .Add(component => component.HandSize, handSize)
                .Add(component => component.IsStale, isStale)
                .Add(component => component.IsCalculating, isCalculating);
            if (feedbackTimeProvider is not null)
                parameters.Add(component => component.FeedbackTimeProvider, feedbackTimeProvider);
        });

    private static ProbabilityCalculationResult GermanExampleResult() => new(
        0.8361,
        [
            new ComboProbabilityResult(0, "VS Starter + Fire", 0.2605, "vs"),
            new ComboProbabilityResult(1, "K9 Starter + Lv. 5", 0.4802, "k9"),
            new ComboProbabilityResult(2, "Izuna + Sue + 2 x Lv. 5", 0.025, "k9")
        ],
        [
            new GroupProbabilityResult("vs", "Full VS", 0.6107, 6),
            new GroupProbabilityResult("k9", "Full K9", 0.4884, 3)
        ]);

    private static string ExpectedGermanSummary() => string.Join('\n', new[] {
        "Probability results",
        "Hand size: 5",
        "Any active combo: 83,61 %",
        "",
        "Group probabilities:",
        "- **Full VS** — 61,07 % (6 active)",
        "  - **VS Starter + Fire** — 26,05 %",
        "- **Full K9** — 48,84 % (3 active)",
        "  - **K9 Starter + Lv. 5** — 48,02 %",
        "  - **Izuna + Sue + 2 x Lv. 5** — 2,50 %"
    });

    private static ProbabilityCalculationResult Result(double total, string comboName) =>
        new(total, [new ComboProbabilityResult(0, comboName, total)]);

    private static int Occurrences(string value, string search) =>
        value.Split(search, StringSplitOptions.None).Length - 1;

    private sealed class ManualTimeProvider : TimeProvider {
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset utcNow = DateTimeOffset.UnixEpoch;

        public int ActiveTimerCount => timers.Count(timer => timer.IsActive);
        public int CallbackCount { get; private set; }

        public override DateTimeOffset GetUtcNow() => utcNow;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan amount) {
            if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(amount));

            utcNow += amount;
            foreach (var timer in timers.ToArray()) {
                if (!timer.FireIfDue(utcNow)) continue;
                CallbackCount++;
            }
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer {
            private DateTimeOffset? dueAt;
            private TimeSpan period;
            private bool disposed;

            public bool IsActive => !disposed && dueAt.HasValue;

            public bool Change(TimeSpan dueTime, TimeSpan period) {
                if (disposed) return false;
                if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                    throw new ArgumentOutOfRangeException(nameof(dueTime));
                if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
                    throw new ArgumentOutOfRangeException(nameof(period));

                this.period = period;
                dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner.utcNow + dueTime;
                return true;
            }

            public void Dispose() {
                disposed = true;
                dueAt = null;
            }

            public ValueTask DisposeAsync() {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public bool FireIfDue(DateTimeOffset now) {
                if (!IsActive || dueAt > now) return false;

                if (period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero) dueAt = null;
                else dueAt += period;

                callback(state);
                return true;
            }
        }
    }

    private sealed class ClipboardInterop : IJSRuntime {
        private readonly ClipboardModule module;

        public ClipboardInterop() => module = new ClipboardModule(this);

        public Func<string, Task<bool>> Copy { get; set; } = _ => Task.FromResult(true);
        public bool FailImport { get; set; }
        public bool FailCopy { get; set; }
        public Task? ImportDelay { get; set; }
        public TaskCompletionSource ModuleDisposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposalCount { get; private set; }
        public int CopyAttempts { get; private set; }
        public int FocusAttempts { get; private set; }
        public string? LastCopiedText { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) {
            if (identifier == "import") {
                if (FailImport)
                    return ValueTask.FromException<TValue>(new JSException("Module load failed."));

                return new ValueTask<TValue>(ImportAsync<TValue>());
            }

            return ValueTask.FromException<TValue>(new JSException("Unexpected JavaScript invocation."));
        }

        private async Task<TValue> ImportAsync<TValue>() {
            if (ImportDelay is not null) await ImportDelay;
            return (TValue)(object)module;
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

            public ValueTask DisposeAsync() {
                owner.DisposalCount++;
                owner.ModuleDisposed.TrySetResult();
                return ValueTask.CompletedTask;
            }
        }
    }
}
