using RQSimulation.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main - Physics Module Checkboxes to Pipeline synchronization.
/// Wires module checkboxes to pipeline IsEnabled state.
/// </summary>
public partial class Form_Main_RqSim
{
    // === Module Checkbox to Pipeline Name Mapping ===
    private readonly Dictionary<string, string> _moduleCheckboxMapping = new()
    {
        ["chkQuantumDriven"] = "Quantum Driven States",
        ["chkSpacetimePhysics"] = "Spacetime Physics",
        ["chkSpinorField"] = "Spinor Field",
        ["chkVacuumFluctuations"] = "Vacuum Fluctuations",
        ["chkBlackHolePhysics"] = "Black Hole Physics",
        ["chkYangMillsGauge"] = "Yang-Mills Gauge",
        ["chkEnhancedKleinGordon"] = "Enhanced Klein-Gordon",
        ["chkInternalTime"] = "Internal Time",
        ["chkSpectralGeometry"] = "Spectral Geometry",
        ["chkQuantumGraphity"] = "Quantum Graphity",
        ["chkRelationalTime"] = "Relational Time",
        ["chkRelationalYangMills"] = "Relational Yang-Mills",
        ["chkNetworkGravity"] = "Network Gravity",
        ["chkUnifiedPhysicsStep"] = "Unified Physics Step",
        ["chkEnforceGaugeConstraints"] = "Enforce Gauge Constraints",
        ["chkCausalRewiring"] = "Causal Rewiring",
        ["chkTopologicalProtection"] = "Topological Protection",
        ["chkValidateEnergyConservation"] = "Validate Energy Conservation",
        ["chkMexicanHatPotential"] = "Mexican Hat Potential",
        ["chkGeometryMomenta"] = "Geometry Momenta",
        ["chkTopologicalCensorship"] = "Topological Censorship"
    };

    /// <summary>
    /// Wires all physics module checkboxes to pipeline synchronization handlers.
    /// Call this after InitializeComponent() in the constructor.
    /// </summary>
    private void WireModuleCheckboxHandlers()
    {
        // Wire each checkbox to the common handler
        WireCheckbox(chkQuantumDriven);
        WireCheckbox(chkSpacetimePhysics);
        WireCheckbox(chkSpinorField);
        WireCheckbox(chkVacuumFluctuations);
        WireCheckbox(chkBlackHolePhysics);
        WireCheckbox(chkYangMillsGauge);
        WireCheckbox(chkEnhancedKleinGordon);
        WireCheckbox(chkInternalTime);
        WireCheckbox(chkSpectralGeometry);
        WireCheckbox(chkQuantumGraphity);
        WireCheckbox(chkRelationalTime);
        WireCheckbox(chkRelationalYangMills);
        WireCheckbox(chkNetworkGravity);
        WireCheckbox(chkUnifiedPhysicsStep);
        WireCheckbox(chkEnforceGaugeConstraints);
        WireCheckbox(chkCausalRewiring);
        WireCheckbox(chkTopologicalProtection);
        WireCheckbox(chkValidateEnergyConservation);
        WireCheckbox(chkMexicanHatPotential);
        WireCheckbox(chkGeometryMomenta);
        WireCheckbox(chkTopologicalCensorship);
    }

    private void WireCheckbox(CheckBox? checkbox)
    {
        if (checkbox is null) return;
        checkbox.CheckedChanged += OnModuleCheckboxChanged;
    }

    /// <summary>
    /// Handler for physics module checkbox changes.
    /// Immediately applies to pipeline if simulation is running.
    /// Also updates the UniPipeline DataGridView.
    /// </summary>
    private void OnModuleCheckboxChanged(object? sender, EventArgs e)
    {
        if (sender is not CheckBox checkbox) return;
        if (_eventsSupressed) return;

        var pipeline = _simApi.Pipeline;
        if (pipeline is null) return;

        // Find module name from checkbox
        if (!_moduleCheckboxMapping.TryGetValue(checkbox.Name, out var moduleName))
        {
            // Try to derive from checkbox text
            moduleName = checkbox.Text;
        }

        // Find module in pipeline
        var module = pipeline.GetModule(moduleName);
        if (module is null)
        {
            // Module may not be loaded yet
            return;
        }

        // Update module state
        if (module.IsEnabled != checkbox.Checked)
        {
            module.IsEnabled = checkbox.Checked;
            var state = checkbox.Checked ? "ENABLED" : "DISABLED";

            if (_isModernRunning)
            {
                AppendSimConsole($"[Module] {moduleName} -> {state}\n");
            }

            // Sync with UniPipeline DataGridView
            SyncModuleStateToDataGridView(moduleName, checkbox.Checked);
        }
    }

