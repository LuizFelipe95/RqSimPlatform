using RQSimulation;
using RQSimulation.GPUOptimized;
using RQSimulation.GPUCompressedSparseRow;
using RQSimulation.GPUCompressedSparseRow.Unified;
using RQSimulation.EventBasedModel;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RqSimForms.Forms.Interfaces;

/// <summary>
/// GPU compute engine type selection.
/// </summary>
public enum GpuEngineType
{
    /// <summary>Auto-select based on graph characteristics (size, sparsity).</summary>
    Auto,
    /// <summary>Original dense-style GPU engine.</summary>
    Original,
    /// <summary>CSR (Compressed Sparse Row) optimized for large sparse graphs.</summary>
    Csr,
    /// <summary>CPU-only computation (no GPU).</summary>
    CpuOnly
}

public partial class RqSimEngineApi
{
    // === GPU Engines ===
    public bool GpuAvailable { get; set; }
    public SpectralWalkEngine? GpuSpectralWalkEngine { get; private set; }
    public GpuSpectralEngine? GpuHeatKernelEngine { get; private set; }
    public StatisticsEngine? GpuStatisticsEngine { get; private set; }
    public OptimizedGpuSimulationEngine? OptimizedGpuEngine { get; private set; }

    /// <summary>
    /// CSR-format Cayley evolution engine for large sparse graphs.
    /// Uses BiCGStab solver with double precision.
    /// </summary>
    public GpuCayleyEvolutionEngineCsr? CsrCayleyEngine { get; private set; }

    /// <summary>
    /// Stage 6: Unified CSR engine for constraint/spectral/quantum-edge operations.
    /// Currently used as a derived GPU working set and metrics accelerator.
    /// </summary>
    public CsrUnifiedEngine? CsrUnifiedEngine { get; private set; }

    /// <summary>
    /// Current GPU engine type selection.
    /// </summary>
    public GpuEngineType CurrentEngineType { get; private set; } = GpuEngineType.Auto;

    /// <summary>
    /// Actual engine type being used (resolved from Auto).
    /// </summary>
    public GpuEngineType ActiveEngineType { get; private set; } = GpuEngineType.Original;

    // === GPU Performance Tracking ===
    public GpuDiagnostics GpuStats { get; } = new();

    /// <summary>
    /// Holds GPU performance and shader diagnostics
    /// </summary>
    public class GpuDiagnostics
    {
        public bool IsActive { get; set; }
        public string? DeviceName { get; set; }
        public int KernelLaunches { get; set; }
        public double TotalGpuTimeMs { get; set; }
        public double TotalCopyTimeMs { get; set; }
        public int TopologyRebuilds { get; set; }
        public int WeightSyncs { get; set; }

        /// <summary>
        /// GPU-accelerated operations (shaders used per step)
        /// </summary>
        public string[] AcceleratedOperations { get; } =
        [
            "FormanCurvatureShader - Ricci curvature on edges (O(E) parallel)",
            "GravityShader - Weight evolution via curvature flow (O(E) parallel)",
            "ScalarLaplacianShader - Klein-Gordon field diffusion (O(N) parallel)",
            "ApplyScalarDeltaShader - Field update accumulation (O(N) parallel)",
            "SpectralWalkShader - Random walks for d_S (50K walkers parallel)"
        ];

        /// <summary>
        /// CPU-bound operations (not GPU accelerated)
        /// </summary>
        public string[] CpuBoundOperations { get; } =
        [
            "Topology updates (Step, BuildSoAViews) - every 100 steps",
            "Cluster detection (Union-Find) - every 10+ steps",
            "Metric collection (excited count, cluster stats) - every 10+ steps",
            "Quantum state updates - every step",
            "Spectral dimension computation - every 200 steps"
        ];

        /// <summary>
        /// Optimized intervals for performance (steps)
        /// </summary>
        public int TopologyUpdateInterval { get; set; } = 100;
        public int WeightSyncInterval { get; set; } = 50;
        public int MetricsInterval { get; set; } = 10;
        public int SpectralDimInterval { get; set; } = 200;

