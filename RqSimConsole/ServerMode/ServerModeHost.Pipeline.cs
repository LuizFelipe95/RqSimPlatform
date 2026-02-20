using System.Diagnostics.Metrics;
using RqSimPlatform.Contracts;
using RQSimulation;
using RqSimPlatform.PluginManager.UI.IncludedPlugins.CPU;
using RQSimulation.Core.Observability;
using RQSimulation.Core.Plugins;
using RQSimulation.Core.Plugins.Modules;
using RqSimPlatform.PluginManager.UI.IncludedPlugins.CPU;

namespace RqSimConsole.ServerMode;

/// <summary>
/// PhysicsPipeline integration for console mode (Option A).
/// Creates and manages a full PhysicsPipeline identical to the local UI mode,
/// ensuring feature parity: module enable/disable, per-module timing, dynamic params.
/// </summary>
internal sealed partial class ServerModeHost
{
    private PhysicsPipeline? _pipeline;
    private bool _pipelineInitialized;
    private MeterListener? _pipelineMeterListener;

    /// <summary>
    /// Cached per-module timing stats for SharedMemory transfer.
    /// Updated by MeterListener callbacks from the pipeline's OTel instrumentation.
    /// Key = module name, Value = (totalMs, count, errors).
    /// </summary>
    private readonly Dictionary<string, (double TotalMs, long Count, long Errors)> _moduleStats = new();
    private readonly object _moduleStatsLock = new();

    /// <summary>
    /// Builds or rebuilds the PhysicsPipeline from current settings.
    /// Mirrors FormSimAPI_Core.RegisterDefaultModulesToPipeline logic.
    /// </summary>
    private void BuildPipeline()
    {
        // Dispose previous pipeline modules and listener
        _pipeline?.CleanupAll();
        _pipelineMeterListener?.Dispose();
        _pipelineMeterListener = null;

        _pipeline = new PhysicsPipeline();
        _pipeline.Log += (_, e) => Console.WriteLine($"[Pipeline] {e.Message}");
        _pipeline.ModuleError += (_, e) => Console.WriteLine($"[Pipeline ERROR] {e.Module.Name}.{e.Phase}: {e.Exception.Message}");

        RegisterModulesFromSettings(_pipeline, _settings);

        // Start MeterListener to capture per-module timing from pipeline OTel metrics
        StartPipelineMeterListener();

        _pipelineInitialized = false;
        Console.WriteLine($"[ServerMode] Pipeline built with {_pipeline.Count} modules");
    }

    /// <summary>
    /// Starts a MeterListener that captures rqsim.module.duration_ms and
    /// rqsim.module.error.count metrics emitted by PhysicsPipeline.ExecuteModuleSafe.
    /// Aggregates per-module timing into _moduleStats for SharedMemory transfer.
    /// </summary>
    private void StartPipelineMeterListener()
    {
        _pipelineMeterListener = new MeterListener();

        _pipelineMeterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name != RqSimPlatformTelemetry.MeterName)
                return;

            if (instrument.Name is "rqsim.module.duration_ms" or "rqsim.module.error.count")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _pipelineMeterListener.SetMeasurementEventCallback<double>(OnModuleMeasurement);
        _pipelineMeterListener.SetMeasurementEventCallback<long>(OnModuleCountMeasurement);

