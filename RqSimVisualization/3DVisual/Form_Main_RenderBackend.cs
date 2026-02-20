using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using RqSimVisualization.Rendering;

using RqSimRenderingEngine.Abstractions;
// Veldrid types accessed through plugin only

namespace RqSimVisualization;

/// <summary>
/// Partial class for unified render backend selection.
/// Provides UI controls and logic for switching between Veldrid and DX12.
/// Implements fallback behavior: Auto mode tries DX12 first, falls back to Veldrid.
/// </summary>
public partial class RqSimVisualizationForm
{
    private IRenderHost? _unifiedRenderHost;
    private RenderBackendKind _selectedBackend = RenderBackendKind.Dx12;
    private RenderBackendKind _activeBackend = RenderBackendKind.Dx12;
    private string _lastBackendDiagnostic = "";

    private ComboBox? _cmbRenderBackend;
    private Label? _lblRenderBackendStatus;
    private Panel? _pnlBackendStatusIndicator;
    private ToolTip? _backendStatusTooltip;

    /// <summary>
    /// Currently active render backend.
    /// </summary>
    public RenderBackendKind ActiveRenderBackend => _activeBackend;

    /// <summary>
    /// Last diagnostic message from backend initialization.
    /// </summary>
    public string LastBackendDiagnostic => _lastBackendDiagnostic;

    /// <summary>
    /// Check if DX12 is available (for UI hints).
    /// </summary>
    public bool IsDx12Supported => RenderHostFactory.IsDx12Available();

    /// <summary>
    /// Initialize unified render backend selection controls.
    /// Call from Form_Main constructor or InitializeVeldridRendering.
    /// </summary>
    private void InitializeRenderBackendSelection()
    {
        // Check if backend selection UI already exists
        if (_cmbRenderBackend is not null)
            return;

        _selectedBackend = RenderBackendKind.Dx12;

        // Create tooltip for detailed status
        _backendStatusTooltip = new ToolTip
        {
            AutoPopDelay = 10000,
            InitialDelay = 500,
            ReshowDelay = 100,
            ShowAlways = true
        };

        // Create backend selection label
        var lblBackend = new Label
        {
            Text = "Renderer:",
            AutoSize = true,
            Location = new Point(10, 13),
            ForeColor = Color.LightGray,
            BackColor = Color.Transparent
        };

        // Create backend selection combo
        _cmbRenderBackend = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 100,
            Location = new Point(80, 10),
            BackColor = Color.FromArgb(40, 40, 40),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };

        _cmbRenderBackend.Items.Clear();
        _cmbRenderBackend.Items.AddRange(["DirectX 12"]);
        _cmbRenderBackend.SelectedIndex = 0;
        _cmbRenderBackend.SelectedIndexChanged += (_, _) => { };

        // Add to DX12 panel if it exists
        if (_dx12Panel is not null)
        {
            _dx12Panel.Controls.Add(lblBackend);
            _dx12Panel.Controls.Add(_cmbRenderBackend);
            _dx12Panel.Controls.Add(_pnlBackendStatusIndicator);
            _dx12Panel.Controls.Add(_lblRenderBackendStatus);

            lblBackend.BringToFront();
            _cmbRenderBackend.BringToFront();
            _pnlBackendStatusIndicator.BringToFront();
            _lblRenderBackendStatus.BringToFront();
        }
    }

    private void PnlBackendStatusIndicator_Paint(object? sender, PaintEventArgs e)
    {
        if (_pnlBackendStatusIndicator is null)
            return;

        // Draw circle indicator
        using var brush = new SolidBrush(_pnlBackendStatusIndicator.BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillEllipse(brush, 0, 0, 11, 11);

        // Draw border
        using var pen = new Pen(Color.FromArgb(80, 80, 80), 1);
        e.Graphics.DrawEllipse(pen, 0, 0, 10, 10);
    }

    private void CmbRenderBackend_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // Backend switching disabled (DX12 only).
    }

    private static int BackendToIndex(RenderBackendKind backend) => 0;

    private void RestartRenderHost()
    {
        DisposeUnifiedRenderHost();
        DisposeDx12Components();

        if (_dx12Panel is null || !_dx12Panel.Visible)
            return;

        UpdateBackendStatus("Initializing...", Color.Yellow, "Creating render backend...");

        var result = RenderHostFactory.Create(RenderBackendKind.Auto);
        _lastBackendDiagnostic = result.DiagnosticMessage;

        if (result.Host is null)
        {
            UpdateBackendStatus("Failed", Color.Red, _lastBackendDiagnostic);
            return;
        }

        _unifiedRenderHost = result.Host;
        _activeBackend = result.ActualBackend;

        try
        {
            _unifiedRenderHost.Initialize(new RenderHostInitOptions(
                _dx12Panel.Handle,
                _dx12Panel.Width,
                _dx12Panel.Height,
                VSync: true));

            UpdateBackendStatus(_activeBackend.ToString(), Color.LimeGreen, _lastBackendDiagnostic);
            UpdateRendererStatusBar();
        }
        catch (Exception ex)
        {
            try
            {
                _unifiedRenderHost.Dispose();
            }
            catch
            {
                // ignore dispose errors
            }
            finally
            {
                _unifiedRenderHost = null;
            }

            _lastBackendDiagnostic = $"{_activeBackend} initialization failed: {ex.Message}";
            UpdateBackendStatus("Failed", Color.Red, _lastBackendDiagnostic);
        }
    }

    private void UpdateBackendStatus(string status, Color indicatorColor, string tooltip)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateBackendStatus(status, indicatorColor, tooltip));
            return;
        }

        if (_lblRenderBackendStatus is not null)
        {
            _lblRenderBackendStatus.Text = status;
        }

        if (_pnlBackendStatusIndicator is not null)
        {
            _pnlBackendStatusIndicator.BackColor = indicatorColor;
            _pnlBackendStatusIndicator.Invalidate();
        }

        if (_backendStatusTooltip is not null)
        {
            if (_lblRenderBackendStatus is not null)
                _backendStatusTooltip.SetToolTip(_lblRenderBackendStatus, tooltip);
            if (_pnlBackendStatusIndicator is not null)
                _backendStatusTooltip.SetToolTip(_pnlBackendStatusIndicator, tooltip);
        }
    }

    /// <summary>
    /// Dispose unified render host resources.
    /// Call from DeactivateVeldridRendering or form disposal.
    /// </summary>
    private void DisposeUnifiedRenderHost()
    {
        _unifiedRenderHost?.Dispose();
        _unifiedRenderHost = null;
    }

    /// <summary>
    /// Get current renderer status string for status bar display.
    /// </summary>
    public string GetRendererStatusText()
    {
        return _unifiedRenderHost is not null
            ? $"Renderer: {_activeBackend}"
            : "Renderer: GDI+ (Legacy)";
    }
}
