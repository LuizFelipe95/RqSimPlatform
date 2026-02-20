using System.Numerics;
using Arch.Core;
using RqSimForms.Forms.Interfaces;
using RqSimRenderingEngine.Abstractions;
using RqSimRenderingEngine.Rendering.Backend.DX12;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using RqSimRenderingEngine.Rendering.Data;
using RqSimRenderingEngine.Rendering.Input;
using RqSimVisualization.Controls;
using RqSimVisualization.Rendering;
using RqSimUI.Rendering.Systems;
using RQSimulation;
using RQSimulation.Core.ECS;
using RQSimulation.GPUCompressedSparseRow;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RqSimVisualization;

public partial class RqSimVisualizationForm
{
    // DX12 rendering for CSR visualization
    private IRenderHost? _csrRenderHost;
    private Dx12RenderHost? _csrDx12Host; // Keep for DX12-specific operations (Clear with color)
    private InputSnapshotAdapter? _csrInputAdapter;

    // ECS world for CSR visualization
    private World? _csrEcsWorld;
    private PhysicsSyncSystem? _physicsSyncSystem;

    // Camera state
    private OrbitCamera? _csrCamera;

    // Visualization state
    private bool _csrShowEdges = true;
    private double _csrEdgeWeightThreshold = 0.3;

    // UI controls
    private Panel? _csrRenderPanel;
    private Panel? _csrControlsHostPanel; // Separate panel for WinForms controls (not overlapping DX12)
    private ComboBox? _csrModeComboBox;
    private CheckBox? _csrShowEdgesCheckBox;
    private Label? _csrStatsLabel;

    // === GPU 3D Rendering Controls (matching standalone Form_Rsim3DForm) ===
    
    /// <summary>
    /// CSR Render mode enum matching Form_Rsim3DForm.RenderMode3D.
    /// </summary>
    private enum CsrRenderMode3D
    {
        /// <summary>2D projection via ImGui DrawList (legacy, CPU-based).</summary>
        ImGui2D = 0,
        /// <summary>Full GPU 3D rendering with spheres and lines/quads.</summary>
        Gpu3D = 1
    }
    
    private CsrRenderMode3D _csrRenderMode3D = CsrRenderMode3D.ImGui2D;
    private ComboBox? _csrRenderModeComboBox;
    private CheckBox? _csrEdgeQuadsCheckBox;
    private CheckBox? _csrOcclusionCullingCheckBox;
    private TrackBar? _csrNodeRadiusTrackBar;
    private TrackBar? _csrEdgeThicknessTrackBar;
    
    // GPU 3D rendering state
    private float _csrNodeRadius = 0.5f;
    private bool _csrUseEdgeQuads = false;
    private bool _csrUseOcclusionCulling = false;

    // State flags
    private bool _csrVisualizationInitialized;
    private System.Windows.Forms.Timer? _timerCsr3D;
    private bool _isCsr3DInitialized;
    private Label? _csrPlaceholderLabel;

    // CSR data cache for rendering
    private CsrTopology? _csrCachedTopology;
    private Vector3[]? _csrPositions;
    private Vector4[]? _csrColors;

    // Cluster IDs for cluster visualization mode
    private int[]? _csrClusterIds;

    /// <summary>
    /// Initialize CSR 3D visualization tab with DX12 renderer.
    /// </summary>
    public void Initialize3DVisualCSR()
    {
        if (!_isCsr3DInitialized)
        {
            tabControl_Main.SelectedIndexChanged += TabControl1_SelectedIndexChanged_CSR;
        }
        TryInitializeCsrWithDx12();
    }

    private void TabControl1_SelectedIndexChanged_CSR(object? sender, EventArgs e)
    {
        if (tabControl_Main.SelectedTab == tabPage_3DVisualCSR)
        {
            if (!_csrVisualizationInitialized)
            {
                TryInitializeCsrWithDx12();
            }
        }
    }


    /// <summary>
    /// Simple orbit camera for 3D navigation.
    /// </summary>
    public class OrbitCamera
    {
        public float Distance { get; set; } = 50f;
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public Vector3 Target { get; set; } = Vector3.Zero;

