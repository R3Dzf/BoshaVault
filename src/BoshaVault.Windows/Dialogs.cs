using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BoshaVault.Core;

namespace BoshaVault.Windows;

/// <summary>Screen-sized dialog with a permanently visible action bar.</summary>
public sealed class FormDialog : Window
{
    public readonly StackPanel Body = new() { Margin = new Thickness(20, 16, 20, 12) };
    private readonly StackPanel footer = new() { Orientation = Orientation.Vertical,
        Margin = new Thickness(20, 5, 20, 15) };
    public FormDialog(Window owner, string title, double width = 480)
    {
        Owner = owner;
        Title = title;
        Width = Math.Min(width, Math.Max(350, SystemParameters.WorkArea.Width - 40));
        Height = Math.Max(320, Math.Min(685, SystemParameters.WorkArea.Height - 70));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = 350;
        MinHeight = 300;
        Background = new SolidColorBrush(Color.FromRgb(248, 247, 253));
        var layout = new DockPanel();
        var footerHost = new Border { Child = footer, Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(231, 228, 239)),
            BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(footerHost, Dock.Bottom);
        layout.Children.Add(footerHost);
        var scroller = new ScrollViewer { Content = Body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        layout.Children.Add(scroller);
        Content = layout;
        Body.Children.Add(new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        PreviewKeyDown += (_, _) => (owner as MainWindow)?.Touch();
        PreviewMouseDown += (_, _) => (owner as MainWindow)?.Touch();
    }
    public TextBox Field(string label, string initial = "", int max = 2000, bool multi = false)
    {
        Label(label);
        var box = new TextBox { Text = initial, MaxLength = max,
            Margin = new Thickness(0, 0, 0, 8), MinHeight = 0,
            Height = multi ? 76 : 38, FontSize = 13,
            Padding = new Thickness(10, multi ? 9 : 7, 10, multi ? 9 : 7),
            AcceptsReturn = multi,
            VerticalContentAlignment = multi ? VerticalAlignment.Top : VerticalAlignment.Center,
            TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap };
        Body.Children.Add(box); return box;
    }
    public PasswordBox Secret(string label)
    {
        Label(label);
        var p = new PasswordBox { MaxLength = 1024,
            Height = 38, MinHeight = 0, FontSize = 13,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 8) };
        Body.Children.Add(p); return p;
    }
    public void Label(string text) => Body.Children.Add(new TextBlock { Text = text,
        Foreground = Brushes.Gray, FontSize = 12,
        Margin = new Thickness(0, 3, 0, 5), TextWrapping = TextWrapping.Wrap });
    public Button Action(string text, Action click, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 5, 0, 0),
            MinHeight = 37, Padding = new Thickness(10, 7, 10, 7) };
        if (primary) button.Style = (Style)FindResource("Primary");
        button.Click += (_, _) => click();
        footer.Children.Add(button); return button;
    }
    public static VaultEntry? Edit(Window owner, VaultEntry? original,
        string suggestedUrl = "", string suggestedUsername = "")
    {
        var dialog = new FormDialog(owner, original == null ? "Add a new login" : "Edit login", 495);
        var entry = original?.Clone() ?? new VaultEntry();

        dialog.Label("Website and account");
        var url = dialog.Field("Website (HTTPS)", entry.Url.Length>0 ? entry.Url : suggestedUrl, 2048);
        var title = dialog.Field("Name", entry.Title, 200);
        var username = dialog.Field("Username / email",
            entry.Username.Length>0 ? entry.Username : suggestedUsername);
        string lastAutomaticName = "";
        string Normalize(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.Length == 0) return "";
            if (!trimmed.Contains("://", StringComparison.Ordinal))
                trimmed = "https://" + trimmed;
            var host = OriginPolicy.ExactHost(trimmed);
            return "https://" + host + "/";
        }
        void SuggestName()
        {
            string normalized;
            try { normalized = Normalize(url.Text); }
            catch (VaultException) { return; }
            if(normalized.Length==0 || (title.Text.Length!=0 && title.Text!=lastAutomaticName))return;
            string domain = OriginPolicy.ExactHost(normalized);
            string suggestion = domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? domain[4..] : domain;
            if (title.Text == suggestion) return;
            lastAutomaticName=suggestion;
            title.Text=suggestion;
        }
        url.TextChanged += (_, _) => SuggestName();
        SuggestName();
        var siteButtons = new StackPanel { Orientation=Orientation.Horizontal,
            Margin=new Thickness(0,0,0,8) };
        var pasteSite = new Button { Content="Paste website URL",
            Padding=new Thickness(10,6,10,6), Margin=new Thickness(0,0,6,0) };
        pasteSite.Click += (_, _) => {
            try {
                if (!Clipboard.ContainsText()) return;
                string clipboard = Clipboard.GetText();
                string normalized = Normalize(clipboard);
                if(normalized.Length>0){url.Text=normalized;SuggestName();}
                else MessageBox.Show(dialog,"Clipboard does not contain an HTTPS website.",
                    "BoshaVault",MessageBoxButton.OK,MessageBoxImage.Information);
            }
            catch(Exception ex) when(ex is VaultException or System.Runtime.InteropServices.ExternalException)
            { MessageBox.Show(dialog,"Clipboard does not contain a usable HTTPS website URL.",
                "BoshaVault",MessageBoxButton.OK,MessageBoxImage.Warning); }
        };
        siteButtons.Children.Add(pasteSite);
        dialog.Body.Children.Add(siteButtons);

        dialog.Label("Password");
        var password = new PasswordBox { Password = entry.Password, MaxLength=4096,
            Height=38, MinHeight=0, FontSize=13, Padding=new Thickness(10,7,10,7),
            Margin=new Thickness(0,0,0,6) };
        dialog.Body.Children.Add(password);
        var helpers = new Grid { Margin=new Thickness(0,0,0,8) };
        helpers.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        helpers.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var generate = new Button { Content="Generate strong password",
            Padding=new Thickness(10,7,10,7), Margin=new Thickness(0,0,6,0) };
        var copy = new Button { Content="Copy password", Padding=new Thickness(10,7,10,7) };
        Grid.SetColumn(generate,0);
        Grid.SetColumn(copy,1);
        helpers.Children.Add(generate);helpers.Children.Add(copy);
        dialog.Body.Children.Add(helpers);
        var helperStatus = new TextBlock { FontSize=11,
            Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,0,0,7),
            Text="Generate a unique 24-character password, then Copy if needed (clipboard clears after 20 seconds)." };
        dialog.Body.Children.Add(helperStatus);
        generate.Click += (_, _) => {
            password.Password=PasswordGenerator.Generate();
            helperStatus.Text="New strong password generated. Copy or save it to the encrypted vault.";
        };
        copy.Click += (_, _) => {
            if(password.Password.Length==0){helperStatus.Text="Enter or generate a password first.";return;}
            PrivateClipboard.Copy(password.Password);
            helperStatus.Text="Password copied. Clipboard will be cleared automatically after 20 seconds.";
        };
        var folder = dialog.Field("Folder", entry.Folder, 100);
        var notes = dialog.Field("Private notes", entry.Notes, 32000, true);
        var favorite = new CheckBox { Content = "Add to favorites", IsChecked = entry.Favorite,
            Margin=new Thickness(0,4,0,5) };
        dialog.Body.Children.Add(favorite);
        var error = new TextBlock { Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        dialog.Body.Children.Add(error);
        dialog.Action("Save login", () => {
            try {
                string normalized = Normalize(url.Text);
                if (title.Text.Trim().Length==0)
                    throw new VaultException("Enter a name or a website to generate a name.");
                entry.Title = title.Text.Trim();
                entry.Username = username.Text;
                entry.Password = password.Password;
                entry.Url = normalized;
                entry.Folder=folder.Text.Trim();
                entry.Notes=notes.Text;
                entry.Favorite=favorite.IsChecked==true;
                entry.Deleted=false;
                dialog.DialogResult=true;
            }
            catch(VaultException ex){error.Text=ex.Message;}
        }, true);
        dialog.Action("Cancel",()=>dialog.DialogResult=false);
        bool saved=dialog.ShowDialog()==true;
        password.Clear();username.Clear();notes.Clear();
        return saved?entry:null;
    }
}
