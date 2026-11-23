using System.Reflection;
using System.Text.Json;

namespace Juicebar.Core;

// Checks the project's latest GitHub release. Only the public GitHub API is contacted, with a generic
// User-Agent; nothing about the user or the computer is sent.
public static class Updater
{
    public const string Repo = "hydrxgenn-sv/Juicebar";

    public sealed record Release(Version Version, string Tag, string Url);

    public static Version Current
    {
        get
        {
            Version v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string CurrentText => Current.ToString(3);

    // Ok = GitHub answered (Release is null when nothing has been published yet).
    // Ok = false when the check failed (no network, rate limit...).
    public sealed record Result(bool Ok, Release? Release);

    public static async Task<Result> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Juicebar-update-check");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage resp = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return new Result(true, null);
            if (!resp.IsSuccessStatusCode) return new Result(false, null);
            return new Result(true, await ParseAsync(resp));
        }
        catch (Exception ex)
        {
            Log.Write("update check: " + ex.Message);
            return new Result(false, null);
        }
    }

    static async Task<Release?> ParseAsync(HttpResponseMessage resp)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            string url = doc.RootElement.GetProperty("html_url").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? v)) return null;
            return new Release(new Version(v.Major, v.Minor, Math.Max(0, v.Build)), tag, url);
        }
        catch (Exception ex)
        {
            Log.Write("update check: " + ex.Message);
            return null;
        }
    }

    public static bool IsNewer(Release r) => r.Version > Current;
}
