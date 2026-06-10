using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Dtos;

namespace StabilizatorHub.Infrastructure.Update;

/// <summary>
/// Compares the running version against the latest GitHub release
/// (GET /repos/{owner}/{repo}/releases/latest, anonymous access).
/// </summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private readonly HttpClient _http;
    private readonly UpdateOptions _options;
    private readonly ILogger<GitHubUpdateChecker> _logger;

    public GitHubUpdateChecker(HttpClient http, IOptions<UpdateOptions> options, ILogger<GitHubUpdateChecker> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress = new Uri("https://api.github.com/");
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("StabilizatorHubVRG", AppVersion.Current));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateInfoDto> CheckAsync(CancellationToken ct = default)
    {
        var current = AppVersion.Current;

        if (!_options.Enabled)
        {
            return new UpdateInfoDto(current, null, false, null, null, "Self-update is disabled.");
        }

        try
        {
            using var response = await _http.GetAsync(
                $"repos/{_options.GitHubOwner}/{_options.GitHubRepo}/releases/latest", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateInfoDto(current, null, false, null, null, "No releases published yet.");
            }

            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
            var notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() : null;

            var latestVersion = AppVersion.ParseVersion(tag);
            var currentVersion = AppVersion.ParseVersion(current);
            var updateAvailable = latestVersion is not null
                                  && currentVersion is not null
                                  && latestVersion > currentVersion;

            return new UpdateInfoDto(current, tag, updateAvailable, url, Truncate(notes, 2000), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Update check against GitHub failed");
            return new UpdateInfoDto(current, null, false, null, null,
                "Could not reach GitHub to check for updates.");
        }
    }

    private static string? Truncate(string? text, int max) =>
        text is null || text.Length <= max ? text : text[..max];
}
