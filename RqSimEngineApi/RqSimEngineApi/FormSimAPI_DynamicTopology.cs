using System;
using RQSimulation.GPUCompressedSparseRow;
using RQSimulation.GPUCompressedSparseRow.DynamicTopology;

namespace RqSimForms.Forms.Interfaces;

/// <summary>
/// RqSimEngineApi extension for Dynamic Topology management.
/// </summary>
public partial class RqSimEngineApi
{
    // Stored topology mode for deferred application
    private GpuCayleyEvolutionEngineCsr.TopologyMode _pendingTopologyMode = 
        GpuCayleyEvolutionEngineCsr.TopologyMode.CsrStatic;
    
    // Stored dynamic topology config for deferred application
    private DynamicTopologyConfig? _pendingDynamicConfig;

    /// <summary>
    /// Sets the topology mode for CSR engine.
    /// If engine is not yet initialized, the mode will be applied on initialization.
    /// </summary>
    public void SetTopologyMode(GpuCayleyEvolutionEngineCsr.TopologyMode mode)
    {
        _pendingTopologyMode = mode;
        
        if (CsrCayleyEngine is not null && CsrCayleyEngine.IsInitialized)
        {
            CsrCayleyEngine.CurrentTopologyMode = mode;
            OnConsoleLog?.Invoke($"[GPU] Topology mode set to: {mode}\n");
            
            // If switching to dynamic hard rewiring, apply pending config
            if (mode == GpuCayleyEvolutionEngineCsr.TopologyMode.DynamicHardRewiring && 
                _pendingDynamicConfig is not null)
            {
                ApplyDynamicTopologyConfig(_pendingDynamicConfig);
            }
        }
        else
        {
            OnConsoleLog?.Invoke($"[GPU] Topology mode {mode} will be applied on engine init\n");
        }
    }

    /// <summary>
    /// Gets the current topology mode.
    /// </summary>
    public GpuCayleyEvolutionEngineCsr.TopologyMode GetTopologyMode()
    {
        return CsrCayleyEngine?.CurrentTopologyMode ?? _pendingTopologyMode;
    }

    /// <summary>
    /// Sets dynamic topology configuration.
    /// </summary>
    public void SetDynamicTopologyConfig(int rebuildInterval, double deletionThreshold, double beta, double initialWeight = 0.5)
    {
        _pendingDynamicConfig = new DynamicTopologyConfig
        {
            RebuildInterval = rebuildInterval,
            DeletionThreshold = deletionThreshold,
            Beta = beta,
            InitialWeight = initialWeight
        };

        if (CsrCayleyEngine is not null && CsrCayleyEngine.IsInitialized &&
            CsrCayleyEngine.CurrentTopologyMode == GpuCayleyEvolutionEngineCsr.TopologyMode.DynamicHardRewiring)
        {
            ApplyDynamicTopologyConfig(_pendingDynamicConfig);
        }
        else
        {
            OnConsoleLog?.Invoke($"[GPU] Dynamic topology config saved for later application\n");
        }
    }

    /// <summary>
    /// Gets the current dynamic topology statistics, if available.
    /// </summary>
    public DynamicTopologyStats? GetDynamicTopologyStats()
    {
        return CsrCayleyEngine?.LastDynamicStats;
    }

    /// <summary>
    /// Applies dynamic topology configuration to the CSR engine.
    /// </summary>
    private void ApplyDynamicTopologyConfig(DynamicTopologyConfig config)
    {
        if (CsrCayleyEngine is null) return;

        try
        {
            CsrCayleyEngine.ConfigureDynamicTopology(cfg =>
            {
                cfg.RebuildInterval = config.RebuildInterval;
                cfg.DeletionThreshold = config.DeletionThreshold;
                cfg.Beta = config.Beta;
                cfg.InitialWeight = config.InitialWeight;
            });
            
            OnConsoleLog?.Invoke($"[GPU] Dynamic topology configured: Interval={config.RebuildInterval}, Threshold={config.DeletionThreshold:F4}, Beta={config.Beta:F2}\n");
        }
        catch (Exception ex)
        {
            OnConsoleLog?.Invoke($"[GPU] Failed to configure dynamic topology: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Called after CSR engine initialization to apply pending settings.
    /// </summary>
    private void ApplyPendingTopologySettings()
    {
        if (CsrCayleyEngine is null) return;

        CsrCayleyEngine.CurrentTopologyMode = _pendingTopologyMode;
        
        if (_pendingTopologyMode == GpuCayleyEvolutionEngineCsr.TopologyMode.DynamicHardRewiring &&
            _pendingDynamicConfig is not null)
        {
            ApplyDynamicTopologyConfig(_pendingDynamicConfig);
        }
    }
}