    /// <summary>
    /// Synchronizes a single module's enabled state to the _dgvModules DataGridView.
    /// </summary>
    private void SyncModuleStateToDataGridView(string moduleName, bool isEnabled)
    {
        if (_dgvModules is null) return;

        foreach (DataGridViewRow row in _dgvModules.Rows)
        {
            if (row.Tag is IPhysicsModule module &&
                string.Equals(module.Name, moduleName, StringComparison.OrdinalIgnoreCase))
            {
                if (row.Cells[_colEnabled.Index].Value is bool currentValue && currentValue != isEnabled)
                {
                    row.Cells[_colEnabled.Index].Value = isEnabled;
                }
                break;
            }
        }
    }

    /// <summary>
    /// Synchronizes all module checkboxes with current pipeline state.
    /// Call after pipeline is initialized or loaded from config.
    /// </summary>
    private void SyncModuleCheckboxesWithPipeline()
    {
        var pipeline = _simApi.Pipeline;
        if (pipeline is null) return;

        SuspendControlEvents();
        try
        {
            foreach (var (checkboxName, moduleName) in _moduleCheckboxMapping)
            {
                var module = pipeline.GetModule(moduleName);
                if (module is null) continue;

                // Find checkbox by name
                var checkbox = FindCheckboxByName(checkboxName);
                if (checkbox is not null)
                {
                    checkbox.Checked = module.IsEnabled;
                }
            }
        }
        finally
        {
            ResumeControlEvents();
        }
    }

    private CheckBox? FindCheckboxByName(string name)
    {
        return name switch
        {
            "chkQuantumDriven" => chkQuantumDriven,
            "chkSpacetimePhysics" => chkSpacetimePhysics,
            "chkSpinorField" => chkSpinorField,
            "chkVacuumFluctuations" => chkVacuumFluctuations,
            "chkBlackHolePhysics" => chkBlackHolePhysics,
            "chkYangMillsGauge" => chkYangMillsGauge,
            "chkEnhancedKleinGordon" => chkEnhancedKleinGordon,
            "chkInternalTime" => chkInternalTime,
            "chkSpectralGeometry" => chkSpectralGeometry,
            "chkQuantumGraphity" => chkQuantumGraphity,
            "chkRelationalTime" => chkRelationalTime,
            "chkRelationalYangMills" => chkRelationalYangMills,
            "chkNetworkGravity" => chkNetworkGravity,
            "chkUnifiedPhysicsStep" => chkUnifiedPhysicsStep,
            "chkEnforceGaugeConstraints" => chkEnforceGaugeConstraints,
            "chkCausalRewiring" => chkCausalRewiring,
            "chkTopologicalProtection" => chkTopologicalProtection,
            "chkValidateEnergyConservation" => chkValidateEnergyConservation,
            "chkMexicanHatPotential" => chkMexicanHatPotential,
            "chkGeometryMomenta" => chkGeometryMomenta,
            "chkTopologicalCensorship" => chkTopologicalCensorship,
            _ => null
        };
    }

    /// <summary>
    /// Applies all current module checkbox states to the pipeline.
    /// Use when pipeline is (re)initialized.
    /// </summary>
    private void ApplyModuleCheckboxesToPipeline()
    {
        var pipeline = _simApi.Pipeline;
        if (pipeline is null) return;

        foreach (var (checkboxName, moduleName) in _moduleCheckboxMapping)
        {
            var checkbox = FindCheckboxByName(checkboxName);
            if (checkbox is null) continue;

            var module = pipeline.GetModule(moduleName);
            if (module is null) continue;

            module.IsEnabled = checkbox.Checked;
        }

        AppendSysConsole("[Pipeline] Module states synchronized from UI\n");
    }

    /// <summary>
    /// Synchronizes all physics panel checkboxes to the pipeline and refreshes UniPipeline grid.
    /// Called from button_ApplyPipelineConfSet_Click.
    /// </summary>
    private void SyncPhysicsCheckboxesToPipelineAndGrid()
    {
        var pipeline = _simApi.Pipeline;
        if (pipeline is null) return;

        int changedCount = 0;

        foreach (var (checkboxName, moduleName) in _moduleCheckboxMapping)
        {
            var checkbox = FindCheckboxByName(checkboxName);
            if (checkbox is null) continue;

            var module = pipeline.GetModule(moduleName);
            if (module is null) continue;

            if (module.IsEnabled != checkbox.Checked)
            {
                module.IsEnabled = checkbox.Checked;
                changedCount++;

                var state = checkbox.Checked ? "ENABLED" : "DISABLED";
                AppendSimConsole($"  * {moduleName} -> {state}\n");
            }
        }

        // Refresh UniPipeline DataGridView to reflect all changes
        RefreshUniPipelineModuleList();

        if (changedCount > 0)
        {
            AppendSysConsole($"[Pipeline] Synchronized {changedCount} module states from physics checkboxes\n");
        }
    }
}
