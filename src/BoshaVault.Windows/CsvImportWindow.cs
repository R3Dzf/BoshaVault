using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BoshaVault.Core;
using Microsoft.Win32;

namespace BoshaVault.Windows;

public partial class MainWindow
{
    private void ImportBrowserPasswords(Window owner)
    {
        var active = session;
        if (active == null || !active.IsOpen) return;
        var formatDialog = new FormDialog(owner, "Import saved passwords", 530);
        formatDialog.Label("Select the browser export type. Imports stay on this PC; no file is uploaded. Plaintext CSV files remain readable to other programs until securely handled by you.");
        var source = new ComboBox {
            ItemsSource = new[] { "Chrome / Google Password Manager", "Bitwarden (login-only)" },
            SelectedIndex = 0, Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(10)
        };
        formatDialog.Body.Children.Add(source);
        formatDialog.Label("Before importing: export a verified encrypted BoshaVault backup. After a successful import, remove the plaintext CSV through your normal file-management process and review any other copies or backups. Deleting a CSV cannot guarantee secure erasure on an SSD.");
        formatDialog.Action("Choose CSV and preview", () => formatDialog.DialogResult = true, true);
        if (formatDialog.ShowDialog() != true) return;

        var open = new OpenFileDialog {
            Title = "Select your plaintext password CSV",
            Filter = "CSV files (*.csv)|*.csv",
            CheckFileExists = true, Multiselect = false
        };
        if (open.ShowDialog(owner) != true) return;
        byte[] contents = [];
        CredentialCsvImport.Plan? plan = null;
        try
        {
            var info = new FileInfo(open.FileName);
            if (info.Length is < 5 or > CredentialCsvImport.MaxBytes)
                throw new VaultException("Choose a CSV smaller than 2 MB.");
            contents = File.ReadAllBytes(open.FileName);
            plan = CredentialCsvImport.Prepare(contents,
                source.SelectedIndex == 0 ? "chrome" : "bitwarden",
                active.Data.Entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or VaultException)
        {
            MessageBox.Show(owner, ex is VaultException ? ex.Message :
                "Cannot read the CSV file. The vault has not been modified.",
                "BoshaVault import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally { if (contents.Length != 0) CryptographicOperations.ZeroMemory(contents); }

        using (plan)
        {
            var preview = new FormDialog(owner, "Review password import", 560);
            preview.Label($"CSV records: {plan.SourceRows}. Ready: {plan.Entries.Count}. " +
                $"Skipped duplicates: {plan.SkippedDuplicate}. Unsupported/insecure: {plan.SkippedUnsafe}. " +
                $"Non-login records: {plan.SkippedNonLogin}.");
            preview.Label("No passwords will be displayed in this preview. Entries are added atomically, preserving your existing logins and encrypted file. HTTPS hostnames must match exactly for Autofill.");
            foreach (var entry in plan.Entries.Take(20))
            {
                preview.Label(entry.Title + " · " + OriginPolicy.ExactHost(entry.Url) +
                    " · " + entry.Username);
            }
            if (plan.Entries.Count > 20)
                preview.Label($"...and {plan.Entries.Count - 20} more entries.");
            var acknowledge = new CheckBox {
                Content = "I reviewed the import, made an encrypted backup, and understand the source CSV contains plaintext secrets.",
                IsChecked = false
            };
            preview.Body.Children.Add(acknowledge);
            var error = new TextBlock {
                Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 9, 0, 0), FontSize = 12
            };
            preview.Body.Children.Add(error);
            int stamp = generation;
            bool approved = false;
            preview.Action("Import into encrypted vault", () =>
            {
                if (plan.Entries.Count == 0) { error.Text = "No safe new logins in this CSV."; return; }
                if (acknowledge.IsChecked != true)
                { error.Text = "Confirm the safety notice before importing."; return; }
                approved = true;
                preview.DialogResult = true;
            }, true);
            preview.Action("Cancel", () => preview.DialogResult = false);
            if (preview.ShowDialog() != true || !approved) return;
            if (generation != stamp || session != active || !active.IsOpen)
            {
                MessageBox.Show(owner, "Vault locked during import. Unlock and retry.", "BoshaVault");
                return;
            }
            try
            {
                int imported = active.ImportEntries(plan.Entries);
                Refresh();
                lastActivity = DateTime.UtcNow;
                SetStatus($"Imported {imported} encrypted logins. The plaintext CSV remains on disk until you remove it.");
                MessageBox.Show(owner, $"Imported {imported} encrypted logins.\n\n" +
                    "Important: The original plaintext CSV has NOT been deleted. Handle it safely and verify the imported accounts.",
                    "BoshaVault import completed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or VaultException)
            {
                MessageBox.Show(owner, ex is VaultException ? ex.Message :
                    "Import could not be completed; no partial import was committed.",
                    "BoshaVault import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
