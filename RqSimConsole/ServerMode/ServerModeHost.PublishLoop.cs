using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using RQSimulation;

namespace RqSimConsole.ServerMode;

internal sealed partial class ServerModeHost
{
    private const int FallbackTickIntervalMs = 50;
    private const int MaxModuleStatsEntries = 32;

    private static readonly int ModuleStatsEntrySize = Marshal.SizeOf<ModuleStatsEntry>();

    private void PublishLoop(MemoryMappedViewAccessor accessor, CancellationToken cancellationToken)
    {
        long iteration = 0;
        long lastLoggedIteration = -1;
        long lastLogUtcTicks = 0;
        long lastSimStepUtcTicks = 0;
        bool wasActive = false;

        while (_running && !cancellationToken.IsCancellationRequested)
        {
            if (_simulationActive && _currentStatus == SimulationStatus.Running)
            {
                // Detect restart: simulation transitioned from inactive → active
                if (!wasActive)
                {
                    iteration = 0;
                    lastSimStepUtcTicks = 0;
                    Console.WriteLine("[ServerMode] Iteration counter reset for new run");
                }
                wasActive = true;

                // Enforce TotalSteps limit
                if (_settings.TotalSteps > 0 && iteration >= _settings.TotalSteps)
                {
                    Console.WriteLine($"[ServerMode] Reached TotalSteps={_settings.TotalSteps}, stopping simulation");
                    _simulationActive = false;
                    _currentStatus = SimulationStatus.Stopped;
                }
                // Pipeline path: use full PhysicsPipeline for feature parity with local mode
                else if (_pipeline is not null && _graph is not null)
                {
                    try
                    {
                        ExecutePipelineFrame(iteration, cancellationToken);
                        iteration++;

                        // Periodically compute expensive metrics
                        if (iteration % 10 == 0)
                        {
                            try
                            {
                                double ds = _graph.ComputeSpectralDimension();
                                if (ds > 0 && !double.IsNaN(ds))
                                    _latestSpectralDim = ds;
                                _graph.UpdateSpectralCoordinates();
                            }
                            catch
                            {
                                // Spectral computation can fail - ignore
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        ConsoleExceptionLogger.Log("[ServerMode] Pipeline step error:", ex);
                        _currentStatus = SimulationStatus.Faulted;
                        _simulationActive = false;
                    }
                }
                // Legacy GPU engine path (when pipeline is not available)
                else if (_physicsEngine != null)
                {
                    try
                    {
                        const int batchSize = 10;
                        _physicsEngine.StepGpuBatch(
                            batchSize: batchSize,
                            dt: (float)PhysicsConstants.BaseTimestep,
                            G: (float)PhysicsConstants.GravitationalCoupling,
                            lambda: (float)PhysicsConstants.CosmologicalConstant,
                            diffusionRate: (float)PhysicsConstants.FieldDiffusionRate,
                            higgsLambda: (float)PhysicsConstants.HiggsLambda,
                            higgsMuSquared: (float)PhysicsConstants.HiggsMuSquared);

                        // Increment iteration to match actual physics steps completed
                        iteration += batchSize;

                        // Periodically sync full GPU state to CPU and recompute metrics
                        if (iteration % 20 == 0 && _graph is not null)
                        {
                            try
                            {
                                // Sync all GPU state (weights + scalar field + correlation mass)
                                _physicsEngine.SyncAllStatesToGraph();

                                // Derive node states from scalar field (GPU doesn't update State[])
                                DeriveNodeStatesFromScalarField();

                                // Compute spectral dimension (required for d_S metric)
                                double ds = _graph.ComputeSpectralDimension();
                                if (ds > 0 && !double.IsNaN(ds))
                                    _latestSpectralDim = ds;

                                // Recompute spectral coordinates for visualization
                                _graph.UpdateSpectralCoordinates();
                            }
                            catch
                            {
                                // Sync or spectral computation can fail - ignore
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ConsoleExceptionLogger.Log("[ServerMode] Physics step error:", ex);
                        _currentStatus = SimulationStatus.Faulted;
                        _simulationActive = false;
                    }
                }
                else if (_useFallbackSimulation)
                {
                    // CPU fallback: run graph physics when GPU is unavailable
                    if (_graph is not null)
                    {
                        try
                        {
                            _graph.UnifiedPhysicsStep(PhysicsConstants.BaseTimestep);
                            iteration++;

                            // Periodically compute expensive metrics
                            if (iteration % 10 == 0)
                            {
                                double ds = _graph.ComputeSpectralDimension();
                                if (ds > 0 && !double.IsNaN(ds))
                                    _latestSpectralDim = ds;
                                _graph.UpdateSpectralCoordinates();
                            }
                        }
                        catch (Exception ex)
                        {
                            ConsoleExceptionLogger.Log("[ServerMode] CPU physics step error:", ex);
                        }

                        // Throttle CPU mode to ~20 steps/sec
                        var now = DateTimeOffset.UtcNow.UtcTicks;
                        if (lastSimStepUtcTicks == 0)
                            lastSimStepUtcTicks = now;

                        long elapsed = now - lastSimStepUtcTicks;
                        long targetInterval = TimeSpan.FromMilliseconds(FallbackTickIntervalMs).Ticks;
                        if (elapsed < targetInterval)
                        {
                            try
                            {
                                Task.Delay(TimeSpan.FromTicks(targetInterval - elapsed), cancellationToken).GetAwaiter().GetResult();
                            }
                            catch (OperationCanceledException) { return; }
                        }
                        lastSimStepUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
                    }
                    else
                    {
                        // No graph at all — just throttle
                        var now = DateTimeOffset.UtcNow.UtcTicks;
                        if (lastSimStepUtcTicks == 0)
                            lastSimStepUtcTicks = now;

                        if (now - lastSimStepUtcTicks >= TimeSpan.FromMilliseconds(FallbackTickIntervalMs).Ticks)
                        {
                            iteration++;
                            lastSimStepUtcTicks = now;
                        }
                    }
                }
            }
            else
            {
                wasActive = false;
            }

            _currentTick = iteration;

            var nowTicks = DateTimeOffset.UtcNow.UtcTicks;

            var status = _orchestrator?.GetStatus();

            int nodeCount = _graph?.N ?? 0;
            int edgeCount = _graph?.FlatEdgesFrom?.Length ?? 0;

            // Compute real metrics from graph
            double systemEnergy = 0.0;
            double spectralDim = 0.0;
            int excitedCount = 0;
            double heavyMass = 0.0;
            int largestCluster = 0;
            int strongEdgeCount = 0;
            double qNorm = 0.0;
            double entanglement = 0.0;
            double correlation = 0.0;
            double networkTemp = 0.0;
            double effectiveG = 0.0;

            if (_graph is not null)
            {
                // Count excited nodes
                for (int i = 0; i < _graph.N; i++)
                {
                    if (_graph.State[i] == NodeState.Excited)
                        excitedCount++;
                }

                // Get spectral dimension if available (protect against NaN)
                spectralDim = _graph.SmoothedSpectralDimension;
                if (double.IsNaN(spectralDim) || double.IsInfinity(spectralDim))
                    spectralDim = 0.0;

                // Compute energy metric — use physics Hamiltonian to match local mode
                // (sum of degree penalties + weight coupling + string energy)
                try
                {
                    systemEnergy = _graph.ComputeTotalEnergy();
                    if (double.IsNaN(systemEnergy) || double.IsInfinity(systemEnergy))
                        systemEnergy = 0.0;
                }
                catch
                {
                    systemEnergy = 0.0;
                }

                // Count strong edges from flat edge arrays
                if (_graph.FlatEdgesFrom is not null && _graph.Weights is not null)
                {
                    int edgeLen = _graph.FlatEdgesFrom.Length;
                    for (int e = 0; e < edgeLen; e++)
                    {
                        int i = _graph.FlatEdgesFrom[e];
                        int j = _graph.FlatEdgesTo[e];
                        double w = _graph.Weights[i, j];
                        if (!double.IsNaN(w) && w > 0.7)
                            strongEdgeCount++;
                    }
                }

                // Get heavy mass from correlation mass
                var correlationMass = _graph.CorrelationMass;
                if (correlationMass is not null)
                {
                    for (int i = 0; i < Math.Min(_graph.N, correlationMass.Length); i++)
                    {
                        if (!double.IsNaN(correlationMass[i]))
                            heavyMass += correlationMass[i];
                    }
                }

                // Get largest cluster (with exception protection)
                try
                {
                    var threshold = _graph.GetAdaptiveHeavyThreshold();
                    if (!double.IsNaN(threshold) && threshold > 0)
                    {
                        var clusters = _graph.GetStrongCorrelationClusters(threshold);
                        if (clusters.Count > 0)
                        {
                            largestCluster = clusters.Max(c => c.Count);
                        }
                    }
                }
                catch
                {
                    // Cluster computation can fail on malformed graphs
                    largestCluster = 0;
                }

                // Get quantum metrics if available (with NaN protection)
                try
                {
                    qNorm = _graph.ComputeAvgPairCorrelation();
                    if (double.IsNaN(qNorm) || double.IsInfinity(qNorm))
                        qNorm = 0.0;
                    entanglement = qNorm; // Same metric
                    
                    // FIX 33: Correlation is average edge weight, not qNorm
                    // Get weight stats to compute proper correlation
                    var weightStats = _graph.GetWeightStats(0.7);
                    correlation = weightStats.avgWeight;
                    if (double.IsNaN(correlation) || double.IsInfinity(correlation))
                        correlation = 0.0;
                }
                catch
                {
                    qNorm = entanglement = correlation = 0.0;
                }

                // Network temperature — apply annealing schedule to match local mode behavior
                // Formula: T(t) = T_f + (T_i - T_f) * exp(-t / τ)
                // where τ = totalSteps * DefaultAnnealingFraction
                if (_settings.TotalSteps > 0 && iteration > 0)
                {
                    double startTemp = _settings.HotStartTemperature > 0
                        ? _settings.HotStartTemperature
                        : _settings.Temperature;
                    double finalTemp = PhysicsConstants.FinalAnnealingTemperature;
                    double tau = PhysicsConstants.ComputeAnnealingTimeConstant(_settings.TotalSteps);
                    double annealedTemp = finalTemp + (startTemp - finalTemp) * Math.Exp(-iteration / tau);
                    _graph.NetworkTemperature = annealedTemp;
                }

                networkTemp = _graph.NetworkTemperature;
                if (double.IsNaN(networkTemp) || double.IsInfinity(networkTemp))
                    networkTemp = 1.0;
                
                // Effective gravitational coupling (FIX 33)
                // Use the physics constant or compute from graph metrics
                effectiveG = PhysicsConstants.GravitationalCoupling;
            }

            int maxNodesThatFit = (int)Math.Max(0, (SharedMemoryCapacityBytes - HeaderSize) / Math.Max(1, RenderNodeSize));
            if (nodeCount > maxNodesThatFit)
                nodeCount = maxNodesThatFit;

            // Calculate edge data offset (after header and nodes)
            int edgeDataOffset = HeaderSize + nodeCount * RenderNodeSize;

            // Check if edges fit in remaining capacity
            int maxEdgesThatFit = (int)Math.Max(0, (SharedMemoryCapacityBytes - edgeDataOffset) / Math.Max(1, RenderEdgeSize));
            if (edgeCount > maxEdgesThatFit)
                edgeCount = maxEdgesThatFit;

            // Module stats go AFTER edge data (no collision with nodes/edges)
            var moduleStatsSnapshot = GetModuleStatsSnapshot();
            int moduleStatsCount = Math.Min(moduleStatsSnapshot.Count, MaxModuleStatsEntries);
            int moduleStatsOffset = edgeDataOffset + edgeCount * RenderEdgeSize;

            SharedHeader header = new()
            {
                Iteration = iteration,
                NodeCount = nodeCount,
                EdgeCount = edgeCount,
                SystemEnergy = systemEnergy,
                StateCode = (int)_currentStatus,
                LastUpdateTimestampUtcTicks = nowTicks,

                GpuClusterSize = _cluster?.TotalGpuCount ?? 1,
                BusySpectralWorkers = status?.BusySpectralWorkers ?? 0,
                BusyMcmcWorkers = status?.BusyMcmcWorkers ?? 0,
                // Use spectralDim directly (already NaN-protected), or 0 if not computed yet
                LatestSpectralDimension = spectralDim > 0 ? spectralDim : (double.IsNaN(_latestSpectralDim) ? 0.0 : _latestSpectralDim),
                LatestMcmcEnergy = double.IsNaN(_latestMcmcEnergy) ? 0.0 : _latestMcmcEnergy,
                TotalSpectralResults = status?.TotalSpectralResults ?? 0,
                TotalMcmcResults = status?.TotalMcmcResults ?? 0,

                // Extended metrics
                ExcitedCount = excitedCount,
                HeavyMass = heavyMass,
                LargestCluster = largestCluster,
                StrongEdgeCount = strongEdgeCount,
                QNorm = qNorm,
                Entanglement = entanglement,
                Correlation = correlation,
                NetworkTemperature = networkTemp,
                EffectiveG = effectiveG,

                // Total steps from GUI settings
                TotalSteps = _settings.TotalSteps,

                // Edge data offset (after nodes)
                EdgeDataOffset = edgeDataOffset,

                // Pipeline module stats
                ModuleStatsCount = moduleStatsCount,
                ModuleStatsOffset = moduleStatsCount > 0 ? moduleStatsOffset : 0
            };

            accessor.Write(0, ref header);

            if (_graph != null && nodeCount > 0)
            {
                EnsureRenderBuffer(nodeCount);
                FillRenderNodes(_graph, _renderNodesBuffer!, iteration);
                accessor.WriteArray(HeaderSize, _renderNodesBuffer!, 0, nodeCount);

                // Write edge data after nodes
                if (edgeCount > 0 && _graph.FlatEdgesFrom != null)
                {
                    EnsureEdgeBuffer(edgeCount);
                    FillRenderEdges(_graph, _renderEdgesBuffer!);
                    accessor.WriteArray(edgeDataOffset, _renderEdgesBuffer!, 0, edgeCount);
                }
            }

            // Write module stats AFTER edge data (no collision with nodes/edges)
            WriteModuleStatsToSharedMemory(accessor, moduleStatsSnapshot, moduleStatsCount, moduleStatsOffset);
            
            // Diagnostic logging (throttled to every 2 seconds)
            if (iteration != lastLoggedIteration && nowTicks - lastLogUtcTicks >= TimeSpan.FromSeconds(2).Ticks)
            {
                string coordSource = "None";
                if (_graph is not null)
                {
                    bool hasSpectral = _graph.SpectralX is not null && _graph.SpectralX.Length == _graph.N;
#pragma warning disable CS0618
                    bool hasCoords = _graph.Coordinates is not null && _graph.Coordinates.Length == _graph.N;
#pragma warning restore CS0618
                    coordSource = hasSpectral ? "Spectral3D" : (hasCoords ? "Coords2D" : "Fallback");
                }
                
                Console.WriteLine($"[ServerMode] Tick={iteration} Status={_currentStatus} Nodes={nodeCount} Coords={coordSource} Excited={excitedCount} StrongEdges={strongEdgeCount} SpectralDim={spectralDim:F3}");
                lastLoggedIteration = iteration;
                lastLogUtcTicks = nowTicks;
            }

            if (_simulationActive && _orchestrator != null && _physicsEngine != null && iteration % _snapshotInterval == 0)
            {
                try
                {
                    var snapshot = _physicsEngine.DownloadSnapshot(iteration);
                    _orchestrator.OnPhysicsStepCompleted(snapshot);
                }
                catch
                {
                }
            }

            try
            {
                Task.Delay(50, cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Derives node excited/rest states from the scalar field values.
    /// GPU physics updates scalar field but not State[] directly.
    /// Nodes with scalar field above the adaptive heavy threshold are marked Excited.
    /// </summary>
    private void DeriveNodeStatesFromScalarField()
    {
        if (_graph is null) return;

        double threshold = _graph.GetAdaptiveHeavyThreshold();
        if (double.IsNaN(threshold) || threshold <= 0)
            threshold = 0.5;

        for (int i = 0; i < _graph.N; i++)
        {
            double phi = _graph.ScalarField[i];
            _graph.State[i] = Math.Abs(phi) > threshold ? NodeState.Excited : NodeState.Rest;
        }
    }

    /// <summary>
    /// Writes per-module stats entries into shared memory at the specified offset.
    /// Module stats are placed AFTER edge data to avoid collisions with node/edge regions.
    /// </summary>
    private void WriteModuleStatsToSharedMemory(
        MemoryMappedViewAccessor accessor,
        Dictionary<string, (double TotalMs, long Count, long Errors)> snapshot,
        int count,
        int offset)
    {
        if (count <= 0 || offset <= 0) return;

        int idx = 0;

        foreach (var kvp in snapshot)
        {
            if (idx >= count) break;

            var entry = new ModuleStatsEntry
            {
                NameHash = Fnv1aHash(kvp.Key),
                AvgMs = kvp.Value.Count > 0 ? (float)(kvp.Value.TotalMs / kvp.Value.Count) : 0f,
                Count = (int)Math.Min(kvp.Value.Count, int.MaxValue),
                Errors = (int)Math.Min(kvp.Value.Errors, int.MaxValue)
            };

            accessor.Write(offset + idx * ModuleStatsEntrySize, ref entry);
            idx++;
        }
    }

    /// <summary>
    /// FNV-1a hash for module name mapping (deterministic, 32-bit).
    /// UI side uses same algorithm to map hash → display name.
    /// </summary>
    private static int Fnv1aHash(string text)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return (int)hash;
        }
    }
}
