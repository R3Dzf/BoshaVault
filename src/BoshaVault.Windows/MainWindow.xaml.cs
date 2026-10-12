using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
using Velopack;
using Velopack.Sources;

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
    private UpdateDialog? updates;
    private DateTime nextUpdateCheckUtc = DateTime.UtcNow.AddSeconds(18);
    private bool updateCheckBusy, updatePromptVisible;
    private string? pendingUpdateVersion, alertedUpdateVersion;
    private string? undoEntryId; private DateTime undoUntil;
    private readonly SemaphoreSlim cloudGate = new(1, 1);
    private bool cloudDialogOpen;
    private DateTime nextCloudCheck = DateTime.MinValue;
    private SupabaseCloudAccount.Session? cachedCloudSession;
    private DateTime cachedCloudUntil;

    public MainWindow()
    {
        InitializeComponent();
        var builtVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        InstalledVersionLabel.Text = "LOCKVYN  /  v" + builtVersion;
        SidebarVersionLabel.Text = "v" + builtVersion;
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
        EntryList.PreviewMouseWheel += SlowVaultWheel;
        SystemEvents.SessionSwitch += OnSessionSwitch; SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Closing += OnClosing;
        Closed += (_, _) => { ReleaseDesktopResources(); Application.Current.Shutdown(); };
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) previousWindowState = WindowState;
            TryShowUpdateNotice();
        };
        Activated += (_, _) => TryShowUpdateNotice();
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (DateTime.UtcNow >= PrivateClipboard.Expires) PrivateClipboard.Clear();
            if (revealed && DateTime.UtcNow >= revealUntil) Mask();
            if (UndoButton.Visibility == Visibility.Visible && DateTime.UtcNow > undoUntil) { undoEntryId = null; UndoButton.Visibility = Visibility.Collapsed; }
            if (session != null && (transfer?.IsActive != true || transfer.IsCompleted) && DateTime.UtcNow - lastActivity > TimeSpan.FromMinutes(timeoutMinutes)) LockVault();
            if (preferences.UpdateNotificationsEnabled && !updateCheckBusy &&
                !exiting && DateTime.UtcNow >= nextUpdateCheckUtc)
            {
                nextUpdateCheckUtc = DateTime.UtcNow.AddHours(6);
                _ = CheckUpdateAnnouncement();
            }
            if (!cloudDialogOpen && DateTime.UtcNow >= nextCloudCheck)
            {
                nextCloudCheck = DateTime.UtcNow.AddSeconds(session == null ? 60 : 12);
                _ = RunCloudSync();
            }
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
            filter = "all"; folderFilter = null; SearchBox.Clear(); Refresh(); SearchBox.Focus(); tray?.Update();
            nextCloudCheck = DateTime.MinValue; _ = RunCloudSync();
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
        undoEntryId = null; UndoButton.Visibility = Visibility.Collapsed;
        session?.Dispose(); session = null;
        selected = null; EntryList.ItemsSource = null; DetailUsername.Text = ""; DetailNotes.Text = ""; DetailHost.Text = ""; DetailTitle.Text = ""; SearchBox.Clear();
        MasterBox.Clear(); ConfirmBox.Clear(); LockError.Text = ""; ConfigureUnlock();
    }

    private async Task RunCloudSync()
    {
        if (cloudDialogOpen || resourcesReleased || exiting || !File.Exists(vaultPath) || !await cloudGate.WaitAsync(0)) return;
        CloudProfile? profile = null;
        try
        {
            using var http = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
            byte[] disk = File.ReadAllBytes(vaultPath);
            Guid vaultId;
            try { vaultId = Guid.Parse(VaultCodec.Parse(disk).VaultId); }
            finally { CryptographicOperations.ZeroMemory(disk); }
            var store = new CloudProfileStore(root);
            profile = store.Load(vaultId);
            if (profile == null) { CloudStatus.Text = "Cloud not connected · open Sync"; cachedCloudSession = null; return; }
            if (!profile.AutoSync) { CloudStatus.Text = "Cloud paused on this computer"; cachedCloudSession = null; return; }
            var auth = new SupabaseCloudAccount(http);
            var signedIn = cachedCloudSession;
            if (signedIn == null || cachedCloudUntil < DateTime.UtcNow || signedIn.OwnerId != profile.OwnerId)
            {
                signedIn = await auth.Refresh(profile.RefreshToken);
                if (signedIn.OwnerId != profile.OwnerId) throw new VaultException("Cloud account changed. Reconnect this device.");
                profile.RefreshToken = signedIn.RefreshToken; store.Save(profile);
                if (cloudDialogOpen) return; // Persist refresh rotation before the manual dialog loads it.
                var factors = await auth.ListFactors(signedIn);
                if (factors.RequiresChallenge(signedIn))
                {
                    cachedCloudSession = null; CloudStatus.Text = "Cloud paused · verify 2FA in Sync";
                    return;
                }
                cachedCloudSession = signedIn; cachedCloudUntil = DateTime.UtcNow.AddSeconds(45);
            }
            var transport = new SupabaseSnapshotTransport(http, new Uri(CloudProject.Url), CloudProject.PublishableKey, _ => Task.FromResult(signedIn.AccessToken));
            var opened = session; int stamp = generation;
            var result = await AutomaticCloudSync.RunAsync(transport, profile,
                opened == null ? null : inner =>
                {
                    if (session != opened || generation != stamp || !opened.IsOpen) throw new OperationCanceledException();
                    int conflicts = opened.MergeCloudSnapshots(inner); Refresh();
                    return Task.FromResult((opened.ExportEncrypted(), conflicts));
                },
                () => Task.FromResult(session == opened && generation == stamp && opened?.IsOpen == true
                    ? opened.ExportEncrypted() : File.ReadAllBytes(vaultPath)),
                store.ReadPending, store.WritePending, () => store.Save(profile));
            CloudStatus.Text = result.WaitingForUnlock ? "Cloud update ready · unlock to apply" :
                result.Conflicts > 0 ? $"Cloud synced · {result.Conflicts} conflict copies" : "Cloud synced · up to date";
        }
        catch (Exception ex) when (ex is VaultException or IOException or System.Net.Http.HttpRequestException or CryptographicException or UnauthorizedAccessException or OperationCanceledException)
        { cachedCloudSession = null; if (profile != null) CloudStatus.Text = ex is VaultException ? ex.Message : "Cloud offline · changes retry automatically"; }
        finally { profile?.Clear(); cloudGate.Release(); }
    }

    // WPF defaults to several full list items per wheel notch. Precision laptop
    // touchpads should scroll pixels, not jump through whole screens of logins.
    private static ScrollViewer? VaultScroller(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = VaultScroller(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }
    private void SlowVaultWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = VaultScroller(EntryList);
        if (viewer == null || viewer.ScrollableHeight <= 0) return;
        double distance = Math.Clamp(Math.Abs(e.Delta) * 0.30, 2, 52);
        viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset - Math.Sign(e.Delta) * distance,
            0, viewer.ScrollableHeight));
        e.Handled = true;
        lastActivity = DateTime.UtcNow;
    }

    private string? folderFilter;
    private bool updatingFolders;
    private sealed record FolderChoice(string? Name, string Label) { public override string ToString() => Label; }
    private void SaveGeneratorDefaults(PasswordOptions options)
    { preferences.Generator = options.Clone(); preferences.Save(preferencesPath); }
    private void FolderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingFolders || session == null) return;
        folderFilter = (FolderFilter.SelectedItem as FolderChoice)?.Name; Refresh();
    }
    private void OptionsClick(object sender, RoutedEventArgs e)
    {
        if (session == null) return;
        var d = new FormDialog(this, "Vault options");
        d.Label("These options stay available above your login list.");
        d.Action("Add login", () => { d.Close(); AddClick(sender, e); }, true);
        d.Action("Generate password", () => { d.Close(); GeneratorClick(sender, e); });
        d.Action("Folders", () => { d.Close(); FoldersClick(); });
        d.Action("Sync & Devices", () => { d.Close(); SyncClick(sender, e); });
        d.Action("Backup & Restore", () => { d.Close(); BackupClick(sender, e); });
        d.Action("Settings", () => { d.Close(); SettingsClick(sender, e); });
        d.ShowDialog();
    }
    private void FoldersClick()
    {
        if (session == null) return;
        var d = new FormDialog(this, "Folders");
        d.Label("Choose a folder above the vault list to filter any section. Pick or create folders while editing a login.");
        var names = new ListBox { ItemsSource = FolderCatalog.Choices(session.Data), MaxHeight = 200 };
        d.Body.Children.Add(names); var name = d.Field("New folder name", "", 100);
        d.Action("Add folder", () => Guard(() =>
        {
            if (session == null) return;
            session.AddFolder(name.Text); name.Clear(); names.ItemsSource = FolderCatalog.Choices(session.Data); Refresh();
        }), true); d.ShowDialog();
    }
    private void Refresh()
    {
        if (session == null) return;
        var live = session.Data.Entries.Where(e => !e.Deleted).ToList();
        var report = PasswordHealth.Audit(session.Data.Entries);
        var pending = report.Findings.Select(f => f.EntryId).ToHashSet(StringComparer.Ordinal);
        CountText.Text = live.Count.ToString(); HealthText.Text = live.Count == 0 ? "Ready" : pending.Count == 0 ? "All reviewed" : $"{pending.Count} to review";
        updatingFolders = true;
        try
        {
            var choices = new List<FolderChoice> { new(null, "All folders") };
            choices.AddRange(FolderCatalog.Choices(session.Data).Select(name => new FolderChoice(name,
                name + " (" + session.Data.Entries.Count(e => !e.Purged && (filter == "trash" ? e.Deleted : !e.Deleted) &&
                    (filter != "favorites" || e.Favorite) && (filter != "health" || pending.Contains(e.Id)) &&
                    string.Equals(e.Folder, name, StringComparison.OrdinalIgnoreCase)) + ")")));
            FolderFilter.ItemsSource = choices;
            FolderFilter.SelectedItem = choices.FirstOrDefault(c => string.Equals(c.Name, folderFilter, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
            folderFilter = ((FolderChoice)FolderFilter.SelectedItem).Name;
        }
        finally { updatingFolders = false; }
        string search = SearchBox.Text.Trim();
        var items = session.Data.Entries.Where(e => !e.Purged && (filter == "trash" ? e.Deleted : !e.Deleted))
            .Where(e => filter != "favorites" || e.Favorite)
            .Where(e => filter != "health" || pending.Contains(e.Id))
            .Where(e => folderFilter == null || string.Equals(e.Folder, folderFilter, StringComparison.OrdinalIgnoreCase))
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
        PermanentDeleteButton.Visibility = selected.Deleted ? Visibility.Visible : Visibility.Collapsed;
        var finding = session == null ? null : PasswordHealth.Audit(session.Data.Entries, true).Findings.FirstOrDefault(f => f.EntryId == selected.Id);
        bool reviewed = finding != null && selected.ReviewAcknowledgement == finding.Fingerprint;
        ReviewWarnings.Text = finding == null ? "" : (reviewed ? "Reviewed: " : "Review: ") + string.Join("; ", finding.Reasons);
        ReviewButton.Visibility = finding != null && !reviewed ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (SearchHint != null) SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; if (session != null) Refresh(); }
    private void AllClick(object sender, RoutedEventArgs e) { filter = "all"; Refresh(); }
    private void FavoriteClick(object sender, RoutedEventArgs e) { filter = "favorites"; Refresh(); }
    private void TrashClick(object sender, RoutedEventArgs e) { filter = "trash"; Refresh(); }
    private void HealthClick(object sender, RoutedEventArgs e) { filter = "health"; Refresh(); SetStatus("Local checks flag short, reused, common or predictable passwords. Reviewed risks return if they change. No online breach lookup."); }
    private void AddClick(object sender, RoutedEventArgs e) => Edit(null);
    private void EditClick(object sender, RoutedEventArgs e) { if (selected != null) Edit(selected); }
    private void Edit(VaultEntry? entry)
    {
        if (session == null) return;
        var updated = FormDialog.Edit(this, entry, folders: FolderCatalog.Choices(session.Data),
            generatorOptions: preferences.Generator, saveGeneratorDefaults: SaveGeneratorDefaults);
        if (updated == null || session == null) return;
        Guard(() => { session.Upsert(updated); selected = updated; Refresh(); SetStatus("Login saved to your encrypted vault."); });
    }
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (selected == null || session == null) return;
        Guard(() => { var copy = selected.Clone(); copy.Deleted = !copy.Deleted; session.Upsert(copy); undoEntryId = copy.Deleted ? copy.Id : null; undoUntil = DateTime.UtcNow.AddSeconds(15); UndoButton.Visibility = copy.Deleted ? Visibility.Visible : Visibility.Collapsed; nextCloudCheck = DateTime.MinValue; Refresh(); SetStatus(copy.Deleted ? "Moved to encrypted trash. Undo is available for 15 seconds." : "Login restored."); });
    }
    private void UndoClick(object sender, RoutedEventArgs e)
    {
        if (session == null || undoEntryId == null || DateTime.UtcNow > undoUntil) return;
        Guard(() => { var entry = session.Data.Entries.FirstOrDefault(x => x.Id == undoEntryId && x.Deleted && !x.Purged); if (entry == null) return; var copy = entry.Clone(); copy.Deleted = false; session.Upsert(copy); undoEntryId = null; UndoButton.Visibility = Visibility.Collapsed; nextCloudCheck = DateTime.MinValue; Refresh(); SetStatus("Login restored."); });
    }
    private void LockedCloudClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (File.Exists(vaultPath)) { LockError.Text = "Unlock this vault and connect it from Sync. A local vault already exists."; return; }
        var d = new FormDialog(this, "Restore encrypted cloud vault", 510);
        d.Label("Sign in with the same cloud account and paste the connection code from your own device. Your master passphrase unlocks the encrypted backup on this computer.");
        var email = d.Field("Cloud account email", "", 320); var account = d.Secret("Cloud account password");
        var code = d.Secret("Cloud connection code (BVCLOUD1:…)"); var master = d.Secret("Vault master passphrase");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) }; d.Body.Children.Add(status);
        using var cancellation = new CancellationTokenSource();
        using var http = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        var auth = new SupabaseCloudAccount(http); bool running = false; int stamp = generation;
        var restore = d.Action("Sign in & restore", async () =>
        {
            if (running) return; running = true;
            string secret = account.Password, pairingCode = code.Password.Trim(), password = master.Password; account.Clear(); master.Clear();
            try
            {
                if (File.Exists(vaultPath)) throw new VaultException("A local vault already exists.");
                status.Text = "Signing in and checking encrypted snapshots…";
                var signed = await auth.SignIn(email.Text.Trim(), secret, cancellation.Token);
                var factors = await auth.ListFactors(signed, cancellation.Token);
                if (factors.RequiresChallenge(signed)) signed = CloudMfaDialog.Verify(d, auth, signed, factors.Totp, _ => { },
                    () => { if (stamp != generation || !d.IsVisible) throw new OperationCanceledException(); }, cancellation.Token);
                var pairing = CloudPairing.Decode(pairingCode, Guid.Empty, signed.OwnerId);
                var transport = new SupabaseSnapshotTransport(http, new Uri(CloudProject.Url), CloudProject.PublishableKey, _ => Task.FromResult(signed.AccessToken));
                var rows = await transport.ListAsync(pairing.VaultSlot, cancellation.Token);
                if (rows.Count == 0) throw new VaultException("No cloud backup yet. Sync the first device before joining.");
                byte[] key = CloudSnapshotCodec.ParseRecoveryKey(pairing.RecoveryKey), inner = [];
                try
                {
                    inner = CloudSnapshotCodec.Open(rows[0].Blob, key, pairing.VaultSlot, rows[0].DeviceSlot);
                    if (VaultCodec.Parse(inner).VaultId != pairing.VaultId.ToString("D")) throw new VaultException("Cloud vault identity does not match the connection code.");
                    if (stamp != generation || File.Exists(vaultPath)) throw new OperationCanceledException();
                    var opened = await VaultSession.RestoreCloudNew(vaultPath, inner, password, deviceId);
                    if (stamp != generation) { opened.Dispose(); throw new OperationCanceledException(); }
                    session = opened; var profile = new CloudProfile { OwnerId = signed.OwnerId, VaultId = pairing.VaultId, VaultSlot = pairing.VaultSlot,
                        DeviceSlot = Guid.NewGuid(), RecoveryKey = pairing.RecoveryKey, RefreshToken = signed.RefreshToken, Email = email.Text.Trim() };
                    try { new CloudProfileStore(root).Save(profile); } finally { profile.Clear(); }
                    LockView.Visibility = Visibility.Collapsed; VaultView.Visibility = Visibility.Visible; filter = "all"; folderFilter = null; SearchBox.Clear();
                    MasterBox.Clear(); ConfirmBox.Clear(); lastActivity = DateTime.UtcNow; Refresh(); tray?.Update(); d.Close(); nextCloudCheck = DateTime.MinValue; _ = RunCloudSync();
                }
                finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(inner); foreach (var row in rows) CryptographicOperations.ZeroMemory(row.Blob); }
            }
            catch (OperationCanceledException) { status.Text = "Cloud restore cancelled. Your local vault is preserved."; }
            catch (Exception ex) when (ex is VaultException or IOException or System.Net.Http.HttpRequestException or CryptographicException or UnauthorizedAccessException)
            { status.Text = ex is VaultException ? ex.Message : "Cloud could not be reached or opened. Try again when connected."; }
            finally { secret = ""; pairingCode = ""; password = ""; running = false; }
        }, true);
        d.Action("Cancel", d.Close); d.Closed += (_, _) => { cancellation.Cancel(); code.Clear(); account.Clear(); master.Clear(); };
        d.ShowDialog();
    }
    private void PermanentDeleteClick(object sender, RoutedEventArgs e)
    {
        if (selected == null || session == null || !selected.Deleted) return;
        string id = selected.Id;
        if (MessageBox.Show(this, "Permanently remove this login from the vault? It cannot be restored. Older exported backups and unsynced devices may still hold their previous encrypted copy.", "Delete permanently", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        Guard(() => { session.DeletePermanently(id); selected = null; PrivateClipboard.Clear(); Refresh(); SetStatus("Login permanently deleted. Sync your devices to apply this deletion everywhere."); });
    }
    private void ReviewClick(object sender, RoutedEventArgs e)
    {
        if (selected == null || session == null || selected.Deleted) return;
        if (MessageBox.Show(this, "I reviewed the warnings and accept this password for this login. Remove it from Review? Warnings return if the password or its risks change.", "Confirm review", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        Guard(() => { session.AcknowledgeReview(selected.Id); Refresh(); SetStatus("Review confirmed. This does not change the password or make it stronger."); });
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
        PasswordGeneratorDialog.Show(this, preferences.Generator, SaveGeneratorDefaults, Copy);
    }
    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (session == null) return;
        var d = new FormDialog(this, "Your vault settings");
        d.Action("Check for updates", () => { d.Close(); UpdatesClick(sender, e); }, inBody: true);
        var updateAlerts = new CheckBox {
            Content = "Notify me when new versions are available",
            IsChecked = preferences.UpdateNotificationsEnabled,
            Margin = new Thickness(0, 10, 0, 10) };
        d.Body.Children.Add(updateAlerts);
        updateAlerts.Click += (_, _) => Guard(() =>
        {
            preferences.UpdateNotificationsEnabled = updateAlerts.IsChecked == true;
            preferences.Save(preferencesPath);
            if (preferences.UpdateNotificationsEnabled)
                nextUpdateCheckUtc = DateTime.UtcNow.AddSeconds(2);
            else pendingUpdateVersion = null;
        });
        var startup = new CheckBox { Content = "Start with Windows · locked in the tray", IsChecked = StartupEnabled() }; d.Body.Children.Add(startup);
        startup.Click += (_, _) => { SetStartup(startup.IsChecked == true); startup.IsChecked = StartupEnabled(); };
        d.Label("Closing the main window locks the vault and keeps Lockvyn in the tray. Click the tray icon or use Ctrl + Alt + P to reopen. Right-click the icon for Exit.");
        var browserAuto = new CheckBox { Content = "Enable local Chrome / Edge Autofill", IsChecked = preferences.BrowserAutofillEnabled };
        d.Body.Children.Add(browserAuto);
        browserAuto.Click += (_, _) => Guard(() => {
            preferences.BrowserAutofillEnabled = browserAuto.IsChecked == true;
            preferences.Save(preferencesPath);
            SetStatus(preferences.BrowserAutofillEnabled ? "Browser Autofill enabled." : "Browser Autofill disabled.");
        });
        d.Label("Browser Autofill setup: 1. Open the extension folder below. 2. Load it as an unpacked extension in Chrome or Edge. 3. Run Install-BrowserAutofill.ps1 with the extension ID shown by your browser. Matching HTTPS logins appear near password fields and every fill asks for your approval.");
        d.Action("Open browser extension folder", () => Guard(() => {
            string folder = System.IO.Path.Combine(AppContext.BaseDirectory, "browser-extension");
            if (!Directory.Exists(folder)) throw new VaultException("The extension folder is missing from this installation. Reinstall the complete Windows package.");
            Process.Start(new ProcessStartInfo("explorer.exe", '"' + folder + '"') { UseShellExecute = true });
        }), inBody: true);
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
            popup.Label($"Saved: {review.Total} logins. Awaiting review: {review.AtRisk}. Reused: {review.Reused}. Short: {review.Short}. Common: {review.CommonPassword}. Predictable: {review.Predictable}.");
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
        d.Action("Exit Lockvyn", ExitApplication);
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
        var d = new FormDialog(this, "Sync & Devices", 540);
        CloudProfile? summaryProfile = null;
        try { summaryProfile = new CloudProfileStore(root).Load(Guid.Parse(session.Data.VaultId)); }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException) { }
        bool connected = summaryProfile != null;
        string lastSynced = summaryProfile?.LastSyncUtc ?? "";
        summaryProfile?.Clear();
        d.Label(connected
            ? "Cloud sync is connected. New passwords sync automatically when Internet is available. Open your vault on other devices to apply changes."
            : "Connect an encrypted cloud account once to sync passwords between your devices automatically.");
        var cloudCard = new StackPanel { Margin = new Thickness(0, 8, 0, 14) };
        cloudCard.Children.Add(new TextBlock {
            Text = connected ? "● Cloud connected · automatic sync" : "○ Cloud not connected",
            FontWeight = FontWeights.SemiBold, FontSize = 17,
            Foreground = connected ? Brushes.SeaGreen : Brushes.SlateGray });
        cloudCard.Children.Add(new TextBlock { Text = connected
            ? "Use Cloud Sync to view last sync, sync manually, or manage your account."
            : "Your local vault stays available offline. Cloud is optional.",
            Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 7) });
        d.Body.Children.Add(cloudCard);
        if (connected)
        {
            d.Label(lastSynced.Length == 0 ? "No successful sync yet." : "Last synced: " + lastSynced);
            var hubStatus = new TextBlock { Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 5) };
            d.Action("Sync Now", async () =>
            {
                hubStatus.Text = "Synchronizing encrypted changes…";
                await RunCloudSync();
                if (d.IsVisible) hubStatus.Text = CloudStatus.Text;
            }, true, true);
            d.Body.Children.Add(hubStatus);
        }
        d.Action("Cloud Sync · manage account", () => { d.Close(); CloudClick(sender, e); }, false, true);
        d.Action("Add new device · secure QR", () => { d.Close(); OpenCloudSync(true); }, false, true);
        d.Label("First-time pairing displays a two-minute public-key QR and asks you to verify six digits on both devices.");
        d.Action("Offline transfer · private Wi-Fi", () => { d.Close(); OfflineTransferClick(sender, e); }, false, true);
        d.Label("Use offline transfer only when cloud access is unavailable. Private-network tools are tucked into advanced options.");
        d.ShowDialog();
    }

    private void BackupClick(object sender, RoutedEventArgs e)
    {
        if (session == null) return;
        var d = new FormDialog(this, "Backup & Restore", 500);
        d.Label("Keep an encrypted backup on another trusted drive. Export and restore never send your vault to a server.");
        d.Action("Export encrypted backup", () => { d.Close(); Export(); }, true, true);
        d.Action("Import / merge encrypted backup", () => { d.Close(); ImportMerge(); }, false, true);
        d.Label("Import verifies the encrypted vault; any conflicting records are preserved. Your local vault is never silently replaced.");
        d.ShowDialog();
    }

    private async void OfflineTransferClick(object sender, RoutedEventArgs e)
    {
        if (session == null) return;
        cloudDialogOpen = true;
        cachedCloudSession = null;
        await cloudGate.WaitAsync(); cloudGate.Release();
        var d = new FormDialog(this, "Devices & encrypted backup", 610);
        d.Label("Send an encrypted vault locally when the cloud is unavailable. On Android choose Sync & Devices → Offline Wi-Fi transfer.");
        int networkStart = d.Body.Children.Count;
        string defaultIp = ""; try { defaultIp = LocalTransferServer.FindPrivateHost(); } catch (Exception ex) when (ex is VaultException or System.Net.Sockets.SocketException) { }
        var address = d.Field("Your private Wi-Fi IPv4 address", defaultIp, 15);
        var networks = new ComboBox { Margin = new(0, 6, 0, 8), MinWidth = 300 };
        void RefreshNetworks()
        {
            try
            {
                var choices = LocalTransferServer.PrivateInterfaces();
                networks.ItemsSource = choices; networks.DisplayMemberPath = "Label";
                networks.SelectedItem = choices.FirstOrDefault(n => n.Host == address.Text) ?? choices.FirstOrDefault();
            }
            catch (System.Net.NetworkInformation.NetworkInformationException) { networks.ItemsSource = null; }
        }
        networks.SelectionChanged += (_, _) => { if (networks.SelectedItem is LocalTransferServer.PrivateInterface choice) address.Text = choice.Host; };
        d.Body.Children.Add(networks); RefreshNetworks();
        d.Action("Refresh Wi-Fi addresses", RefreshNetworks, inBody: true);
        var networkAdvanced = new StackPanel();
        while (d.Body.Children.Count > networkStart)
        {
            var element = d.Body.Children[networkStart];
            d.Body.Children.RemoveAt(networkStart);
            networkAdvanced.Children.Add(element);
        }
        d.Body.Children.Add(new Expander {
            Header = "Advanced network settings · IP addresses",
            Content = networkAdvanced, Margin = new Thickness(0, 12, 0, 12) });
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
            else if (transfer is { IsCompleted: true }) { ClearPairing(); status.Text = "Transfer complete. Both devices have the merged vault."; }
            else if (transfer is { IsActive: false }) { ClearPairing(); status.Text = "Transfer stopped. Start a new private transfer and scan its new QR."; }
            else { countdown.Text = $"Expires in {remaining / 60}:{remaining % 60:00}"; if (transfer?.LastIssue != null) status.Text = transfer.LastIssue; }
        };
        d.Action("Start a private transfer", () => Guard(() =>
        {
            ClearPairing(); transfer?.Dispose(); transfer = null;
            LocalTransferServer? currentTransfer = null;
            currentTransfer = new LocalTransferServer(
                () => Dispatcher.Invoke(() => { if (transfer != currentTransfer || session == null) throw new VaultException("Transfer stopped."); return session.ExportEncrypted(); }),
                bytes => Dispatcher.Invoke(() => { if (transfer != currentTransfer || session == null || currentTransfer?.IsActive != true) throw new VaultException("Transfer stopped."); int c = MergeBackup(bytes); ClearPairing(); Refresh(); Touch(); status.Text = c == 0 ? "Transfer complete. Both devices have the merged vault." : $"Transfer complete. {c} conflict copies kept."; }),
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
        var copyPairing = d.Action("Copy pairing code · fallback", () => { if (pairing.Text.Length > 0) Copy(pairing.Text); }, inBody: true);
        d.Body.Children.Remove(copyPairing);
        networkAdvanced.Children.Add(copyPairing);
        networkAdvanced.Children.Add(new Expander { Header = "Manual pairing code · fallback", Content = pairing, Margin = new(0, 12, 0, 12) });
        d.Label("If Windows asks for firewall access, allow this app on your private network. Guest Wi-Fi may block connections.");
        var firewallButton = d.Action("Allow Lockvyn on private Wi-Fi (administrator approval)", async () =>
        {
            try
            {
                string script = System.IO.Path.Combine(AppContext.BaseDirectory, "Configure-PrivateTransfer.ps1");
                string app = Environment.ProcessPath ?? throw new IOException();
                if (!File.Exists(script)) throw new IOException();
                string command = "& '" + script.Replace("'", "''") + "' -ProgramPath '" + app.Replace("'", "''") + "'";
                string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));
                string powershell = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                using var process = Process.Start(new ProcessStartInfo(powershell, "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                if (process == null) throw new IOException();
                await process.WaitForExitAsync();
                status.Text = process.ExitCode == 0
                    ? "Private-network access is configured for this installed app. Set your trusted home Wi-Fi to Private in Windows, then retry the current QR."
                    : "Windows could not configure private-network access. Your firewall remains enabled. Check administrator permissions.";
            }
            catch (Win32Exception) { status.Text = "Administrator approval was cancelled. Private-network access was not changed."; }
            catch (IOException) { status.Text = "Cannot locate the installed app's private-transfer helper. Update Lockvyn and retry."; }
        }, inBody: true);
        d.Body.Children.Remove(firewallButton);
        networkAdvanced.Children.Add(firewallButton);
        d.Label("This optional action needs administrator approval and allows only private local-network traffic for Lockvyn.");
        var firewallHelp = d.Body.Children[^1];
        d.Body.Children.RemoveAt(d.Body.Children.Count - 1);
        networkAdvanced.Children.Add(firewallHelp);
        try { d.ShowDialog(); } finally { ClearPairing(); transfer?.Dispose(); transfer = null; cloudDialogOpen = false; nextCloudCheck = DateTime.MinValue; }
    }
    private void CloudClick(object sender, RoutedEventArgs e) => OpenCloudSync(false);

    private async void OpenCloudSync(bool openPairing)
    {
        if (session == null || cloudDialogOpen) return;
        cloudDialogOpen = true;
        cachedCloudSession = null;
        try
        {
            await cloudGate.WaitAsync(); cloudGate.Release();
            if (session == null) return;
            var opened = session; int stamp = generation;
            CloudSyncDialog.Show(this, root, opened, () => session == opened && generation == stamp,
                () => { Refresh(); Touch(); }, Copy, openPairing);
        }
        finally { cloudDialogOpen = false; if (!new CloudProfileStore(root).Exists) CloudStatus.Text = "Cloud is optional"; nextCloudCheck = DateTime.MinValue; }
    }
    private void Export()
    {
        if (session == null) return;
        var picker = new SaveFileDialog { Filter = "Encrypted vault (*.boshavault)|*.boshavault", FileName = $"Lockvyn-{DateTime.Now:yyyy-MM-dd}.boshavault" };
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
    { try { action(); } catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException or Win32Exception or COMException or System.Net.Sockets.SocketException) { MessageBox.Show(this, ex is VaultException ? ex.Message : ex is System.Net.Sockets.SocketException ? "Cannot start the transfer at this address. Use your current Wi-Fi IPv4 address and check your private-network firewall settings." : "The action could not be completed. Check file access and retry.", "Lockvyn"); } }
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) { if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect) Dispatcher.BeginInvoke(LockVault); }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Suspend) Dispatcher.BeginInvoke(LockVault); }
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        hwnd = new WindowInteropHelper(this).Handle; source = HwndSource.FromHwnd(hwnd); source?.AddHook(WndProc);
        if (!RegisterHotKey(hwnd, 1, 0x4003, 0x50)) SetStatus("Ctrl+Alt+P is already in use by another app. Open Lockvyn from the taskbar.");
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
        { BringToFront(); MessageBox.Show(this, "Cannot update your Windows startup setting. Check your user-account permissions.", "Lockvyn"); }
    }
    private void ExitApplication() { PrepareToExit(); Application.Current.Shutdown(); }
    private void UpdatesClick(object sender, RoutedEventArgs e) => ShowUpdates(false);

    private void ShowUpdates(bool checkImmediately)
    {
        if (updates != null) { updates.Activate(); return; }
        updates = new UpdateDialog(this, checkImmediately);
        updates.Closed += (_, _) => updates = null;
        BringToFront();
        updates.Show();
    }

    private async Task CheckUpdateAnnouncement()
    {
        if (updateCheckBusy || !preferences.UpdateNotificationsEnabled || exiting) return;
        updateCheckBusy = true;
        try
        {
            var manager = new UpdateManager(
                new GithubSource("https://github.com/R3Dzf/BoshaVault", null, false));
            if (!manager.IsInstalled) return;
            var update = await manager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(16));
            if (update == null || exiting || !preferences.UpdateNotificationsEnabled) return;
            string version = update.TargetFullRelease.Version.ToString();
            if (version == alertedUpdateVersion) return;
            pendingUpdateVersion = version;
            alertedUpdateVersion = version;
            if (!IsVisible || WindowState == WindowState.Minimized)
                tray?.ShowUpdateAvailable(version);
            else TryShowUpdateNotice();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or
                                    OperationCanceledException or TimeoutException)
        {
            // Update availability never blocks vault unlock, offline use or cloud sync.
        }
        finally { updateCheckBusy = false; }
    }

    private void TryShowUpdateNotice()
    {
        if (updatePromptVisible || !preferences.UpdateNotificationsEnabled || exiting ||
            string.IsNullOrEmpty(pendingUpdateVersion) || !IsVisible ||
            WindowState == WindowState.Minimized || updates != null ||
            OwnedWindows.Cast<Window>().Any(window => window.IsVisible)) return;
        string version = pendingUpdateVersion;
        pendingUpdateVersion = null;
        updatePromptVisible = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (exiting) { updatePromptVisible = false; return; }
            var dialog = new FormDialog(this, "Update available · " + version, 490);
            dialog.Label("A new Lockvyn version is available. Your encrypted vault and settings stay on this device.");
            dialog.Label("You can read a short What's New summary and see the download size before downloading.");
            dialog.Action("Update now", () =>
            {
                dialog.Close();
                ShowUpdates(true);
            }, true, true);
            dialog.Action("Later", dialog.Close, inBody: true);
            dialog.Action("Don't notify me again", () =>
            {
                Guard(() =>
                {
                    preferences.UpdateNotificationsEnabled = false;
                    preferences.Save(preferencesPath);
                });
                dialog.Close();
            }, inBody: true);
            dialog.Closed += (_, _) => updatePromptVisible = false;
            dialog.ShowDialog();
        });
    }
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
