using RQSimulation.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main - Real-time Physics Module Changes with Confirmation.
/// Handles confirmation dialogs when changing physics modules during simulation.
/// </summary>
public partial class Form_Main_RqSim
{
    /// <summary>
    /// Shows confirmation dialog before applying physics module changes during running simulation.
    /// </summary>
    /// <param name="moduleName">The name of the module being changed.</param>
    /// <param name="newState">True if enabling, false if disabling.</param>
    /// <returns>True if user confirms the change, false otherwise.</returns>
    private bool ConfirmPhysicsModuleChange(string moduleName, bool newState)
    {
        if (!_isModernRunning)
        {
            return true; // No confirmation needed when simulation is not running
        }

        string action = newState ? "enable" : "disable";
        string warning = GetModuleChangeWarning(moduleName, newState);

        var result = MessageBox.Show(
            $"Do you want to {action} the '{moduleName}' module?\n\n" +
            $"{warning}\n\n" +
            "This change will be applied immediately to the running simulation.",
            "Confirm Physics Module Change",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        return result == DialogResult.Yes;
    }

    /// <summary>
    /// Gets a warning message specific to the module being changed.
    /// </summary>
    private static string GetModuleChangeWarning(string moduleName, bool enabling)
    {
        return moduleName switch
        {
            "Quantum Driven States" => enabling
                ? "Enabling quantum driven states may introduce quantum fluctuations in the graph."
                : "Disabling quantum states may cause the simulation to lose quantum coherence.",

            "Spacetime Physics" => enabling
                ? "Spacetime physics adds relativistic effects to the simulation."
                : "Disabling spacetime physics removes relativistic corrections.",

            "Black Hole Physics" => enabling
                ? "WARNING: Black hole physics can cause significant topology changes."
                : "Disabling black hole physics will stop horizon formation.",

            "Vacuum Fluctuations" => enabling
                ? "Vacuum fluctuations add energy perturbations to the system."
                : "Removing vacuum fluctuations may reduce system stability.",

            "Yang-Mills Gauge" => enabling
                ? "Enabling Yang-Mills adds gauge field dynamics."
                : "Disabling Yang-Mills removes gauge constraint enforcement.",

            "Network Gravity" => enabling
                ? "Network gravity adds gravitational effects based on graph topology."
                : "Disabling network gravity removes curvature-based forces.",

            "Topological Protection" => enabling
                ? "Topological protection prevents unauthorized topology changes."
                : "WARNING: Disabling protection may allow invalid topologies.",

            "Validate Energy Conservation" => enabling
                ? "Enabling validation will check energy conservation each step."
                : "WARNING: Disabling validation may hide energy non-conservation.",

            _ => enabling
                ? $"Enabling {moduleName} will add its effects to the simulation."
                : $"Disabling {moduleName} will remove its effects from the simulation."
        };
    }

    /// <summary>
    /// Enhanced handler for physics module checkbox changes with confirmation.
    /// Replaces the basic OnModuleCheckboxChanged when confirmation is required.
    /// </summary>
    private void OnModuleCheckboxChangedWithConfirmation(object? sender, EventArgs e)
    {
        if (sender is not CheckBox checkbox) return;
        if (_eventsSupressed) return;

        var pipeline = _simApi.Pipeline;
        if (pipeline is null) return;

        // Find module name from checkbox
        if (!_moduleCheckboxMapping.TryGetValue(checkbox.Name, out var moduleName))
        {
            moduleName = checkbox.Text;
        }

        // Find module in pipeline
        var module = pipeline.GetModule(moduleName);
        if (module is null)
        {
            return;
        }

        // Check if state actually changed
        if (module.IsEnabled == checkbox.Checked)
        {
            return;
        }

        // Show confirmation if simulation is running
        if (_isModernRunning)
        {
            if (!ConfirmPhysicsModuleChange(moduleName, checkbox.Checked))
            {
                // User cancelled - revert checkbox state
                SuspendControlEvents();
                try
                {
                    checkbox.Checked = module.IsEnabled;
                }
                finally
                {
                    ResumeControlEvents();
                }
                return;
            }
        }

        // Apply the change
        module.IsEnabled = checkbox.Checked;
        var state = checkbox.Checked ? "ENABLED" : "DISABLED";

        if (_isModernRunning)
        {
            AppendSimConsole($"[Module] {moduleName} -> {state} (applied to running simulation)\n");
        }
        else
        {
            AppendSimConsole($"[Module] {moduleName} -> {state}\n");
        }

        // Sync with UniPipeline DataGridView
        SyncModuleStateToDataGridView(moduleName, checkbox.Checked);
    }

    /// <summary>
    /// Rewires module checkboxes to use confirmation-enabled handlers.
    /// Call this after WireModuleCheckboxHandlers() to upgrade to confirmation mode.
    /// </summary>
    private void EnableModuleChangeConfirmation()
    {
        // Unwire basic handlers and wire confirmation handlers
        UnwireCheckboxBasic(chkQuantumDriven);
        UnwireCheckboxBasic(chkSpacetimePhysics);
        UnwireCheckboxBasic(chkSpinorField);
        UnwireCheckboxBasic(chkVacuumFluctuations);
        UnwireCheckboxBasic(chkBlackHolePhysics);
        UnwireCheckboxBasic(chkYangMillsGauge);
        UnwireCheckboxBasic(chkEnhancedKleinGordon);
        UnwireCheckboxBasic(chkInternalTime);
        UnwireCheckboxBasic(chkSpectralGeometry);
        UnwireCheckboxBasic(chkQuantumGraphity);
        UnwireCheckboxBasic(chkRelationalTime);
        UnwireCheckboxBasic(chkRelationalYangMills);
        UnwireCheckboxBasic(chkNetworkGravity);
        UnwireCheckboxBasic(chkUnifiedPhysicsStep);
        UnwireCheckboxBasic(chkEnforceGaugeConstraints);
        UnwireCheckboxBasic(chkCausalRewiring);
        UnwireCheckboxBasic(chkTopologicalProtection);
        UnwireCheckboxBasic(chkValidateEnergyConservation);
        UnwireCheckboxBasic(chkMexicanHatPotential);
        UnwireCheckboxBasic(chkGeometryMomenta);
        UnwireCheckboxBasic(chkTopologicalCensorship);

        // Wire confirmation handlers
        WireCheckboxWithConfirmation(chkQuantumDriven);
        WireCheckboxWithConfirmation(chkSpacetimePhysics);
        WireCheckboxWithConfirmation(chkSpinorField);
        WireCheckboxWithConfirmation(chkVacuumFluctuations);
        WireCheckboxWithConfirmation(chkBlackHolePhysics);
        WireCheckboxWithConfirmation(chkYangMillsGauge);
        WireCheckboxWithConfirmation(chkEnhancedKleinGordon);
        WireCheckboxWithConfirmation(chkInternalTime);
        WireCheckboxWithConfirmation(chkSpectralGeometry);
        WireCheckboxWithConfirmation(chkQuantumGraphity);
        WireCheckboxWithConfirmation(chkRelationalTime);
        WireCheckboxWithConfirmation(chkRelationalYangMills);
        WireCheckboxWithConfirmation(chkNetworkGravity);
        WireCheckboxWithConfirmation(chkUnifiedPhysicsStep);
        WireCheckboxWithConfirmation(chkEnforceGaugeConstraints);
        WireCheckboxWithConfirmation(chkCausalRewiring);
        WireCheckboxWithConfirmation(chkTopologicalProtection);
        WireCheckboxWithConfirmation(chkValidateEnergyConservation);
        WireCheckboxWithConfirmation(chkMexicanHatPotential);
        WireCheckboxWithConfirmation(chkGeometryMomenta);
        WireCheckboxWithConfirmation(chkTopologicalCensorship);

        AppendSysConsole("[Pipeline] Module change confirmation enabled\n");
    }

    private void UnwireCheckboxBasic(CheckBox? checkbox)
    {
        if (checkbox is null) return;
        checkbox.CheckedChanged -= OnModuleCheckboxChanged;
    }

    private void WireCheckboxWithConfirmation(CheckBox? checkbox)
    {
        if (checkbox is null) return;
        checkbox.CheckedChanged += OnModuleCheckboxChangedWithConfirmation;
    }

    /// <summary>
    /// Batch applies multiple module state changes with single confirmation.
    /// Use when applying a preset or loading configuration.
    /// </summary>
    /// <param name="moduleStates">Dictionary of module names to their desired states.</param>
    /// <returns>True if changes were applied, false if cancelled.</returns>
    private bool ApplyModuleStatesBatch(Dictionary<string, bool> moduleStates)
    {
        if (moduleStates.Count == 0) return true;

        // Build list of changes
        var pipeline = _simApi.Pipeline;
        if (pipeline is null) return false;

        var changes = new List<(string Name, bool OldState, bool NewState)>();
        foreach (var (moduleName, newState) in moduleStates)
        {
            var module = pipeline.GetModule(moduleName);
            if (module is not null && module.IsEnabled != newState)
            {
                changes.Add((moduleName, module.IsEnabled, newState));
            }
        }

        if (changes.Count == 0) return true;

        // Show batch confirmation if running
        if (_isModernRunning)
        {
            var enableList = string.Join("\n  • ", changes.Where(c => c.NewState).Select(c => c.Name));
            var disableList = string.Join("\n  • ", changes.Where(c => !c.NewState).Select(c => c.Name));

            var message = $"Apply {changes.Count} physics module change(s)?\n\n";
            if (!string.IsNullOrEmpty(enableList))
                message += $"Enable:\n  • {enableList}\n\n";
            if (!string.IsNullOrEmpty(disableList))
                message += $"Disable:\n  • {disableList}\n\n";
            message += "These changes will be applied immediately to the running simulation.";

            var result = MessageBox.Show(
                message,
                "Confirm Batch Physics Module Changes",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (result != DialogResult.Yes)
                return false;
        }

        // Apply all changes
        SuspendControlEvents();
        try
        {
            foreach (var (moduleName, _, newState) in changes)
            {
                var module = pipeline.GetModule(moduleName);
                if (module is not null)
                {
                    module.IsEnabled = newState;
                    var state = newState ? "ENABLED" : "DISABLED";
                    AppendSimConsole($"[Module] {moduleName} -> {state}\n");

                    // Update checkbox
                    var checkboxName = _moduleCheckboxMapping
                        .FirstOrDefault(kvp => kvp.Value == moduleName).Key;
                    if (!string.IsNullOrEmpty(checkboxName))
                    {
                        var checkbox = FindCheckboxByName(checkboxName);
                        if (checkbox is not null)
                            checkbox.Checked = newState;
                    }

                    // Sync DataGridView
                    SyncModuleStateToDataGridView(moduleName, newState);
                }
            }
        }
        finally
        {
            ResumeControlEvents();
        }

        AppendSimConsole($"[Module] Applied {changes.Count} module changes\n");
        return true;
    }
}
