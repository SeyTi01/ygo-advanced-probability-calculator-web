using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using Microsoft.JSInterop;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Localization;

public sealed class LocalizationTest {
    [Test]
    public void GermanSatelliteCoversEveryNeutralMessageWithMatchingArguments() {
        ResourceSet english = UiText.Resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        ResourceSet german = UiText.Resources.GetResourceSet(CultureInfo.GetCultureInfo("de"), true, false)!;
        Assert.That(german, Is.Not.Null, "The production German satellite must exist without fallback.");
        foreach (DictionaryEntry resource in english) {
            string key = (string)resource.Key;
            string original = (string)resource.Value!;
            string? translated = german.GetString(key);
            Assert.That(translated, Is.Not.Null, key);
            Assert.That(UiText.HasSameArguments(original, translated!), Is.True, key);
            if (original.Length > 0) { Assert.That(translated, Is.Not.Empty, key); }
        }
    }

    [Test]
    public void RealSatelliteSubstitutionFallbackAndArguments() {
        using PresentationLocale locale = new();
        Assert.Multiple(() => {
            Assert.That(UiText.Format("Deck.Count", 4), Is.EqualTo("⟦Deck 4⟧"));
            Assert.That(UiText.Get("Ui.Cancel"), Is.EqualTo("Cancel"));
            Assert.That(UiText.Get("Missing.TestKey"), Is.EqualTo("Missing.TestKey"));
            Assert.That(UiText.Format("Move.ToPosition", "sentinel"), Is.EqualTo("Move sentinel to position"));
            Assert.That(UiText.Format("Ui.TheActiveDeckHas0CopiesFewerThanTheHandSizeOf1", 2, 5), Is.EqualTo("⟦Hand 5; only 2 copies⟧"));
            Assert.That(UiText.English("Effects.35261759.Summary"), Is.Not.EqualTo("⟦Maintained draw⟧"));
            Assert.That(UiText.Get("Ui.PreviousResultCurrentInputsHaveChanged"), Is.EqualTo("Previous result — current inputs have changed.\n"));
        });
        Assert.Throws<FormatException>(() => UiText.Format("Deck.Count"));
    }

    [TestCase("{0} {1}", "{1} {0}", true)]
    [TestCase("{0} {1}", "{1}", false)]
    [TestCase("{0}", "{1}", false)]
    [TestCase("{0}", "{", false)]
    [TestCase("{{literal}} {0:N2}", "{0:N2} {{literal}}", true)]
    public void PlaceholderValidationChecksIndicesAndSyntax(string english, string translated, bool valid) {
        Assert.That(UiText.HasSameArguments(english, translated), Is.EqualTo(valid));
    }

    [Test]
    public void EnglishInventoryAndAllMaintainedDefinitionsHaveValidResources() {
        ResourceSet set = UiText.Resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        foreach (DictionaryEntry resource in set) {
            string text = (string)resource.Value!;
            Assert.That(UiText.HasSameArguments(text, text), Is.True, (string)resource.Key);
        }
        foreach (MaintainedCardEffect effect in CardEffectRegistry.Default.All) {
            string summary = RequiredNeutralResource(set, $"Effects.{effect.CanonicalCardId}.Summary");
            string limitations = RequiredNeutralResource(set, $"Effects.{effect.CanonicalCardId}.Limitations");
            Assert.That(summary, Is.Not.Empty);
            Assert.That(CompositeFormat.Parse(summary).MinimumArgumentCount, Is.Zero);
            Assert.That(CompositeFormat.Parse(limitations).MinimumArgumentCount, Is.Zero);
            Assert.That(UiText.English($"Effects.{effect.CanonicalCardId}.Summary"), Is.EqualTo(effect.Profile.Summary));
            Assert.That(UiText.English($"Effects.{effect.CanonicalCardId}.Limitations"), Is.EqualTo(effect.Profile.Limitations ?? ""));
        }
    }

    [Test]
    public void MaintainedInventoryRejectsAnAbsentEntryInsteadOfAcceptingItsFallbackKey() {
        ResourceSet set = UiText.Resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        const string missing = "Effects.MissingTestDefinition.Summary";
        Assert.That(UiText.English(missing), Is.EqualTo(missing));
        Assert.Throws<AssertionException>(() => RequiredNeutralResource(set, missing));
    }

    private static string RequiredNeutralResource(ResourceSet set, string key) {
        string? value = set.GetString(key);
        Assert.That(value, Is.Not.Null, $"Missing neutral resource: {key}");
        return value!;
    }

