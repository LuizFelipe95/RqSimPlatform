using System.Diagnostics;

namespace RqSimForms;

/// <summary>
/// GPU device enumeration and initialization for Form_Main.
/// Populates GPU combo boxes with real hardware device names via ComputeSharp.
/// </summary>
partial class Form_Main_RqSim
{
    /// <summary>
    /// Initializes GPU controls: populates comboBox1 (rendering GPU) with real devices.
    /// </summary>
    private void InitializeGpuControls()
    {
        comboBox_3DRenderingGPU.Items.Clear();

        try
        {
            var defaultDevice = ComputeSharp.GraphicsDevice.GetDefault();
            _simApi.GpuAvailable = defaultDevice is not null;

            if (_simApi.GpuAvailable)
            {
                comboBox_3DRenderingGPU.Items.Add($"0: {defaultDevice.Name}");

                int deviceIndex = 0;
                foreach (var device in ComputeSharp.GraphicsDevice.EnumerateDevices())
                {
                    if (deviceIndex > 0)
                    {
                        comboBox_3DRenderingGPU.Items.Add($"{deviceIndex}: {device.Name}");
                    }
                    deviceIndex++;
                }

                comboBox_3DRenderingGPU.SelectedIndex = 0;
                AppendSysConsole($"[GPU] Devices found: {comboBox_3DRenderingGPU.Items.Count}\n");
                AppendSysConsole($"[GPU] Default device: {defaultDevice.Name}\n");
            }
            else
            {
                comboBox_3DRenderingGPU.Items.Add("No GPU available");
                comboBox_3DRenderingGPU.SelectedIndex = 0;
                checkBox_EnableGPU.Checked = false;
                checkBox_EnableGPU.Enabled = false;
            }
        }
        catch (Exception ex)
        {
            _simApi.GpuAvailable = false;
            comboBox_3DRenderingGPU.Items.Add("GPU unavailable");
            comboBox_3DRenderingGPU.SelectedIndex = 0;
            checkBox_EnableGPU.Checked = false;
            checkBox_EnableGPU.Enabled = false;
            Debug.WriteLine($"[GPU] Init error: {ex.Message}");
        }
    }
}
