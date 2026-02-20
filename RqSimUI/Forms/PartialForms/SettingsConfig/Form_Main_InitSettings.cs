using RqSimForms.Forms.Interfaces;
using System.Diagnostics;
using System.Windows.Forms;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main - Settings initialization calls.
/// This file consolidates all Settings tab initialization in one place.
/// </summary>
public partial class Form_Main_RqSim
{
    /// <summary>
    /// Initializes all Settings tab controls.
    /// Call this from Form_Main constructor after InitializeComponent() and before other initializations.
    /// This method calls all Settings-related initialization methods in correct order.
    /// </summary>
    private void InitializeAllSettingsControls()
    {
        // 1. Initialize Graph Health controls (adds to tlpPhysicsConstants)
        InitializeGraphHealthControls();

        // 2. Initialize RQ-Hypothesis Experimental Flags (adds to flpPhysics)
        InitializeRQExperimentalFlagsControls();

        // 3. Initialize Advanced Physics controls (adds more to tlpPhysicsConstants)
        InitializeAdvancedPhysicsControls();

        // 4. Initialize All Physics Constants display panel (read-only reference)
        InitializeAllPhysicsConstantsDisplay();

        // 5. Sync UI with PhysicsConstants defaults
        SyncUIWithPhysicsConstants();
    }

    /// <summary>
    /// Handles the Run button — dispatches to console mode or local simulation.
    /// </summary>
    private async void button_RunModernSim_Click(object? sender, EventArgs e)
    {
        // If bound to console, route command there
        if (_isExternalSimulation)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!_isConsoleBound)
                    {
                        BeginInvoke(() => button_BindConsoleSession_Click(null, EventArgs.Empty));
                        await Task.Delay(300).ConfigureAwait(false);
                    }

                    if (_isConsoleBound)
                    {
                        await HandleConsoleBoundSimulationAsync().ConfigureAwait(false);
                        return;
                    }

