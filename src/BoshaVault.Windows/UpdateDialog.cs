using System.Diagnostics;
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
    private readonly UpdateManager manager = new(new GithubSource("https://github.com/R3Dzf/BoshaVault", null, false));
    private readonly CancellationTokenSource cancellation = new();
    private readonly TextBlock status, versions, sizes, progressLabel, fallbackNote;
    private readonly ProgressBar progress;
    private readonly Button check, download, restart;
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

    public UpdateDialog(MainWindow owner)
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
                }
            }
        };
        return button;
    }

    private void Begin(string text)
    {
        working = true;
        check.IsEnabled = download.IsEnabled = restart.IsEnabled = false;
        status.Text = text;
    }

    private async Task CheckAsync()
    {
        Begin("Checking official Windows release feed…");
        update = null; plannedBytes = fullBytes = 0;
        sizes.Text = ""; fallbackNote.Text = "";
        progress.Visibility = progressLabel.Visibility = Visibility.Collapsed;
        update = await manager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(30), cancellation.Token);
        if (closed) return;
        if (update == null)
        {
            versions.Text = $"Installed: v{manager.CurrentVersion} · Up to date";
            status.Text = "You're running the latest stable Lockvyn release.";
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
        status.Text = "Ready to download. Nothing is downloaded until you choose Download update.";
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
