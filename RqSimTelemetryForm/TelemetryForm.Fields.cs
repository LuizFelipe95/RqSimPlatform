using RqSimForms.Forms.Interfaces;
using RqSimForms.ProcessesDispatcher.Managers;
using RqSimForms.ProcessesDispatcher.IPC;
using RqSimForms.ProcessesDispatcher.Contracts;
using RqSimUI.FormSimAPI.Interfaces;
using RqSimGraphEngine.Experiments;
using RQSimulation;

namespace RqSimTelemetryForm;

/// <summary>
/// Event args for experiment config apply requests.
/// Raised when user clicks "Apply to Simulation" in the Experiments tab.
/// </summary>
public sealed class ExperimentConfigEventArgs : EventArgs
{
    /// <summary>
    /// The experiment configuration to apply to the simulation.
    /// </summary>
    public ExperimentConfig Config { get; }

    /// <summary>
    /// The experiment name for logging purposes.
    /// </summary>
    public string ExperimentName { get; }

    public ExperimentConfigEventArgs(ExperimentConfig config, string experimentName)
    {
        ArgumentNullException.ThrowIfNull(config);
        Config = config;
        ExperimentName = experimentName ?? "";
    }
}

/// <summary>
/// Fields, enums, and stub methods for TelemetryForm partial classes.
/// TelemetryForm is a standalone telemetry window that does NOT own the simulation.
/// Data arrives via RqSimEngineApi reference or IPC/SharedMemory.
/// </summary>
public partial class TelemetryForm
{
    // ============================================================
    // EVENTS
    // ============================================================

    /// <summary>
    /// Raised when user clicks "Apply to Simulation" in the Experiments tab.
    /// The hosting application (RqSimUI) should subscribe to apply the config.
    /// </summary>
    public event EventHandler<ExperimentConfigEventArgs>? ExperimentConfigApplyRequested;
    // ============================================================
    // SIMULATION API (read-only consumer)
    // ============================================================

    /// <summary>
    /// Simulation API reference. Used only for reading metrics.
    /// The actual simulation is driven by RqSimUI or RqSimConsole.
    /// </summary>
    private RqSimForms.Forms.Interfaces.RqSimEngineApi _simApi = new();

    /// <summary>
    /// Lifecycle manager for console process IPC (shared memory, pipe commands).
    /// </summary>
    private readonly LifeCycleManager _lifeCycleManager = new();

    // ============================================================
    // SIMULATION STATE FLAGS
    // ============================================================

    private bool _isExternalSimulation;
    private bool _isModernRunning;
    private bool _isConsoleBound;
    private bool _hasApiConnection;

    // ============================================================
    // EXTERNAL DATA BUFFERS
    // ============================================================

    private readonly DataReader _externalReader = new();
    private RenderNode[] _externalNodesBuffer = Array.Empty<RenderNode>();
    private DateTime _lastExternalNoDataLogUtc = DateTime.MinValue;

    /// <summary>
    /// Local time-series accumulator for IPC (Console) mode.
    /// Collects snapshots from SharedMemory on each timer tick so charts
    /// can render historical data without in-process MetricsDispatcher.
    /// </summary>
    private readonly IpcTimeSeries _ipcTimeSeries = new();

    // ============================================================
    // UI TIMERS
    // ============================================================

    private System.Windows.Forms.Timer? _uiUpdateTimer;

    // ============================================================
    // SHORTCUT: Simulation engine from API (read-only)
    // ============================================================

    private SimulationEngine? _simulationEngine => _simApi.SimulationEngine;

    // ============================================================
    // SPECTRUM LOGGING (compatibility)
    // ============================================================

    private bool _spectrumLoggingEnabled;

    // ============================================================
    // PUBLIC API
    // ============================================================

    /// <summary>
    /// Sets the simulation API reference for read-only telemetry access.
    /// Called by RqSimUI when launching this form.
    /// </summary>
    public void SetTelemetryApi(RqSimForms.Forms.Interfaces.RqSimEngineApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _simApi = api;
        _hasApiConnection = true;
        _isModernRunning = api.IsModernRunning;
    }

    /// <summary>
    /// Notifies the TelemetryForm that the host is in console (IPC) mode.
    /// When set, chart rendering uses <see cref="IpcTimeSeries"/> instead of
    /// dispatcher decimated arrays which are not populated in console mode.
    /// </summary>
    public void SetConsoleMode(bool isConsoleMode)
    {
        _isExternalSimulation = isConsoleMode;
    }

    /// <summary>
    /// Clears accumulated IPC time-series data, OTel snapshots, and resets dashboard labels.
    /// Call when the simulation is terminated so charts start fresh on next run.
    /// </summary>
    public void ResetForNewSession()
    {
        _ipcTimeSeries.Clear();
        _metricSnapshots.Clear();

        // Reset status labels so UI immediately reflects stopped state
        _valStatus.Text = "Stopped";
        _valStatus.ForeColor = SystemColors.ControlText;

        // Invalidate charts so they repaint (now empty)
        _panelExcitedChart?.Invalidate();
        _panelHeavyChart?.Invalidate();
        _panelClusterChart?.Invalidate();
        _panelEnergyChart?.Invalidate();
        _chartsTabExcited?.Invalidate();
        _chartsTabHeavy?.Invalidate();
        _chartsTabCluster?.Invalidate();
        _chartsTabEnergy?.Invalidate();
    }

    /// <summary>
    /// Ensures the UI update timer is running. Call when simulation state changes
    /// (e.g., local sim started or console bound) so telemetry polling resumes.
    /// </summary>
    public void EnsureTimerRunning()
    {
        _isModernRunning = _simApi.IsModernRunning;

        if (_uiUpdateTimer is null)
        {
            StartUiUpdateTimer();
            return;
        }

        if (!_uiUpdateTimer.Enabled)
        {
            _uiUpdateTimer.Start();
        }
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private static void SetNumericValueSafe(NumericUpDown? control, decimal value)
    {
        if (control is null) return;

        decimal originalMinimum = control.Minimum;
        decimal originalMaximum = control.Maximum;

        try
        {
            if (control.DecimalPlaces >= 0)
            {
                value = Math.Round(value, control.DecimalPlaces, MidpointRounding.AwayFromZero);
            }

            control.Minimum = decimal.MinValue;
            control.Maximum = decimal.MaxValue;
            control.Value = value;
        }
        catch (ArgumentOutOfRangeException)
        {
            control.Value = originalMinimum;
        }
        finally
        {
            control.Minimum = originalMinimum;
            control.Maximum = originalMaximum;

            decimal clamped = Math.Clamp(control.Value, control.Minimum, control.Maximum);
            if (control.Value != clamped)
                control.Value = clamped;
        }
    }
}
