using RqSimForms.ProcessesDispatcher.Contracts;

namespace RqSimVisualization;

public partial class RqSimVisualizationForm : Form
{
    public RqSimVisualizationForm()
    {
        InitializeComponent();
        Shown += RqSimVisualizationForm_Shown;
    }

    private async void RqSimVisualizationForm_Shown(object? sender, EventArgs e)
    {
        try
        {
            // Always initialize visualization panels (they show "waiting for data" internally)
            Initialize3DVisual();
            Initialize3DVisualCSR();

            // When embedded in RqSimUI, API is already injected — skip external discovery
            if (_hasApiConnection)
            {
                UpdateConnectionStatus("Connected to RqSimUI", StatusKind.Connected);
                StartUiUpdateTimer();
                return;
            }

            UpdateConnectionStatus("Searching for simulation process...", StatusKind.Searching);

            // Try to discover a running RqSimConsole process
            await _lifeCycleManager.OnFormLoadAsync().ConfigureAwait(true);

            // Poll for external simulation availability (up to 5 seconds)
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
                SyncToExternalSimulation(externalState.Value);
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
                    "No simulation detected. Launch RqSimConsole or start simulation from RqSimUI.",
                    StatusKind.NoSimulation);
            }
        }
        catch (Exception ex)
        {
            UpdateConnectionStatus($"Error: {ex.Message}", StatusKind.Error);
            System.Diagnostics.Trace.WriteLine($"[RqSimVisualizationForm] Shown error: {ex}");
        }
    }

    /// <summary>
    /// Resets visualization state (caches, snapshots, embedding).
    /// Call this when simulation is terminated or restarted.
    /// </summary>
    public void ResetVisualization()
    {
        // Reset Manifold embedding state (velocities, positions)
        ResetManifoldEmbedding();

        // Clear last snapshot
        _lastSnapshot = null;

        // Clear CSR/DX12 cached graph data and renderer buffers
        ClearCsrVisualizationData();

        // Reset stats label
        if (_lblStats != null && !_lblStats.IsDisposed)
        {
            if (_lblStats.InvokeRequired)
                _lblStats.Invoke(() => _lblStats.Text = "Waiting for Sim...");
            else
                _lblStats.Text = "Waiting for Sim...";
        }

        // Force repaint
        _panel3D?.Invalidate();
    }

    private enum StatusKind { Searching, Connected, NoSimulation, Error }

    private void UpdateConnectionStatus(string text, StatusKind kind)
    {
        if (_lblConnectionStatus is null || _lblConnectionStatus.IsDisposed)
            return;

        _lblConnectionStatus.Text = kind switch
        {
            StatusKind.Searching => $"\u23F3 {text}",
            StatusKind.Connected => $"\u2705 {text}",
            StatusKind.NoSimulation => $"\u26A0 {text}",
            StatusKind.Error => $"\u274C {text}",
            _ => text
        };

        _lblConnectionStatus.ForeColor = kind switch
        {
            StatusKind.Connected => Color.LimeGreen,
            StatusKind.NoSimulation => Color.Orange,
            StatusKind.Error => Color.OrangeRed,
            _ => Color.White
        };
    }
}
