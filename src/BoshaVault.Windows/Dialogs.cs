using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BoshaVault.Core;

namespace BoshaVault.Windows;

public sealed class FormDialog : Window
{
    public readonly StackPanel Body = new() { Margin = new Thickness(26) };
    public FormDialog(Window owner, string title, double width = 480)
    {
        Owner = owner; Title = title; Width = width; SizeToContent = SizeToContent.Height;
        MaxHeight = 760; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(248, 247, 253));
        Body.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.Bold, Margin = new(0, 0, 0, 22) });
        Content = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        PreviewKeyDown += (_, _) => (owner as MainWindow)?.Touch();
        PreviewMouseDown += (_, _) => (owner as MainWindow)?.Touch();
    }
    public TextBox Field(string label, string initial = "", int max = 2000, bool multi = false)
    {
        Label(label);
        var box = new TextBox { Text = initial, MaxLength = max, Margin = new(0, 0, 0, 13), AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multi ? 80 : 42 };
        Body.Children.Add(box); return box;
    }
    public PasswordBox Secret(string label)
    {
        Label(label); var p = new PasswordBox { MaxLength = 1024, Margin = new(0, 0, 0, 15) }; Body.Children.Add(p); return p;
    }
    public void Label(string text) => Body.Children.Add(new TextBlock { Text = text, Foreground = Brushes.Gray, FontSize = 12, Margin = new(0, 0, 0, 7), TextWrapping = TextWrapping.Wrap });
    public Button Action(string text, Action click, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new(0, 9, 0, 0) };
        if (primary) button.Style = (Style)FindResource("Primary");
        button.Click += (_, _) => click(); Body.Children.Add(button); return button;
    }
    public static VaultEntry? Edit(Window owner, VaultEntry? original)
    {
        var dialog = new FormDialog(owner, original == null ? "Add a new login" : "Edit login", 490);
        var entry = original?.Clone() ?? new VaultEntry();
        var title = dialog.Field("Name", entry.Title, 200);
        var username = dialog.Field("Username / email", entry.Username);
        dialog.Label("Password");
        var password = new PasswordBox { Password = entry.Password, MaxLength = 4096, Margin = new(0, 0, 0, 6) };
        dialog.Body.Children.Add(password);
        dialog.Action("Generate a strong password", () => password.Password = PasswordGenerator.Generate());
        dialog.Body.Children.Add(new TextBlock { Text = "Generated passwords contain 24 random characters.", Foreground = Brushes.Gray, FontSize = 10, Margin = new(0, 7, 0, 14) });
        var url = dialog.Field("Website (https://example.com)", entry.Url, 2048);
        var folder = dialog.Field("Folder", entry.Folder, 100);
        var notes = dialog.Field("Private notes", entry.Notes, 32000, true);
        var favorite = new CheckBox { Content = "Add to favorites", IsChecked = entry.Favorite }; dialog.Body.Children.Add(favorite);
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, FontSize = 12 }; dialog.Body.Children.Add(error);
        dialog.Action("Save login", () =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(title.Text)) throw new VaultException("Give this login a name.");
                if (!string.IsNullOrWhiteSpace(url.Text)) OriginPolicy.ExactHost(url.Text.Trim());
                entry.Title = title.Text.Trim(); entry.Username = username.Text; entry.Password = password.Password;
                entry.Url = url.Text.Trim(); entry.Folder = folder.Text.Trim(); entry.Notes = notes.Text; entry.Favorite = favorite.IsChecked == true; entry.Deleted = false;
                dialog.DialogResult = true;
            }
            catch (VaultException ex) { error.Text = ex.Message; }
        }, true);
        bool saved = dialog.ShowDialog() == true;
        password.Clear(); username.Clear(); notes.Clear();
        return saved ? entry : null;
    }
}
