using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace BringLater;

internal sealed record UpdateResult(Version? Newer, string? Url, bool Failed);

/// <summary>
/// Compares this build with the newest GitHub release. Runs only when the user asks; Bring Later
/// makes no other network requests.
/// </summary>
internal static class UpdateChecker
{
    public const string ReleasesPage = "https://github.com/shivashis-adhikari/bring-later/releases";
    private const string Api = "https://api.github.com/repos/shivashis-adhikari/bring-later/releases?per_page=20";

    public static Version Current { get; } = ParseVersion(
        typeof(UpdateChecker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? new Version(0, 0, 0);

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    public static async Task<UpdateResult> CheckAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"BringLater/{CurrentText}");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri(Api)).ConfigureAwait(true));

            Version? newest = null;
            string? url = null;
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.GetProperty("draft").GetBoolean())
                    continue;
                var version = ParseVersion(release.GetProperty("tag_name").GetString());
                if (version is not null && (newest is null || version > newest))
                {
                    newest = version;
                    url = release.GetProperty("html_url").GetString();
                }
            }
            return newest is not null && newest > Current ? new UpdateResult(newest, url ?? ReleasesPage, false) : new UpdateResult(null, null, false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Log.Error("Update check failed", ex);
            return new UpdateResult(null, null, true);
        }
    }

    /// <summary>"v1.2.3", "1.2.3-beta" and "1.2.3+abc" all read as 1.2.3.</summary>
    private static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var core = text.TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(core, out var version) ? new Version(version.Major, version.Minor, Math.Max(0, version.Build)) : null;
    }
}
