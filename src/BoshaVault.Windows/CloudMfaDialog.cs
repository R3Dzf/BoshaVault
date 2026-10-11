using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using BoshaVault.Core;

namespace BoshaVault.Windows;

internal static class CloudMfaDialog
{
    private static string Error(Exception ex) => ex is VaultException ? ex.Message : "Cloud could not be reached. Try again when connected.";

    public static SupabaseCloudAccount.Session Verify(Window owner, SupabaseCloudAccount auth,
        SupabaseCloudAccount.Session session, IReadOnlyList<SupabaseCloudAccount.Factor> factors,
        Action<SupabaseCloudAccount.Session> updated, Action ensureCurrent, CancellationToken ct)
    {
        if (factors.Count == 0) throw new VaultException("This account requires an authenticator method this version cannot use.");
        var d = new FormDialog(owner, "Verify cloud two-factor authentication", 460);
        d.Label("Enter a current six-digit code from your authenticator. Cloud access waits for verification.");
        var device = new ComboBox { ItemsSource = factors, DisplayMemberPath = "Name", SelectedIndex = 0, MinHeight = 40 };
        d.Body.Children.Add(device);
        var code = d.Secret("Authenticator code"); code.MaxLength = 6;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; d.Body.Children.Add(status);
        SupabaseCloudAccount.Session? result = null; bool working = false, closed = false;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var verify = d.Action("Verify & continue", async () =>
        {
            if (working) return;
            working = true; string digits = code.Password.Trim(); code.Clear();
            try
            {
                ensureCurrent(); status.Text = "Checking authenticator…";
                var next = await auth.VerifyTotp(session, ((SupabaseCloudAccount.Factor)device.SelectedItem).Id, digits, stop.Token);
                ensureCurrent(); if (closed) return;
                updated(next); result = next; d.Close();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is VaultException or HttpRequestException or IOException) { if (!closed) status.Text = Error(ex); }
            finally { digits = ""; working = false; }
        }, true);
        d.Action("Cancel", d.Close);
        d.Closed += (_, _) => { closed = true; stop.Cancel(); code.Clear(); };
        d.ShowDialog(); ensureCurrent();
        return result ?? throw new OperationCanceledException();
    }

    public static void Manage(Window owner, SupabaseCloudAccount auth, SupabaseCloudAccount.Session initial,
        CloudProfile profile, CloudProfileStore store, Action ensureCurrent, CancellationToken ct)
    {
        var d = new FormDialog(owner, "Cloud account security · 2FA", 510);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) };
        var session = initial; bool busy = false, closed = false;
        SupabaseCloudAccount.TotpEnrollment? enrollment = null;
        var controls = new List<Button>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        void Save(SupabaseCloudAccount.Session next)
        {
            ensureCurrent(); if (closed) throw new OperationCanceledException();
            if (next.OwnerId != profile.OwnerId) throw new VaultException("Cloud account changed.");
            profile.RefreshToken = next.RefreshToken; store.Save(profile); session = next;
        }
        Button Action(string label, Func<Task> work, bool primary = false)
        {
            var b = d.Action(label, async () =>
            {
                if (busy) return; busy = true;
                foreach (var c in controls) c.IsEnabled = false;
                try { ensureCurrent(); status.Text = "Working…"; await work(); }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is VaultException or HttpRequestException or IOException or CryptographicException or UnauthorizedAccessException)
                { if (!closed) status.Text = Error(ex); }
                finally { busy = false; if (!closed) foreach (var c in controls) c.IsEnabled = true; }
            }, primary, true); controls.Add(b); return b;
        }
        void Clear()
        {
            foreach (var child in d.Body.Children)
            {
                if (child is TextBox text) text.Clear();
                if (child is PasswordBox secret) secret.Clear();
                if (child is Image image) image.Source = null;
            }
            d.Body.Children.Clear(); controls.Clear(); enrollment?.Dispose(); enrollment = null;
        }
        async Task List()
        {
            var factors = await auth.ListFactors(session, stop.Token); ensureCurrent(); if (closed) return;
            Clear(); d.Label(factors.HasVerified ? "2FA is enabled for this cloud account." : "2FA is not enabled yet.");
            d.Label("Use Google Authenticator, Aegis or another TOTP app. Back up your authenticator or add a second one. Losing every authenticator locks cloud access; your offline vault still works.");
            d.Label("If you switched to your authenticator before finishing, choose Finish setup below and enter its current code.");
            d.Body.Children.Add(status);
            foreach (var pending in factors.All.Where(f => !f.Verified && f.Type == "totp"))
                Action("Finish setup · " + pending.Name, async () =>
                {
                    session = Verify(d, auth, session, [pending], Save, ensureCurrent, stop.Token);
                    await List(); status.Text = "2FA enabled. Keep an authenticator backup.";
                });
            foreach (var factor in factors.All.Where(f => f.Verified && f.Type == "totp"))
            {
                Action("Remove " + factor.Name, async () =>
                {
                    if (MessageBox.Show(d, "Remove this authenticator from the cloud account? A current authenticator code is required. Removing the last one turns off 2FA.", "Remove authenticator", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                    var verified = Verify(d, auth, session, factors.Totp, Save, ensureCurrent, stop.Token);
                    await auth.Unenroll(verified, factor.Id, stop.Token);
                    Save(await auth.Refresh(session.RefreshToken, stop.Token));
                    await List(); status.Text = "Authenticator removed. Cloud backups and your local vault are preserved.";
                });
            }
            Action(factors.HasVerified ? "Add another authenticator" : "Enable 2FA", async () =>
            {
                if (factors.RequiresChallenge(session)) session = Verify(d, auth, session, factors.Totp, Save, ensureCurrent, stop.Token);
                var next = await auth.EnrollTotp(session, profile.Email, stop.Token); ensureCurrent();
                if (closed) { next.Dispose(); return; }
                Clear(); enrollment = next;
                d.Label("Scan this QR in your authenticator, or enter the setup key manually. Account name: " + profile.Email + ". Time based, 6 digits, 30 seconds.");
                byte[] png = next.RenderQr();
                try
                {
                    using var stream = new MemoryStream(png);
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                    d.Body.Children.Add(new Image { Source = bitmap, Width = 230, Height = 230, Margin = new Thickness(0, 8, 0, 8) });
                }
                finally { CryptographicOperations.ZeroMemory(png); }
                var key = d.Field("Setup key · keep private", next.Secret, 128); key.IsReadOnly = true;
                var code = d.Secret("Current authenticator code"); code.MaxLength = 6;
                d.Label("2FA becomes active only after the code is verified. Save an encrypted authenticator backup before you finish.");
                d.Body.Children.Add(status);
                Action("Verify code & enable", async () =>
                {
                    string digits = code.Password.Trim(); code.Clear();
                    try { Save(await auth.VerifyTotp(session, next.Id, digits, stop.Token)); await List(); status.Text = "2FA enabled. Future cloud sign-ins require an authenticator code."; }
                    finally { digits = ""; }
                }, true);
                Action("Cancel setup", async () => { await auth.Unenroll(session, next.Id, stop.Token); await List(); status.Text = "Unfinished setup removed."; });
                status.Text = "Add Lockvyn to your authenticator, then enter its code.";
            }, true);
            status.Text = "Ready.";
        }
        d.Action("Close", d.Close);
        d.Loaded += async (_, _) =>
        {
            try { await List(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is VaultException or HttpRequestException or IOException)
            { if (!closed) { d.Body.Children.Add(status); status.Text = Error(ex); } }
        };
        d.Closed += (_, _) => { closed = true; stop.Cancel(); Clear(); };
        d.ShowDialog();
    }
}
