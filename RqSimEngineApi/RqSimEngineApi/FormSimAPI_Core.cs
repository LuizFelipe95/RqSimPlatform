using RQSimulation;
using RQSimulation.GPUOptimized;
using RQSimulation.EventBasedModel;
using RQSimulation.Core.Plugins;
using RQSimulation.Core.Plugins.Modules;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RqSimForms.Forms.Interfaces;

/// <summary>
/// Simulation API - contains all simulation logic separated from UI.
/// Used by Form_Main to run simulations without coupling to WinForms controls.
/// 
/// Threading model:
/// - Calculation thread: AboveNormal priority, writes to MetricsDispatcher
/// - UI thread: Normal priority, reads from MetricsDispatcher (lock-free)
/// - MetricsDispatcher: double-buffered, auto-decimates large datasets
/// </summary>
public partial class RqSimEngineApi
{
    // === Simulation Engine ===
    public SimulationEngine? SimulationEngine { get; private set; }
    public SimulationConfig? LastConfig { get; private set; }
    public SimulationResult? LastResult { get; private set; }
    public RQSimulation.ExampleModernSimulation.ScenarioResult? ModernResult { get; private set; }
    
    /// <summary>
    /// Reference to the last active graph, kept alive for visualization after simulation ends.
    /// </summary>
    private RQGraph? _lastActiveGraph;
    
    /// <summary>
    /// Gets the currently active graph for visualization purposes.
    /// Returns SimulationEngine.Graph if running, otherwise returns cached graph from last simulation.
    /// </summary>
    public RQGraph? ActiveGraph => SimulationEngine?.Graph ?? _lastActiveGraph;
    
    /// <summary>
    /// Updates the cached graph reference. Called during simulation.
    /// </summary>
    public void CacheActiveGraph(RQGraph? graph)
    {
        if (graph is not null)
            _lastActiveGraph = graph;
    }

    /// <summary>
    /// Clears the cached graph reference. Called when starting a new simulation.
    /// </summary>
    public void ClearCachedGraph()
    {
        _lastActiveGraph = null;
    }

    // === Default Pipeline (available before simulation starts) ===
    private PhysicsPipeline? _defaultPipeline;

    /// <summary>
    /// Gets the physics pipeline. Returns the simulation engine's pipeline if running,
    /// otherwise returns the default pipeline (creating it if needed).
    /// </summary>
    public PhysicsPipeline Pipeline => SimulationEngine?.Pipeline ?? GetOrCreateDefaultPipeline();

    // === Simulation State ===
    public bool IsModernRunning { get; set; }
    public bool SpectrumLoggingEnabled { get; set; }
    public DateTime SimulationWallClockStart { get; private set; }
    public volatile bool SimulationComplete;

    // === Final State Values ===
    public double FinalSpectralDimension { get; private set; }
    public double FinalNetworkTemperature { get; private set; }

    // === Callbacks for UI updates ===
    public Action<string>? OnConsoleLog { get; set; }

    // === Custom Initializer for Experiments ===
    private Action<RQGraph>? _pendingCustomInitializer;
    

    /// <summary>
    /// Sets a custom graph initializer to be used in the next simulation.
    /// Called by Form_Main when loading an experiment with custom topology.
    /// </summary>
    public void SetCustomInitializer(Action<RQGraph>? initializer)
    {
        _pendingCustomInitializer = initializer;
    }

    /// <summary>
    /// Sets up GPU synchronization for the pipeline.
    /// Call this from UI when GPU context is initialized.
    /// </summary>
    public void SetupGpuSynchronization(IGpuSyncManager gpuSyncManager)
    {
        // Set for default pipeline
        _defaultPipeline?.SetGpuSyncManager(gpuSyncManager);
        
        // Set for active simulation pipeline if running
        SimulationEngine?.Pipeline.SetGpuSyncManager(gpuSyncManager);
    }

    /// <summary>
    /// Gets or creates the default pipeline with modules based on last config or defaults.
    /// </summary>
    private PhysicsPipeline GetOrCreateDefaultPipeline()
    {
        if (_defaultPipeline is null)
        {
            _defaultPipeline = new PhysicsPipeline();
            _defaultPipeline.Log += (_, e) => OnConsoleLog?.Invoke($"[Pipeline] {e.Message}\n");
            _defaultPipeline.ModuleError += (_, e) => OnConsoleLog?.Invoke($"[Pipeline ERROR] {e.Module.Name}.{e.Phase}: {e.Exception.Message}\n");
            
            // Register default modules based on last config or use defaults
            RegisterDefaultModulesToPipeline(_defaultPipeline, LastConfig);
        }
        return _defaultPipeline;
    }

    /// <summary>
    /// Registers default physics modules to a pipeline based on configuration.
    /// This replicates the legacy InitializePhysicsModules order from SimulationEngine.
    /// </summary>
    /// <param name="pipeline">The pipeline to register modules to</param>
    /// <param name="config">Configuration (uses defaults if null)</param>
    public void RegisterDefaultModulesToPipeline(PhysicsPipeline pipeline, SimulationConfig? config)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        // Clear existing modules
        pipeline.Clear();

        // Use default config if none provided
        var cfg = config ?? CreateDefaultConfig();

