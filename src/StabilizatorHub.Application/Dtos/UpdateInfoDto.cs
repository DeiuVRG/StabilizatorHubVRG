namespace StabilizatorHub.Application.Dtos;

/// <summary>Result of an update check against GitHub Releases.</summary>
public sealed record UpdateInfoDto(
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string? ReleaseUrl,
    string? ReleaseNotes,
    string? Error);
