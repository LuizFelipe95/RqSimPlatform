using System.Diagnostics;
using RqSimRenderingEngine.Abstractions;
using RqSimRenderingEngine.Rendering.Backend.DX12;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using RqSimUI.Rendering.Plugins;
using Vortice.Mathematics;
using DrawingColor = System.Drawing.Color;

namespace Dx12WinForm;

public partial class Dx12WinForm : Form
{
    private IRenderHost? _renderHost;
    private bool _isRendering;
    private readonly Stopwatch _frameTimer = new();
    private int _frameCount;
    private double _fpsAccumulator;
    private double _lastFps;

    public Dx12WinForm()
    {
        InitializeComponent();
        InitializeShaderModeComboBox();
    }

    private void InitializeShaderModeComboBox()
    {
        _cmbShaderMode.Items.Clear();
        foreach (var mode in Dx12RenderHost.AvailableShaderModes)
        {
            _cmbShaderMode.Items.Add(mode);
        }
        _cmbShaderMode.SelectedIndex = 0; // Production
    }

    private void CmbShaderMode_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_renderHost is Dx12RenderHost dx12Host && _cmbShaderMode.SelectedItem is ImGuiShaderMode mode)
        {
            dx12Host.ImGuiShaderMode = mode;
            Debug.WriteLine($"[DX12Test] Shader mode changed to: {mode}");
        }
    }

    private void Dx12WinForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        StopRendering();
    }

    private void StopRendering()
    {
        _isRendering = false;

        // Give render loop time to exit
        Application.DoEvents();

        _renderHost?.Dispose();
        _renderHost = null;

        UpdateStatus("Stopped", DrawingColor.Gray);
        _btnStartDx12Test.Enabled = true;
        _btnStopDx12Test.Enabled = false;
    }
}
