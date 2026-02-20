using System;
using System.ComponentModel;
using System.Numerics;
using ImGuiNET;
using RqSimGraphEngine;
using RqSimRenderingEngine.Rendering.Backend.DX12;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using RqSimRenderingEngine.Rendering.Data;
using RqSimRenderingEngine.Rendering.Input;
using RqSimRenderingEngine.Abstractions;
using RqSimUI.Rendering.Data;
using RQSimulation;
using RQSimulation.GPUCompressedSparseRow.Data;
using RQSimulation.GPUCompressedSparseRow.Unified;
using ComputeSharp;

namespace RqSimVisualization;

/// <summary>
/// Partial class for DX12 render data feeding.
/// Converts graph state to DX12 instance/edge buffers using RenderDataExtractor.
/// Supports both RQGraph (dense) and CsrTopology (sparse) data sources.
/// </summary>
public partial class RqSimVisualizationForm
{
    // DX12 data extraction service
    private RenderDataExtractor? _renderDataExtractor;

    // Data providers for different sources
    private RQGraphRenderDataProvider? _rqGraphProvider;
    private CsrRenderDataProvider? _csrProvider;
    private IGraphRenderDataProvider? _activeDataProvider;

    // Unified input adapter for DX12
    private InputSnapshotAdapter? _inputAdapter;

    // Camera state for DX12
    private Matrix4x4 _dx12ViewMatrix = Matrix4x4.Identity;
    private Matrix4x4 _dx12ProjMatrix = Matrix4x4.Identity;

    // ImGui frame timing
    private DateTime _dx12LastFrameTime = DateTime.Now;
    private bool _dx12ImGuiEnabled = true;
    private bool _dx12ShowDemoWindow = false;
    private bool _dx12ShowMetricsWindow = false;

    // CSR data cache
    private CsrTopology? _cachedCsrTopology;
    private CsrUnifiedEngine? _cachedCsrEngine;
    private Vector3[]? _csrNodePositions;
    private double[]? _csrCurvatures;
    private double[]? _csrMasses;

    // Mouse position for pan tracking
    private Vector2? _lastDx12MousePos;

    // DX12 camera state (Veldrid camera state was migrated out of RqSimUI)
    private Vector2 _dx12CameraCenter = Vector2.Zero;
    private float _dx12Zoom = 10f;

    /// <summary>
    /// Whether ImGui overlay is enabled for DX12 rendering.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dx12ImGuiEnabled
    {
        get => _dx12ImGuiEnabled;
        set
        {
            _dx12ImGuiEnabled = value;
            if (_unifiedRenderHost is Dx12RenderHost dx12Host)
            {
                dx12Host.ImGuiEnabled = value;
            }
        }
    }

    /// <summary>
    /// Initialize DX12-specific components.
    /// </summary>
    private void InitializeDx12Components()
    {
        _renderDataExtractor ??= new RenderDataExtractor();
        _inputAdapter ??= new InputSnapshotAdapter();

        _rqGraphProvider ??= new RQGraphRenderDataProvider();
        _csrProvider ??= new CsrRenderDataProvider();

        // Default to RQGraph provider
        _activeDataProvider = _rqGraphProvider;
    }

    /// <summary>
    /// Set up the DX12 data provider for CSR mode.
    /// </summary>
    public void SetCsrDataSource(CsrUnifiedEngine? engine, CsrTopology? topology)
    {
        _cachedCsrEngine = engine;
        _cachedCsrTopology = topology;

        if (topology is not null && _csrProvider is not null)
        {
            _csrProvider.SetTopology(topology);
            _activeDataProvider = _csrProvider;
        }
        else
        {
            _activeDataProvider = _rqGraphProvider;
        }
    }

    /// <summary>
    /// Set 3D node positions for CSR rendering.
    /// </summary>
    public void SetCsrNodePositions(Vector3[]? positions)
    {
        _csrNodePositions = positions;
        _csrProvider?.SetPositions(positions);
        _renderDataExtractor?.SetNodePositions3D(positions);
    }

    /// <summary>
    /// Set curvature data for CSR node coloring.
    /// </summary>
    public void SetCsrCurvatures(double[]? curvatures)
    {
        _csrCurvatures = curvatures;
        _csrProvider?.SetCurvatures(curvatures);
    }

