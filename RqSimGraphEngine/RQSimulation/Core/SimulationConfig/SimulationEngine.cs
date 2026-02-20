using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RQSimulation.Core.Infrastructure;
using RQSimulation.Core.Plugins;
using RQSimulation.Core.Plugins.Modules;

namespace RQSimulation
{
    /// <summary>
    /// Configuration for simulation parameters.
    /// Used by both legacy and modern simulation modes.
    /// </summary>
    public sealed class SimulationConfig
    {
        // === Basic Graph Parameters ===
        public int NodeCount { get; set; } = 64;
        public double InitialEdgeProb { get; set; } = 1;
        public double InitialExcitedProb { get; set; } = 0.1;
        public int TargetDegree { get; set; } = 4;
        public double LambdaState { get; set; } = 0.5;
        public double Temperature { get; set; } = PhysicsConstants.InitialAnnealingTemperature;
        public double EdgeTrialProbability { get; set; } = 0.2;
        public double MeasurementThreshold { get; set; } = 0.3;
        public double DynamicMeasurementThreshold { get; set; } = 0.9;
        public int Seed { get; set; } = 42;
        public int TotalSteps { get; set; } = 10000;
        public int LogEvery { get; set; } = 1;
        public int BaselineWindow { get; set; } = 100;

        // === Legacy impulse parameters (disabled in Modern mode) ===
        public int FirstImpulse { get; set; } = -1;
        public int ImpulsePeriod { get; set; } = -1;
        public int CalibrationStep { get; set; } = -1;

        public int VisualizationInterval { get; set; } = 10;
        public int MeasurementLogInterval { get; set; } = 100;
        public bool UseQuantumDrivenStates { get; set; } = true;
        public int FractalLevels { get; set; } = 0;
        public int FractalBranchFactor { get; set; } = 0;
        public int StrongEdgeThreshold { get; set; } = 25;

        // === Physics Modules ===
        public bool UseSpacetimePhysics { get; set; } = true;
        public bool UseSpinorField { get; set; } = true;
        public bool UseVacuumFluctuations { get; set; } = true;
        public bool UseBlackHolePhysics { get; set; } = true;
        public bool UseYangMillsGauge { get; set; } = true;
        public bool UseEnhancedKleinGordon { get; set; } = true;

        // === Physics Constants (should use PhysicsConstants values) ===
        public double GravitationalCoupling { get; set; } = PhysicsConstants.GravitationalCoupling;
        public double VacuumEnergyScale { get; set; } = PhysicsConstants.VacuumFluctuationBaseRate * 20;
        public bool UseInternalTime { get; set; } = true;
        public bool UseSpectralGeometry { get; set; } = true;
        public bool UseQuantumGraphity { get; set; } = true;
        public double InitialNetworkTemperature { get; set; } = PhysicsConstants.InitialAnnealingTemperature;
        public double AnnealingCoolingRate { get; set; } = 0.995;

        // === Relational Dynamics ===
        public bool UseRelationalTime { get; set; } = true;
        public bool UseRelationalYangMills { get; set; } = true;
        public bool UseNetworkGravity { get; set; } = true;
        public double DecoherenceRate { get; set; } = 0.001;

        // === RQ-Hypothesis Compliance ===
        public bool UseUnifiedPhysicsStep { get; set; } = true;
        public bool EnforceGaugeConstraints { get; set; } = true;
        public bool UseCausalRewiring { get; set; } = true;
        public bool UseAsynchronousTime { get; set; } = false;
        public bool UseTopologicalProtection { get; set; } = true;
        public bool ValidateEnergyConservation { get; set; } = true;
        public double EnergyConservationTolerance { get; set; } = 0.01;

        /// <summary>
        /// RQ-COMPLIANT: Use event-based simulation with per-node proper time.
        /// When enabled, each node evolves according to its local proper time ?_i
        /// with gravitational time dilation (heavy regions run slower).
        /// 
        /// This is the TRUE relational time model where:
        /// - No global "now" exists
        /// - Time emerges from quantum correlations
        /// - Causality is enforced via graph connectivity
        /// 
        /// Default: false - controlled by UI checkbox "Events Model (time free)"
        /// </summary>
        public bool UseEventBasedSimulation { get; set; } = true;

