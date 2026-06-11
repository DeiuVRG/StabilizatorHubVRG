namespace StabilizatorHub.Infrastructure.Demo;

/// <summary>
/// Demo mode configuration (appsettings: "Demo"). When enabled, a simulated
/// device feeds realistic telemetry through the real ingestion pipeline and a
/// read-only demo account can be entered straight from the login page.
/// </summary>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    public bool Enabled { get; set; }

    /// <summary>Identity of the shared read-only demo account.</summary>
    public string Email { get; set; } = "demo@stabilizatorhub.local";

    public string DeviceId { get; set; } = "DEMO00000001";

    public string DeviceName { get; set; } = "Demo stabilizer (simulated)";

    /// <summary>How much history is generated on first run.</summary>
    public int BackfillDays { get; set; } = 30;
}
