using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using YGOProbabilityCalculatorBlazor.Constants;

namespace YGOProbabilityCalculatorBlazorTest;

[Explicit("Run scripts/verify-published-seo.sh after Release publish.")]
public class PublishedOutputSeoTest {
    private const string AppTitle = "Yu-Gi-Oh! Advanced Probability Calculator";

    [TestCase("index.html", AppTitle,
        "Calculate exact Yu-Gi-Oh! opening-hand and deck-combo probabilities with overlapping card categories, direct card requirements, and custom constraints.",
        "https://ygo-calculator.pages.dev/")]
    [TestCase("help.html", "Help | " + AppTitle,
        "Help and usage guide for the Yu-Gi-Oh! Advanced Probability Calculator, including cards, categories, combo requirements, constraints, and probability results.",
        "https://ygo-calculator.pages.dev/help")]
    public async Task PublicRouteContainsCompleteInitialHtml(string relativePath, string title, string description, string canonical) {
        var publishRoot = Environment.GetEnvironmentVariable("YGO_PUBLISH_ROOT");
        Assert.That(publishRoot, Is.Not.Null.And.Not.Empty, "Set YGO_PUBLISH_ROOT to the published wwwroot directory.");
        var path = Path.Combine(publishRoot!, relativePath);
        Assert.That(File.Exists(path), Is.True, $"Missing prerendered route: {path}");

        // Parse the deployable file only. Do not execute JavaScript or render Razor components.
        using var document = await new HtmlParser().ParseDocumentAsync(await File.ReadAllTextAsync(path));
        AssertSeo(document, relativePath, title, description, canonical);
        Assert.That(File.Exists(Path.Combine(publishRoot!, "help", "index.html")), Is.False,
            "A directory-index Help asset makes Cloudflare redirect the canonical /help route.");
    }

    [TestCase("/")]
    [TestCase("/help")]
    [TestCase("/help/")]
    public async Task PublicHttpRouteContainsCompleteInitialHtml(string route) {
        var baseUrl = Environment.GetEnvironmentVariable("YGO_SEO_BASE_URL");
        if (string.IsNullOrEmpty(baseUrl)) {
            Assert.Ignore("Set YGO_SEO_BASE_URL to a local Pages server or Cloudflare preview URL to verify HTTP routing.");
        }

        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl!) };
        using var response = await client.GetAsync(route);
        TestContext.Out.WriteLine($"GET {route}: {(int)response.StatusCode}; Location: {response.Headers.Location}");
        if (route == "/help/") {
            Assert.That((int)response.StatusCode, Is.AnyOf(301, 302, 307, 308));
            Assert.That(response.Headers.Location, Is.Not.Null);
            Assert.That(new Uri(client.BaseAddress, response.Headers.Location!).AbsoluteUri,
                Is.EqualTo(new Uri(client.BaseAddress, "/help").AbsoluteUri), "Redirect once to the canonical Help route.");
            return;
        }

        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK), "The canonical route must return direct HTML.");
        Assert.That(response.Headers.Location, Is.Null);
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        using var document = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
        var isRoot = route == "/";
        AssertSeo(document, isRoot ? "index.html" : "help.html", isRoot ? AppTitle : "Help | " + AppTitle,
            isRoot
                ? "Calculate exact Yu-Gi-Oh! opening-hand and deck-combo probabilities with overlapping card categories, direct card requirements, and custom constraints."
                : "Help and usage guide for the Yu-Gi-Oh! Advanced Probability Calculator, including cards, categories, combo requirements, constraints, and probability results.",
            isRoot ? "https://ygo-calculator.pages.dev/" : "https://ygo-calculator.pages.dev/help");
    }

    private static void AssertSeo(IDocument document, string relativePath, string title, string description, string canonical) {
        AssertSingle(document, "head title", title);
        AssertAttribute(document, "head meta[name='description']", "content", description);
        AssertAttribute(document, "head link[rel='canonical']", "href", canonical);
        AssertSingle(document, "h1", relativePath == "index.html" ? AppTitle : $"{AppTitle} v{ProjectConstants.Version}");

        var metadata = new Dictionary<string, string> {
            ["og:type"] = "website",
            ["og:site_name"] = AppTitle,
            ["og:title"] = title,
            ["og:description"] = description,
            ["og:url"] = canonical,
            ["og:image"] = "https://ygo-calculator.pages.dev/social-preview.png",
            ["og:image:alt"] = "Dark calculator interface showing Vanquish Soul Razen with VS Monster and VS Starter roles, a VS Starter plus FIRE-or-DARK combo expression, and exact Full VS probability results.",
            ["og:image:width"] = "1280",
            ["og:image:height"] = "640",
            ["twitter:card"] = "summary_large_image",
            ["twitter:title"] = title,
            ["twitter:description"] = description,
            ["twitter:image"] = "https://ygo-calculator.pages.dev/social-preview.png",
            ["twitter:image:alt"] = "Dark calculator interface showing Vanquish Soul Razen with VS Monster and VS Starter roles, a VS Starter plus FIRE-or-DARK combo expression, and exact Full VS probability results."
        };
        foreach (var (name, value) in metadata) {
            var attribute = name.StartsWith("og:", StringComparison.Ordinal) ? "property" : "name";
            AssertAttribute(document, $"head meta[{attribute}='{name}']", "content", value);
        }

        Assert.That(document.QuerySelector("#app main"), Is.Not.Null, "The page body must be prerendered inside the normal app root.");
        Assert.That(document.QuerySelectorAll("script[src='_framework/blazor.webassembly.js']"), Has.Length.EqualTo(1));
    }

    private static IElement Single(IDocument document, string selector) {
        var matches = document.QuerySelectorAll(selector);
        Assert.That(matches, Has.Length.EqualTo(1), selector);
        return matches[0];
    }

    private static void AssertSingle(IDocument document, string selector, string text) =>
        Assert.That(Single(document, selector).TextContent.Trim(), Is.EqualTo(text), selector);

    private static void AssertAttribute(IDocument document, string selector, string attribute, string value) =>
        Assert.That(Single(document, selector).GetAttribute(attribute), Is.EqualTo(value), selector);
}
