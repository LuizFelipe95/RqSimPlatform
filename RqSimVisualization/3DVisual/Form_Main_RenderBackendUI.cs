using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using RqSimVisualization.Rendering;
using RqSimRenderingEngine.Abstractions;

namespace RqSimVisualization;

/// <summary>
/// Partial class for render backend UI components.
/// Provides status bar integration and additional diagnostic UI.
/// </summary>
public partial class RqSimVisualizationForm
{
    private StatusStrip? _statusStrip;
    private ToolStripStatusLabel? _tslRendererStatus;
    private ToolStripStatusLabel? _tslDx12Available;

    /// <summary>
    /// Initialize status bar with renderer status display.
    /// Call from Form_Load or after InitializeComponent.
    /// </summary>
    private void InitializeRendererStatusBar()
    {
        // Check if already initialized or if form doesn't want status bar
        if (_statusStrip is not null)
            return;

        // Create status strip
        _statusStrip = new StatusStrip
        {
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGray,
            SizingGrip = true,
            Dock = DockStyle.Bottom
        };

        // Create renderer status label
        _tslRendererStatus = new ToolStripStatusLabel
        {
            Text = "Renderer: GDI+ (Legacy)",
            ForeColor = Color.LightGray,
            Spring = false,
            AutoSize = true,
            Padding = new Padding(5, 0, 10, 0)
        };

        // Create DX12 availability label
        _tslDx12Available = new ToolStripStatusLabel
        {
            ForeColor = Color.Gray,
            Spring = false,
            AutoSize = true,
            Padding = new Padding(5, 0, 5, 0)
        };

        UpdateDx12AvailabilityStatus();

        // Add separator and spring for layout
        var spring = new ToolStripStatusLabel
        {
            Spring = true
        };

        _statusStrip.Items.Add(_tslRendererStatus);
        _statusStrip.Items.Add(new ToolStripSeparator());
        _statusStrip.Items.Add(_tslDx12Available);
        _statusStrip.Items.Add(spring);

        // Add to form
        Controls.Add(_statusStrip);
    }

    /// <summary>
    /// Update status bar with current renderer info.
    /// Call after render host changes.
    /// </summary>
    private void UpdateRendererStatusBar()
    {
        if (_tslRendererStatus is null)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(UpdateRendererStatusBar);
            return;
        }

        string status = GetRendererStatusText();
        _tslRendererStatus.Text = status;

        // Update color based on renderer state (DX12 only)
        _tslRendererStatus.ForeColor = _activeBackend == RenderBackendKind.Dx12
            ? Color.LimeGreen
            : Color.Gray;
    }

    private void UpdateDx12AvailabilityStatus()
    {
        if (_tslDx12Available is null)
            return;

        try
        {
            bool dx12Available = RenderHostFactory.IsDx12Available();
            _tslDx12Available.Text = dx12Available
                ? "DX12: Available"
                : "DX12: Not Available";
            _tslDx12Available.ForeColor = dx12Available
                ? Color.LimeGreen
                : Color.Gray;
        }
        catch (Exception ex)
        {
            _tslDx12Available.Text = "DX12: Check Failed";
            _tslDx12Available.ForeColor = Color.Orange;
            Debug.WriteLine($"[RenderBackend] DX12 check failed: {ex.Message}");
        }
    }

    private void ShowRenderBackendDiagnostics()
    {
        bool dx12Available = RenderHostFactory.IsDx12Available();

        string info = $"""
            Render Backend Diagnostics
            ==========================
            
            Current Backend: {_activeBackend}
            Selected Backend: {_selectedBackend}
            
            Available Backends:
            - DX12: {(dx12Available ? "Yes" : "No")}
            
            Last Diagnostic: {_lastBackendDiagnostic}
            """;

        MessageBox.Show(
            this,
            info,
            "Render Backend Diagnostics",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>
    /// Quickly switch to Veldrid (safe mode).
    /// </summary>
    private void SwitchToSafeMode()
    {
        // Safe mode (Veldrid) removed.
        _consoleBuffer?.Append("[RenderBackend] Safe Mode (Veldrid) is no longer available.\n");
    }

    /// <summary>
    /// Try to switch to DX12 for maximum performance.
    /// Falls back to Veldrid if DX12 is not available.
    /// </summary>
    private void TrySwitchToDx12()
    {
        if (!RenderHostFactory.IsDx12Available())
        {
            MessageBox.Show(
                this,
                "DirectX 12 is not available on this system.",
                "DX12 Not Available",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        _selectedBackend = RenderBackendKind.Dx12;
        RestartRenderHost();
    }
}
