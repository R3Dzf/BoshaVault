using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using BoshaVault.Core;

namespace BoshaVault.Windows;

internal static class CloudSyncDialog
{
    public static void Show(MainWindow owner, string root, VaultSession vault,
        Func<bool> current, Action changed, Action<string> copy)
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        var auth = new SupabaseCloudAccount(http);
        var store = new CloudProfileStore(root);
        CloudProfile? profile = null;
        var d = new FormDialog(owner, "Encrypted cloud sync", 520);
        var buttons = new List<Button>();
        bool running = false, closed = false;
        Guid vaultId = Guid.Parse(vault.Data.VaultId);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 10) };
        void EnsureCurrent() { if (closed || !current() || !vault.IsOpen) throw new OperationCanceledException(); }
        void Status(string message) { if (!closed) status.Text = message; }
        Button Action(string label, Func<Task> action, bool primary = false, bool inBody = false)
        {
            var button = d.Action(label, async () =>
            {
                if (running) return;
                running = true;
                foreach (var b in buttons) b.IsEnabled = false;
                try { EnsureCurrent(); Status("Working… Keep this window open."); await action(); }
                catch (OperationCanceledException) { Status("Cloud operation cancelled. Your local vault remains available."); }
                catch (Exception ex) when (ex is VaultException or HttpRequestException or IOException or CryptographicException or UnauthorizedAccessException or ObjectDisposedException)
                { Status(ex is VaultException ? ex.Message : "Cloud could not be reached. Your local vault is preserved. Try again when connected."); }
                finally { running = false; if (!closed) foreach (var b in buttons) b.IsEnabled = true; }
            }, primary, inBody);
            buttons.Add(button); return button;
        }
        void ClearPage()
        {
            foreach (var element in d.Body.Children)
            {
                if (element is PasswordBox password) password.Clear();
                if (element is TextBox text) text.Clear();
            }
            foreach (var b in buttons) b.Visibility = Visibility.Collapsed;
            d.Body.Children.Clear();
        }
        void SaveSession(SupabaseCloudAccount.Session signedIn)
        {
            EnsureCurrent();
            if (signedIn.OwnerId != profile!.OwnerId) throw new VaultException("Cloud account changed. Reconnect this device.");
            profile.RefreshToken = signedIn.RefreshToken; store.Save(profile);
        }
        async Task<SupabaseCloudAccount.Session> Assure(SupabaseCloudAccount.Session signedIn)
        {
            var factors = await auth.ListFactors(signedIn, cancellation.Token); EnsureCurrent();
            if (factors.RequiresChallenge(signedIn))
                return CloudMfaDialog.Verify(d, auth, signedIn, factors.Totp, next => { if (profile != null) SaveSession(next); }, EnsureCurrent, cancellation.Token);
            return signedIn;
        }
        async Task<SupabaseCloudAccount.Session> Refresh()
        {
            EnsureCurrent();
            var signedIn = await auth.Refresh(profile!.RefreshToken, cancellation.Token);
            EnsureCurrent();
            SaveSession(signedIn); // Persist token rotation before any subsequent network request.
            return await Assure(signedIn);
        }
        void Connected()
        {
            ClearPage();
            d.Label("Cloud is connected to " + profile!.Email + ". New logins sync automatically while connected; offline changes retry when Internet returns.");
            d.Label(profile.LastSyncUtc.Length == 0 ? "No successful sync yet." : "Last successful sync: " + profile.LastSyncUtc);
            d.Label("Connect your second device once with the same account and connection code. Unlock it to apply incoming changes. Wi-Fi transfer still works offline.");
            var code = d.Field("Connection code for your other device", "", 1024, true); code.IsReadOnly = true;
            var qr = new Image { Width = 260, Height = 260, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 8) }; d.Body.Children.Add(qr);
            d.Label("The code contains your cloud recovery key. Share it only with your own trusted device. Keep an encrypted local backup.");
            d.Body.Children.Add(status);
            var auto = new CheckBox { Content = "Sync automatically on this device", IsChecked = profile.AutoSync, Margin = new Thickness(0, 8, 0, 8) };
            auto.Click += (_, _) => { profile.AutoSync = auto.IsChecked == true; store.Save(profile); Status(profile.AutoSync ? "Automatic sync is on. Offline changes retry when connected." : "Automatic sync is paused on this device."); };
            d.Body.Children.Add(auto);
            Action("Sync now", async () =>
            {
                var signedIn = await Refresh();
                var transport = new SupabaseSnapshotTransport(http, new Uri(CloudProject.Url), CloudProject.PublishableKey, _ => Task.FromResult(signedIn.AccessToken));
                var result = await AutomaticCloudSync.RunAsync(transport, profile,
                    inner => { EnsureCurrent(); int conflicts = vault.MergeCloudSnapshots(inner); changed(); return Task.FromResult((vault.ExportEncrypted(), conflicts)); },
                    () => { EnsureCurrent(); return Task.FromResult(vault.ExportEncrypted()); },
                    store.ReadPending, store.WritePending, () => store.Save(profile), cancellation.Token);
                EnsureCurrent();
                Status($"Cloud is up to date. {result.Conflicts} conflict copies kept.");
            }, true);
            Action("Show QR & copy connection code", () => {
                code.Text = CloudPairing.Encode(profile); byte[] png = PairingQr.Render(code.Text);
                try { using var stream = new MemoryStream(png); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); qr.Source = bitmap; qr.Visibility = Visibility.Visible; }
                finally { CryptographicOperations.ZeroMemory(png); }
                copy(code.Text); Status("Scan this QR only on your own device. The code was copied and clipboard clears in 20 seconds."); return Task.CompletedTask;
            });
            Action("Cloud account security · 2FA", async () =>
            {
                var signedIn = await Refresh();
                CloudMfaDialog.Manage(d, auth, signedIn, profile, store, EnsureCurrent, cancellation.Token);
                Status("Cloud security settings updated.");
            });
            Action("Disconnect this device", async () =>
            {
                if (MessageBox.Show(d, "Keep your local vault and encrypted cloud backups, and disconnect this device? Keep the connection code on another trusted device before disconnecting.", "Disconnect cloud", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                var signedIn = await Refresh(); await auth.SignOut(signedIn.AccessToken, cancellation.Token);
                EnsureCurrent(); store.Forget(); profile.Clear(); profile = null;
                Setup(); Status("This cloud session was signed out. Previously issued access tokens expire on their normal schedule. Your local vault is unchanged.");
            });
            Action("Forget local cloud settings (offline)", () =>
            {
                if (MessageBox.Show(d, "Forget this device's recovery key and saved sign-in? This does not revoke the server session or remove cloud backups. Keep your connection code elsewhere first.", "Forget cloud settings", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                { store.Forget(); profile!.Clear(); profile = null; Setup(); Status("Local cloud settings forgotten. Server sessions were not revoked."); }
                return Task.CompletedTask;
            });
            Status("Ready. Your vault stays usable without Internet.");
        }
        void Setup()
        {
            ClearPage();
            d.Label("Optional encrypted sync. Create a Lockvyn cloud account here using your own email. Each person uses their own account; use that same account on your own devices.");
            d.Label("Your offline vault needs no account. The cloud account password is separate from your master passphrase.");
            var email = d.Field("Cloud account email", "", 320);
            var password = d.Secret("Cloud account password (separate from your master passphrase)");
            var join = d.Secret("Other device's cloud connection code (only when joining)"); join.MaxLength = 1024;
            var confirmation = d.Secret("Email confirmation code (if shown in your email)"); confirmation.MaxLength = 10;
            d.Label("On your first device: create and confirm a cloud account, then start a new cloud vault. On a new device without a vault, use Restore from cloud on its lock screen with this account and connection code.");
            d.Body.Children.Add(status);
            Action("Create cloud account", async () =>
            {
                string secret = password.Password; password.Clear();
                try { await auth.SignUp(email.Text.Trim(), secret, cancellation.Token); EnsureCurrent(); Status("Check your email and confirm the cloud account, then sign in below. The local vault has not changed."); }
                finally { secret = ""; }
            }, inBody: true);
            Action("Resend confirmation email", async () =>
            {
                await auth.ResendConfirmation(email.Text.Trim(), cancellation.Token); EnsureCurrent();
                Status("If the account is awaiting confirmation, check your email including Spam. Wait at least 60 seconds before requesting another message.");
            }, inBody: true);
            Action("Confirm email code", async () =>
            {
                string digits = confirmation.Password.Trim(); confirmation.Clear();
                try { await auth.VerifyEmail(email.Text.Trim(), digits, cancellation.Token); EnsureCurrent(); Status("Email confirmed. Enter your cloud password and sign in below. 2FA is checked separately."); }
                finally { digits = ""; }
            }, inBody: true);
            async Task Connect(bool existing)
            {
                string secret = password.Password, pairingCode = join.Password; password.Clear(); join.Clear();
                try
                {
                    var signedIn = await Assure(await auth.SignIn(email.Text.Trim(), secret, cancellation.Token)); EnsureCurrent();
                    var pairing = existing ? CloudPairing.Decode(pairingCode.Trim(), vaultId, signedIn.OwnerId) : null;
                    profile = new CloudProfile { OwnerId = signedIn.OwnerId, VaultId = vaultId,
                        VaultSlot = pairing?.VaultSlot ?? Guid.NewGuid(), DeviceSlot = Guid.NewGuid(),
                        RecoveryKey = pairing?.RecoveryKey ?? CloudSnapshotCodec.NewRecoveryKey(),
                        RefreshToken = signedIn.RefreshToken, Email = email.Text.Trim() };
                    store.Save(profile); Connected();
                }
                finally { secret = ""; pairingCode = ""; }
            }
            Action("Start a new cloud vault", () => Connect(false), true);
            Action("Join this vault's existing cloud", () => Connect(true));
        }
        d.Closed += (_, _) => { closed = true; cancellation.Cancel(); profile?.Clear(); ClearPage(); };
        try { profile = store.Load(vaultId); if (profile == null) Setup(); else Connected(); }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            d.Label("Saved cloud settings could not be opened. Your local vault is available.");
            d.Action("Forget damaged cloud settings", () => { store.Forget(); Setup(); });
        }
        try { d.ShowDialog(); }
        finally { cancellation.Cancel(); profile?.Clear(); }
    }
}
