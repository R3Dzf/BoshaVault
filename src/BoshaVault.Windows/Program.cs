using Velopack;

namespace BoshaVault.Windows;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Installer/update hooks must finish before WPF, vault files or the mutex exist.
        // Applying an update is an explicit UI action after the vault has been locked.
        VelopackApp.Build().SetArgs(args).SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(_ => DesktopInstallHooks.RepairExisting())
            .OnAfterUpdateFastCallback(_ => DesktopInstallHooks.RepairExisting())
            .Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
