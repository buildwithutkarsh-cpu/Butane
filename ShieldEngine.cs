using System.IO;
using System.Net;
using System.Net.Http;

namespace Butane;

// Host-level blocking engine for Butane Shield.
//
// - Lookups are O(number of labels in the host): the host and each parent domain are checked
//   against a HashSet, so the list can hold hundreds of thousands of entries.
// - Lists are loaded and parsed on a background thread. A small built-in list is active
//   immediately, and the full lists replace it atomically when ready.
// - Lists live in %LOCALAPPDATA%\Butane\filters\*.txt. Any extra .txt file you drop in that
//   folder (hosts format, plain domains, or simple ||domain^ rules) is loaded too.
// - Only whole-host rules are used. Path rules, regex rules, cosmetic rules, and rules with
//   $domain=/$script/$image-style restrictions are skipped on purpose, because turning them
//   into whole-host blocks would break sites.
internal sealed class ShieldEngine
{
    public static ShieldEngine Instance { get; } = new();

    private static readonly string FilterFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Butane", "filters");

    // Verify these URLs before shipping. Each list has its own license and terms.
    private static readonly (string FileName, string Url)[] Lists =
    {
        ("easylist.txt", "https://easylist.to/easylist/easylist.txt"),
        ("easyprivacy.txt", "https://easylist.to/easylist/easyprivacy.txt"),
        ("peterlowe.txt",
            "https://pgl.yoyo.org/adservers/serverlist.php?hostformat=hosts&showintro=0&mimetype=plaintext"),
    };

    private static readonly TimeSpan MaxListAge = TimeSpan.FromDays(7);

    // Used until the downloaded lists are loaded, and as the fallback if they never are.
    private static readonly string[] BuiltIn =
    {
        "doubleclick.net", "googlesyndication.com", "googleadservices.com",
        "google-analytics.com", "adservice.google.com", "connect.facebook.net",
        "scorecardresearch.com", "hotjar.com", "taboola.com", "outbrain.com",
        "adnxs.com", "criteo.com", "amazon-adsystem.com",
        "adsrvr.org", "pubmatic.com", "rubiconproject.com", "openx.net", "casalemedia.com",
        "quantserve.com", "moatads.com", "mathtag.com", "bluekai.com", "demdex.net",
        "krxd.net", "2mdn.net", "adform.net", "smartadserver.com", "serving-sys.com",
        "mouseflow.com", "crazyegg.com", "fullstory.com", "ads-twitter.com",
        "analytics.twitter.com", "bat.bing.com", "clarity.ms", "analytics.tiktok.com",
        "px.ads.linkedin.com"
    };

    private static readonly char[] RuleTerminators = { '^', '/', '$', '*', ':', '|' };
    private static readonly HashSet<string> HarmlessOptions =
        new(StringComparer.OrdinalIgnoreCase) { "third-party", "3p", "important", "all" };

