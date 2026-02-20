using RQSimulation;
using RQSimulation.GPUOptimized;
using RQSimulation.EventBasedModel;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RqSimForms.Forms.Interfaces;

public partial class RqSimEngineApi
{
    // === Energy Ledger for Conservation Tracking ===
    public EnergyLedger EnergyLedger { get; } = new();

    // === Parallel Event Engine for Multi-Threaded Simulation ===
    private ParallelEventEngine? _parallelEngine;

    /// <summary>
    /// Configured CPU thread count from UI (numericUpDown1).
    /// Used for all Parallel.For operations to respect user preference.
    /// </summary>
    public int CpuThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Creates ParallelOptions with the configured CPU thread count.
    /// Use this for all Parallel.For calls to respect UI setting.
    /// </summary>
    public ParallelOptions CreateParallelOptions()
    {
        return new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, CpuThreadCount)
        };
    }

    /// <summary>
    /// Finalizes simulation and stores final metrics
    /// </summary>
    public void FinalizeSimulation(List<int> excitedHistory)
    {
        if (SimulationEngine?.Graph == null) return;
        var graph = SimulationEngine.Graph;

        // Get final spectral dimension - compute fresh if series is empty or last value is 0
        double finalSpectralDim = SeriesSpectralDimension.Count > 0
            ? SeriesSpectralDimension.Where(d => d != 0).LastOrDefault()
            : 0;

        // If still 0, compute it now
        if (finalSpectralDim == 0)
        {
            finalSpectralDim = graph.ComputeSpectralDimension(t_max: 100, num_walkers: 100);
        }

        FinalSpectralDimension = finalSpectralDim;
        FinalNetworkTemperature = graph.NetworkTemperature;

        int finalExcited = graph.State.Count(s => s == NodeState.Excited);
        int maxExcited = excitedHistory.Count > 0 ? excitedHistory.Max() : 0;
        double avgExcitedFinal = excitedHistory.Count > 0 ? excitedHistory.Average() : 0.0;
        var finalHeavy = graph.ComputeHeavyClustersEnergy();
        var finalClusters = graph.GetStrongCorrelationClusters(graph.GetAdaptiveHeavyThreshold());
        int finalHeavyCount = finalClusters.Count(c => c.Count >= RQGraph.HeavyClusterMinSize);
        double scalarEnergy = graph.ComputeScalarFieldEnergy();
        double simulationTime = SeriesSteps.Count * 0.01;

        ModernResult = new RQSimulation.ExampleModernSimulation.ScenarioResult
        {
            FinalTime = simulationTime,
            ExcitedCount = finalExcited,
            HeavyClusterCount = finalHeavyCount,
            HeavyClusterTotalMass = finalHeavy.totalMass,
            HeavyClusterMaxMass = finalHeavy.maxMass,
            HeavyClusterMeanMass = finalHeavyCount > 0 ? finalHeavy.totalMass / finalHeavyCount : 0.0,
            ScalarFieldEnergy = scalarEnergy,
            HiggsFieldEnergy = scalarEnergy * 0.5
        };

        LastResult = new SimulationResult
        {
            AverageExcited = avgExcitedFinal,
            MaxExcited = maxExcited,
            MeasurementConfigured = false,
            MeasurementTriggered = false
        };
    }

    /// <summary>
    /// RQ-COMPLIANT: Event-based simulation loop.
    /// 
    /// Uses priority queue where each node has its own proper time ?_i.
    /// Time dilation from gravity affects node update rate.
    /// This is the TRUE relational time evolution where:
    /// - There is no global "now"
    /// - Each node evolves according to local proper time
    /// - Heavy/curved regions run slower (GR time dilation)
    /// 
    /// RQ-Hypothesis Principle: Time emerges from quantum correlations,
    /// not from external parameter.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <param name="totalEvents">Total events to process (mapped to UI "steps")</param>
    /// <param name="useGpu">Whether to use GPU acceleration</param>
    public void RunEventBasedLoop(CancellationToken ct, int totalEvents, bool useGpu)
    {
        if (SimulationEngine?.Graph == null)
            throw new InvalidOperationException("SimulationEngine not initialized");

        var graph = SimulationEngine.Graph;
        int nodeCount = graph.N;

        // Initialize asynchronous time system
        graph.InitAsynchronousTime();

        // Initialize energy ledger
        EnergyLedger.Initialize(graph.ComputeTotalEnergyUnified());
        OnConsoleLog?.Invoke($"[EventBased] Energy ledger initialized: E_0 = {EnergyLedger.TrackedEnergy:F4}\n");

        // Clear time series
        ClearTimeSeries();

        // Events processed counter (for UI "step" mapping)
        int eventsProcessed = 0;
        int metricsInterval = Math.Max(100, nodeCount); // Collect metrics every ~N events (one "sweep")
        int spectralDimInterval = nodeCount * 20; // Every ~20 sweeps
        int energyValidationInterval = nodeCount * 10; // Every ~10 sweeps

        // NOTE: Initialize to 0 to indicate "not yet computed"
        // UI will show 0.000 until first actual d_S computation
        double lastSpectralDimension = 0.0;
        List<int> excitedHistory = [];
        double dt = 0.01;

        OnConsoleLog?.Invoke($"[EventBased] Starting event-driven simulation: {totalEvents} events, {nodeCount} nodes\n");
        OnConsoleLog?.Invoke($"[EventBased] Metrics interval: {metricsInterval}, Spectral: {spectralDimInterval}\n");

        // Log pipeline integration status
        if (SimulationEngine?.Pipeline is { IsInitialized: true, Count: > 0 })
        {
            OnConsoleLog?.Invoke($"[EventBased] Pipeline ACTIVE: {SimulationEngine.Pipeline.Count} modules, interval={metricsInterval} events\n");
        }

        while (!ct.IsCancellationRequested && eventsProcessed < totalEvents)
        {
            // Process batch of events (10 at a time for efficiency)
            int batchSize = Math.Min(10, totalEvents - eventsProcessed);
            graph.StepEventBasedBatch(batchSize);
            eventsProcessed += batchSize;

            // Map events to equivalent "step" for UI compatibility
            // One "step" → N events (one full sweep of all nodes on average)
            int equivalentStep = eventsProcessed / Math.Max(1, nodeCount);

            // === PIPELINE MODULE EXECUTION ===
            // Execute registered physics modules at metrics interval cadence.
            if (SimulationEngine?.Pipeline is { IsInitialized: true, Count: > 0 }
                && eventsProcessed % metricsInterval == 0)
            {
                var pipelineParams = BuildDynamicParamsFromLiveConfig();
                pipelineParams.DeltaTime = dt;
                pipelineParams.CurrentTime = equivalentStep * dt;
                pipelineParams.TickId = equivalentStep;

                SimulationEngine.Pipeline.UpdateParameters(in pipelineParams);
                SimulationEngine.Pipeline.ExecuteFrameWithParams(graph, dt);
            }

            // Lightweight metrics every step
            int excitedCount = CollectExcitedCount();
            excitedHistory.Add(excitedCount);

            // Full metrics collection at intervals
            if (eventsProcessed % metricsInterval == 0)
            {
                var metrics = CollectMetrics();
                double threshold = Math.Min(graph.GetAdaptiveHeavyThreshold(), RQGraph.HeavyClusterThreshold);

                // === FIX: Update NetworkTemperature by annealing schedule ===
                // In Event-Based mode, temperature must be computed and updated explicitly
                // to show proper cooling during simulation (Big Bang ? Cold Universe)
                double startTemp = LiveConfig.HotStartTemperature;
                double currentTemp = ComputeAnnealingTemperature(equivalentStep, startTemp, totalEvents / Math.Max(1, nodeCount));
                graph.NetworkTemperature = currentTemp;

                // Effective G from live config
                double effectiveG = LiveConfig.GravitationalCoupling;

                StoreMetrics(equivalentStep, metrics.excited, metrics.heavyMass, metrics.heavyCount,
                    metrics.largestCluster, metrics.energy, metrics.strongEdges, metrics.correlation,
                    metrics.qNorm, metrics.entanglement, lastSpectralDimension, currentTemp, effectiveG, threshold,
                    metrics.totalClusters, metrics.avgClusterMass, metrics.maxClusterMass, metrics.avgDegree);

                // Auto-tuning (if enabled)
                string? tuneResult = PerformAutoTuning(
                    equivalentStep, lastSpectralDimension, metrics.excited, metrics.totalClusters,
                    metrics.largestCluster, metrics.heavyMass, nodeCount);

                if (tuneResult != null)
                {
                    OnConsoleLog?.Invoke($"[AutoTune] Event {eventsProcessed}: {tuneResult}\n");
                }
            }

            // Spectral dimension computation (expensive, less frequent)
            if (eventsProcessed % spectralDimInterval == 0 && eventsProcessed > 0)
            {
                double newDim = graph.ComputeSpectralDimension(t_max: 100, num_walkers: 50);

                if (newDim > 0 && !double.IsNaN(newDim))
                {
                    lastSpectralDimension = newDim;

                    // Check graph health
                    var clusters = graph.GetStrongCorrelationClusters(graph.GetAdaptiveHeavyThreshold());
                    int largestClusterSize = clusters.Count > 0 ? clusters.Max(c => c.Count) : 0;
                    var healthStatus = graph.CheckGraphHealth(newDim, largestClusterSize);

                    OnConsoleLog?.Invoke($"[d_S] Event {eventsProcessed}: {healthStatus.StatusDescription}\n");

                    // Fragmentation check
                    if (healthStatus.IsFragmented)
                    {
                        try
                        {
                            graph.CheckFragmentationTerminal(equivalentStep, newDim);
                            string recovery = graph.PerformGraphRecovery(healthStatus);
                            OnConsoleLog?.Invoke($"[FRAGMENTATION RECOVERY] {recovery}\n");
                        }
                        catch (GraphFragmentationException ex)
                        {
                            OnConsoleLog?.Invoke($"[FRAGMENTATION TERMINAL] {ex.Message}\n");
                            FinalizeSimulation(excitedHistory);
                            throw;
                        }
                    }
                }
            }

            // Energy conservation validation
            if (eventsProcessed % energyValidationInterval == 0 && eventsProcessed > 0)
            {
                try
                {
                    double currentEnergy = graph.ComputeTotalEnergyUnified();
                    EnergyLedger.ValidateConservation(currentEnergy);
                }
                catch (EnergyConservationException ex)
                {
                    OnConsoleLog?.Invoke($"[ENERGY FATAL] {ex.Message}\n");
                    OnConsoleLog?.Invoke("[ENERGY] Simulation halted due to conservation violation.\n");
                    // RQ-FIX: Strict enforcement - halt simulation on energy violation
                    throw;
                }
            }

            // Progress logging every ~10000 events
            if (eventsProcessed % 10000 == 0 && eventsProcessed > 0)
            {
                double elapsed = (DateTime.UtcNow - SimulationWallClockStart).TotalSeconds;
                double eventsPerSec = eventsProcessed / elapsed;
                double globalTime = graph.GlobalTime;
                OnConsoleLog?.Invoke($"[EventBased] Events: {eventsProcessed}/{totalEvents}, " +
                    $"?_global={globalTime:F3}, speed={eventsPerSec:F0} ev/s, d_S={lastSpectralDimension:F2}\n");
            }
        }

        // Finalize
        FinalizeSimulation(excitedHistory);

        // Final energy check
        double finalEnergy = graph.ComputeTotalEnergyUnified();
        double energyDrift = Math.Abs(finalEnergy - EnergyLedger.TrackedEnergy);
        OnConsoleLog?.Invoke($"[EventBased] Simulation complete: {eventsProcessed} events, " +
            $"d_S={FinalSpectralDimension:F3}, E_drift={energyDrift:F6}\n");
    }

    /// <summary>
    /// RQ-COMPLIANT: Parallel event-based simulation loop.
    /// 
    /// Uses graph coloring to identify causally independent node groups,
    /// then processes each group in parallel with work-stealing thread pool.
    /// 
    /// Key RQ-Hypothesis insight:
    /// - No global "now" means nodes without causal connection can evolve independently
    /// - Causal independence = no shared edges AND no shared neighbors
    /// - Graph coloring partitions nodes into such independent sets
    /// 
    /// NOTE ON TIME: The "equivalentStep" and "eventsProcessed" are UI-ONLY counters
    /// for progress display. They do NOT represent physical time - each node has its
    /// own proper time ?_i tracked by the graph's asynchronous time system.
    /// 
    /// Performance benefits:
    /// - Thread pool is created once, reused across all sweeps
    /// - Work-stealing balances load between threads
    /// - Barrier sync between color groups ensures causality
    /// - Typical speedup: 2-4x on multi-core systems
    /// - GPU acceleration for gravity/curvature computation (10-50x for large graphs)
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <param name="totalEvents">Total events to process (UI progress counter only)</param>
    /// <param name="useParallel">Enable multi-threaded processing</param>
    /// <param name="useGpu">Enable GPU acceleration for gravity computation</param>
    public void RunParallelEventBasedLoop(CancellationToken ct, int totalEvents, bool useParallel = true, bool useGpu = false)
    {
        if (SimulationEngine?.Graph == null)
            throw new InvalidOperationException("SimulationEngine not initialized");

        var graph = SimulationEngine.Graph;
        int nodeCount = graph.N;

        // Initialize asynchronous time system (proper time per node)
        graph.InitAsynchronousTime();

        // Initialize energy ledger
        EnergyLedger.Initialize(graph.ComputeTotalEnergyUnified());
        OnConsoleLog?.Invoke($"[ParallelEvent] Energy ledger initialized: E_0 = {EnergyLedger.TrackedEnergy:F4}\n");

        // Initialize GPU engines if requested
        bool gpuActive = false;
        if (useGpu && GpuAvailable)
        {
            gpuActive = InitializeGpuEngines();
            if (gpuActive)
            {
                GpuStats.IsActive = true;
                OnConsoleLog?.Invoke($"[ParallelEvent] GPU acceleration ENABLED for gravity/curvature\n");
            }
            else
            {
                OnConsoleLog?.Invoke($"[ParallelEvent] GPU init failed, using CPU fallback\n");
            }
        }

        // Initialize parallel engine if requested
        if (useParallel)
        {
            _parallelEngine?.Dispose();
            _parallelEngine = new ParallelEventEngine(graph);
            _parallelEngine.ComputeGraphColoring();
            OnConsoleLog?.Invoke($"[ParallelEvent] Workers: {_parallelEngine.WorkerCount}, Colors: {_parallelEngine.ColorCount}\n");
        }

        // Clear time series
        ClearTimeSeries();

        // CRITICAL FIX: Set LiveTotalSteps to number of SWEEPS (not events)
        // UI timer reads LiveTotalSteps and compares to LiveStep (which is sweepCount)
        int totalSweeps = totalEvents / Math.Max(1, nodeCount);
        Dispatcher.LiveTotalSteps = totalSweeps;

        // UI progress counter (NOT physical time!)
        int eventsProcessed = 0;

        // FIXED: Collect metrics EVERY SWEEP for responsive UI
        // metricsInterval in events = nodeCount (one sweep)
        int metricsInterval = nodeCount; // Every sweep exactly
        int spectralDimInterval = Math.Max(nodeCount * 10, 2000); // Every 10 sweeps
        int energyValidationInterval = Math.Max(nodeCount * 5, 1000); // Every 5 sweeps
        int recolorInterval = Math.Max(nodeCount * 20, 5000); // Recompute coloring every 20 sweeps
        int progressLogInterval = Math.Max(nodeCount * 5, 1000); // Log progress every 5 sweeps
        int gravityInterval = 5; // Run GPU gravity every 5 sweeps (batched for efficiency)
        int gpuSyncInterval = 50; // Sync GPU weights back every 50 sweeps

        // NOTE: Initialize to 0 to indicate "not yet computed"
        // UI will show 0.000 until first actual d_S computation
        double lastSpectralDimension = 0.0;
        List<int> excitedHistory = [];
        double dt = 0.01;

        // Track sweep count for UI "step" display
        int sweepCount = 0;

        OnConsoleLog?.Invoke($"[ParallelEvent] Starting: {totalEvents} events, {nodeCount} nodes, parallel={useParallel}, gpu={gpuActive}\n");
        OnConsoleLog?.Invoke($"[ParallelEvent] Intervals: metrics={metricsInterval}, spectral={spectralDimInterval}, progress={progressLogInterval}\n");

        // Log pipeline integration status
        if (SimulationEngine?.Pipeline is { IsInitialized: true, Count: > 0 })
        {
            OnConsoleLog?.Invoke($"[ParallelEvent] Pipeline ACTIVE: {SimulationEngine.Pipeline.Count} modules, interval={gravityInterval} sweeps\n");
        }

        while (!ct.IsCancellationRequested && eventsProcessed < totalEvents)
        {
            int batchEvents;

            if (useParallel && _parallelEngine != null)
            {
                // Parallel sweep: process all nodes once, grouped by color
                batchEvents = _parallelEngine.ProcessParallelSweep(dt);
                eventsProcessed += batchEvents;
                sweepCount++;

                // Recompute coloring periodically (topology may change)
                if (eventsProcessed % recolorInterval == 0 && _parallelEngine.NeedsRecoloring)
                {
                    _parallelEngine.ComputeGraphColoring();
                    OnConsoleLog?.Invoke($"[ParallelEvent] Recolored: {_parallelEngine.ColorCount} colors\n");
                }
            }
            else
            {
                // Sequential fallback
                batchEvents = Math.Min(nodeCount, totalEvents - eventsProcessed);
                graph.StepEventBasedBatch(batchEvents);
                eventsProcessed += batchEvents;
                sweepCount++;
            }

            // === GPU GRAVITY STEP (batched for efficiency) ===
            // Run gravity computation on GPU every N sweeps
            if (gpuActive && sweepCount % gravityInterval == 0)
            {
                // In CSR ownership mode, do not use dense GPU gravity path.
                if (ActiveEngineType == GpuEngineType.Csr && CsrUnifiedOwnsWeights && CsrUnifiedEngine != null)
                {
                    // Make sure graph is consistent with CSR weights before applying any CPU-side operations.
                    PullWeightsFromCsrUnified(graph);

                    double effectiveG = LiveConfig.GravitationalCoupling;
                    if (effectiveG > 0)
                    {
                        ImprovedNetworkGravity.EvolveNetworkGeometryForman(graph, dt, effectiveG);
                    }

                    // Push updated weights back into CSR buffers without rebuilding topology.
                    PushWeightsToCsrUnified(graph);
                }
                else
                {
                    double effectiveG = LiveConfig.GravitationalCoupling;

                    // Skip GPU gravity when G=0 (consistent with CPU and CSR paths)
                    if (effectiveG <= 0 && PhysicsConstants.CosmologicalConstant == 0)
                    {
                        // No gravity contribution — skip GPU dispatch entirely
                    }
                    else if (OptimizedGpuEngine != null)
                    {
                        // Batch GPU computation (5 steps at once for reduced overhead)
                        OptimizedGpuEngine.StepGpuBatch(
                            batchSize: gravityInterval,
                            dt: (float)dt,
                            G: (float)effectiveG,
                            lambda: (float)PhysicsConstants.CosmologicalConstant,
                            diffusionRate: (float)PhysicsConstants.FieldDiffusionRate,
                            higgsLambda: (float)PhysicsConstants.HiggsLambda,
                            higgsMuSquared: (float)PhysicsConstants.HiggsMuSquared);

                        // Track GPU kernel launches (4 shaders × batchSize)
                        GpuStats.RecordKernelLaunch(4 * gravityInterval);

                        // Comprehensive sync: weights, scalar field, and derived metrics
                        OptimizedGpuEngine.SyncAllStatesToGraph();
                        GpuStats.RecordWeightSync();
                    }
                    else if (effectiveG > 0 && graph.IsGpuGravityActive())
                    {
                        // Use standalone GPU gravity engine
                        graph.EvolveGravityGpuBatch(
                            batchSize: gravityInterval,
                            dt: dt,
                            G: effectiveG,
                            lambda: PhysicsConstants.CosmologicalConstant);
                        GpuStats.RecordKernelLaunch(2 * gravityInterval);
                    }
                }
            }
            else if (!gpuActive && sweepCount % gravityInterval == 0)
            {
                // CPU gravity fallback (less frequent for performance)
                double effectiveG = LiveConfig.GravitationalCoupling;
                if (effectiveG > 0)
                {
                    ImprovedNetworkGravity.EvolveNetworkGeometryForman(graph, dt, effectiveG);
                }

                // In CSR mode, keep CSR buffers in sync with weights.
                if (ActiveEngineType == GpuEngineType.Csr && CsrUnifiedOwnsWeights && CsrUnifiedEngine != null)
                {
                    PushWeightsToCsrUnified(graph);
                }
            }

            // === CSR UNIFIED STEP (Stage 6) ===
            // Runs constraint + spectral action + quantum edges on CSR topology.
            // This is currently treated as a derived GPU working set; it does not mutate graph weights.
            if (gpuActive && ActiveEngineType == GpuEngineType.Csr)
            {
                // Sync CSR buffers from current graph snapshot less frequently than gravity.
                var csrUnified = TrySyncCsrUnifiedEngine(graph, sweepCount, syncInterval: gpuSyncInterval);
                if (csrUnified != null)
                {
                    csrUnified.PhysicsStepGpu(dt);

                    // Optional lightweight logging (not every sweep)
                    if (sweepCount % progressLogInterval == 0)
                    {
                        OnConsoleLog?.Invoke(
                            $"[CSR Unified] sweep={sweepCount} constraint={csrUnified.LastConstraintViolation:F6} spectral={csrUnified.LastSpectralAction:F6}\n");
                    }
                }
            }

            // === PIPELINE MODULE EXECUTION ===
            // Execute registered physics modules via Pipeline.ExecuteFrameWithParams().
            // This bridges the gap between LiveConfig params and the module system.
            // Runs at the same cadence as gravity to avoid excessive overhead.
            if (SimulationEngine?.Pipeline is { IsInitialized: true, Count: > 0 }
                && sweepCount % gravityInterval == 0)
            {
                var pipelineParams = BuildDynamicParamsFromLiveConfig();
                pipelineParams.DeltaTime = dt;
                pipelineParams.CurrentTime = sweepCount * dt;
                pipelineParams.TickId = sweepCount;

                SimulationEngine.Pipeline.UpdateParameters(in pipelineParams);
                SimulationEngine.Pipeline.ExecuteFrameWithParams(graph, dt);
            }

            // UI "step" counter - NOT physical time, just for display
            // Maps sweep count to "equivalent step" for UI compatibility
            int equivalentStep = sweepCount;

            // Lightweight metrics every sweep (for excitedHistory tracking)
            int excitedCount = CollectExcitedCount();
            excitedHistory.Add(excitedCount);

            // Full metrics collection - THROTTLED to reduce lock contention
            // Store every 20 sweeps to reduce CPU overhead from CollectMetrics()
            // UI will display slightly delayed data but simulation runs faster
            int storeInterval = 20; // Reduced from 5 to 20 for performance
            bool shouldStoreMetrics = (sweepCount % storeInterval == 0) || sweepCount <= 5;

            if (shouldStoreMetrics)
            {
                // FIX: Update correlation weights less frequently (expensive O(E) operation)
                // Only update every 50 sweeps instead of every store to reduce CPU load
                bool shouldUpdateWeights = (sweepCount % 50 == 0) || sweepCount <= 5;
                if (shouldUpdateWeights)
                {
                    graph.UpdateCorrelationWeights();
                }

                var metrics = CollectMetrics();
                double threshold = Math.Min(graph.GetAdaptiveHeavyThreshold(), RQGraph.HeavyClusterThreshold);

                // === FIX: Update NetworkTemperature by annealing schedule ===
                // In Event-Based mode, temperature must be computed and updated explicitly
                // to show proper cooling during simulation (Big Bang ? Cold Universe)
                double startTemp = LiveConfig.HotStartTemperature;
                double currentTemp = ComputeAnnealingTemperature(equivalentStep, startTemp, totalSweeps);
                graph.NetworkTemperature = currentTemp;

                double effectiveG = LiveConfig.GravitationalCoupling;

                // Compute topology metrics for Summary tab (edge/component counts)
                // Edge count: count undirected edges (i<j)
                int edgeCount = 0;
                for (int i = 0; i < graph.N; i++)
                {
                    foreach (int j in graph.Neighbors(i))
                    {
                        if (j > i) edgeCount++;
                    }
                }
                // Component count via union-find over current topology
                int componentCount = 0;
                {
                    int[] parent = new int[nodeCount];
                    int[] rank = new int[nodeCount];
                    for (int i = 0; i < nodeCount; i++) { parent[i] = i; rank[i] = 0; }
                    int Find(int x)
                    {
                        while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                        return x;
                    }
                    void Union(int a, int b)
                    {
                        int pa = Find(a), pb = Find(b);
                        if (pa == pb) return;
                        if (rank[pa] < rank[pb]) parent[pa] = pb;
                        else if (rank[pa] > rank[pb]) parent[pb] = pa;
                        else { parent[pb] = pa; rank[pa]++; }
                    }
                    for (int i = 0; i < nodeCount; i++)
                    {
                        foreach (int j in graph.Neighbors(i))
                        {
                            if (i < j) Union(i, j);
                        }
                    }
                    var seen = new HashSet<int>();
                    for (int i = 0; i < nodeCount; i++) seen.Add(Find(i));
                    componentCount = seen.Count;
                }

                // Store metrics - this updates Dispatcher for UI
                StoreMetrics(equivalentStep, metrics.excited, metrics.heavyMass, metrics.heavyCount,
                    metrics.largestCluster, metrics.energy, metrics.strongEdges, metrics.correlation,
                    metrics.qNorm, metrics.entanglement, lastSpectralDimension, currentTemp, effectiveG, threshold,
                    metrics.totalClusters, metrics.avgClusterMass, metrics.maxClusterMass, metrics.avgDegree,
                    edgeCount, componentCount);

                // Auto-tuning (if enabled)
                string? tuneResult = PerformAutoTuning(
                    equivalentStep, lastSpectralDimension, metrics.excited, metrics.totalClusters,
                    metrics.largestCluster, metrics.heavyMass, nodeCount);

                if (tuneResult != null)
                {
                    OnConsoleLog?.Invoke($"[AutoTune] Sweep {sweepCount}: {tuneResult}\n");
                }
            } // end shouldStoreMetrics

            // Spectral dimension (expensive, less frequent)
            // Use GPU if available for faster computation
            if (eventsProcessed % spectralDimInterval == 0 && eventsProcessed > 0)
            {
                double newDim;
                if (gpuActive && (GpuSpectralWalkEngine != null || GpuHeatKernelEngine != null))
                {
                    // GPU-accelerated spectral dimension with automatic method selection
                    // and topology synchronization
                    newDim = ComputeSpectralDimensionGpu(graph, enableCpuComparison: false);
                }
                else
                {
                    newDim = graph.ComputeSpectralDimension(t_max: 100, num_walkers: 50);
                }

                if (newDim > 0 && !double.IsNaN(newDim))
                {
                    lastSpectralDimension = newDim;
                    var clusters = graph.GetStrongCorrelationClusters(graph.GetAdaptiveHeavyThreshold());
                    int largestClusterSize = clusters.Count > 0 ? clusters.Max(c => c.Count) : 0;
                    var healthStatus = graph.CheckGraphHealth(newDim, largestClusterSize);
                    OnConsoleLog?.Invoke($"[d_S] Sweep {sweepCount}: {healthStatus.StatusDescription}\n");

                    if (healthStatus.IsFragmented)
                    {
                        try
                        {
                            graph.CheckFragmentationTerminal(equivalentStep, newDim);
                            string recovery = graph.PerformGraphRecovery(healthStatus);
                            OnConsoleLog?.Invoke($"[FRAGMENTATION RECOVERY] {recovery}\n");
                        }
                        catch (GraphFragmentationException ex)
                        {
                            OnConsoleLog?.Invoke($"[FRAGMENTATION TERMINAL] {ex.Message}\n");
                            FinalizeSimulation(excitedHistory);
                            throw;
                        }
                    }
                }
            }

            // Energy validation
            if (eventsProcessed % energyValidationInterval == 0 && eventsProcessed > 0)
            {
                try
                {
                    double currentEnergy = graph.ComputeTotalEnergyUnified();
                    EnergyLedger.ValidateConservation(currentEnergy);
                }
                catch (EnergyConservationException ex)
                {
                    OnConsoleLog?.Invoke($"[ENERGY] {ex.Message}\n");
                }
            }

            // Progress logging
            if (eventsProcessed % progressLogInterval == 0 && eventsProcessed > 0)
            {
                double elapsed = (DateTime.UtcNow - SimulationWallClockStart).TotalSeconds;
                double eventsPerSec = elapsed > 0 ? eventsProcessed / elapsed : 0;
                double sweepsPerSec = elapsed > 0 ? sweepCount / elapsed : 0;
                string parallelStats = _parallelEngine != null
                    ? $", parallel={_parallelEngine.ParallelUpdates / (double)(_parallelEngine.ParallelUpdates + _parallelEngine.SequentialUpdates + 1):P0}"
                    : "";
                string gpuStats = gpuActive ? $", GPU kernels={GpuStats.KernelLaunches}" : "";
                OnConsoleLog?.Invoke($"[ParallelEvent] Sweep {sweepCount}, Events: {eventsProcessed}/{totalEvents}, " +
                    $"speed={sweepsPerSec:F1} sweeps/s, d_S={lastSpectralDimension:F2}{parallelStats}{gpuStats}\n");
            }

            // CRITICAL FIX: Variable yielding to UI thread
            // Every 10 sweeps, brief sleep to allow UI to acquire _writeLock for buffer swap
            // Less frequent than before (was every 5) for better simulation speed
            if (sweepCount % 10 == 0)
            {
                Thread.Sleep(5); // 5ms - brief yield for UI
            }
            else if (sweepCount % 50 == 0)
            {
                Thread.Sleep(1); // 1ms yield every 50 sweeps
            }
        }

        // Finalize
        FinalizeSimulation(excitedHistory);

        // Report parallel stats
        if (_parallelEngine != null)
        {
            OnConsoleLog?.Invoke($"[ParallelEvent] {_parallelEngine.GetStatsSummary()}\n");
            _parallelEngine.Dispose();
            _parallelEngine = null;
        }

        // Report GPU stats
        if (gpuActive)
        {
            OnConsoleLog?.Invoke($"[GPU] Total kernels: {GpuStats.KernelLaunches}, syncs: {GpuStats.WeightSyncs}, rebuilds: {GpuStats.TopologyRebuilds}\n");
            DisposeGpuEngines();
        }

        double finalEnergy = graph.ComputeTotalEnergyUnified();
        double energyDrift = Math.Abs(finalEnergy - EnergyLedger.TrackedEnergy);
        OnConsoleLog?.Invoke($"[ParallelEvent] Complete: sweeps={sweepCount}, d_S={FinalSpectralDimension:F3}, E_drift={energyDrift:F6}\n");
    }
}
