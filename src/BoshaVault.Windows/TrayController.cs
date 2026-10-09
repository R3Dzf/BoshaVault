using System.IO;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace BoshaVault.Windows;

internal sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Drawing.Icon artwork;
    private readonly Forms.ToolStripMenuItem state, lockItem, startupItem;
    private readonly Dispatcher dispatcher;
    private readonly Func<bool> unlocked, startupEnabled;
    private bool disposed;

    public TrayController(Dispatcher ui, Action open, Action lockVault, Action exit, Func<bool> isUnlocked, Func<bool> startsWithWindows, Action<bool> setStartup)
    {
        dispatcher = ui; unlocked = isUnlocked; startupEnabled = startsWithWindows;
        using var stream = typeof(TrayController).Assembly.GetManifestResourceStream("BoshaVault.TrayIcon") ?? throw new IOException("Tray artwork is unavailable.");
        using var original = new Drawing.Icon(stream, new Drawing.Size(32, 32)); artwork = (Drawing.Icon)original.Clone();
        menu = new Forms.ContextMenuStrip();
        state = new Forms.ToolStripMenuItem("Vault locked") { Enabled = false }; menu.Items.Add(state); menu.Items.Add(new Forms.ToolStripSeparator());
        var openItem = new Forms.ToolStripMenuItem("Open BoshaVault") { Font = new Drawing.Font(menu.Font, Drawing.FontStyle.Bold) };
        openItem.Click += (_, _) => Queue(open); menu.Items.Add(openItem);
        lockItem = new Forms.ToolStripMenuItem("Lock vault"); lockItem.Click += (_, _) => Queue(lockVault); menu.Items.Add(lockItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        startupItem = new Forms.ToolStripMenuItem("Start with Windows"); startupItem.Click += (_, _) => Queue(() => setStartup(!startupEnabled())); menu.Items.Add(startupItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        var exitItem = new Forms.ToolStripMenuItem("Exit BoshaVault"); exitItem.Click += (_, _) => Queue(exit); menu.Items.Add(exitItem);
        icon = new Forms.NotifyIcon { Icon = artwork, Text = "BoshaVault · Vault locked", ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Queue(open); };
        icon.BalloonTipClicked += (_, _) => Queue(open);
        menu.Opening += (_, _) => Update(); Update();
    }
    private void Queue(Action action) => dispatcher.BeginInvoke(() => { if (!disposed) action(); });
    public void Update()
    {
        if (disposed) return;
        bool isOpen = unlocked(); state.Text = isOpen ? "Vault unlocked" : "Vault locked";
        lockItem.Enabled = isOpen; startupItem.Checked = startupEnabled(); icon.Text = "BoshaVault · " + state.Text;
    }
    public void ShowCloseHint()
    {
        if (!disposed) icon.ShowBalloonTip(3500, "BoshaVault is in the tray", "Your vault is locked. Click the B icon or press Ctrl + Alt + P to open it. Right-click for Exit.", Forms.ToolTipIcon.Info);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; icon.Visible = false; icon.Dispose(); menu.Dispose(); artwork.Dispose();
    }
}
