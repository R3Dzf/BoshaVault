using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BoshaVault.Core;

namespace BoshaVault.Windows;

/// <summary>Explicit approval screen: never display or copy the recovery key.</summary>
internal static class DevicePairingWindow
{
    public static async Task Show(MainWindow owner, CloudProfile profile, SupabaseCloudAccount.Session signedIn, CancellationToken outer)
    {
        profile.Validate(profile.VaultId);
        if (profile.OwnerId != signedIn.OwnerId) throw new VaultException("Cloud account changed. Sign in again.");
        using var ecdh = new DevicePairing();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(15) };
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var relay = new DevicePairingTransport(http, signedIn.AccessToken);
        await relay.Create(ecdh, profile.OwnerId, cancel.Token);

        var d = new FormDialog(owner, "Add a device · secure pairing", 490);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Waiting for your phone to scan…", Margin = new Thickness(0, 8, 0, 10) };
        d.Label("On your phone choose Connect to my PC and scan this temporary QR. Both devices may use different networks.");
        var qr = new Image { Width = 236, Height = 236, Margin = new Thickness(0, 8, 0, 8) };
        byte[] png = PairingQr.Render(ecdh.Invitation());
        try
        {
            using var stream = new MemoryStream(png);
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = stream; bmp.EndInit(); bmp.Freeze();
            qr.Source = bmp;
        }
        finally { CryptographicOperations.ZeroMemory(png); }
        d.Body.Children.Add(qr);
        d.Label("This QR contains a temporary public key, NOT your permanent vault recovery key. Expires in two minutes.");
        var sas = new TextBlock { FontSize = 28, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 7, 0, 9), Text = "••• •••", TextAlignment = TextAlignment.Center };
        d.Body.Children.Add(sas);
        var confirm = new CheckBox { Content = "I checked that all six digits match the number on my phone.", IsChecked = false, Margin = new Thickness(0, 0, 0, 10) };
        d.Body.Children.Add(confirm);
        d.Body.Children.Add(status);
        string? joined = null;
        bool closed = false, approved = false, polling = false;
        Button? approve = null;
        approve = d.Action("Approve this device", async () =>
        {
            if (approved || joined == null) return;
            if (confirm.IsChecked != true)
            {
                status.Text = "Compare both displayed numbers before approving. Never approve a mismatched code.";
                return;
            }
            approve!.IsEnabled = false;
            try
            {
                var fresh = await relay.Read(ecdh.SessionId, cancel.Token);
                if (fresh?.OwnerId != profile.OwnerId || fresh.InitiatorKey != ecdh.PublicKey ||
                    fresh.State != "requested" || fresh.JoinerKey != joined)
                    throw new VaultException("The pairing request changed. Restart pairing.");
                string payload = ecdh.SealPairing(CloudPairing.Encode(profile), joined);
                await relay.Approve(ecdh, profile.OwnerId, joined, payload, cancel.Token);
                approved = true;
                qr.Visibility = Visibility.Collapsed;
                status.Text = "Approved. Waiting for your phone to decrypt and finish pairing…";
            }
            catch (Exception ex) when (ex is VaultException or HttpRequestException or IOException or CryptographicException or OperationCanceledException)
            { status.Text = ex is VaultException ? ex.Message : "Connection failed. Restart secure pairing."; }
        }, true);
        approve.IsEnabled = false;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += async (_, _) =>
        {
            if (closed || polling) return;
            if (DateTimeOffset.UtcNow >= ecdh.ExpiresAt)
            {
                timer.Stop(); approve.IsEnabled = false; qr.Visibility = Visibility.Collapsed;
                status.Text = approved ? "Pairing window expired. Check your phone for completion." : "Pairing expired. Close and choose Add device again.";
                return;
            }
            polling = true;
            try
            {
                var row = await relay.Read(ecdh.SessionId, cancel.Token);
                if (closed) return;
                if (row?.OwnerId != profile.OwnerId || row.InitiatorKey != ecdh.PublicKey)
                    throw new VaultException("Pairing was cancelled or does not match your account.");
                if (row.State == "requested" && !approved)
                {
                    if (joined != null && joined != row.JoinerKey)
                        throw new VaultException("The requesting device changed. Restart pairing.");
                    joined = row.JoinerKey;
                    sas.Text = string.Format("{0} {1}", ecdh.SecurityCode(joined!, ecdh.PublicKey)[..3],
                        ecdh.SecurityCode(joined!, ecdh.PublicKey)[3..]);
                    approve.IsEnabled = true;
                    status.Text = "Compare these six digits with your phone. Approve ONLY when they match.";
                }
                else if (row.State == "completed")
                {
                    timer.Stop(); status.Text = "Device connected. Your phone can now sync encrypted changes.";
                    approve.IsEnabled = false; qr.Visibility = Visibility.Collapsed;
                }
                else if (row.State == "cancelled")
                {
                    timer.Stop(); approve.IsEnabled = false; status.Text = "Pairing cancelled.";
                }
            }
            catch (Exception ex) when (ex is VaultException or HttpRequestException or IOException or OperationCanceledException)
            { if (!closed) { timer.Stop(); approve.IsEnabled = false; status.Text = ex is VaultException ? ex.Message : "Could not reach pairing relay."; } }
            finally { polling = false; }
        };
        d.Closed += (_, _) => { closed = true; timer.Stop(); cancel.Cancel(); qr.Source = null; };
        try { timer.Start(); d.ShowDialog(); }
        finally
        {
            timer.Stop();
            if (!approved)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await relay.Cancel(ecdh.SessionId, cleanup.Token);
                }
                catch (Exception) { /* server expiry prevents reuse if the network is unavailable */ }
            }
        }
    }
}
