using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace BoshaVault.Core;

// A short-lived, single-vault, private-address-only HTTPS transfer endpoint.
// Keep any logging limited to stages and exception types: the capability URL, token,
// vault data and certificate must NEVER be written to disk.
public sealed class LocalTransferServer(Func<byte[]> export, Action<byte[]> import, Action<string>? diagnostics = null) : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly byte[] token = RandomNumberGenerator.GetBytes(32);
    private TcpListener? listener;
    private X509Certificate2? cert;
    private DateTime deadline;
    private int downloads, uploads;
    private volatile bool running;
    private volatile string? lastIssue;
    public DateTime ExpiresUtc => deadline;
    public bool IsActive => running && !stop.IsCancellationRequested && DateTime.UtcNow < deadline;
    public string? LastIssue => lastIssue;

    private void Diagnostic(string stage, Exception? ex = null)
    {
        // Do not include exception messages. Those may contain request headers or secrets.
        try
        {
            string details = ex is null ? "" : $" | {ex.GetType().Name} | hresult=0x{ex.HResult:X8}";
            if (ex?.InnerException is { } inner)
                details += $" | inner={inner.GetType().Name} | inner-hresult=0x{inner.HResult:X8}";
            diagnostics?.Invoke(stage + details);
        }
        catch { /* Diagnostics must never break the encryption/transfer path. */ }
    }

    public string Start(string? chosenHost = null)
    {
        string host = chosenHost ?? FindPrivateHost();
        if (!IPAddress.TryParse(host, out var address) || !PrivateAddress(address))
            throw new VaultException("Choose the private IPv4 address of your Wi-Fi interface.");
        try
        {
            cert = TemporaryTlsCertificate.Create();
            listener = new TcpListener(address, 0); // Never listen on 0.0.0.0 or a public IP.
            listener.Start(4);
            deadline = DateTime.UtcNow.AddMinutes(3);
            running = true;
            _ = Run();
            string raw = JsonSerializer.Serialize(new
            {
                v = 1,
                host,
                port = ((IPEndPoint)listener.LocalEndpoint).Port,
                cert = Convert.ToHexString(SHA256.HashData(cert.RawData)).ToLowerInvariant(),
                token = Base64Url(token)
            });
            Diagnostic("listener-ready");
            return "BV1:" + Base64Url(Encoding.UTF8.GetBytes(raw));
        }
        catch (Exception ex)
        {
            Diagnostic("listener-start-failed", ex);
            running = false;
            listener?.Stop();
            cert?.Dispose();
            CryptographicOperations.ZeroMemory(token);
            throw;
        }
    }

    public static bool PrivateAddress(IPAddress a)
    {
        byte[] b = a.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] >= 16 && b[1] <= 31);
    }

    public static string FindPrivateHost()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 1 : 2)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && PrivateAddress(a))?.ToString()
                ?? throw new VaultException("No private IPv4 address found. Connect both devices to the same Wi-Fi.");
        }
        catch (NetworkInformationException)
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && PrivateAddress(a))?.ToString()
                ?? throw new VaultException("Cannot discover your private IPv4 address. Enter it manually.");
        }
    }

    private async Task Run()
    {
        stop.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            // A broken handshake, a stale client, or an incomplete GET must not kill the listener.
            for (int i = 0; i < 16 && !stop.IsCancellationRequested && uploads == 0; i++)
            {
                using var client = await listener!.AcceptTcpClientAsync(stop.Token);
                if (client.Client.RemoteEndPoint is not IPEndPoint peer || !PrivateAddress(peer.Address))
                {
                    lastIssue = "A non-private network client was rejected.";
                    Diagnostic("peer-rejected");
                    continue;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                string stage = "tls";
                try
                {
                    await using var tls = new SslStream(client.GetStream(), false);
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = cert,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        ClientCertificateRequired = false
                    }, timeout.Token);
                    stage = "http";
                    await Handle(tls, timeout.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    break; // Normal expiry/lock/tray stop.
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Diagnostic(stage + "-failed", ex);
                    lastIssue = stage == "tls"
                        ? "TLS handshake closed. Check transfer.log for the error type, and verify Windows firewall, selected Wi-Fi IPv4 and the temporary certificate."
                        : "Transfer interrupted. Check transfer.log for the error type; try the current QR again while the countdown is active.";
                    // Continue accepting legitimate retries until expiry or a successful upload.
                }
            }
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            if (!stop.IsCancellationRequested) { lastIssue = "The Windows listener stopped unexpectedly. Start another transfer."; Diagnostic("accept-failed", ex); }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            lastIssue = "The Windows listener stopped unexpectedly. Start another transfer.";
            Diagnostic("listener-failed", ex);
        }
        finally
        {
            running = false;
            listener?.Stop();
            cert?.Dispose();
            CryptographicOperations.ZeroMemory(token);
            Diagnostic("listener-stopped");
        }
    }

    private async Task Handle(SslStream tls, CancellationToken ct)
    {
        using var headers = new MemoryStream();
        var one = new byte[1];
        int tail = 0;
        while (headers.Length < 4096)
        {
            if (await tls.ReadAsync(one, ct) == 0) { Diagnostic("http-disconnected-before-headers"); return; }
            headers.WriteByte(one[0]);
            tail = (tail << 8) | one[0];
            if (tail == 0x0D0A0D0A) break;
        }
        if (tail != 0x0D0A0D0A) { Diagnostic("http-invalid-headers"); return; }

        string[] lines = Encoding.ASCII.GetString(headers.ToArray()).Split("\r\n", StringSplitOptions.None);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1).Where(l => l.Length > 0))
        {
            int split = line.IndexOf(':');
            if (split < 1 || !fields.TryAdd(line[..split].Trim(), line[(split + 1)..].Trim()))
            { await Respond(tls, 400, [], ct); Diagnostic("http-invalid-fields"); return; }
        }

        string expected = "Bearer " + Base64Url(token);
        if (DateTime.UtcNow > deadline || !fields.TryGetValue("Authorization", out var auth) || auth.Length != expected.Length ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(auth), Encoding.ASCII.GetBytes(expected)))
        {
            Diagnostic("http-auth-rejected");
            await Respond(tls, 403, [], ct);
            return;
        }
        if (fields.ContainsKey("Transfer-Encoding")) { await Respond(tls, 400, [], ct); Diagnostic("http-chunked-rejected"); return; }

        // A transient TCP loss after a successful write cannot prove Android read all bytes.
        // Permit at most three authenticated downloads of this encrypted blob, before POST.
        if (lines[0] == "GET /vault HTTP/1.1" && downloads < 3 && uploads == 0)
        {
            byte[] bytes;
            try { bytes = export(); }
            catch (Exception ex) when (ex is VaultException or IOException)
            {
                lastIssue = "Vault unavailable. Unlock the vault and start a new transfer.";
                Diagnostic("vault-export-failed", ex);
                await Respond(tls, 409, [], ct); return;
            }
            downloads++; // Bounded even if the response is interrupted midstream.
            await Respond(tls, 200, bytes, ct);
            Diagnostic("encrypted-download-sent");
            lastIssue = "Phone received the encrypted transfer response. Complete the passphrase prompt before expiry.";
            return;
        }
        if (lines[0] == "POST /vault HTTP/1.1" && downloads > 0 && uploads == 0 &&
            fields.TryGetValue("Content-Length", out string? size) && int.TryParse(size, out int n) && n >= 100 && n <= VaultCodec.MaxFileBytes)
        {
            var bytes = new byte[n];
            await tls.ReadExactlyAsync(bytes, ct);
            import(bytes); // Validates/decrypts/merges locally in the unlocked Windows session.
            uploads++;
            await Respond(tls, 200, Encoding.UTF8.GetBytes("Merged"), ct);
            Diagnostic("encrypted-upload-merged");
            return;
        }
        await Respond(tls, 400, [], ct);
        Diagnostic("http-state-rejected");
    }

    private static async Task Respond(SslStream tls, int status, byte[] body, CancellationToken ct)
    {
        byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Rejected")}\r\nContent-Type: application/octet-stream\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await tls.WriteAsync(header, ct);
        await tls.WriteAsync(body, ct);
        await tls.FlushAsync(ct);
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public void Dispose()
    {
        stop.Cancel();
        listener?.Stop();
        // The pending SslStream handshake finishes before the cert is disposed by Run().
    }
}
