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
        Func<bool> current, Action changed, Action<string> copy, bool openPairing = false)
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        var auth = new SupabaseCloudAccount(http);
        var store = new CloudProfileStore(root);
        CloudProfile? profile = null;
        var d = new FormDialog(owner, "Encrypted cloud sync", 520);
        var buttons = new List<Button>();
        bool running = false, closed = false;
        Button? addDeviceButton = null;
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
        void ScrubFields(UIElement element)
        {
            if (element is PasswordBox secret) secret.Clear();
            if (element is TextBox value) value.Clear();
            if (element is Image image) image.Source = null;
            if (element is Panel panel)
                foreach (UIElement child in panel.Children) ScrubFields(child);
            if (element is ContentControl control && control.Content is UIElement nested)
                ScrubFields(nested);
        }
        void ClearPage()
        {
            // Legacy recovery codes can now live inside an Advanced expander. Recursively
            // clear those fields and QR bitmaps before replacing or closing the page.
            foreach (UIElement element in d.Body.Children) ScrubFields(element);
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
            d.Label("Your other devices can sync automatically. Add a device with the secure two-minute QR instead of sending recovery codes.");
            var code = d.Field("Connection code for your other device", "", 1024, true); code.IsReadOnly = true;
            var qr = new Image { Width = 260, Height = 260, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 8) }; d.Body.Children.Add(qr);
            d.Label("Legacy recovery exposes a permanent cloud key. Only use it for trusted recovery when normal QR pairing is unavailable.");
            var recovery = new StackPanel { Margin = new Thickness(0, 4, 0, 12) };
            int recoveryStart = d.Body.Children.Count - 4;
            for (int i = 0; i < 4; i++)
            {
                var item = d.Body.Children[recoveryStart];
                d.Body.Children.RemoveAt(recoveryStart);
                recovery.Children.Add(item);
            }
            var advanced = new StackPanel();
            Button AdvancedAction(string label, Func<Task> action)
            {
                var b = Action(label, action, inBody: true);
                d.Body.Children.Remove(b);
                advanced.Children.Add(b);
                return b;
            }
            advanced.Children.Add(new Expander { Header = "Legacy recovery key · only if needed",
                Content = recovery, Margin = new Thickness(0, 5, 0, 12) });
            d.Body.Children.Add(status);
            var auto = new CheckBox { Content = "Sync automatically on this device", IsChecked = profile.AutoSync, Margin = new Thickness(0, 8, 0, 8) };
            auto.Click += (_, _) => { profile.AutoSync = auto.IsChecked == true; store.Save(profile); Status(profile.AutoSync ? "Automatic sync is on. Offline changes retry when connected." : "Automatic sync is paused on this device."); };
            d.Body.Children.Add(auto);
            d.Label("Optional session list: when enabled, your cloud account shows this signed-in app session, self-reported Windows/software versions, and the last successful sync. It does not verify physical hardware. Remove it any time; cloud sync and offline vault data remain available.");
            var inventory = new CheckBox {
                Content = "Show this Windows app session in my cloud session list",
                IsChecked = profile.DeviceInventoryConsent,
                Margin = new Thickness(0, 4, 0, 8)
            };
            d.Body.Children.Add(inventory);
            Action("Apply session visibility choice", async () =>
            {
                bool enabled = inventory.IsChecked == true;
                if (enabled == profile.DeviceInventoryConsent)
                { Status(enabled ? "This signed-in session is already opted in." : "This signed-in session is not listed."); return; }
                string message = enabled
                    ? "Show this signed-in Windows app session, its software versions, and server-recorded sync time in your cloud account's session list? This does not identify physical hardware."
                    : "Remove this session's metadata from your cloud account list? Cloud sync and the offline vault remain available.";
                if (MessageBox.Show(d, message, "Session visibility", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
                { inventory.IsChecked = profile.DeviceInventoryConsent; return; }
                if (enabled)
                {
                    profile.DeviceInventoryConsent = true;
                    profile.DeviceInventoryState = "pending";
                    store.Save(profile);
                    Status("Session visibility enabled. This app session appears after its next successful cloud sync.");
                    return;
                }
                try
                {
                    var signedIn = await Refresh();
                    var transport = new SupabaseSnapshotTransport(http, new Uri(CloudProject.Url),
                        CloudProject.PublishableKey, _ => Task.FromResult(signedIn.AccessToken));
                    await transport.UnregisterCloudDeviceAsync(cancellation.Token);
                    EnsureCurrent();
                    profile.DeviceInventoryConsent = false;
                    profile.DeviceInventoryState = "";
                    store.Save(profile);
                    Status("Session metadata removed. Cloud sync and offline vault data remain available.");
                }
                catch
                {
                    inventory.IsChecked = true;
                    throw;
                }
            }, inBody: true);
            Action("Support tickets", async () =>
            {
                await Refresh();
                SupportTicketDialog.Show(d, http, Refresh, EnsureCurrent, cancellation.Token);
            }, inBody: true);
            Action("Sync now", async () =>
            {
                var signedIn = await Refresh();
                var transport = new SupabaseSnapshotTransport(http, new Uri(CloudProject.Url), CloudProject.PublishableKey, _ => Task.FromResult(signedIn.AccessToken));
                var result = await AutomaticCloudSync.RunAsync(transport, profile,
                    inner => { EnsureCurrent(); int conflicts = vault.MergeCloudSnapshots(inner); changed(); return Task.FromResult((vault.ExportEncrypted(), conflicts)); },
                    () => { EnsureCurrent(); return Task.FromResult(vault.ExportEncrypted()); },
                    store.ReadPending, store.WritePending, () => store.Save(profile), cancellation.Token);
                EnsureCurrent();
                string visibility = profile.DeviceInventoryConsent
                    ? profile.DeviceInventoryState == "visible"
                        ? " This app session is listed in your cloud account."
                        : " Session listing will retry on the next successful sync."
                    : "";
                Status($"Cloud is up to date. {result.Conflicts} conflict copies kept.{visibility}");
            }, true, inBody: true);
            addDeviceButton = Action("Add new device · secure QR", async () =>
            {
                var signedIn = await Refresh();
                EnsureCurrent();
                await DevicePairingWindow.Show(owner, profile!, signedIn, cancellation.Token);
                EnsureCurrent();
                Status("Add device session closed. Check the phone for pairing status.");
            }, true, inBody: true);
            AdvancedAction("Show legacy recovery code / QR", () => {
                code.Text = CloudPairing.Encode(profile); byte[] png = PairingQr.Render(code.Text);
                try { using var stream = new MemoryStream(png); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); qr.Source = bitmap; qr.Visibility = Visibility.Visible; }
                finally { CryptographicOperations.ZeroMemory(png); }
                copy(code.Text); Status("Scan this QR only on your own device. The code was copied and clipboard clears in 20 seconds."); return Task.CompletedTask;
            });
            AdvancedAction("Cloud account security · 2FA", async () =>
            {
                var signedIn = await Refresh();
                CloudMfaDialog.Manage(d, auth, signedIn, profile, store, EnsureCurrent, cancellation.Token);
                Status("Cloud security settings updated.");
            });
            AdvancedAction("Disconnect this device", async () =>
            {
                if (MessageBox.Show(d, "Keep your local vault and encrypted cloud backups, and disconnect this device? Keep the connection code on another trusted device before disconnecting.", "Disconnect cloud", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                var signedIn = await Refresh(); await auth.SignOut(signedIn.AccessToken, cancellation.Token);
                EnsureCurrent(); store.Forget(); profile.Clear(); profile = null;
                Setup(); Status("This cloud session was signed out. Previously issued access tokens expire on their normal schedule. Your local vault is unchanged.");
            });
            AdvancedAction("Forget local cloud settings (offline)", () =>
            {
                if (MessageBox.Show(d, "Forget this device's recovery key and saved sign-in? This does not revoke the server session or remove cloud backups. Keep your connection code elsewhere first.", "Forget cloud settings", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                { store.Forget(); profile!.Clear(); profile = null; Setup(); Status("Local cloud settings forgotten. Server sessions were not revoked."); }
                return Task.CompletedTask;
            });
            d.Body.Children.Add(new Expander {
                Header = "Security & advanced cloud options",
                Content = advanced, IsExpanded = false, Margin = new Thickness(0, 12, 0, 12) });
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
        d.Loaded += (_, _) =>
        {
            if (openPairing && addDeviceButton != null)
                addDeviceButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        };
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