        // === Absolute Rigor Options ===
        public bool UseMexicanHatPotential { get; set; } = true;
        public bool UseHotStartAnnealing { get; set; } = true;
        public bool DisableManualParticleDetection { get; set; } = true;
        public bool UseSoftPotentialWalls { get; set; } = true;
        public bool UseGeometryMomenta { get; set; } = true;
        public bool UseTopologicalCensorship { get; set; } = true;
        public bool UseSpectralMass { get; set; } = false;
        public double HotStartTemperature { get; set; } = PhysicsConstants.InitialAnnealingTemperature;

        // ============================================================
        // RQ-MODERNIZATION: Scientific Validity Options
        // ============================================================

        /// <summary>
        /// RQ-MODERNIZATION: Allow edge weights to reach true zero.
        /// When TRUE: Weights can reach 0.0, allowing physical horizon formation.
        /// When FALSE: Legacy behavior with soft walls preventing zero weights.
        /// 
        /// SCIENTIFIC RATIONALE:
        /// In proper Regge calculus, w ? 0 represents a genuine metric singularity.
        /// Default: false for backward compatibility.
        /// </summary>
        public bool AllowZeroWeightEdges { get; set; } = false;

        /// <summary>
        /// RQ-MODERNIZATION: Enable/disable soft walls for edge weights.
        /// When TRUE: Clamp weights to prevent extreme values.
        /// When FALSE: Allow weights to evolve freely (requires adaptive timestep).
        /// 
        /// Default: true for numerical stability.
        /// </summary>
        public bool UseSoftWalls { get; set; } = true;

        /// <summary>
        /// RQ-MODERNIZATION: Enable/disable connectivity protection.
        /// When TRUE: Suppress weight decreases when graph is at fragmentation risk.
        /// When FALSE: Let physics equations determine graph fate.
        /// 
        /// Default: true for stable simulations.
        /// </summary>
        public bool UseConnectivityProtection { get; set; } = true;

        /// <summary>
        /// RQ-MODERNIZATION: Use unbounded linear flow for gravity evolution.
        /// When TRUE: Use linear Euler step without Tanh saturation (unbounded flow).
        /// When FALSE: Use Tanh-bounded flow (legacy, more stable but artificially saturated).
        /// 
        /// TERMINOLOGICAL NOTE:
        /// This is NOT a symplectic integrator. True symplectic integration requires
        /// a momentum variable and Velocity Verlet scheme. This simply removes the
        /// artificial Tanh() saturation from the flow equation.
        /// 
        /// Default: false for stability.
        /// </summary>
        public bool UseUnboundedFlow { get; set; } = false;

        /// <summary>
        /// RQ-MODERNIZATION: Treat spacetime singularities as valid physics results.
        /// When TRUE: NaN/Infinity weights are interpreted as singularity formation.
        /// When FALSE: NaN/Infinity trigger exceptions (legacy error handling).
        /// 
        /// Default: true for physically rigorous simulations.
        /// </summary>
        public bool AllowSingularityFormation { get; set; } = true;

        /// <summary>
        /// Enable MCMC sampling mode instead of time evolution.
        /// When true, simulation samples configuration space via Metropolis-Hastings.
        /// Time emerges from correlations, not from iteration count.
        /// </summary>
        public bool UseMCMCSampling { get; set; } = false;

        /// <summary>
        /// Number of MCMC samples to collect.
        /// </summary>
        public int MCMCSamples { get; set; } = 10000;

        /// <summary>
        /// Thermalization sweeps before collecting samples.
        /// </summary>
        public int MCMCThermalizationSweeps { get; set; } = 1000;
    }

    /// <summary>
    /// Result container for simulation metrics.
    /// </summary>
    public sealed class SimulationResult
    {
        public double AverageExcited { get; set; }
        public int MaxExcited { get; set; }
        public List<string> TimeSeries { get; } = new();
        public List<string> AvalancheStats { get; } = new();
        public List<string> MeasurementEvents { get; } = new();
        public List<string> HeavyClusters { get; } = new();
        public bool MeasurementConfigured { get; set; }
        public bool MeasurementTriggered { get; set; }
        public List<string> DiagnosticsExport { get; } = new();
    }

