using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using RqSimRenderingEngine.Abstractions;
using RqSimRenderingEngine.Rendering.Backend.DX12;
using RqSimUI.Rendering.Plugins;
using Vortice.Mathematics;
using DrawingColor = System.Drawing.Color;

namespace Dx12WinForm;

/// <summary>
/// Partial class containing DX12 render loop and diagnostic test code.
/// </summary>
public partial class Dx12WinForm : Form
{
    /// <summary>
    /// Start DX12 rendering test with diagnostic output.
    /// </summary>
    private void BtnStartDx12Test_Click(object? sender, EventArgs e)
    {
        if (_isRendering)
            return;

        // Set environment variable for debug layer if checked
        if (_chkEnableDebugLayer.Checked)
        {
            Environment.SetEnvironmentVariable("DX12_FORCE_DEBUG_LAYER", "1");
            Debug.WriteLine("[DX12Test] Debug layer ENABLED via environment variable");
        }
        else
        {
            Environment.SetEnvironmentVariable("DX12_FORCE_DEBUG_LAYER", null);
        }

        UpdateStatus("Initializing...", DrawingColor.Yellow);
        Application.DoEvents();

        try
        {
            // Create render host
            var factoryResult = RenderHostFactory.Create(RenderBackendKind.Dx12);

            if (factoryResult.Host is null)
            {
                UpdateStatus($"Failed: {factoryResult.DiagnosticMessage}", DrawingColor.Red);
                Debug.WriteLine($"[DX12Test] Factory failed: {factoryResult.DiagnosticMessage}");
                return;
            }

            _renderHost = factoryResult.Host;
            Debug.WriteLine($"[DX12Test] Factory success: {factoryResult.DiagnosticMessage}");

            // Initialize with render panel handle
            int panelWidth = _pnlRenderArea.Width;
            int panelHeight = _pnlRenderArea.Height;
            IntPtr hwnd = _pnlRenderArea.Handle;

            Debug.WriteLine($"[DX12Test] Initializing: HWND=0x{hwnd:X}, Size={panelWidth}x{panelHeight}");

            _renderHost.Initialize(new RenderHostInitOptions(hwnd, panelWidth, panelHeight, VSync: true));

            Debug.WriteLine("[DX12Test] Initialization complete");
            UpdateStatus("Running (DX12)", DrawingColor.LimeGreen);

            _btnStartDx12Test.Enabled = false;
            _btnStopDx12Test.Enabled = true;
            _chkEnableDebugLayer.Enabled = false;

            // Start render loop
            _isRendering = true;
            _frameCount = 0;
            _fpsAccumulator = 0;
            _frameTimer.Restart();

            StartRenderLoop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DX12Test] EXCEPTION: {ex}");
            UpdateStatus($"Error: {ex.Message}", DrawingColor.Red);

            _renderHost?.Dispose();
            _renderHost = null;
        }
    }

    private void BtnStopDx12Test_Click(object? sender, EventArgs e)
    {
        StopRendering();
        _chkEnableDebugLayer.Enabled = true;
    }

    /// <summary>
    /// Start the render loop using Application.Idle for best WinForms integration.
    /// </summary>
    private void StartRenderLoop()
    {
        Application.Idle += RenderLoop_Idle;
    }

    private void RenderLoop_Idle(object? sender, EventArgs e)
    {
        if (!_isRendering || _renderHost is null)
        {
            Application.Idle -= RenderLoop_Idle;
            return;
        }

        // Check if device is lost
        if (_renderHost is Dx12RenderHost dx12Host && dx12Host.IsDeviceLost)
        {
            UpdateStatus("Device Lost!", DrawingColor.Red);
            Debug.WriteLine("[DX12Test] Device lost detected, stopping render loop");
            Application.Idle -= RenderLoop_Idle;
            return;
        }

        RenderFrame();
    }

    /// <summary>
    /// Render a single frame with diagnostic ImGui overlay.
    /// </summary>
    private void RenderFrame()
    {
        if (_renderHost is null || !_isRendering)
            return;

        // Calculate delta time
        double elapsedMs = _frameTimer.Elapsed.TotalMilliseconds;
        _frameTimer.Restart();
        float deltaTime = (float)(elapsedMs / 1000.0);
        if (deltaTime <= 0) deltaTime = 1.0f / 60.0f;

        // Update FPS counter
        _frameCount++;
        _fpsAccumulator += elapsedMs;
        if (_fpsAccumulator >= 500.0) // Update FPS display every 0.5 seconds
        {
            _lastFps = _frameCount / (_fpsAccumulator / 1000.0);
            _frameCount = 0;
            _fpsAccumulator = 0;
        }

        // Create minimal input snapshot
        var mousePos = _pnlRenderArea.PointToClient(Cursor.Position);
        var inputSnapshot = new InputSnapshot
        {
            DeltaTime = deltaTime,
            MousePosition = new Vector2(mousePos.X, mousePos.Y),
            MouseButtons = MouseButtonState.None,
            Modifiers = KeyModifiers.None,
            WheelDelta = 0,
            TextInput = [],
            KeysPressed = [],
            KeysReleased = []
        };

        try
        {
            // Update input for ImGui
            _renderHost.UpdateInput(inputSnapshot);

            // Begin frame (clears to red for diagnostic, sets viewport/scissor)
            _renderHost.BeginFrame();

            // Clear with a visible color for diagnostic
            if (_renderHost is Dx12RenderHost host)
            {
                // Clear with gray to verify basic rendering
                host.Clear(new Color4(0.2f, 0.2f, 0.25f, 1.0f));
            }

            // End frame (renders ImGui, resolves MSAA, presents)
            _renderHost.EndFrame();

            // Update UI (throttled)
            if (_frameCount % 10 == 0)
            {
                UpdateFrameInfo();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DX12Test] Render exception: {ex.Message}");
            UpdateStatus($"Render Error: {ex.Message}", DrawingColor.Orange);
        }
    }

    private void UpdateStatus(string text, DrawingColor color)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateStatus(text, color));
            return;
        }

        _lblStatus.Text = $"Status: {text}";
        _lblStatus.ForeColor = color;
    }

    private void UpdateFrameInfo()
    {
        if (InvokeRequired)
        {
            BeginInvoke(UpdateFrameInfo);
            return;
        }

        string hostType = _renderHost switch
        {
            Dx12RenderHost => "DX12",
            _ => "Unknown"
        };

        _lblFrameInfo.Text = $"FPS: {_lastFps:F1} | Renderer: {hostType} | Panel: {_pnlRenderArea.Width}x{_pnlRenderArea.Height}";
    }
}
