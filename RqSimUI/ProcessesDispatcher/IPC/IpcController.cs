using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using RqSimPlatform.Contracts;

namespace RqSimForms.ProcessesDispatcher.IPC;

public sealed class IpcController
{
    public Action<string>? Logger { get; set; }

    private void Log(string message)
    {
        Trace.WriteLine(message);
        Logger?.Invoke(message);
    }

    private static readonly JsonSerializerOptions PipeJsonOptions = new(JsonSerializerDefaults.General)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    /// <summary>
    /// Checks if the console pipe server is available by attempting a quick connection.
    /// </summary>
    public static async Task<bool> IsPipeServerAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: DispatcherConfig.ControlPipeName,
                direction: PipeDirection.Out,
                options: PipeOptions.Asynchronous);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(200));

            await client.ConnectAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

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

            // Use longer timeout for regular commands, shorter for handshake
            var timeout = command.Type == SimCommandType.Handshake
                ? TimeSpan.FromMilliseconds(1000)  // 1 second for handshake
                : DispatcherConfig.PipeConnectTimeout;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                await client.ConnectAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log($"[IpcController] Pipe connect timeout after {timeout.TotalMilliseconds}ms");
                return false;
            }

            // Ensure connection is established
            if (!client.IsConnected)
            {
                Log("[IpcController] Pipe client not connected after ConnectAsync");
                return false;
            }

            await using StreamWriter writer = new(client)
            {
                AutoFlush = true
            };

            string json = JsonSerializer.Serialize(command, PipeJsonOptions);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            Log($"[IpcController] Command sent: {command.Type}");
            return true;
        }
        catch (TimeoutException ex)
        {
            Log($"[IpcController] Pipe connect timeout: {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            Log($"[IpcController] IO error: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Log($"[IpcController] Unexpected error: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Attempts handshake with retries.
    /// </summary>
    public async Task<bool> SendHandshakeWithRetryAsync(int maxRetries = 3, CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            Log($"[IpcController] Handshake attempt {i + 1}/{maxRetries}");
            
            if (await SendHandshakeAsync(cancellationToken).ConfigureAwait(false))
                return true;

            // Wait before retry
            if (i < maxRetries - 1)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
        return false;
    }

    public Task<bool> SendStartAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(new SimCommand { Type = SimCommandType.Start }, cancellationToken);
    }

    public Task<bool> SendPauseAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(new SimCommand { Type = SimCommandType.Pause }, cancellationToken);
    }

    /// <summary>
    /// Resume/Attach to an existing simulation without restarting.
    /// Use this when reconnecting to a running console session.
    /// </summary>
    public Task<bool> SendResumeAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(new SimCommand { Type = SimCommandType.Resume }, cancellationToken);
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

    public Task<bool> SendUpdateSettingsAsync(string jsonPayload, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(new SimCommand
        {
            Type = SimCommandType.UpdateSettings,
            PayloadJson = jsonPayload
        }, cancellationToken);
    }
}