    /// <summary>
    /// Event args for console logging.
    /// </summary>
    public sealed class ConsoleLogEventArgs : EventArgs
    {
        public ConsoleLogEventArgs(string message) => Message = message;
        public string Message { get; }
    }

    /// <summary>
    /// Event args for progress updates (kept for compatibility, but minimally used).
    /// </summary>
    public sealed class SimulationProgressEventArgs : EventArgs
    {
        public SimulationProgressEventArgs(int currentStep, int totalSteps, int currentOn, bool shouldRedraw)
        {
            CurrentStep = currentStep;
            TotalSteps = totalSteps;
            CurrentOn = currentOn;
            ShouldRedraw = shouldRedraw;
        }
        public int CurrentStep { get; }
        public int TotalSteps { get; }
        public int CurrentOn { get; }
        public bool ShouldRedraw { get; }
    }

    /// <summary>
    /// Modern simulation engine - acts as a factory for RQGraph initialization.
    /// The actual simulation loop is in Form_Main.RunModernAsync().
    /// 
    /// ARCHITECTURE:
    /// - SimulationEngine: Creates and configures RQGraph with PhysicsPipeline
    /// - PhysicsPipeline: Manages ordered collection of physics modules
    /// - Form_Main.RunModernAsync(): Runs the simulation loop with GPU support
    /// - RQGraph: Contains all physics methods
    /// </summary>
    public sealed class SimulationEngine
    {
        private readonly SimulationConfig _cfg;
        private readonly RQGraph _graph;
        private Action<RQGraph>? _customInitializer;
        private MCMCSampler? _mcmcSampler;
        private readonly bool _useExternalPipeline;

        /// <summary>
        /// Physics module pipeline for configurable physics execution.
        /// Use this to add, remove, reorder, or enable/disable physics modules at runtime.
        /// </summary>
        public PhysicsPipeline Pipeline { get; }

        /// <summary>
        /// Exposes the underlying graph for UI and testing.
        /// </summary>
        public RQGraph Graph => _graph;

        /// <summary>
        /// Creates a new simulation engine and initializes the graph.
        /// </summary>
        public SimulationEngine(SimulationConfig cfg) : this(cfg, null, null)
        {
        }

        /// <summary>
        /// Creates a new simulation engine with optional custom graph initializer.
        /// </summary>
        /// <param name="cfg">Simulation configuration</param>
        /// <param name="customInitializer">Optional action to customize graph topology after basic initialization</param>
        public SimulationEngine(SimulationConfig cfg, Action<RQGraph>? customInitializer)
            : this(cfg, customInitializer, null)
        {
        }