                    bool started = await _lifeCycleManager.Ipc.SendStartAsync().ConfigureAwait(false);
                    if (!started)
                    {
                        Debug.WriteLine("[Console] Failed to send START command");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Console] Error: {ex.Message}");
                }
            });
            return;
        }

        // Local simulation mode
        if (_simApi.IsModernRunning)
        {
            Debug.WriteLine("[RunSim] Simulation already running");
            return;
        }

        // Prevent parallel sessions: check for a running console before starting local
        if (!await EnsureNoConflictingSessionAsync().ConfigureAwait(true))
        {
            Debug.WriteLine("[RunSim] Blocked by active console session");
            return;
        }

        // Wait for previous simulation task to fully complete before starting a new one.
        // This prevents re-entrancy where the old finally block overwrites new state.
        if (_modernSimTask is not null)
        {
            try { await _modernSimTask.ConfigureAwait(true); }
            catch { /* already handled inside the task */ }
            _modernSimTask = null;
        }

        try
        {
            button_RunModernSim.Enabled = false;
            button_TerminateSimSession.Enabled = true;

            // Reset visualization state before starting a new simulation
            // to clear stale data from any previous run
            _embeddedVisualizationForm?.ResetVisualization();

            var config = GetConfigFromUI();
            _simApi.CpuThreadCount = (int)numericUpDown1.Value;

            bool useGpu = checkBox_EnableGPU.Checked;
            bool useMultiGpu = checkBox_UseMultiGPU.Checked;

            int engineIdx = comboBox_GPUComputeEngine.SelectedIndex;
            GpuEngineType engineType = engineIdx switch
            {
                1 => GpuEngineType.Original,
                2 => GpuEngineType.Csr,
                3 => GpuEngineType.CpuOnly,
                _ => GpuEngineType.Auto
            };
            _simApi.SetGpuEngineType(engineType);

            _simApi.ClearTimeSeries();
            _simApi.SimulationComplete = false;
            _simApi.InitializeSimulation(config);
            _simApi.InitializeLiveConfig(config);
            _simApi.GpuAvailable = useGpu;

            if (useGpu) _simApi.InitializeGpuEngines();
            if (useMultiGpu) _simApi.InitializeMultiGpuCluster();
            if (_simApi.AutoTuningEnabled) _simApi.InitializeAutoTuning();

            _simApi.IsModernRunning = true;

            // Start session storage for metrics JSONL recording
            StartNewSession("local");

            // Notify embedded forms that simulation has started
            _embeddedVisualizationForm?.NotifySimulationStarted();
            _embeddedTelemetryForm?.EnsureTimerRunning();

            _modernCts?.Dispose();
            _modernCts = new CancellationTokenSource();
            var ct = _modernCts.Token;

            int totalSteps = config.TotalSteps;
            int nodeCount = config.NodeCount;
            int totalEvents = totalSteps * nodeCount;

            Debug.WriteLine($"[RunSim] Starting: {nodeCount} nodes, {totalSteps} steps, GPU={useGpu}");

            _modernSimTask = Task.Run(() =>
            {
                try
                {
                    _simApi.RunParallelEventBasedLoop(ct, totalEvents, useParallel: true, useGpu: useGpu);
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[RunSim] Cancelled");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[RunSim] Error: {ex.Message}");
                }
            });

            await _modernSimTask;

            _simApi.IsModernRunning = false;
            Debug.WriteLine($"[RunSim] Complete: d_S={_simApi.FinalSpectralDimension:F3}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RunSim] Setup error: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Simulation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _simApi.IsModernRunning = false;
            _modernSimTask = null;

            // Finalize session storage (flush metrics JSONL, update session_info.json)
            FinalizeCurrentSession();

            _embeddedVisualizationForm?.NotifySimulationStopped();
            button_RunModernSim.Enabled = true;

            // Keep terminate enabled if bound to console — user can still stop the remote sim
            if (!_isConsoleBound)
            {
                button_TerminateSimSession.Enabled = false;
            }
        }
    }

    private void checkBox_AutoTuning_CheckedChanged(object? sender, EventArgs e)
    {
        _simApi.AutoTuningEnabled = checkBox_AutoTuning.Checked;
        Debug.WriteLine($"[AutoTuning] {(checkBox_AutoTuning.Checked ? "Enabled" : "Disabled")}");

        if (checkBox_AutoTuning.Checked && _simApi.IsModernRunning)
        {
            _simApi.InitializeAutoTuning();
        }
    }

    private void checkBox_EnableGPU_CheckedChanged(object? sender, EventArgs e)
    {
        bool gpuEnabled = checkBox_EnableGPU.Checked;
        _simApi.GpuAvailable = gpuEnabled;

        comboBox_GPUComputeEngine.Enabled = gpuEnabled;
        checkBox_UseMultiGPU.Enabled = gpuEnabled;

        // Disable child controls inside GroupBox, but NOT the checkBox_EnableGPU itself
        foreach (Control ctrl in groupBox_MultiGpu_Settings.Controls)
        {
            if (ctrl != checkBox_EnableGPU)
            {
                ctrl.Enabled = gpuEnabled;
            }
        }

        if (!gpuEnabled)
        {
            checkBox_UseMultiGPU.Checked = false;
        }

        Debug.WriteLine($"[GPU] {(gpuEnabled ? "Enabled" : "Disabled")}");
    }

    private void checkBox_UseMultiGPU_CheckedChanged(object? sender, EventArgs e)
    {
        bool multiGpu = checkBox_UseMultiGPU.Checked;

        comboBox_BackgroundPipelineGPU.Enabled = multiGpu;
        numericUpDown_BackgroundPluginGPUKernels.Enabled = multiGpu;
        button_AddGpuBackgroundPluginToPipeline.Enabled = multiGpu;
        button_RemoveGpuBackgroundPluginToPipeline.Enabled = multiGpu;
        listView_AnaliticsGPU.Enabled = multiGpu;
        comboBox_MultiGpu_PhysicsGPU.Enabled = multiGpu;

        if (multiGpu)
        {
            _simApi.MultiGpuSettings.Enabled = true;
        }
        else
        {
            _simApi.MultiGpuSettings.Enabled = false;
            _simApi.DisposeMultiGpuCluster();
        }

        Debug.WriteLine($"[MultiGPU] {(multiGpu ? "Enabled" : "Disabled")}");
    }

    private void button_AddGpuBackgroundPluginToPipeline_Click(object? sender, EventArgs e)
    {
        string gpuName = comboBox_BackgroundPipelineGPU.SelectedItem?.ToString() ?? "Default";
        int kernels = (int)numericUpDown_BackgroundPluginGPUKernels.Value;

        var item = new ListViewItem(new[] { gpuName, "Physics Pipeline", kernels.ToString() });
        item.Group = listView_AnaliticsGPU.Groups["listViewGroup_GPU"];
        item.Checked = true;
        listView_AnaliticsGPU.Items.Add(item);

        Debug.WriteLine($"[MultiGPU] Added plugin: {gpuName}, kernels={kernels}");
    }

    private void button_RemoveGpuBackgroundPluginToPipeline_Click(object? sender, EventArgs e)
    {
        if (listView_AnaliticsGPU.SelectedItems.Count > 0)
        {
            var selected = listView_AnaliticsGPU.SelectedItems[0];
            Debug.WriteLine($"[MultiGPU] Removed plugin: {selected.Text}");
            listView_AnaliticsGPU.Items.Remove(selected);
        }
    }
}