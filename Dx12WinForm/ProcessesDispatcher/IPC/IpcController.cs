using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using RqSimPlatform.Contracts;

namespace Dx12WinForm.ProcessesDispatcher.IPC;

public sealed class IpcController
{
    public async Task<bool> SendCommandAsync(SimCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            using NamedPipeClientStream client = new(
                serverName: ".",
                pipeName: DispatcherConfig.ControlPipeName,
                direction: PipeDirection.Out,
                options: PipeOptions.Asynchronous);

            await client.ConnectAsync(DispatcherConfig.PipeConnectTimeout, cancellationToken).ConfigureAwait(false);

            await using StreamWriter writer = new(client)
            {
                AutoFlush = true
            };

            string json = JsonSerializer.Serialize(command);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException ex)
        {
            Trace.WriteLine($"[IpcController] Pipe connect timeout: {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            Trace.WriteLine($"[IpcController] IO error: {ex.Message}");
            return false;
        }
    }

    public Task<bool> SendStartAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(new SimCommand { Type = SimCommandType.Start }, cancellationToken);
    }

    public Task<bool> SendPauseAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(new SimCommand { Type = SimCommandType.Pause }, cancellationToken);
    }

    public Task<bool> SendStopAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(new SimCommand { Type = SimCommandType.Stop }, cancellationToken);
    }

    public Task<bool> RequestShutdownAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(new SimCommand { Type = SimCommandType.Shutdown }, cancellationToken);
    }

    public Task<bool> SendHandshakeAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(new SimCommand { Type = SimCommandType.Handshake }, cancellationToken);
    }
}