    // Swapped as a whole when a new list finishes loading, so readers never see a half-built set.
    private volatile HashSet<string> _blocked = new(BuiltIn, StringComparer.OrdinalIgnoreCase);
    private volatile HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);
    private int _started;

    public int BlockedHostCount => _blocked.Count;

    // True if the host, or any parent domain of it, is blocked (and not allowed by an exception rule).
    // Pass the ASCII/punycode host (Uri.IdnHost).
    public bool IsBlocked(string host)
    {
        var blocked = _blocked;
        var allowed = _allowed;
        string h = host.TrimEnd('.');
        if (h.Length == 0) return false;

        bool hit = false;
        int start = 0;
        while (true)
        {
            string candidate = start == 0 ? h : h.Substring(start);
            if (allowed.Contains(candidate)) return false;
            if (blocked.Contains(candidate)) hit = true;

            int dot = h.IndexOf('.', start);
            if (dot < 0) break;
            start = dot + 1;
            if (h.IndexOf('.', start) < 0) break; // never test a bare TLD such as "com"
        }

        return hit;
    }

    // Starts loading the cached lists (and refreshing stale ones). Safe to call more than once.
    public void Start(bool allowDownload)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _ = Task.Run(() => LoadAsync(allowDownload));
    }

    private async Task LoadAsync(bool allowDownload)
    {
        try
        {
            Directory.CreateDirectory(FilterFolder);
            Rebuild();
            if (!allowDownload) return;

            bool changed = false;
            using var http = CreateHttpClient();
            foreach (var (fileName, url) in Lists)
            {
                string path = Path.Combine(FilterFolder, fileName);
                if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < MaxListAge)
                    continue;

                if (await DownloadAsync(http, url, path)) changed = true;
            }

            if (changed) Rebuild();
        }
        catch (Exception)
        {
            // Filter lists are an optional upgrade. Any failure leaves the current set in place.
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All
        };
        var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 32 * 1024 * 1024
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Butane-FilterUpdater");
        return http;
    }

    private static async Task<bool> DownloadAsync(HttpClient http, string url, string path)
    {
        try
        {
            string text = await http.GetStringAsync(url);

            // Reject error pages and truncated responses so a bad download never replaces a good list.
            if (text.Length < 1000 || text.TrimStart().StartsWith('<')) return false;

            string temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, text);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Rebuild()
    {
        var blocked = new HashSet<string>(BuiltIn, StringComparer.OrdinalIgnoreCase);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(FilterFolder, "*.txt"))
        {
            try
            {
                ParseInto(File.ReadLines(file), blocked, allowed);
            }
            catch (IOException)
            {
                // Skip a list that is locked or unreadable.
            }
        }

        _blocked = blocked;
        _allowed = allowed;
    }

    // Accepts: hosts files ("0.0.0.0 example.com"), plain domain lists, and simple Adblock rules
    // of the form ||example.com^ (optionally with $third-party), plus @@||example.com^ exceptions.
    internal static void ParseInto(IEnumerable<string> lines, HashSet<string> blocked, HashSet<string> allowed)
    {
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length < 4) continue;
            if (line[0] is '!' or '#' or '[') continue;
            if (line.Contains("##") || line.Contains("#@#") || line.Contains("#?#") || line.Contains("#$#"))
                continue; // cosmetic filters

            if (line.StartsWith("@@||", StringComparison.Ordinal))
            {
                if (TryParseRule(line.Substring(4), out string? exception)) allowed.Add(exception);
                continue;
            }

            if (line.StartsWith("||", StringComparison.Ordinal))
            {
                if (TryParseRule(line.Substring(2), out string? host)) blocked.Add(host);
                continue;
            }

            if (line[0] is '|' or '/' or '@' or '-' or '_' or '.' || line.Contains('*') || line.Contains('$'))
                continue; // other Adblock syntax we deliberately do not support

            int comment = line.IndexOf('#');
            if (comment > 0) line = line.Substring(0, comment).TrimEnd();

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            string? domain = parts.Length switch
            {
                1 => parts[0],
                2 when parts[0] is "0.0.0.0" or "127.0.0.1" => parts[1],
                _ => null
            };

            if (domain != null && IsValidHost(domain)) blocked.Add(domain.ToLowerInvariant());
        }
    }

    // Parses the part of a rule after "||" or "@@||". Returns false for anything that is not a
    // plain whole-host rule.
    private static bool TryParseRule(string rest, out string host)
    {
        host = "";
        int end = rest.IndexOfAny(RuleTerminators);
        if (end <= 0 || rest[end] != '^') return false;

        string tail = rest.Substring(end + 1);
        if (tail.Length > 0 && tail != "|")
        {
            if (tail[0] != '$') return false;
            foreach (string option in tail.Substring(1).Split(','))
                if (!HarmlessOptions.Contains(option)) return false;
        }

        string candidate = rest.Substring(0, end);
        if (!IsValidHost(candidate)) return false;

        host = candidate.ToLowerInvariant();
        return true;
    }

    private static bool IsValidHost(string value)
    {
        if (value.Length is < 3 or > 253) return false;
        if (value[0] is '.' or '-' || value[^1] is '.' or '-') return false;
        if (value.Contains("..")) return false;

        foreach (char c in value)
        {
            bool ok = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '.' or '_';
            if (!ok) return false;
        }

        int lastDot = value.LastIndexOf('.');
        if (lastDot < 0) return false;

        string tld = value.Substring(lastDot + 1);
        if (tld.StartsWith("xn--", StringComparison.OrdinalIgnoreCase)) return true;
        return tld.Length >= 2 && tld.All(char.IsAsciiLetter);
    }
}
