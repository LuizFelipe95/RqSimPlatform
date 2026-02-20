using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using RqSimForms.ProcessesDispatcher;
using RqSimForms.ProcessesDispatcher.Contracts;
using RqSimForms.ProcessesDispatcher.IPC;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main — session exclusivity guard and extended terminate scope.
/// Prevents parallel local + console simulations and ensures Terminate reaches
/// autonomous (unbound) console sessions.
/// </summary>
partial class Form_Main_RqSim
{
    /// <summary>
    /// Result of probing for a running RqSimConsole session.
    /// </summary>
    internal readonly record struct ConsoleSessionInfo(
        bool ProcessAlive,
        bool PipeAvailable,
        bool SharedMemoryAvailable,
        SimulationStatus Status,
        int NodeCount,
        long Iteration);

    /// <summary>
    /// Probes for a running RqSimConsole session using process list, named pipe,
    /// and shared memory. Returns <see langword="null"/> if no session is detected.
    /// </summary>
    private static async Task<ConsoleSessionInfo?> DetectRunningConsoleSessionAsync()
    {
        // 1. Check for a live RqSimConsole process
        var consoleProcesses = Process.GetProcessesByName(DispatcherConfig.SimulationProcessName);
        bool processAlive = consoleProcesses.Any(p => !p.HasExited);

        if (!processAlive)
            return null;

        // 2. Check named pipe availability
        bool pipeAvailable;
        try
        {
            pipeAvailable = await IpcController.IsPipeServerAvailableAsync().ConfigureAwait(false);
        }
        catch
        {
            pipeAvailable = false;
        }

        // 3. Read shared memory for status details
        bool sharedMemoryAvailable = false;
        SimulationStatus status = SimulationStatus.Unknown;
        int nodeCount = 0;
        long iteration = 0;

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(
                DispatcherConfig.SharedMemoryMapName,
                MemoryMappedFileRights.Read);
            using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            accessor.Read(0, out SharedHeader header);

            if (header.NodeCount >= 0)
            {
                sharedMemoryAvailable = true;
                status = header.Status;
                nodeCount = header.NodeCount;
                iteration = header.Iteration;
            }
        }
        catch
        {
            // Shared memory not available — process may be initializing
        }

