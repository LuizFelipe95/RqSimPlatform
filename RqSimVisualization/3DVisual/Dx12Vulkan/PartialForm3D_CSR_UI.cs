using System.Numerics;
using RqSimRenderingEngine.Abstractions;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using RqSimVisualization.Rendering;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;

namespace RqSimVisualization;

public partial class RqSimVisualizationForm
{
    // CSR Backend selection controls
    private ComboBox? _csrBackendComboBox;
    private Label? _csrBackendStatusLabel;
    private RenderBackendKind _csrSelectedBackend = RenderBackendKind.Dx12;

    private void InitializeCsrVisualizationControls()
    {
        // Use the separate controls host panel (not the DX12 render panel!)
        // This prevents WinForms from repainting over DX12 swapchain output
        if (_csrControlsHostPanel is null)
            return;

        // Controls panel
        var controlsPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = false,
            AutoScroll = true,
            WrapContents = false,
            BackColor = Color.Transparent, // Transparent to show host panel background
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };

        // === Backend Status Section ===
        var lblBackend = new Label
        {
            Text = "Render Backend:",
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3)
        };

        _csrBackendStatusLabel = new Label
        {
            Text = GetCsrBackendStatusText(),
            ForeColor = Color.LimeGreen,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };

        // Separator
        var separator1 = new Label
        {
            Text = "-----------------------",
            ForeColor = Color.Gray,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 5)
        };

        // === Visualization Mode Section ===
        var lblMode = new Label
        {
            Text = "Visualization Mode:",
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 5)
        };

        _csrModeComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180,
            BackColor = Color.FromArgb(50, 50, 60),
            ForeColor = Color.White
        };
        _csrModeComboBox.Items.AddRange(
        [
            "Quantum Phase",
            "Probability Density",
            "Curvature",
            "Gravity Heatmap",
            "Network Topology",
            "Clusters"
        ]);
        _csrModeComboBox.SelectedIndex = 0;
        _csrModeComboBox.SelectedIndexChanged += CsrModeComboBox_SelectedIndexChanged;

        // Show edges toggle
        _csrShowEdgesCheckBox = new CheckBox
        {
            Text = "Show Edges",
            ForeColor = Color.White,
            Checked = true,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 5)
        };
        _csrShowEdgesCheckBox.CheckedChanged += CsrShowEdgesCheckBox_CheckedChanged;

        // Edge Threshold
        var lblEdgeThreshold = new Label
        {
            Text = "Edge Threshold:",
            ForeColor = Color.LightGray,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 3)
        };

        var trackEdgeThreshold = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = (int)(_csrEdgeWeightThreshold * 100),
            Width = 180,
            Height = 30,
            TickFrequency = 20,
            BackColor = Color.FromArgb(30, 30, 40),
            Margin = new Padding(0, 0, 0, 5)
        };
        trackEdgeThreshold.ValueChanged += (s, e) =>
        {
            _csrEdgeWeightThreshold = trackEdgeThreshold.Value / 100.0;
            SyncEdgeThresholdFromCsrWindow(_csrEdgeWeightThreshold);
        };

        StoreCsrEdgeThresholdReference(trackEdgeThreshold);

        // Stats label
        _csrStatsLabel = new Label
        {
            Text = "Initializing...",
            ForeColor = Color.LightGray,
            AutoSize = true,
            MaximumSize = new Size(180, 0),
            Margin = new Padding(0, 15, 0, 0)
        };

        // Manifold Embedding CheckBox
        var chkManifold = new CheckBox
        {
            Text = "Manifold Embedding",
            ForeColor = Color.White,
            Checked = _enableManifoldEmbedding,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 5)
        };
        chkManifold.CheckedChanged += (s, e) =>
        {
            _enableManifoldEmbedding = chkManifold.Checked;
            if (!_enableManifoldEmbedding) ResetManifoldEmbedding();
        };

        // === GPU 3D Rendering Section (matching standalone Form_Rsim3DForm) ===
        var separatorGpu = new Label
        {
            Text = "-----------------------",
            ForeColor = Color.Gray,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 5)
        };

        var lblRenderMode = new Label
        {
            Text = "Render Mode:",
            ForeColor = Color.Cyan,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 5)
        };

        _csrRenderModeComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180,
            BackColor = Color.FromArgb(50, 50, 60),
            ForeColor = Color.White
        };
        _csrRenderModeComboBox.Items.AddRange(new object[]
        {
            "ImGui 2D (CPU)",
            "GPU 3D (DX12)"
        });
        _csrRenderModeComboBox.SelectedIndex = 0;
        _csrRenderModeComboBox.SelectedIndexChanged += CsrRenderModeComboBox_SelectedIndexChanged;

        // Edge Quads (Vertex Pulling)
        _csrEdgeQuadsCheckBox = new CheckBox
        {
            Text = "Edge Quads (GPU)",
            ForeColor = Color.White,
            Checked = _csrUseEdgeQuads,
            AutoSize = true,
            Enabled = false, // Enabled only in GPU 3D mode
            Margin = new Padding(0, 5, 0, 3)
        };
        _csrEdgeQuadsCheckBox.CheckedChanged += CsrEdgeQuadsCheckBox_CheckedChanged;

        // Occlusion Culling
        _csrOcclusionCullingCheckBox = new CheckBox
        {
            Text = "Occlusion Culling",
            ForeColor = Color.White,
            Checked = _csrUseOcclusionCulling,
            AutoSize = true,
            Enabled = false, // Enabled only in GPU 3D mode
            Margin = new Padding(0, 3, 0, 3)
        };
        _csrOcclusionCullingCheckBox.CheckedChanged += CsrOcclusionCullingCheckBox_CheckedChanged;

        // Node Radius
        var lblNodeRadius = new Label
        {
            Text = "Node Radius:",
            ForeColor = Color.LightGray,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 3)
        };

        _csrNodeRadiusTrackBar = new TrackBar
        {
            Minimum = 1,
            Maximum = 50,
            Value = (int)(_csrNodeRadius * 10),
            Width = 180,
            Height = 30,
            TickFrequency = 10,
            BackColor = Color.FromArgb(30, 30, 40),
            Enabled = false,
            Margin = new Padding(0, 0, 0, 3)
        };
        _csrNodeRadiusTrackBar.ValueChanged += CsrNodeRadiusTrackBar_ValueChanged;

        // Edge Thickness
        var lblEdgeThickness = new Label
        {
            Text = "Edge Thickness:",
            ForeColor = Color.LightGray,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 3)
        };

        _csrEdgeThicknessTrackBar = new TrackBar
        {
            Minimum = 1,
            Maximum = 20,
            Value = 2,
            Width = 180,
            Height = 30,
            TickFrequency = 5,
            BackColor = Color.FromArgb(30, 30, 40),
            Enabled = false,
            Margin = new Padding(0, 0, 0, 5)
        };
        _csrEdgeThicknessTrackBar.ValueChanged += CsrEdgeThicknessTrackBar_ValueChanged;

        // Targets Section
        var separator2 = new Label
        {
            Text = "-----------------",
            ForeColor = Color.Gray,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 5)
        };

        var lblTarget = new Label
        {
            Text = "Target Metrics:",
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 5)
        };

        var cmbTarget = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180,
            BackColor = Color.FromArgb(50, 50, 60),
            ForeColor = Color.White
        };
        cmbTarget.Items.AddRange(new object[]
        {
            "Combined",
            "Mass Gap",
            "Speed of Light",
            "Ricci Flatness",
            "Holographic"
        });
        cmbTarget.SelectedIndex = 0;
        cmbTarget.SelectedIndexChanged += (s, e) =>
        {
            _activeTargetVis = cmbTarget.SelectedIndex switch
            {
                0 => TargetStateType.Combined,
                1 => TargetStateType.MassGap,
                2 => TargetStateType.SpeedOfLight,
                3 => TargetStateType.RicciFlatness,
                4 => TargetStateType.HolographicAreaLaw,
                _ => TargetStateType.None
            };
        };

        var chkTargetOverlay = new CheckBox
        {
            Text = "Show Metrics Overlay",
            ForeColor = Color.White,
            Checked = true,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 5)
        };
        chkTargetOverlay.CheckedChanged += (s, e) =>
        {
            _showTargetOverlay = chkTargetOverlay.Checked;
            if (_pnlTargetStatus != null) _pnlTargetStatus.Visible = _showTargetOverlay;
        };

        // Initialize Target Status Panel
        if (_pnlTargetStatus == null)
        {
            _pnlTargetStatus = new DoubleBufferedPanel
            {
                Size = new Size(200, 160), // Slightly narrower to fit in side panel
                BackColor = Color.FromArgb(180, 15, 15, 25),
                Visible = _showTargetOverlay,
                Margin = new Padding(0, 10, 0, 0)
            };
            _pnlTargetStatus.Paint += PnlTargetStatus_Paint;
        }

        // Ensure it's added to the controls panel (not render panel)
        if (!controlsPanel.Controls.Contains(_pnlTargetStatus))
        {
            controlsPanel.Controls.Add(_pnlTargetStatus);
        }

        // Add controls in order
        controlsPanel.Controls.Add(lblBackend);
        controlsPanel.Controls.Add(_csrBackendStatusLabel);
        controlsPanel.Controls.Add(separator1);
        controlsPanel.Controls.Add(lblMode);
        controlsPanel.Controls.Add(_csrModeComboBox);
        controlsPanel.Controls.Add(_csrShowEdgesCheckBox);
        controlsPanel.Controls.Add(lblEdgeThreshold);
        controlsPanel.Controls.Add(trackEdgeThreshold);
        controlsPanel.Controls.Add(chkManifold);
        // GPU 3D Rendering controls
        controlsPanel.Controls.Add(separatorGpu);
        controlsPanel.Controls.Add(lblRenderMode);
        controlsPanel.Controls.Add(_csrRenderModeComboBox);
        controlsPanel.Controls.Add(_csrEdgeQuadsCheckBox);
        controlsPanel.Controls.Add(_csrOcclusionCullingCheckBox);
        controlsPanel.Controls.Add(lblNodeRadius);
        controlsPanel.Controls.Add(_csrNodeRadiusTrackBar);
        controlsPanel.Controls.Add(lblEdgeThickness);
        controlsPanel.Controls.Add(_csrEdgeThicknessTrackBar);
        // Target Metrics section
        controlsPanel.Controls.Add(separator2);
        controlsPanel.Controls.Add(lblTarget);
        controlsPanel.Controls.Add(cmbTarget);
        controlsPanel.Controls.Add(chkTargetOverlay);
        controlsPanel.Controls.Add(_csrStatsLabel);

        // Add spectral interpretation legend panel
        _csrLegendPanel = CreateCsrLegendPanel();
        controlsPanel.Controls.Add(_csrLegendPanel);

        // Target status panel is added via check above or we can add it here explicitly if we want strict order
        // Let's move it here to be explicit about order
        controlsPanel.Controls.SetChildIndex(_pnlTargetStatus, controlsPanel.Controls.Count - 1);

        // Add to separate controls host panel (NOT the DX12 render panel!)
        _csrControlsHostPanel.Controls.Add(controlsPanel);
    }

    private void CsrBackendComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // DX12 is the only supported backend now
    }

    private void RestartCsrVisualizationWithBackend()
    {
        var currentMode = _csrVisMode;
        var showEdges = _csrShowEdges;

        ResetCsrVisualization();
        TryInitializeOrShowPlaceholder();

        _csrVisMode = currentMode;
        _csrShowEdges = showEdges;

        if (_csrModeComboBox is not null)
            _csrModeComboBox.SelectedIndex = (int)currentMode;
        if (_csrShowEdgesCheckBox is not null)
            _csrShowEdgesCheckBox.Checked = showEdges;
    }

    private string GetCsrBackendStatusText()
    {
        if (_csrDx12Host is not null && _csrDx12Host.IsInitialized)
            return "Active: DirectX 12";
        return "Not initialized";
    }

    private void UpdateCsrBackendStatus()
    {
        if (_csrBackendStatusLabel is null)
            return;

        string status = GetCsrBackendStatusText();
        Color color = _csrDx12Host?.IsInitialized == true ? Color.LimeGreen : Color.Gray;

        if (_csrBackendStatusLabel.InvokeRequired)
        {
            _csrBackendStatusLabel.Invoke(() =>
            {
                _csrBackendStatusLabel.Text = status;
                _csrBackendStatusLabel.ForeColor = color;
            });
        }
        else
        {
            _csrBackendStatusLabel.Text = status;
            _csrBackendStatusLabel.ForeColor = color;
        }
    }

    private void UpdateCsrStats()
    {
        if (_csrStatsLabel is null)
            return;

        // Use _csrNodeCount from rendering (updated by UpdateCsrGraphData)
        // instead of _csrCachedTopology which may not be set
        int nodeCount = _csrNodeCount;
        int edgeCount = _csrEdgeCount;

        string stats = $"""
            Nodes: {nodeCount}
            Edges: {edgeCount}
            Mode: {_csrVisMode}
            FPS: {_csrFps:F1}
            Backend: DirectX 12
            """;

        if (_csrStatsLabel.InvokeRequired)
        {
            _csrStatsLabel.Invoke(() => _csrStatsLabel.Text = stats);
        }
        else
        {
            _csrStatsLabel.Text = stats;
        }

        UpdateCsrBackendStatus();
    }

    // Event handlers
    private void CsrModeComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_csrModeComboBox is not null)
        {
            _csrVisMode = (CsrVisualizationMode)_csrModeComboBox.SelectedIndex;
            _physicsSyncSystem?.SetColorMode((uint)_csrVisMode);
        }
    }

    private void CsrShowEdgesCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        if (_csrShowEdgesCheckBox is not null)
        {
            _csrShowEdges = _csrShowEdgesCheckBox.Checked;
        }
    }

    private DrawingPoint _csrLastMousePos;
    private bool _csrIsRotating;
    private bool _csrIsPanning;

    private void CsrRenderPanel_MouseDown(object? sender, MouseEventArgs e)
    {
        _csrLastMousePos = e.Location;
        _csrIsRotating = e.Button == MouseButtons.Left;
        _csrIsPanning = e.Button == MouseButtons.Middle;
    }

    private void CsrRenderPanel_MouseUp(object? sender, MouseEventArgs e)
    {
        _csrIsRotating = false;
        _csrIsPanning = false;
    }

    private void CsrRenderPanel_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_csrCamera is null)
            return;

        int dx = e.X - _csrLastMousePos.X;
        int dy = e.Y - _csrLastMousePos.Y;
        _csrLastMousePos = e.Location;

        if (_csrIsRotating)
        {
            _csrCamera.Yaw += dx * 0.01f;
            _csrCamera.Pitch += dy * 0.01f;
            _csrCamera.Pitch = Math.Clamp(_csrCamera.Pitch, -MathF.PI / 2f + 0.1f, MathF.PI / 2f - 0.1f);
        }

        if (_csrIsPanning)
        {
            _csrCamera.Target += new Vector3(-dx * 0.1f, dy * 0.1f, 0);
        }
    }

    private void CsrRenderPanel_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (_csrCamera is null)
            return;

        _csrCamera.Distance -= e.Delta * 0.05f;
        _csrCamera.Distance = Math.Clamp(_csrCamera.Distance, 1f, 500f);
    }

    // === GPU 3D Rendering Event Handlers ===

    private void CsrRenderModeComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        _csrRenderMode3D = (CsrRenderMode3D)(_csrRenderModeComboBox?.SelectedIndex ?? 0);
        UpdateCsrGpuRenderingControls();
    }

    private void CsrEdgeQuadsCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        _csrUseEdgeQuads = _csrEdgeQuadsCheckBox?.Checked ?? false;
        if (_csrDx12Host is not null)
        {
            _csrDx12Host.EdgeRenderMode = _csrUseEdgeQuads ? EdgeRenderMode.Quads : EdgeRenderMode.Lines;
        }
        // Edge thickness only available when using quads
        if (_csrEdgeThicknessTrackBar is not null)
        {
            _csrEdgeThicknessTrackBar.Enabled = _csrRenderMode3D == CsrRenderMode3D.Gpu3D && _csrUseEdgeQuads;
        }
    }

    private void CsrOcclusionCullingCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        _csrUseOcclusionCulling = _csrOcclusionCullingCheckBox?.Checked ?? false;
        if (_csrDx12Host is not null)
        {
            _csrDx12Host.OcclusionCullingEnabled = _csrUseOcclusionCulling;
        }
    }

    private void CsrNodeRadiusTrackBar_ValueChanged(object? sender, EventArgs e)
    {
        _csrNodeRadius = (_csrNodeRadiusTrackBar?.Value ?? 5) / 10f;
    }

    private void CsrEdgeThicknessTrackBar_ValueChanged(object? sender, EventArgs e)
    {
        if (_csrDx12Host is not null)
        {
            _csrDx12Host.EdgeQuadThickness = (_csrEdgeThicknessTrackBar?.Value ?? 2) / 100f;
        }
    }

    /// <summary>
    /// Enable/disable GPU 3D rendering controls based on current render mode.
    /// Matching Form_Rsim3DForm.UpdateGpuRenderingControls().
    /// </summary>
    private void UpdateCsrGpuRenderingControls()
    {
        bool isGpu3D = _csrRenderMode3D == CsrRenderMode3D.Gpu3D;

        if (_csrEdgeQuadsCheckBox is not null)
            _csrEdgeQuadsCheckBox.Enabled = isGpu3D && (_csrDx12Host?.IsEdgeQuadAvailable ?? false);

        if (_csrOcclusionCullingCheckBox is not null)
            _csrOcclusionCullingCheckBox.Enabled = isGpu3D && (_csrDx12Host?.IsOcclusionCullingAvailable ?? false);

        if (_csrNodeRadiusTrackBar is not null)
            _csrNodeRadiusTrackBar.Enabled = isGpu3D;

        if (_csrEdgeThicknessTrackBar is not null)
            _csrEdgeThicknessTrackBar.Enabled = isGpu3D && _csrUseEdgeQuads;

        // Update DX12 host settings if switching to GPU mode
        if (_csrDx12Host is not null && isGpu3D)
        {
            _csrDx12Host.EdgeRenderMode = _csrUseEdgeQuads ? EdgeRenderMode.Quads : EdgeRenderMode.Lines;
            _csrDx12Host.OcclusionCullingEnabled = _csrUseOcclusionCulling;
            _csrDx12Host.EdgeQuadThickness = (_csrEdgeThicknessTrackBar?.Value ?? 2) / 100f;
        }
    }
}
