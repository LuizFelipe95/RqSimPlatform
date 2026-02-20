using System.Numerics;
using ImGuiNET;
using RqSimRenderingEngine.Abstractions;
using RqSimRenderingEngine.Rendering.Backend.DX12;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using RQSimulation;
using Vortice.Mathematics;
using Color = System.Drawing.Color;
using Size = System.Drawing.Size;

namespace RqSim3DForm;

/// <summary>
/// Render mode for 3D visualization.
/// </summary>
public enum RenderMode3D
{
    /// <summary>2D projection via ImGui DrawList (legacy, CPU-based).</summary>
    ImGui2D,

    /// <summary>Full GPU 3D rendering with spheres and lines/quads.</summary>
    Gpu3D
}

/// <summary>
/// Standalone 3D visualization form for RQ Simulation.
/// Independent window that displays simulation graph data with DX12 rendering.
/// </summary>
public partial class Form_Rsim3DForm : Form
{
    // === DX12 Rendering ===
    private IRenderHost? _renderHost;
    private Dx12RenderHost? _dx12Host;
    private Panel? _renderPanel;
    private Panel? _controlsPanel;
    private System.Windows.Forms.Timer? _renderTimer;

    // === Camera ===
    private float _cameraDistance = 50f;
    private float _cameraYaw = 0.5f;
    private float _cameraPitch = 0.3f;
    private bool _isDragging;
    private Point _lastMousePos;

    // === Graph Data Provider ===
    private Func<GraphRenderData>? _getGraphData;

    // === Cached Render Data ===
    private float[]? _nodeX, _nodeY, _nodeZ;
    private NodeState[]? _nodeStates;
    private List<(int u, int v, float w)>? _edges;
    private int _nodeCount;
    private double _spectralDim;

    // === Visualization Settings ===
    private VisualizationMode _visMode = VisualizationMode.QuantumPhase;
    private bool _showEdges = true;
    private double _edgeWeightThreshold = 0.1;

    // === GPU 3D Rendering ===
    private RenderMode3D _renderMode = RenderMode3D.ImGui2D;
    private Dx12NodeInstance[]? _gpuNodeInstances;
    private Dx12LineVertex[]? _gpuEdgeVertices;
    private Dx12PackedEdgeData[]? _gpuPackedEdges;
    private Dx12PackedNodeData[]? _gpuPackedNodes;
    private int _gpuNodeCount;
    private int _gpuEdgeVertexCount;
    private int _gpuPackedEdgeCount;
    private float _nodeRadius = 0.5f;
    private bool _useEdgeQuads = false;
    private bool _useOcclusionCulling = false;

    // === Stats ===
    private float _fps;
    private int _frameCount;
    private DateTime _fpsUpdateTime = DateTime.Now;
    private DateTime _lastDebugLog = DateTime.MinValue;

    // === UI Controls ===
    private ComboBox? _modeComboBox;
    private CheckBox? _showEdgesCheckBox;
    private Label? _statsLabel;
    private ComboBox? _renderModeComboBox;
    private CheckBox? _edgeQuadsCheckBox;
    private CheckBox? _occlusionCullingCheckBox;
    private TrackBar? _nodeRadiusTrackBar;
    private TrackBar? _edgeThicknessTrackBar;

    public Form_Rsim3DForm()
    {
        InitializeComponent();
        SetupForm();
    }

    private void SetupForm()
    {
        Text = "RQ Simulation - 3D Visualization";
        this.Size = new Size(1200, 800);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(20, 20, 30);

        CreateLayout();
    }

    private void CreateLayout()
    {
        // Controls panel on left
        _controlsPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 220,
            BackColor = Color.FromArgb(25, 25, 35),
            Padding = new Padding(10)
        };

        // Render panel fills rest
        _renderPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black
        };
        _renderPanel.MouseDown += (s, e) => OnRenderPanelMouseDown(e);
        _renderPanel.MouseUp += (s, e) => OnRenderPanelMouseUp(e);
        _renderPanel.MouseMove += (s, e) => OnRenderPanelMouseMove(e);
        _renderPanel.MouseWheel += (s, e) => OnRenderPanelMouseWheel(e);
        _renderPanel.Resize += (s, e) => OnRenderPanelResize();

        Controls.Add(_renderPanel);
        Controls.Add(_controlsPanel);

        SetupControlsUI();
    }

    /// <summary>
    /// Sets the data provider for graph visualization.
    /// </summary>
    public void SetDataProvider(Func<GraphRenderData> provider)
    {
        _getGraphData = provider;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        InitializeDx12();
    }

    private void InitializeDx12()
    {
        if (_renderPanel is null || !_renderPanel.IsHandleCreated)
            return;

        try
        {
            _dx12Host = new Dx12RenderHost();
            _renderHost = _dx12Host;

            _renderHost.Initialize(new RenderHostInitOptions(
                _renderPanel.Handle,
                Math.Max(_renderPanel.Width, 100),
                Math.Max(_renderPanel.Height, 100),
                VSync: true));

            _renderTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 FPS
            _renderTimer.Tick += RenderTimer_Tick;
            _renderTimer.Start();

            SetStatusText("DX12 Initialized");

            // Log available GPU features
            if (_dx12Host is not null)
            {
                System.Diagnostics.Debug.WriteLine($"[3DForm] EdgeQuad Available: {_dx12Host.IsEdgeQuadAvailable}");
                System.Diagnostics.Debug.WriteLine($"[3DForm] Occlusion Culling Available: {_dx12Host.IsOcclusionCullingAvailable}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize DX12:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _renderTimer?.Stop();
        _renderTimer?.Dispose();
        _renderHost?.Dispose();
        base.OnFormClosing(e);
    }

}

/// <summary>
/// Data structure for graph rendering.
/// </summary>
public record struct GraphRenderData(
    float[]? NodeX,
    float[]? NodeY,
    float[]? NodeZ,
    NodeState[]? States,
    List<(int u, int v, float w)>? Edges,
    int NodeCount,
    double SpectralDimension = 0);

/// <summary>
/// Visualization modes for the 3D view.
/// </summary>
public enum VisualizationMode
{
    QuantumPhase,
    ProbabilityDensity,
    Curvature,
    GravityHeatmap,
    NetworkTopology,
    Clusters
}