        /// <summary>
        /// Creates a new simulation engine with optional custom initializer and external pipeline.
        /// </summary>
        /// <param name="cfg">Simulation configuration</param>
        /// <param name="customInitializer">Optional action to customize graph topology after basic initialization</param>
        /// <param name="externalPipeline">Optional external pipeline to use instead of creating a new one.
        /// If provided, its modules will be used; otherwise, modules are registered based on config.</param>
        public SimulationEngine(SimulationConfig cfg, Action<RQGraph>? customInitializer, PhysicsPipeline? externalPipeline)
        {
            ArgumentNullException.ThrowIfNull(cfg);
            _cfg = cfg;
            _customInitializer = customInitializer;
            _useExternalPipeline = externalPipeline is not null;
            Pipeline = externalPipeline ?? new PhysicsPipeline();

            // Subscribe to pipeline logging (only if we created the pipeline)
            if (!_useExternalPipeline)
            {
                Pipeline.Log += (_, e) => LogConsole($"[Pipeline] {e.Message}\n");
                Pipeline.ModuleError += (_, e) => LogConsole($"[Pipeline ERROR] {e.Module.Name}.{e.Phase}: {e.Exception.Message}\n");
            }

            // Create graph with initial topology
            _graph = new RQGraph(
                cfg.NodeCount,
                cfg.InitialEdgeProb,
                cfg.InitialExcitedProb,
                cfg.TargetDegree,
                cfg.LambdaState,
                cfg.Temperature,
                cfg.EdgeTrialProbability,
                cfg.MeasurementThreshold,
                cfg.Seed);

            // Apply custom initializer if provided (e.g., for DNA linear chain)
            // This is done BEFORE fractal topology to allow complete override
            if (_customInitializer != null)
            {
                LogConsole("[SimulationEngine] Applying custom graph initializer...\n");
                _customInitializer(_graph);
            }
            else
            {
                // Initialize fractal topology only if no custom initializer
                _graph.InitFractalTopology(cfg.FractalLevels, cfg.FractalBranchFactor);
            }

            _graph.ComputeFractalLevels();

            // Initialize coordinates for visualization
            _graph.InitCoordinatesRandom(range: 1.0);
            _graph.RelaxCoordinatesFromCorrelation(0.05);

            // Initialize quantum wavefunction
            if (_cfg.UseQuantumDrivenStates)
            {
                _graph.ConfigureQuantumComponents(1);
                _graph.InitQuantumWavefunction();
            }

            // Register physics modules based on configuration
            // Skip if using external pipeline (modules already configured)
            if (!_useExternalPipeline)
            {
                RegisterDefaultModules();
            }

            // Initialize all registered modules
            Pipeline.InitializeAll(_graph);

            // Log initialization
            LogConsole("[SimulationEngine] Graph initialized\n");
            LogConsole($"[SimulationEngine] N={_graph.N}, edges={_graph.FlatEdgesFrom?.Length ?? 0}\n");
            LogConsole($"[SimulationEngine] Pipeline modules: {Pipeline.Count}\n");

            // DEBUG: Dump settings via reflection for comparison between local and console modes
            try
            {
                var additionalData = new Dictionary<string, object?>
                {
                    ["ConfigSnapshot"] = new
                    {
                        cfg.NodeCount,
                        cfg.InitialEdgeProb,
                        cfg.InitialExcitedProb,
                        cfg.TargetDegree,
                        cfg.LambdaState,
                        cfg.Temperature,
                        cfg.EdgeTrialProbability,
                        cfg.MeasurementThreshold,
                        cfg.Seed,
                        cfg.TotalSteps,
                        cfg.GravitationalCoupling,
                        cfg.VacuumEnergyScale,
                        cfg.DecoherenceRate,
                        cfg.HotStartTemperature,
                        cfg.UseQuantumDrivenStates,
                        cfg.UseSpacetimePhysics,
                        cfg.UseSpinorField,
                        cfg.UseVacuumFluctuations,
                        cfg.UseBlackHolePhysics,
                        cfg.UseYangMillsGauge,
                        cfg.UseEnhancedKleinGordon,
                        cfg.UseInternalTime,
                        cfg.UseSpectralGeometry,
                        cfg.UseQuantumGraphity,
                        cfg.UseNetworkGravity,
                        cfg.UseCausalRewiring
                    },
                    ["PipelineModuleCount"] = Pipeline.Count,
                    ["CustomInitializerUsed"] = _customInitializer is not null
                };

                string dumpPath = SettingsDumper.DumpGraphSettings(_graph, "local", additionalData);
                if (!string.IsNullOrEmpty(dumpPath))
                {
                    LogConsole($"[SimulationEngine] Settings dumped to: {dumpPath}\n");
                }
            }
            catch (Exception dumpEx)
            {
                LogConsole($"[SimulationEngine] Settings dump failed: {dumpEx.Message}\n");
            }
        }

        /// <summary>
        /// Sets a custom initializer to be applied to the next graph created.
        /// Used by experiment system to set up special topologies (e.g., DNA linear chain).
        /// </summary>
        public void SetCustomInitializer(Action<RQGraph>? initializer)
        {
            _customInitializer = initializer;
        }

        /// <summary>
        /// Gets the current custom initializer, if any.
        /// </summary>
        public Action<RQGraph>? CustomInitializer => _customInitializer;

