using System.Numerics;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using ImGuiNET;
using Vortice.Mathematics;

namespace RqSim3DForm;

public partial class Form_Rsim3DForm
{
    /// <summary>
    /// Indicates whether the simulation is currently running.
    /// Used to control animation/physics updates in the visualization.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsSimulationRunning { get; set; }

    /// <summary>
    /// Reentry guard to prevent overlapping render ticks.
    /// </summary>
    private bool _isRendering;

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        // Reentry guard: skip tick if previous frame is still rendering
        if (_isRendering) return;

        if (_renderHost is null || _dx12Host?.IsDeviceLost == true)
            return;

        _isRendering = true;
        try
        {
            UpdateGraphData();

            // Apply manifold embedding if enabled (matching GDI+ behavior)
            // Unlike previous version, we apply manifold even when simulation is stopped
            // to maintain the "breathing" dynamics that users expect
            if (_enableManifoldEmbedding)
            {
                ApplyManifoldEmbedding();
            }

            // Update target metrics only if overlay is shown (matching CSR behavior)
            if (_showTargetOverlay && _nodeCount > 0 && _nodeX != null && _nodeY != null && _nodeZ != null)
            {
                var data = new GraphRenderData(_nodeX, _nodeY, _nodeZ, _nodeStates, _edges, _nodeCount, _spectralDim);
                UpdateTargetMetrics(data);
            }

            _renderHost.BeginFrame();
            _dx12Host?.Clear(new Color4(0.02f, 0.02f, 0.05f, 1f));

            DrawGraph();
            DrawImGuiOverlay();

            _renderHost.EndFrame();

            UpdateFps();
        }
        catch (Exception ex)
        {
            if ((DateTime.Now - _lastDebugLog).TotalSeconds > 1)
            {
                System.Diagnostics.Debug.WriteLine($"[3DForm] Render error: {ex.Message}");
                _lastDebugLog = DateTime.Now;
            }
        }
        finally
        {
            _isRendering = false;
        }
    }

    private void UpdateGraphData()
    {
        if (_getGraphData is null)
            return;

        try
        {
            var data = _getGraphData();

            if (data.NodeCount <= 0 || data.NodeX is null || data.NodeY is null || data.NodeZ is null)
            {
                _nodeX = null;
                _nodeY = null;
                _nodeZ = null;
                _nodeStates = null;
                _edges = null;
                _nodeCount = 0;
                _spectralDim = double.NaN;
                return;
            }

            // Validate lengths to avoid out-of-range usage when provider returns inconsistent snapshots
            int n = data.NodeCount;
            if (data.NodeX.Length < n || data.NodeY.Length < n || data.NodeZ.Length < n)
            {
                throw new InvalidOperationException($"GraphRenderData arrays shorter than NodeCount={n}. X={data.NodeX.Length}, Y={data.NodeY.Length}, Z={data.NodeZ.Length}");
            }

            _nodeX = data.NodeX;
            _nodeY = data.NodeY;
            _nodeZ = data.NodeZ;
            _nodeStates = data.States;
            _edges = data.Edges;
            _nodeCount = n;
            _spectralDim = data.SpectralDimension;
        }
        catch (Exception ex)
        {
            if ((DateTime.Now - _lastDebugLog).TotalSeconds > 1)
            {
                System.Diagnostics.Debug.WriteLine($"[3DForm] UpdateGraphData error: {ex.GetType().Name}: {ex.Message}");
                _lastDebugLog = DateTime.Now;
            }

            // Keep last valid data to avoid flicker.
        }
    }

    private void DrawGraph()
    {
        if (_nodeCount == 0 || _nodeX is null) 
        {
            DrawNoDataMessage();
            return;
        }

        // Switch between rendering modes
        if (_renderMode == RenderMode3D.Gpu3D)
        {
            RenderSceneGpu3D();
            return;
        }

        // === ImGui 2D Mode (CPU-based legacy rendering) ===
        var drawList = ImGui.GetForegroundDrawList();
        int panelW = _renderPanel?.Width ?? 800;
        int panelH = _renderPanel?.Height ?? 600;
        float cx = panelW / 2f;
        float cy = panelH / 2f;

        // Camera rotation
        float cosYaw = MathF.Cos(_cameraYaw);
        float sinYaw = MathF.Sin(_cameraYaw);
        float cosPitch = MathF.Cos(_cameraPitch);
        float sinPitch = MathF.Sin(_cameraPitch);

        int count = Math.Min(_nodeCount, 10000);

        // First pass: transform and find bounds
        Span<Vector2> screenPos = stackalloc Vector2[count];
        Span<float> depths = stackalloc float[count];
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float sumX = 0, sumY = 0;

        for (int i = 0; i < count; i++)
        {
            float x = _nodeX[i];
            float y = _nodeY![i];
            float z = _nodeZ![i];

            // Rotate around Y (yaw)
            float rx = x * cosYaw - z * sinYaw;
            float rz = x * sinYaw + z * cosYaw;

            // Rotate around X (pitch)
            float ry = y * cosPitch - rz * sinPitch;
            float rz2 = y * sinPitch + rz * cosPitch;

            screenPos[i] = new Vector2(rx, ry);
            depths[i] = rz2;

            minX = Math.Min(minX, rx);
            maxX = Math.Max(maxX, rx);
            minY = Math.Min(minY, ry);
            maxY = Math.Max(maxY, ry);
            sumX += rx;
            sumY += ry;
        }

        // Calculate auto-scale
        float dataRange = Math.Max(maxX - minX, maxY - minY);
        if (dataRange < 0.001f) dataRange = 1f;

        float availableSize = Math.Min(panelW, panelH) * 0.8f;
        float autoScale = availableSize / dataRange;
        float zoomFactor = 100f / Math.Max(_cameraDistance, 1f);
        float scale = autoScale * zoomFactor;

        float dataCenterX = sumX / count;
        float dataCenterY = sumY / count;

        // Convert to final screen positions
        for (int i = 0; i < count; i++)
        {
            float sx = cx + (screenPos[i].X - dataCenterX) * scale;
            float sy = cy - (screenPos[i].Y - dataCenterY) * scale;
            screenPos[i] = new Vector2(sx, sy);
        }

        // Draw edges first (behind nodes) - use mode-based styling
        // NOTE: ImGui 2D mode is CPU-bound; limit edges to avoid <10 FPS
        if (_showEdges && _edges is not null)
        {
            int edgeCount = _edges.Count;
            int maxEdgesForCpu = 800; // Beyond this, FPS drops significantly
            int edgeStep = edgeCount > maxEdgesForCpu ? Math.Max(1, edgeCount / maxEdgesForCpu) : 1;
            int drawnEdges = 0;

            for (int idx = 0; idx < edgeCount; idx += edgeStep)
            {
                var (u, v, w) = _edges[idx];
                if (u < count && v < count && w >= _edgeWeightThreshold)
                {
                    var (edgeColor, thickness) = GetEdgeStyle(u, v, w);
                    uint col = ImGui.ColorConvertFloat4ToU32(edgeColor);
                    drawList.AddLine(screenPos[u], screenPos[v], col, thickness);
                    drawnEdges++;
                }
            }

            // Log warning once if many edges are being sampled
            if (edgeStep > 1 && (DateTime.Now - _lastDebugLog).TotalSeconds > 5)
            {
                System.Diagnostics.Debug.WriteLine($"[3DForm] ImGui 2D: Sampling edges ({drawnEdges}/{edgeCount}). Switch to GPU 3D mode for full detail.");
                _lastDebugLog = DateTime.Now;
            }
        }

        // Draw nodes as circles - use mode-based coloring
        for (int i = 0; i < count; i++)
        {
            Vector4 color = GetNodeColor(i);
            float depthFactor = 1f - Math.Clamp(depths[i] / 10f, -0.5f, 0.5f);
            float radius = 1.3f + depthFactor * 1f; // Reduced radius (matching CSR)

            uint col = ImGui.ColorConvertFloat4ToU32(color);
            drawList.AddCircleFilled(screenPos[i], radius, col);
        }
    }

    private void DrawNoDataMessage()
    {
        var drawList = ImGui.GetForegroundDrawList();
        int panelW = _renderPanel?.Width ?? 800;
        int panelH = _renderPanel?.Height ?? 600;

        string msg = "Waiting for simulation data...";
        var textSize = ImGui.CalcTextSize(msg);
        var pos = new Vector2((panelW - textSize.X) / 2, (panelH - textSize.Y) / 2);

        drawList.AddText(pos, ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 1f)), msg);
    }

    private void DrawImGuiOverlay()
    {
        // Stats overlay
        ImGui.SetNextWindowPos(new Vector2(230, 10), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.7f);
        if (ImGui.Begin("##Stats", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextColored(new Vector4(0.8f, 0.9f, 1f, 1f), $"Nodes: {_nodeCount}");
            ImGui.TextColored(new Vector4(0.8f, 0.9f, 1f, 1f), $"Edges: {_edges?.Count ?? 0}");
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), $"d_S: {_spectralDim:F3}");
            ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), $"FPS: {_fps:F0}");
            
            if (_enableManifoldEmbedding)
            {
                ImGui.TextColored(new Vector4(0.3f, 1f, 1f, 1f), "Manifold: ON");
            }
        }
        ImGui.End();

        // Controls hint
        ImGui.SetNextWindowPos(new Vector2(230, (float)((_renderPanel?.Height ?? 600) - 35)), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.5f);
        if (ImGui.Begin("##Controls", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "LMB: Rotate | Wheel: Zoom");
        }
        ImGui.End();
    }

    private void UpdateFps()
    {
        _frameCount++;
        if ((DateTime.Now - _fpsUpdateTime).TotalSeconds >= 1.0)
        {
            _fps = _frameCount / (float)(DateTime.Now - _fpsUpdateTime).TotalSeconds;
            _frameCount = 0;
            _fpsUpdateTime = DateTime.Now;

            UpdateStatsLabel();
        }
    }

    private void UpdateStatsLabel()
    {
        if (_statsLabel is null) return;

        string manifoldStatus = _enableManifoldEmbedding ? " [Manifold]" : "";
        string stats = $"Nodes: {_nodeCount}\n" +
                       $"Edges: {_edges?.Count ?? 0}\n" +
                       $"d_S: {_spectralDim:F3}\n" +
                       $"FPS: {_fps:F0}\n" +
                       $"Mode: {_visMode}{manifoldStatus}";

        if (_statsLabel.InvokeRequired)
            _statsLabel.Invoke(() => _statsLabel.Text = stats);
        else
            _statsLabel.Text = stats;
    }

    /// <summary>
    /// Clears cached graph data so the renderer shows "Waiting for simulation data...".
    /// </summary>
    public void ClearData()
    {
        _nodeCount = 0;
        _nodeX = null;
        _nodeY = null;
        _nodeZ = null;
        _nodeStates = null;
        _edges = null;
    }
}

