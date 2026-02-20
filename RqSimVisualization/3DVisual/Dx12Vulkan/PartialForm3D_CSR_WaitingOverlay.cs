using System.Numerics;
using ImGuiNET;
using RqSimRenderingEngine.Rendering.Backend.DX12;
using Vortice.Mathematics;

namespace RqSimVisualization;

/// <summary>
/// Partial class for CSR rendering waiting state overlay.
/// Part of Phase 4 of uni-pipeline implementation.
/// </summary>
public partial class RqSimVisualizationForm
{
    /// <summary>
    /// Draws the "waiting for simulation data" overlay if no data is available.
    /// Called from RenderCsrVisualizationFrame.
    /// </summary>
    private bool DrawCsrWaitingOverlayIfNeeded()
    {
        // Check if we have valid data
        bool hasData = _csrNodeCount > 0 && _csrNodeX is not null;
        
        // Check simulation state
        bool isSimActive = _isModernRunning || _isExternalSimulation;
        bool isTerminated = !isSimActive && _simApi?.SimulationComplete == true;
        
        // Show overlay if no data and either terminated or not yet started
        _csrIsWaitingForData = !hasData;
        
        if (!_csrIsWaitingForData)
        {
            // Hide the waiting label if we have data
            UpdateCsrWaitingLabelVisibility(false);
            return false;
        }
        
        // Show waiting overlay
        UpdateCsrWaitingLabelVisibility(true);
        
        // Draw ImGui waiting message as overlay
        DrawCsrWaitingImGuiOverlay(isTerminated);
        
        return true; // Indicates we drew the waiting overlay
    }

    /// <summary>
    /// Updates the visibility of the waiting label (WinForms control).
    /// </summary>
    private void UpdateCsrWaitingLabelVisibility(bool visible)
    {
        if (_csrWaitingLabel is null) return;
        
        if (_csrWaitingLabel.InvokeRequired)
        {
            _csrWaitingLabel.BeginInvoke(() => _csrWaitingLabel.Visible = visible);
        }
        else
        {
            _csrWaitingLabel.Visible = visible;
        }
    }

    /// <summary>
    /// Draws an ImGui overlay for the waiting state.
    /// </summary>
    private void DrawCsrWaitingImGuiOverlay(bool isTerminated)
    {
        try
        {
            var drawList = ImGui.GetForegroundDrawList();
            int panelW = _csrRenderPanel?.Width ?? 800;
            int panelH = _csrRenderPanel?.Height ?? 600;
            float cx = panelW / 2f;
            float cy = panelH / 2f;
            
            string message = isTerminated 
                ? "Simulation terminated\nWaiting for new data..." 
                : "Waiting for simulation data...";
            
            var textSize = ImGui.CalcTextSize(message);
            var textPos = new Vector2(cx - textSize.X / 2, cy - textSize.Y / 2);
            
            // Draw semi-transparent background box
            var boxPadding = new Vector2(20, 15);
            var boxMin = textPos - boxPadding;
            var boxMax = textPos + textSize + boxPadding;
            uint boxColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.15f, 0.85f));
            uint borderColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.4f, 1f));
            
            drawList.AddRectFilled(boxMin, boxMax, boxColor, 8f);
            drawList.AddRect(boxMin, boxMax, borderColor, 8f, ImDrawFlags.None, 2f);
            
            // Draw text
            uint textColor = isTerminated
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.6f, 0.3f, 1f)) // Orange for terminated
                : ImGui.ColorConvertFloat4ToU32(new Vector4(0.6f, 0.6f, 0.6f, 1f)); // Gray for waiting
            
            drawList.AddText(textPos, textColor, message);
            
            // Draw pulsing indicator
            float pulse = (float)(Math.Sin(DateTime.Now.Millisecond / 500.0 * Math.PI) * 0.5 + 0.5);
            uint indicatorColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f + pulse * 0.3f, 0.4f + pulse * 0.2f, 0.6f + pulse * 0.2f, 1f));
            
            float indicatorRadius = 6f + pulse * 2f;
            var indicatorPos = new Vector2(cx, cy + textSize.Y / 2 + 25);
            drawList.AddCircleFilled(indicatorPos, indicatorRadius, indicatorColor);
        }
        catch
        {
            // ImGui not ready - silently ignore
        }
    }

    /// <summary>
    /// Checks if simulation data is available for rendering.
    /// </summary>
    public bool HasCsrSimulationData()
    {
        return _csrNodeCount > 0 && _csrNodeX is not null;
    }

    /// <summary>
    /// Gets the current simulation status for the CSR window.
    /// </summary>
    public string GetCsrSimulationStatus()
    {
        if (_csrIsWaitingForData)
        {
            return _isModernRunning || _isExternalSimulation
                ? "Running (no render data yet)"
                : "Stopped (no data)";
        }
        
        return _isModernRunning || _isExternalSimulation
            ? $"Running ({_csrNodeCount} nodes)"
            : $"Stopped ({_csrNodeCount} nodes cached)";
    }
}