        public void Reset()
        {
            IsActive = false;
            KernelLaunches = 0;
            TotalGpuTimeMs = 0;
            TotalCopyTimeMs = 0;
            TopologyRebuilds = 0;
            WeightSyncs = 0;
        }

        public void RecordKernelLaunch(int count = 4) => KernelLaunches += count;
        public void RecordTopologyRebuild() => TopologyRebuilds++;
        public void RecordWeightSync() => WeightSyncs++;
    }

    /// <summary>
    /// Set the GPU engine type to use for physics computation.
    /// </summary>
    /// <param name="engineType">Engine type to use</param>
    public void SetGpuEngineType(GpuEngineType engineType)
    {
        CurrentEngineType = engineType;
        OnConsoleLog?.Invoke($"[GPU] Engine type set to: {engineType}\n");

        // If simulation is running, reinitialize engines
        if (SimulationEngine?.Graph != null && GpuAvailable && engineType != GpuEngineType.CpuOnly)
        {
            InitializeGpuEnginesWithType(engineType);
        }
    }

    /// <summary>
    /// Initializes GPU engines for the current graph
    /// </summary>
    public bool InitializeGpuEngines()
    {
        return InitializeGpuEnginesWithType(CurrentEngineType);
    }

    /// <summary>
    /// Initializes GPU engines with specific engine type selection.
    /// </summary>
    private bool InitializeGpuEnginesWithType(GpuEngineType requestedType)
    {
        if (SimulationEngine?.Graph == null) return false;

        var graph = SimulationEngine.Graph;
        int edgeCount = graph.FlatEdgesFrom.Length;
        if (edgeCount == 0) return false;

        try
        {
            graph.BuildSoAViews();

            // Determine actual engine type
            int nnz = graph.CsrOffsets[graph.N];
            GpuEngineType actualType = requestedType;

            if (requestedType == GpuEngineType.Auto)
            {
                // Use CSR recommendation logic from factory
                var recommended = GpuCayleyEngineFactory.RecommendEngineType(graph.N, nnz);
                actualType = recommended == GpuCayleyEngineFactory.EngineType.Csr
                    ? GpuEngineType.Csr
                    : GpuEngineType.Original;
                OnConsoleLog?.Invoke($"[GPU] Auto-selected engine: {actualType} (N={graph.N}, nnz={nnz})\n");
            }

            ActiveEngineType = actualType;

            // Initialize Original engine (always, as fallback)
            OptimizedGpuEngine = new OptimizedGpuSimulationEngine(graph);
            OptimizedGpuEngine.Initialize();
            OptimizedGpuEngine.UploadState();

            // Dispose CSR unified if switching away
            CsrUnifiedEngine?.Dispose();
            CsrUnifiedEngine = null;
            ResetCsrUnifiedSyncState();

            // Initialize CSR engine(s) if selected
            if (actualType == GpuEngineType.Csr && GpuCayleyEngineFactory.IsGpuSupported())
            {
                try
                {
                    // Build dense edges and weights for InitializeFromDense
                    bool[,] edges = new bool[graph.N, graph.N];
                    for (int i = 0; i < graph.N; i++)
                    {
                        foreach (int j in graph.Neighbors(i))
                        {
                            edges[i, j] = true;
                        }
                    }
                    
                    CsrCayleyEngine = new GpuCayleyEvolutionEngineCsr();
                    CsrCayleyEngine.InitializeFromDense(
                        edges,
                        graph.Weights,
                        graph.LocalPotential,
                        gaugeDim: 1);
                    
                    // Apply pending topology mode and dynamic config
                    ApplyPendingTopologySettings();
                    
                    OnConsoleLog?.Invoke($"[GPU] CSR Cayley engine initialized (double precision: {CsrCayleyEngine.IsDoublePrecision})\n");
                }
                catch (Exception csrEx)
                {
                    OnConsoleLog?.Invoke($"[GPU] CSR engine failed: {csrEx.Message}, using Original\n");
                    CsrCayleyEngine?.Dispose();
                    CsrCayleyEngine = null;
                    CsrUnifiedEngine?.Dispose();
                    CsrUnifiedEngine = null;
                    ActiveEngineType = GpuEngineType.Original;
                }

                // Stage 6: CSR Unified Engine
                graph.EnsureCorrelationMassComputed();
                CsrUnifiedEngine = new CsrUnifiedEngine();
                CsrUnifiedEngine.Initialize(graph);
                ResetCsrUnifiedSyncState();
                OnConsoleLog?.Invoke("[GPU] CSR Unified engine initialized (Stage 6)\n");
            }

            // Also initialize the standalone GpuGravityEngine for ImprovedNetworkGravity
            // This enables GPU-accelerated gravity in CPU simulation paths too
            bool gravityGpuOk = graph.InitGpuGravity();
            if (gravityGpuOk)
            {
                OnConsoleLog?.Invoke("[GPU] GpuGravityEngine initialized for Ollivier/Forman curvature\n");
            }

            int totalDirectedEdges = graph.CsrOffsets[graph.N];
            float[] csrWeights = new float[totalDirectedEdges];
            for (int n = 0; n < graph.N; n++)
            {
                int start = graph.CsrOffsets[n];
                int end = graph.CsrOffsets[n + 1];
                for (int k = start; k < end; k++)
                {
                    int to = graph.CsrIndices[k];
                    csrWeights[k] = (float)graph.Weights[n, to];
                }
            }

            GpuSpectralWalkEngine = new SpectralWalkEngine();
            int walkerCount = Math.Clamp(graph.N * 100, 50000, 200000);
            GpuSpectralWalkEngine.Initialize(walkerCount, graph.N, totalDirectedEdges);
            GpuSpectralWalkEngine.UpdateTopology(graph.CsrOffsets, graph.CsrIndices, csrWeights);
            GpuSpectralWalkEngine.InitializeWalkersUniform();

            // Initialize Heat Kernel GPU engine (alternative d_S computation method)
            // Heat Kernel is better for dense graphs, Random Walk for sparse
            GpuHeatKernelEngine = new GpuSpectralEngine();
            GpuHeatKernelEngine.UpdateTopology(graph);

            GpuStatisticsEngine = new StatisticsEngine();
            GpuStatisticsEngine.Initialize(Math.Max(graph.N, edgeCount));

            GpuStats.IsActive = true;
            GpuStats.DeviceName = GpuCayleyEngineFactory.GetGpuInfo();

            return true;
        }
        catch (Exception ex)
        {
            OnConsoleLog?.Invoke($"[GPU] Error: {ex.Message}, using CPU fallback\n");
            DisposeGpuEngines();
            return false;
        }
    }