        return new ConsoleSessionInfo(
            ProcessAlive: processAlive,
            PipeAvailable: pipeAvailable,
            SharedMemoryAvailable: sharedMemoryAvailable,
            Status: status,
            NodeCount: nodeCount,
            Iteration: iteration);
    }

    /// <summary>
    /// Gate method: checks for a conflicting console session before starting a local
    /// simulation. If a console session is running, shows a dialog and optionally
    /// terminates it.
    /// </summary>
    /// <returns><see langword="true"/> if it is safe to proceed with local simulation.</returns>
    private async Task<bool> EnsureNoConflictingSessionAsync()
    {
        var session = await DetectRunningConsoleSessionAsync().ConfigureAwait(true);
        if (session is null)
            return true;

        var info = session.Value;

        // Only block if the console simulation is actively running or paused
        if (info.Status is not (SimulationStatus.Running or SimulationStatus.Paused)
            && !info.PipeAvailable)
        {
            return true;
        }

        string statusText = info.Status switch
        {
            SimulationStatus.Running => "Running",
            SimulationStatus.Paused => "Paused",
            _ => "Active"
        };

        string message =
            $"An RqSimConsole session is currently active:\n\n" +
            $"  • Status: {statusText}\n" +
            $"  • Nodes: {info.NodeCount:N0}\n" +
            $"  • Iteration: {info.Iteration:N0}\n\n" +
            "Running two simultaneous sessions is not supported.\n" +
            "Terminate the console session and start local simulation?";

        var result = MessageBox.Show(
            this,
            message,
            "Active Console Session Detected",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
            return false;

        // User chose to terminate the console session
        bool terminated = await ForceTerminateConsoleSessionAsync().ConfigureAwait(true);

        if (!terminated)
        {
            MessageBox.Show(
                this,
                "Could not terminate the console session.\nPlease close RqSimConsole manually before starting a local simulation.",
                "Termination Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        // Brief delay to let process exit fully
        await Task.Delay(500).ConfigureAwait(true);
        return true;
    }

    /// <summary>
    /// Sends Stop + Shutdown commands via IPC pipe, then force-kills the process
    /// if it is still alive.
    /// </summary>
    /// <returns><see langword="true"/> if the console process is no longer running.</returns>
    private async Task<bool> ForceTerminateConsoleSessionAsync()
    {
        try
        {
            // Try graceful stop via pipe
            bool pipeAvailable = await IpcController.IsPipeServerAvailableAsync().ConfigureAwait(false);
            if (pipeAvailable)
            {
                await _lifeCycleManager.Ipc.SendStopAsync().ConfigureAwait(false);
                await Task.Delay(200).ConfigureAwait(false);
                await _lifeCycleManager.Ipc.RequestShutdownAsync().ConfigureAwait(false);
                await Task.Delay(DispatcherConfig.ShutdownGracePeriod).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SessionGuard] Graceful stop failed: {ex.Message}");
        }

        // Check if process is still alive; kill if necessary
        var remaining = Process.GetProcessesByName(DispatcherConfig.SimulationProcessName);
        foreach (var proc in remaining)
        {
            if (proc.HasExited) continue;

            try
            {
                proc.Kill(entireProcessTree: true);
                await proc.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SessionGuard] Kill failed for PID {proc.Id}: {ex.Message}");
                return false;
            }
        }

        return true;
    }

    // ====================================================================
    //  S2 — Extended Terminate: covers local + bound + autonomous console
    // ====================================================================

    /// <summary>
    /// Terminates all active simulation sessions (local and/or console) with
    /// user confirmation dialogs where appropriate. Replaces the original
    /// <c>button_TerminateSimSession_Click</c> logic.
    /// </summary>
    private async Task TerminateAllSessionsAsync()
    {
        // --- Console-bound path: ask first, abort if user says No ---
        bool isConsoleBound = _isConsoleBound || _lifeCycleManager.IsExternalProcessAttached;

        if (isConsoleBound)
        {
            bool userConfirmed = await TerminateBoundConsoleSessionAsync().ConfigureAwait(true);
            if (!userConfirmed)
            {
                // User chose not to terminate — leave everything running
                return;
            }
        }
        else
        {
            // Check for an autonomous (unbound) console session
            await TerminateAutonomousConsoleSessionAsync().ConfigureAwait(true);
        }

        // --- Stop local simulation if running ---
        if (_simApi.IsModernRunning)
        {
            Debug.WriteLine("[SessionGuard] Stopping local simulation...");
            _modernCts?.Cancel();
            _simApi.IsModernRunning = false;

            if (_modernSimTask is not null)
            {
                try { await _modernSimTask.ConfigureAwait(true); }
                catch { /* already handled inside the task */ }
                _modernSimTask = null;
            }
        }

        // Finalize session storage (flush metrics JSONL, update session_info.json)
        FinalizeCurrentSession();

        // Reset visualization and telemetry
        _embeddedVisualizationForm?.ResetVisualization();
        _embeddedVisualizationForm?.NotifySimulationStopped();
        _embeddedTelemetryForm?.ResetForNewSession();

        // Clear dispatcher data to stop stale rendering
        if (!_isConsoleBound)
        {
            _simApi.Dispatcher.Clear();
            _simApi.SimulationComplete = true;
        }

        // Update UI state
        button_RunModernSim.Enabled = true;
        button_RunModernSim.Text = _isConsoleBound ? "Start Console Sim" : "Run simulation";
        button_TerminateSimSession.Enabled = _isConsoleBound;
    }

    /// <summary>
    /// Handles terminate for a bound console session — asks user whether to stop
    /// the console as well, then sends Stop command.
    /// </summary>
    /// <returns><see langword="true"/> if user confirmed termination; <see langword="false"/> to cancel.</returns>
    private async Task<bool> TerminateBoundConsoleSessionAsync()
    {
        var result = MessageBox.Show(
            this,
            "Terminate the console simulation?",
            "Console Session Running",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (result != DialogResult.Yes)
            return false;

        Debug.WriteLine("[SessionGuard] Stopping bound console simulation...");
        try
        {
            await _lifeCycleManager.Ipc.SendStopAsync().ConfigureAwait(false);
            await SendSettingsToConsoleAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SessionGuard] Failed to stop bound console: {ex.Message}");
        }

        return true;
    }

    /// <summary>
    /// Detects an autonomous (unbound) console session, asks the user, and
    /// terminates it if confirmed.
    /// </summary>
    private async Task TerminateAutonomousConsoleSessionAsync()
    {
        var session = await DetectRunningConsoleSessionAsync().ConfigureAwait(true);
        if (session is null)
            return;

        var info = session.Value;
        if (info.Status is not (SimulationStatus.Running or SimulationStatus.Paused)
            && !info.PipeAvailable)
        {
            return;
        }

        string statusText = info.Status switch
        {
            SimulationStatus.Running => "Running",
            SimulationStatus.Paused => "Paused",
            _ => "Active"
        };

        var result = MessageBox.Show(
            this,
            $"An RqSimConsole session is running independently (not bound to this UI).\n\n" +
            $"  • Status: {statusText}\n" +
            $"  • Nodes: {info.NodeCount:N0}\n" +
            $"  • Iteration: {info.Iteration:N0}\n\n" +
            "Do you want to terminate it as well?",
            "Console Session Running",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
            return;

        Debug.WriteLine("[SessionGuard] User confirmed — terminating autonomous console...");
        bool terminated = await ForceTerminateConsoleSessionAsync().ConfigureAwait(true);

        if (!terminated)
        {
            MessageBox.Show(
                this,
                "Could not terminate the console session.\nPlease close RqSimConsole manually.",
                "Termination Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
