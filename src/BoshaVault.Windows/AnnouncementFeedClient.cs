using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoshaVault.Core;

namespace BoshaVault.Windows;

internal sealed class AnnouncementNotice
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("body")] public string Body { get; init; } = "";
    [JsonPropertyName("severity")] public string Severity { get; init; } = "";
    [JsonPropertyName("notify")] public bool Notify { get; init; }
    [JsonPropertyName("publishedAt")] public string PublishedAt { get; init; } = "";
    [JsonPropertyName("expiresAt")] public string? ExpiresAt { get; init; }
}

internal static class AnnouncementFeedClient
{
    private const int MaxResponseBytes = 65536;
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
    }) { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = false,
        MaxDepth = 4,
    };

    public static async Task<IReadOnlyList<AnnouncementNotice>> FetchAsync(
        string version, string locale, CancellationToken cancellationToken = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(version,
                @"^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){2}$") ||
            locale is not ("en" or "ar"))
            throw new ArgumentException("Invalid announcement feed query.");

        string path = "functions/v1/lockvyn-admin/announcements?platform=windows&version=" +
            Uri.EscapeDataString(version) + "&locale=" + Uri.EscapeDataString(locale);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(CloudProject.Url), path));
        request.Headers.Add("apikey", CloudProject.PublishableKey);
        using var response = await Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Announcements are unavailable.", null, response.StatusCode);
        byte[] bytes = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
        try
        {
            var notices = JsonSerializer.Deserialize<List<AnnouncementNotice>>(bytes, Json)
                ?? throw new InvalidDataException("Invalid announcement response.");
            if (notices.Count > 20 || notices.Any(x =>
                    !Guid.TryParseExact(x.Id, "D", out _) ||
                    string.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 120 ||
                    string.IsNullOrWhiteSpace(x.Body) || x.Body.Length > 800 ||
                    x.Severity is not ("info" or "warning" or "critical")))
                throw new InvalidDataException("Invalid announcement response.");
            return notices;
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxResponseBytes)
            throw new InvalidDataException("Announcement response exceeds its size limit.");
        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] chunk = new byte[4096];
        try
        {
            int count;
            while ((count = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (output.Length + count > MaxResponseBytes)
                    throw new InvalidDataException("Announcement response exceeds its size limit.");
                output.Write(chunk, 0, count);
            }
            return output.ToArray();
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(chunk); }
    }
}