    /// <summary>
    /// Disposes GPU engines
    /// </summary>
    public void DisposeGpuEngines()
    {
        GpuSpectralWalkEngine?.Dispose();
        GpuSpectralWalkEngine = null;
        GpuHeatKernelEngine?.Dispose();
        GpuHeatKernelEngine = null;
        GpuStatisticsEngine?.Dispose();
        GpuStatisticsEngine = null;
        OptimizedGpuEngine?.Dispose();
        OptimizedGpuEngine = null;

        // Dispose CSR Cayley engine
        CsrCayleyEngine?.Dispose();
        CsrCayleyEngine = null;

        // Dispose CSR Unified engine
        CsrUnifiedEngine?.Dispose();
        CsrUnifiedEngine = null;

        // Also dispose standalone gravity engine
        SimulationEngine?.Graph?.DisposeGpuGravity();

        GpuStats.IsActive = false;
    }

    /// <summary>
    /// Compute spectral dimension using the best available GPU method.
    /// 
    /// Method selection:
    /// - Heat Kernel (GpuHeatKernelEngine): Better for dense graphs (density > 10%)
    /// - Random Walk (GpuSpectralWalkEngine): Better for sparse graphs
    /// 
    /// Both methods automatically sync topology when graph.TopologyVersion changes.
    /// </summary>
    /// <param name="graph">The RQGraph to compute d_S for</param>
    /// <param name="enableCpuComparison">If true, also runs CPU version for debugging</param>
    /// <returns>Spectral dimension estimate, or NaN if computation failed</returns>
    public double ComputeSpectralDimensionGpu(RQGraph graph, bool enableCpuComparison = false)
    {
        ArgumentNullException.ThrowIfNull(graph);

        // Compute graph density to choose method
        int edgeCount = 0;
        for (int i = 0; i < graph.N; i++)
        {
            foreach (int j in graph.Neighbors(i))
            {
                if (j > i) edgeCount++;
            }
        }
        int maxEdges = graph.N * (graph.N - 1) / 2;
        double density = maxEdges > 0 ? (double)edgeCount / maxEdges : 0;

        // Choose method based on density
        if (density > 0.10 && GpuHeatKernelEngine != null)
        {
            // Dense graph: Heat Kernel is more stable
            double ds = GpuHeatKernelEngine.ComputeSpectralDimension(
                graph,
                dt: 0.01,
                numSteps: 100,
                numProbeVectors: 8,
                enableCpuComparison: enableCpuComparison);

            if (!double.IsNaN(ds))
            {
                OnConsoleLog?.Invoke($"[GPU d_S] HeatKernel method: d_S={ds:F4} (density={density:P1})\n");
                return ds;
            }
        }

        // Sparse graph or HeatKernel fallback: Random Walk
        if (GpuSpectralWalkEngine != null)
        {
            double ds = GpuSpectralWalkEngine.ComputeSpectralDimensionWithSyncCheck(
                graph,
                numSteps: 100,
                walkerCount: 10000,
                skipInitial: 10);

            if (!double.IsNaN(ds))
            {
                OnConsoleLog?.Invoke($"[GPU d_S] RandomWalk method: d_S={ds:F4} (density={density:P1})\n");
                return ds;
            }
        }

        // CPU fallback
        OnConsoleLog?.Invoke($"[GPU d_S] GPU methods failed, using CPU fallback\n");
        return graph.ComputeSpectralDimension(t_max: 100, num_walkers: 50);
    }

