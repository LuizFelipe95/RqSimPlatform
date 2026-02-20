using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using Color = System.Drawing.Color;
using Size = System.Drawing.Size;

namespace RqSim3DForm;

public partial class Form_Rsim3DForm
{
    private CheckBox? _manifoldCheckBox;
    private ComboBox? _targetMetricsComboBox;
    private int _activeTargetType; // 0=Combined, 1=MassGap, 2=SpeedOfLight, 3=Ricci, 4=Holographic

    private void SetupControlsUI()
    {
        if (_controlsPanel is null) return;

        var flowPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
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

        var lblBackendStatus = new Label
        {
            Text = "Active: DirectX 12",
            ForeColor = Color.LimeGreen,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };

        // Separator 1
        var sep1 = CreateSeparator();

        // === Visualization Mode Section ===
        var lblMode = new Label
        {
            Text = "Visualization Mode:",
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 5)
        };

        _modeComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180,
            BackColor = Color.FromArgb(50, 50, 60),
            ForeColor = Color.White
        };
        _modeComboBox.Items.AddRange(new object[]
        {
            "Quantum Phase",
            "Probability Density",
            "Curvature",
            "Gravity Heatmap",
            "Network Topology",
            "Clusters"
        });
        _modeComboBox.SelectedIndex = 0;
        _modeComboBox.SelectedIndexChanged += (s, e) => 
        {
            if (_modeComboBox is not null)
                _visMode = (VisualizationMode)_modeComboBox.SelectedIndex;
        };

        // Show Edges
        _showEdgesCheckBox = new CheckBox
        {
            Text = "Show Edges",
            ForeColor = Color.White,
            Checked = true,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 5)
        };
        _showEdgesCheckBox.CheckedChanged += (s, e) =>
        {
            if (_showEdgesCheckBox is not null)
                _showEdges = _showEdgesCheckBox.Checked;
        };

        // Edge Threshold
        var lblThreshold = new Label
        {
            Text = "Edge Threshold:",
            ForeColor = Color.LightGray,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 3)
        };

        var trackThreshold = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = (int)(_edgeWeightThreshold * 100),
            Width = 180,
            Height = 30,
            TickFrequency = 20,
            BackColor = Color.FromArgb(30, 30, 40),
            Margin = new Padding(0, 0, 0, 5)
        };
        trackThreshold.ValueChanged += (s, e) =>
        {
            _edgeWeightThreshold = trackThreshold.Value / 100.0;
        };

        // Manifold Embedding
        _manifoldCheckBox = new CheckBox
        {
            Text = "Manifold Embedding",
            ForeColor = Color.White,
            Checked = _enableManifoldEmbedding,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 5)
        };
        _manifoldCheckBox.CheckedChanged += (s, e) =>
        {
            _enableManifoldEmbedding = _manifoldCheckBox?.Checked ?? false;
            if (!_enableManifoldEmbedding)
                ResetManifoldEmbedding();
        };

        // Separator 2
        var sep2 = CreateSeparator();

        // === GPU 3D Rendering Section ===
        var lblRenderMode = new Label
        {
            Text = "Render Mode:",
            ForeColor = Color.Cyan,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 5)
        };

        _renderModeComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180,
            BackColor = Color.FromArgb(50, 50, 60),
            ForeColor = Color.White
        };
        _renderModeComboBox.Items.AddRange(new object[]
        {
            "ImGui 2D (CPU)",
            "GPU 3D (DX12)"
        });
        _renderModeComboBox.SelectedIndex = 0;
        _renderModeComboBox.SelectedIndexChanged += (s, e) =>
        {
            _renderMode = (RenderMode3D)(_renderModeComboBox?.SelectedIndex ?? 0);
            UpdateGpuRenderingControls();
        };

        // Edge Quads (Vertex Pulling)
        _edgeQuadsCheckBox = new CheckBox
        {
            Text = "Edge Quads (GPU)",
            ForeColor = Color.White,
            Checked = _useEdgeQuads,
            AutoSize = true,
            Enabled = false, // Enabled only in GPU 3D mode
            Margin = new Padding(0, 5, 0, 3)
        };
        _edgeQuadsCheckBox.CheckedChanged += (s, e) =>
        {
            _useEdgeQuads = _edgeQuadsCheckBox?.Checked ?? false;
            if (_dx12Host is not null)
            {
                _dx12Host.EdgeRenderMode = _useEdgeQuads ? EdgeRenderMode.Quads : EdgeRenderMode.Lines;
            }
        };

        // Occlusion Culling
        _occlusionCullingCheckBox = new CheckBox
        {
            Text = "Occlusion Culling",
            ForeColor = Color.White,
            Checked = _useOcclusionCulling,
            AutoSize = true,
            Enabled = false, // Enabled only in GPU 3D mode
            Margin = new Padding(0, 3, 0, 3)
        };
        _occlusionCullingCheckBox.CheckedChanged += (s, e) =>
        {
            _useOcclusionCulling = _occlusionCullingCheckBox?.Checked ?? false;
            if (_dx12Host is not null)
            {
                _dx12Host.OcclusionCullingEnabled = _useOcclusionCulling;
            }
        };

        // Node Radius
        var lblNodeRadius = new Label
        {
            Text = "Node Radius:",
            ForeColor = Color.LightGray,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 3)
        };

        _nodeRadiusTrackBar = new TrackBar
        {
            Minimum = 1,
            Maximum = 50,
            Value = (int)(_nodeRadius * 10),
            Width = 180,
            Height = 30,
            TickFrequency = 10,
            BackColor = Color.FromArgb(30, 30, 40),
            Enabled = false,
            Margin = new Padding(0, 0, 0, 3)
        };
        _nodeRadiusTrackBar.ValueChanged += (s, e) =>
        {
            _nodeRadius = (_nodeRadiusTrackBar?.Value ?? 5) / 10f;
        };

        // Edge Thickness
        var lblEdgeThickness = new Label
        {
            Text = "Edge Thickness:",
            ForeColor = Color.LightGray,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 3)
        };

        _edgeThicknessTrackBar = new TrackBar
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
        _edgeThicknessTrackBar.ValueChanged += (s, e) =>
        {
            if (_dx12Host is not null)
            {
                _dx12Host.EdgeQuadThickness = (_edgeThicknessTrackBar?.Value ?? 2) / 100f;
            }
        };

        // Separator 3
        var sep3 = CreateSeparator();

        // === Target Metrics Section ===
        var lblTarget = new Label
        {
            Text = "Target Metrics:",
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 5)
        };

        _targetMetricsComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180,
            BackColor = Color.FromArgb(50, 50, 60),
            ForeColor = Color.White
        };
        _targetMetricsComboBox.Items.AddRange(new object[]
        {
            "Combined",
            "Mass Gap",
            "Speed of Light",
            "Ricci Flatness",
            "Holographic"
        });
        _targetMetricsComboBox.SelectedIndex = 0;
        _targetMetricsComboBox.SelectedIndexChanged += (s, e) =>
        {
            _activeTargetType = _targetMetricsComboBox?.SelectedIndex ?? 0;
        };

        // Show Metrics Overlay
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
            if (_targetStatusPanel != null)
                _targetStatusPanel.Visible = _showTargetOverlay;
        };

        // Stats label
        _statsLabel = new Label
        {
            Text = "Initializing...",
            ForeColor = Color.LightGray,
            AutoSize = true,
            MaximumSize = new Size(180, 0),
            Margin = new Padding(0, 15, 0, 0)
        };

        // Spectral Interpretation Legend
        var spectralLegend = CreateSpectralLegendPanel();

        // Target Status Panel
        _targetStatusPanel = CreateTargetStatusPanel();

        // Add controls in order (matching CSR UI)
        flowPanel.Controls.Add(lblBackend);
        flowPanel.Controls.Add(lblBackendStatus);
        flowPanel.Controls.Add(sep1);
        flowPanel.Controls.Add(lblMode);
        flowPanel.Controls.Add(_modeComboBox);
        flowPanel.Controls.Add(_showEdgesCheckBox);
        flowPanel.Controls.Add(lblThreshold);
        flowPanel.Controls.Add(trackThreshold);
        flowPanel.Controls.Add(_manifoldCheckBox);
        flowPanel.Controls.Add(sep2);
        flowPanel.Controls.Add(lblRenderMode);
        flowPanel.Controls.Add(_renderModeComboBox);
        flowPanel.Controls.Add(_edgeQuadsCheckBox);
        flowPanel.Controls.Add(_occlusionCullingCheckBox);
        flowPanel.Controls.Add(lblNodeRadius);
        flowPanel.Controls.Add(_nodeRadiusTrackBar);
        flowPanel.Controls.Add(lblEdgeThickness);
        flowPanel.Controls.Add(_edgeThicknessTrackBar);
        flowPanel.Controls.Add(sep3);
        flowPanel.Controls.Add(lblTarget);
        flowPanel.Controls.Add(_targetMetricsComboBox);
        flowPanel.Controls.Add(chkTargetOverlay);
        flowPanel.Controls.Add(_statsLabel);
        flowPanel.Controls.Add(spectralLegend);
        flowPanel.Controls.Add(_targetStatusPanel);

        _controlsPanel.Controls.Add(flowPanel);
    }

    private Label CreateSeparator()
    {
        return new Label
        {
            Text = "-----------------------",
            ForeColor = Color.Gray,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 5)
        };
    }

    /// <summary>
    /// Creates the Spectral Interpretation legend panel.
    /// </summary>
    private Panel CreateSpectralLegendPanel()
    {
        var panel = new Panel
        {
            Width = 190,
            Height = 115,
            BackColor = Color.FromArgb(35, 35, 45),
            Margin = new Padding(0, 10, 0, 5)
        };

        panel.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using var font = new Font(SystemFonts.DefaultFont.FontFamily, 8f);
            using var titleFont = new Font(SystemFonts.DefaultFont.FontFamily, 8f, FontStyle.Bold);
            using var brushText = new SolidBrush(Color.LightGray);

            int y = 4;
            int boxSize = 10;

            g.DrawString("Spectral Interpretation:", titleFont, brushText, 4, y);
            y += 16;

            var items = new (string Label, Color Color)[]
            {
                ("d_S < 1.5: Dust", Color.Gray),
                ("d_S ? 2: Filament", Color.Cyan),
                ("d_S ? 3: Membrane", Color.Yellow),
                ("d_S ? 4: Bulk (Target)", Color.Lime),
                ("d_S > 4.5: Complex", Color.Magenta)
            };

            foreach (var item in items)
            {
                using var brush = new SolidBrush(item.Color);
                g.FillRectangle(brush, 4, y + 1, boxSize, boxSize);
                g.DrawString(item.Label, font, brushText, boxSize + 8, y);
                y += 16;
            }
        };

        return panel;
    }

    private void SetStatusText(string status)
    {
        if (_statsLabel is not null)
        {
            if (_statsLabel.InvokeRequired)
                _statsLabel.Invoke(() => _statsLabel.Text = status);
            else
                _statsLabel.Text = status;
        }
    }

    /// <summary>
    /// Enable/disable GPU 3D rendering controls based on current render mode.
    /// </summary>
    private void UpdateGpuRenderingControls()
    {
        bool isGpu3D = _renderMode == RenderMode3D.Gpu3D;
        
        if (_edgeQuadsCheckBox is not null)
            _edgeQuadsCheckBox.Enabled = isGpu3D && (_dx12Host?.IsEdgeQuadAvailable ?? false);
        
        if (_occlusionCullingCheckBox is not null)
            _occlusionCullingCheckBox.Enabled = isGpu3D && (_dx12Host?.IsOcclusionCullingAvailable ?? false);
        
        if (_nodeRadiusTrackBar is not null)
            _nodeRadiusTrackBar.Enabled = isGpu3D;
        
        if (_edgeThicknessTrackBar is not null)
            _edgeThicknessTrackBar.Enabled = isGpu3D && _useEdgeQuads;

        // Update DX12 host settings
        if (_dx12Host is not null && isGpu3D)
        {
            _dx12Host.EdgeRenderMode = _useEdgeQuads ? EdgeRenderMode.Quads : EdgeRenderMode.Lines;
            _dx12Host.OcclusionCullingEnabled = _useOcclusionCulling;
            _dx12Host.EdgeQuadThickness = (_edgeThicknessTrackBar?.Value ?? 2) / 100f;
        }
    }

    #region Mouse Handling

    private void OnRenderPanelMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _lastMousePos = e.Location;
        }
    }

    private void OnRenderPanelMouseUp(MouseEventArgs e)
    {
        _isDragging = false;
    }

    private void OnRenderPanelMouseMove(MouseEventArgs e)
    {
        if (_isDragging)
        {
            float dx = e.X - _lastMousePos.X;
            float dy = e.Y - _lastMousePos.Y;

            _cameraYaw += dx * 0.01f;
            _cameraPitch = Math.Clamp(_cameraPitch + dy * 0.01f, -1.5f, 1.5f);

            _lastMousePos = e.Location;
        }
    }

    private void OnRenderPanelMouseWheel(MouseEventArgs e)
    {
        _cameraDistance = Math.Clamp(_cameraDistance - e.Delta * 0.05f, 5f, 500f);
    }

    private void OnRenderPanelResize()
    {
        if (_renderHost is not null && _renderPanel is not null)
            _renderHost.Resize(Math.Max(_renderPanel.Width, 1), Math.Max(_renderPanel.Height, 1));
    }

    #endregion
}
