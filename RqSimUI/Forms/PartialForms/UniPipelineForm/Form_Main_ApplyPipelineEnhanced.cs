using RqSimEngineApi.Contracts;

namespace RqSimForms;

/// <summary>
/// Partial class for pipeline physics parameter conversion.
/// </summary>
partial class Form_Main_RqSim
{
    /// <summary>
    /// Gets physics parameters from current UI state for pipeline update.
    /// </summary>
    private SimulationParameters GetCurrentPhysicsParametersFromUI()
    {
        if (_currentPhysicsConfig is null)
        {
            return SimulationParameters.Default;
        }

        var gpuParams = _currentPhysicsConfig.ToGpuParameters();

        // Update lazy walk alpha from slider
        if (_trkLazyWalkAlpha is not null)
        {
            gpuParams = gpuParams.With(lazyWalkAlpha: _trkLazyWalkAlpha.Value / 100.0);
        }

        return gpuParams;
    }
}