    /// <summary>
    /// Runs one physics step with GPU or CPU.
    /// Selects between CSR engine (for large sparse graphs) and Original engine.
    /// </summary>
    public void RunPhysicsStep(int step, double dt, double effectiveG, bool useGpu)
    {
        if (SimulationEngine?.Graph == null) return;
        var graph = SimulationEngine.Graph;

        if (effectiveG > 0)
        {
            // Determine which GPU engine to use
            bool useCsr = useGpu &&
                          ActiveEngineType == GpuEngineType.Csr &&
                          CsrCayleyEngine != null &&
                          CsrCayleyEngine.IsInitialized;

            bool useOriginal = useGpu &&
                               !useCsr &&
                               OptimizedGpuEngine != null &&
                               ActiveEngineType != GpuEngineType.CpuOnly;

            if (useCsr)
            {
                // CSR Engine path - optimized for large sparse graphs
                if (step == 0)
                {
                    OnConsoleLog?.Invoke($"[GPU CSR] Starting Cayley evolution\n");
                }

                // Run CSR Cayley step
                int iterations = CsrCayleyEngine!.EvolveStep(dt);
                GpuStats.RecordKernelLaunch(iterations);

                // Also run gravity on Original engine for weight updates
                if (OptimizedGpuEngine != null)
                {
                    if (step == 0) OptimizedGpuEngine.UploadState();

                    OptimizedGpuEngine.StepGpu(
                        dt: (float)dt,
                        G: (float)effectiveG,
                        lambda: (float)PhysicsConstants.CosmologicalConstant,
                        diffusionRate: (float)PhysicsConstants.FieldDiffusionRate,
                        higgsLambda: (float)PhysicsConstants.HiggsLambda,
                        higgsMuSquared: (float)PhysicsConstants.HiggsMuSquared);

                    GpuStats.RecordKernelLaunch(4);
                }

                // Sync less frequently
                if (step % 50 == 0)
                {
                    OptimizedGpuEngine?.SyncAllStatesToGraph();

                    // Update CSR potential periodically
                    if (step % 200 == 0)
                    {
                        CsrCayleyEngine.UpdatePotential(graph.LocalPotential ?? new double[graph.N]);
                    }

                    // Optionally sync wavefunction to graph for visualization
                    // This allows 3D visualization to show quantum probability density
                    SyncCsrWavefunctionToGraph(graph);

                    GpuStats.RecordWeightSync();
                }
            }
            else if (useOriginal)
            {
                // Original engine path
                if (step == 0) OptimizedGpuEngine!.UploadState();

                OptimizedGpuEngine!.StepGpu(
                    dt: (float)dt,
                    G: (float)effectiveG,
                    lambda: (float)PhysicsConstants.CosmologicalConstant,
                    diffusionRate: (float)PhysicsConstants.FieldDiffusionRate,
                    higgsLambda: (float)PhysicsConstants.HiggsLambda,
                    higgsMuSquared: (float)PhysicsConstants.HiggsMuSquared);

                GpuStats.RecordKernelLaunch(4);

                if (step % 50 == 0)
                {
                    OptimizedGpuEngine.SyncAllStatesToGraph();
                    GpuStats.RecordWeightSync();
                }
            }
            else
            {
                // CPU fallback
                ImprovedNetworkGravity.EvolveNetworkGeometryOllivierDynamic(
                    graph, dt: dt, effectiveG: effectiveG);
            }
        }
    }

