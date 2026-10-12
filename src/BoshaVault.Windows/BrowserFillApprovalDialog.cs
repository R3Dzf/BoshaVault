using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BoshaVault.Windows;

/// <summary>
/// Minimal, browser-adjacent approval instead of stealing focus by restoring
/// the entire password manager. No password is ever displayed here.
/// BrowserFillApprovalDialog is intentionally NOT owned by MainWindow:
/// restoring the main vault would interrupt the active browser login.
/// </summary>
internal sealed class BrowserFillApprovalDialog : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    internal static bool Confirm(string currentHost, string savedHost,
        string title, string username, bool related)
    {
        var prompt = new BrowserFillApprovalDialog(currentHost, savedHost, title, username, related);
        try { return prompt.ShowDialog() == true; }
        finally { prompt.Close(); }
    }

    private readonly DispatcherTimer timeout;
    private BrowserFillApprovalDialog(string currentHost, string savedHost,
        string title, string username, bool related)
    {
        Title = "Lockvyn · Confirm fill";
        Width = Math.Min(440, SystemParameters.WorkArea.Width - 24);
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(340, SystemParameters.WorkArea.Height - 24);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = true;

        var ink = new SolidColorBrush(Color.FromRgb(23, 27, 46));
        var muted = new SolidColorBrush(Color.FromRgb(105, 113, 133));
        var border = new Border
        {
            Padding = new Thickness(23, 20, 23, 20),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(225, 229, 239)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(17),
        };
        var body = new StackPanel();
        border.Child = body;
        Content = border;

        body.Children.Add(new TextBlock { Text = related ? "Related website · extra caution" : "Confirm browser Autofill",
            FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = ink, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock
        {
            Text = "Only approve if the address in your browser is the one you intended.",
            FontSize = 12, Foreground = muted, Margin = new Thickness(0, 7, 0, 18),
            TextWrapping = TextWrapping.Wrap
        });
        AddDetail(body, "CURRENT BROWSER SITE", "https://" + currentHost, ink, muted);
        AddDetail(body, "SAVED LOGIN SITE", "https://" + savedHost, ink, muted);
        AddDetail(body, "ACCOUNT", title + " · " + username, ink, muted);
        if (related)
            body.Children.Add(new TextBlock
            {
                Text = "Warning: The saved and current domains are different. Check both before sending a password.",
                Foreground = new SolidColorBrush(Color.FromRgb(158, 75, 30)),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12, Margin = new Thickness(0, 8, 0, 15)
            });
        var buttons = new Grid { Margin = new Thickness(0, 14, 0, 10) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cancel = new Button { Content = "Cancel", MinHeight = 44, Margin = new Thickness(0, 0, 6, 0),
            IsCancel = true };
        var fill = new Button { Content = "Fill this login", MinHeight = 44, Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)Application.Current.FindResource("Primary") };
        // No default 'Enter to approve'. A deliberate selection is required.
        cancel.Click += (_, _) => DialogResult = false;
        fill.Click += (_, _) => DialogResult = true;
        Grid.SetColumn(fill, 1);
        buttons.Children.Add(cancel);
        buttons.Children.Add(fill);
        body.Children.Add(buttons);
        var countdown = new TextBlock { FontSize = 11, Foreground = muted,
            Text = "One-time request · closes in 30 seconds",
            HorizontalAlignment = HorizontalAlignment.Center };
        body.Children.Add(countdown);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timeout.Tick += (_, _) =>
        {
            int remaining = (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds);
            if (remaining <= 0)
            {
                timeout.Stop();
                if (IsVisible) DialogResult = false;
            }
            else countdown.Text = "One-time request · closes in " + remaining + " seconds";
        };
        Loaded += (_, _) => { timeout.Start(); Activate(); };
        Closed += (_, _) => timeout.Stop();
        SourceInitialized += (_, _) =>
        {
            try { SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11); }
            catch { /* Best-effort screenshot exclusion; no secret is displayed. */ }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        };
    }

    private static void AddDetail(Panel body, string label, string value, Brush ink, Brush muted)
    {
        body.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = muted, Margin = new Thickness(0, 2, 0, 4) });
        body.Children.Add(new TextBlock
        {
            Text = value.Length > 155 ? value[..152] + "..." : value,
            FontSize = 13, Foreground = ink, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });
    }
}
