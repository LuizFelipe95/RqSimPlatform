using ComputeSharp;
using System.Diagnostics;

namespace RqSimForms;

/// <summary>
/// Multi-GPU cluster UI integration for Form_Main.
/// Handles GPU enumeration, role assignment, and cluster lifecycle via UI controls.
/// </summary>
partial class Form_Main_RqSim
{
    /// <summary>
    /// All enumerated hardware-accelerated GPUs.
    /// </summary>
    private List<GpuDeviceInfo> _availableGpus = [];

    /// <summary>
    /// Initialize Multi-GPU UI controls after device enumeration.
    /// Call after InitializeGpuControls().
    /// </summary>
    private void InitializeMultiGpuControls()
    {
        EnumerateGpuDevices();
        PopulateMultiGpuLists();
        UpdateMultiGpuSettingsState();
    }

    /// <summary>
    /// Enumerate all hardware-accelerated GPUs.
    /// </summary>
    private void EnumerateGpuDevices()
    {
        _availableGpus.Clear();

        try
        {
            int index = 0;
            foreach (var device in GraphicsDevice.EnumerateDevices())
            {
                if (device.IsHardwareAccelerated)
                {
                    _availableGpus.Add(new GpuDeviceInfo
                    {
                        Index = index,
                        Name = device.Name,
                        SupportsDoublePrecision = device.IsDoublePrecisionSupportAvailable()
                    });
                    index++;
                }
            }

            AppendSysConsole($"[MultiGPU] Found {_availableGpus.Count} hardware-accelerated GPU(s)\n");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MultiGPU] GPU enumeration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Populate all Multi-GPU combo boxes with real device names.
    /// </summary>
    private void PopulateMultiGpuLists()
    {
        // Physics GPU selector
        comboBox_MultiGpu_PhysicsGPU.Items.Clear();
        foreach (GpuDeviceInfo gpu in _availableGpus)
        {
            string suffix = gpu.SupportsDoublePrecision ? " [FP64]" : "";
            comboBox_MultiGpu_PhysicsGPU.Items.Add($"GPU {gpu.Index}: {gpu.Name}{suffix}");
        }

        if (comboBox_MultiGpu_PhysicsGPU.Items.Count > 0)
        {
            comboBox_MultiGpu_PhysicsGPU.SelectedIndex = 0;
        }

        // Background pipeline GPU selector
        comboBox_BackgroundPipelineGPU.Items.Clear();
        comboBox_BackgroundPipelineGPU.Items.Add("Auto (next available)");
        for (int i = 0; i < _availableGpus.Count; i++)
        {
            GpuDeviceInfo gpu = _availableGpus[i];
            string suffix = gpu.SupportsDoublePrecision ? " [FP64]" : "";
            string role = i == 0 ? " (Physics)" : " (Background)";
            comboBox_BackgroundPipelineGPU.Items.Add($"GPU {i}: {gpu.Name}{suffix}{role}");
        }

        if (comboBox_BackgroundPipelineGPU.Items.Count > 0)
        {
            comboBox_BackgroundPipelineGPU.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// Update enabled state of Multi-GPU controls based on current settings.
    /// </summary>
    private void UpdateMultiGpuSettingsState()
    {
        bool gpuEnabled = checkBox_EnableGPU.Checked && _availableGpus.Count > 0;
        bool multiGpuPossible = gpuEnabled && _availableGpus.Count >= 2;

        checkBox_UseMultiGPU.Enabled = multiGpuPossible;
        comboBox_MultiGpu_PhysicsGPU.Enabled = gpuEnabled;

        if (_availableGpus.Count <= 1)
        {
            checkBox_UseMultiGPU.Text = "Multi GPU (requires 2+ GPUs)";
        }
        else
        {
            checkBox_UseMultiGPU.Text = $"Multi GPU Cluster ({_availableGpus.Count} GPUs)";
        }
    }

    /// <summary>
    /// GPU device information for UI display.
    /// </summary>
    private sealed class GpuDeviceInfo
    {
        public int Index { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool SupportsDoublePrecision { get; init; }
    }
}
