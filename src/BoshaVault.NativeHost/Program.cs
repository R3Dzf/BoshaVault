using System.IO.Pipes;
using BoshaVault.Core;

// Chromium's Native Messaging uses binary length-prefix frames on stdin/stdout.
// No log messages or exception text may ever go to stdout.
BrowserAutofillResponse answer;
try
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    if (args.Length == 0 || !args[0].StartsWith("chrome-extension://", StringComparison.Ordinal) ||
        !Uri.TryCreate(args[0], UriKind.Absolute, out var caller) ||
        caller.Scheme != "chrome-extension" || caller.AbsolutePath != "/" ||
        caller.Host.Length != 32 || caller.Host.Any(c => c is < 'a' or > 'p'))
        throw new InvalidDataException("Browser extension identity missing.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(48));
    var request = await BrowserAutofillProtocol.ReadAsync<BrowserAutofillRequest>(Console.OpenStandardInput(), timeout.Token);
    if (request.Op == "save") timeout.CancelAfter(TimeSpan.FromSeconds(130));
    BrowserAutofillProtocol.Validate(request);
    using var pipe = new NamedPipeClientStream(".", BrowserAutofillProtocol.PipeName(),
        PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    await pipe.ConnectAsync(connect.Token);
    await BrowserAutofillProtocol.WriteAsync(pipe, request, timeout.Token);
    answer = await BrowserAutofillProtocol.ReadAsync<BrowserAutofillResponse>(pipe, timeout.Token);
}
catch (Exception)
{
    // Deliberately no exception detail: browser UI gets a safe bounded status.
    answer = new BrowserAutofillResponse { Status = "unavailable",
        Message = "BoshaVault Windows is not running, or its browser integration is unavailable." };
}
try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    await BrowserAutofillProtocol.WriteAsync(Console.OpenStandardOutput(), answer, timeout.Token);
}
catch { /* Chromium owns the pipe and may close it during navigation. */ }
