namespace YGOProbabilityCalculatorBlazor.Constants;

public static class ProjectConstants {
    public const string Version = "2.1.0";

#if APP_DEPLOYMENT_CHANNEL_STABLE
    public const string DeploymentChannel = "stable";
#elif APP_DEPLOYMENT_CHANNEL_DEV
    public const string DeploymentChannel = "dev";
#elif APP_DEPLOYMENT_CHANNEL_PREVIEW
    public const string DeploymentChannel = "preview";
#elif APP_DEPLOYMENT_CHANNEL_LOCAL
    public const string DeploymentChannel = "local";
#else
#error A deployment channel must be selected by the application project.
#endif

    public static string DisplayVersion => FormatDisplayVersion(Version, DeploymentChannel);

    public static string FormatDisplayVersion(string version, string deploymentChannel) {
        string suffix = deploymentChannel switch {
            "stable" => string.Empty,
            "dev" => " (dev)",
            "preview" => " (preview)",
            "local" => " (local)",
            _ => throw new ArgumentOutOfRangeException(
                nameof(deploymentChannel),
                deploymentChannel,
                "Unknown deployment channel."
            )
        };

        return $"v{version}{suffix}";
    }
}
