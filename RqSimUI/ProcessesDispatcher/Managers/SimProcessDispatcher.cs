using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RqSimForms.ProcessesDispatcher.Managers;

public sealed class SimProcessDispatcher
{
    private Process? _simProcess;

    public bool IsConnected => _simProcess is { HasExited: false };

    public int? ProcessId => _simProcess is { HasExited: false } process ? process.Id : null;

    public async Task EnsureSimulationRunningAsync(CancellationToken cancellationToken = default)
    {
        if (TryAttachToExisting())
            return;

        StartNewSimulationProcess();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public bool TryAttachToExisting()
    {
        var existing = Process.GetProcessesByName(DispatcherConfig.SimulationProcessName)
            .FirstOrDefault(p => !p.HasExited);

        if (existing is null)
            return false;

        _simProcess = existing;
        return true;
    }

    public void StartNewSimulationProcess()
    {
        var exePath = DispatcherConfig.SimulationExecutablePath;
        Trace.WriteLine($"[SimProcessDispatcher] Looking for: {exePath}");
        Trace.WriteLine($"[SimProcessDispatcher] File exists: {File.Exists(exePath)}");

        if (!File.Exists(exePath))
        {
            Trace.WriteLine($"[SimProcessDispatcher] RqSimConsole.exe not found - running in standalone mode");
            throw new FileNotFoundException("Simulation backend not found.\nUsing direct UI to RqSimEngineAPI interop", exePath);
        }

        var startInfo = DispatcherConfig.BuildStartInfo();
        Trace.WriteLine($"[SimProcessDispatcher] Starting process: {startInfo.FileName} {startInfo.Arguments}");
        _simProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start simulation process.");
        Trace.WriteLine($"[SimProcessDispatcher] Process started with PID: {_simProcess.Id}");
    }

    public void Detach()
    {
        _simProcess?.Dispose();
        _simProcess = null;
    }

    public async Task ForceKillAsync(CancellationToken cancellationToken = default)
    {
        if (_simProcess is null || _simProcess.HasExited)
        {
            _simProcess?.Dispose();
            _simProcess = null;
            return;
        }

        try
        {
            _simProcess.Kill(entireProcessTree: true);
            await _simProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            Trace.WriteLine($"[SimProcessDispatcher] Kill failed: {ex.Message}");
        }
        finally
        {
            _simProcess?.Dispose();
            _simProcess = null;
        }
    }
}
