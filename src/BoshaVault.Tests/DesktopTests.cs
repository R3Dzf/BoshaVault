using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using BoshaVault.Windows;

public static class DesktopTests
{
    public static async Task Run()
    {
        int passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS " + name); }
        string folder = Path.Combine(Path.GetTempPath(), "boshavault-desktop-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "desktop.json");
            Check(DesktopPreferences.Load(path).AutoLockMinutes == 2, "Missing preferences use secure auto-lock default");
            new DesktopPreferences { AutoLockMinutes = 5, TrayTipShown = true }.Save(path);
            var restored = DesktopPreferences.Load(path);
            Check(restored.AutoLockMinutes == 5 && restored.TrayTipShown, "Desktop preferences survive reopening");
            new DesktopPreferences { AutoLockMinutes = 1 }.Save(path);
            Check(DesktopPreferences.Load(path).AutoLockMinutes == 1 && Directory.GetFiles(folder).Length == 1, "Preference replacement is atomic and leaves no temporary files");
            string original = File.ReadAllText(path); bool refused = false;
            try { new DesktopPreferences { AutoLockMinutes = 0 }.Save(path); } catch (ArgumentException) { refused = true; }
            Check(refused && File.ReadAllText(path) == original, "Invalid auto-lock setting cannot overwrite saved preferences");
            foreach (string bad in new[] { "{bad", "{\"Version\":1,\"AutoLockMinutes\":999}", "{\"Version\":2,\"AutoLockMinutes\":10}", "{\"Version\":1,\"AutoLockMinutes\":10,\"unknown\":true}", new string('x', 4096) })
            { File.WriteAllText(path, bad); Check(DesktopPreferences.Load(path).AutoLockMinutes == 2, "Corrupt/unsupported preferences fall back to default"); }

            if (!OperatingSystem.IsWindows())
            {
                try { using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified); }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
                { Console.WriteLine("SKIP: this environment denies the Unix socket backend used by named pipes. IPC integration checks require another environment."); Console.WriteLine($"RESULT: {passed} desktop preference checks passed; IPC checks not run."); return; }
            }

            string pipe = "boshavault-desktop-test-" + Guid.NewGuid().ToString("N"); int opened = 0;
            using var channel = new InstanceActivationChannel(pipe, () => Interlocked.Increment(ref opened));
            int seenPid = 0;
            Check(await InstanceActivationChannel.RequestOpen(pipe, pid => seenPid = pid), "Existing-instance activation connects");
            await WaitFor(() => Volatile.Read(ref opened) == 1);
            Check(seenPid == Environment.ProcessId && opened == 1, "IPC reports owning process and requests window activation");

            var launch = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) launch.ArgumentList.Add(typeof(DesktopTests).Assembly.Location);
            launch.ArgumentList.Add("--activation-client"); launch.ArgumentList.Add(pipe);
            launch.Environment["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../.."));
            using (var process = Process.Start(launch) ?? throw new Exception("Cannot start activation test client."))
            { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)); Check(process.ExitCode == 0, "A separate process activates the existing instance"); }
            await WaitFor(() => Volatile.Read(ref opened) == 2);

            using (var invalid = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                using var timeout = new CancellationTokenSource(2500); await invalid.ConnectAsync(timeout.Token);
                var owner = new byte[4]; await invalid.ReadExactlyAsync(owner, timeout.Token); await invalid.WriteAsync(new byte[] { 0 }, timeout.Token); await invalid.FlushAsync(timeout.Token);
            }
            Check(await InstanceActivationChannel.RequestOpen(pipe), "Server accepts another launch after a malformed activation");
            await WaitFor(() => Volatile.Read(ref opened) == 3);
            Check(opened == 3, "Malformed activation does not open the window");

            using (var stalled = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                using var timeout = new CancellationTokenSource(2500); await stalled.ConnectAsync(timeout.Token);
                var owner = new byte[4]; await stalled.ReadExactlyAsync(owner, timeout.Token);
                channel.Dispose(); await channel.Completion.WaitAsync(TimeSpan.FromSeconds(2));
                Check(true, "Shutdown is not blocked by a stalled IPC client");
            }
            Check(!await InstanceActivationChannel.RequestOpen(pipe, timeoutMilliseconds: 150), "Launch request to a stopped instance times out cleanly");
            Console.WriteLine($"RESULT: {passed} desktop integration checks passed.");
        }
        finally { Directory.Delete(folder, true); }
    }
    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(2500);
        while (!condition()) await Task.Delay(15, timeout.Token);
    }
}
