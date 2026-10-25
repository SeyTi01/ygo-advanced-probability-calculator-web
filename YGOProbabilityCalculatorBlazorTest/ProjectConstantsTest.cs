using YGOProbabilityCalculatorBlazor.Constants;

namespace YGOProbabilityCalculatorBlazorTest;

public class ProjectConstantsTest {
    [Test]
    public void DeploymentChannelMatchesCloudflarePagesBranchOrLocalFallback() {
        string expectedChannel = Environment.GetEnvironmentVariable("CF_PAGES_BRANCH") switch {
            "main" => "stable",
            "dev" => "dev",
            null or "" => "local",
            _ => "preview"
        };

        Assert.That(expectedChannel, Is.EqualTo(ProjectConstants.DeploymentChannel));
        Assert.That(ProjectConstants.DisplayVersion, Is.EqualTo(
            ProjectConstants.FormatDisplayVersion(ProjectConstants.Version, expectedChannel)
        ));
    }

    [TestCase("2.1.0", "stable", "v2.1.0")]
    [TestCase("2.1.0", "dev", "v2.1.0 (dev)")]
    [TestCase("2.1.0", "preview", "v2.1.0 (preview)")]
    [TestCase("2.1.0", "local", "v2.1.0 (local)")]
    [TestCase("3.0.0", "stable", "v3.0.0")]
    [TestCase("3.0.0", "dev", "v3.0.0 (dev)")]
    [TestCase("3.0.0", "preview", "v3.0.0 (preview)")]
    [TestCase("3.0.0", "local", "v3.0.0 (local)")]
    public void FormatDisplayVersionUsesCanonicalVersionAndDeploymentChannel(string version, string channel, string expected) {
        Assert.That(ProjectConstants.FormatDisplayVersion(version, channel), Is.EqualTo(expected));
    }

    [Test]
    public void FormatDisplayVersionRejectsUnknownDeploymentChannels() {
        Assert.That(() => ProjectConstants.FormatDisplayVersion("2.1.0", "unknown"), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
