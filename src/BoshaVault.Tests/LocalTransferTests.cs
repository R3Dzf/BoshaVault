using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoshaVault.Core;

public static class LocalTransferTests
{
    public static async Task Run()
    {
        int passed = 0, exported = 0, merged = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL " + label); passed++; Console.WriteLine("PASS " + label); }
        byte[] encrypted = RandomNumberGenerator.GetBytes(256);
        var diagnostics = new List<string>();
        var imported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new LocalTransferServer(() => { Interlocked.Increment(ref exported); return encrypted.ToArray(); },
            bytes => { if (!bytes.SequenceEqual(encrypted)) throw new VaultException("Synthetic payload rejected."); Interlocked.Increment(ref merged); imported.TrySetResult(); },
            stage => { lock (diagnostics) diagnostics.Add(stage); });
        using var pairing = JsonDocument.Parse(Convert.FromBase64String(Pad(server.Start()[4..])));
        var p = pairing.RootElement; string host = p.GetProperty("host").GetString()!, token = p.GetProperty("token").GetString()!, pin = p.GetProperty("cert").GetString()!;
        int port = p.GetProperty("port").GetInt32();
        using var handler = new HttpClientHandler { UseProxy = false, ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert != null && Convert.ToHexString(SHA256.HashData(cert.RawData)).Equals(pin, StringComparison.OrdinalIgnoreCase) };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        async Task<HttpResponseMessage> Request(string method, byte[]? body = null, string? bearer = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"https://{host}:{port}/vault");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer ?? token);
            if (body != null) request.Content = new ByteArrayContent(body);
            return await http.SendAsync(request);
        }
        using (var rejected = await Request("GET", bearer: new string('A', 43))) Check(rejected.StatusCode == HttpStatusCode.Forbidden, "wrong token is rejected without consuming a download");
        using var stale = new TcpClient(); await stale.ConnectAsync(host, port);
        using (var response = await Request("GET"))
        {
            Check(response.IsSuccessStatusCode, "a stalled TLS client does not block the legitimate transfer");
            Check((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(encrypted), "pinned TLS downloads the complete encrypted bytes");
            Check(response.Headers.GetValues("X-Lockvyn-Transfer").Single() == "2", "new server advertises idempotent upload receipts");
        }
        using (var response = await Request("GET")) Check((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(encrypted) && exported == 1, "retry downloads use the same encrypted snapshot");
        using (var response = await Request("GET")) Check(response.IsSuccessStatusCode, "third bounded download remains available");
        using (var response = await Request("GET")) Check(response.StatusCode == HttpStatusCode.Conflict, "downloads remain bounded after three attempts");
        using (var response = await Request("POST", new byte[99])) Check(!response.IsSuccessStatusCode && merged == 0, "invalid upload size does not merge data");
        // Send a valid body and deliberately discard its acknowledgement.
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(host, port);
            await using var tls = new SslStream(tcp.GetStream(), false, (_, cert, _, _) => cert != null && Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())).Equals(pin, StringComparison.OrdinalIgnoreCase));
            await tls.AuthenticateAsClientAsync(host);
            await tls.WriteAsync(Encoding.ASCII.GetBytes($"POST /vault HTTP/1.1\r\nHost: {host}\r\nAuthorization: Bearer {token}\r\nContent-Length: {encrypted.Length}\r\n\r\n"));
            await tls.WriteAsync(encrypted); await tls.FlushAsync();
            await imported.Task.WaitAsync(TimeSpan.FromSeconds(4));
        }
        using (var response = await Request("POST", encrypted)) Check(response.IsSuccessStatusCode && merged == 1, "lost upload acknowledgement is recovered without merging twice");
        using (var response = await Request("POST", new byte[256])) Check(response.StatusCode == HttpStatusCode.Conflict && merged == 1, "completed pairing refuses a different encrypted upload");
        using (var response = await Request("GET")) Check(response.StatusCode == HttpStatusCode.Conflict, "completed pairing cannot download the vault again");
        Check(server.IsCompleted, "completion state is visible separately from receipt recovery");
        Check(LocalTransferServer.PrivateInterfaces().All(n => IPAddress.TryParse(n.Host, out var a) && LocalTransferServer.PrivateAddress(a)), "interface choices contain only private IPv4 addresses");
        server.Dispose();
        Check(!server.IsActive, "stop/lock immediately invalidates the listener");
        await Task.Delay(100);
        lock (diagnostics) Check(diagnostics.All(line => !line.Contains(token) && !line.Contains(pin) && !line.Contains(host)), "diagnostics never contain pairing credentials or addresses");
        int attempts = 0;
        using var retryServer = new LocalTransferServer(() => encrypted.ToArray(), _ => { if (++attempts == 1) throw new VaultException("Synthetic merge failure."); });
        using var secondPair = JsonDocument.Parse(Convert.FromBase64String(Pad(retryServer.Start()[4..])));
        var q = secondPair.RootElement;
        using var secondHandler = new HttpClientHandler { UseProxy = false, ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert != null && Convert.ToHexString(SHA256.HashData(cert.RawData)).Equals(q.GetProperty("cert").GetString(), StringComparison.OrdinalIgnoreCase) };
        using var second = new HttpClient(secondHandler) { Timeout = TimeSpan.FromSeconds(5) };
        second.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", q.GetProperty("token").GetString());
        string url = $"https://{q.GetProperty("host").GetString()}:{q.GetProperty("port").GetInt32()}/vault";
        using (await second.GetAsync(url)) { }
        using (var rejected = await second.PostAsync(url, new ByteArrayContent(encrypted))) Check(rejected.StatusCode == HttpStatusCode.Conflict && !retryServer.IsCompleted, "a rejected merge preserves retry availability");
        using (var retry = await second.PostAsync(url, new ByteArrayContent(encrypted))) Check(retry.IsSuccessStatusCode && retryServer.IsCompleted && attempts == 2, "a valid retry can complete after a merge failure");
        Console.WriteLine($"RESULT: {passed} real local transfer/TLS lifecycle checks passed.");
    }
    private static string Pad(string raw) { raw = raw.Replace('-', '+').Replace('_', '/'); return raw.PadRight((raw.Length + 3) / 4 * 4, '='); }

    public static async Task NativeHost(string folder)
    {
        Directory.CreateDirectory(folder);
        string password = "correct horse battery staple café 🔑", device = "11111111-1111-1111-1111-111111111111";
        string path = Path.Combine(folder, "native-server.boshavault");
        using var vault = await VaultSession.Create(path, password, device);
        vault.Upsert(new VaultEntry { Title = "Windows LAN fixture", Username = "synthetic@example.invalid", Password = "SyntheticWindowsOffline42!", Url = "https://example.com" });
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new LocalTransferServer(vault.ExportEncrypted, bytes => { vault.MergeEncrypted(bytes); done.TrySetResult(); });
        string pairing = Path.Combine(folder, "pairing.txt");
        File.WriteAllText(pairing, server.Start());
        Console.WriteLine("Synthetic native LAN host ready.");
        try
        {
            await done.Task.WaitAsync(TimeSpan.FromMinutes(2));
            if (!vault.Data.Entries.Any(e => e.Title == "Native LAN update" && e.Password == "SyntheticNativeOffline42!")) throw new Exception("Native Android update was not merged.");
            Console.WriteLine("NATIVE_LAN_HOST_PASS: Android encrypted edits reopen and merge in .NET.");
            await Task.Delay(500);
        }
        finally { File.Delete(pairing); }
    }
}
