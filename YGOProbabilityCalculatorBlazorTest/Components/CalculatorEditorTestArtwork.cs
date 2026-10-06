using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorTestArtwork : CalculatorEditorTestBase
{
    [Test]
    public async Task AutomaticArtworkKeepsResultsDraftsActiveStateAndSessionUnchanged()
    {
        Mock<ICardArtworkService> artwork = new();
        artwork
            .Setup(x => x.GetArtworkUrlAsync(1234))
            .ReturnsAsync(CardArtworkService.ArtworkOrigin + "/small/1234.jpg");
        context.Services.AddSingleton(artwork.Object);
        SessionState session = Session();
        Card original = session.Cards[0];
        session.Cards[0] = new Card(
            original.Categories,
            original.Copies,
            original.Name,
            false,
            original.Id,
            1234
        );
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        Assert.That(cut.FindAll("img"), Is.Empty);
        artwork.Verify(x => x.GetArtworkUrlAsync(It.IsAny<int>()), Times.Never);
        await editor.Find(".accordion-button").ClickAsync(new());
        artwork.Verify(x => x.GetArtworkUrlAsync(It.IsAny<int>()), Times.Never);
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = b.Identity });
        await Button(cut, "Calculate").ClickAsync(new());
        string results = cut.Find(".probability-results").OuterHtml;
        await Button(cut, "Save Session").ClickAsync(new());
        string before = SavedSessionJson();
        IRenderedComponent<CardArtwork> thumbnail = editor.FindComponents<CardArtwork>().First();
        CardArtwork.ArtworkResolution resolved = await thumbnail.Instance.ResolveArtwork(1);
        await thumbnail.InvokeAsync(() => thumbnail.Instance.ArtworkReady(1, resolved.Url));
        IRenderedComponent<CardArtwork> preview = editor.FindComponents<CardArtwork>().Last();
        await preview.InvokeAsync(() => preview.Instance.ArtworkReady(1, resolved.Url));
        Assert.That(
            editor.Find("img").GetAttribute("src"),
            Is.EqualTo(CardArtworkService.ArtworkOrigin + "/small/1234.jpg")
        );
        Assert.That(
            editor.Find(".card-artwork-preview img").GetAttribute("alt"),
            Does.Contain("First")
        );
        Assert.That(cut.Find(".probability-results").OuterHtml, Is.EqualTo(results));
        Assert.That(editor.Instance.Card.Active, Is.False);
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo(b.Identity));
        await Button(cut, "Save Session").ClickAsync(new());
        Assert.That(SavedSessionJson(1), Is.EqualTo(before));
        Assert.That(
            before,
            Does.Not.Contain("image").And.Not.Contain("Artwork").And.Not.Contain("blob:")
        );
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(
            cut.FindAll("img"),
            Has.Count.EqualTo(1),
            "The collapsed thumbnail remains visible"
        );
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(editor.FindAll("img"), Has.Count.EqualTo(1));
        artwork.Verify(x => x.GetArtworkUrlAsync(1234), Times.Once);
    }

    [Test]
    public async Task ArtworkFollowsStableEditorThroughReorderingAndDisappearsOnDeletion()
    {
        Mock<ICardArtworkService> artwork = new();
        artwork
            .Setup(x => x.GetArtworkUrlAsync(1234))
            .ReturnsAsync(CardArtworkService.ArtworkOrigin + "/small/1234.jpg");
        context.Services.AddSingleton(artwork.Object);
        SessionState session = Session();
        Card card = session.Cards[0];
        session.Cards[0] = new Card(
            card.Categories,
            card.Copies,
            card.Name,
            card.Active,
            card.Id,
            1234
        );
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = b.Identity });
        IRenderedComponent<CardArtwork> thumbnail = editor.FindComponents<CardArtwork>().First();
        CardArtwork.ArtworkResolution resolved = await thumbnail.Instance.ResolveArtwork(1);
        await thumbnail.InvokeAsync(() => thumbnail.Instance.ArtworkReady(1, resolved.Url));
        await editor.Find("[aria-label='Move card First, row 1 down']").ClickAsync(new());
        Assert.That(cut.FindComponents<CardEditor>()[1].Instance, Is.SameAs(editor.Instance));
        Assert.That(editor.Find("#cardCategory1").GetAttribute("value"), Is.EqualTo(b.Identity));
        Assert.That(editor.Find("img").GetAttribute("src"), Does.EndWith("/small/1234.jpg"));
        await editor.Find("[aria-label='Remove card']").ClickAsync(new());
        Assert.That(cut.FindAll("img"), Is.Empty);
        Assert.That(
            cut.FindComponents<CardEditor>().Single().Instance.Card.Name,
            Is.EqualTo("Second")
        );
        artwork.Verify(x => x.GetArtworkUrlAsync(1234), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualRemovalOrSessionReplacementDiscardsLateArtwork(bool replaceSession)
    {
        TaskCompletionSource<string?> pending = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Mock<ICardArtworkService> artwork = new();
        artwork.Setup(x => x.GetArtworkUrlAsync(1234)).Returns(pending.Task);
        artwork
            .Setup(x => x.GetArtworkUrlAsync(5678))
            .ReturnsAsync(CardArtworkService.ArtworkOrigin + "/small/5678.jpg");
        context.Services.AddSingleton(artwork.Object);
        SessionState session = Session();
        Card card = session.Cards[0];
        session.Cards[0] = new Card(
            card.Categories,
            card.Copies,
            card.Name,
            card.Active,
            card.Id,
            1234
        );
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        IRenderedComponent<CardEditor> editor = cut.FindComponents<CardEditor>()[0];
        CardArtwork old = editor.FindComponents<CardArtwork>().First().Instance;
        Task<CardArtwork.ArtworkResolution> lookup = old.ResolveArtwork(1);

        if (replaceSession)
        {
            const string replacement = """
                {"SchemaVersion":2,"Categories":[],"Cards":[{"Categories":[],"Copies":1,"Name":"Replacement",
                 "Id":"replacement","ExternalCardId":5678}],"Combos":[],"HandSize":5}
                """;
            cut.FindComponents<InputFile>()[1]
                .UploadFiles(InputFileContent.CreateFromText(replacement, "replacement.json"));
            cut.WaitForState(() =>
                cut.FindComponents<CardEditor>().Count == 1
                && cut.FindComponent<CardEditor>().Instance.Card.Name == "Replacement"
            );
        }
        else
        {
            await editor.Find("[aria-label='Remove card']").ClickAsync(new());
        }

        pending.SetResult(CardArtworkService.ArtworkOrigin + "/small/1234.jpg");
        CardArtwork.ArtworkResolution result = await lookup;
        await cut.InvokeAsync(() => old.ArtworkReady(1, result.Url));
        Assert.That(cut.Markup, Does.Not.Contain("1234.jpg"));

        if (replaceSession)
        {
            IRenderedComponent<CardArtwork> next = cut.FindComponent<CardArtwork>();
            CardArtwork.ArtworkResolution nextResult = await next.Instance.ResolveArtwork(1);
            await next.InvokeAsync(() => next.Instance.ArtworkReady(1, nextResult.Url));
            Assert.That(next.Find("img").GetAttribute("src"), Does.EndWith("/small/5678.jpg"));
        }
    }
}
