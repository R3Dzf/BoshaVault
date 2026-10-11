using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
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
        async Task<SupabaseCloudAccount.Session> Refresh()
        {
            EnsureCurrent();
            var signedIn = await auth.Refresh(profile!.RefreshToken, cancellation.Token);
            EnsureCurrent();
            if (signedIn.OwnerId != profile.OwnerId) throw new VaultException("Cloud account changed. Reconnect this device.");
            profile.RefreshToken = signedIn.RefreshToken;
            store.Save(profile); // Persist token rotation before any subsequent network request.
            return signedIn;
        }
        void Connected()
        {
            ClearPage();
            d.Label("Cloud is connected to " + profile!.Email + ". Sync runs only when you press Sync now.");
            d.Label(profile.LastSyncUtc.Length == 0 ? "No successful sync yet." : "Last successful sync: " + profile.LastSyncUtc);
            d.Label("Both devices must use the same vault, cloud account and connection code. Wi-Fi transfer works independently.");
            var code = d.Field("Connection code for your other device", "", 1024, true); code.IsReadOnly = true;
            d.Label("The code contains your cloud recovery key. Share it only with your own trusted device. Keep an encrypted local backup.");
            d.Body.Children.Add(status);
            Action("Sync now", async () =>
            {
                var signedIn = await Refresh();
                var transport = new SupabaseSnapshotTransport(http, new Uri(CloudProject.Url), CloudProject.PublishableKey, _ => Task.FromResult(signedIn.AccessToken));
                var rows = await transport.ListAsync(profile.VaultSlot, cancellation.Token);
                EnsureCurrent();
                byte[] key = CloudSnapshotCodec.ParseRecoveryKey(profile.RecoveryKey);
                var inner = new List<byte[]>();
                byte[] local = [], outer = [];
                try
                {
                    // Authenticate all inputs before making any local change.
                    foreach (var row in rows) inner.Add(CloudSnapshotCodec.Open(row.Blob, key, profile.VaultSlot, row.DeviceSlot));
                    EnsureCurrent();
                    int conflicts = vault.MergeCloudSnapshots(inner);
                    changed();
                    local = vault.ExportEncrypted();
                    outer = CloudSnapshotCodec.Seal(local, key, profile.VaultSlot, profile.DeviceSlot);
                    await transport.UploadAsync(profile.OwnerId, profile.VaultSlot, profile.DeviceSlot, outer, cancellation.Token);
                    EnsureCurrent();
                    profile.LastSyncUtc = DateTimeOffset.UtcNow.ToString("O"); store.Save(profile);
                    Status($"Synced successfully. {rows.Count} device backup(s) checked; {conflicts} conflict copies kept.");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(local); CryptographicOperations.ZeroMemory(outer);
                    foreach (var bytes in inner) CryptographicOperations.ZeroMemory(bytes);
                }
            }, true);
            Action("Show & copy connection code", () => { code.Text = CloudPairing.Encode(profile); copy(code.Text); Status("Code copied. Paste it on your own device. Clipboard clears in 20 seconds."); return Task.CompletedTask; });
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
            d.Label("On your first device: create a cloud account, confirm its email, then choose Start a new cloud vault. On your second device: first transfer the same vault over Wi-Fi, then sign in with the same cloud account and paste its connection code.");
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
            async Task Connect(bool existing)
            {
                string secret = password.Password, pairingCode = join.Password; password.Clear(); join.Clear();
                try
                {
                    var signedIn = await auth.SignIn(email.Text.Trim(), secret, cancellation.Token); EnsureCurrent();
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
