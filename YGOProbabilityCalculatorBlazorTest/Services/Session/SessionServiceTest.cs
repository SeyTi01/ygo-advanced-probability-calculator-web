using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public class SessionServiceTests {
    private Mock<IJSRuntime> _jsRuntimeMock;
    private Mock<ISerializer> _serializerMock;
    private SessionService _sessionService;

    [SetUp]
    public void Setup() {
        _jsRuntimeMock = new Mock<IJSRuntime>();
        _serializerMock = new Mock<ISerializer>();
        _sessionService = new SessionService(_jsRuntimeMock.Object, _serializerMock.Object);
    }

    [Test]
    public async Task SaveSessionAsync_WithValidFileName_CallsSerializerAndJsRuntime() {
        var session = new SessionState();
        const string fileName = "test";
        const string serializedJson = "{}";
        const string expectedFileName = "test.json";
        var expectedBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(serializedJson));

        _serializerMock.Setup(x => x.Serialize(It.IsAny<SessionState>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(serializedJson);

        await _sessionService.SaveSessionAsync(session, fileName);

        _serializerMock.Verify(x => x.Serialize(session, It.IsAny<JsonSerializerOptions>()), Times.Once);
        _jsRuntimeMock.Verify(x => x.InvokeAsync<object>(
            "downloadFileFromStream",
            It.Is<object[]>(args =>
                args.Length == 2 &&
                args[0].ToString() == expectedFileName &&
                args[1].ToString() == expectedBase64
            )
        ), Times.Once);
    }


    [Test]
    public async Task SaveSessionAsync_WithJsonExtension_DoesNotAppendExtension() {
        var session = new SessionState();
        const string fileName = "test.json";
        const string serializedJson = "{}";
        var expectedBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(serializedJson));

        _serializerMock.Setup(x => x.Serialize(It.IsAny<SessionState>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(serializedJson);

        await _sessionService.SaveSessionAsync(session, fileName);

        _jsRuntimeMock.Verify(x => x.InvokeAsync<object>(
            "downloadFileFromStream",
            It.Is<object[]>(args =>
                args.Length == 2 &&
                args[0].ToString() == fileName &&
                args[1].ToString() == expectedBase64
            )
        ), Times.Once);
    }

    [Test]
    public async Task LoadSessionAsync_WithValidJson_ReturnsDeserializedSession() {
        const string fileContent = "{}";
        var expectedSession = new SessionState();

        _serializerMock.Setup(x => x.Deserialize<SessionState>(fileContent, It.IsAny<JsonSerializerOptions>()))
            .Returns(expectedSession);

        var result = await _sessionService.LoadSessionAsync(fileContent);

        Assert.That(result, Is.SameAs(expectedSession));
        _serializerMock.Verify(x => x.Deserialize<SessionState>(
            fileContent,
            It.IsAny<JsonSerializerOptions>()
        ), Times.Once);
    }

    [Test]
    public void LoadSessionAsync_WhenDeserializerReturnsNull_ThrowsInvalidOperationException() {
        const string fileContent = "{}";
        _serializerMock.Setup(x => x.Deserialize<SessionState>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>()))
            .Returns((SessionState?)null);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _sessionService.LoadSessionAsync(fileContent));
        Assert.That(exception.Message, Is.EqualTo("Failed to deserialize session data"));
    }

    [Test]
    public void LoadSessionAsync_WhenDeserializerThrowsJsonException_ThrowsInvalidOperationException() {
        const string fileContent = "invalid json";
        _serializerMock.Setup(x => x.Deserialize<SessionState>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>()))
            .Throws(new JsonException("Invalid JSON"));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _sessionService.LoadSessionAsync(fileContent));
        Assert.That(exception.Message, Is.EqualTo("Invalid session file format"));
    }
}
