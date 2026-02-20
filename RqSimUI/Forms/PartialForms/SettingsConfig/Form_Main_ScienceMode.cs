using System.Diagnostics;
using RQSimulation;

namespace RqSimForms;

/// <summary>
/// Science Mode toggle — sets PhysicsConstants.ScientificMode and
/// disables Auto-Tuning (incompatible with strict validation).
/// </summary>
partial class Form_Main_RqSim
{
    private void checkBox_ScienceSimMode_CheckedChanged(object? sender, EventArgs e)
    {
        bool scienceMode = checkBox_ScienceSimMode.Checked;

        PhysicsConstants.ScientificMode = scienceMode;
        Debug.WriteLine($"[ScienceMode] {(scienceMode ? "ENABLED" : "disabled")}");

        // Science mode is incompatible with auto-tuning — disable and uncheck
        checkBox_AutoTuning.Checked = !scienceMode && checkBox_AutoTuning.Checked;
        checkBox_AutoTuning.Enabled = !scienceMode;
    }
}