    /// <summary>
    /// Updates topology and syncs GPU buffers if needed
    /// </summary>
    public void UpdateTopology(bool useGpu)
    {
        if (SimulationEngine?.Graph == null) return;
        var graph = SimulationEngine.Graph;

        graph.Step();

        if (useGpu && OptimizedGpuEngine != null)
        {
            graph.BuildSoAViews();
            OptimizedGpuEngine.UpdateTopologyBuffers();
            OptimizedGpuEngine.UploadState();
            GpuStats.RecordTopologyRebuild();
        }

        // Update CSR topology if using CSR engine
        if (useGpu && CsrCayleyEngine != null && CsrCayleyEngine.IsInitialized)
        {
            // CSR engine needs topology update after graph structure changes
            // This is expensive, so only do it when topology actually changed
            CsrCayleyEngine.UpdatePotential(graph.LocalPotential ?? new double[graph.N]);
        }
    }

    /// <summary>
    /// Sync wavefunction from CSR engine to graph for visualization.
    /// Updates graph.QuantumProbability with |?|? values.
    /// </summary>
    private void SyncCsrWavefunctionToGraph(RQGraph graph)
    {
        if (CsrCayleyEngine == null || !CsrCayleyEngine.IsInitialized)
            return;

        try
        {
            // Download wavefunction from GPU
            double[] psiReal = new double[graph.N];
            double[] psiImag = new double[graph.N];
            CsrCayleyEngine.DownloadWavefunction(psiReal, psiImag);

            // Compute probability density |?|? and store in graph
            // This can be used for visualization
            if (graph.QuantumProbability == null || graph.QuantumProbability.Length != graph.N)
            {
                graph.QuantumProbability = new double[graph.N];
            }

            for (int i = 0; i < graph.N; i++)
            {
                graph.QuantumProbability[i] = psiReal[i] * psiReal[i] + psiImag[i] * psiImag[i];
            }
        }
        catch
        {
            // Silently ignore download errors during visualization sync
        }
    }
}
