using RqSimPlatform.PluginManager.UI;
using RQSimulation;
using RQSimulation.Core.Plugins;

namespace RqSimForms;

/// <summary>
/// Partial class for Physics Plugin Manager integration.
/// </summary>
partial class Form_Main_RqSim
{

    private PhysxPluginsForm? _pluginManagerForm;



    private void button_Plugins_Click(object sender, EventArgs e)
    {
        OpenPluginManager();

    }
    /// <summary>
    /// Opens the Plugin Manager form using _simApi.Pipeline (always available).
    /// </summary>
    public void OpenPluginManager()
    {
        EnsurePipelineInitialized();

        var pipeline = _simApi.Pipeline;

        // Create or reuse the form
        if (_pluginManagerForm is null || _pluginManagerForm.IsDisposed)
        {
            _pluginManagerForm = new PhysxPluginsForm(pipeline);
            _pluginManagerForm.FormClosed += (_, _) => _pluginManagerForm = null;
        }
        else
        {
            // Update pipeline reference in case it changed
            _pluginManagerForm.SetPipeline(pipeline);
        }

        // Show as non-modal to allow interaction with main form
        if (!_pluginManagerForm.Visible)
        {
            _pluginManagerForm.Show(this);
        }
        else
        {
            _pluginManagerForm.BringToFront();
        }
    }

    /// <summary>
    /// Ensures pipeline is initialized with default modules from current UI settings.
    /// </summary>
    private void EnsurePipelineInitialized()
    {
        if (_simApi.SimulationEngine is not null)
        {
            return;
        }

        var config = GetConfigFromUI();
        _simApi.RefreshDefaultPipeline(config);

        AppendSysConsole($"[Pipeline] Default pipeline initialized with {_simApi.Pipeline.Count} modules from UI settings\n");
    }
}