    [TestCase("en")]
    [TestCase("fr")]
    public void RepeatedLabelLookupsDoNotAllocateFormatParsers(string language) {
        ResourceManager resources = new TestResources();
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        for (int index = 0; index < 100; index++) { UiText.Get(resources, "Label", culture); }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++) { UiText.Get(resources, "Label", culture); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero, "Warm label lookup should not allocate format parsers or placeholder sets.");
    }

    [Test]
    public void CachedTranslationValidationFollowsResourceSourceCultureAndChangedText() {
        TestResources first = new();
        TestResources second = new() { Translation = "second {0}" };
        CultureInfo french = CultureInfo.GetCultureInfo("fr");
        CultureInfo english = CultureInfo.GetCultureInfo("en");
        Assert.That(UiText.Get(first, "Label", french), Is.EqualTo("translated {0}"));
        Assert.That(UiText.Get(second, "Label", french), Is.EqualTo("second {0}"));
        Assert.That(UiText.Get(first, "Label", english), Is.EqualTo("English {0}"));
        first.Translation = "invalid {1}";
        Assert.That(UiText.Get(first, "Label", french), Is.EqualTo("English {0}"));
        first.Translation = "repeated {0} {0}\r\n";
        Assert.That(UiText.Get(first, "Label", french), Is.EqualTo("repeated {0} {0}\n"));
        Assert.That(UiText.Get(first, "Label", french), Is.EqualTo("repeated {0} {0}\n"));
        first.Neutral = "changed {0} {1}";
        Assert.That(UiText.Get(first, "Label", french), Is.EqualTo("changed {0} {1}"));
        using (UiText.UseResources(first)) {
            using (UiText.UseResources(second)) {
                Assert.That(UiText.Get(UiText.Resources, "Label", french), Is.EqualTo("second {0}"));
            }
            Assert.That(UiText.Get(UiText.Resources, "Label", french), Is.EqualTo("changed {0} {1}"));
        }
    }

    [Test]
    public void RepeatedSatelliteFormattingRetainsCallerNumericCulture() {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try {
            using PresentationLocale locale = new();
            foreach (string language in new[] { "en-US", "de-DE" }) {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
                string expected = "⟦Deck " + 12.5m.ToString(CultureInfo.CurrentCulture) + "⟧";
                for (int index = 0; index < 3; index++) {
                    Assert.That(UiText.Format("Deck.Count", 12.5m), Is.EqualTo(expected));
                    Assert.That(UiText.Get("Move.ToPosition"), Is.EqualTo("Move {0} to position"));
                }
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private sealed class TestResources : ResourceManager {
        public string Neutral { get; set; } = "English {0}";
        public string Translation { get; set; } = "translated {0}";

        public override string? GetString(string name, CultureInfo? culture) =>
            culture?.Name == "fr" ? Translation : Neutral;
    }

    [TestCase("catalog-id:123", "Card identity: Target", "Identity: Target")]
    [TestCase("card-name:target", "Card name: Target", "Name: Target")]
    [TestCase("monster-race:warrior", "Monster Type: Warrior", "Type: Warrior")]
    public void AutomaticLabelsShortenLegacyNamesWithoutRewritingDataOrCustomLabels(string key, string name, string expected) {
        CategoryBase automatic = new(name, CategorySource.Metadata, key);
        string identity = automatic.Identity;
        Assert.That(DisplayLabels.Category(automatic), Is.EqualTo(expected));
        Assert.That(automatic.Name, Is.EqualTo(name));
        Assert.That(automatic.Identity, Is.EqualTo(identity));
        Assert.That(automatic.MetadataKey, Is.EqualTo(key));
        Assert.That(DisplayLabels.Category(new CategoryBase(name)), Is.EqualTo(name));
        using PresentationLocale locale = new();
        Assert.That(DisplayLabels.Category(automatic), Is.EqualTo(expected), "An absent locale label falls back to abbreviated English.");
    }

    [Test]
    public void LabelsUseVerifiedCatalogIdentityAndNeverGuessStoredNameAuthorship() {
        using PresentationLocale locale = new();
        CardInfo catalog = new() { Id = 111, CanonicalCardId = 35261759, Name = "English name" };
        CardInfo unknown = new() { Id = 112, Name = "Unknown catalog name" };
        CategoryBase metadata = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase manual = new("Monster");
        Card saved = new([metadata], name: catalog.Name, externalCardId: 35261759);
        Assert.Multiple(() => {
            Assert.That(DisplayLabels.Card(catalog), Is.EqualTo("⟦Catalog card⟧"));
            Assert.That(DisplayLabels.Card(unknown), Is.EqualTo(unknown.Name));
            Assert.That(DisplayLabels.Card(saved), Is.EqualTo(saved.Name));
            Assert.That(DisplayLabels.Card(saved.WithName("My deliberate name")), Is.EqualTo("My deliberate name"));
            Assert.That(DisplayLabels.Category(metadata), Is.EqualTo("⟦Monster⟧"));
            Assert.That(DisplayLabels.Category(manual), Is.EqualTo("Monster"));
            Assert.That(DisplayLabels.Archetype("k9", "K9"), Is.EqualTo("⟦K9⟧"));
            Assert.That(metadata.Identity, Is.EqualTo("metadata:kind:monster"));
            Assert.That(CardSelector.For(metadata).CategoryIdentities, Is.EqualTo(new[] { metadata.Identity }));
            Assert.That(new NumericPropertyRange(NumericPropertyKind.Level, 8, 8).ToCategory().Name, Is.EqualTo("Level 8"));
        });
    }

    [TestCase("current", "fr")]
    [TestCase("canonical", "fr")]
    [TestCase("revision", "fr")]
    [TestCase("plan", "fr")]
    [TestCase("support", "fr")]
    [TestCase("execution", "fr")]
    [TestCase("custom", "fr")]
    [TestCase("legacy", "fr")]
    [TestCase("current", "de")]
    [TestCase("canonical", "de")]
    [TestCase("revision", "de")]
    [TestCase("plan", "de")]
    [TestCase("support", "de")]
    [TestCase("execution", "de")]
    [TestCase("custom", "de")]
    [TestCase("legacy", "de")]
    public void OnlyFullyCompatibleEffectsReceiveLocalizedMaintainedDescriptions(string difference, string language) {
        MaintainedCardEffect maintained = CardEffectRegistry.Default.Find(35261759)!;
        EffectProfile profile = maintained.Profile with { Summary = "Historical summary", Limitations = "Historical limits" };
        DrawEffectPlan plan = maintained.Plan;
        profile = difference switch {
            "canonical" => profile with { CanonicalCardId = 55144522 },
            "revision" => profile with { Revision = maintained.Profile.Revision + 1 },
            "support" => profile with { Support = EffectSupport.Partial },
            "execution" => profile with { ExecutionModel = EffectExecutionModel.Unsupported },
            "custom" => profile.AsCustom(),
            "legacy" => EffectProfile.Legacy,
            _ => profile
        };
        if (difference == "plan") { plan = plan with { HiddenDeckCost = 0 }; }
        Card saved = new Card([], externalCardId: maintained.CanonicalCardId).WithEffect(plan, profile);
        EffectiveCardEffect canonical = CardEffectRegistry.Default.Describe(saved);
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        string savedJson = codec.SerializeSession(new() { Cards = [saved] });
        using PresentationLocale locale = new(language, production: language == "de");
        EffectiveCardEffect display = EffectDescriptions.Describe(saved);
        Assert.Multiple(() => {
            Assert.That(display.UsesCurrentDescription, Is.EqualTo(difference == "current"));
            Assert.That(display.Summary, Is.EqualTo(difference == "current" ? UiText.Get("Effects.35261759.Summary") : canonical.Summary));
            Assert.That(display.Limitations, Is.EqualTo(difference == "current" ? UiText.Get("Effects.35261759.Limitations") : canonical.Limitations));
            Assert.That(display.Plan, Is.EqualTo(plan));
            Assert.That(saved.EffectProfile, Is.EqualTo(profile));
            Assert.That(display.CanExecute, Is.EqualTo(canonical.CanExecute));
            Assert.That(codec.SerializeSession(new() { Cards = [saved] }), Is.EqualTo(savedJson));
            Assert.That(new CardEffectRegistry([new PotOfDesiresEffect()]).Find(35261759)!.Profile, Is.EqualTo(maintained.Profile));
        });
    }

    [TestCase("fr")]
    [TestCase("de")]
    public async Task SessionsShareLinksWorkerBytesAndExactFractionRemainLocaleIndependent(string language) {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase authored = new("My role");
        SessionState session = new() {
            HandSize = 1, CalculationModel = CalculationModel.OpeningHand,
            Categories = [authored],
            Cards = [new([monster, authored], 1, "My card", id: "one"), new([], 2, "Other", id: "two")],
            Combos = [new([new(authored, 1, 1)], "My combo")]
        };
        string before = codec.SerializeSession(session);
        string link = SessionShareCodec.CreateLink("https://example.test/", before);
        CalculationOptions options = new() { Mode = CalculationModel.OpeningHand };
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, 1, [], options: options);
        ProbabilityCalculationResult english = new ProbabilityCalculatorService().CalculateProbabilityResults(session.Cards, session.Combos, 1, [], CalculationWorkPolicy.Default, options);
        using PresentationLocale locale = new(language, production: language == "de");
        SessionState loaded = await codec.LoadSessionAsync(before);
        DisplayLabels.Category(monster);
        ProbabilityCalculationResult translated = new ProbabilityCalculatorService().CalculateProbabilityResults(loaded.Cards, loaded.Combos, 1, [], CalculationWorkPolicy.Default, options);
        Assert.Multiple(() => {
            Assert.That(codec.SerializeSession(loaded), Is.EqualTo(before));
            Assert.That(SessionShareCodec.CreateLink("https://example.test/", codec.SerializeSession(loaded)), Is.EqualTo(link));
            Assert.That(CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, 1, [], options: options).Json, Is.EqualTo(snapshot.Json));
            Assert.That(translated.TotalProbability, Is.EqualTo(1d / 3));
            Assert.That(translated.Contract!.Fractions!.Total, Is.EqualTo(english.Contract!.Fractions!.Total));
            Assert.That(translated.Contract.Fractions.Total, Is.EqualTo(new ProbabilityFraction("1", "3")));
            Assert.That(loaded.Cards[0].Name, Is.EqualTo("My card"));
            Assert.That(loaded.Combos[0].Name, Is.EqualTo("My combo"));
        });
    }

    [Test, NonParallelizable]
    public void ShellResolvesGermanAndExplicitEnglishWithoutChangingNumericCulture() {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo? previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        CultureInfo numbers = CultureInfo.CurrentCulture;
        try {
            LocalePreference preference = new();
            preference.Initialize("fr", ["de-DE", "en-GB"]);
            Assert.That(preference.Locale, Is.EqualTo("de"));
            Assert.That(CultureInfo.CurrentUICulture.Name, Is.EqualTo("de"));
            Assert.That(CultureInfo.CurrentCulture, Is.SameAs(numbers));
            Assert.That(LocalePreference.Resolve("xx", ["de"]), Is.EqualTo("de"));
            Assert.That(LocalePreference.Resolve("en", ["de-DE"]), Is.EqualTo("en"));
            Assert.That(LocalePreference.Resolve(), Is.EqualTo("en"));
            Assert.That(LocalePreference.SupportedLocales, Is.EqualTo(new[] { "en", "de" }));
            ServiceCollection services = new();
            YGOProbabilityCalculatorBlazor.Program.ConfigureServices(services, "https://example.test/");
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<LocalePreference>().Locale, Is.EqualTo("en"));
            Assert.That(CultureInfo.CurrentCulture, Is.SameAs(numbers));
        }
        finally {
            CultureInfo.CurrentUICulture = previous;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
        }
    }

    [TestCase(CalculationModel.Exact)]
    [TestCase(CalculationModel.Fast)]
    public void GermanPresentationPreservesExistingPhysicalFixtureFractionsAndCertificates(CalculationModel mode) {
        Services.ProbabilityCalculator.SearchCertificateFixtures.Fixture fixture =
            Services.ProbabilityCalculator.SearchCertificateFixtures.Fixtures().Single(value => value.Name == "minimal-hidden-only-target");
        CalculationOptions options = new(Mode: mode);
        ProbabilityCalculationResult english;
        string request;
        using (PresentationLocale locale = new("en", production: true)) {
            request = CalculationSnapshot.Capture(fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, options: options).Json;
            english = new ProbabilityCalculatorService().CalculateProbabilityResults(
                fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Default, options);
        }
        using (PresentationLocale locale = new("de", production: true)) {
            ProbabilityCalculationResult german = new ProbabilityCalculatorService().CalculateProbabilityResults(
                fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Default, options);
            Assert.That(CalculationSnapshot.Capture(fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, options: options).Json, Is.EqualTo(request));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(german.Contract!.Fractions),
                Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(english.Contract!.Fractions)));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(german.SearchAvailability),
                Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(english.SearchAvailability)));
            if (mode == CalculationModel.Exact) {
                Assert.That(german.Contract.Fractions!.Combos[0], Is.EqualTo(new ProbabilityFraction("23", "30")));
            }
            else { Assert.That(german.SearchAvailability, Is.Not.Null); }
        }
    }
}
