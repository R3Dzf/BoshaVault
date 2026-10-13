using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoshaVault.Core;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Velopack;
using Velopack.Sources;

namespace BoshaVault.Windows;

/// <summary>Installed-version-aware update screen with release-asset size estimates.</summary>
internal sealed class UpdateDialog : Window
{
    private static readonly HttpClient PolicyHttp = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly UpdateManager manager = new(new GithubSource("https://github.com/R3Dzf/BoshaVault", null, false));
    private readonly CancellationTokenSource cancellation = new();
    private readonly TextBlock status, versions, sizes, progressLabel, fallbackNote;
    private readonly ProgressBar progress;
    private readonly Button check, download, restart, whatsNew;
    private readonly MainWindow owner;
    private UpdateInfo? update;
    private long plannedBytes, fullBytes;
    private bool working, closed;

    private static string FormatBytes(long n)
    {
        if (n < 0) return "Unknown";
        if (n < 1024) return $"{n} B";
        if (n < 1024 * 1024) return $"{n / 1024d:0.#} KB";
        if (n < 1024L * 1024 * 1024) return $"{n / (1024d * 1024):0.##} MB";
        return $"{n / (1024d * 1024 * 1024):0.##} GB";
    }

    private static string AppVersion =>
        (typeof(UpdateDialog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? typeof(UpdateDialog).Assembly.GetName().Version?.ToString(3) ?? "unknown")
        .Split('+')[0];

    public UpdateDialog(MainWindow owner, bool checkImmediately = false)
    {
        this.owner = owner;
        Owner = owner;
        Title = "Lockvyn · Software update";
        Width = 545;
        MinHeight = 440;
        MaxHeight = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new StackPanel { Margin = new Thickness(24) };
        scroller.Content = body; Content = scroller;

        body.Children.Add(new TextBlock { Text = "Software update", FontSize = 25, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = "Download only when you're ready. Your encrypted vault and settings are preserved.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 12) });
        versions = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 2, 0, 8), TextWrapping = TextWrapping.Wrap };
        body.Children.Add(versions);
        sizes = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 4, 0, 8) };
        body.Children.Add(sizes);
        fallbackNote = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 13) };
        body.Children.Add(fallbackNote);
        status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Foreground = Brushes.DimGray };
        body.Children.Add(status);
        progressLabel = new TextBlock { Visibility = Visibility.Collapsed, FontWeight = FontWeights.SemiBold, FontSize = 12 };
        body.Children.Add(progressLabel);
        progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 13, Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 8, 0, 12) };
        body.Children.Add(progress);
        check = Action(body, "Check for updates", CheckAsync);
        whatsNew = Action(body, "What's New · additions, improvements & fixes", WhatsNewAsync);
        whatsNew.IsEnabled = false;
        download = Action(body, "Download update", DownloadAsync); download.IsEnabled = false;
        restart = Action(body, "Restart and install", RestartAsync); restart.IsEnabled = false;
        Action(body, "View release notes on GitHub", () =>
        {
            Process.Start(new ProcessStartInfo("https://github.com/R3Dzf/BoshaVault/releases") { UseShellExecute = true });
            return Task.CompletedTask;
        });
        var close = new Button { Content = "Close / cancel", Margin = new Thickness(0, 6, 0, 0), MinHeight = 36 };
        close.Click += (_, _) => Close(); body.Children.Add(close);
        versions.Text = $"Installed: v{(manager.IsInstalled ? manager.CurrentVersion?.ToString() : AppVersion)}";
        status.Text = manager.IsInstalled
            ? "Select Check for updates to see the exact package sizes before downloading."
            : "This portable copy does not support in-app updating. Install Lockvyn-win-Setup.exe once; your vault stays intact.";
        check.IsEnabled = manager.IsInstalled;
        Closed += (_, _) => { closed = true; cancellation.Cancel(); if (!working) cancellation.Dispose(); };
        PreviewKeyDown += (_, _) => owner.Touch();
        PreviewMouseDown += (_, _) => owner.Touch();
        if (checkImmediately)
            Loaded += async (_, _) =>
            {
                if (working || closed) return;
                try { await CheckAsync(); }
                catch (OperationCanceledException) { if (!closed) status.Text = "Update check cancelled."; }
                catch (Exception) { if (!closed) status.Text = "Cannot check for updates. Retry from this screen."; }
                finally
                {
                    if (!closed) { working = false; check.IsEnabled = manager.IsInstalled; download.IsEnabled = update != null; }
                }
            };
    }

    private Button Action(StackPanel body, string label, Func<Task> work)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 4, 0, 0), MinHeight = 39 };
        body.Children.Add(button);
        button.Click += async (_, _) =>
        {
            if (working) return;
            try { await work(); }
            catch (OperationCanceledException) { if (!closed) status.Text = "Update cancelled. You can retry safely."; }
            catch (Exception)
            {
                if (!closed) status.Text = "Update failed. Check connectivity, storage, or GitHub status and retry. Your encrypted vault is unchanged.";
            }
            finally
            {
                working = false;
                if (closed) cancellation.Dispose();
                else
                {
                    check.IsEnabled = manager.IsInstalled;
                    download.IsEnabled = update != null && !restart.IsEnabled;
                    whatsNew.IsEnabled = update != null;
                }
            }
        };
        return button;
    }

    private void Begin(string text)
    {
        working = true;
        check.IsEnabled = download.IsEnabled = restart.IsEnabled = whatsNew.IsEnabled = false;
        status.Text = text;
    }

    private async Task<(string? CandidateTag, Version MinimumVersion, int RolloutPercent)> LoadReleasePolicyAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            CloudProject.Url + "functions/v1/lockvyn-admin/release-policy?platform=windows");
        request.Headers.Add("apikey", CloudProject.PublishableKey);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await PolicyHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 8192)
            throw new InvalidOperationException("Cannot verify the Windows update policy.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation.Token);
        var root = document.RootElement;
        if (!root.TryGetProperty("platform", out var platform) || platform.GetString() != "windows" ||
            !root.TryGetProperty("minimumVersion", out var minimum) || minimum.ValueKind != JsonValueKind.String ||
            !Version.TryParse(minimum.GetString(), out var floor) ||
            !root.TryGetProperty("rolloutPercent", out var rollout) || !rollout.TryGetInt32(out int percent) ||
            percent is < 0 or > 100 || !root.TryGetProperty("candidateTag", out var candidate))
            throw new InvalidOperationException("Invalid Windows update policy.");
        string? tag = candidate.ValueKind == JsonValueKind.Null ? null :
            candidate.ValueKind == JsonValueKind.String ? candidate.GetString() : null;
        if ((candidate.ValueKind != JsonValueKind.Null && tag is null) ||
            (tag is not null && !Regex.IsMatch(tag, @"^windows-v[0-9]+\.[0-9]+\.[0-9]+$")))
            throw new InvalidOperationException("Invalid Windows update candidate.");
        return (tag, floor!, percent);
    }

    private async Task CheckAsync()
    {
        Begin("Checking official Windows release feed…");
        update = null; whatsNew.IsEnabled = false; plannedBytes = fullBytes = 0;
        sizes.Text = ""; fallbackNote.Text = "";
        progress.Visibility = progressLabel.Visibility = Visibility.Collapsed;
        var policy = await LoadReleasePolicyAsync();
        Version installedVersion = manager.CurrentVersion ?? Version.Parse(AppVersion);
        bool mandatory = installedVersion < policy.MinimumVersion;
        // Rollout sampling stays on this device. No stable device identifier or telemetry is sent.
        if (!mandatory && (policy.RolloutPercent == 0 || Random.Shared.Next(100) >= policy.RolloutPercent))
        {
            versions.Text = $"Installed: v{installedVersion} · staged rollout";
            status.Text = "This installation is not in the current optional update group. Retry later; your encrypted vault is unchanged.";
            return;
        }
        update = await manager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(30), cancellation.Token);
        if (closed) return;
        if (update == null)
        {
            if (mandatory) throw new InvalidOperationException("A required newer Windows release is not available yet. Retry later.");
            versions.Text = $"Installed: v{manager.CurrentVersion} · Up to date";
            status.Text = "You're running the latest stable Lockvyn release.";
            return;
        }
        string availableTag = "windows-v" + update.TargetFullRelease.Version.ToString();
        if (policy.CandidateTag is not null && !StringComparer.Ordinal.Equals(policy.CandidateTag, availableTag))
        {
            update = null;
            versions.Text = $"Installed: v{manager.CurrentVersion} · candidate held";
            status.Text = "The published Windows candidate is held by the release policy. The installed app and vault are unchanged.";
            return;
        }
        fullBytes = update.TargetFullRelease.Size;
        long deltaBytes = update.DeltasToTarget?.Sum(part => part.Size) ?? 0L;
        bool eligible = deltaBytes > 0 && deltaBytes < fullBytes && !update.IsDowngrade;
        plannedBytes = eligible ? deltaBytes : fullBytes;
        versions.Text = $"Installed: v{manager.CurrentVersion}    →    Available: v{update.TargetFullRelease.Version}";
        sizes.Text = $"Expected download: {FormatBytes(plannedBytes)}" +
                     (eligible ? $"  ·  Delta update ({update.DeltasToTarget!.Length} package(s))" : "  ·  Full update") +
                     $"\nFull package size: {FormatBytes(fullBytes)}";
        fallbackNote.Text = eligible
            ? $"Saves about {(1d - (double)plannedBytes / fullBytes) * 100:0.#}% of download data. If the delta cannot be applied, Velopack may fetch the full {FormatBytes(fullBytes)} package instead."
            : "No compatible smaller delta is available for this installed version.";
        status.Text = "Ready to download. Read What's New first if you want a quick release summary.";
        whatsNew.IsEnabled = true;
        download.IsEnabled = true;
    }

    private async Task WhatsNewAsync()
    {
        if (update == null) return;
        string version = update.TargetFullRelease.Version.ToString();
        ReleaseNotesSummary.Summary release = await ReleaseNotesSummary.LoadAsync(version, cancellation.Token);
        if (closed) return;
        var details = new FormDialog(this, "What's New · v" + version, 535);
        void Section(string name, string[] items)
        {
            details.Body.Children.Add(new TextBlock {
                Text = name, FontWeight = FontWeights.SemiBold, FontSize = 16,
                Margin = new Thickness(0, 16, 0, 8), Foreground = new SolidColorBrush(Color.FromRgb(23,27,46))
            });
            if (items.Length == 0)
            {
                details.Label("No separately listed items.");
                return;
            }
            foreach (string item in items.Take(4))
                details.Label("• " + item);
        }
        if (release.Verified)
        {
            Section("New features", release.Added);
            Section("Improvements", release.Improved);
            Section("Fixes", release.Fixed);
        }
        else details.Label("Cannot load the official release notes right now. You can read them on GitHub.");
        details.Action("View complete release notes", () =>
            Process.Start(new ProcessStartInfo(
                "https://github.com/R3Dzf/BoshaVault/releases/tag/windows-v" + version
            ) { UseShellExecute = true }), inBody: true);
        details.Action("Close", details.Close, true);
        details.ShowDialog();
    }

    private async Task DownloadAsync()
    {
        if (update == null) return;
        Begin("Downloading and verifying the update package checksums…");
        progress.Value = 0;
        progress.Visibility = progressLabel.Visibility = Visibility.Visible;
        progressLabel.Text = $"Preparing · 0% · planned transfer {FormatBytes(plannedBytes)}";
        await manager.DownloadUpdatesAsync(update, percent =>
        {
            if (!closed && !Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() =>
                {
                    if (closed) return;
                    int p = Math.Clamp(percent, 0, 100);
                    progress.Value = p;
                    // SDK reports combined download/verify/reconstruction progress, not
                    // network bytes. Never present a fabricated byte counter as measured.
                    progressLabel.Text = $"Download & verification: {p}% · approximately {FormatBytes((long)(plannedBytes * (p / 100d)))} / {FormatBytes(plannedBytes)} (combined estimate)" +
                                         (fullBytes != plannedBytes ? $" · fallback up to {FormatBytes(fullBytes)}" : "");
                });
        }, cancellation.Token);
        if (closed) return;
        progress.Value = 100;
        progressLabel.Text = "Download and checksum verification: 100%";
        restart.IsEnabled = true;
        status.Text = $"v{update.TargetFullRelease.Version} is ready. Restart and install will lock the vault and close the application safely.";
    }

    private Task RestartAsync()
    {
        if (update == null || manager.UpdatePendingRestart == null) return Task.CompletedTask;
        manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: false, restart: true, restartArgs: []);
        owner.PrepareToExit();
        Application.Current.Shutdown();
        return Task.CompletedTask;
    }
}
