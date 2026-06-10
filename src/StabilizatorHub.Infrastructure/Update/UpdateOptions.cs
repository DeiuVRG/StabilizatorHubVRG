namespace StabilizatorHub.Infrastructure.Update;

/// <summary>Self-update configuration (appsettings: "Update").</summary>
public sealed class UpdateOptions
{
    public const string SectionName = "Update";

    /// <summary>Allows disabling the whole self-update mechanism.</summary>
    public bool Enabled { get; set; } = true;

    public string GitHubOwner { get; set; } = "DeiuVRG";

    public string GitHubRepo { get; set; } = "StabilizatorHubVRG";

    /// <summary>
    /// File watched by the stabilizatorhub-update systemd path unit.
    /// Defaults to {DataDirectory}/update.requested when left empty.
    /// </summary>
    public string TriggerFilePath { get; set; } = string.Empty;
}
