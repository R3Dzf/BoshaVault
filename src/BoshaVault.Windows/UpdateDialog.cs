using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Velopack;
using Velopack.Sources;

namespace BoshaVault.Windows;

internal sealed class UpdateDialog : Window
{
    private readonly UpdateManager manager = new(new GithubSource("https://github.com/R3Dzf/BoshaVault", null, false));
    private readonly CancellationTokenSource cancellation = new();
    private readonly TextBlock status;
    private readonly ProgressBar progress;
    private readonly Button check, download, restart;
    private readonly MainWindow owner;
    private UpdateInfo? update;
    private bool working, closed;

    public UpdateDialog(MainWindow owner)
    {
        this.owner = owner;
        Owner = owner; Title = "Lockvyn updates"; Width = 510; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(24) }; Content = body;
        body.Children.Add(new TextBlock { Text = "Keep Lockvyn up to date", FontSize = 23, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = "Updates come from the official Lockvyn GitHub releases. Available changes are downloaded first; a full download is used when needed. Your vault stays in its existing data folder.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 14) });
        status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Foreground = Brushes.DimGray };
        body.Children.Add(status);
        progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12) }; body.Children.Add(progress);
        check = Action("Check for updates", CheckAsync);
        download = Action("Download update", DownloadAsync); download.IsEnabled = false;
        restart = Action("Restart and update", RestartAsync); restart.IsEnabled = false;
        Action("View downloads on GitHub", () => { Process.Start(new ProcessStartInfo("https://github.com/R3Dzf/BoshaVault/releases") { UseShellExecute = true }); return Task.CompletedTask; });
        var close = new Button { Content = "Close / cancel download", Margin = new Thickness(0, 4, 0, 0) };
        close.Click += (_, _) => Close(); body.Children.Add(close);
        status.Text = manager.IsInstalled ? $"Installed version: {manager.CurrentVersion}. Check when you are ready; downloads start only when you choose." : "This copy uses the original ZIP layout. Install Lockvyn-Setup.exe once to enable updates. You can keep your existing encrypted vault.";
        check.IsEnabled = manager.IsInstalled;
        Closed += (_, _) => { closed = true; cancellation.Cancel(); if (!working) cancellation.Dispose(); };
        PreviewKeyDown += (_, _) => owner.Touch(); PreviewMouseDown += (_, _) => owner.Touch();
    }

    private Button Action(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 4, 0, 0) }; ((StackPanel)Content).Children.Add(button);
        button.Click += async (_, _) =>
        {
            if (working) return;
            try { await action(); }
            catch (OperationCanceledException) { if (!closed) status.Text = "Update cancelled. You can retry later."; }
            catch (Exception) { if (!closed) status.Text = "The update could not be completed. Check your connection, free space and GitHub availability, then retry. Your vault is preserved."; }
            finally { working = false; if (closed) cancellation.Dispose(); else { check.IsEnabled = manager.IsInstalled; download.IsEnabled = update != null && !restart.IsEnabled; } }
        };
        return button;
    }

    private void Begin(string message)
    { working = true; check.IsEnabled = download.IsEnabled = restart.IsEnabled = false; status.Text = message; }

    private async Task CheckAsync()
    {
        Begin("Checking official releases…"); update = null;
        update = await manager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(30), cancellation.Token);
        if (closed) return;
        status.Text = update == null ? "You have the latest published version." : $"Version {update.TargetFullRelease.Version} is available. Choose Download update to continue.";
    }

    private async Task DownloadAsync()
    {
        if (update == null) return;
        Begin("Downloading and verifying the update…"); progress.Value = 0; progress.Visibility = Visibility.Visible;
        await manager.DownloadUpdatesAsync(update, value => { if (!closed && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => { if (!closed) progress.Value = value; }); }, cancellation.Token);
        if (closed) return;
        progress.Value = 100; restart.IsEnabled = true;
        status.Text = "Update verified and ready. Restart and update will lock your vault, close Lockvyn and install the new version.";
    }

    private Task RestartAsync()
    {
        if (update == null || manager.UpdatePendingRestart == null) return Task.CompletedTask;
        // Shut down normally so the tray, local connections, file handles and mutex are released.
        // The updater waits for process exit; it does not replace a running vault.
        manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: false, restart: true, restartArgs: []);
        owner.PrepareToExit(); Application.Current.Shutdown();
        return Task.CompletedTask;
    }
}
