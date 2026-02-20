using RqSimForms.ProcessesDispatcher.Contracts;
using RQSimulation;
using RQSimulation.Analysis;

namespace RqSimTelemetryForm;

/// <summary>
/// Standalone telemetry form for RqSim simulation monitoring.
/// Displays live metrics, charts, console logs, and experiments.
/// Does NOT own the simulation — receives data via RqSimEngineApi or IPC.
/// </summary>
public partial class TelemetryForm : Form
{
    public TelemetryForm()
    {
        InitializeComponent();
        Shown += TelemetryForm_Shown;
    }

    private async void TelemetryForm_Shown(object? sender, EventArgs e)
    {
        try
        {
            // Initialize all tab content
            InitializeDashboardTab();
            InitializeChartsTab();
            InitializeConsoleTab();
            InitializeExperimentsTab();
            InitializeOpenTelemetryTab();
            InitializePhysicsConstantsTab();

            // Start OpenTelemetry metrics listener
            StartMetricsListener();

            // Welcome messages in console
            AppendSysConsole("[System] RqSim Telemetry Form started\n");
            AppendSysConsole($"[System] MeterListener active on '{RQSimulation.Core.Observability.RqSimPlatformTelemetry.MeterName}'\n");

            if (_hasApiConnection)
            {
                UpdateConnectionStatus("Connected to RqSimUI", StatusKind.Connected);
                _isModernRunning = _simApi.IsModernRunning;
            }
            else
            {
                UpdateConnectionStatus("Searching for simulation process...", StatusKind.Searching);
                AppendSysConsole("[System] Searching for running RqSimConsole process...\n");

                // Try to discover a running RqSimConsole process
                await _lifeCycleManager.OnFormLoadAsync().ConfigureAwait(true);

                SimState? externalState = null;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    externalState = _lifeCycleManager.TryGetExternalSimulationState();
                    if (externalState is not null)
                        break;

                    await Task.Delay(500).ConfigureAwait(true);
                }

                if (externalState is not null)
                {
                    _isExternalSimulation = true;
                    int nodeCount = externalState.Value.NodeCount;
                    string status = externalState.Value.Status switch
                    {
                        SimulationStatus.Running => $"Connected to Console — {nodeCount} nodes, running",
                        SimulationStatus.Paused => $"Connected to Console — {nodeCount} nodes, paused",
                        _ => $"Connected to Console — {nodeCount} nodes"
                    };
                    UpdateConnectionStatus(status, StatusKind.Connected);
                }
                else
                {
                    UpdateConnectionStatus(
                        "No simulation detected. Launch RqSimConsole or connect via RqSimUI.",
                        StatusKind.NoSimulation);
                }
            }

            // Start UI update timer
            StartUiUpdateTimer();
        }
        catch (Exception ex)
        {
            UpdateConnectionStatus($"Error: {ex.Message}", StatusKind.Error);
            System.Diagnostics.Trace.WriteLine($"[TelemetryForm] Shown error: {ex}");
        }
    }

    private void StartUiUpdateTimer()
    {
        _uiUpdateTimer = new System.Windows.Forms.Timer
        {
            Interval = 200
        };
        _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
        _uiUpdateTimer.Start();
    }

    /// <summary>
    /// Timer tick handler — updates UI from live metrics (runs on UI thread).
    /// Non-blocking: skips frame if dispatcher is busy.
    /// </summary>
    private void UiUpdateTimer_Tick(object? sender, EventArgs e)
    {
        // 1. Fetch and display logs from LogStatistics
        string[] cpuLogs = LogStatistics.FetchCpuLogs();
        if (cpuLogs.Length > 0)
        {
            AppendSysConsole(string.Join(Environment.NewLine, cpuLogs) + Environment.NewLine);
        }

        string[] gpuLogs = LogStatistics.FetchGpuLogs();
        if (gpuLogs.Length > 0)
        {
            AppendGPUConsole(string.Join(Environment.NewLine, gpuLogs) + Environment.NewLine);
        }

        if (_isExternalSimulation)
        {
            HandleExternalSimulationTick();
            return;
        }

        if (!_hasApiConnection || _simApi.SimulationComplete || !_simApi.IsModernRunning)
        {
            // Update status label so it reflects stopped state instead of stale "Running..."
            if (_hasApiConnection && !_simApi.IsModernRunning)
            {
                _valStatus.Text = _simApi.SimulationComplete ? "Complete" : "Stopped";
                _valStatus.ForeColor = SystemColors.ControlText;
            }

            // Keep timer alive but skip frame — simulation may start later
            return;
        }

        // Force get display data with timeout
        var displayData = _simApi.Dispatcher.ForceGetDisplayDataImmediate(timeoutMs: 50);

        // Read volatile fields (lock-free)
        int step = _simApi.LiveStep;
        int excited = _simApi.LiveExcited;
        double heavyMass = _simApi.LiveHeavyMass;
        int largestCluster = _simApi.LiveLargestCluster;
        int strongEdges = _simApi.LiveStrongEdges;
        double qNorm = _simApi.LiveQNorm;
        double entanglement = _simApi.LiveEntanglement;
        double correlation = _simApi.LiveCorrelation;
        int totalSteps = _simApi.LiveTotalSteps;

        // Calculate statistics
        double avgExcited = 0.0;
        int maxExcited = 0;
        if (displayData.DecimatedExcited.Length > 0)
        {
            int sum = 0;
            for (int i = 0; i < displayData.DecimatedExcited.Length; i++)
            {
                int val = displayData.DecimatedExcited[i];
                sum += val;
                if (val > maxExcited) maxExcited = val;
            }
            avgExcited = (double)sum / displayData.DecimatedExcited.Length;
        }

        // Invalidate chart panels (Dashboard + Charts tab)
        _panelExcitedChart?.Invalidate();
        _panelHeavyChart?.Invalidate();
        _panelClusterChart?.Invalidate();
        _panelEnergyChart?.Invalidate();
        _chartsTabExcited?.Invalidate();
        _chartsTabHeavy?.Invalidate();
        _chartsTabCluster?.Invalidate();
        _chartsTabEnergy?.Invalidate();

        // Poll observable gauges so MeterListener captures their current values
        _meterListener?.RecordObservableInstruments();

        // Refresh OpenTelemetry viewer
        RefreshOpenTelemetryTab();

        int graphN = _simApi.Dispatcher.LiveNodeCount > 0
            ? _simApi.Dispatcher.LiveNodeCount
            : _simulationEngine?.Graph?.N ?? 100;
        string phase = excited > graphN / 3 ? "Active" : (excited > graphN / 10 ? "Moderate" : "Quiet");

        // Update all dashboard components
        UpdateDashboard(step, totalSteps, excited, heavyMass, largestCluster,
            strongEdges, phase, qNorm, entanglement, correlation);
        UpdateStatusBar(step, totalSteps, excited, avgExcited, heavyMass);

        // Refresh live parameters on Physics Constants tab
        RefreshLivePhysicsParams();
    }

    private void HandleExternalSimulationTick()
    {
        // In embedded mode _simApi.Dispatcher is fed by the host's ConsolePollingTimer.
        // In standalone mode we read from our own LifeCycleManager.
        // Detect embedded console mode: _hasApiConnection means host set our API.
        if (_hasApiConnection)
        {
            HandleEmbeddedConsoleModeTick();
            return;
        }

        SimState? externalState = _lifeCycleManager.TryGetExternalSimulationState();
        if (externalState is null)
        {
            RateLimitedLogExternal("externalState=null (shared memory not available or stale)");
            return;
        }

        SimState s = externalState.Value;

        // Throttled log
        DateTime now = DateTime.UtcNow;
        if (now - _lastExternalNoDataLogUtc >= TimeSpan.FromSeconds(2))
        {
            _lastExternalNoDataLogUtc = now;
            AppendSysConsole($"[External] Nodes={s.NodeCount}, Status={s.Status}, Excited={s.ExcitedCount}\n");
        }

        // Accumulate IPC snapshot into local time series for chart rendering
        _ipcTimeSeries.Append(
            step: (int)s.Iteration,
            excited: s.ExcitedCount,
            heavyMass: s.HeavyMass,
            largestCluster: s.LargestCluster,
            energy: s.SystemEnergy,
            networkTemp: s.NetworkTemperature);

        // === Update ALL dashboard labels from IPC state directly ===
        // (Do NOT call UpdateDashboard() — it reads from _simApi.Live* which are zero in IPC mode)
        _valNodes.Text = s.NodeCount.ToString();
        _valCurrentStep.Text = ((int)s.Iteration).ToString();
        _valTotalSteps.Text = s.TotalSteps > 0 ? s.TotalSteps.ToString() : "—";
        _valExcited.Text = s.ExcitedCount.ToString();
        _valHeavyMass.Text = s.HeavyMass.ToString("F2");
        _valLargestCluster.Text = s.LargestCluster.ToString();
        _valStrongEdges.Text = s.StrongEdgeCount.ToString();
        _valQNorm.Text = s.QNorm.ToString("F6");
        _valEntanglement.Text = s.Entanglement.ToString("F6");
        _valCorrelation.Text = s.Correlation.ToString("F6");

        // Status
        _valStatus.Text = s.Status.ToString();
        _valStatus.ForeColor = s.Status switch
        {
            SimulationStatus.Faulted => Color.Red,
            SimulationStatus.Running => Color.Green,
            SimulationStatus.Paused => Color.DarkOrange,
            _ => SystemColors.ControlText
        };

        // Phase heuristic
        int graphN = s.NodeCount > 0 ? s.NodeCount : 100;
        _valPhase.Text = s.ExcitedCount > graphN / 3 ? "Active"
            : (s.ExcitedCount > graphN / 10 ? "Moderate" : "Quiet");

        // Spectral metrics from IPC (not from _simApi)
        _valSpectralDim.Text = s.SpectralDimension.ToString("F3");
        _valEffectiveG.Text = s.EffectiveG.ToString("F4");
        _valNetworkTemp.Text = s.NetworkTemperature.ToString("F3");

        // g Suppression = effectiveG / targetG
        double targetG = PhysicsConstants.GravitationalCoupling;
        double gSuppression = targetG > 0 ? s.EffectiveG / targetG : 1.0;
        gSuppression = Math.Clamp(gSuppression, 0.0, 2.0);
        _valGSuppression.Text = gSuppression.ToString("F3");

        // Color-code g suppression
        if (gSuppression < 0.3)
            _valGSuppression.ForeColor = Color.Red;
        else if (gSuppression < 0.7)
            _valGSuppression.ForeColor = Color.DarkOrange;
        else
            _valGSuppression.ForeColor = SystemColors.ControlText;

        ColorCodeSpectralDim(s.SpectralDimension);

        // Status bar
        _statusLabelSteps.Text = $"Step: {(int)s.Iteration}/{(s.TotalSteps > 0 ? s.TotalSteps.ToString() : "∞")}";
        _statusLabelExcited.Text = $"Excited: {s.ExcitedCount}";

        string status = s.Status switch
        {
            SimulationStatus.Running => $"Connected to Console — {s.NodeCount} nodes, running",
            SimulationStatus.Paused => $"Connected to Console — {s.NodeCount} nodes, paused",
            _ => $"Connected to Console — {s.NodeCount} nodes"
        };
        UpdateConnectionStatus(status, StatusKind.Connected);

        // Invalidate charts so they repaint with IPC data
        _panelExcitedChart?.Invalidate();
        _panelHeavyChart?.Invalidate();
        _panelClusterChart?.Invalidate();
        _panelEnergyChart?.Invalidate();

        // Also invalidate Charts tab panels (separate panel instances)
        _chartsTabExcited?.Invalidate();
        _chartsTabHeavy?.Invalidate();
        _chartsTabCluster?.Invalidate();
        _chartsTabEnergy?.Invalidate();

        // Populate OpenTelemetry metrics from IPC state
        PopulateIpcMetrics(s);

        // Populate module performance from SharedMemory pipeline stats
        PopulateModuleStatsFromIpc();

        // Refresh OpenTelemetry viewer
        RefreshOpenTelemetryTab();

        // Refresh live parameters on Physics Constants tab
        RefreshLivePhysicsParams();
    }

    /// <summary>
    /// Handles timer tick in embedded console mode.
    /// Reads from _simApi.Dispatcher (fed by host's ConsolePollingTimer) and
    /// accumulates into IpcTimeSeries for chart rendering.
    /// </summary>
    private void HandleEmbeddedConsoleModeTick()
    {
        // If the host has signalled that the simulation is no longer running
        // (e.g. Terminate was pressed), skip metric accumulation and show stopped state.
        bool isRunning = _simApi.IsModernRunning;

        int step = _simApi.LiveStep;
        int nodeCount = _simApi.Dispatcher.LiveNodeCount;

        if (!isRunning)
        {
            // Update status labels to reflect stopped state
            _valStatus.Text = "Stopped";
            _valStatus.ForeColor = SystemColors.ControlText;
            string stoppedStatus = nodeCount > 0
                ? $"Connected to Console — {nodeCount} nodes, stopped"
                : "Connected to Console — stopped";
            UpdateConnectionStatus(stoppedStatus, StatusKind.Connected);
            return;
        }

        if (nodeCount <= 0 || step <= 0)
            return;

        int excited = _simApi.LiveExcited;
        double heavyMass = _simApi.LiveHeavyMass;
        int largestCluster = _simApi.LiveLargestCluster;
        int strongEdges = _simApi.LiveStrongEdges;
        double qNorm = _simApi.LiveQNorm;
        double entanglement = _simApi.LiveEntanglement;
        double correlation = _simApi.LiveCorrelation;
        int totalSteps = _simApi.LiveTotalSteps;
        double spectralDim = _simApi.Dispatcher.LiveSpectralDim;
        double effectiveG = _simApi.Dispatcher.LiveEffectiveG;
        double networkTemp = _simApi.Dispatcher.LiveTemp;
        double systemEnergy = _simApi.Dispatcher.LiveSystemEnergy;

        // Accumulate into local time series for chart rendering
        _ipcTimeSeries.Append(step, excited, heavyMass, largestCluster, systemEnergy, networkTemp);

        // Update dashboard labels
        _valNodes.Text = nodeCount.ToString();
        _valCurrentStep.Text = step.ToString();
        _valTotalSteps.Text = totalSteps > 0 ? totalSteps.ToString() : "—";
        _valExcited.Text = excited.ToString();
        _valHeavyMass.Text = heavyMass.ToString("F2");
        _valLargestCluster.Text = largestCluster.ToString();
        _valStrongEdges.Text = strongEdges.ToString();
        _valQNorm.Text = qNorm.ToString("F6");
        _valEntanglement.Text = entanglement.ToString("F6");
        _valCorrelation.Text = correlation.ToString("F6");

        // Spectral metrics
        _valSpectralDim.Text = spectralDim.ToString("F3");
        _valEffectiveG.Text = effectiveG.ToString("F4");
        _valNetworkTemp.Text = networkTemp.ToString("F3");

        // g Suppression
        double targetG = PhysicsConstants.GravitationalCoupling;
        double gSuppression = targetG > 0 ? effectiveG / targetG : 1.0;
        gSuppression = Math.Clamp(gSuppression, 0.0, 2.0);
        _valGSuppression.Text = gSuppression.ToString("F3");

        if (gSuppression < 0.3)
            _valGSuppression.ForeColor = Color.Red;
        else if (gSuppression < 0.7)
            _valGSuppression.ForeColor = Color.DarkOrange;
        else
            _valGSuppression.ForeColor = SystemColors.ControlText;

        ColorCodeSpectralDim(spectralDim);

        // Status
        _valStatus.Text = "Running...";
        _valStatus.ForeColor = Color.Green;

        int graphN = nodeCount > 0 ? nodeCount : 100;
        _valPhase.Text = excited > graphN / 3 ? "Active"
            : (excited > graphN / 10 ? "Moderate" : "Quiet");

        // Status bar
        _statusLabelSteps.Text = $"Step: {step}/{(totalSteps > 0 ? totalSteps.ToString() : "∞")}";
        _statusLabelExcited.Text = $"Excited: {excited}";
        UpdateConnectionStatus($"Connected to Console — {nodeCount} nodes, running", StatusKind.Connected);

        // Invalidate all chart panels
        _panelExcitedChart?.Invalidate();
        _panelHeavyChart?.Invalidate();
        _panelClusterChart?.Invalidate();
        _panelEnergyChart?.Invalidate();
        _chartsTabExcited?.Invalidate();
        _chartsTabHeavy?.Invalidate();
        _chartsTabCluster?.Invalidate();
        _chartsTabEnergy?.Invalidate();

        // Populate OTel metrics from dispatcher values (MeterListener doesn't
        // capture cross-process metrics from the console)
        PopulateEmbeddedConsoleMetrics(step, nodeCount, excited, heavyMass,
            largestCluster, strongEdges, spectralDim, effectiveG, networkTemp,
            qNorm, entanglement, correlation);

        // Populate module performance from SharedMemory pipeline stats
        PopulateModuleStatsFromIpc();

        RefreshOpenTelemetryTab();

        // Refresh live parameters on Physics Constants tab
        RefreshLivePhysicsParams();
    }

    /// <summary>
    /// Populates OTel metric snapshots from Dispatcher live values in embedded console mode.
    /// MeterListener only captures in-process metrics — console runs in a separate process.
    /// </summary>
    private void PopulateEmbeddedConsoleMetrics(
        int step, int nodeCount, int excited, double heavyMass,
        int largestCluster, int strongEdges, double spectralDim, double effectiveG,
        double networkTemp, double qNorm, double entanglement, double correlation)
    {
        DateTime now = DateTime.UtcNow;
        KeyValuePair<string, object?>[] empty = [];

        RecordIpcMetric("rqsim.graph.nodes", nodeCount, "nodes", now, empty);
        RecordIpcMetric("rqsim.graph.excited", excited, "nodes", now, empty);
        RecordIpcMetric("rqsim.physics.heavy_mass", heavyMass, "", now, empty);
        RecordIpcMetric("rqsim.physics.largest_cluster", largestCluster, "nodes", now, empty);
        RecordIpcMetric("rqsim.physics.strong_edges", strongEdges, "edges", now, empty);
        RecordIpcMetric("rqsim.physics.spectral_dimension", spectralDim, "", now, empty);
        RecordIpcMetric("rqsim.physics.effective_g", effectiveG, "", now, empty);
        RecordIpcMetric("rqsim.physics.network_temp", networkTemp, "", now, empty);
        RecordIpcMetric("rqsim.physics.q_norm", qNorm, "", now, empty);
        RecordIpcMetric("rqsim.physics.entanglement", entanglement, "", now, empty);
        RecordIpcMetric("rqsim.physics.correlation", correlation, "", now, empty);
        RecordIpcMetric("rqsim.simulation.iteration", step, "steps", now, empty);
    }

    private void RateLimitedLogExternal(string msg)
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastExternalNoDataLogUtc < TimeSpan.FromSeconds(1))
            return;

        _lastExternalNoDataLogUtc = now;
        AppendSysConsole($"[ExternalRender] no data: {msg}\n");
    }

    private void ColorCodeSpectralDim(double spectralDim)
    {
        if (spectralDim < 1.5)
            _valSpectralDim.ForeColor = Color.Red;
        else if (spectralDim > 4.0)
            _valSpectralDim.ForeColor = Color.DarkOrange;
        else
            _valSpectralDim.ForeColor = Color.Green;
    }

    private enum StatusKind { Searching, Connected, NoSimulation, Error }

    private void UpdateConnectionStatus(string text, StatusKind kind)
    {
        _statusLabelConnection.Text = kind switch
        {
            StatusKind.Searching => $"\u23F3 {text}",
            StatusKind.Connected => $"\u2705 {text}",
            StatusKind.NoSimulation => $"\u26A0 {text}",
            StatusKind.Error => $"\u274C {text}",
            _ => text
        };

        _statusLabelConnection.ForeColor = kind switch
        {
            StatusKind.Connected => Color.Green,
            StatusKind.NoSimulation => Color.Orange,
            StatusKind.Error => Color.OrangeRed,
            _ => SystemColors.ControlText
        };
    }

}
