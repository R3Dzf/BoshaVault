using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using BoshaVault.Core;

namespace BoshaVault.Windows;

internal static class SupportTicketClient
{
    internal static async Task<JsonDocument> SendAsync(HttpClient http,
        SupabaseCloudAccount.Session session, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(CloudProject.Url), "functions/v1/lockvyn-admin/tickets"));
        request.Headers.Add("apikey", CloudProject.PublishableKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(body);
        try
        {
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            byte[] bytes = await SupabaseCloudAccount.ReadBounded(response.Content, 65536, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    string message = "Support could not complete this request. Try again when connected.";
                    try
                    {
                        using var error = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
                        if (error.RootElement.TryGetProperty("error", out var field) &&
                            field.ValueKind == JsonValueKind.String &&
                            field.GetString() is { Length: > 0 and <= 180 } safe)
                            message = safe;
                    }
                    catch (JsonException) { }
                    throw new HttpRequestException(message, null, response.StatusCode);
                }
                return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }
}