    /// <summary>
    /// Set mass data for CSR node sizing.
    /// </summary>
    public void SetCsrMasses(double[]? masses)
    {
        _csrMasses = masses;
        _csrProvider?.SetMasses(masses);
    }

    /// <summary>
    /// Updates DX12 render data from the current graph state using RenderDataExtractor.
    /// Call this before rendering a DX12 frame.
    /// </summary>
    private void UpdateDx12RenderData()
    {
        // Try CSR provider first if active
        if (_activeDataProvider == _csrProvider && _cachedCsrTopology is not null)
        {
            UpdateCsrRenderData();
            return;
        }

        // Fall back to RQGraph
        var graph = _simulationEngine?.Graph;
        if (graph is null)
            return;

        // Prefer provider path when available
        _rqGraphProvider?.SetGraph(graph);
        if (_activeDataProvider == _rqGraphProvider && _rqGraphProvider is not null)
        {
            _rqGraphProvider.Extract();
            return;
        }

        _renderDataExtractor ??= new RenderDataExtractor();
        _renderDataExtractor.Extract(graph);
    }

    /// <summary>
    /// Update render data from CSR topology.
    /// </summary>
    private void UpdateCsrRenderData()
    {
        if (_csrProvider is null || _cachedCsrTopology is null)
            return;

        // Generate layout positions if not provided
        if (_csrNodePositions is null)
        {
            GenerateCsrLayoutPositions();
        }

        // Extract CPU-side default data into provider (ensures capacity)
        _csrProvider.Extract();

        // If we have a CSR unified engine with GPU render buffer, try to use it
        try
        {
            if (_cachedCsrEngine is not null)
            {
                var gpuBuf = _cachedCsrEngine.GetRenderBufferInterop();
                if (gpuBuf is not null)
                {
                    int n = Math.Min(_cachedCsrTopology.NodeCount, gpuBuf.Length);
                    var temp = new RQSimulation.GPUOptimized.Rendering.RenderNodeVertex[n];

                    // Copy GPU buffer to temp array (ComputeSharp ReadWriteBuffer<T>.CopyTo)
                    gpuBuf.CopyTo(temp);

                    // Map RenderNodeVertex -> Dx12NodeInstance (overwrite extractor output)
                    var instances = _csrProvider.NodeInstances;
                    int count = Math.Min(instances.Length, n);
                    for (int i = 0; i < count; i++)
                    {
                        var v = temp[i];
                        instances[i] = new Dx12NodeInstance(
                            new System.Numerics.Vector3(v.X, v.Y, v.Z),
                            v.Size,
                            new System.Numerics.Vector4(v.R, v.G, v.B, v.A));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DX12 CSR] GPU vertex copy failed: {ex.Message}");
            // fallback: provider already has CPU instances from Extract()
        }
    }

    /// <summary>
    /// Generate 3D layout positions for CSR nodes.
    /// </summary>
    private void GenerateCsrLayoutPositions()
    {
        if (_cachedCsrTopology is null)
            return;

        int nodeCount = _cachedCsrTopology.NodeCount;
        if (_csrNodePositions is null || _csrNodePositions.Length != nodeCount)
        {
            _csrNodePositions = new Vector3[nodeCount];
        }

        float radius = MathF.Sqrt(nodeCount) * 0.5f;
        for (int i = 0; i < nodeCount; i++)
        {
            float angle = 2f * MathF.PI * i / nodeCount;
            _csrNodePositions[i] = new Vector3(
                radius * MathF.Cos(angle),
                radius * MathF.Sin(angle),
                0f);
        }

        _csrProvider?.SetPositions(_csrNodePositions);
        _renderDataExtractor?.SetNodePositions3D(_csrNodePositions);
    }

    /// <summary>
    /// Update DX12 camera matrices based on current zoom/pan state.
    /// Uses the same camera parameters as Veldrid backend for consistency.
    /// </summary>
    private void UpdateDx12Camera(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        float aspect = (float)width / height;

        Vector3 cameraPos = new(_dx12CameraCenter.X, _dx12CameraCenter.Y, _dx12Zoom * 2f);
        Vector3 cameraTarget = new(_dx12CameraCenter.X, _dx12CameraCenter.Y, 0f);
        Vector3 up = Vector3.UnitY;

        _dx12ViewMatrix = Matrix4x4.CreateLookAt(cameraPos, cameraTarget, up);

        // Orthographic projection for 2D-like view
        float orthoSize = _dx12Zoom;
        _dx12ProjMatrix = Matrix4x4.CreateOrthographic(orthoSize * aspect, orthoSize, 0.1f, 100f);
    }


    /// <summary>
    /// Render a DX12 frame with current graph data.
    /// </summary>
    private void RenderDx12Frame()
    {
        if (_unifiedRenderHost is not Dx12RenderHost dx12Host || !dx12Host.IsInitialized)
            return;

        if (_dx12Panel is null)
            return;

        // Calculate frame delta time
        var now = DateTime.Now;
        float deltaTime = (float)(now - _dx12LastFrameTime).TotalSeconds;
        _dx12LastFrameTime = now;
        deltaTime = Math.Clamp(deltaTime, 0.001f, 0.1f);

        // Process input via unified adapter
        if (_inputAdapter is not null)
        {
            var snapshot = _inputAdapter.CreateSnapshot();
            dx12Host.UpdateInput(snapshot);

            // Process camera controls if ImGui doesn't capture input
            if (!dx12Host.WantCaptureMouse)
            {
                ProcessDx12CameraInput(snapshot);
            }

            _inputAdapter.ResetFrame();
        }

        // Update data from graph using extractor
        UpdateDx12RenderData();
        UpdateDx12Camera(_dx12Panel.Width, _dx12Panel.Height);

        // Feed extracted data to renderer - use provider if available
        if (_activeDataProvider is not null && _activeDataProvider.HasData)
        {
            dx12Host.SetNodeInstances(_activeDataProvider.NodeInstances, _activeDataProvider.NodeCount);
            dx12Host.SetEdgeVertices(_activeDataProvider.EdgeVertices, _activeDataProvider.EdgeVertexCount);
        }
        else if (_renderDataExtractor is not null)
        {
            dx12Host.SetNodeInstances(_renderDataExtractor.NodeInstances, _renderDataExtractor.NodeCount);
            dx12Host.SetEdgeVertices(_renderDataExtractor.EdgeVertices, _renderDataExtractor.EdgeVertexCount);
        }

        dx12Host.SetCameraMatrices(_dx12ViewMatrix, _dx12ProjMatrix);

        // Begin ImGui frame
        if (_dx12ImGuiEnabled)
        {
            dx12Host.ImGuiNewFrame(deltaTime);
            RenderDx12ImGuiOverlay();
        }

        // Render frame
        dx12Host.BeginFrame();
        dx12Host.Clear(new Vortice.Mathematics.Color4(0.05f, 0.05f, 0.1f, 1.0f));
        dx12Host.EndFrame();
    }

    /// <summary>
    /// Process camera input from InputSnapshot.
    /// </summary>
    private void ProcessDx12CameraInput(InputSnapshot snapshot)
    {
        // Zoom with mouse wheel
        if (MathF.Abs(snapshot.WheelDelta) > 0.001f)
        {
            float zoomFactor = 1f - snapshot.WheelDelta * 0.1f;
            _dx12Zoom = Math.Clamp(_dx12Zoom * zoomFactor, 0.1f, 100f);
        }

        // Pan with middle mouse button or right mouse button + ctrl
        bool isPanning = snapshot.MouseButtons.IsPressed(MouseButton.Middle) ||
                        (snapshot.MouseButtons.IsPressed(MouseButton.Right) &&
                         (snapshot.Modifiers & KeyModifiers.Control) != 0);

        if (isPanning && _lastDx12MousePos.HasValue)
        {
            Vector2 delta = snapshot.MousePosition - _lastDx12MousePos.Value;
            float panSpeed = _dx12Zoom * 0.002f;
            _dx12CameraCenter.X -= delta.X * panSpeed;
            _dx12CameraCenter.Y += delta.Y * panSpeed;
        }

        _lastDx12MousePos = snapshot.MousePosition;
    }

    /// <summary>
    /// Render ImGui overlay UI for DX12 mode.
    /// </summary>
    private void RenderDx12ImGuiOverlay()
    {
        if (_unifiedRenderHost is not Dx12RenderHost dx12Host)
            return;

        // Simple status overlay
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.7f);

        if (ImGui.Begin("DX12 Renderer", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGui.Text("Backend: DirectX 12");
            ImGui.Separator();

            // Show data source info
            bool usingCsr = _activeDataProvider == _csrProvider && _cachedCsrTopology is not null;
            ImGui.Text($"Mode: {(usingCsr ? "CSR (Sparse)" : "RQGraph (Dense)")}");

            if (usingCsr && _cachedCsrTopology is not null)
            {
                ImGui.Text($"Nodes: {_cachedCsrTopology.NodeCount}");
                ImGui.Text($"Edges: {_cachedCsrTopology.Nnz / 2}");
            }
            else
            {
                var graph = _simulationEngine?.Graph;
                if (graph is not null)
                {
                    ImGui.Text($"Nodes: {graph.N}");
                    ImGui.Text($"Edges: {(_renderDataExtractor?.EdgeVertexCount ?? 0) / 2}");
                }
            }

            ImGui.Separator();
            ImGui.Text($"Zoom: {_dx12Zoom:F2}");
            ImGui.Text($"Center: ({_dx12CameraCenter.X:F2}, {_dx12CameraCenter.Y:F2})");

            ImGui.Separator();

            if (ImGui.Checkbox("Demo Window", ref _dx12ShowDemoWindow))
            {
            }

            if (ImGui.Checkbox("Metrics", ref _dx12ShowMetricsWindow))
            {
            }
        }
        ImGui.End();

        // Optional windows
        if (_dx12ShowDemoWindow)
        {
            ImGui.ShowDemoWindow(ref _dx12ShowDemoWindow);
        }

        if (_dx12ShowMetricsWindow)
        {
            ImGui.ShowMetricsWindow(ref _dx12ShowMetricsWindow);
        }
    }

    /// <summary>
    /// Route mouse down to unified input adapter.
    /// </summary>
    private void HandleDx12MouseDown(System.Windows.Forms.MouseEventArgs e)
    {
        _inputAdapter?.OnMouseDown(e);
    }

    /// <summary>
    /// Route mouse up to unified input adapter.
    /// </summary>
    private void HandleDx12MouseUp(System.Windows.Forms.MouseEventArgs e)
    {
        _inputAdapter?.OnMouseUp(e);
    }

    /// <summary>
    /// Route mouse move to unified input adapter.
    /// </summary>
    private void HandleDx12MouseMove(System.Windows.Forms.MouseEventArgs e)
    {
        _inputAdapter?.OnMouseMove(e);
    }

    /// <summary>
    /// Route mouse wheel to unified input adapter.
    /// </summary>
    private void HandleDx12MouseWheel(System.Windows.Forms.MouseEventArgs e)
    {
        _inputAdapter?.OnMouseWheel(e);
    }

    /// <summary>
    /// Route key down to unified input adapter.
    /// </summary>
    private void HandleDx12KeyDown(System.Windows.Forms.KeyEventArgs e)
    {
        _inputAdapter?.OnKeyDown(e);
    }

    /// <summary>
    /// Route key up to unified input adapter.
    /// </summary>
    private void HandleDx12KeyUp(System.Windows.Forms.KeyEventArgs e)
    {
        _inputAdapter?.OnKeyUp(e);
    }

    /// <summary>
    /// Route key press to unified input adapter.
    /// </summary>
    private void HandleDx12KeyPress(System.Windows.Forms.KeyPressEventArgs e)
    {
        _inputAdapter?.OnKeyPress(e);
    }

    /// <summary>
    /// Check if ImGui wants mouse input in DX12 mode.
    /// </summary>
    private bool Dx12ImGuiWantCaptureMouse
    {
        get
        {
            if (_unifiedRenderHost is Dx12RenderHost dx12Host && _dx12ImGuiEnabled)
            {
                return dx12Host.WantCaptureMouse;
            }
            return false;
        }
    }

    /// <summary>
    /// Check if ImGui wants keyboard input in DX12 mode.
    /// </summary>
    private bool Dx12ImGuiWantCaptureKeyboard
    {
        get
        {
            if (_unifiedRenderHost is Dx12RenderHost dx12Host && _dx12ImGuiEnabled)
            {
                return dx12Host.WantCaptureKeyboard;
            }
            return false;
        }
    }

    /// <summary>
    /// Checks if the unified render host is using DX12 backend.
    /// </summary>
    private bool IsUsingDx12Backend => _unifiedRenderHost is Dx12RenderHost && _activeBackend == RenderBackendKind.Dx12;

    /// <summary>
    /// Dispose DX12-specific components.
    /// </summary>
    private void DisposeDx12Components()
    {
        _renderDataExtractor = null;
        _inputAdapter = null;
    }
}
