using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;

namespace YGOProbabilityCalculatorBlazorTest.Fixtures;

internal static class SessionFixture {
    internal static SessionService CreateCodec() => new(Mock.Of<IJSRuntime>(), new JsonSerializer());
}
