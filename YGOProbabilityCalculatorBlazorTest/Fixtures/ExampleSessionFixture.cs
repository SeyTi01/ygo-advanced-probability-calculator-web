using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Fixtures;

internal static class ExampleSessionFixture {
    internal static async Task<SessionState> LoadAsync(string filename) => await SessionFixture.CreateCodec().LoadSessionAsync(
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", filename)));
}
