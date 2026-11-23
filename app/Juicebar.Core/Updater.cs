using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Juicebar.Core;

// Update check and one-click update.
//   Check:   the public GitHub API for this repository's latest release (anonymous, generic User-Agent;
//            nothing about the user or the computer is sent).
//   Install: only when the user clicks "Update now". The package for this system is downloaded from this
//            repository's release assets, its SHA-256 must match the release's SHA256SUMS.txt, and only then
//            it replaces the installed copy. The previous copy is kept as a backup until the next start.
public static class Updater
{
    public const string Repo = "hydrxgenn-sv/Juicebar";
    static string AssetPrefix => $"https://github.com/{Repo}/releases/download/";

    public sealed record Release(Version Version, string Tag, string Url, string Notes, IReadOnlyDictionary<string, string> Assets);

    // Ok = GitHub answered (Release is null when nothing has been published yet).
    // Ok = false when the check failed (no network, rate limit...).
    public sealed record Result(bool Ok, Release? Release);

    public enum Stage { Downloading, Verifying, Installing }

    const UnixFileMode Mode755 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                 UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    public static Version Current
    {
        get
        {
            Version v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string CurrentText => Current.ToString(3);

    static HttpClient Http(int seconds)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(seconds) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Juicebar-updater");
        return http;
    }

    public static async Task<Result> CheckAsync()
    {
        try
        {
            using HttpClient http = Http(10);
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
            JsonElement root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            string url = root.GetProperty("html_url").GetString() ?? "";
            string notes = root.TryGetProperty("body", out JsonElement b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? v)) return null;
            // Only links and downloads from this repository are ever used, whatever the answer says.
            if (!url.StartsWith($"https://github.com/{Repo}/", StringComparison.OrdinalIgnoreCase))
                url = $"https://github.com/{Repo}/releases/latest";
            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("assets", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
                foreach (JsonElement a in list.EnumerateArray())
                {
                    string? name = a.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                    string? dl = a.TryGetProperty("browser_download_url", out JsonElement d) ? d.GetString() : null;
                    if (name != null && dl != null && dl.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase)) assets[name] = dl;
                }
            return new Release(new Version(v.Major, v.Minor, Math.Max(0, v.Build)), tag, url, notes, assets);
        }
        catch (Exception ex)
        {
            Log.Write("update check: " + ex.Message);
            return null;
        }
    }

    public static bool IsNewer(Release r) => r.Version > Current;

    // Package published for this system, or null when there is none (e.g. Windows on ARM).
    public static string? AssetName()
    {
        Architecture arch = RuntimeInformation.OSArchitecture;
        if (OperatingSystem.IsWindows()) return arch == Architecture.X64 ? "Juicebar-win-x64.zip" : null;
        if (OperatingSystem.IsLinux()) return arch == Architecture.X64 ? "Juicebar-linux-x64.tar.gz" : null;
        if (OperatingSystem.IsMacOS()) return arch == Architecture.Arm64 ? "Juicebar-osx-arm64.zip" : "Juicebar-osx-x64.zip";
        return null;
    }

    public static bool CanInstall(Release r) =>
        Environment.ProcessPath != null && AssetName() is string name && r.Assets.ContainsKey(name) && r.Assets.ContainsKey("SHA256SUMS.txt");

    // Downloads, verifies and installs the release; returns the executable to start.
    // Throws with a readable message when something fails; the installed copy stays untouched then.
    public static async Task<string> InstallAsync(Release r, Action<Stage, double> progress, CancellationToken ct = default)
    {
        string name = AssetName() ?? throw new InvalidOperationException("there is no package for this system");
        if (!r.Assets.TryGetValue(name, out string? packageUrl)) throw new InvalidOperationException("the release has no package for this system");
        if (!r.Assets.TryGetValue("SHA256SUMS.txt", out string? sumsUrl)) throw new InvalidOperationException("the release has no checksum file");
        string dir = Path.Combine(AppPaths.Dir, "update");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        using HttpClient http = Http(900);
        string sums = await http.GetStringAsync(sumsUrl, ct);
        string? expected = null;
        foreach (string line in sums.Split('\n'))
        {
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[^1].TrimStart('*') == name) { expected = parts[0].ToLowerInvariant(); break; }
        }
        if (expected == null) throw new InvalidOperationException("the checksum file does not list " + name);

        string package = Path.Combine(dir, name);
        using (HttpResponseMessage resp = await http.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1, done = 0;
            await using Stream src = await resp.Content.ReadAsStreamAsync(ct);
            await using FileStream dst = File.Create(package);
            var buffer = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress(Stage.Downloading, (double)done / total);
            }
        }

        progress(Stage.Verifying, 1);
        string actual;
        await using (FileStream fs = File.OpenRead(package))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
        if (actual != expected)
        {
            File.Delete(package);
            throw new InvalidOperationException("the download does not match its SHA-256 checksum, so nothing was changed");
        }

        progress(Stage.Installing, 1);
        return InstallPackage(package, Path.Combine(dir, "files"), Environment.ProcessPath!);
    }

    // Replaces 'currentExe' (or, on macOS, its .app bundle) with the copy inside a verified package.
    // A running program can be renamed but not overwritten, so the current copy is moved aside first
    // and put back if anything goes wrong.
    public static string InstallPackage(string package, string extractDir, string currentExe)
    {
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        Directory.CreateDirectory(extractDir);
        if (package.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var gz = new GZipStream(File.OpenRead(package), CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gz, extractDir, overwriteFiles: true);
        }
        else ZipFile.ExtractToDirectory(package, extractDir, overwriteFiles: true);

        if (OperatingSystem.IsMacOS())
        {
            string bundle = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(currentExe)!, "..", ".."));   // .../Juicebar.app
            string fresh = Directory.GetDirectories(extractDir, "Juicebar.app", SearchOption.AllDirectories).FirstOrDefault()
                           ?? throw new InvalidOperationException("the package does not contain Juicebar.app");
            string backup = bundle + ".old";
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            Directory.Move(bundle, backup);
            try { CopyDirectory(fresh, bundle); }
            catch { if (Directory.Exists(bundle)) Directory.Delete(bundle, true); Directory.Move(backup, bundle); throw; }
            string exe = Path.Combine(bundle, "Contents", "MacOS", "Juicebar");
            File.SetUnixFileMode(exe, Mode755);
            return exe;
        }

        string fileName = OperatingSystem.IsWindows() ? "Juicebar.exe" : "Juicebar";
        string newExe = Directory.GetFiles(extractDir, fileName, SearchOption.AllDirectories).FirstOrDefault()
                        ?? throw new InvalidOperationException("the package does not contain " + fileName);
        string old = currentExe + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(currentExe, old);
        try { File.Copy(newExe, currentExe); }
        catch { File.Move(old, currentExe); throw; }
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(currentExe, Mode755);
        return currentExe;
    }

    static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (string d in Directory.GetDirectories(from)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
    }

    // Removes what a previous update left behind (backup copy, downloaded package). Safe to call any time.
    public static void CleanupAfterUpdate()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe != null)
            {
                if (OperatingSystem.IsMacOS())
                {
                    string old = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(exe)!, "..", "..")) + ".old";
                    if (Directory.Exists(old)) Directory.Delete(old, true);
                }
                else if (File.Exists(exe + ".old")) File.Delete(exe + ".old");
            }
            string dir = Path.Combine(AppPaths.Dir, "update");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex) { Log.Write("update cleanup: " + ex.Message); }   // the old copy may still be closing
    }
}
