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
            var matches = activeSession.Data.Entries
                .Where(e => !e.Deleted && e.Password.Length != 0 && e.Url.Length > 0 &&
                    OriginPolicy.Matches(e.Url, request.Origin))
                .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
            if (request.Op == "list")
                return new() { Status = "ok", Accounts = matches.Select(e =>
                    new BrowserAccount { Id = e.Id, Title = e.Title, Username = e.Username }).ToList() };
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
        catch (Exception ex) when (ex is VaultException or InvalidOperationException or ArgumentException)
        {
            return new() { Status = "denied", Message = "BoshaVault refused an unsafe browser request." };
        }
    }
}
