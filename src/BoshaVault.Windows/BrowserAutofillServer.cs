using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using BoshaVault.Core;
using System.Windows.Threading;

namespace BoshaVault.Windows;

// Current-user-only IPC. Sensitive replies require a human approval on the WPF UI.
internal sealed class BrowserAutofillServer : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Dispatcher dispatcher;
    private readonly Func<BrowserAutofillRequest, BrowserAutofillResponse> handler;
    private readonly Task listener;
    private bool disposed;
    public BrowserAutofillServer(Dispatcher ui, Func<BrowserAutofillRequest, BrowserAutofillResponse> handle)
    {
        dispatcher = ui; handler = handle; listener = ListenAsync();
    }
    private async Task ListenAsync()
    {
        CancellationToken ct = stop.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(BrowserAutofillProtocol.PipeName(),
                        PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(45));
                    var request = await BrowserAutofillProtocol.ReadAsync<BrowserAutofillRequest>(pipe, timeout.Token).ConfigureAwait(false);
                    if (request.Op is "save" or "capture" or "update") timeout.CancelAfter(TimeSpan.FromSeconds(120));
                    BrowserAutofillResponse response = await dispatcher.InvokeAsync(() => handler(request),
                        DispatcherPriority.Normal, timeout.Token).Task.ConfigureAwait(false);
                    await BrowserAutofillProtocol.WriteAsync(pipe, response, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                    or OperationCanceledException or SocketException or VaultException or System.Text.Json.JsonException)
                {
                    if (!ct.IsCancellationRequested) await Task.Delay(60, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; stop.Cancel();
    }
}
