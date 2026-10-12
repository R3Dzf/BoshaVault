using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace BoshaVault.Windows;

/// <summary>Only displays public GitHub release notes; no vault/network secrets.</summary>
internal static class ReleaseNotesSummary
{
    internal sealed record Summary(string[] Added, string[] Improved, string[] Fixed, bool Verified);
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Lockvyn-Update-UI/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string Plain(string text)
    {
        text = text.Trim().TrimStart('-', '*', ' ');
        text = text.Replace("**", "").Replace("`", "").Replace("__", "");
        return text.Length > 170 ? text[..167] + "..." : text;
    }

    internal static Summary Parse(string markdown)
    {
        var added = new List<string>();
        var improved = new List<string>();
        var fixedItems = new List<string>();
        string category = "improved";
        foreach (string raw in markdown.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('#'))
            {
                string title = line.TrimStart('#', ' ').ToLowerInvariant();
                if (title.Contains("fix") || title.Contains("bug") || title.Contains("repair")) category = "fixed";
                else if (title.Contains("add") || title.Contains("new") || title.Contains("feature")) category = "added";
                else if (title.Contains("improv") || title.Contains("change") || title.Contains("polish")) category = "improved";
                continue;
            }
            if (!line.StartsWith("- ") && !line.StartsWith("* ")) continue;
            string item = Plain(line);
            if (item.Length == 0) continue;
            string lower = item.ToLowerInvariant();
            string target = lower.StartsWith("fix") || lower.StartsWith("correct") ? "fixed"
                : lower.StartsWith("add") || lower.StartsWith("new ") || lower.StartsWith("introduc") ? "added" : category;
            var list = target == "fixed" ? fixedItems : target == "added" ? added : improved;
            if (list.Count < 4) list.Add(item);
        }
        return new Summary(added.ToArray(), improved.ToArray(), fixedItems.ToArray(), false);
    }

    internal static async Task<Summary> LoadAsync(string version, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
            return new Summary([], [], [], false);
        try
        {
            using var response = await Client.GetAsync(
                "https://api.github.com/repos/R3Dzf/BoshaVault/releases/tags/windows-v" + version,
                HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return new Summary([], [], [], false);
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            byte[] bytes = new byte[4096]; int read;
            while ((read = await stream.ReadAsync(bytes, ct)) != 0)
            {
                if (buffer.Length + read > 65536) return new Summary([], [], [], false);
                buffer.Write(bytes, 0, read);
            }
            using var doc = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tag) || tag.GetString() != "windows-v" + version)
                return new Summary([], [], [], false);
            string body = root.TryGetProperty("body", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString() ?? "" : "";
            var parsed = Parse(body);
            return parsed with { Verified = true };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or JsonException)
        {
            return new Summary([], [], [], false);
        }
    }
}
