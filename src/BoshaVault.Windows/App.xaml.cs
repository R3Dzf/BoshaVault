using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace BoshaVault.Windows;

public partial class App : Application
{
    private Mutex? single;
    private bool ownsMutex;
    private InstanceActivationChannel? activation;
    private BrowserAutofillServer? autofill;
    protected override void OnStartup(StartupEventArgs e)
    {
        bool background = e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        string identity = (WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName) + "|" + Process.GetCurrentProcess().SessionId;
        string pipe = "BoshaVault-Open-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        single = new Mutex(true, "Local\\BoshaVault-" + Environment.UserName, out bool created); ownsMutex = created;
        if (!created)
        {
            if (!background && !InstanceActivationChannel.RequestOpen(pipe, pid => AllowSetForegroundWindow(pid)).GetAwaiter().GetResult())
                MessageBox.Show("BoshaVault is already running. Use its tray icon or Ctrl + Alt + P. If you are updating an older version, close that version first.", "BoshaVault");
            Shutdown(); return;
        }
        DispatcherUnhandledException += (_, args) =>
        { MessageBox.Show("The action could not be completed. Your encrypted file is preserved. Reopen the vault and retry.", "BoshaVault"); args.Handled = true; };
        base.OnStartup(e);
        try
        {
            var window = new MainWindow(); MainWindow = window;
            activation = new InstanceActivationChannel(pipe, () => { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => window.BringToFront()); });
            autofill = new BrowserAutofillServer(Dispatcher, window.HandleBrowserAutofill);
            window.InitializeLaunch(background);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { MessageBox.Show("BoshaVault could not start. Check access to your user data folder and reopen the app.", "BoshaVault"); Shutdown(); }
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    { (MainWindow as MainWindow)?.PrepareToExit(); base.OnSessionEnding(e); }
    protected override void OnExit(ExitEventArgs e)
    {
        autofill?.Dispose(); activation?.Dispose(); (MainWindow as MainWindow)?.ReleaseDesktopResources();
        if (ownsMutex) { try { single?.ReleaseMutex(); } catch (ApplicationException) { } }
        single?.Dispose(); base.OnExit(e);
    }
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}
