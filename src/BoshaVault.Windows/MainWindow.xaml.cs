using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BoshaVault.Core;
using Microsoft.Win32;

namespace BoshaVault.Windows;

public partial class MainWindow : Window
{
    private readonly string root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoshaVault");
    private readonly string vaultPath;
    private readonly string deviceId;
    private VaultSession? session;
    private VaultEntry? selected;
    private DateTime lastActivity = DateTime.UtcNow, revealUntil;
    private readonly DispatcherTimer timer;
    private int timeoutMinutes = 2, attempts, generation;
    private DateTime retryAfter;
    private string filter = "all";
    private bool revealed, busy;
    private LocalTransferServer? transfer;
    private HwndSource? source;
    private nint hwnd;
    private TrayController? tray;
    private readonly DesktopPreferences preferences;
    private readonly string preferencesPath;
    private bool exiting, resourcesReleased;
    private WindowState previousWindowState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(root); vaultPath = System.IO.Path.Combine(root, "vault.boshavault");
        preferencesPath = System.IO.Path.Combine(root, "desktop.json"); preferences = DesktopPreferences.Load(preferencesPath); timeoutMinutes = preferences.AutoLockMinutes;
        string devicePath = System.IO.Path.Combine(root, "device.id");
        deviceId = File.Exists(devicePath) ? File.ReadAllText(devicePath).Trim() : Guid.NewGuid().ToString();
        if (!VaultCodec.IsId(deviceId)) deviceId = Guid.NewGuid().ToString();
        File.WriteAllText(devicePath, deviceId);
        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += (_, _) => lastActivity = DateTime.UtcNow;
        PreviewMouseDown += (_, _) => lastActivity = DateTime.UtcNow;
        foreach (var input in new UIElement[] { SearchBox, EntryList }) input.MouseWheel += (_, _) => lastActivity = DateTime.UtcNow;
        SystemEvents.SessionSwitch += OnSessionSwitch; SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Closing += OnClosing;
        Closed += (_, _) => { ReleaseDesktopResources(); Application.Current.Shutdown(); };
        StateChanged += (_, _) => { if (WindowState != WindowState.Minimized) previousWindowState = WindowState; };
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (DateTime.UtcNow >= PrivateClipboard.Expires) PrivateClipboard.Clear();
            if (revealed && DateTime.UtcNow >= revealUntil) Mask();
            if (session != null && transfer?.IsActive != true && DateTime.UtcNow - lastActivity > TimeSpan.FromMinutes(timeoutMinutes)) LockVault();
        };
        timer.Start(); ConfigureUnlock();
    }

    private void ConfigureUnlock()
    {
        bool exists = File.Exists(vaultPath);
        LockHeading.Text = exists ? "Welcome back" : "Make yourself at home";
        LockDescription.Text = exists ? "Unlock your private space with your master passphrase." : "Choose a long, unique passphrase of at least 16 characters. Only you can unlock your vault.";
        ConfirmPanel.Visibility = exists ? Visibility.Collapsed : Visibility.Visible;
        UnlockButton.Content = exists ? "Unlock vault →" : "Create encrypted vault →";
        LockView.Visibility = Visibility.Visible; VaultView.Visibility = Visibility.Collapsed; MasterBox.Focus();
        tray?.Update();
    }
    private async void UnlockClick(object sender, RoutedEventArgs e)
    {
        if (busy || DateTime.UtcNow < retryAfter) { LockError.Text = "Please wait a moment before trying again."; return; }
        bool exists = File.Exists(vaultPath);
        string password = MasterBox.Password;
        if (!exists && password != ConfirmBox.Password) { LockError.Text = "The passphrases do not match."; return; }
        busy = true; UnlockButton.IsEnabled = false; LockError.Text = "Unlocking your encrypted vault…";
        int token = ++generation;
        try
        {
            var opened = exists ? await VaultSession.Open(vaultPath, password, deviceId) : await VaultSession.Create(vaultPath, password, deviceId);
            if (token != generation) { opened.Dispose(); if (!resourcesReleased && !exiting) ConfigureUnlock(); return; }
            session = opened; attempts = 0; MasterBox.Clear(); ConfirmBox.Clear(); LockError.Text = "";
            lastActivity = DateTime.UtcNow; LockView.Visibility = Visibility.Collapsed; VaultView.Visibility = Visibility.Visible;
            filter = "all"; SearchBox.Clear(); Refresh(); SearchBox.Focus(); tray?.Update();
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            if (token != generation || resourcesReleased || exiting) return;
            attempts++; retryAfter = DateTime.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempts, 5))));
            LockError.Text = ex is VaultException ? ex.Message : "Cannot access the encrypted vault file.";
            MasterBox.Clear(); ConfirmBox.Clear();
        }
        finally { busy = false; UnlockButton.IsEnabled = true; password = ""; }
    }
    private void MasterKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) UnlockClick(sender, e); }
    private void LockClick(object sender, RoutedEventArgs e) => LockVault();
    private void LockVault()
    {
        generation++; transfer?.Dispose(); transfer = null;
        foreach (Window child in OwnedWindows.Cast<Window>().ToArray()) child.Close();
        PrivateClipboard.Clear(); Mask();
        session?.Dispose(); session = null;
        selected = null; EntryList.ItemsSource = null; DetailUsername.Text = ""; DetailNotes.Text = ""; DetailHost.Text = ""; DetailTitle.Text = ""; SearchBox.Clear();
        MasterBox.Clear(); ConfirmBox.Clear(); LockError.Text = ""; ConfigureUnlock();
    }

    private void Refresh()
    {
        if (session == null) return;
        var live = session.Data.Entries.Where(e => !e.Deleted).ToList();
        var repeated = live.GroupBy(e => e.Password).Where(g => g.Key.Length > 0 && g.Count() > 1).SelectMany(g => g).Select(e => e.Id).ToHashSet();
        int weak = live.Count(e => PasswordGenerator.Weak(e.Password));
        CountText.Text = live.Count.ToString(); HealthText.Text = live.Count == 0 ? "Ready" : weak + repeated.Count == 0 ? "All checked" : $"{live.Where(e => PasswordGenerator.Weak(e.Password) || repeated.Contains(e.Id)).Count()} to review";
        string search = SearchBox.Text.Trim();
        var items = session.Data.Entries.Where(e => filter == "trash" ? e.Deleted : !e.Deleted)
            .Where(e => filter != "favorites" || e.Favorite)
            .Where(e => filter != "health" || PasswordGenerator.Weak(e.Password) || repeated.Contains(e.Id))
            .Where(e => search.Length == 0 || new[] { e.Title, e.Username, e.Url, e.Folder }.Any(s => s.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.Favorite).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase).Select(e => new EntryRow(e)).ToList();
        string? previous = selected?.Id;
        EntryList.ItemsSource = items;
        if (previous != null) EntryList.SelectedItem = items.FirstOrDefault(x => x.Entry.Id == previous);
        if (EntryList.SelectedItem == null && items.Count > 0) EntryList.SelectedIndex = 0;
        if (items.Count == 0) { selected = null; DetailFields.Visibility = Visibility.Collapsed; DetailTitle.Text = "Your logins, protected."; DetailHost.Text = "Choose an item to view its details."; }
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageHeading.Text = filter switch { "favorites" => "Your favorites", "health" => "Passwords to review", "trash" => "Your trash", _ => "Your vault" };
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Mask(); selected = (EntryList.SelectedItem as EntryRow)?.Entry;
        DetailFields.Visibility = selected == null ? Visibility.Collapsed : Visibility.Visible;
        if (selected == null) return;
        DetailTitle.Text = selected.Title; DetailHost.Text = selected.Url.Length > 0 ? OriginPolicy.ExactHost(selected.Url) : "No website saved";
        DetailUsername.Text = selected.Username; DetailNotes.Text = selected.Notes.Length > 0 ? selected.Notes : "No notes yet.";
        DeleteButton.Content = selected.Deleted ? "Restore login" : "Move to trash";
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (SearchHint != null) SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; if (session != null) Refresh(); }
    private void AllClick(object sender, RoutedEventArgs e) { filter = "all"; Refresh(); }
    private void FavoriteClick(object sender, RoutedEventArgs e) { filter = "favorites"; Refresh(); }
    private void TrashClick(object sender, RoutedEventArgs e) { filter = "trash"; Refresh(); }
    private void HealthClick(object sender, RoutedEventArgs e) { filter = "health"; Refresh(); SetStatus("Local checks flag short or reused passwords. They do not check online breaches."); }
    private void AddClick(object sender, RoutedEventArgs e) => Edit(null);
    private void EditClick(object sender, RoutedEventArgs e) { if (selected != null) Edit(selected); }
    private void Edit(VaultEntry? entry)
    {
        if (session == null) return;
        var updated = FormDialog.Edit(this, entry);
        if (updated == null || session == null) return;
        Guard(() => { session.Upsert(updated); selected = updated; Refresh(); SetStatus("Login saved to your encrypted vault."); });
    }
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (selected == null || session == null) return;
        Guard(() => { var copy = selected.Clone(); copy.Deleted = !copy.Deleted; session.Upsert(copy); Refresh(); SetStatus(copy.Deleted ? "Moved to encrypted trash. You can restore it." : "Login restored."); });
    }
    private void CopyUserClick(object sender, RoutedEventArgs e) { if (selected != null) Copy(selected.Username); }
    private void CopyPasswordClick(object sender, RoutedEventArgs e) { if (selected != null) Copy(selected.Password); }
    private void Copy(string value) => Guard(() => { PrivateClipboard.Copy(value); SetStatus("Copied. Clipboard clears after 20 seconds. Paste only into the intended website."); });
    private void RevealClick(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        if (revealed) Mask(); else { DetailPassword.Text = selected.Password; revealed = true; revealUntil = DateTime.UtcNow.AddSeconds(15); RevealButton.Content = "Hide"; }
    }
    private void Mask() { revealed = false; if (DetailPassword != null) DetailPassword.Text = "••••••••••••••••"; if (RevealButton != null) RevealButton.Content = "Show"; }
    private void OpenWebsiteClick(object sender, RoutedEventArgs e)
    {
        if (selected?.Url.Length > 0) Guard(() => { OriginPolicy.ExactHost(selected.Url); Process.Start(new ProcessStartInfo(selected.Url) { UseShellExecute = true }); });
    }
    private void GeneratorClick(object sender, RoutedEventArgs e)
    {
        if (session == null) return;
        var d = new FormDialog(this, "Make a stronger password");
        d.Label("Cryptographically random · generated on this device");
        var result = d.Field("Password", PasswordGenerator.Generate(), 128); result.IsReadOnly = true; result.FontFamily = new("Consolas");
        var length = new Slider { Minimum = 16, Maximum = 64, Value = 24, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new(0, 12, 0, 12) };
        var caption = new TextBlock { Text = "Length: 24 characters" }; d.Body.Children.Add(caption); d.Body.Children.Add(length);
        length.ValueChanged += (_, _) => caption.Text = $"Length: {(int)length.Value} characters";
        var symbols = new CheckBox { Content = "Include symbols", IsChecked = true }; d.Body.Children.Add(symbols);
        d.Action("Generate again", () => result.Text = PasswordGenerator.Generate((int)length.Value, symbols.IsChecked == true));
        d.Action("Copy password", () => Copy(result.Text), true); d.ShowDialog(); result.Clear();
    }
    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (session == null) return;
        var d = new FormDialog(this, "Your vault settings");
        var startup = new CheckBox { Content = "Start with Windows · locked in the tray", IsChecked = StartupEnabled() }; d.Body.Children.Add(startup);
        startup.Click += (_, _) => { SetStartup(startup.IsChecked == true); startup.IsChecked = StartupEnabled(); };
        d.Label("Closing the main window locks the vault and keeps BoshaVault in the tray. Click the tray icon or use Ctrl + Alt + P to reopen. Right-click the icon for Exit.");
        var browserAuto = new CheckBox { Content = "Enable local Chrome / Edge Autofill", IsChecked = preferences.BrowserAutofillEnabled };
        d.Body.Children.Add(browserAuto);
        browserAuto.Click += (_, _) => Guard(() => {
            preferences.BrowserAutofillEnabled = browserAuto.IsChecked == true;
            preferences.Save(preferencesPath);
            SetStatus(preferences.BrowserAutofillEnabled ? "Browser Autofill enabled." : "Browser Autofill disabled.");
        });
        d.Label("Browser setup: Install the BoshaVault extension in Chrome or Edge (Load unpacked), then run Install-BrowserAutofill.ps1 with its extension ID. Matching HTTPS logins appear near password fields. Every fill requires your approval on Windows. No blind typing.");
        d.Label("Auto-lock after inactivity");
        var options = new ComboBox { ItemsSource = new[] { 1, 2, 5, 10 }, SelectedItem = timeoutMinutes, Padding = new(10), Margin = new(0, 0, 0, 18) }; d.Body.Children.Add(options);
        d.Action("Apply auto-lock time", () => Guard(() => { int minutes = (int)options.SelectedItem; preferences.AutoLockMinutes = minutes; preferences.Save(preferencesPath); timeoutMinutes = minutes; SetStatus($"Auto-lock set to {timeoutMinutes} minutes and saved."); }));
        var current = d.Secret("Current master passphrase"); var next = d.Secret("New master passphrase (16+ characters)"); var confirm = d.Secret("Confirm new passphrase");
        var note = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray }; d.Body.Children.Add(note);
        Button? change = null;
        change = d.Action("Change master passphrase", async () =>
        {
            if (next.Password != confirm.Password) { note.Text = "The new passphrases do not match."; return; }
            if (session == null) return;
            var activeSession = session; int stamp = generation;
            string oldPassword = current.Password, newPassword = next.Password;
            current.Clear(); next.Clear(); confirm.Clear();
            change!.IsEnabled = false;
            try { await activeSession.ChangePassword(oldPassword, newPassword); if (stamp != generation || session != activeSession || !d.IsVisible) return; Refresh(); note.Text = "Passphrase and vault key changed. Old backups still use their original passphrase. Other devices must verify the updated passphrase when merging."; }
            catch (Exception ex) when (ex is VaultException or IOException) { if (stamp == generation && d.IsVisible) note.Text = ex.Message; }
            finally { oldPassword = ""; newPassword = ""; change.IsEnabled = true; }
        }, true);
        d.Action("View local password health report", () => {
            if (session == null) return;
            var review = PasswordHealth.Audit(session.Data.Entries);
            var popup = new FormDialog(this, "Password health · local check", 540);
            popup.Label($"Reviewed: {review.Total} logins. At risk: {review.AtRisk}. Reused: {review.Reused}. Short: {review.Short}. Common: {review.CommonPassword}. Predictable: {review.Predictable}.");
            popup.Label("Results are calculated on this device only. No passwords or website history leave your vault. This is a limited local check, not an online breach database.");
            if(review.Findings.Count==0) popup.Label("No obvious issues detected by the current local rules.");
            foreach(var finding in review.Findings.Take(35))
                popup.Label(finding.Title + ": " + string.Join("; ",finding.Reasons));
            if(review.Findings.Count>35) popup.Label($"Plus {review.Findings.Count-35} more entries. Review the vault list for the rest.");
            popup.Action("Close",popup.Close,true);
            popup.ShowDialog();
        });
        d.Action("Import Chrome / Bitwarden passwords from CSV", () => ImportBrowserPasswords(d));
        d.Label("Importing requires reviewing a local plaintext CSV. No source file is automatically deleted or uploaded.");
        d.Label("No reset service exists. Old backups are not revoked by changing the passphrase. Windows quick unlock is intentionally unavailable in this release.");
        d.Action("Lock & move to tray", HideToTray);
        d.Action("Exit BoshaVault", ExitApplication);
        d.ShowDialog(); current.Clear(); next.Clear(); confirm.Clear();
    }
    // Diagnostic records contain stages + exception type only. Never write pairing
    // codes, TLS pins, network payloads, master passwords, or vault contents.
    private void WriteTransferDiagnostic(string eventCode)
    {
        try
        {
            string path = System.IO.Path.Combine(root, "transfer.log");
            lock (transferLogGate)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                    File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} | {eventCode}{Environment.NewLine}");
            }
        }
        catch { /* Never abort encrypted transport because logging failed. */ }
    }

    private readonly object transferLogGate = new();

    private void SyncClick(object sender, RoutedEventArgs e)
    {
        if (session == null) return;
        var d = new FormDialog(this, "Devices & encrypted backup", 610);
        d.Label("Connect your phone on the same private Wi-Fi. Keep this window open until the encrypted transfer finishes.");
        string defaultIp = ""; try { defaultIp = LocalTransferServer.FindPrivateHost(); } catch (Exception ex) when (ex is VaultException or System.Net.Sockets.SocketException) { }
        var address = d.Field("Your private Wi-Fi IPv4 address", defaultIp, 15);
        var pairing = new TextBox { IsReadOnly = true, MaxLength = 4096, TextWrapping = TextWrapping.Wrap, MinHeight = 80, Margin = new(0, 10, 0, 0) };
        var qr = new Image { Width = 310, Height = 310, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(qr, BitmapScalingMode.NearestNeighbor);
        var card = new Grid { Margin = new(0, 18, 0, 12), Visibility = Visibility.Collapsed };
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(322) });
        card.ColumnDefinitions.Add(new ColumnDefinition());
        var frame = new Border { Child = qr, Background = Brushes.White, Padding = new(6), CornerRadius = new(12) }; card.Children.Add(frame);
        var instructions = new StackPanel { Margin = new(18, 12, 0, 0) }; Grid.SetColumn(instructions, 1); card.Children.Add(instructions);
        instructions.Children.Add(new TextBlock { Text = "Scan to connect", FontSize = 18, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        instructions.Children.Add(new TextBlock { Text = "Android → Devices & encrypted backup → Connect to Windows → Scan QR code", FontSize = 12, Margin = new(0, 12, 0, 12), TextWrapping = TextWrapping.Wrap });
        var countdown = new TextBlock { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(119, 98, 223)) }; instructions.Children.Add(countdown);
        instructions.Children.Add(new TextBlock { Text = "Temporary pairing code. Show it only to your own phone.", FontSize = 11, Foreground = Brushes.Gray, Margin = new(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });
        var status = new TextBlock { FontSize = 12, Margin = new(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        var expiry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void ClearPairing() { expiry.Stop(); qr.Source = null; card.Visibility = Visibility.Collapsed; pairing.Clear(); }
        expiry.Tick += (_, _) =>
        {
            int remaining = (int)Math.Ceiling(((transfer?.ExpiresUtc ?? DateTime.UtcNow) - DateTime.UtcNow).TotalSeconds);
            if (remaining <= 0) { ClearPairing(); transfer?.Dispose(); transfer = null; status.Text = "Pairing expired. Start a new private transfer to try again."; }
            else if (transfer is { IsActive: false }) { ClearPairing(); status.Text = "Transfer stopped. Start a new private transfer and scan its new QR."; }
            else { countdown.Text = $"Expires in {remaining / 60}:{remaining % 60:00}"; if (transfer?.LastIssue != null) status.Text = transfer.LastIssue; }
        };
        d.Action("Start a private transfer", () => Guard(() =>
        {
            ClearPairing(); transfer?.Dispose(); transfer = null;
            LocalTransferServer? currentTransfer = null;
            currentTransfer = new LocalTransferServer(
                () => Dispatcher.Invoke(() => { if (transfer != currentTransfer || session == null) throw new VaultException("Transfer stopped."); return session.ExportEncrypted(); }),
                bytes => Dispatcher.Invoke(() => { if (transfer != currentTransfer || session == null) throw new VaultException("Transfer stopped."); int c = MergeBackup(bytes); ClearPairing(); Refresh(); Touch(); status.Text = c == 0 ? "Transfer complete. Both devices have the merged vault." : $"Transfer complete. {c} conflict copies kept."; }),
                WriteTransferDiagnostic);
            transfer = currentTransfer;
            try
            {
                pairing.Text = transfer.Start(address.Text.Trim());
                byte[] bytes = PairingQr.Render(pairing.Text);
                try
                {
                    using var stream = new MemoryStream(bytes);
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); qr.Source = image;
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
                countdown.Text = "Expires in 3:00"; card.Visibility = Visibility.Visible; expiry.Start();
                status.Text = $"Ready at {address.Text.Trim()}. Scan this QR, then tap Connect & transfer. Keep this window open; closing or locking ends the transfer.";
            }
            catch { ClearPairing(); transfer.Dispose(); transfer = null; throw; }
        }), true);
        d.Body.Children.Add(card); d.Body.Children.Add(status);
        d.Action("Copy pairing code", () => { if (pairing.Text.Length > 0) Copy(pairing.Text); });
        d.Body.Children.Add(new Expander { Header = "Or paste the pairing code manually", Content = pairing, Margin = new(0, 14, 0, 20) });
        d.Label("If Windows asks for firewall access, allow this app on your private network. Guest Wi-Fi may block connections. Error types are saved to %LOCALAPPDATA%\\BoshaVault\\transfer.log without secret data.");
        d.Label("Encrypted backup files"); d.Action("Export encrypted backup", Export); d.Action("Merge an encrypted backup", ImportMerge);
        try { d.ShowDialog(); } finally { ClearPairing(); transfer?.Dispose(); transfer = null; }
    }
    private void Export()
    {
        if (session == null) return;
        var picker = new SaveFileDialog { Filter = "Encrypted vault (*.boshavault)|*.boshavault", FileName = $"BoshaVault-{DateTime.Now:yyyy-MM-dd}.boshavault" };
        if (picker.ShowDialog(this) != true) return;
        Guard(() => { if (System.IO.Path.GetFullPath(picker.FileName).Equals(vaultPath, StringComparison.OrdinalIgnoreCase)) throw new VaultException("Choose a separate backup path."); File.WriteAllBytes(picker.FileName, session.ExportEncrypted()); SetStatus("Encrypted backup saved. Keep a copy away from this device."); });
    }
    private void ImportMerge()
    {
        if (session == null) return;
        var picker = new OpenFileDialog { Filter = "Encrypted vault (*.boshavault)|*.boshavault" };
        if (picker.ShowDialog(this) != true) return;
        Guard(() => { int c = MergeBackup(VaultSession.ReadBounded(picker.FileName)); Refresh(); SetStatus($"Backup merged. {c} conflict copies kept."); });
    }
    private int MergeBackup(byte[] bytes)
    {
        if (session == null) throw new VaultException("Vault locked.");
        if (!session.NeedsPassphrase(bytes)) return session.MergeEncrypted(bytes);
        var prompt = new FormDialog(this, "A device rotated the vault key");
        prompt.Label("Enter the incoming backup's master passphrase. A newer key will be adopted; local edits will be merged. This vault will then use the incoming passphrase.");
        var p = prompt.Secret("Incoming master passphrase"); prompt.Action("Verify & merge", () => prompt.DialogResult = true, true);
        if (prompt.ShowDialog() != true) { p.Clear(); throw new VaultException("Transfer cancelled. Your existing vault is preserved."); }
        string password = p.Password; p.Clear();
        return session.MergeUsingPassword(bytes, password).GetAwaiter().GetResult();
    }
    private async void LockedImportClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (File.Exists(vaultPath)) { LockError.Text = "Unlock this vault to merge a backup. A different vault must use a separate Windows user profile."; return; }
        var picker = new OpenFileDialog { Filter = "Encrypted vault (*.boshavault)|*.boshavault" };
        if (picker.ShowDialog(this) != true) return;
        var d = new FormDialog(this, "Open your encrypted backup"); var p = d.Secret("Master passphrase used for this backup");
        d.Action("Import vault", () => d.DialogResult = true, true);
        if (d.ShowDialog() != true) { p.Clear(); return; }
        string password = p.Password; p.Clear(); busy = true;
        string staged = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N") + ".import");
        try
        {
            var bytes = VaultSession.ReadBounded(picker.FileName); File.WriteAllBytes(staged, bytes);
            using (await VaultSession.Open(staged, password, deviceId)) { }
            VaultSession.AtomicWrite(vaultPath, bytes, null, false); ConfigureUnlock(); LockError.Text = "Vault imported. Unlock with the backup's master passphrase.";
        }
        catch (Exception ex) when (ex is VaultException or IOException) { LockError.Text = ex.Message; }
        finally { File.Delete(staged); File.Delete(staged + ".lock"); busy = false; password = ""; }
    }
    private void SetStatus(string text) { StatusText.Text = text; lastActivity = DateTime.UtcNow; }
    internal void Touch() => lastActivity = DateTime.UtcNow;
    private void Guard(Action action)
    { try { action(); } catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException or Win32Exception or COMException or System.Net.Sockets.SocketException) { MessageBox.Show(this, ex is VaultException ? ex.Message : ex is System.Net.Sockets.SocketException ? "Cannot start the transfer at this address. Use your current Wi-Fi IPv4 address and check your private-network firewall settings." : "The action could not be completed. Check file access and retry.", "BoshaVault"); } }
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) { if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect) Dispatcher.BeginInvoke(LockVault); }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Suspend) Dispatcher.BeginInvoke(LockVault); }
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        hwnd = new WindowInteropHelper(this).Handle; source = HwndSource.FromHwnd(hwnd); source?.AddHook(WndProc);
        if (!RegisterHotKey(hwnd, 1, 0x4003, 0x50)) SetStatus("Ctrl+Alt+P is already in use by another app. Open BoshaVault from the taskbar.");
        SetWindowDisplayAffinity(hwnd, 0x11); // Best effort; cannot defeat cameras / privileged software.
        try { tray = new TrayController(Dispatcher, BringToFront, LockVault, ExitApplication, () => session != null, StartupEnabled, SetStartup); }
        catch (Exception ex) when (ex is IOException or ArgumentException or Win32Exception or ExternalException)
        { SetStatus("Tray unavailable. Closing the window will exit. Use the taskbar or Ctrl + Alt + P."); }
    }
    private nint WndProc(nint h, int msg, nint w, nint l, ref bool handled)
    {
        if (msg == 0x0312 && w == 1) { BringToFront(); handled = true; }
        return 0;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!exiting && tray != null) { e.Cancel = true; HideToTray(); return; }
        PrepareToExit();
    }
    internal void InitializeLaunch(bool background)
    {
        new WindowInteropHelper(this).EnsureHandle();
        if (!background || tray == null) BringToFront();
    }
    internal void BringToFront()
    {
        if (exiting || resourcesReleased) return;
        Show(); WindowState = previousWindowState; Activate(); lastActivity = DateTime.UtcNow;
        var child = OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible);
        if (child != null) { child.Activate(); return; }
        if (session == null) MasterBox.Focus(); else SearchBox.Focus();
    }
    private void HideToTray()
    {
        if (exiting || tray == null) return;
        if (WindowState != WindowState.Minimized) previousWindowState = WindowState;
        LockVault(); Hide();
        if (!preferences.TrayTipShown)
        {
            tray.ShowCloseHint(); preferences.TrayTipShown = true;
            try { preferences.Save(preferencesPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    private bool StartupEnabled()
    { try { return WindowsStartup.IsEnabled(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; } }
    private void SetStartup(bool enabled)
    {
        try { WindowsStartup.SetEnabled(enabled); tray?.Update(); SetStatus(enabled ? "Starts with Windows, locked in the tray." : "Automatic Windows startup disabled."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { BringToFront(); MessageBox.Show(this, "Cannot update your Windows startup setting. Check your user-account permissions.", "BoshaVault"); }
    }
    private void ExitApplication() { PrepareToExit(); Application.Current.Shutdown(); }
    internal void PrepareToExit() { if (exiting) return; exiting = true; LockVault(); }
    internal void ReleaseDesktopResources()
    {
        if (resourcesReleased) return; resourcesReleased = true;
        generation++; timer.Stop(); transfer?.Dispose(); PrivateClipboard.Clear(); session?.Dispose(); session = null;
        tray?.Dispose(); tray = null;
        SystemEvents.SessionSwitch -= OnSessionSwitch; SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        if (hwnd != 0) UnregisterHotKey(hwnd, 1); source?.RemoveHook(WndProc);
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    private sealed class EntryRow(VaultEntry entry)
    { public VaultEntry Entry { get; } = entry; public string Title => Entry.Title; public string Username => Entry.Username; public string Initial => Title.Length == 0 ? "B" : Title[..1].ToUpperInvariant(); public string Star => Entry.Favorite ? "★" : ""; }
}