        /// <summary>
        /// Registers default physics modules based on configuration.
        /// Modules can be added/removed/reordered via Pipeline property.
        /// </summary>
        private void RegisterDefaultModules()
        {
            // Clear any existing modules
            Pipeline.Clear();

            // === Geometry Modules (Priority 10-20) ===
            if (_cfg.UseSpacetimePhysics)
            {
                Pipeline.RegisterModule(new SpacetimePhysicsModule());
            }

            // === Field Modules (Priority 20-60) ===
            if (_cfg.UseSpinorField || _cfg.UseYangMillsGauge)
            {
                Pipeline.RegisterModule(new SpinorFieldModule(0.01));
            }

            if (_cfg.UseVacuumFluctuations)
            {
                Pipeline.RegisterModule(new VacuumFluctuationsModule());
            }

            if (_cfg.UseBlackHolePhysics)
            {
                Pipeline.RegisterModule(new BlackHolePhysicsModule());
            }

            if (_cfg.UseYangMillsGauge)
            {
                Pipeline.RegisterModule(new YangMillsGaugeModule());
            }

            if (_cfg.UseEnhancedKleinGordon)
            {
                Pipeline.RegisterModule(new KleinGordonModule(0.01));
            }

            // === Time Modules (Priority 70-90) ===
            if (_cfg.UseInternalTime)
            {
                Pipeline.RegisterModule(new InternalTimeModule(0.05));
            }

            if (_cfg.UseRelationalTime)
            {
                Pipeline.RegisterModule(new RelationalTimeModule());
            }

            if (_cfg.UseSpectralGeometry)
            {
                Pipeline.RegisterModule(new SpectralGeometryModule());
            }

            if (_cfg.UseQuantumGraphity)
            {
                Pipeline.RegisterModule(new QuantumGraphityModule(_cfg.InitialNetworkTemperature));
            }

            if (_cfg.UseAsynchronousTime)
            {
                Pipeline.RegisterModule(new AsynchronousTimeModule());
            }

            // === Potential Modules (Priority 95) ===
            if (_cfg.UseMexicanHatPotential)
            {
                Pipeline.RegisterModule(new MexicanHatPotentialModule(
                    _cfg.UseHotStartAnnealing,
                    _cfg.HotStartTemperature));
            }

            // === Gravity Modules (Priority 100) ===
            if (_cfg.UseGeometryMomenta)
            {
                Pipeline.RegisterModule(new GeometryMomentaModule());
            }

            // === Core Evolution Module (Priority 200) ===
            if (_cfg.UseUnifiedPhysicsStep)
            {
                Pipeline.RegisterModule(new UnifiedPhysicsStepModule(
                    _cfg.EnforceGaugeConstraints,
                    _cfg.ValidateEnergyConservation));
            }

            // Sort modules by priority for correct execution order
            Pipeline.SortByPriority();
        }

        /// <summary>
        /// Event for console logging.
        /// </summary>
        public event EventHandler<ConsoleLogEventArgs>? ConsoleLog;


        private void LogConsole(string msg) => ConsoleLog?.Invoke(this, new ConsoleLogEventArgs(msg));

