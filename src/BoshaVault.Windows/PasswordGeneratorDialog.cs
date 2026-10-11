using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BoshaVault.Core;

namespace BoshaVault.Windows;

public static class PasswordGeneratorDialog
{
    public static string? Show(Window owner, PasswordOptions defaults, Action<PasswordOptions>? saveDefaults,
        Action<string> copy, bool choose = false)
    {
        var d = new FormDialog(owner, "Generate password", 510);
        d.Label("Generated on this device. The initial default is 24 characters. Use a unique password for every website.");
        var result = d.Field("Generated password", "", 128); result.IsReadOnly = true; result.FontFamily = new("Consolas");
        var length = d.Field("Length (8–128; 16 or more recommended)", defaults.Length.ToString(), 3);
        CheckBox Group(string name, bool enabled)
        {
            var check = new CheckBox { Content = name, IsChecked = enabled, Margin = new(0, 5, 0, 5) };
            d.Body.Children.Add(check); return check;
        }
        var lower = Group("Lowercase letters (a–z)", defaults.Lowercase);
        var upper = Group("Uppercase letters (A–Z)", defaults.Uppercase);
        var digits = Group("Digits (0–9)", defaults.Digits);
        var symbols = Group("Symbols", defaults.Symbols);
        var ambiguous = Group("Exclude ambiguous characters (0, O, 1, I, l)", defaults.ExcludeAmbiguous);
        var allowed = d.Field("Allowed symbols (only these will be used)", defaults.AllowedSymbols, 32);
        var minDigits = d.Field("Minimum digits when enabled", defaults.MinimumDigits.ToString(), 3);
        var minSymbols = d.Field("Minimum symbols when enabled", defaults.MinimumSymbols.ToString(), 3);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
        d.Body.Children.Add(status); string? chosen = null;
        PasswordOptions Read()
        {
            if (!int.TryParse(length.Text, out int count) || !int.TryParse(minDigits.Text, out int nd) || !int.TryParse(minSymbols.Text, out int ns))
                throw new VaultException("Enter whole numbers for length and minimum counts.");
            var options = new PasswordOptions { Length = count, Lowercase = lower.IsChecked == true,
                Uppercase = upper.IsChecked == true, Digits = digits.IsChecked == true, Symbols = symbols.IsChecked == true,
                ExcludeAmbiguous = ambiguous.IsChecked == true, AllowedSymbols = allowed.Text, MinimumDigits = nd, MinimumSymbols = ns };
            PasswordGenerator.Validate(options); return options;
        }
        void Guard(Action action)
        {
            try { action(); status.Text = ""; }
            catch (VaultException ex) { status.Text = ex.Message; }
        }
        void Generate() { result.Text = PasswordGenerator.Generate(Read()); }
        foreach (var field in new[] { length, allowed, minDigits, minSymbols }) field.TextChanged += (_, _) => result.Clear();
        foreach (var check in new[] { lower, upper, digits, symbols, ambiguous })
        { check.Checked += (_, _) => result.Clear(); check.Unchecked += (_, _) => result.Clear(); }
        d.Action("Generate again", () => Guard(Generate));
        d.Action(choose ? "Use this password" : "Copy password", () => Guard(() =>
        {
            if (result.Text.Length == 0) Generate();
            if (choose) { chosen = result.Text; d.DialogResult = true; } else copy(result.Text);
        }), true);
        if (saveDefaults != null) d.Action("Save these rules as my default", () =>
        {
            Guard(() => saveDefaults(Read())); if (status.Text.Length == 0) status.Text = "Default rules saved on this device.";
        });
        Guard(Generate); d.ShowDialog(); result.Clear(); return chosen;
    }
}