        // === Geometry Modules (Priority 10-20) - matches legacy order ===
        if (cfg.UseSpacetimePhysics)
        {
            pipeline.RegisterModule(new SpacetimePhysicsModule());
        }

        // === Field Modules (Priority 20-60) ===
        if (cfg.UseSpinorField || cfg.UseYangMillsGauge)
        {
            pipeline.RegisterModule(new SpinorFieldModule(0.01));
        }

        if (cfg.UseVacuumFluctuations)
        {
            pipeline.RegisterModule(new VacuumFluctuationsModule());
        }

        if (cfg.UseBlackHolePhysics)
        {
            pipeline.RegisterModule(new BlackHolePhysicsModule());
        }

        if (cfg.UseYangMillsGauge)
        {
            pipeline.RegisterModule(new YangMillsGaugeModule());
        }

        if (cfg.UseEnhancedKleinGordon)
        {
            pipeline.RegisterModule(new KleinGordonModule(0.01));
        }

        // === Time Modules (Priority 70-90) ===
        if (cfg.UseInternalTime)
        {
            pipeline.RegisterModule(new InternalTimeModule(0.05));
        }

        if (cfg.UseRelationalTime)
        {
            pipeline.RegisterModule(new RelationalTimeModule());
        }

        if (cfg.UseSpectralGeometry)
        {
            pipeline.RegisterModule(new SpectralGeometryModule());
        }

        if (cfg.UseQuantumGraphity)
        {
            pipeline.RegisterModule(new QuantumGraphityModule(cfg.InitialNetworkTemperature));
        }

        if (cfg.UseAsynchronousTime)
        {
            pipeline.RegisterModule(new AsynchronousTimeModule());
        }

        // === Potential Modules (Priority 95) ===
        if (cfg.UseMexicanHatPotential)
        {
            pipeline.RegisterModule(new MexicanHatPotentialModule(
                cfg.UseHotStartAnnealing,
                cfg.HotStartTemperature));
        }

        // === Gravity Modules (Priority 100) ===
        if (cfg.UseGeometryMomenta)
        {
            pipeline.RegisterModule(new GeometryMomentaModule());
        }

        // === Core Evolution Module (Priority 200) ===
        if (cfg.UseUnifiedPhysicsStep)
        {
            pipeline.RegisterModule(new UnifiedPhysicsStepModule(
                cfg.EnforceGaugeConstraints,
                cfg.ValidateEnergyConservation));
        }

        // Sort modules by priority for correct execution order
        pipeline.SortByPriority();

        OnConsoleLog?.Invoke($"[Pipeline] Registered {pipeline.Count} default modules\n");
    }

    /// <summary>
    /// Creates a default configuration with all physics modules enabled.
    /// </summary>
    private static SimulationConfig CreateDefaultConfig()
    {
        return new SimulationConfig
        {
            // Enable all physics modules by default (matches legacy behavior)
            UseSpacetimePhysics = true,
            UseSpinorField = true,
            UseVacuumFluctuations = true,
            UseBlackHolePhysics = true,
            UseYangMillsGauge = true,
            UseEnhancedKleinGordon = true,
            UseInternalTime = true,
            UseRelationalTime = true,
            UseSpectralGeometry = true,
            UseQuantumGraphity = true,
            UseAsynchronousTime = false,
            UseMexicanHatPotential = true,
            UseHotStartAnnealing = true,
            UseGeometryMomenta = true,
            UseUnifiedPhysicsStep = true,
            EnforceGaugeConstraints = true,
            ValidateEnergyConservation = true,
            InitialNetworkTemperature = PhysicsConstants.InitialAnnealingTemperature,
            HotStartTemperature = PhysicsConstants.InitialAnnealingTemperature
        };
    }

    /// <summary>
    /// Refreshes the default pipeline with updated configuration.
    /// Call this when UI settings change and no simulation is running.
    /// </summary>
    public void RefreshDefaultPipeline(SimulationConfig config)
    {
        if (_defaultPipeline is not null)
        {
            RegisterDefaultModulesToPipeline(_defaultPipeline, config);
        }
    }

    /// <summary>
    /// Initializes simulation engine with given config.
    /// Uses the default pipeline if available, transferring its configuration.
    /// </summary>
    public void InitializeSimulation(SimulationConfig config)
    {
        LastConfig = config;



        // Create simulation engine with external pipeline if we have one configured
        if (_defaultPipeline is not null && _defaultPipeline.Count > 0)
        {
            SimulationEngine = new SimulationEngine(config, _pendingCustomInitializer, _defaultPipeline);
        }
        else
        {
            SimulationEngine = new SimulationEngine(config, _pendingCustomInitializer);
        }



        _pendingCustomInitializer = null; // Clear after use
        Dispatcher.LiveTotalSteps = config.TotalSteps;
        SimulationWallClockStart = DateTime.UtcNow;
    }

    /// <summary>
    /// Cleans up all simulation resources
    /// </summary>
    public void Cleanup()
    {
        DisposeGpuEngines();
        SimulationEngine = null;
        GpuStats.Reset();
        OnConsoleLog?.Invoke("[Cleanup] –есурсы симул€ции освобождены.\n");
    }
}
