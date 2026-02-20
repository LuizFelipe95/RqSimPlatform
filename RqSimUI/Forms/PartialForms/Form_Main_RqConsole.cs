using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text.Json;
using RqSimForms.ProcessesDispatcher;
using RqSimForms.ProcessesDispatcher.Contracts;
using RqSimForms.ProcessesDispatcher.IPC;
using RqSimPlatform.Contracts;
using RQSimulation;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main — RqSimConsole binding, shared memory polling,
/// and console process lifecycle management.
/// </summary>
public partial class Form_Main_RqSim
{
    // === Console Session Binding ===
    private MemoryMappedFile? _consoleSharedMemory;
    private MemoryMappedViewAccessor? _consoleSharedMemoryAccessor;
    private System.Windows.Forms.Timer? _consolePollingTimer;
    private bool _isConsoleBound;
    private bool _isExternalSimulation;
    private Button? _boundConsoleButton;
    private DateTime _lastSharedMemoryAttachAttemptUtc = DateTime.MinValue;
    private bool _reportedSharedMemoryMissing;


    private async void button_ApplyPipelineConfSet_Click(object? sender, EventArgs e)
    {
        try
        {
            // 1. Apply pipeline module flags to the local engine
            ApplyPhysicsParametersToPipeline();

            // 2. Force LiveConfig sync so the running local simulation picks up
            // values immediately (Pipeline params alone are not read by the sim loop)
            OnLiveParameterChanged(sender, e);

            // 3. Persist the full settings snapshot to simulation_settings.json
            // so both local restarts and Console reads see up-to-date values
            SaveUnifiedSettingsFile();
            Debug.WriteLine($"[ApplyPipeline] Settings saved to {SessionStoragePaths.SimulationSettingsPath}");

            // 4. Push settings to Console process via IPC if bound
            if (_isConsoleBound)
            {
                await SendSettingsToConsoleAsync().ConfigureAwait(true);
                Debug.WriteLine("[ApplyPipeline] Settings pushed to console via IPC");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ApplyPipeline] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Syncs UI to an external (console) simulation discovered at startup.
    /// If simulation is stopped, automatically sends Settings and START command.
    /// </summary>
    private void SyncToExternalSimulation(SimState state)
    {
        Debug.WriteLine($"[SyncToExternalSimulation] Status={state.Status}, Nodes={state.NodeCount}");

        _isExternalSimulation = true;
        _isConsoleBound = true;

        UpdateConsoleBindButton(true);
        // Enable Terminate button so user can stop the session or reset state
        if (button_TerminateSimSession != null) button_TerminateSimSession.Enabled = true;

        TryAttachConsoleSharedMemory(force: true);
        StartConsolePollingTimer();
        NotifyTelemetryConsoleMode(true);
        _embeddedVisualizationForm?.SetConsoleMode(true);

        switch (state.Status)
        {
            case SimulationStatus.Running:
                _simApi.IsModernRunning = true;
                button_RunModernSim.Text = "Pause Console Sim";
                Debug.WriteLine($"[Dispatcher] Attached to RUNNING simulation: Nodes={state.NodeCount}");
                break;

            case SimulationStatus.Paused:
                _simApi.IsModernRunning = false;
                button_RunModernSim.Text = "Resume Console Sim";
                Debug.WriteLine($"[Dispatcher] Attached to PAUSED simulation: Nodes={state.NodeCount}");
                break;

            default:
                _simApi.IsModernRunning = false;
                button_RunModernSim.Text = "Starting...";
                Debug.WriteLine("[Dispatcher] Attached to STOPPED simulation - AUTO-STARTING...");
                _ = AutoStartConsoleSimulationAsync();
                break;
        }
    }

    /// <summary>
    /// Sends START command to a stopped console after pushing current settings.
    /// </summary>
    private async Task AutoStartConsoleSimulationAsync()
    {
        try
        {
            Debug.WriteLine("[AutoStart] Sending settings to console...");
            await SendSettingsToConsoleAsync();
            await Task.Delay(200);

            Debug.WriteLine("[AutoStart] Sending START command...");
            bool success = await _lifeCycleManager.Ipc.SendStartAsync();

            if (success)
            {
                Debug.WriteLine("[AutoStart] START command sent");
                Invoke(() =>
                {
                    _simApi.IsModernRunning = true;
                    button_RunModernSim.Text = "Pause Console Sim";
                });
            }
            else
            {
                Debug.WriteLine("[AutoStart] Failed to send START command");
                Invoke(() => button_RunModernSim.Text = "Start Console Sim");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoStart] Error: {ex.Message}");
            Invoke(() => button_RunModernSim.Text = "Start Console Sim");
        }
    }

    /// <summary>
    /// Binds to a running RqSimConsole session via shared memory and named pipes.
    /// If no console is running, offers to start one.
    /// </summary>
    private async void button_BindConsoleSession_Click(object? sender, EventArgs e)
    {
        if (sender is Button btn)
        {
            _boundConsoleButton = btn;
        }

        if (_isConsoleBound)
        {
            UnbindConsoleSession();
            return;
        }

        try
        {
            Debug.WriteLine("[Console] Checking for RqSimConsole...");

            // Ensure the process dispatcher has a reference to the console process.
            // Without this, IsExternalProcessAttached returns false after manual bind,
            // causing the polling timer to unbind the session immediately.
            _lifeCycleManager.Dispatcher.TryAttachToExisting();

            if (!_lifeCycleManager.IsExternalProcessAttached)
            {
                var consoleProcesses = Process.GetProcessesByName("RqSimConsole");
                if (consoleProcesses.Length == 0)
                {
                    Debug.WriteLine("[Console] RqSimConsole process not found");

                    var result = MessageBox.Show(
                        "RqSimConsole is not running.\n\n" +
                        "Would you like to start it now in server mode?",
                        "Console Not Found",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.Yes)
                    {
                        StartConsoleProcess();
                        Debug.WriteLine("[Console] Waiting for console to initialize...");
                        await Task.Delay(3000);
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    Debug.WriteLine($"[Console] Found {consoleProcesses.Length} RqSimConsole process(es)");
                }
            }
            else
            {
                Debug.WriteLine("[Console] Found attached console process via LifeCycleManager");
            }

            Debug.WriteLine("[Console] Attempting handshake...");
            bool handshakeOk = await _lifeCycleManager.Ipc.SendHandshakeWithRetryAsync(maxRetries: 5);

            if (!handshakeOk)
            {
                Debug.WriteLine("[Console] Handshake failed after 5 attempts");
                MessageBox.Show(
                    "Could not connect to RqSimConsole pipe.\n\n" +
                    "Possible reasons:\n" +
                    "• Console is not running in server mode (--server-mode)\n" +
                    "• Pipe server not yet initialized\n\n" +
                    "Check the console window for error messages.",
                    "Connection Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Debug.WriteLine("[Console] Handshake successful!");

            StartConsolePollingTimer();
            TryAttachConsoleSharedMemory(force: true);

            // Check current console status
            bool consoleIsRunning = false;
            if (_consoleSharedMemoryAccessor is not null)
            {
                try
                {
                    _consoleSharedMemoryAccessor.Read(0, out SharedHeader header);
                    var status = (SimulationStatus)header.StateCode;
                    consoleIsRunning = status is SimulationStatus.Running or SimulationStatus.Paused;

                    if (consoleIsRunning)
                    {
                        Debug.WriteLine($"[Console] Console already running (Status={status}, Nodes={header.NodeCount})");
                    }
                }
                catch
                {
                    // Ignore read errors
                }
            }

            _isConsoleBound = true;
            _isExternalSimulation = true;
            UpdateConsoleBindButton(true);
            // Enable Terminate button so user can stop the session or reset state
            if (button_TerminateSimSession != null) button_TerminateSimSession.Enabled = true;
            NotifyTelemetryConsoleMode(true);

            // If console is stopped, push current UI settings for initialization
            if (!consoleIsRunning)
            {
                Debug.WriteLine("[Console] Console stopped — sending settings for initialization...");
                await SendSettingsToConsoleAsync();
            }

            button_RunModernSim.Text = consoleIsRunning ? "Pause Console Sim" : "Start Console Sim";

            Debug.WriteLine("[Console] Successfully bound to RqSimConsole!");
            MessageBox.Show(
                "Connected to RqSimConsole!\n\n" +
                "The UI will reattach to the running simulation automatically.",
                "Connected",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Console] Error: {ex.Message}");
            MessageBox.Show($"Failed to connect: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void button_TerminateSimSession_Click(object? sender, EventArgs e)
    {
        try
        {
            await TerminateAllSessionsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Terminate] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles Run button click when bound to console — toggles Start/Pause/Resume.
    /// </summary>
    private async Task HandleConsoleBoundSimulationAsync()
    {
        if (!_isConsoleBound)
        {
            Debug.WriteLine("[Console] Not bound, cannot handle console simulation");
            return;
        }

        // Check current status via shared memory
        SimulationStatus currentStatus = SimulationStatus.Stopped;
        if (_consoleSharedMemoryAccessor is not null)
        {
            try
            {
                _consoleSharedMemoryAccessor.Read(0, out SharedHeader header);
                currentStatus = (SimulationStatus)header.StateCode;
            }
            catch
            {
                // Fallback to stopped
            }
        }

        switch (currentStatus)
        {
            case SimulationStatus.Running:
                Debug.WriteLine("[Console] Sending PAUSE...");
                await _lifeCycleManager.Ipc.SendPauseAsync();
                Invoke(() =>
                {
                    _simApi.IsModernRunning = false;
                    button_RunModernSim.Text = "Resume Console Sim";
                });
                break;

            case SimulationStatus.Paused:
                Debug.WriteLine("[Console] Sending RESUME...");
                await _lifeCycleManager.Ipc.SendResumeAsync();
                Invoke(() =>
                {
                    _simApi.IsModernRunning = true;
                    button_RunModernSim.Text = "Pause Console Sim";
                    button_TerminateSimSession.Enabled = true;
                });
                break;

            default:
                Debug.WriteLine("[Console] Sending settings then START...");
                await SendSettingsToConsoleAsync();
                bool started = await _lifeCycleManager.Ipc.SendStartAsync();
                if (started)
                {
                    StartNewSession("console");

                    Invoke(() =>
                    {
                        _simApi.IsModernRunning = true;
                        button_RunModernSim.Text = "Pause Console Sim";
                        button_TerminateSimSession.Enabled = true;
                    });
                }
                break;
        }
    }

    private bool TryAttachConsoleSharedMemory(bool force)
    {
        if (!_isConsoleBound && !force)
            return false;

        var nowUtc = DateTime.UtcNow;
        if (!force && nowUtc - _lastSharedMemoryAttachAttemptUtc < TimeSpan.FromSeconds(1))
            return _consoleSharedMemoryAccessor is not null;

        _lastSharedMemoryAttachAttemptUtc = nowUtc;

        if (_consoleSharedMemoryAccessor is not null)
            return true;

        try
        {
            _consoleSharedMemory = MemoryMappedFile.OpenExisting(
                DispatcherConfig.SharedMemoryMapName,
                MemoryMappedFileRights.Read);

            _consoleSharedMemoryAccessor = _consoleSharedMemory.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _reportedSharedMemoryMissing = false;

            Debug.WriteLine("[Console] Shared memory connected");
            return true;
        }
        catch (FileNotFoundException)
        {
            if (!_reportedSharedMemoryMissing)
            {
                _reportedSharedMemoryMissing = true;
                Debug.WriteLine("[Console] Shared memory not found yet (will retry)");
            }
            return false;
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[Console] Shared memory attach failed: {ex.Message}");
            return false;
        }
    }

    private void ReleaseConsoleSharedMemory()
    {
        _consoleSharedMemoryAccessor?.Dispose();
        _consoleSharedMemoryAccessor = null;

        _consoleSharedMemory?.Dispose();
        _consoleSharedMemory = null;
    }

    private void StartConsolePollingTimer()
    {
        _consolePollingTimer ??= new System.Windows.Forms.Timer { Interval = 200 };
        _consolePollingTimer.Tick -= ConsolePollingTimer_Tick;
        _consolePollingTimer.Tick += ConsolePollingTimer_Tick;
        _consolePollingTimer.Start();
    }

    private void ConsolePollingTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isConsoleBound)
            return;

        try
        {
            if (_consoleSharedMemoryAccessor is null)
            {
                // Re-check process attachment — manual bind may not have set _simProcess
                if (!_lifeCycleManager.IsExternalProcessAttached)
                {
                    _lifeCycleManager.Dispatcher.TryAttachToExisting();
                }

                if (!_lifeCycleManager.IsExternalProcessAttached)
                {
                    Debug.WriteLine("[Console] Console session lost. Switching to local mode.");
                    UnbindConsoleSession();
                    return;
                }

                TryAttachConsoleSharedMemory(force: false);
                return;
            }

            _consoleSharedMemoryAccessor.Read(0, out SharedHeader header);

            if (header.NodeCount < 0)
                return;

            // Stale data detection — if timestamp is too old, shared memory may be invalid
            if (header.LastUpdateTimestampUtcTicks > 0)
            {
                TimeSpan age = DateTime.UtcNow - new DateTime(header.LastUpdateTimestampUtcTicks, DateTimeKind.Utc);
                if (age > DispatcherConfig.StaleDataThreshold)
                {
                    // Don't release shared memory immediately — the console may be
                    // reinitializing the graph (GPU init + graph build takes several seconds).
                    // Just skip this polling tick and retry next time.
                    if (!_reportedSharedMemoryMissing)
                    {
                        Debug.WriteLine($"[Console] Shared memory data stale ({age.TotalSeconds:F1}s) — waiting for console update");
                        _reportedSharedMemoryMissing = true;
                    }
                    return;
                }
                else if (_reportedSharedMemoryMissing)
                {
                    // Data is fresh again after a stale period
                    Debug.WriteLine("[Console] Shared memory data is fresh again");
                    _reportedSharedMemoryMissing = false;
                }
            }

            // Push metrics into dispatcher so _simApi.Live* fields are up-to-date
            var status = (SimulationStatus)header.StateCode;
            _simApi.Dispatcher.UpdateLiveMetrics(
                (int)header.Iteration,
                header.ExcitedCount,
                header.HeavyMass,
                header.LargestCluster,
                header.StrongEdgeCount,
                header.QNorm,
                header.Entanglement,
                header.Correlation,
                header.LatestSpectralDimension,
                header.NetworkTemperature,
                header.EffectiveG,
                header.TotalSteps,
                0.0,
                0, 0, 0.0, 0.0, 0.0,
                header.EdgeCount, 1, header.NodeCount);
            _simApi.Dispatcher.SetLiveSystemEnergy(header.SystemEnergy);

            // Stream metrics to session JSONL (throttled internally)
            SampleAndAppendMetrics();

            // Detect console status transitions (e.g. TotalSteps reached → Stopped)
            if (status == SimulationStatus.Stopped && _simApi.IsModernRunning)
            {
                Debug.WriteLine("[Console] Console simulation stopped (TotalSteps reached or external stop)");
                _simApi.IsModernRunning = false;
                button_RunModernSim.Text = "Start Console Sim";
            }
            else if (status == SimulationStatus.Running && !_simApi.IsModernRunning)
            {
                _simApi.IsModernRunning = true;
                button_RunModernSim.Text = "Pause Console Sim";
            }
        }
        catch (ObjectDisposedException)
        {
            ReleaseConsoleSharedMemory();
        }
        catch (IOException)
        {
            ReleaseConsoleSharedMemory();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Console] Polling error: {ex.Message}");
        }
    }

    private void UnbindConsoleSession()
    {
        _consolePollingTimer?.Stop();
        _consolePollingTimer?.Dispose();
        _consolePollingTimer = null;

        ReleaseConsoleSharedMemory();

        _isConsoleBound = false;
        _isExternalSimulation = false;
        _reportedSharedMemoryMissing = false;
        _lastSharedMemoryAttachAttemptUtc = DateTime.MinValue;

        UpdateConsoleBindButton(false);
        // Disable Terminate button unless local simulation is running (which Unbind implies it isn't, but let's be safe)
        if (!_simApi.IsModernRunning && button_TerminateSimSession != null)
            button_TerminateSimSession.Enabled = false;

        NotifyTelemetryConsoleMode(false);
        _embeddedVisualizationForm?.SetConsoleMode(false);
        Debug.WriteLine("[Console] Disconnected from RqSimConsole");
        button_RunModernSim.Text = "Run simulation";
    }

    /// <summary>
    /// Updates the Bind Console button visual state — green when bound, default when unbound.
    /// </summary>
    private void UpdateConsoleBindButton(bool isBound)
    {
        var btn = _boundConsoleButton ?? button_BindConsoleSession;
        btn.Text = isBound ? "Disconnect" : "Bind Console Session";
        btn.BackColor = isBound
            ? Color.FromArgb(100, 180, 100)
            : SystemColors.Control;
    }

    /// <summary>
    /// Sends current UI settings to the Console process via IPC pipe.
    /// Also persists the unified settings file for Console startup reads.
    /// </summary>
    private async Task SendSettingsToConsoleAsync()
    {
        try
        {
            // Persist unified settings file (replaces old SaveSharedSettingsForConsole)
            SaveUnifiedSettingsFile();

            ServerModeSettingsDto settings = default!;
            Invoke(() => settings = BuildServerModeSettingsFromUI());

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = false });

            bool sent = await _lifeCycleManager.Ipc.SendUpdateSettingsAsync(json);
            if (sent)
            {
                Debug.WriteLine($"[Console] Settings sent (Nodes={settings.NodeCount}, TotalSteps={settings.TotalSteps}, Temp={settings.Temperature:F2})");
            }
            else
            {
                Debug.WriteLine("[Console] Failed to send settings");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Console] SendSettings error: {ex.Message}");
        }
    }

    private void StartConsoleProcess()
    {
        try
        {
            var startInfo = DispatcherConfig.BuildStartInfo();
            var process = Process.Start(startInfo);
            if (process is not null)
            {
                Debug.WriteLine($"[Console] Started RqSimConsole (PID: {process.Id})");
            }
            else
            {
                Debug.WriteLine("[Console] Failed to start RqSimConsole");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Console] Error starting console: {ex.Message}");
            MessageBox.Show($"Failed to start RqSimConsole: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Checks if a pipeline module is enabled by name.
    /// Falls back to true (enabled) if pipeline or module not found.
    /// </summary>
    private bool IsModuleEnabled(string moduleName)
    {
        var pipeline = _simApi?.Pipeline;
        if (pipeline is null)
            return true;

        var module = pipeline.GetModule(moduleName);
        return module?.IsEnabled ?? true;
    }
}
