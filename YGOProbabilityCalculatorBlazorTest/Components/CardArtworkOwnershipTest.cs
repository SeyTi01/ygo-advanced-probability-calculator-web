using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CardArtworkOwnershipTest {
    [TestCase(false)]
    [TestCase(true)]
    public async Task ImageCallbackPreservesBusyCalculationAcceptedResultDraftAndRecoverySnapshot(bool success) {
        using var context = new Bunit.TestContext(); context.JSInterop.Mode = JSRuntimeMode.Loose;
        var jobs = new List<TaskCompletionSource<ProbabilityCalculationResult>>(); var tokens = new List<CancellationToken>();
        var calculator = new Mock<IBackgroundCalculator>();
        calculator.Setup(x => x.CalculateAsync(It.IsAny<CalculationSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns((CalculationSnapshot _, CancellationToken token) => {
                var pending = new TaskCompletionSource<ProbabilityCalculationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                jobs.Add(pending); tokens.Add(token); return pending.Task;
            });
        context.Services.AddSingleton(calculator.Object);
        context.Services.AddSingleton<ISerializer, JsonSerializer>(); context.Services.AddSingleton<ISessionService, SessionService>();
        context.Services.AddSingleton(Mock.Of<IDeckImportService>()); context.Services.AddSingleton(Mock.Of<ILegacyCardMetadataEnricher>());
        context.Services.AddSingleton(Mock.Of<ICardArtworkService>());
        var category = new CategoryBase("Draft category");
        var session = new SessionState { Cards = [new([], 4, "Imported", id: "a", externalCardId: 1234), new([], 4, "Manual", id: "b")],
            Categories = [category], Combos = [new([], "Route", cards: [new("a", 1, 5)])], HandSize = 2 };
        context.Services.AddSingleton<IPendingSessionService>(new PendingSessionService { PendingSession = session });
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        var accepted = cut.Find(".calculate-action > button").ClickAsync(new());
        jobs[0].SetResult(new(0.5, [new(0, "Route", 0.5)])); await accepted;
        var editor = cut.FindComponents<CardEditor>()[0]; await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = category.Identity });
        var second = cut.Find(".calculate-action > button").ClickAsync(new());
        var resultMarkup = cut.Find(".probability-results").OuterHtml;
        var recovery = context.JSInterop.Invocations["sessionRecovery.update"].Select(x => (string)x.Arguments[2]!).ToArray();
        var bytes = context.Services.GetRequiredService<ISessionService>().SerializeSession(session);
        var thumbnail = editor.FindComponents<CardArtwork>().First();
        await thumbnail.InvokeAsync(() => thumbnail.Instance.ArtworkReady(1, success ? CardArtworkService.ArtworkOrigin + "/small/1234.jpg" : null));
        Assert.That(tokens[1].IsCancellationRequested, Is.False);
        Assert.That(cut.FindAll("[aria-label='Cancel calculation']"), Has.Count.EqualTo(1));
        Assert.That(cut.Find(".probability-results").OuterHtml, Is.EqualTo(resultMarkup));
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo(category.Identity));
        Assert.That(context.Services.GetRequiredService<ISessionService>().SerializeSession(session), Is.EqualTo(bytes));
        Assert.That(context.JSInterop.Invocations["sessionRecovery.update"].Select(x => (string)x.Arguments[2]!), Is.EqualTo(recovery));
        jobs[1].SetResult(new(0.5, [new(0, "Route", 0.5)])); await second;
    }
}
