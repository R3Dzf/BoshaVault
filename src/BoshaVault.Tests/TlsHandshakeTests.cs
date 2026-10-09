using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BoshaVault.Core;

// Run this on Windows to exercise Schannel with exactly the certificate loader
// used by BoshaVault's real Wi-Fi pairing service. No secrets or user vault involved.
public static class TlsHandshakeTests
{
    public static async Task Run()
    {
        using var certificate = TemporaryTlsCertificate.Create();
        byte[] expectedPin = SHA256.HashData(certificate.RawData);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        Task server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(cancel.Token);
            await using var tls = new SslStream(peer.GetStream(), false);
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificateRequired = false
            }, cancel.Token);
            await tls.WriteAsync(new byte[] { 0x42, 0x56 }, cancel.Token);
            await tls.FlushAsync(cancel.Token);
        }, cancel.Token);
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port, cancel.Token);
        await using (var tls = new SslStream(client.GetStream(), false, (_, remote, _, _) =>
        {
            return remote is not null &&
                CryptographicOperations.FixedTimeEquals(expectedPin, SHA256.HashData(remote.GetRawCertData()));
        }))
        {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, cancel.Token);
            byte[] answer = new byte[2];
            await tls.ReadExactlyAsync(answer, cancel.Token);
            if (answer[0] != 0x42 || answer[1] != 0x56)
                throw new Exception("TLS handshake test received an invalid payload.");
        }
        await server.WaitAsync(cancel.Token);
        Console.WriteLine("PASS Windows local TLS handshake: temporary private key, Schannel/OpenSSL and pinned certificate exchange");
        Console.WriteLine("NOTE: This tests local TLS, not firewall permissions or phone ↔ PC network connectivity.");
    }
}
