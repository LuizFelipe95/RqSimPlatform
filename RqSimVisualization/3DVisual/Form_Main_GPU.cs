using RqSimForms.Forms.Interfaces;
using RQSimulation;
using RQSimulation.Analysis;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Linq;
using RqSimUI.FormSimAPI.Interfaces;

namespace RqSimVisualization;

public partial class RqSimVisualizationForm
{
    /// <summary>
    /// Инициализирует контролы GPU: заполняет comboBox_GPUIndex доступными устройствами
    /// </summary>
    private void InitializeGpuControls()
    {
        comboBox_GPUIndex.Items.Clear();

        try
        {
            // Проверяем доступность GPU через ComputeSharp
            var defaultDevice = ComputeSharp.GraphicsDevice.GetDefault();
            _simApi.GpuAvailable = defaultDevice != null;

            if (_simApi.GpuAvailable)
            {
                // Добавляем устройство по умолчанию
                comboBox_GPUIndex.Items.Add($"0: {defaultDevice.Name}");

                // Пытаемся получить все устройства
                int deviceIndex = 0;
                foreach (var device in ComputeSharp.GraphicsDevice.EnumerateDevices())
                {
                    if (deviceIndex > 0) // Первое уже добавлено
                    {
                        comboBox_GPUIndex.Items.Add($"{deviceIndex}: {device.Name}");
                    }
                    deviceIndex++;
                }

                comboBox_GPUIndex.SelectedIndex = 0;
                AppendSysConsole($"[GPU] Обнаружено устройств: {comboBox_GPUIndex.Items.Count}\n");
                AppendSysConsole($"[GPU] Устройство по умолчанию: {defaultDevice.Name}\n");
            }
            else
            {
                comboBox_GPUIndex.Items.Add("Нет доступных GPU");
                comboBox_GPUIndex.SelectedIndex = 0;
                checkBox_EnableGPU.Checked = false;
                checkBox_EnableGPU.Enabled = false;
            }
        }
        catch (Exception ex)
        {
            _simApi.GpuAvailable = false;
            comboBox_GPUIndex.Items.Add("GPU недоступен");
            comboBox_GPUIndex.SelectedIndex = 0;
            checkBox_EnableGPU.Checked = false;
            checkBox_EnableGPU.Enabled = false;
            AppendSysConsole($"[GPU] Ошибка инициализации: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Проверяет доступность GPU и возвращает true если можно использовать GPU ускорение
    /// </summary>
    private bool CanUseGpu()
    {
        return _simApi.GpuAvailable && checkBox_EnableGPU.Checked;
    }

    private void checkBox_EnableGPU_CheckedChanged(object sender, EventArgs e)
    {
        // Здесь можно добавить логику для обработки изменения состояния GPU
        AppendSysConsole($"GPU ускорение {(checkBox_EnableGPU.Checked ? "включено" : "выключено")}.\n");
    }

    // Helper to append to GPU console
    private void AppendGPUConsole(string text)
    {
        if (textBox_SimConsole.InvokeRequired)
        {
            textBox_SimConsole.BeginInvoke(new Action(() => AppendGPUConsole(text)));
        }
        else
        {
            if (textBox_SimConsole.TextLength > 50000)
            {
                textBox_SimConsole.Clear();
                textBox_SimConsole.AppendText("[Console cleared due to size limit]\n");
            }
            textBox_SimConsole.AppendText(text);
            if (checkBox_AutoScrollSysConsole.Checked)
            {
                textBox_SimConsole.SelectionStart = textBox_SimConsole.TextLength;
                textBox_SimConsole.ScrollToCaret();
            }
        }
    }

}

