using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public class SessionShareCodecTest {
    private Bunit.TestContext _context = null!;
    private ISessionService _sessions = null!;

    [SetUp]
    public void Setup() {
        _context = new();
        _sessions = new SessionService(_context.JSInterop.JSRuntime, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
    }

    [TearDown]
    public void Cleanup() => _context.Dispose();

    private static string Fragment(string link) => link[link.IndexOf('#')..];

    // Independent transport construction, also used to attack the decoder with arbitrary bytes.
    private static string Payload(byte[] json, int? declared = null, bool forgeTrailer = false) {
        using MemoryStream stream = new();
        using (GZipStream gzip = new(stream, CompressionLevel.SmallestSize, true)) {
            gzip.Write(json);
        }

        byte[] compressed = stream.ToArray();

        if (forgeTrailer) {
            BinaryPrimitives.WriteInt32LittleEndian(compressed.AsSpan(compressed.Length - 4), declared!.Value);
        }

        byte[] bytes = new byte[36 + compressed.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, declared ?? json.Length);
        SHA256.HashData(compressed).CopyTo(bytes, 4);
        compressed.CopyTo(bytes, 36);

        return SessionShareCodec.Prefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Payload(string json) => Payload(Encoding.UTF8.GetBytes(json));

    [Test]
    public async Task LoadsAnIndependentNodeGzipFixture() {
        const string fixture = "#ygo-session=v1.NQAAAKK_xBk2PLQ4t_Jhj8jUSmyljm62x9kJEr73_uIc56XtH4sIAAAAAAAACqtW8kjMSwnOrEpVsjLWUXJOLEopVrKKjtVRcs7PTcqHsRNLUtPzizJTwfxaAEY1zC01AAAA";
        Assert.That((await _sessions.LoadSessionAsync(SessionShareCodec.Decode(fixture))).HandSize, Is.EqualTo(3));
    }

    [Test]
    public async Task PreservesAllSupportedSemanticsAndOrder() {
        CategoryBase role = new("役割 / \"Starter\" & <b>🔥</b>");
        CategoryBase metadata = new("Attribute: FIRE", CategorySource.Metadata, "metadata:attribute:fire");
        SessionState session = new() {
            Categories = [role, new("Second")],
            Cards = [new([role, metadata], 0, "é — 中文", false, "stable:雪", 1234, [metadata.MetadataKey!]), new([], 3, "Second", true, "second")],
            Combos = [new([new(role, 0, 0)], "Fixed", false, "g", [new("stable:雪", 0, 0)]),
                new([new(role, 1, 0, RequirementMaximumMode.HandSize)], "Any", true)],
            ComboGroups = [new("g", "群"), new("other", "Other")],
            CategoryColorIndices = new() { [role.Name] = 11, ["Second"] = 0 }, HandSize = 7
        };
        string json = _sessions.SerializeSession(session);
        string link = SessionShareCodec.CreateLink("https://dev.example.test/calculator/", json);
        SessionState restored = await _sessions.LoadSessionAsync(SessionShareCodec.Decode(Fragment(link)));
        Assert.That(_sessions.SerializeSession(restored), Is.EqualTo(json));
        Assert.That(link, Does.StartWith("https://dev.example.test/calculator/#ygo-session=v1."));
        Assert.That(link, Does.Not.Contain("?").And.Not.Contain("%"));
    }

    [TestCase("{}")]
    [TestCase("{\"HandSize\":5,\"Combos\":[{\"Categories\":[],\"Name\":\"Incomplete\",\"Active\":false}]} ")]
    public async Task EmptyAndIncompleteLegacySessionsAreSupported(string json) {
        SessionState loaded = await _sessions.LoadSessionAsync(SessionShareCodec.Decode(Payload(json)));
        Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
    }

    [Test]
    public async Task LegacySchemaKeepsFixedZeroAndStableIds() {
        string json = "{\"SchemaVersion\":1,\"HandSize\":5,\"Cards\":[{\"Categories\":[],\"Copies\":1,\"Name\":\"Old\",\"Id\":\"old-id\"}],\"Combos\":[{\"Categories\":[],\"Cards\":[{\"CardId\":\"old-id\",\"MinCount\":0,\"MaxCount\":0}]}]}";
        SessionState loaded = await _sessions.LoadSessionAsync(SessionShareCodec.Decode(Payload(json)));
        Assert.That(loaded.Cards[0].Id, Is.EqualTo("old-id"));
        Assert.That(loaded.Combos[0].Cards[0].MaximumMode, Is.EqualTo(RequirementMaximumMode.Fixed));
        Assert.That(loaded.Combos[0].Cards[0].MaxCount, Is.Zero);
    }

    [TestCase("#ygo-session=v2.abc")]
    [TestCase("#ygo-session=v1.%41")]
    [TestCase("#ygo-session=v1.!")]
    [TestCase("#ygo-session=v1.a")]
    [TestCase("#ygo-session=v1.")]
    public void RejectsUnsupportedMalformedAndPercentEncodedFragments(string fragment) =>
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.Decode(fragment));

    [Test]
    public void RejectsCorruptionTruncationOversizedEncodingAndInvalidUtf8() {
        string fragment = Payload("{\"HandSize\":5}");
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.Decode(fragment[..^3]));
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.Decode(fragment[..^8] + "AAAAAAAA"));
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.Decode(SessionShareCodec.Prefix + new string('a', SessionShareCodec.MaxUrlLength)));
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.Decode(Payload([0x7b, 0x22, 0xff, 0x22, 0x3a, 0x31, 0x7d])));
    }

    [Test]
    public void DecompressionBombIsBoundedEvenWithAForgedSmallLength() {
        byte[] bomb = Encoding.UTF8.GetBytes("{\"x\":\"" + new string('a', SessionShareCodec.MaxJsonBytes * 8) + "\"}");
        string fragment = Payload(bomb);
        Assert.That(fragment.Length, Is.LessThan(SessionShareCodec.MaxUrlLength));
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.Decode(fragment));
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.Decode(Payload(bomb, 32)));
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.Decode(Payload(bomb, 32, forgeTrailer: true)));
    }

    [TestCase("[]")]
    [TestCase("{\"Cards\":null}")]
    [TestCase("{\"Categories\":[null]}")]
    [TestCase("{\"Combos\":[{\"Categories\":[{\"BaseCategory\":null}]}]}")]
    [TestCase("{\"Cards\":[{\"Categories\":[],\"Copies\":2147483647}]}")]
    [TestCase("{\"SchemaVersion\":2,\"schemaversion\":2}")]
    public void RejectsInvalidStructures(string json) => Assert.Throws<InvalidOperationException>(() => SessionShareCodec.Decode(Payload(json)));

    [Test]
    public void RejectsExcessiveDepthStringsAndCollections() {
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.Decode(Payload("{\"x\":" + new string('[', 33) + "0" + new string(']', 33) + "}")));
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.Decode(Payload("{\"x\":\"" + new string('a', 4097) + "\"}")));
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.Decode(Payload("{\"Cards\":[" + string.Join(',', Enumerable.Repeat("{}", 2049)) + "]}")));
    }

    [Test]
    public void FutureSessionVersionUsesExistingValidator() {
        string json = SessionShareCodec.Decode(Payload("{\"SchemaVersion\":999}"));
        Assert.Throws<InvalidOperationException>(() => _sessions.LoadSessionAsync(json));
    }

    [Test]
    public async Task MeasuredExamplesFitAndUrlBoundaryNeverTruncates() {
        string json = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json"));
        SessionState session = await _sessions.LoadSessionAsync(json);
        string bundled = SessionShareCodec.CreateLink("https://dev.example.test/", _sessions.SerializeSession(session));
        CategoryBase[] roles = Enumerable.Range(0, 30).Select(i => new CategoryBase($"Role {i}")).ToArray();
        SessionState larger = new() { HandSize = 5, Categories = [..roles],
            Cards = Enumerable.Range(0, 100).Select(i => new Card([roles[i % 30], roles[(i + 7) % 30], roles[(i + 13) % 30]],
                3, $"Realistic card {i}", true, $"id-{i}")).ToList(),
            Combos = Enumerable.Range(0, 50).Select(i => new Combo([new(roles[i % 30], 1, 0, RequirementMaximumMode.HandSize),
                new(roles[(i + 5) % 30], 0, 0)], $"Route {i}", i % 2 == 0, $"group-{i % 5}", [new($"id-{i}", 0, 2)])).ToList(),
            ComboGroups = Enumerable.Range(0, 5).Select(i => new ComboGroup($"group-{i}", $"Group {i}")).ToList() };
        string big = SessionShareCodec.CreateLink("https://dev.example.test/", _sessions.SerializeSession(larger));
        TestContext.Out.WriteLine($"Bundled example URL: {bundled.Length} characters; 100-card/50-combo URL: {big.Length} characters.");
        Assert.That(bundled.Length, Is.LessThan(SessionShareCodec.MaxUrlLength));
        Assert.That(big.Length, Is.LessThan(SessionShareCodec.MaxUrlLength));
        string fragment = Fragment(big);
        string baseAtLimit = "https://example.test/" + new string('a', SessionShareCodec.MaxUrlLength - fragment.Length - "https://example.test/".Length);
        Assert.That(SessionShareCodec.CreateLink(baseAtLimit, _sessions.SerializeSession(larger)).Length, Is.EqualTo(SessionShareCodec.MaxUrlLength));
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.CreateLink(baseAtLimit + "a", _sessions.SerializeSession(larger)));
        Assert.Throws<InvalidOperationException>(() => SessionShareCodec.CreateLink("https://example.test/", new string('x', SessionShareCodec.MaxJsonBytes + 1)));
    }
}