        public Matrix4x4 GetViewMatrix()
        {
            float x = Distance * MathF.Cos(Pitch) * MathF.Sin(Yaw);
            float y = Distance * MathF.Sin(Pitch);
            float z = Distance * MathF.Cos(Pitch) * MathF.Cos(Yaw);

            Vector3 eye = Target + new Vector3(x, y, z);
            return Matrix4x4.CreateLookAt(eye, Target, Vector3.UnitY);
        }
    }
    /// <summary>
    /// Initialize CSR visualization with DX12 renderer.
    /// Works with any simulation type, not just CSR engine.
    /// </summary>
    private void TryInitializeCsrWithDx12()
    {
        if (tabPage_3DVisualCSR is null)
            return;

        // Create render panel if not exists
        // IMPORTANT: Do NOT add child controls to this panel - WinForms will repaint over DX12 output
        if (_csrRenderPanel is null)
        {
            // Create a container layout: Left panel for controls, Fill for DX12 render
            var containerPanel = new Panel
            {
                Dock = DockStyle.Fill
            };

            // Controls panel on the left (WinForms UI)
            var controlsHostPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 220,
                BackColor = Color.FromArgb(20, 20, 30)
            };

            // DX12 render panel takes the rest (no children!)
            // Use NativeRenderPanel to prevent WinForms from painting over DX12 output
            _csrRenderPanel = new NativeRenderPanel
            {
                Dock = DockStyle.Fill
            };
            _csrRenderPanel.MouseDown += CsrRenderPanel_MouseDown;
            _csrRenderPanel.MouseUp += CsrRenderPanel_MouseUp;
            _csrRenderPanel.MouseMove += CsrRenderPanel_MouseMove;
            _csrRenderPanel.MouseWheel += CsrRenderPanel_MouseWheel;
            _csrRenderPanel.Resize += CsrRenderPanel_Resize;

            // Store controls host for later use
            _csrControlsHostPanel = controlsHostPanel;

            tabPage_3DVisualCSR.Controls.Clear();
            // Add in correct order: Fill first, then Left (docking order matters)
            containerPanel.Controls.Add(_csrRenderPanel);
            containerPanel.Controls.Add(controlsHostPanel);
            tabPage_3DVisualCSR.Controls.Add(containerPanel);

            // Initialize UI controls immediately so they are visible even if engine is not ready
            InitializeCsrVisualizationControls();
        }

        // REMOVED: IsCsrEngineAvailable() check - we now work with any graph type
        // The visualization will use ActiveGraph or SimulationEngine.Graph

        // Ensure panel has valid handle
        if (!_csrRenderPanel.IsHandleCreated)
        {
            _csrRenderPanel.CreateControl();
        }

        if (_csrRenderPanel.Handle == IntPtr.Zero)
        {
            ShowCsrPlaceholder("Render panel not ready.\nPlease switch tabs and try again.");
            return;
        }

        int panelWidth = Math.Max(_csrRenderPanel.Width, 100);
        int panelHeight = Math.Max(_csrRenderPanel.Height, 100);