        _pipelineMeterListener.Start();
        Console.WriteLine("[ServerMode] Pipeline MeterListener started for module timing");
    }

    /// <summary>
    /// Callback for Histogram&lt;double&gt; measurements (rqsim.module.duration_ms).
    /// </summary>
    private void OnModuleMeasurement(
        Instrument instrument,
        double value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        if (instrument.Name != "rqsim.module.duration_ms")
            return;

        string? moduleName = null;
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key is "module.name")
            {
                moduleName = tag.Value?.ToString();
                break;
            }
        }

        if (moduleName is null) return;

        lock (_moduleStatsLock)
        {
            if (_moduleStats.TryGetValue(moduleName, out var stats))
            {
                _moduleStats[moduleName] = (stats.TotalMs + value, stats.Count + 1, stats.Errors);
            }
            else
            {
                _moduleStats[moduleName] = (value, 1, 0);
            }
        }
    }

    /// <summary>
    /// Callback for Counter&lt;long&gt; measurements (rqsim.module.error.count).
    /// </summary>
    private void OnModuleCountMeasurement(
        Instrument instrument,
        long value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        if (instrument.Name != "rqsim.module.error.count")
            return;

        string? moduleName = null;
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key is "module.name")
            {
                moduleName = tag.Value?.ToString();
                break;
            }
        }

        if (moduleName is null) return;

        lock (_moduleStatsLock)
        {
            if (_moduleStats.TryGetValue(moduleName, out var stats))
            {
                _moduleStats[moduleName] = (stats.TotalMs, stats.Count, stats.Errors + value);
            }
            else
            {
                _moduleStats[moduleName] = (0, 0, value);
            }
        }
    }

    /// <summary>
    /// Registers physics modules based on current ServerModeSettingsDto flags.
    /// Mirrors the registration order from FormSimAPI_Core.RegisterDefaultModulesToPipeline.
    /// </summary>
    private static void RegisterModulesFromSettings(PhysicsPipeline pipeline, ServerModeSettingsDto settings)
    {
        pipeline.Clear();

        // === Geometry Modules (Priority 10-20) ===
        if (settings.UseSpacetimePhysics)
        {
            pipeline.RegisterModule(new SpacetimePhysicsModule());
        }

        // === Field Modules (Priority 20-60) ===
        if (settings.UseSpinorField || settings.UseYangMillsGauge)
        {
            pipeline.RegisterModule(new SpinorFieldModule(0.01));
        }

        if (settings.UseVacuumFluctuations)
        {
            pipeline.RegisterModule(new VacuumFluctuationsModule());
        }

        if (settings.UseBlackHolePhysics)
        {
            pipeline.RegisterModule(new BlackHolePhysicsModule());
        }

        if (settings.UseYangMillsGauge)
        {
            pipeline.RegisterModule(new YangMillsGaugeModule());
        }

        if (settings.UseEnhancedKleinGordon)
        {
            pipeline.RegisterModule(new KleinGordonModule(0.01));
        }

        // === Time Modules (Priority 70-90) ===
        if (settings.UseInternalTime)
        {
            pipeline.RegisterModule(new InternalTimeModule(0.05));
        }

        if (settings.UseRelationalTime)
        {
            pipeline.RegisterModule(new RelationalTimeModule());
        }

        if (settings.UseSpectralGeometry)
        {
            pipeline.RegisterModule(new SpectralGeometryModule());
        }

        if (settings.UseQuantumGraphity)
        {
            pipeline.RegisterModule(new QuantumGraphityModule(
                PhysicsConstants.InitialAnnealingTemperature));
        }

        // AsynchronousTimeModule — per-node proper time evolution
        pipeline.RegisterModule(new AsynchronousTimeModule());

        // === Potential Modules (Priority 95) ===
        if (settings.UseMexicanHatPotential)
        {
            pipeline.RegisterModule(new MexicanHatPotentialModule(
                useHotStart: true,
                hotStartTemperature: settings.HotStartTemperature));
        }

        // === Gravity Modules (Priority 100) ===
        if (settings.UseGeometryMomenta)
        {
            pipeline.RegisterModule(new GeometryMomentaModule());
        }

        // === Core Evolution Module (Priority 200) ===
        if (settings.UseUnifiedPhysicsStep)
        {
            pipeline.RegisterModule(new UnifiedPhysicsStepModule(
                settings.EnforceGaugeConstraints,
                settings.ValidateEnergyConservation));
        }

        // === Included CPU Plugins (Priority 42-45) ===
        if (settings.PreferOllivierRicciCurvature)
        {
            pipeline.RegisterModule(new OllivierRicciCpuModule
            {
                SinkhornIterations = settings.SinkhornIterations,
                SinkhornEpsilon = settings.SinkhornEpsilon,
                ConvergenceThreshold = settings.SinkhornConvergenceThreshold,
                LazyWalkAlpha = settings.LazyWalkAlpha
            });
        }

        if (settings.UseMcmc)
        {
            pipeline.RegisterModule(new MCMCSamplerCpuModule
            {
                Beta = settings.McmcBeta,
                SamplesPerStep = settings.McmcStepsPerCall,
                WeightPerturbation = settings.McmcWeightPerturbation,
                MinWeight = settings.McmcMinWeight
            });
        }

        pipeline.SortByPriority();
    }

    /// <summary>
    /// Initializes the pipeline for the current graph. 
    /// Call after graph is created and before first ExecuteFrame.
    /// </summary>
    private void InitializePipelineForGraph()
    {
        if (_pipeline is null || _graph is null || _pipelineInitialized)
            return;

        try
        {
            _pipeline.InitializeAll(_graph);
            _pipelineInitialized = true;
            Console.WriteLine($"[ServerMode] Pipeline initialized for graph ({_graph.N} nodes)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServerMode] Pipeline initialization error: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts ServerModeSettingsDto to DynamicPhysicsParams for pipeline execution.
    /// This keeps physics parameter hot-swapping working in console mode.
    /// </summary>
    private static DynamicPhysicsParams SettingsToDynamicParams(ServerModeSettingsDto s, long tickId)
    {
        var p = DynamicPhysicsParams.Default;

        p.DeltaTime = PhysicsConstants.BaseTimestep;
        p.TickId = tickId;
        p.GravitationalCoupling = s.GravitationalCoupling;
        p.LapseFunctionAlpha = s.LapseFunctionAlpha;
        p.VacuumEnergyScale = s.VacuumEnergyScale;
        p.LazyWalkAlpha = s.LazyWalkAlpha;
        p.Temperature = s.Temperature;
        p.DecoherenceRate = s.DecoherenceRate;
        p.WilsonParameter = s.WilsonParameter;
        p.GaugeFieldDamping = s.GaugeFieldDamping;
        p.EdgeTrialProbability = s.EdgeTrialProb;
        p.PairCreationEnergy = s.PairCreationEnergy;
        p.SpectralCutoff = s.SpectralLambdaCutoff;
        p.TargetSpectralDimension = s.SpectralTargetDimension;
        p.SpectralDimensionStrength = s.SpectralDimensionPotentialStrength;

        // Sinkhorn Ollivier-Ricci
        p.SinkhornIterations = s.SinkhornIterations;
        p.SinkhornEpsilon = s.SinkhornEpsilon;
        p.ConvergenceThreshold = s.SinkhornConvergenceThreshold;

        // MCMC Metropolis-Hastings
        p.McmcBeta = s.McmcBeta;
        p.McmcStepsPerCall = s.McmcStepsPerCall;
        p.McmcWeightPerturbation = s.McmcWeightPerturbation;

        // Flags
        p.EnableOllivierRicci = s.PreferOllivierRicciCurvature;
        p.ScientificMode = s.PreferOllivierRicciCurvature;

        return p;
    }

    /// <summary>
    /// Applies module enable/disable flags from settings to a running pipeline.
    /// Called on live settings updates so that UI checkboxes take effect immediately.
    /// </summary>
    private void SyncModuleEnabledFlags(ServerModeSettingsDto settings)
    {
        if (_pipeline is null) return;

        SetModuleEnabled("Spacetime Physics", settings.UseSpacetimePhysics);
        SetModuleEnabled("Spinor Field", settings.UseSpinorField || settings.UseYangMillsGauge);
        SetModuleEnabled("Vacuum Fluctuations", settings.UseVacuumFluctuations);
        SetModuleEnabled("Black Hole Physics", settings.UseBlackHolePhysics);
        SetModuleEnabled("Yang-Mills Gauge", settings.UseYangMillsGauge);
        SetModuleEnabled("Enhanced Klein-Gordon", settings.UseEnhancedKleinGordon);
        SetModuleEnabled("Internal Time", settings.UseInternalTime);
        SetModuleEnabled("Relational Time", settings.UseRelationalTime);
        SetModuleEnabled("Spectral Geometry", settings.UseSpectralGeometry);
        SetModuleEnabled("Quantum Graphity", settings.UseQuantumGraphity);
        SetModuleEnabled("Mexican Hat Potential", settings.UseMexicanHatPotential);
        SetModuleEnabled("Geometry Momenta", settings.UseGeometryMomenta);
        SetModuleEnabled("Unified Physics Step", settings.UseUnifiedPhysicsStep);

        // Included CPU plugins
        SetModuleEnabled("Ollivier-Ricci Curvature (CPU)", settings.PreferOllivierRicciCurvature);
        SetModuleEnabled("MCMC Sampler (CPU)", settings.UseMcmc);

        Console.WriteLine("[ServerMode] Module enable flags synced from settings");
    }

    private void SetModuleEnabled(string moduleName, bool enabled)
    {
        var module = _pipeline?.GetModule(moduleName);
        if (module is not null)
        {
            module.IsEnabled = enabled;
        }
    }

    /// <summary>
    /// Executes one pipeline frame. Per-module timing is captured automatically
    /// by the MeterListener from the pipeline's OTel instrumentation.
    /// Also runs event-based node state transitions (the core simulation loop)
    /// so that Excited/Refractory/Rest state changes occur, matching local mode behavior.
    /// </summary>
    private void ExecutePipelineFrame(long tickId, CancellationToken ct)
    {
        if (_pipeline is null || _graph is null) return;

        if (!_pipelineInitialized)
        {
            InitializePipelineForGraph();
        }

        // Convert current settings to dynamic params (hot-swappable)
        DynamicPhysicsParams dynamicParams = SettingsToDynamicParams(_settings, tickId);
        _pipeline.UpdateParameters(in dynamicParams);

        // Step 1: Run event-based node state transitions (Excited → Refractory → Rest)
        // Without this, node states never change and Excited/Cluster metrics stay at 0.
        _graph.StepEventBasedBatch(_graph.N);

        // Step 2: Execute all enabled pipeline modules — timing captured by MeterListener
        _pipeline.ExecuteFrameWithParams(_graph, PhysicsConstants.BaseTimestep);

        // Step 3: Update derived metrics periodically (expensive O(E) operation)
        if (tickId % 10 == 0)
        {
            _graph.UpdateCorrelationWeights();
        }
    }

    /// <summary>
    /// Takes a thread-safe snapshot of _moduleStats for SharedMemory writing.
    /// Called from PublishLoop which runs on a different thread than MeterListener callbacks.
    /// </summary>
    private Dictionary<string, (double TotalMs, long Count, long Errors)> GetModuleStatsSnapshot()
    {
        lock (_moduleStatsLock)
        {
            return new Dictionary<string, (double TotalMs, long Count, long Errors)>(_moduleStats);
        }
    }
}
