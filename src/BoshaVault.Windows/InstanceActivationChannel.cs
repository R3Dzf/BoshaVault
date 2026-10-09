using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;

namespace BoshaVault.Windows;

/// <summary>Same-user, bounded IPC for opening the existing window. No vault data is sent.</summary>
public sealed class InstanceActivationChannel : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly string name;
    private readonly Action activate;
    private int disposed;
    public Task Completion { get; }
    public InstanceActivationChannel(string pipeName, Action onActivate)
    { name = pipeName; activate = onActivate; Completion = Listen(); }

    private async Task Listen()
    {
        CancellationToken ct = stop.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 16, 16);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    var pid = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(pid, Environment.ProcessId);
                    await server.WriteAsync(pid, timeout.Token).ConfigureAwait(false);
                    var command = new byte[1];
                    int read = await server.ReadAsync(command, timeout.Token).ConfigureAwait(false);
                    if (read == 1 && command[0] == 0x42 && !ct.IsCancellationRequested) activate();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or SocketException)
                { if (!ct.IsCancellationRequested) await Task.Delay(80, ct).ConfigureAwait(false); }
            }
        }
        catch (OperationCanceledException) { }
        finally { stop.Dispose(); }
    }

    public static async Task<bool> RequestOpen(string pipeName, Action<int>? allowForeground = null, int timeoutMilliseconds = 1800)
    {
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            var pid = new byte[4]; await client.ReadExactlyAsync(pid, timeout.Token).ConfigureAwait(false);
            int processId = BinaryPrimitives.ReadInt32LittleEndian(pid); if (processId < 1) return false;
            allowForeground?.Invoke(processId);
            await client.WriteAsync(new byte[] { 0x42 }, timeout.Token).ConfigureAwait(false);
            await client.FlushAsync(timeout.Token).ConfigureAwait(false); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or SocketException) { return false; }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { stop.Cancel(); } catch (ObjectDisposedException) { }
    }
}