        try
        {
            // Create render host using factory with fallback
            if (_csrRenderHost is null)
            {
                var result = RenderHostFactory.Create(RenderBackendKind.Auto);
                
                if (result.Host is null)
                {
                    _consoleBuffer?.Append($"[CSR 3D] Render backend failed: {result.DiagnosticMessage}\n");
                    ShowCsrPlaceholder($"Render backend initialization failed:\n{result.DiagnosticMessage}");
                    return;
                }

                _csrRenderHost = result.Host;
                
                // Keep reference to DX12 host for DX12-specific operations
                _csrDx12Host = result.Host as Dx12RenderHost;

                try
                {
                    _csrRenderHost.Initialize(new RenderHostInitOptions(
                        _csrRenderPanel.Handle,
                        panelWidth,
                        panelHeight,
                        VSync: true));
                    
                    _consoleBuffer?.Append($"[CSR 3D] Using {result.ActualBackend} backend\n");
                }
                catch (Exception initEx)
                {
                    // DX12 Initialize failed, try Veldrid fallback
                    _consoleBuffer?.Append($"[CSR 3D] {result.ActualBackend} Initialize failed: {initEx.Message}, trying Veldrid fallback...\n");
                    
                    try { _csrRenderHost.Dispose(); } catch { }
                    _csrRenderHost = null;
                    _csrDx12Host = null;

                    var fallbackResult = RenderHostFactory.Create(RenderBackendKind.Veldrid);
                    if (fallbackResult.Host is null)
                    {
                        ShowCsrPlaceholder($"All render backends failed.\n\nDX12: {initEx.Message}\nVeldrid: {fallbackResult.DiagnosticMessage}");
                        return;
                    }

                    _csrRenderHost = fallbackResult.Host;
                    _csrDx12Host = null; // Veldrid doesn't have typed Clear

                    _csrRenderHost.Initialize(new RenderHostInitOptions(
                        _csrRenderPanel.Handle,
                        panelWidth,
                        panelHeight,
                        VSync: true));
                    
                    _consoleBuffer?.Append($"[CSR 3D] Fallback to {fallbackResult.ActualBackend} successful\n");
                }
            }

            _csrInputAdapter ??= new InputSnapshotAdapter();
            _csrCamera ??= new OrbitCamera { Distance = 50f, Yaw = 0.5f, Pitch = 0.3f };

            // Start render timer
            if (_timerCsr3D is null)
            {
                _timerCsr3D = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 FPS
                _timerCsr3D.Tick += TimerCsr3D_Tick;
            }
            _timerCsr3D.Start();

            _csrVisualizationInitialized = true;
            _isCsr3DInitialized = true;

            // Remove placeholder
            if (_csrPlaceholderLabel is not null)
            {
                _csrRenderPanel.Controls.Remove(_csrPlaceholderLabel);
                _csrPlaceholderLabel.Dispose();
                _csrPlaceholderLabel = null;
            }

            _consoleBuffer?.Append("[CSR 3D] Visualization initialized successfully\n");
        }
        catch (Exception ex)
        {
            _consoleBuffer?.Append($"[CSR 3D] Initialization failed: {ex.Message}\n");
            ShowCsrPlaceholder($"Initialization failed:\n{ex.Message}");
            
            // Cleanup partial initialization
            _csrRenderHost?.Dispose();
            _csrRenderHost = null;
            _csrDx12Host = null;
        }
    }

    /// <summary>
    /// Show placeholder message in CSR tab.
    /// </summary>
    private void ShowCsrPlaceholder(string message)
    {
        if (_csrRenderPanel is null)
            return;

        if (_csrPlaceholderLabel is null)
        {
            _csrPlaceholderLabel = new Label
            {
                ForeColor = Color.White,
                BackColor = Color.FromArgb(30, 30, 40),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 12f)
            };
        }

        _csrPlaceholderLabel.Text = message;

        if (!_csrRenderPanel.Controls.Contains(_csrPlaceholderLabel))
        {
            _csrRenderPanel.Controls.Clear();
            _csrRenderPanel.Controls.Add(_csrPlaceholderLabel);
        }

        _isCsr3DInitialized = true;
    }

    /// <summary>
    /// Reset CSR visualization state.
    /// </summary>
    public void ResetCsrVisualization()
    {
        _timerCsr3D?.Stop();
        _timerCsr3D?.Dispose();
        _timerCsr3D = null;

        _csrRenderHost?.Dispose();
        _csrRenderHost = null;
        _csrDx12Host = null;

        _csrEcsWorld?.Dispose();
        _csrEcsWorld = null;

        _csrVisualizationInitialized = false;
        _isCsr3DInitialized = false;
        _csrCachedTopology = null;
        _csrPositions = null;
        _csrColors = null;
    }

    /// <summary>
    /// Alias for TryInitializeCsrWithDx12 for backward compatibility.
    /// </summary>
    private void TryInitializeOrShowPlaceholder() => TryInitializeCsrWithDx12();

    private bool TryInitializeCsrVisualizationFromSimApi()
    {
        TryInitializeCsrWithDx12();
        return _csrVisualizationInitialized;
    }

    /// <summary>
    /// Checks if CSR engine is currently available and active.
    /// </summary>
    private bool IsCsrEngineAvailable()
    {
        return _simApi is not null
            && _simApi.ActiveEngineType == GpuEngineType.Csr
            && _simApi.CsrCayleyEngine is not null
            && _simApi.CsrCayleyEngine.IsInitialized;
    }

    /// <summary>
    /// Checks if any graph data is available for visualization.
    /// </summary>
    private bool IsGraphDataAvailable()
    {
        var graph = _simApi?.ActiveGraph ?? _simApi?.SimulationEngine?.Graph;
        return graph is not null && graph.N > 0;
    }
}

