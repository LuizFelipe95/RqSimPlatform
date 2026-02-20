using RqSimForms.Forms.Interfaces;
using RqSimForms.ProcessesDispatcher.Contracts;
using RqSimForms.ProcessesDispatcher.Managers;
using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace RqSimVisualization;

partial class RqSimVisualizationForm
{
    /// <summary>
    /// Synchronizes UI to external simulation that was already running when UI attached.
    /// Updates button states and starts render timer to display external simulation data.
    /// If simulation is stopped, automatically sends Settings and START command.
    /// </summary>
    private void SyncToExternalSimulation(SimState state)
    {
        AppendSysConsole($"[SyncToExternalSimulation] STARTING: Setting _isExternalSimulation=true, _isConsoleBound=true\n");

        _isExternalSimulation = true;
        _isConsoleBound = true; // Mark as bound so button clicks route to console commands

        // FIX 33: Sync the Bind Console button visual state
        UpdateConsoleBindButton(true);

        // Start UI update timer to read render nodes from shared memory and populate _externalNodesBuffer.
        // This is essential for the GDI+ 3D visualization (Timer3D_Tick) to receive node data.
        AppendSysConsole($"[SyncToExternalSimulation] Starting UI update timer...\n");
        StartUiUpdateTimer();

        // Attach shared memory for reading simulation state (required for HandleConsoleBoundSimulationAsync)
        AppendSysConsole($"[SyncToExternalSimulation] Attaching shared memory...\n");
        TryAttachConsoleSharedMemory(force: true);

        // Update UI button state based on external simulation status.
        switch (state.Status)
        {
            case SimulationStatus.Running:
                _isModernRunning = true;
                if (button_RunModernSim is not null) button_RunModernSim.Text = "Pause Console Sim";
                AppendSysConsole($"[Dispatcher] Attached to RUNNING simulation: Iteration={state.Iteration}, Nodes={state.NodeCount}\n");
                break;

            case SimulationStatus.Paused:
                _isModernRunning = false;
                if (button_RunModernSim is not null) button_RunModernSim.Text = "Resume Console Sim";
                AppendSysConsole($"[Dispatcher] Attached to PAUSED simulation: Iteration={state.Iteration}, Nodes={state.NodeCount}. Press Start to resume.\n");
                break;

            default:
                // Simulation is STOPPED - automatically start it!
                _isModernRunning = false;
                if (button_RunModernSim is not null) button_RunModernSim.Text = "Starting...";
                AppendSysConsole($"[Dispatcher] Attached to STOPPED simulation - AUTO-STARTING...\n");
                
                // Fire and forget async auto-start (don't block UI thread)
                _ = AutoStartConsoleSimulationAsync();
                break;
        }


    }

    // Ensures panel has focus to receive keyboard events (DX12 input adapter relies on WinForms events)
    private void EnsureRenderPanelFocus()
    {
        // Prefer DX12 render panel.
        var panel = _dx12Panel;
        if (panel is null)
            return;

        panel.TabStop = true;
        panel.Focus();
    }

    // Call after backend restart.
    private void AfterBackendRestartUi()
    {
        EnsureRenderPanelFocus();
        UpdateMainFormStatusBar();

        Debug.WriteLine($"[RenderBackend] AfterBackendRestartUi (Active={_activeBackend})");
    }
}
