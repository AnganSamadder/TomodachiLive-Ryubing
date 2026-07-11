using Ryujinx.Input.Tomodachi;
using Ryujinx.Input.Tomodachi.Ipc;

if (!TomodachiPipeOptions.TryLoadFromProcess(out TomodachiPipeOptions options, out _))
{
    return 2;
}

using TomodachiInputState state = new();
await using TomodachiPipeServer server = TomodachiPipeServer.Start(options, state);
using CancellationTokenSource pollingCancellation = new();
Task polling = Task.Run(async () =>
{
    while (!pollingCancellation.IsCancellationRequested)
    {
        state.PollMappedSnapshot();
        await Task.Delay(5, pollingCancellation.Token).ConfigureAwait(false);
    }
});

Console.WriteLine("READY");
string command = await Console.In.ReadLineAsync().ConfigureAwait(false);
pollingCancellation.Cancel();
try
{
    await polling.ConfigureAwait(false);
}
catch (OperationCanceledException)
{
}

return string.Equals(command, "STOP", StringComparison.Ordinal) ? 0 : 3;
