using RqSimForms.ProcessesDispatcher.Contracts;
using RqSimForms.ProcessesDispatcher.Managers;
using RqSimUI.FormSimAPI.Interfaces;
using RQSimulation;

namespace RqSimVisualization;

/// <summary>
/// Fields, enums, and stub methods required by visualization partial classes.
/// RqSimVisualizationForm is a standalone visualization window launched from RqSimUI.
/// It does NOT own RQGraph or SimulationEngine — data arrives via:
///   1. Shared memory from RqSimConsole (ProcessesDispatcher)
///   2. Public API called by RqSimUI when launched from the main form
/// </summary>
public partial class RqSimVisualizationForm
{
    // ============================================================
    // ENUMS
    // ============================================================

    private enum CsrVisualizationMode
    {
        QuantumPhase = 0,
        ProbabilityDensity = 1,
        Curvature = 2,
        GravityHeatmap = 3,
        NetworkTopology = 4,
        Clusters = 5
    }

    // ============================================================
    // SIMULATION API (read-only consumer — does NOT own the simulation)
    // ============================================================

    /// <summary>
    /// Simulation API reference. Used only for reading state (metrics, graph data).
    /// The actual simulation is driven by RqSimUI or RqSimConsole.
    /// When embedded in RqSimUI, this is replaced by the main form's instance via SetSimulationApi.
    /// </summary>
    private RqSimForms.Forms.Interfaces.RqSimEngineApi _simApi = new();

    /// <summary>
    /// Sets the simulation API reference for local mode visualization.
    /// Called by RqSimUI when embedding this form, so the visualization can
    /// read graph data from the running local simulation.
    /// </summary>
    public void SetSimulationApi(RqSimForms.Forms.Interfaces.RqSimEngineApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _simApi = api;
        _hasApiConnection = true;
    }

    /// <summary>
    /// Notifies the visualization form that a local simulation has started.
    /// Called by RqSimUI when the user clicks "Run Simulation".
    /// </summary>
    public void NotifySimulationStarted()
    {
        _isModernRunning = true;
        int nodeCount = _simApi.SimulationEngine?.Graph?.N ?? 0;
        UpdateConnectionStatus($"Local simulation running — {nodeCount} nodes", StatusKind.Connected);
    }

    /// <summary>
    /// Notifies the visualization form that a local simulation has stopped.
    /// </summary>
    public void NotifySimulationStopped()
    {
        _isModernRunning = false;
        UpdateConnectionStatus("Simulation stopped. Ready.", StatusKind.NoSimulation);
    }

    /// <summary>
    /// Notifies the visualization form that a console session has been bound.
    /// </summary>
    public void SetConsoleMode(bool isConsoleMode)
    {
        _isExternalSimulation = isConsoleMode;
        _isConsoleBound = isConsoleMode;
        if (isConsoleMode)
        {
            UpdateConnectionStatus("Console session bound", StatusKind.Connected);
            StartUiUpdateTimer();
        }
    }

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
    // VISUALIZATION STATE
    // ============================================================

    private CsrVisualizationMode _csrVisMode = CsrVisualizationMode.QuantumPhase;
    private ConsoleBuffer? _consoleBuffer;

    // ============================================================
    // EDGE THRESHOLD
    // ============================================================

    private double _edgeThresholdValue = 0.5;

    // ============================================================
    // EXTERNAL DATA BUFFERS
    // ============================================================

    private readonly RqSimForms.ProcessesDispatcher.IPC.DataReader _externalReader = new();
    private RenderNode[] _externalNodesBuffer = Array.Empty<RenderNode>();

    // ============================================================
    // SHORTCUT: Simulation engine from API (read-only)
    // ============================================================

    private SimulationEngine? _simulationEngine => _simApi.SimulationEngine;

    // ============================================================
    // DESIGNER CONTROL ALIASES
    // Maps names used by migrated partial classes to actual designer controls.
    // ============================================================