        /// <summary>
        /// Runs the event-driven simulation loop.
        /// Replaces the synchronous RunModernSync loop with a relativistic event-based approach.
        /// Now integrates with PhysicsPipeline for modular physics execution.
        /// </summary>
        public void RunEventDrivenLoop()
        {
            // Priority queue: (Time, NodeIndex). Sorted by time.
            var eventQueue = new PriorityQueue<int, double>();

            // Initialize: each node schedules its first update
            // ProperTime is initialized to 0 for all nodes
            for (int i = 0; i < _graph.N; i++)
            {
                // Ensure ProperTime array exists and is initialized
                if (_graph.ProperTime == null || _graph.ProperTime.Length != _graph.N)
                    _graph.InitAsynchronousTime();

                eventQueue.Enqueue(i, _graph.ProperTime[i]);
            }

            // Simulation loop
            int maxEvents = _cfg.TotalSteps * _graph.N; // Approximate equivalent work
            int eventsProcessed = 0;

            while (eventsProcessed < maxEvents && eventQueue.Count > 0)
            {
                // 1. Dequeue event with minimum local time
                if (!eventQueue.TryDequeue(out int nodeIndex, out double executionTime)) break;

                // 2. Compute relational time step
                double relDt = _graph.ComputeRelationalDtExtended();
                double lapse = _graph.GetLocalLapse(nodeIndex);
                double properDt = relDt * lapse;

                // 3. Execute physics pipeline for this timestep
                // This calls all enabled modules in order
                Pipeline.ExecuteFrame(_graph, properDt);

                // 4. Update node-specific physics (per-node evolution)
                _graph.UpdateNodePhysics(nodeIndex, properDt);

                // 5. Compute next event time and reschedule
                double nextTime = executionTime + properDt;
                _graph.ProperTime[nodeIndex] = nextTime;
                eventQueue.Enqueue(nodeIndex, nextTime);

                eventsProcessed++;
            }
        }

        /// <summary>
        /// Runs a single simulation step using the physics pipeline.
        /// Call this from UI loop for stepped execution.
        /// </summary>
        public void RunPipelineStep(double dt)
        {
            Pipeline.ExecuteFrame(_graph, dt);
        }

        /// <summary>
        /// Async version of pipeline step execution.
        /// </summary>
        public async Task RunPipelineStepAsync(double dt, CancellationToken ct = default)
        {
            await Pipeline.ExecuteFrameAsync(_graph, dt, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// RQ-COMPLIANT: Event-driven simulation step based on local proper time.
        /// Replaces the global synchronous loop with a relativistic event queue.
        /// </summary>
        public void RunEventDrivenStep()
        {
            // 1. Find node with minimum local time (?)
            int nextNode = -1;
            double minTau = double.MaxValue;

            var nextUpdates = _graph.NodeNextUpdateTime;

            for (int i = 0; i < _graph.N; i++)
            {
                if (nextUpdates[i] < minTau)
                {
                    minTau = nextUpdates[i];
                    nextNode = i;
                }
            }

            if (nextNode == -1) return;

            // 2. Compute relational dt from clock state change (Page-Wootters)
            double relDt = _graph.ComputeRelationalDtExtended();

            // 3. Update ONLY this node and its local environment
            double lapse = _graph.GetLocalLapse(nextNode);
            double properDt = relDt * lapse;

            _graph.UpdateLocalPhysics(nextNode, properDt);

            // 4. Schedule next update
            nextUpdates[nextNode] += properDt;
        }

        /// <summary>
        /// Alternative to time-based evolution: sample configuration space.
        /// </summary>
        public void RunMCMCSampling(int samples, Action<int, RQGraph>? onSample = null)
        {
            _mcmcSampler ??= new MCMCSampler(_graph, _cfg.Seed);
            _mcmcSampler.SampleConfigurationSpace(samples, onSample);
        }

        /// <summary>
        /// Cleans up all pipeline modules. Call when stopping simulation.
        /// </summary>
        public void Cleanup()
        {
            Pipeline.CleanupAll();
        }

        // ============================================================
        // RQ-MODERNIZATION: Singularity Handling
        // ============================================================

        private SimulationState _state = SimulationState.Running;
        private string _terminationReason = "";

        /// <summary>
        /// RQ-MODERNIZATION: Current state of the simulation.
        /// </summary>
        public SimulationState State => _state;

        /// <summary>
        /// RQ-MODERNIZATION: Reason for simulation termination (if any).
        /// </summary>
        public string TerminationReason => _terminationReason;

        /// <summary>
        /// RQ-MODERNIZATION: Check for spacetime singularity formation.
        /// 
        /// Call this after each simulation step to detect singularities.
        /// If AllowSingularityFormation is enabled, singularities are recognized
        /// as valid physics results rather than errors.
        /// 
        /// When a terminal singularity is detected, exports a crash dump snapshot
        /// for post-mortem scientific analysis.
        /// 
        /// Returns true if simulation should continue, false if it should terminate.
        /// </summary>
        /// <param name="step">Current simulation step</param>
        /// <param name="spectralDimension">Optional pre-computed spectral dimension for snapshot</param>
        /// <returns>True if simulation should continue</returns>
        public bool CheckAndHandleSingularity(int step, double? spectralDimension = null)
        {
            if (!PhysicsConstants.AllowSingularityFormation)
            {
                // Legacy behavior: singularities throw exceptions
                return true;
            }

            var singularity = _graph.CheckSingularityState();

            if (!singularity.HasSingularity)
            {
                _state = SimulationState.Running;
                return true;
            }

            // Log singularity detection
            LogConsole($"[Singularity] Step {step}: {singularity.Description}\n");

            if (singularity.IsTerminal)
            {
                // Terminal singularity - simulation complete
                _state = MapSingularityToState(singularity.Type);
                _terminationReason = singularity.Description;

                LogConsole($"[Singularity] TERMINAL: {_terminationReason}\n");
                LogConsole($"[Singularity] Simulation concluded at step {step}\n");

                // RQ-MODERNIZATION: Export crash dump for scientific analysis
                ExportSingularityCrashDump(step, singularity, spectralDimension);

                return false; // Stop simulation
            }

            // Non-terminal singularity - continue but warn
            _state = SimulationState.SingularityForming;
            return true;
        }

        /// <summary>
        /// RQ-MODERNIZATION: Export crash dump when singularity occurs.
        /// 
        /// The crash dump contains all data needed for post-mortem analysis:
        /// - Edge weights (especially near-zero and anomalous)
        /// - Curvature distribution
        /// - Spectral dimension before crash
        /// - Node mass distribution
        /// </summary>
        private void ExportSingularityCrashDump(int step, SingularityStatus singularity, double? spectralDimension)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string singularityName = singularity.Type.ToString();
                string filename = $"CRASH_DUMP_{singularityName}_step{step}_{timestamp}.json";

                string directory = Path.GetTempPath();
                string filePath = Path.Combine(directory, filename);

                LogConsole($"[Singularity] Exporting crash dump to: {filePath}\n");

                _graph.ExportSingularitySnapshot(filePath, step, singularity, spectralDimension);

                LogConsole($"[Singularity] Crash dump exported successfully\n");
            }
            catch (Exception ex)
            {
                LogConsole($"[Singularity] Failed to export crash dump: {ex.Message}\n");
            }
        }

