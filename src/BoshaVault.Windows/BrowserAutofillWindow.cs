using System.Windows;
using BoshaVault.Core;

namespace BoshaVault.Windows;

public partial class MainWindow
{
    // Never release a password merely because an IPC client requests it.
    // Any process in the same Windows account may open a CurrentUserOnly pipe,
    // so every credential disclosure requires explicit approval in our WPF UI.
    internal BrowserAutofillResponse HandleBrowserAutofill(BrowserAutofillRequest request)
    {
        try
        {
            if (exiting || resourcesReleased) return new() { Status = "unavailable" };
            string host = BrowserAutofillProtocol.Validate(request);
            if (!preferences.BrowserAutofillEnabled)
                return new() { Status = "denied", Message = "Browser Autofill is disabled in BoshaVault settings." };
            if (request.Op == "open")
            {
                BringToFront();
                return new() { Status = session == null ? "locked" : "ok" };
            }
            var activeSession = session;
            if (activeSession == null || !activeSession.IsOpen)
                return new() { Status = "locked", Message = "Unlock BoshaVault on Windows first." };
            if (request.Op == "save")
            {
                // The extension never persists generated passwords. This dialog
                // is the mandatory review/consent boundary before storing one.
                int saveStamp = generation;
                BringToFront();
                var dialog = new FormDialog(this, "Save generated login", 510);
                dialog.Label("Browser-reported HTTPS site: https://" + host + "\nThis creates a new entry; it does not register an account at the site.");
                var title = dialog.Field("Login name", host, 200);
                var user = dialog.Field("Username / email from signup form (review or edit)", request.Username, 2000);
                dialog.Label("Generated password: " + request.Password.Length +
                    " characters (hidden). Only press Save after checking the browser address bar and username.");
                var warning = new System.Windows.Controls.TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = System.Windows.Media.Brushes.IndianRed,
                    FontSize = 12
                };
                dialog.Body.Children.Add(warning);
                string chosenTitle = "", chosenUser = "";
                dialog.Action("Save encrypted login", () =>
                {
                    chosenTitle = title.Text.Trim();
                    chosenUser = user.Text.Trim();
                    if (chosenTitle.Length == 0 || chosenUser.Length == 0)
                    {
                        warning.Text = "Enter a login name and a username / email before saving.";
                        return;
                    }
                    if (chosenUser.IndexOfAny(['\r','\n','\0']) >= 0)
                    {
                        warning.Text = "Invalid username.";
                        return;
                    }
                    bool duplicate = activeSession.Data.Entries.Any(e => !e.Deleted &&
                        string.Equals(e.Username, chosenUser, StringComparison.OrdinalIgnoreCase) &&
                        e.Url.Length > 0 && OriginPolicy.Matches(e.Url, request.Origin));
                    if (duplicate)
                    {
                        warning.Text = "A login with this username already exists for this website. Edit it manually rather than creating a duplicate.";
                        return;
                    }
                    dialog.DialogResult = true;
                }, true);
                dialog.Action("Cancel", () => dialog.DialogResult = false);
                bool approved = dialog.ShowDialog() == true;
                user.Clear();title.Clear();
                if (!approved || generation != saveStamp || session != activeSession || !activeSession.IsOpen)
                    return new() { Status = "denied", Message = "Saving was canceled. The website form still contains the generated password." };
                var item = new VaultEntry {
                    Title = chosenTitle, Username = chosenUser, Password = request.Password,
                    Url = "https://" + host + "/", Folder = "Personal"
                };
                activeSession.Upsert(item);
                selected = item;
                Refresh();
                lastActivity = DateTime.UtcNow;
                return new() { Status = "saved", Message = "Saved to your encrypted BoshaVault vault. Complete signup on the website separately." };
            }
            var matches = activeSession.Data.Entries
                .Where(e => !e.Deleted && e.Password.Length != 0 && e.Url.Length > 0 &&
                    OriginPolicy.Matches(e.Url, request.Origin))
                .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
            if (request.Op == "list")
            {
                var hosts=activeSession.Data.Entries.Where(e=>!e.Deleted && e.Url.Length>0)
                    .Select(e=>{
                        try { return OriginPolicy.ExactHost(e.Url); }
                        catch(VaultException) { return ""; }
                    }).Where(x=>x.Length>0).Distinct(StringComparer.OrdinalIgnoreCase);
                bool suspicious=matches.Length==0 && OriginPolicy.LooksLikeSavedHostname(host,hosts);
                return new() {
                    Status="ok",
                    Warning=suspicious
                        ? "Caution: this domain may resemble another saved login, or use international characters. Verify the exact address before creating an account."
                        : "",
                    Accounts=matches.Select(e=>new BrowserAccount {
                        Id=e.Id,Title=e.Title,Username=e.Username }).ToList()
                };
            }
            var target = matches.FirstOrDefault(e => string.Equals(e.Id, request.EntryId, StringComparison.Ordinal));
            if (target == null) return new() { Status = "denied", Message = "No exact website match." };

            // Foreground notice must name the destination and account.
            int stamp = generation;
            BringToFront();
            bool allowed = MessageBox.Show(this,
                "Allow this one-time fill?\n\nWebsite (exact HTTPS host): " + host +
                "\nSaved login: " + target.Title +
                "\nUsername: " + target.Username +
                "\n\nOnly approve if you are currently signing in at this website.\n" +
                "BoshaVault will release the selected username and password to your browser.",
                "BoshaVault · Confirm browser Autofill", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
            if (!allowed || generation != stamp || session != activeSession || !activeSession.IsOpen)
                return new() { Status = "denied", Message = "Browser fill canceled." };
            if (!OriginPolicy.Matches(target.Url, request.Origin))
                return new() { Status = "denied", Message = "Origin changed." };
            lastActivity = DateTime.UtcNow;
            return new() { Status = "filled", Username = target.Username, Password = target.Password };
        }
        catch (Exception ex) when (ex is VaultException or InvalidOperationException or ArgumentException or System.IO.IOException)
        {
            return new() { Status = "denied", Message = "BoshaVault refused an unsafe browser request." };
        }
    }
}