    /// <summary>Tab control alias for migrated code referencing tabControl_Main.</summary>
    private TabControl tabControl_Main => tabControl_MainVisualizationFormTab;

    /// <summary>GDI+ tab alias.</summary>
    private TabPage tabPage_3DVisual => tabPage_GDI;

    /// <summary>DX12/CSR tab alias.</summary>
    private TabPage tabPage_3DVisualCSR => tabPage_DX12;

    // ============================================================
    // STUB CONTROLS (not present on this form — null-safe)
    // These controls exist on RqSimUI's Form_Main, not here.
    // Stubs prevent compilation errors in migrated code.
    // ============================================================

    private CheckBox? checkBox_ScienceSimMode;
    private CheckBox? checkBox_EnableGPU;
    private CheckBox? checkBox_AutoScrollSysConsole;
    private ComboBox? comboBox_GPUIndex;
    private TextBox? textBox_SimConsole;
    private Button? button_RunModernSim;
    private TrackBar? _trkEdgeThreshold;
    private Label? _lblEdgeThresholdValue;

    /// <summary>Standalone 3D form reference (optional, created on demand).</summary>
    private RqSim3DForm.Form_Rsim3DForm? _standalone3DForm;

    // ============================================================
    // STUB METHODS
    // Minimal implementations for methods referenced by migrated code.
    // Full implementations will be added as features are wired up.
    // ============================================================

    /// <summary>
    /// Appends text to the system console output.
    /// In the visualization form, this writes to the console buffer or Trace.
    /// </summary>
    private void AppendSysConsole(string text)
    {
        _consoleBuffer?.Append(text);
        System.Diagnostics.Trace.WriteLine(text);
    }

    // ============================================================
    // UI UPDATE TIMER
    // ============================================================

    private System.Windows.Forms.Timer? _uiUpdateTimer;

    /// <summary>Starts the UI update timer for periodic shared memory reads.</summary>
    private void StartUiUpdateTimer()
    {
        if (_uiUpdateTimer is not null)
            return;

        _uiUpdateTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
        _uiUpdateTimer.Start();
    }

    private void UiUpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isExternalSimulation)
            return;

        var nodes = _lifeCycleManager.GetExternalRenderNodes();
        if (nodes is not null && nodes.Length > 0)
        {
            _externalNodesBuffer = nodes;

            // Update connection status with live node count (throttled)
            var state = _lifeCycleManager.TryGetExternalSimulationState();
            if (state is not null && _lblConnectionStatus is not null && !_lblConnectionStatus.IsDisposed)
            {
                string statusText = state.Value.Status switch
                {
                    SimulationStatus.Running => $"Connected — {nodes.Length} nodes, step {state.Value.Iteration}",
                    SimulationStatus.Paused => $"Connected — {nodes.Length} nodes, paused",
                    _ => $"Connected — {nodes.Length} nodes"
                };
                UpdateConnectionStatus(statusText, StatusKind.Connected);
            }
        }
    }

    /// <summary>Updates the console bind button visual state.</summary>
    private void UpdateConsoleBindButton(bool bound)
    {
        _isConsoleBound = bound;
    }

    /// <summary>Attaches shared memory for reading simulation state.</summary>
    private void TryAttachConsoleSharedMemory(bool force = false)
    {
        if (force || !_externalReader.IsConnected)
        {
            _externalReader.TryConnect();
        }
    }

    /// <summary>Auto-starts console simulation when attached to a stopped process.</summary>
    private async Task AutoStartConsoleSimulationAsync()
    {
        try
        {
            await _lifeCycleManager.Ipc.SendStartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppendSysConsole($"[AutoStart] Failed: {ex.Message}\n");
        }
    }

    /// <summary>Updates the main form status bar.</summary>
    private void UpdateMainFormStatusBar()
    {
        // Status bar update — will be implemented with actual status strip
    }
}