        /// <summary>
        /// Maps singularity type to simulation state.
        /// </summary>
        private static SimulationState MapSingularityToState(SingularityType type) => type switch
        {
            SingularityType.Numerical => SimulationState.NumericalBreakdown,
            SingularityType.Curvature => SimulationState.CurvatureSingularity,
            SingularityType.Topological => SimulationState.SpacetimeFragmented,
            SingularityType.Horizon => SimulationState.HorizonFormed,
            _ => SimulationState.Finished
        };

        /// <summary>
        /// RQ-MODERNIZATION: Reset simulation state for restart.
        /// </summary>
        public void ResetState()
        {
            _state = SimulationState.Running;
            _terminationReason = "";
            _graph.ResetSingularityCounter();
            _graph.ResetFragmentationCounter();
        }
    }

    /// <summary>
    /// RQ-MODERNIZATION: States of the simulation.
    /// </summary>
    public enum SimulationState
    {
        /// <summary>Simulation is running normally.</summary>
        Running,

        /// <summary>Singularity is forming but not yet terminal.</summary>
        SingularityForming,

        /// <summary>Simulation finished normally.</summary>
        Finished,

        /// <summary>Black hole horizon formed (edges reached zero weight).</summary>
        HorizonFormed,

        /// <summary>Curvature singularity (edges reached infinite weight).</summary>
        CurvatureSingularity,

        /// <summary>Spacetime fragmented into disconnected regions.</summary>
        SpacetimeFragmented,

        /// <summary>Numerical breakdown (NaN in calculations).</summary>
        NumericalBreakdown,

        /// <summary>User cancelled the simulation.</summary>
        Cancelled
    }
}


