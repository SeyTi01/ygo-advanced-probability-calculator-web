using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorTestCardProperties : CalculatorEditorTestBase {

    [TestCase(false)]
    [TestCase(true)]
    public async Task ManualAndHelpPendingLegacySessionsAwaitEnrichmentAndSaveProperties(bool fromHelp) {
        const string legacy = """
            {"Categories":[{"Name":"Monster"}],
             "Cards":[{"Id":"ash","Name":"Ash Blossom & Joyous Spring","Copies":2,"Active":true,"Categories":[{"Name":"Monster"}]},
                      {"Id":"custom","Name":"My Custom Card","Copies":1,"Active":true,"Categories":[]}],
             "Combos":[{"Name":"Existing route","Categories":[{"BaseCategory":{"Name":"Monster"},"MinCount":1,"MaxCount":1}]}],"HandSize":1}
            """;
        var release = new TaskCompletionSource<IReadOnlyDictionary<string, CardInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cardInfo = new Mock<ICardInfoService>();
        cardInfo.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>())).Returns(release.Task);
        context.Services.AddSingleton(cardInfo.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut;
        Task? upload = null;
        if (fromHelp) {
            var files = new Mock<IFileService>();
            files.Setup(service => service.ReadAllTextAsync("sample-data/example_session_state.json")).ReturnsAsync(legacy);
            context.Services.AddSingleton(files.Object);
            var help = context.RenderComponent<YGOProbabilityCalculatorBlazor.Pages.Help>();
            await Button(help, "Try It with Example Data").ClickAsync(new());
            Assert.That(context.Services.GetRequiredService<IPendingSessionService>().PendingSession, Is.Not.Null);
            cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        }
        else {
            cut = Render();
            var file = new Mock<IBrowserFile>();
            file.Setup(file => file.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(legacy)));
            upload = cut.InvokeAsync(() => cut.FindComponents<InputFile>()[1].Instance.OnChange
                .InvokeAsync(new InputFileChangeEventArgs([file.Object])));
        }
        cut.WaitForAssertion(() => cardInfo.Verify(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()), Times.Once));
        Assert.That(cut.FindComponents<CardEditor>(), Is.Empty, "Restoration waits for the asynchronous best-effort step.");
        await cut.InvokeAsync(() => release.SetResult(new Dictionary<string, CardInfo>(StringComparer.Ordinal) {
            ["Ash Blossom & Joyous Spring"] = new() { Id = 14558127, Name = "Ash Blossom & Joyous Spring", Type = "Tuner Monster", Attribute = "FIRE", Race = "Zombie", Level = 3 }
        }));
        if (upload is not null) await upload;
        cut.WaitForAssertion(() => Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(2)));
        cardInfo.Verify(service => service.GetCardInfoByExactNamesAsync(It.Is<IEnumerable<string>>(names =>
            names.SequenceEqual(new[] { "Ash Blossom & Joyous Spring", "My Custom Card" }))), Times.Once);
        var cards = cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card).ToList();
        Assert.That(cards[0].ExternalCardId, Is.EqualTo(14558127));
        Assert.That(cards[1].ExternalCardId, Is.Null);
        Assert.That(cards[1].Categories, Is.Empty);
        Assert.That(cards.Select(card => card.Id), Is.EqualTo(new[] { "ash", "custom" }));
        Assert.That(cut.FindComponent<CategoryListEditor>().FindAll(".category-chip").Select(chip => chip.TextContent.Trim()), Is.EqualTo(new[] { "Monster" }));
        var combo = cut.FindComponent<ComboEditor>();
        Assert.That(combo.FindAll("optgroup[label='Card properties'] option").Select(option => option.TextContent), Does.Contain("Tuner Monster"));
        Assert.That(combo.Instance.Combo.Categories.Single().BaseCategory.Source, Is.EqualTo(CategorySource.User));
        var expected = SmallDeckOracle.EnumerateProbability(cards, [combo.Instance.Combo], 1);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(expected.ToString("P2")));
        await Button(cut, "Save Session").ClickAsync(new());
        var invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        var saved = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        using var document = System.Text.Json.JsonDocument.Parse(saved);
        Assert.That(document.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(document.RootElement.GetProperty("Cards")[0].GetProperty("ExternalCardId").GetInt32(), Is.EqualTo(14558127));
        cardInfo.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>())).ThrowsAsync(new HttpRequestException("Offline"));
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(saved, "saved.json"));
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".probability-results"), Is.Empty));
        cardInfo.Verify(service => service.GetCardInfoByExactNamesAsync(It.Is<IEnumerable<string>>(names =>
            names.SequenceEqual(new[] { "My Custom Card" }))), Times.Once);
        Assert.That(cut.FindComponents<CardEditor>()[0].Instance.Card.ExternalCardId, Is.EqualTo(14558127));
        Assert.That(cut.FindAll("[role='alert']"), Is.Empty);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(expected.ToString("P2")));
    }

    [Test]
    public async Task BundledHelpExampleTransfersSelfContainedPropertiesWithoutMetadataLookup() {
        var source = await File.ReadAllTextAsync(Path.Combine(NUnit.Framework.TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json"));
        var files = new Mock<IFileService>();
        files.Setup(service => service.ReadAllTextAsync("sample-data/example_session_state.json")).ReturnsAsync(source);
        var offline = new Mock<ICardInfoService>(MockBehavior.Strict);
        context.Services.AddSingleton(files.Object);
        context.Services.AddSingleton(offline.Object);
        var help = context.RenderComponent<YGOProbabilityCalculatorBlazor.Pages.Help>();
        await Button(help, "Try It with Example Data").ClickAsync(new());
        var cut = context.RenderComponent<ProbabilityCalculatorComponent>();
        cut.WaitForAssertion(() => Assert.That(cut.FindComponents<CardEditor>(), Has.Count.EqualTo(25)));
        Assert.That(cut.FindComponent<ComboEditor>().FindAll("optgroup[label='Card properties'] option").Select(option => option.TextContent),
            Does.Contain("Monster").And.Contain("Quick-Play Spell"));
        Assert.That(context.Services.GetRequiredService<IPendingSessionService>().PendingSession, Is.Null);
        offline.VerifyNoOtherCalls();
    }

    [Test]
    public async Task CardPropertiesAreHiddenAndSameLabelRequirementsCanBeEditedIndependently() {
        var user = new CategoryBase("Spell");
        var spell = new CategoryBase("Spell", CategorySource.Metadata, "kind:spell");
        var quick = new CategoryBase("Quick-Play Spell", CategorySource.Metadata, "spell-type:quick-play");
        var orphan = new CategoryBase("Monster", CategorySource.Metadata, "kind:monster");
        var cut = Render(new SessionState {
            Categories = [user, b], Cards = [new([spell, quick], 2, "Imported", externalCardId: 123)],
            Combos = [new([new(orphan, 0, 0)]), new([])], HandSize = 1,
            CategoryColorIndices = new() { ["Spell"] = 5, ["B"] = 2 }
        });
        var categories = cut.FindComponent<CategoryListEditor>();
        Assert.That(categories.FindAll(".category-chip"), Has.Count.EqualTo(2));
        var card = cut.FindComponent<CardEditor>();
        Assert.That(card.FindAll(".accordion-button .category-tag"), Is.Empty);
        var propertyInspector = card.Find("details.card-property-inspector");
        Assert.That(propertyInspector.HasAttribute("open"), Is.False);
        Assert.That(propertyInspector.QuerySelector("summary")!.TextContent.Trim(), Is.EqualTo("Card properties (2)"));
        Assert.That(propertyInspector.QuerySelectorAll(".detected-card-property").Select(property => property.TextContent.Trim()),
            Is.EquivalentTo(new[] { spell.Name, quick.Name }));
        Assert.That(card.Find(".accordion-button").TextContent, Does.Not.Contain(spell.Name).And.Not.Contain(quick.Name));
        Assert.That(card.FindAll("optgroup[label='User categories'] option").Select(o => o.GetAttribute("value")), Is.EqualTo(new[] { "user:Spell", "user:B" }));
        Assert.That(card.FindAll("optgroup[label='Card properties'] option").Select(o => o.GetAttribute("value")), Is.EqualTo(new[] { orphan.Identity }));
        var combo = cut.FindComponents<ComboEditor>()[1];
        Assert.That(combo.FindAll("optgroup").Select(g => g.GetAttribute("label")), Is.EqualTo(new[] { "User categories", "Card properties" }));
        Assert.That(combo.FindAll("optgroup[label='Card properties'] option").Select(o => o.TextContent),
            Is.EqualTo(new[] { "Monster", "Quick-Play Spell", "Spell" }));
        Assert.That(combo.FindAll("option").Where(o => o.TextContent == "Spell").Select(o => o.GetAttribute("value")),
            Is.EquivalentTo(new[] { "user:Spell", "metadata:kind:spell" }));

        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = spell.Identity });
        await Button(combo, "Add").ClickAsync(new());
        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = user.Identity });
        await Button(combo, "Add").ClickAsync(new());
        Assert.That(combo.Instance.Combo.Categories.Select(c => c.BaseCategory.Source),
            Is.EqualTo(new[] { CategorySource.Metadata, CategorySource.User }));
        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = spell.Identity });
        await combo.Find("#minCount1").InputAsync(new() { Value = "0" });
        await combo.Find("#maxCount1").InputAsync(new() { Value = "0" });
        await Button(combo, "Update").ClickAsync(new());
        Assert.That(combo.Instance.Combo.Categories[0].MaxCount, Is.Zero);
        Assert.That(combo.Instance.Combo.Categories[1].MinCount, Is.EqualTo(1));

        // A metadata membership does not prevent adding/removing a same-label user membership.
        await card.Find("select").ChangeAsync(new() { Value = user.Identity });
        await Button(card, "Add").ClickAsync(new());
        Assert.That(card.Instance.Card.Categories, Has.Count.EqualTo(3));
        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = spell.Identity });
        await categories.Find("[aria-label='Edit category Spell']").ClickAsync(new());
        await categories.Find("[aria-label='New name for category Spell']").InputAsync(new() { Value = "Role" });
        await categories.Find("[aria-label='Save category name']").ClickAsync(new());
        Assert.That(combo.Find("#comboCategory1").GetAttribute("value"), Is.EqualTo(spell.Identity));
        Assert.That(combo.Instance.Combo.Categories.Select(c => c.BaseCategory.Name), Is.EqualTo(new[] { "Spell", "Role" }));
        Assert.That(card.Instance.Card.Categories[0], Is.EqualTo(spell));
        Assert.That(card.Instance.Card.Categories[2].Name, Is.EqualTo("Role"));
        await card.Find("[aria-label='Remove category Role from card Imported']").ClickAsync(new());
        Assert.That(card.Instance.Card.Categories, Is.EqualTo(new[] { spell, quick }));
        Assert.That(combo.FindAll(".accordion-body .card-property-tag"), Has.Count.EqualTo(1));
        Assert.That(combo.Find(".accordion-body .card-property-tag").ClassList, Does.Contain("card-property-color-spell"));
        Assert.That(combo.FindAll(".accordion-body [class*='category-color-']"), Has.Count.EqualTo(1));

        await combo.Find("[aria-label='Remove category Role from combo Combo 2']").ClickAsync(new());
        await categories.Find("[aria-label='Remove category Role']").ClickAsync(new());
        Assert.That(categories.FindAll(".category-chip"), Has.Count.EqualTo(1));
        Assert.That(combo.Instance.Combo.Categories.Single().BaseCategory, Is.EqualTo(spell));
        await cut.Find("[aria-label='Remove card']").ClickAsync(new());
        Assert.That(combo.FindAll("optgroup[label='Card properties'] option").Select(o => o.GetAttribute("value")),
            Is.EquivalentTo(new[] { orphan.Identity, spell.Identity }));
        await combo.Find("#comboCategory1").ChangeAsync(new() { Value = spell.Identity });
        Assert.That(Button(combo, "Update"), Is.Not.Null);
    }

    [Test]
    public void CategoryEditorDoesNotExposeMetadataDefinitionsEvenIfSupplied() {
        var property = new CategoryBase("Spell", CategorySource.Metadata, "kind:spell");
        var cut = context.RenderComponent<CategoryListEditor>(p => p.Add(x => x.CategoryBases, [a, property])
            .Add(x => x.Cards, []).Add(x => x.Combos, []));
        Assert.That(cut.FindAll(".category-chip"), Has.Count.EqualTo(1));
        Assert.That(cut.FindAll("[aria-label='Edit category Spell']"), Is.Empty);
    }

    [Test]
    public void CollapsedCardHeaderShowsManualPropertiesButNotDetectedPropertiesAndDeduplicatesThem() {
        var user = new CategoryBase("VS Monster");
        var fire = new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        var monster = new CategoryBase("Monster", CategorySource.Metadata, "kind:monster");
        var card = new Card([user, fire, fire, monster], name: "Reinforcement of the Army",
            manualMetadataCategoryKeys: [fire.MetadataKey!]);
        var manualOnly = new Card([fire, fire], name: "Manual only",
            manualMetadataCategoryKeys: [fire.MetadataKey!]);
        var cut = Render(new SessionState { Categories = [user], Cards = [card, manualOnly] });

        var header = cut.FindComponents<CardEditor>()[0].Find(".accordion-button");
        Assert.That(header.QuerySelectorAll(".category-tag").Select(badge => badge.TextContent.Trim()),
            Is.EqualTo(new[] { user.Name, fire.Name }));
        Assert.That(header.QuerySelectorAll(".manual-property-header-badge").Length, Is.EqualTo(1));
        Assert.That(header.QuerySelectorAll(".category-tag").Select(badge => badge.TextContent.Trim()), Does.Not.Contain(monster.Name));
        Assert.That(cut.FindComponents<CardEditor>()[0].Find("details.card-property-inspector summary").TextContent.Trim(),
            Is.EqualTo("Card properties (2)"));

        var manualHeader = cut.FindComponents<CardEditor>()[1].Find(".accordion-button");
        Assert.That(manualHeader.QuerySelectorAll(".category-tag").Length, Is.EqualTo(1));
        Assert.That(manualHeader.QuerySelector(".card-header-name")?.TextContent.Trim(), Is.EqualTo("Manual only (1)"));
        Assert.That(manualHeader.QuerySelector(".manual-property-header-badge")?.TextContent.Trim(), Is.EqualTo(fire.Name));
    }

    [Test]
    public void ResponsiveEntryHeadersKeepActionsOutsideTheHeadingAndDoNotToggleTheAccordion() {
        var cut = Render(new SessionState {
            Categories = [a],
            Cards = [new([a], 3, "Starter")],
            Combos = [new([new ComboCategory(a, 1, 5)], "Route")]
        });

        foreach (var kind in new[] { "card", "combo" }) {
            var header = cut.Find($".{kind}-editor .entry-editor-header");
            Assert.That(header.QuerySelectorAll("h2 button").Length, Is.EqualTo(1));
            Assert.That(header.QuerySelectorAll(".entry-actions button").Length, Is.EqualTo(3));
            Assert.That(header.QuerySelectorAll("input[type=checkbox]").Length, Is.EqualTo(1));
            var toggle = header.QuerySelector("input[type=checkbox]")!;
            Assert.That(header.QuerySelector("label")?.GetAttribute("for"), Is.EqualTo(toggle.Id));
            toggle.Change(false);

            header = cut.Find($".{kind}-editor .entry-editor-header");
            Assert.That(header.QuerySelector(".accordion-button")?.GetAttribute("aria-expanded"), Is.EqualTo("false"));
            Assert.That(header.QuerySelector(".accordion-button")?.TextContent, Does.Contain("Inactive"));
            header.QuerySelector(".accordion-button")!.Click();
            Assert.That(cut.Find($".{kind}-editor .accordion-button").GetAttribute("aria-expanded"), Is.EqualTo("true"));
        }
    }

    [Test]
    public void SessionFileActionsExposeOneKeyboardAccessibleInputEach() {
        var cut = Render();
        foreach (var (id, name, extension) in new[] {
            ("fileInput", "Import YDK", ".ydk"),
            ("sessionFileInput", "Load Session", ".json")
        }) {
            var inputs = cut.FindAll($"input#{id}");
            Assert.That(inputs, Has.Count.EqualTo(1));
            var input = inputs.Single();
            Assert.That(input.GetAttribute("aria-label"), Is.EqualTo(name));
            Assert.That(input.GetAttribute("accept"), Is.EqualTo(extension));
            Assert.That(input.GetAttribute("tabindex"), Is.Not.EqualTo("-1"));
            Assert.That(input.ClassList, Does.Not.Contain("d-none"));
            Assert.That(input.ParentElement?.TagName, Is.EqualTo("LABEL"));
            Assert.That(input.ParentElement?.GetAttribute("for"), Is.EqualTo(id));
            Assert.That(input.ParentElement?.TextContent.Trim(), Is.EqualTo(name));
        }
    }

    [Test]
    public async Task CardPropertyInspectorGroupsProvenanceAndRemovesOnlyManualProperties() {
        var user = new CategoryBase("VS Monster");
        var detected = new[] {
            new CategoryBase("Monster", CategorySource.Metadata, "kind:monster"),
            new CategoryBase("Effect Monster", CategorySource.Metadata, "kind:effect-monster"),
            new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire"),
            new CategoryBase("Monster Type: Warrior", CategorySource.Metadata, "race:warrior"),
            new CategoryBase("Level 4", CategorySource.Metadata, "level:4")
        };
        var manual = new CategoryBase("Archetype: Vanquish Soul", CategorySource.Metadata, "archetype:vanquish-soul");
        var card = new Card([user, .. detected, manual], name: "Vanquish Soul Razen",
            manualMetadataCategoryKeys: [manual.MetadataKey!]);
        var cut = Render(new SessionState { Categories = [user], Cards = [card] });
        var editor = cut.FindComponent<CardEditor>();
        var inspector = editor.Find("details.card-property-inspector");

        Assert.That(inspector.HasAttribute("open"), Is.False);
        Assert.That(inspector.QuerySelector("summary")!.TextContent.Trim(), Is.EqualTo("Card properties (6)"));
        Assert.That(inspector.QuerySelector(".detected-card-properties .form-text")!.TextContent.Trim(), Is.EqualTo("Detected from card data"));
        Assert.That(inspector.QuerySelector(".manual-card-properties .form-text")!.TextContent.Trim(), Is.EqualTo("Added manually"));
        Assert.That(inspector.QuerySelectorAll(".detected-card-property").Select(property => property.TextContent.Trim()),
            Is.EquivalentTo(detected.Select(property => property.Name)));
        Assert.That(inspector.QuerySelectorAll(".detected-card-properties button.btn-close"), Is.Empty);
        Assert.That(inspector.QuerySelectorAll(".manual-card-property").Select(property => property.TextContent.Trim()),
            Is.EqualTo(new[] { manual.Name }));
        Assert.That(inspector.QuerySelector(".manual-card-property button.btn-close")!.GetAttribute("aria-label"),
            Is.EqualTo($"Remove manually added card property {manual.Name} from card {card.Name}"));

        await inspector.QuerySelector(".manual-card-property button.btn-close")!.ClickAsync(new());

        Assert.That(editor.Instance.Card.Categories, Does.Not.Contain(manual));
        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Is.Empty);
        Assert.That(editor.Find("details.card-property-inspector summary").TextContent.Trim(), Is.EqualTo("Card properties (5)"));
        Assert.That(editor.FindAll(".manual-card-properties"), Is.Empty);
        Assert.That(editor.FindAll(".manual-property-header-badge"), Is.Empty);
        Assert.That(editor.FindAll(".detected-card-properties button.btn-close"), Is.Empty);
        Assert.That(editor.Find("[aria-label='Remove category VS Monster from card Vanquish Soul Razen']"), Is.Not.Null,
            "user-category editing remains available");
    }

    [Test]
    public void ObjectiveMetadataReconciliationMovesAnOverlappingManualPropertyToDetected() {
        var fire = new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        var card = new Card([], name: "Reinforcement of the Army")
            .WithManualMetadataCategory(fire)
            .WithObjectiveMetadata([fire], 123);
        var cut = Render(new SessionState { Cards = [card] });
        var editor = cut.FindComponent<CardEditor>();
        var inspector = editor.Find("details.card-property-inspector");

        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Is.Empty);
        Assert.That(inspector.QuerySelectorAll(".detected-card-property").Select(property => property.TextContent.Trim()),
            Is.EqualTo(new[] { fire.Name }));
        Assert.That(inspector.QuerySelectorAll(".manual-card-property"), Is.Empty);
        Assert.That(inspector.QuerySelectorAll("button.btn-close"), Is.Empty);
    }

    [Test]
    public async Task ManualPropertyUpdatesPreserveTheCardEditorAndItsSelectionDraftAcrossReplacement() {
        var fire = new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        var level = new CategoryBase("Level 5", CategorySource.Metadata, "level:5");
        var originalCard = new Card([], name: "Custom");
        var cut = Render(new SessionState { Cards = [originalCard],
            Combos = [new([new(fire, 1, 1), new(level, 1, 1)])], HandSize = 1 });
        var editor = cut.FindComponent<CardEditor>();
        var originalEditor = editor.Instance;

        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = fire.Identity });
        await Button(editor, "Add").ClickAsync(new());

        editor = cut.FindComponent<CardEditor>();
        Assert.That(editor.Instance, Is.SameAs(originalEditor));
        Assert.That(editor.Instance.Card.Id, Is.EqualTo(originalCard.Id));
        Assert.That(editor.Find(".accordion-button .manual-property-header-badge").TextContent.Trim(), Is.EqualTo(fire.Name));
        Assert.That(editor.Find("details.card-property-inspector summary").TextContent.Trim(), Is.EqualTo("Card properties (1)"));

        await editor.Find("#cardCategory0").ChangeAsync(new() { Value = level.Identity });
        await editor.Find("#cardName0").InputAsync(new() { Value = "Renamed" });

        editor = cut.FindComponent<CardEditor>();
        Assert.That(editor.Instance, Is.SameAs(originalEditor));
        Assert.That(editor.Find("#cardCategory0").GetAttribute("value"), Is.EqualTo(level.Identity));
        Assert.That(editor.Find(".accordion-button .manual-property-header-badge").TextContent.Trim(), Is.EqualTo(fire.Name));
        await Button(editor, "Add").ClickAsync(new());

        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys,
            Is.EquivalentTo(new[] { fire.MetadataKey, level.MetadataKey }));
        Assert.That(editor.FindAll(".accordion-button .manual-property-header-badge").Select(badge => badge.TextContent.Trim()),
            Is.EquivalentTo(new[] { fire.Name, level.Name }));
        Assert.That(editor.Find("details.card-property-inspector summary").TextContent.Trim(), Is.EqualTo("Card properties (2)"));
    }

    [Test]
    public async Task ManualCardPropertyWorkflowPreservesUserIdentityResultsAndOfflineSession() {
        var offline = new Mock<ICardInfoService>();
        offline.Setup(s => s.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>())).ThrowsAsync(new HttpRequestException("Offline"));
        context.Services.AddSingleton(offline.Object);
        var fire = new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        var user = new CategoryBase("Attribute: FIRE");
        var objective = new Card([fire], 2, "Objective", externalCardId: 123);
        var custom = new Card([], 2, "Custom");
        var cut = Render(new SessionState { Categories = [user], Cards = [objective, custom],
            Combos = [new([new(fire, 1, 1)])], HandSize = 1 });
        var editor = cut.FindComponents<CardEditor>()[1];
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(editor.FindAll("optgroup").Select(g => g.GetAttribute("label")), Is.EqualTo(new[] { "User categories", "Card properties" }));
        Assert.That(editor.FindAll("option").Where(o => o.TextContent == fire.Name).Select(o => o.GetAttribute("value")),
            Is.EquivalentTo(new[] { user.Identity, fire.Identity }));
        await Button(cut, "Calculate").ClickAsync(new());
        var before = cut.Find(".probability-total-value").TextContent;
        await editor.Find("select").ChangeAsync(new() { Value = fire.Identity });
        await Button(editor, "Add").ClickAsync(new());
        AssertPreviousResult(cut);
        Assert.That(editor.Instance.Card.Categories, Is.EqualTo(new[] { fire }));
        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { fire.MetadataKey }));
        Assert.That(editor.Instance.Card.ExternalCardId, Is.Null);
        Assert.That(editor.FindAll("optgroup[label='Card properties'] option"), Is.Empty);
        Assert.That(editor.Find(".manual-card-properties").TextContent, Does.Contain("Added manually").And.Contain(fire.Name));
        Assert.That(editor.FindAll(".accordion-button .category-tag").Select(badge => badge.TextContent.Trim()),
            Is.EqualTo(new[] { fire.Name }));
        Assert.That(editor.Find(".manual-property-header-badge").ClassList,
            Does.Contain("card-property-tag").And.Contain("card-property-color-attribute-fire"));
        // A stale/forged selection cannot add another membership or change provenance.
        await editor.Find("select").ChangeAsync(new() { Value = fire.Identity });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Has.Count.EqualTo(1));
        Assert.That(editor.Instance.Card.Categories, Has.Count.EqualTo(1));
        await editor.Find("select").ChangeAsync(new() { Value = user.Identity });
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(editor.Instance.Card.Categories, Is.EqualTo(new[] { fire, user }));
        Assert.That(editor.FindAll(".accordion-button .category-tag"), Has.Count.EqualTo(2));
        await editor.Find("[aria-label='Remove category Attribute: FIRE from card Custom']").ClickAsync(new());
        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Has.Count.EqualTo(1));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.Not.EqualTo(before));
        await editor.Find("#cardName1").InputAsync(new() { Value = "Renamed" });
        await editor.Find("#cardCopies1").InputAsync(new() { Value = "3" });
        await editor.Find(".accordion-button").ClickAsync(new());
        Assert.That(editor.FindAll(".accordion-button .manual-property-header-badge"), Has.Count.EqualTo(1));
        await Button(cut, "Save Session").ClickAsync(new());
        var saved = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(
            (string)context.JSInterop.Invocations["saveSessionFile"].Single().Arguments[1]!));
        var file = new Mock<IBrowserFile>();
        file.Setup(f => f.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(saved)));
        await cut.InvokeAsync(() => cut.FindComponents<InputFile>()[1].Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([file.Object])));
        editor = cut.FindComponents<CardEditor>()[1];
        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { fire.MetadataKey }));
        Assert.That((editor.Instance.Card.Id, editor.Instance.Card.Copies, editor.Instance.Card.Name), Is.EqualTo((custom.Id, 3, "Renamed")));
        var objectiveEditor = cut.FindComponents<CardEditor>()[0];
        Assert.That(objectiveEditor.FindAll(".manual-card-properties, .accordion-body button.btn-close"), Is.Empty);
        Assert.That(objectiveEditor.FindAll(".detected-card-property"), Has.Count.EqualTo(1));
        Assert.That(objectiveEditor.FindAll("optgroup[label='Card properties'] option"), Is.Empty);
        await objectiveEditor.Find("select").ChangeAsync(new() { Value = fire.Identity });
        await Button(objectiveEditor, "Add").ClickAsync(new());
        Assert.That(objectiveEditor.Instance.Card.ManualMetadataCategoryKeys, Is.Empty);
        Assert.That(objectiveEditor.Instance.Card.Categories, Is.EqualTo(new[] { fire }));
        await Button(cut, "Calculate").ClickAsync(new());
        await editor.Find("[aria-label='Remove manually added card property Attribute: FIRE from card Renamed']").ClickAsync(new());
        AssertPreviousResult(cut);
        Assert.That(editor.Instance.Card.ManualMetadataCategoryKeys, Is.Empty);
        Assert.That(editor.Instance.Card.Categories, Is.Empty);
        Assert.That(editor.FindAll("details.card-property-inspector"), Is.Empty);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo(
            SmallDeckOracle.EnumerateProbability(cut.FindComponents<CardEditor>().Select(e => e.Instance.Card).ToList(),
                cut.FindComponents<ComboEditor>().Select(e => e.Instance.Combo).ToList(), 1).ToString("P2")));
    }

    [Test]
    public async Task ManualCardCanAddPropertyRetainedOnlyByComboAndKeepItThroughReordering() {
        var property = new CategoryBase("Level 5", CategorySource.Metadata, "level:5");
        var cut = Render(new SessionState { Cards = [new([], name: "Custom"), new([], name: "Other")],
            Combos = [new([new(property, 1, 1)])], HandSize = 1 });
        var editor = cut.FindComponents<CardEditor>()[0];
        Assert.That(editor.FindAll("optgroup[label='Card properties'] option").Select(o => o.TextContent), Is.EqualTo(new[] { property.Name }));
        await editor.Find("select").ChangeAsync(new() { Value = property.Identity });
        await editor.Find("[aria-label='Move card Custom, row 1 down']").ClickAsync(new());
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(cut.FindComponents<CardEditor>()[1].Instance.Card.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { property.MetadataKey }));
        Assert.That(cut.FindComponents<CardEditor>()[0].Instance.Card.Categories, Is.Empty);
    }

}
