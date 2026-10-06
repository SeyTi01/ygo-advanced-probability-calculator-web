using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CardArtworkTest
{
    private Bunit.TestContext context = null!;
    private Mock<ICardArtworkService> service = null!;

    [SetUp]
    public void SetUp()
    {
        context = new();
        service = new();
        context.Services.AddSingleton(service.Object);
        context.JSInterop.SetupModule("./js/card-artwork.mjs").Setup<int>("observe", _ => true).SetResult(7);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TearDown]
    public void TearDown() => context.Dispose();

    private IRenderedComponent<CardArtwork> Render(int id = 1234) => context.RenderComponent<CardArtwork>(p =>
        p
            .Add(x => x.CardId, "row")
            .Add(x => x.ExternalCardId, id)
            .Add(x => x.Name, "Card")
            .Add(x => x.Thumbnail, true)
    );

    private static string Url(int id) => $"{CardArtworkService.ArtworkOrigin}/small/{id}.jpg";

    [Test]
    public async Task ObserverDefersMetadataAndAutomaticallyDisplaysDecorativeThumbnail()
    {
        service.Setup(x => x.GetArtworkUrlAsync(1234)).ReturnsAsync(Url(1234));
        IRenderedComponent<CardArtwork> cut = Render();
        service.Verify(x => x.GetArtworkUrlAsync(It.IsAny<int>()), Times.Never);
        Assert.That(cut.FindAll("button"), Is.Empty);
        Assert.That(cut.Find(".card-artwork-thumbnail svg"), Is.Not.Null);
        CardArtwork.ArtworkResolution
            result = await cut.Instance.ResolveArtwork(1); // The real JS observer invokes this bridge.
        await cut.InvokeAsync(() => cut.Instance.ArtworkReady(1, result.Url));
        Assert.That(cut.Find("img").GetAttribute("src"), Is.EqualTo(Url(1234)));
        Assert.That(cut.Find("img").GetAttribute("alt"), Is.Empty);
    }

    [Test]
    public async Task IdentityChangeDiscardsLateMetadataAndDisplayCallbacks()
    {
        TaskCompletionSource<string?> pending = new();
        service.Setup(x => x.GetArtworkUrlAsync(1234)).Returns(pending.Task);
        service.Setup(x => x.GetArtworkUrlAsync(5678)).ReturnsAsync(Url(5678));
        IRenderedComponent<CardArtwork> cut = Render();
        Task<CardArtwork.ArtworkResolution> resolving = cut.Instance.ResolveArtwork(1);
        cut.SetParametersAndRender(p => p.Add(x => x.CardId, "replacement").Add(x => x.ExternalCardId, 5678));
        pending.SetResult(Url(1234));
        CardArtwork.ArtworkResolution old = await resolving;
        await cut.InvokeAsync(() => cut.Instance.ArtworkReady(1, old.Url));
        Assert.That(cut.FindAll("img"), Is.Empty);
        CardArtwork.ArtworkResolution result = await cut.Instance.ResolveArtwork(2);
        await cut.InvokeAsync(() => cut.Instance.ArtworkReady(2, result.Url));
        Assert.That(cut.Find("img").GetAttribute("src"), Is.EqualTo(Url(5678)));
        service.Verify(x => x.GetArtworkUrlAsync(5678), Times.Once);
    }

    [Test]
    public async Task RemovedConsumerCannotDisplayLateImageOrResolveAgain()
    {
        IRenderedComponent<CardArtwork> cut = Render();
        CardArtwork instance = cut.Instance;
        await instance.DisposeAsync();
        Assert.That((await instance.ResolveArtwork(1)).Url, Is.Null);
        await instance.ArtworkReady(1, Url(1234));
        service.Verify(x => x.GetArtworkUrlAsync(It.IsAny<int>()), Times.Never);
        Assert.That(cut.FindAll("img"), Is.Empty);
    }

    [TestCase(0)]
    [TestCase(1234)]
    public async Task MissingArtworkKeepsAlignedQuietFootprint(int id)
    {
        service.Setup(x => x.GetArtworkUrlAsync(1234)).ReturnsAsync((string?)null);
        IRenderedComponent<CardArtwork> cut = Render(id);
        await cut.InvokeAsync(() => cut.Instance.ArtworkReady(1, null));
        Assert.That(cut.FindAll(".card-artwork"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll("img, button, [role='status']"), Is.Empty);
        service.Verify(x => x.GetArtworkUrlAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task TransientMetadataFailureExposesRetryAfterAndProviderUrlsAreRejected()
    {
        service
            .Setup(x => x.GetArtworkUrlAsync(1234))
            .ThrowsAsync(new CardArtworkLookupException(TimeSpan.FromSeconds(180)));
        IRenderedComponent<CardArtwork> cut = Render();
        CardArtwork.ArtworkResolution result = await cut.Instance.ResolveArtwork(1);
        Assert.That(result.RetryAfter, Is.EqualTo(180000));
        await cut.InvokeAsync(() =>
            cut.Instance.ArtworkReady(1, "https://images.ygoprodeck.com/images/cards_small/1234.jpg")
        );
        Assert.That(cut.FindAll("img"), Is.Empty);
    }
}
