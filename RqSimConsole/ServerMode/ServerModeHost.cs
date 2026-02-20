using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using RqSimPlatform.Contracts;
using RQSimulation;
using RQSimulation.Core.Infrastructure;
using RQSimulation.Core.Plugins;
using RQSimulation.Core.Scheduler;
using RQSimulation.GPUOptimized;

namespace RqSimConsole.ServerMode;

internal sealed partial class ServerModeHost : IDisposable
{
    private const string ControlPipeName = "RqSim_Control_Pipe";
    private const string SharedMemoryMapName = "RqSim_Shared_Memory";
    private const long SharedMemoryCapacityBytes = 50L * 1024L * 1024L;

    /// <summary>
    /// Paths to shared settings file that GUI writes and console reads.
    /// Ordered by priority: solution-relative path first, then legacy locations.
    /// </summary>
    private static readonly string[] SharedSettingsPaths = BuildSharedSettingsPaths();

    private static string[] BuildSharedSettingsPaths()
    {
        var paths = new List<string>();

        // 1. Solution-relative: <SolutionRoot>/Users/default/settings/simulation_settings.json
        string? solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        if (solutionRoot is not null)
        {
            paths.Add(Path.Combine(solutionRoot, "Users", "default", "settings", "simulation_settings.json"));
        }

        // 2. Legacy: old %USERPROFILE% path
        paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "RqSimPlatform", "default", "settings", "simulation_settings.json"));

        // 3. Legacy: %APPDATA%
        paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RqSimPlatform", "shared_settings.json"));

        // 4. Legacy: %PROGRAMDATA%
        paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RqSimPlatform", "shared_settings.json"));

        return [.. paths];
    }

    /// <summary>
    /// Finds solution root by walking up from <paramref name="startDir"/>
    /// looking for a directory containing a <c>.sln</c> file.
    /// </summary>
    private static string? FindSolutionRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static readonly int HeaderSize = System.Runtime.InteropServices.Marshal.SizeOf<SharedHeader>();
    private static readonly int RenderNodeSize = System.Runtime.InteropServices.Marshal.SizeOf<RenderNode>();
    private static readonly int RenderEdgeSize = System.Runtime.InteropServices.Marshal.SizeOf<RenderEdge>();

    private readonly CancellationTokenSource _shutdownCts = new();
    private volatile bool _running = true;

    // Simulation state - determines if real simulation is active
    private volatile bool _simulationActive = false;
    private volatile SimulationStatus _currentStatus = SimulationStatus.Stopped;

    // Multi-GPU infrastructure
    private ComputeCluster? _cluster;
    private AsyncAnalysisOrchestrator? _orchestrator;
    private RQGraph? _graph;
    private OptimizedGpuSimulationEngine? _physicsEngine;
    private long _currentTick;
    private int _snapshotInterval = 100;

    private ServerModeSettingsDto _settings = ServerModeSettingsDto.Default;
    private bool _useFallbackSimulation;

    // Latest results cache
    private double _latestSpectralDim = double.NaN;
    private double _latestMcmcEnergy = double.NaN;

    // Cache render nodes to avoid per-tick allocations
    private RenderNode[]? _renderNodesBuffer;

    // Cache render edges to avoid per-tick allocations
    private RenderEdge[]? _renderEdgesBuffer;

    private static readonly JsonSerializerOptions PipeJsonOptions = new(JsonSerializerDefaults.General)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private bool _physicsInitAttempted;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);

        // TRY LOAD SHARED SETTINGS from GUI
        // This ensures console uses same parameters as GUI for consistent visualization
        TryLoadSharedSettings();

        // Initialize Multi-GPU cluster
        InitializeMultiGpuCluster();

        using MemoryMappedFile mmf = MemoryMappedFile.CreateOrOpen(
            SharedMemoryMapName,
            SharedMemoryCapacityBytes,
            MemoryMappedFileAccess.ReadWrite);

        using MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor(0, SharedMemoryCapacityBytes, MemoryMappedFileAccess.ReadWrite);

        // IMPORTANT: Use async lambda to properly await PipeLoopAsync
        // Without async, Task.Run(() => PipeLoopAsync(...)) creates Task<Task> and nobody waits for the inner task!
        var publishTask = Task.Run(() => PublishLoop(accessor, linkedCts.Token), linkedCts.Token);
        var pipeTask = Task.Run(async () => await PipeLoopAsync(linkedCts.Token), linkedCts.Token);

        try
        {
            await Task.WhenAll(publishTask, pipeTask).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        finally
        {
            DisposeMultiGpuCluster();
        }
    }

    private void InitializeMultiGpuCluster()
    {
        try
        {
            _cluster = new ComputeCluster();
            _cluster.Initialize();

            if (_cluster.IsMultiGpuAvailable)
            {
                _orchestrator = new AsyncAnalysisOrchestrator(_cluster);
                _orchestrator.Initialize(100_000);

                // Subscribe to results
                _orchestrator.SpectralCompleted += OnSpectralCompleted;
                _orchestrator.McmcCompleted += OnMcmcCompleted;

                Console.WriteLine($"[ServerMode] Multi-GPU cluster initialized: {_cluster.TotalGpuCount} GPUs");
                Console.WriteLine($"[ServerMode] Physics device: {_cluster.PhysicsDevice?.Name}");
                Console.WriteLine($"[ServerMode] Spectral workers: {_cluster.SpectralWorkers.Length}");
                Console.WriteLine($"[ServerMode] MCMC workers: {_cluster.McmcWorkers.Length}");
            }
            else
            {
                Console.WriteLine($"[ServerMode] Single GPU mode: {_cluster.PhysicsDevice?.Name}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServerMode] GPU init warning: {ex.Message}");
            _cluster?.Dispose();
            _cluster = null;
        }
    }

    private void DisposeMultiGpuCluster()
    {
        if (_orchestrator != null)
        {
            _orchestrator.SpectralCompleted -= OnSpectralCompleted;
            _orchestrator.McmcCompleted -= OnMcmcCompleted;
            _orchestrator.Dispose();
            _orchestrator = null;
        }

        _physicsEngine?.Dispose();
        _physicsEngine = null;

        _cluster?.Dispose();
        _cluster = null;
    }

    /// <summary>
    /// Attempts to load shared settings from GUI.
    /// The new unified file is a serialized <see cref="ServerModeSettingsDto"/>
    /// and can be deserialized directly. Legacy files (FormSettings format) are
    /// handled with per-property fallback parsing.
    /// </summary>
    private void TryLoadSharedSettings()
    {
        try
        {
            foreach (var path in SharedSettingsPaths)
            {
                if (!File.Exists(path))
                    continue;

                string json = File.ReadAllText(path);

                // Try direct deserialization as ServerModeSettingsDto (new unified format)
                try
                {
                    var dto = JsonSerializer.Deserialize<ServerModeSettingsDto>(json);
                    if (dto is not null && dto.NodeCount > 0)
                    {
                        _settings = dto;
                        Console.WriteLine($"[ServerMode] Loaded unified settings from: {path}");
                        Console.WriteLine($"  Core: NodeCount={_settings.NodeCount}, TargetDegree={_settings.TargetDegree}, Temperature={_settings.Temperature:F2}, TotalSteps={_settings.TotalSteps}");
                        Console.WriteLine($"  Physics: ExcitedProb={_settings.InitialExcitedProb:F4}, Lambda={_settings.LambdaState:F4}, EdgeTrialProb={_settings.EdgeTrialProb:F4}");
                        Console.WriteLine($"  Gravity: G={_settings.GravitationalCoupling:F6}, Vacuum={_settings.VacuumEnergyScale:E2}, Warmup={_settings.WarmupDuration}");
                        return;
                    }
                }
                catch (JsonException)
                {
                    // Not a ServerModeSettingsDto — fall through to legacy parsing
                }

                // Legacy per-property parsing (FormSettings format)
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("NodeCount", out var nodeCountProp))
                    _settings = _settings with { NodeCount = nodeCountProp.GetInt32() };

                if (root.TryGetProperty("TargetDegree", out var targetDegreeProp))
                    _settings = _settings with { TargetDegree = targetDegreeProp.GetInt32() };

                if (root.TryGetProperty("Temperature", out var tempProp))
                    _settings = _settings with { Temperature = tempProp.GetDouble() };

                if (root.TryGetProperty("TotalSteps", out var totalStepsProp))
                    _settings = _settings with { TotalSteps = totalStepsProp.GetInt32() };

                if (root.TryGetProperty("InitialExcitedProb", out var excitedProp))
                    _settings = _settings with { InitialExcitedProb = excitedProp.GetDouble() };

                if (root.TryGetProperty("LambdaState", out var lambdaProp))
                    _settings = _settings with { LambdaState = lambdaProp.GetDouble() };

                if (root.TryGetProperty("EdgeTrialProb", out var edgeTrialProp))
                    _settings = _settings with { EdgeTrialProb = edgeTrialProp.GetDouble() };

                if (root.TryGetProperty("InitialEdgeProb", out var initialEdgeProp))
                    _settings = _settings with { InitialEdgeProb = initialEdgeProp.GetDouble() };

                if (root.TryGetProperty("GravitationalCoupling", out var gravProp))
                    _settings = _settings with { GravitationalCoupling = gravProp.GetDouble() };

                if (root.TryGetProperty("VacuumEnergyScale", out var vacuumProp))
                    _settings = _settings with { VacuumEnergyScale = vacuumProp.GetDouble() };

                if (root.TryGetProperty("DecoherenceRate", out var decohProp))
                    _settings = _settings with { DecoherenceRate = decohProp.GetDouble() };

                if (root.TryGetProperty("HotStartTemperature", out var hotStartProp))
                    _settings = _settings with { HotStartTemperature = hotStartProp.GetDouble() };

                if (root.TryGetProperty("WarmupDuration", out var warmupProp))
                    _settings = _settings with { WarmupDuration = warmupProp.GetInt32() };

                if (root.TryGetProperty("GravityTransitionDuration", out var gravTransProp))
                    _settings = _settings with { GravityTransitionDuration = gravTransProp.GetDouble() };

                Console.WriteLine($"[ServerMode] Loaded legacy shared settings from: {path}");
                Console.WriteLine($"  Core: NodeCount={_settings.NodeCount}, TargetDegree={_settings.TargetDegree}, Temperature={_settings.Temperature:F2}, TotalSteps={_settings.TotalSteps}");
                return;
            }

            Console.WriteLine("[ServerMode] No shared settings found, using defaults");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServerMode] Could not load shared settings: {ex.Message}");
        }
    }

    private void OnSpectralCompleted(object? sender, SpectralResultEventArgs e)
    {
        if (e.Result.IsValid)
        {
            _latestSpectralDim = e.Result.SpectralDimension;
            Console.WriteLine($"[ServerMode] Spectral d_s={e.Result.SpectralDimension:F4} (worker {e.Result.WorkerId})");
        }
    }

    private void OnMcmcCompleted(object? sender, McmcResultEventArgs e)
    {
        _latestMcmcEnergy = e.Result.MeanEnergy;
        Console.WriteLine($"[ServerMode] MCMC E={e.Result.MeanEnergy:F4} (worker {e.Result.WorkerId}, T={e.Result.Temperature:F2})");
    }

    private async Task PipeLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Use NamedPipeServerStream.MaxAllowedServerInstances to allow reconnections
                await using NamedPipeServerStream server = new(
                    pipeName: ControlPipeName,
                    direction: PipeDirection.In,
                    maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
                    transmissionMode: PipeTransmissionMode.Byte, // Changed from Message for compatibility
                    options: PipeOptions.Asynchronous);

                Console.WriteLine("[ServerMode] Waiting for client connection...");
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                Console.WriteLine("[ServerMode] Client connected!");

                using StreamReader reader = new(server);

                while (server.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    // Use timeout to prevent blocking forever when client connects without sending data
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    readCts.CancelAfter(TimeSpan.FromSeconds(5)); // 5 second timeout per read

                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync(readCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Read timeout - client connected but didn't send data
                        // Break to allow new connection
                        Console.WriteLine("[ServerMode] Read timeout - client idle, allowing reconnection");
                        break;
                    }

                    if (line is null)
                        break;

                    SimCommand? cmd;
                    try
                    {
                        cmd = JsonSerializer.Deserialize<SimCommand>(line, PipeJsonOptions);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if (cmd is null)
                        continue;

                    switch (cmd.Type)
                    {
                        case SimCommandType.Shutdown:
                            _running = false;
                            _shutdownCts.Cancel();
                            return;

                        case SimCommandType.Handshake:
                            // Report current status - do NOT auto-start simulation
                            // But make sure data plane has something for UI to attach to.
                            // EnsureSimulationInitialized(); // REMOVED: Prevent auto-start of default graph
                            Console.WriteLine($"[ServerMode] Handshake received. Current status: {_currentStatus}");
                            break;

                        case SimCommandType.Start:
                            // Start: reinitialize graph with latest settings to get a fresh run
                            if (_currentStatus == SimulationStatus.Stopped)
                            {
                                ReinitializeSimulation();
                            }
                            else
                            {
                                EnsureSimulationInitialized();
                            }

                            // If GPU engine failed, we still allow a lightweight "fallback" run mode so UI iteration updates work.
                            if (_physicsEngine is null)
                            {
                                _useFallbackSimulation = true;
                                if (_currentStatus == SimulationStatus.Faulted)
                                {
                                    Console.WriteLine("[ServerMode] GPU physics unavailable; switching to fallback simulation mode.");
                                    _currentStatus = SimulationStatus.Stopped;
                                }
                            }

                            if (!_simulationActive)
                            {
                                Console.WriteLine("[ServerMode] >>> START command received <<<");
                                _simulationActive = true;
                                _currentStatus = SimulationStatus.Running;
                                Console.WriteLine("[ServerMode] Simulation STARTED by UI command");
                            }
                            else
                            {
                                // Already active - resume from pause
                                _currentStatus = SimulationStatus.Running;
                                Console.WriteLine("[ServerMode] Simulation RESUMED by UI command");
                            }
                            break;

                        case SimCommandType.Stop:
                            _simulationActive = false;
                            _currentStatus = SimulationStatus.Stopped;
                            _useFallbackSimulation = false;
                            Console.WriteLine("[ServerMode] Simulation STOPPED");
                            break;

                        case SimCommandType.Pause:
                            // Pause keeps simulation active but changes status
                            _currentStatus = SimulationStatus.Paused;
                            Console.WriteLine("[ServerMode] Simulation PAUSED");
                            break;

                        case SimCommandType.Resume:
                            // Resume/Attach: reconnect to running simulation WITHOUT restart
                            // Unlike Start, this does NOT call EnsureSimulationInitialized
                            if (_graph is null)
                            {
                                Console.WriteLine("[ServerMode] Resume: No active session, use Start instead");
                            }
                            else
                            {
                                _simulationActive = true;
                                _currentStatus = SimulationStatus.Running;
                                Console.WriteLine($"[ServerMode] Simulation RESUMED (attached to existing session with {_graph.N} nodes)");
                            }
                            break;

                        case SimCommandType.UpdateSettings:
                            if (!string.IsNullOrEmpty(cmd.PayloadJson))
                            {
                                if (TryApplySettings(cmd.PayloadJson))
                                {
                                    Console.WriteLine($"[ServerMode] Settings applied: Nodes={_settings.NodeCount} Seed={_settings.Seed} Degree={_settings.TargetDegree}");
                                }
                                else
                                {
                                    Console.WriteLine("[ServerMode] Settings update rejected (invalid payload)");
                                }
                            }
                            break;

                        case SimCommandType.GetMultiGpuStatus:
                            Console.WriteLine("[ServerMode] Multi-GPU status requested");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServerMode] Pipe loop exception: {ex.Message}");
            }
        }
    }

    private bool TryApplySettings(string payloadJson)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<ServerModeSettingsDto>(payloadJson, PipeJsonOptions);
            if (settings is null)
                return false;

            // Clamp for safety
            int nodeCount = settings.NodeCount <= 0 ? ServerModeSettingsDto.Default.NodeCount : settings.NodeCount;
            if (nodeCount > 2_000_000)
                nodeCount = 2_000_000;

            int targetDegree = settings.TargetDegree <= 0 ? ServerModeSettingsDto.Default.TargetDegree : settings.TargetDegree;
            if (targetDegree > 256)
                targetDegree = 256;

            int totalSteps = settings.TotalSteps <= 0 ? ServerModeSettingsDto.Default.TotalSteps : settings.TotalSteps;

            // FIX 24: Include extended physics parameters
            // NOTE: Use >= 0 to allow zero values (e.g., GravitationalCoupling=0 disables gravity)
            var newSettings = new ServerModeSettingsDto
            {
                NodeCount = nodeCount,
                TargetDegree = targetDegree,
                Seed = settings.Seed,
                Temperature = settings.Temperature >= 0 ? settings.Temperature : _settings.Temperature,
                TotalSteps = totalSteps,
                // Extended parameters — zero is a valid physics value
                InitialExcitedProb = settings.InitialExcitedProb >= 0 ? settings.InitialExcitedProb : _settings.InitialExcitedProb,
                LambdaState = settings.LambdaState >= 0 ? settings.LambdaState : _settings.LambdaState,
                EdgeTrialProb = settings.EdgeTrialProb >= 0 ? settings.EdgeTrialProb : _settings.EdgeTrialProb,
                InitialEdgeProb = settings.InitialEdgeProb >= 0 ? settings.InitialEdgeProb : _settings.InitialEdgeProb,
                GravitationalCoupling = settings.GravitationalCoupling >= 0 ? settings.GravitationalCoupling : _settings.GravitationalCoupling,
                VacuumEnergyScale = settings.VacuumEnergyScale >= 0 ? settings.VacuumEnergyScale : _settings.VacuumEnergyScale,
                DecoherenceRate = settings.DecoherenceRate >= 0 ? settings.DecoherenceRate : _settings.DecoherenceRate,
                HotStartTemperature = settings.HotStartTemperature >= 0 ? settings.HotStartTemperature : _settings.HotStartTemperature,
                WarmupDuration = settings.WarmupDuration >= 0 ? settings.WarmupDuration : _settings.WarmupDuration,
                GravityTransitionDuration = settings.GravityTransitionDuration >= 0 ? settings.GravityTransitionDuration : _settings.GravityTransitionDuration,

                // Pass through extended physics & flags from UI
                LapseFunctionAlpha = settings.LapseFunctionAlpha > 0 ? settings.LapseFunctionAlpha : _settings.LapseFunctionAlpha,
                WilsonParameter = settings.WilsonParameter > 0 ? settings.WilsonParameter : _settings.WilsonParameter,
                GeometryInertiaMass = settings.GeometryInertiaMass > 0 ? settings.GeometryInertiaMass : _settings.GeometryInertiaMass,
                GaugeFieldDamping = settings.GaugeFieldDamping >= 0 ? settings.GaugeFieldDamping : _settings.GaugeFieldDamping,
                TopologyDecoherenceInterval = settings.TopologyDecoherenceInterval > 0 ? settings.TopologyDecoherenceInterval : _settings.TopologyDecoherenceInterval,
                TopologyDecoherenceTemperature = settings.TopologyDecoherenceTemperature >= 0 ? settings.TopologyDecoherenceTemperature : _settings.TopologyDecoherenceTemperature,
                GaugeTolerance = settings.GaugeTolerance >= 0 ? settings.GaugeTolerance : _settings.GaugeTolerance,
                MaxRemovableFlux = settings.MaxRemovableFlux >= 0 ? settings.MaxRemovableFlux : _settings.MaxRemovableFlux,
                PairCreationMassThreshold = settings.PairCreationMassThreshold >= 0 ? settings.PairCreationMassThreshold : _settings.PairCreationMassThreshold,
                PairCreationEnergy = settings.PairCreationEnergy >= 0 ? settings.PairCreationEnergy : _settings.PairCreationEnergy,
                EdgeWeightQuantum = settings.EdgeWeightQuantum >= 0 ? settings.EdgeWeightQuantum : _settings.EdgeWeightQuantum,
                RngStepCost = settings.RngStepCost >= 0 ? settings.RngStepCost : _settings.RngStepCost,
                EdgeCreationCost = settings.EdgeCreationCost >= 0 ? settings.EdgeCreationCost : _settings.EdgeCreationCost,
                InitialVacuumEnergy = settings.InitialVacuumEnergy >= 0 ? settings.InitialVacuumEnergy : _settings.InitialVacuumEnergy,
                SpectralLambdaCutoff = settings.SpectralLambdaCutoff > 0 ? settings.SpectralLambdaCutoff : _settings.SpectralLambdaCutoff,
                SpectralTargetDimension = settings.SpectralTargetDimension > 0 ? settings.SpectralTargetDimension : _settings.SpectralTargetDimension,
                SpectralDimensionPotentialStrength = settings.SpectralDimensionPotentialStrength >= 0 ? settings.SpectralDimensionPotentialStrength : _settings.SpectralDimensionPotentialStrength,
                GiantClusterThreshold = settings.GiantClusterThreshold >= 0 ? settings.GiantClusterThreshold : _settings.GiantClusterThreshold,
                EmergencyGiantClusterThreshold = settings.EmergencyGiantClusterThreshold >= 0 ? settings.EmergencyGiantClusterThreshold : _settings.EmergencyGiantClusterThreshold,
                GiantClusterDecoherenceRate = settings.GiantClusterDecoherenceRate >= 0 ? settings.GiantClusterDecoherenceRate : _settings.GiantClusterDecoherenceRate,
                MaxDecoherenceEdgesFraction = settings.MaxDecoherenceEdgesFraction >= 0 ? settings.MaxDecoherenceEdgesFraction : _settings.MaxDecoherenceEdgesFraction,
                CriticalSpectralDimension = settings.CriticalSpectralDimension >= 0 ? settings.CriticalSpectralDimension : _settings.CriticalSpectralDimension,
                WarningSpectralDimension = settings.WarningSpectralDimension >= 0 ? settings.WarningSpectralDimension : _settings.WarningSpectralDimension,

                // RQ Experimental Flags — bools: always accept from UI
                EnableNaturalDimensionEmergence = settings.EnableNaturalDimensionEmergence,
                EnableTopologicalParity = settings.EnableTopologicalParity,
                EnableLapseSynchronizedGeometry = settings.EnableLapseSynchronizedGeometry,
                EnableTopologyEnergyCompensation = settings.EnableTopologyEnergyCompensation,
                EnablePlaquetteYangMills = settings.EnablePlaquetteYangMills,
                EnableSymplecticGaugeEvolution = settings.EnableSymplecticGaugeEvolution,
                EnableAdaptiveTopologyDecoherence = settings.EnableAdaptiveTopologyDecoherence,
                PreferOllivierRicciCurvature = settings.PreferOllivierRicciCurvature,

                // Pipeline Module Flags — bools: always accept from UI
                UseSpacetimePhysics = settings.UseSpacetimePhysics,
                UseSpinorField = settings.UseSpinorField,
                UseVacuumFluctuations = settings.UseVacuumFluctuations,
                UseBlackHolePhysics = settings.UseBlackHolePhysics,
                UseYangMillsGauge = settings.UseYangMillsGauge,
                UseEnhancedKleinGordon = settings.UseEnhancedKleinGordon,
                UseInternalTime = settings.UseInternalTime,
                UseRelationalTime = settings.UseRelationalTime,
                UseSpectralGeometry = settings.UseSpectralGeometry,
                UseQuantumGraphity = settings.UseQuantumGraphity,
                UseMexicanHatPotential = settings.UseMexicanHatPotential,
                UseGeometryMomenta = settings.UseGeometryMomenta,
                UseUnifiedPhysicsStep = settings.UseUnifiedPhysicsStep,
                EnforceGaugeConstraints = settings.EnforceGaugeConstraints,
                ValidateEnergyConservation = settings.ValidateEnergyConservation
            };

            // Check if settings actually changed
            bool nodeCountChanged = _settings.NodeCount != newSettings.NodeCount;
            bool seedChanged = _settings.Seed != newSettings.Seed;
            bool degreeChanged = _settings.TargetDegree != newSettings.TargetDegree;

            _settings = newSettings;

            // Log full parameter set so operators can verify what was received
            Console.WriteLine("[ServerMode] === Settings Update Received ===");
            Console.WriteLine($"  Core: Nodes={nodeCount}, Degree={targetDegree}, TotalSteps={totalSteps}, Seed={newSettings.Seed}");
            Console.WriteLine($"  Thermo: Temperature={newSettings.Temperature:F2}, HotStartTemp={newSettings.HotStartTemperature:F1}, Warmup={newSettings.WarmupDuration}");
            Console.WriteLine($"  Graph: InitEdgeProb={newSettings.InitialEdgeProb:F4}, EdgeTrialProb={newSettings.EdgeTrialProb:F4}, Lambda={newSettings.LambdaState:F4}");
            Console.WriteLine($"  Physics: G={newSettings.GravitationalCoupling:F6}, Vacuum={newSettings.VacuumEnergyScale:E2}, Decoh={newSettings.DecoherenceRate:F4}");
            Console.WriteLine($"  Extended: Lapse={newSettings.LapseFunctionAlpha:F4}, Wilson={newSettings.WilsonParameter:F4}, GeoInertia={newSettings.GeometryInertiaMass:F2}");
            Console.WriteLine($"  Flags: NatDim={newSettings.EnableNaturalDimensionEmergence}, Lapse={newSettings.EnableLapseSynchronizedGeometry}, OllivierRicci={newSettings.PreferOllivierRicciCurvature}");

            // Reinitialize graph if node count, seed, or degree changed
            // Do this regardless of simulation status - the graph needs to match settings
            if (nodeCountChanged || seedChanged || degreeChanged)
            {
                Console.WriteLine($"[ServerMode] Graph reinit needed: NodeCount={nodeCountChanged}, Seed={seedChanged}, Degree={degreeChanged} (new={targetDegree})");
                ReinitializeSimulation();
            }
            else
            {
                // Apply physics parameters to running graph without reinit
                ApplySettingsToRunningGraph();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void ReinitializeSimulation()
    {
        _simulationActive = false;
        _currentStatus = SimulationStatus.Stopped;
        _currentTick = 0;

        _physicsEngine?.Dispose();
        _physicsEngine = null;
        _physicsInitAttempted = false;
        _useFallbackSimulation = false;

        // Clean up and rebuild pipeline
        _pipelineMeterListener?.Dispose();
        _pipelineMeterListener = null;
        _pipeline?.CleanupAll();
        _pipeline = null;
        _pipelineInitialized = false;
        lock (_moduleStatsLock) { _moduleStats.Clear(); }

        _graph = null;
        _renderNodesBuffer = null;
        _renderEdgesBuffer = null;

        EnsureSimulationInitialized();
    }

    private void EnsureSimulationInitialized()
    {
        // Initialize a basic graph + engine as soon as UI connects, so shared memory contains drawable data.
        if (_graph is null)
        {
            Console.WriteLine("[ServerMode] Initializing simulation graph...");

            // FIX 24: Apply GUI settings to PhysicsConstants BEFORE creating graph
            ApplySettingsToPhysicsConstants();

            try
            {
                // RQGraph ctor: RQGraph(nodeCount, initialEdgeProb, initialExcitedProb, 
                //                       targetDegree, lambdaState, temperature,
                //                       edgeTrialProbability, measurementThreshold, seed)
                // FIX 28: Use correct parameter order matching RQGraph.Updates.cs constructor

                int n = Math.Max(1, _settings.NodeCount);
                int seed = _settings.Seed;
                double temperature = _settings.Temperature;
                int targetDegree = Math.Max(1, _settings.TargetDegree);

                // Use settings from GUI (loaded via shared_settings.json)
                double initialEdgeProb = _settings.InitialEdgeProb > 0 ? _settings.InitialEdgeProb : 0.1;
                double initialExcitedProb = _settings.InitialExcitedProb > 0 ? _settings.InitialExcitedProb : 0.1;
                double lambdaState = _settings.LambdaState > 0 ? _settings.LambdaState : 0.5;
                double edgeTrialProb = _settings.EdgeTrialProb > 0 ? _settings.EdgeTrialProb : 0.2;
                const double measurementThreshold = 0.8;

                // Log the physics parameters being used
                Console.WriteLine($"[ServerMode] Loaded physics settings (for reference):");
                Console.WriteLine($"  InitialExcitedProb: {initialExcitedProb:F4}");
                Console.WriteLine($"  LambdaState: {lambdaState:F4}");
                Console.WriteLine($"  EdgeTrialProb: {edgeTrialProb:F4}");
                Console.WriteLine($"  GravitationalCoupling: {_settings.GravitationalCoupling:F6} (config only, const in PhysicsConstants)");
                Console.WriteLine($"  VacuumEnergyScale: {_settings.VacuumEnergyScale:E2}");
                Console.WriteLine($"  WarmupDuration: {_settings.WarmupDuration}");

                _graph = new RQGraph(
                    n,
                    initialEdgeProb,
                    initialExcitedProb,
                    targetDegree,
                    lambdaState,
                    temperature,
                    edgeTrialProb,
                    measurementThreshold,
                    seed);

                Console.WriteLine($"[ServerMode] Graph initialized: {_graph.N} nodes, {_graph.FlatEdgesFrom?.Length ?? 0} edges");

                // Initialize asynchronous time system for event-based node processing
                _graph.InitAsynchronousTime();

                // Initialize spectral coordinates for 3D visualization
                // This computes an initial layout from the graph Laplacian
                try
                {
                    _graph.UpdateSpectralCoordinates();
                    Console.WriteLine("[ServerMode] Spectral coordinates computed for visualization");
                }
                catch (Exception coordEx)
                {
                    Console.WriteLine($"[ServerMode] Spectral coordinates computation skipped: {coordEx.Message}");
                    // Continue without spectral coords - FillRenderNodes will use fallback layout
                }

                // Build PhysicsPipeline from settings (Option A: full pipeline in console)
                BuildPipeline();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServerMode] ERROR initializing graph: {ex.Message}");
                _currentStatus = SimulationStatus.Faulted;
                return;
            }
        }

        // Only attempt to create the physics engine once per process lifetime.
        // If it fails (e.g., missing GPU capability), we still keep the server attachable and publish shared memory.
        if (_physicsEngine is null && _graph != null && !_physicsInitAttempted)
        {
            _physicsInitAttempted = true;

            Console.WriteLine("[ServerMode] Initializing physics engine...");
            try
            {
                _physicsEngine = new OptimizedGpuSimulationEngine(_graph);
                _physicsEngine.Initialize();
                _physicsEngine.UploadState();
                Console.WriteLine("[ServerMode] Physics engine initialized.");

                // DEBUG: Dump settings via reflection for comparison with local mode
                DumpConsoleSettings();
            }
            catch (Exception ex)
            {
                ConsoleExceptionLogger.Log("[ServerMode] ERROR initializing physics engine:", ex);
                _physicsEngine?.Dispose();
                _physicsEngine = null;

                // Keep graph alive for UI visualization and mark faulted so UI can reflect it.
                _currentStatus = SimulationStatus.Faulted;

                // Still dump settings even on failure for debugging
                DumpConsoleSettings();
            }
        }

        if (_currentStatus == SimulationStatus.Unknown)
            _currentStatus = SimulationStatus.Stopped;
    }

    /// <summary>
    /// DEBUG: Dumps console mode settings via reflection for comparison with local mode.
    /// </summary>
    private void DumpConsoleSettings()
    {
        try
        {
            var additionalData = new Dictionary<string, object?>
            {
                ["ServerModeSettings"] = new
                {
                    _settings.NodeCount,
                    _settings.TargetDegree,
                    _settings.Seed,
                    _settings.Temperature,
                    _settings.TotalSteps,
                    _settings.InitialExcitedProb,
                    _settings.LambdaState,
                    _settings.EdgeTrialProb,
                    _settings.InitialEdgeProb,
                    _settings.GravitationalCoupling,
                    _settings.VacuumEnergyScale,
                    _settings.DecoherenceRate,
                    _settings.HotStartTemperature,
                    _settings.WarmupDuration,
                    _settings.GravityTransitionDuration
                },
                ["PhysicsEngineInitialized"] = _physicsEngine is not null,
                ["UseFallbackSimulation"] = _useFallbackSimulation,
                ["CurrentStatus"] = _currentStatus.ToString()
            };

            string dumpPath = SettingsDumper.DumpGraphSettings(_graph, "console", additionalData);
            if (!string.IsNullOrEmpty(dumpPath))
            {
                Console.WriteLine($"[ServerMode] Settings dumped to: {dumpPath}");
            }
        }
        catch (Exception dumpEx)
        {
            Console.WriteLine($"[ServerMode] Settings dump failed: {dumpEx.Message}");
        }
    }

    /// <summary>
    /// Applies loaded settings to PhysicsConstants before graph initialization.
    /// Only ScientificMode and UseGpuEdgeAnisotropy are mutable at runtime.
    /// Other physics constants are compile-time const — logged for diagnostics.
    /// The PublishLoop reads _settings directly for GPU shader parameters.
    /// </summary>
    private void ApplySettingsToPhysicsConstants()
    {
        // Apply mutable static fields
        PhysicsConstants.ScientificMode = _settings.PreferOllivierRicciCurvature;
        PhysicsConstants.PreferOllivierRicciCurvature = _settings.PreferOllivierRicciCurvature;

        Console.WriteLine("[ServerMode] Applied physics settings:");
        Console.WriteLine($"  G={_settings.GravitationalCoupling:F4}, Vacuum={_settings.VacuumEnergyScale:E2}, Decoh={_settings.DecoherenceRate:F4}");
        Console.WriteLine($"  Lapse={_settings.LapseFunctionAlpha:F4}, Wilson={_settings.WilsonParameter:F4}, GeoInertia={_settings.GeometryInertiaMass:F2}");
        Console.WriteLine($"  TopoDecoh: interval={_settings.TopologyDecoherenceInterval}, temp={_settings.TopologyDecoherenceTemperature:F4}");
        Console.WriteLine($"  Gauge: tol={_settings.GaugeTolerance:F4}, maxFlux={_settings.MaxRemovableFlux:F4}");
        Console.WriteLine($"  Hawking: pairMass={_settings.PairCreationMassThreshold:F4}, pairE={_settings.PairCreationEnergy:F4}");
        Console.WriteLine($"  Spectral: Λ={_settings.SpectralLambdaCutoff:F2}, d_target={_settings.SpectralTargetDimension:F1}, strength={_settings.SpectralDimensionPotentialStrength:F4}");
        Console.WriteLine($"  Flags: NatDim={_settings.EnableNaturalDimensionEmergence}, TopoParity={_settings.EnableTopologicalParity}, " +
                          $"Lapse={_settings.EnableLapseSynchronizedGeometry}, TopoComp={_settings.EnableTopologyEnergyCompensation}");
        Console.WriteLine($"         Plaquette={_settings.EnablePlaquetteYangMills}, Symplectic={_settings.EnableSymplecticGaugeEvolution}, " +
                          $"AdaptDecoh={_settings.EnableAdaptiveTopologyDecoherence}, OllivierRicci={_settings.PreferOllivierRicciCurvature}");
    }

    /// <summary>
    /// Applies hot-swappable physics parameters to the running graph
    /// without reinitializing topology. Called when settings change
    /// but NodeCount/Seed/Degree remain the same.
    /// </summary>
    private void ApplySettingsToRunningGraph()
    {
        if (_graph is null)
        {
            Console.WriteLine("[ServerMode] No graph to update — settings cached for next init");
            return;
        }

        // NetworkTemperature controls Metropolis-Hastings acceptance (mutable on RQGraph)
        _graph.NetworkTemperature = _settings.Temperature;

        // Hot-swap module enable/disable flags to running pipeline
        SyncModuleEnabledFlags(_settings);

        // Update pipeline dynamic params so modules pick up new values immediately
        if (_pipeline is not null)
        {
            DynamicPhysicsParams dynamicParams = SettingsToDynamicParams(_settings, _currentTick);
            _pipeline.UpdateParameters(in dynamicParams);
        }

        Console.WriteLine("[ServerMode] Physics params applied to running graph:");
        Console.WriteLine($"  NetworkTemperature={_settings.Temperature:F2}");
        Console.WriteLine($"  G={_settings.GravitationalCoupling:F6}, Decoh={_settings.DecoherenceRate:F4}");
        Console.WriteLine($"  (G and Decoh are used by pipeline via DynamicPhysicsParams)");
    }

    private void EnsureRenderBuffer(int nodeCount)
    {
        if (_renderNodesBuffer is null || _renderNodesBuffer.Length < nodeCount)
        {
            _renderNodesBuffer = new RenderNode[nodeCount];
        }
    }

    private static void FillRenderNodes(RQGraph graph, RenderNode[] buffer)
    {
        // NOTE: We don't have a stable "position" API exposed from RQGraph here. For now, we generate a deterministic layout
        // based on node id so UI has visible output and can validate IPC + reconnection + running state.
        // This should be replaced with real positions once the physics engine exposes them.
        int n = graph.N;
        for (int i = 0; i < n; i++)
        {
            float angle = (float)(i * (Math.Tau / Math.Max(1, n)));
            buffer[i] = new RenderNode
            {
                X = MathF.Cos(angle) * 10f,
                Y = MathF.Sin(angle) * 10f,
                Z = 0f,
                R = 0.2f,
                G = 0.8f,
                B = 1.0f,
                Id = i
            };
        }
    }

    private static void FillRenderNodes(RQGraph graph, RenderNode[] buffer, long iteration)
    {
        int n = graph.N;

        // Check if we have valid coordinate sources with relaxed validation
        bool spectralXValid = graph.SpectralX is not null && graph.SpectralX.Length == n;
        bool spectralYValid = graph.SpectralY is not null;
        bool spectralZValid = graph.SpectralZ is not null;
        bool spectralDataValid = spectralXValid && HasValidNumericData(graph.SpectralX!);

        bool hasSpectral = spectralXValid && spectralYValid && spectralZValid && spectralDataValid;

#if DEBUG
        // Log spectral state on first call and every 1000 iterations
        if (iteration == 0 || iteration % 1000 == 0)
        {
            Console.WriteLine($"[DEBUG FillRenderNodes] iteration={iteration} hasSpectral={hasSpectral} " +
                $"(X={spectralXValid}, Y={spectralYValid}, Z={spectralZValid}, Data={spectralDataValid})");
        }
#endif

#pragma warning disable CS0618 // Coordinates is obsolete but needed for visualization
        bool hasCoords = graph.Coordinates is not null &&
                         graph.Coordinates.Length == n &&
                         HasValidCoordData(graph.Coordinates);
#pragma warning restore CS0618

        // Calculate spectral coordinate bounds using PERCENTILES to exclude outliers
        // This prevents a few extreme values from compressing the main distribution
        float spectralP5X = 0, spectralP95X = 0, spectralP5Y = 0, spectralP95Y = 0, spectralP5Z = 0, spectralP95Z = 0;
        float spectralMeanX = 0, spectralMeanY = 0, spectralMeanZ = 0;


        if (hasSpectral)
        {
            // Collect valid values for percentile calculation
            var valuesX = new List<float>(n);
            var valuesY = new List<float>(n);
            var valuesZ = new List<float>(n);

            for (int i = 0; i < n; i++)
            {
                float sx = (float)graph.SpectralX![i];
                float sy = (float)graph.SpectralY![i];
                float sz = (float)graph.SpectralZ![i];

                if (!float.IsNaN(sx) && !float.IsInfinity(sx)) valuesX.Add(sx);
                if (!float.IsNaN(sy) && !float.IsInfinity(sy)) valuesY.Add(sy);
                if (!float.IsNaN(sz) && !float.IsInfinity(sz)) valuesZ.Add(sz);
            }

            // Sort for percentile calculation
            valuesX.Sort();
            valuesY.Sort();
            valuesZ.Sort();

            // Get 5th and 95th percentile indices (or min/max for small arrays)
            int p5IdxX = Math.Max(0, (int)(valuesX.Count * 0.05f));
            int p95IdxX = Math.Min(valuesX.Count - 1, (int)(valuesX.Count * 0.95f));
            int p5IdxY = Math.Max(0, (int)(valuesY.Count * 0.05f));
            int p95IdxY = Math.Min(valuesY.Count - 1, (int)(valuesY.Count * 0.95f));
            int p5IdxZ = Math.Max(0, (int)(valuesZ.Count * 0.05f));
            int p95IdxZ = Math.Min(valuesZ.Count - 1, (int)(valuesZ.Count * 0.95f));

            spectralP5X = valuesX.Count > 0 ? valuesX[p5IdxX] : 0;
            spectralP95X = valuesX.Count > 0 ? valuesX[p95IdxX] : 0;
            spectralP5Y = valuesY.Count > 0 ? valuesY[p5IdxY] : 0;
            spectralP95Y = valuesY.Count > 0 ? valuesY[p95IdxY] : 0;
            spectralP5Z = valuesZ.Count > 0 ? valuesZ[p5IdxZ] : 0;
            spectralP95Z = valuesZ.Count > 0 ? valuesZ[p95IdxZ] : 0;

            // Calculate mean for centering
            spectralMeanX = valuesX.Count > 0 ? valuesX.Average() : 0;
            spectralMeanY = valuesY.Count > 0 ? valuesY.Average() : 0;
            spectralMeanZ = valuesZ.Count > 0 ? valuesZ.Average() : 0;
        }

        // Calculate INDEPENDENT normalization for each axis using PERCENTILE range
        // This spreads the main cluster of nodes evenly in 3D space, ignoring outliers
        float spectralRangeX = spectralP95X - spectralP5X;
        float spectralRangeY = spectralP95Y - spectralP5Y;
        float spectralRangeZ = spectralP95Z - spectralP5Z;

        // SPECTRAL COORDINATES ARE VALID - ALWAYS USE THEM!
        // The spectral embedding provides meaningful 3D geometry from graph Laplacian.
        // Previous "flat detection" logic was incorrect - spectral ranges of 0.6-0.8 are EXCELLENT.
        // We should NEVER use synthetic coordinates when we have valid spectral data.

        // Only use synthetic if we truly have NO spectral data (all zeros or NaN)
        bool useFullySynthetic = !hasSpectral;

        // For rendering: always consider axes valid when hasSpectral is true
        int validAxes = hasSpectral ? 3 : 0;

#if DEBUG
        if (iteration % 2000 == 0)
        {
            Console.WriteLine($"[DEBUG Norm] hasSpectral={hasSpectral} validAxes={validAxes} rangeX={spectralRangeX:F2} rangeY={spectralRangeY:F2} rangeZ={spectralRangeZ:F2} synthetic={useFullySynthetic}");
        }
#endif

        // Use a reasonable minimum range to prevent division by near-zero
        // Spectral coordinates typically span -1 to +1, so min range of 0.1 is safe
        const float minRange = 0.1f;
        spectralRangeX = Math.Max(spectralRangeX, minRange);
        spectralRangeY = Math.Max(spectralRangeY, minRange);
        spectralRangeZ = Math.Max(spectralRangeZ, minRange);

        // Scale each axis to fit in [-10, 10] range (target range = 20)
        // For typical spectral range of ~1.0, this gives scale of ~20x
        // CAP the scale factor to prevent extreme amplification of small variations
        const float maxScale = 25f; // Allow up to 25x to handle normalized eigenvectors
        float normScaleX = Math.Min(20f / spectralRangeX, maxScale);
        float normScaleY = Math.Min(20f / spectralRangeY, maxScale);
        float normScaleZ = Math.Min(20f / spectralRangeZ, maxScale);

        // CRITICAL: Use PERCENTILE CENTER (midpoint of 5th-95th percentile) instead of mean
        // Mean is skewed by outliers; percentile center represents the actual data cluster
        float spectralCenterX = (spectralP5X + spectralP95X) / 2f;
        float spectralCenterY = (spectralP5Y + spectralP95Y) / 2f;
        float spectralCenterZ = (spectralP5Z + spectralP95Z) / 2f;

        // Time-based phase for animation when using synthetic coordinates
        // This creates slow "breathing" animation for flat axes
        float timePhase = (float)(iteration * 0.001); // Slow rotation

        for (int i = 0; i < n; i++)
        {
            float x, y, z;
            float t = (float)i / n * MathF.PI * 2f; // Parameter for synthetic coords

            if (useFullySynthetic)
            {
                // FULLY SYNTHETIC MODE: Create a 3D helix/sphere distribution
                // This ensures 3D visualization even when spectral embedding failed
                float radius = 8f + MathF.Sin(t * 3f) * 2f; // Varying radius for visual interest
                x = MathF.Cos(t + timePhase * 0.5f) * radius;
                z = MathF.Sin(t + timePhase * 0.5f) * radius;
                y = ((float)i / n - 0.5f) * 16f + MathF.Sin(t * 2f + timePhase) * 2f; // Spread along Y axis
            }
            else if (hasSpectral && !double.IsNaN(graph.SpectralX![i]))
            {
                // SPECTRAL MODE: Use actual spectral values - this is the CORRECT path
                // Spectral coordinates from Laplacian eigenvectors represent true graph geometry
                x = ((float)graph.SpectralX![i] - spectralCenterX) * normScaleX;
                y = ((float)graph.SpectralY![i] - spectralCenterY) * normScaleY;
                z = ((float)graph.SpectralZ![i] - spectralCenterZ) * normScaleZ;

                // Clamp outliers to visualization bounds
                x = Math.Clamp(x, -15f, 15f);
                y = Math.Clamp(y, -15f, 15f);
                z = Math.Clamp(z, -15f, 15f);
            }
            else if (hasCoords)
            {
#pragma warning disable CS0618
                x = (float)graph.Coordinates[i].X;
                y = (float)graph.Coordinates[i].Y;
                z = 0f;
#pragma warning restore CS0618
            }
            else
            {
                // Fallback: deterministic circular layout (no animation to reduce jitter)
                float angle = (float)(i * (Math.Tau / Math.Max(1, n)));
                x = MathF.Cos(angle) * 10f;
                y = MathF.Sin(angle) * 10f;
                z = 0f;
            }

            // Color based on node state
            float r, g, b;
            var state = graph.State[i];
            switch (state)
            {
                case NodeState.Excited:
                    r = 1.0f; g = 0.3f; b = 0.1f; // Orange/red for excited
                    break;
                case NodeState.Refractory:
                    r = 0.3f; g = 0.3f; b = 0.8f; // Blue for refractory
                    break;
                default: // Rest
                    r = 0.2f; g = 0.8f; b = 0.2f; // Green for rest
                    break;
            }

            buffer[i] = new RenderNode
            {
                X = x,
                Y = y,
                Z = z,
                R = r,
                G = g,
                B = b,
                Id = i
            };
        }
    }

    /// <summary>
    /// Ensures the edge buffer has enough capacity for the given edge count.
    /// </summary>
    private void EnsureEdgeBuffer(int edgeCount)
    {
        if (_renderEdgesBuffer is null || _renderEdgesBuffer.Length < edgeCount)
        {
            _renderEdgesBuffer = new RenderEdge[edgeCount];
        }
    }

    /// <summary>
    /// Fills the edge buffer with data from the graph's flat edge arrays and weights.
    /// </summary>
    private static void FillRenderEdges(RQGraph graph, RenderEdge[] buffer)
    {
        var from = graph.FlatEdgesFrom;
        var to = graph.FlatEdgesTo;
        var weights = graph.Weights;

        if (from is null || to is null || weights is null)
            return;

        int edgeCount = Math.Min(from.Length, buffer.Length);
        for (int e = 0; e < edgeCount; e++)
        {
            int i = from[e];
            int j = to[e];

            // Get weight from dense matrix, protect against NaN
            double w = weights[i, j];
            if (double.IsNaN(w) || double.IsInfinity(w))
                w = 0.5;

            buffer[e] = new RenderEdge
            {
                FromNode = i,
                ToNode = j,
                Weight = (float)w
            };
        }
    }

    /// <summary>
    /// Checks if double array contains valid numeric data (not all NaN/Infinity).
    /// Zero values ARE valid spectral coordinates - only NaN/Infinity are invalid.
    /// </summary>
    private static bool HasValidNumericData(double[] data)
    {
        if (data is null || data.Length == 0)
            return false;

        // Check a sample of elements for performance
        // We need at least ONE valid (non-NaN, non-Infinity) value
        int step = Math.Max(1, data.Length / 20);
        for (int i = 0; i < data.Length; i += step)
        {
            double v = data[i];
            // Zero IS a valid coordinate - only reject NaN/Infinity
            if (!double.IsNaN(v) && !double.IsInfinity(v))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if coordinate array contains at least one valid (non-zero) coordinate.
    /// </summary>
    private static bool HasValidCoordData((double X, double Y)[] coords)
    {
        if (coords is null || coords.Length == 0)
            return false;

        int step = Math.Max(1, coords.Length / 20);
        for (int i = 0; i < coords.Length; i += step)
        {
            if (coords[i].X != 0.0 || coords[i].Y != 0.0)
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        _pipelineMeterListener?.Dispose();
        _pipeline?.CleanupAll();
        DisposeMultiGpuCluster();
        _shutdownCts.Dispose();
    }
}

// Simulation status enum for shared memory
public enum SimulationStatus
{
    Unknown = 0,
    Running = 1,
    Paused = 2,
    Stopped = 3,
    Faulted = 4
}
