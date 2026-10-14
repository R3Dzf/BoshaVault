using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using BoshaVault.Core;

namespace BoshaVault.Windows;

internal static class SupportTicketDialog
{
    private sealed record TicketChoice(string Id, string Subject, string Status, string Priority)
    {
        public override string ToString() => $"{Subject} · {Status} · {Priority}";
    }

    internal static void Show(Window owner, HttpClient http,
        Func<Task<SupabaseCloudAccount.Session>> refreshSession,
        Func<bool> current, CancellationToken cancellationToken)
    {
        var d = new FormDialog(owner, "Support tickets", 620);
        d.Label("Create a request, review replies, and track status. Tickets are private to your signed-in cloud account and support agents.");
        var subject = d.Field("Short title", "", 120);
        var message = d.Field("Describe what you need help with", "", 4000, multi: true);
        var consent = new CheckBox {
            Content = new TextBlock { Text = "Include optional sanitized diagnostics (platform, app version, OS version and error code). No vault contents, account email, device ID, credentials or raw logs.", TextWrapping = TextWrapping.Wrap },
            IsChecked = false, Margin = new Thickness(0, 5, 0, 8)
        };
        d.Body.Children.Add(consent);
        var diagnosticPreview = new TextBlock {
            Text = "Diagnostics are off until you enable them.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        };
        d.Body.Children.Add(diagnosticPreview);
        consent.Checked += (_, _) => diagnosticPreview.Text =
            "Preview: Windows · app " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown") +
            " · OS " + Environment.OSVersion.Version.Major + " · error code MANUAL";
        consent.Unchecked += (_, _) => diagnosticPreview.Text = "Diagnostics are off until you enable them.";

        d.Label("Your requests");
        var tickets = new ComboBox { MinHeight = 38, Margin = new Thickness(0, 0, 0, 8) };
        d.Body.Children.Add(tickets);
        var thread = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        d.Body.Children.Add(thread);
        var reply = d.Field("Reply to the selected request", "", 4000, multi: true);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 6) };
        d.Body.Children.Add(status);
        TicketChoice? Selected() => tickets.SelectedItem as TicketChoice;

        void EnsureCurrent()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!current()) throw new OperationCanceledException();
        }

        async Task<JsonDocument> Send(object payload)
        {
            EnsureCurrent();
            var session = await refreshSession();
            EnsureCurrent();
            return await SupportTicketClient.SendAsync(http, session, payload, cancellationToken);
        }

        async Task Run(Func<Task> action)
        {
            try { EnsureCurrent(); status.Text = "Working…"; await action(); }
            catch (OperationCanceledException) { status.Text = "Request cancelled. Your offline vault remains available."; }
            catch (Exception ex) { status.Text = ex is HttpRequestException ? ex.Message : "Support is unavailable. Try again when connected; your vault remains available."; }
        }

        async Task LoadThread()
        {
            var selected = Selected();
            if (selected is null) { thread.Text = ""; return; }
            using var response = await Send(new { action = "ticketGet", ticketId = selected.Id });
            var lines = new StringBuilder();
            if (response.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                foreach (var item in messages.EnumerateArray())
                {
                    string role = item.TryGetProperty("senderRole", out var sender) ? sender.GetString() ?? "support" : "support";
                    string body = item.TryGetProperty("body", out var text) ? text.GetString() ?? "" : "";
                    lines.Append(role == "agent" ? "Support: " : "You: ").AppendLine(body).AppendLine();
                }
            if (response.RootElement.TryGetProperty("diagnostics", out var diagnostics) &&
                diagnostics.ValueKind == JsonValueKind.Object &&
                diagnostics.TryGetProperty("payload", out var details))
                lines.Append("Shared diagnostics (consented): ").Append(details.GetRawText()).AppendLine();
            thread.Text = lines.ToString();
            status.Text = "Ticket " + selected.Id + " · " + selected.Status;
        }

        async Task LoadTickets(string? selectId = null)
        {
            using var response = await Send(new { action = "ticketList" });
            tickets.Items.Clear();
            if (response.RootElement.TryGetProperty("tickets", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray())
                {
                    var choice = new TicketChoice(item.GetProperty("id").GetString() ?? "",
                        item.GetProperty("subject").GetString() ?? "Support request",
                        item.GetProperty("status").GetString() ?? "open",
                        item.GetProperty("priority").GetString() ?? "normal");
                    tickets.Items.Add(choice);
                    if (choice.Id == selectId) tickets.SelectedItem = choice;
                }
            if (tickets.SelectedItem is null && tickets.Items.Count > 0) tickets.SelectedIndex = 0;
            await LoadThread();
            if (tickets.Items.Count == 0) status.Text = "No support requests yet.";
        }

        tickets.SelectionChanged += async (_, _) => await Run(LoadThread);
        d.Action("Refresh requests", async () => await Run(() => LoadTickets(Selected()?.Id)), inBody: true);
        d.Action("Create request", async () => await Run(async () =>
        {
            var diagnostic = consent.IsChecked == true
                ? new { platform = "windows", appVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown",
                    osVersion = "Windows " + Environment.OSVersion.Version.Major, errorCode = "MANUAL" }
                : null;
            using var response = await Send(new { action = "ticketCreate", ticket = new {
                subject = subject.Text, body = message.Text,
                diagnosticConsent = consent.IsChecked == true, diagnostics = diagnostic
            }});
            var id = response.RootElement.GetProperty("ticket").GetProperty("id").GetString();
            subject.Clear(); message.Clear(); consent.IsChecked = false;
            await LoadTickets(id);
        }), primary: true, inBody: true);
        d.Action("Send reply", async () => await Run(async () =>
        {
            var selected = Selected(); if (selected is null) { status.Text = "Select a request first."; return; }
            using var response = await Send(new { action = "ticketReply", ticketId = selected.Id, body = reply.Text });
            reply.Clear(); await LoadTickets(selected.Id);
        }), inBody: true);
        d.Action("Close selected request", async () => await Run(async () =>
        {
            var selected = Selected(); if (selected is null) { status.Text = "Select a request first."; return; }
            using var response = await Send(new { action = "ticketClose", ticketId = selected.Id });
            await LoadTickets(selected.Id);
        }), inBody: true);
        d.Action("Reopen selected request", async () => await Run(async () =>
        {
            var selected = Selected(); if (selected is null) { status.Text = "Select a request first."; return; }
            using var response = await Send(new { action = "ticketReopen", ticketId = selected.Id });
            await LoadTickets(selected.Id);
        }), inBody: true);
        d.Action("Refresh now", async () => await Run(() => LoadTickets(Selected()?.Id)));
        d.Loaded += async (_, _) => await Run(() => LoadTickets());
        d.ShowDialog();
    }
}
