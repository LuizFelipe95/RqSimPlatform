using System;
using System.Diagnostics;
using ComputeSharp;

namespace RQSimulation.GPUOptimized;

/// <summary>
/// Extension methods for seamless GPU gravity integration with RQGraph.
/// 
/// Provides high-level API for:
/// - Automatic GPU engine initialization and lifecycle
/// - Topology change detection and buffer updates
/// - Efficient batch processing with minimal CPU-GPU transfers
/// 
/// Usage:
///   graph.InitGpuGravity();
///   for each step:
///     graph.EvolveGravityGpu(dt, G, lambda);  // or batch mode
///   graph.DisposeGpuGravity();
/// </summary>
public static class GpuGravityExtensions
{
    // Track topology version per graph to detect changes
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<RQGraph, int> _lastTopologyVersion = new();

    /// <summary>
    /// Initialize GPU gravity engine for this graph.
    /// Call once before starting GPU-accelerated simulation.
    /// </summary>
    /// <param name="graph">The RQGraph to accelerate</param>
    /// <returns>True if GPU is available and initialized, false for CPU fallback</returns>
    public static bool InitGpuGravity(this RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        try
        {
            // Check if GPU is available
            var device = GraphicsDevice.GetDefault();
            if (device == null)
            {
                //Console.WriteLine("[GPU] No GPU device available, using CPU fallback");
                return false;
            }

            // Get edge count from graph
            graph.BuildSoAViews();
            int edgeCount = graph.FlatEdgesFrom?.Length ?? 0;
            if (edgeCount == 0)
            {
                //Console.WriteLine("[GPU] Graph has no edges, skipping GPU init");
                return false;
            }

            // Create engine
            var config = new GpuConfig { GpuIndex = 0, MultiGpu = false, ThreadBlockSize = 64 };
            graph.GpuGravity = new GpuGravityEngine(config, edgeCount, graph.N);

            // Initialize topology buffers
            graph.GpuGravity.UpdateTopologyBuffers(graph);

            // Upload initial state
            var (weights, masses, edgesFrom, edgesTo) = PrepareGpuData(graph);
            graph.GpuGravity.UploadInitialData(weights, masses, edgesFrom, edgesTo);

            // Track topology version
            _lastTopologyVersion[graph] = graph.TopologyVersion;

            //Console.WriteLine($"[GPU] Gravity engine initialized: {graph.N} nodes, {edgeCount} edges");
            return true;
        }
        catch (Exception ex)
        {
            //Console.WriteLine($"[GPU] Failed to initialize: {ex.Message}, using CPU fallback");
            graph.GpuGravity = null;
            return false;
        }
    }

    /// <summary>
    /// Release GPU gravity resources for this graph.
    /// </summary>
    public static void DisposeGpuGravity(this RQGraph graph)
    {
        if (graph?.GpuGravity != null)
        {
            graph.GpuGravity.Dispose();
            graph.GpuGravity = null;
            _lastTopologyVersion.TryRemove(graph, out _);
            //Console.WriteLine("[GPU] Gravity engine disposed");
        }
    }

    /// <summary>
    /// Check if GPU gravity is active for this graph.
    /// </summary>
    public static bool IsGpuGravityActive(this RQGraph graph)
    {
        return graph?.GpuGravity != null && graph.GpuGravity.IsTopologyInitialized;
    }

    /// <summary>
    /// Evolve network geometry using GPU if available, CPU fallback otherwise.
    /// 
    /// This method:
    /// 1. Detects topology changes and updates GPU buffers if needed
    /// 2. Computes Forman-Ricci curvature on GPU
    /// 3. Evolves gravity (weight updates) on GPU
    /// 4. Syncs results back to CPU graph
    /// 
    /// For maximum performance with multiple steps, use EvolveGravityGpuBatch instead.
    /// </summary>
    /// <param name="graph">The RQGraph to evolve</param>
    /// <param name="dt">Time step</param>
    /// <param name="G">Gravitational coupling constant</param>
    /// <param name="lambda">Cosmological constant</param>
    /// <returns>True if GPU was used, false if CPU fallback</returns>
    public static bool EvolveGravityGpu(
        this RQGraph graph, 
        double dt, 
        double G, 
        double lambda)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.GpuGravity == null)
            return false;

        try
        {
            // Check for topology changes
            if (NeedsTopologyUpdate(graph))
            {
                RebuildTopologyBuffers(graph);
            }

            // Prepare data
            var (weights, masses, edgesFrom, edgesTo) = PrepareGpuData(graph);

            // Run GPU computation
            graph.GpuGravity.EvolveFullGpuStep(
                weights, masses, edgesFrom, edgesTo,
                (float)dt, (float)G, (float)lambda);

            // Copy results back to graph
            ApplyWeightsToGraph(graph, weights);

            return true;
        }
        catch (Exception ex)
        {
            //Console.WriteLine($"[GPU] EvolveGravityGpu failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Evolve network geometry for multiple steps on GPU without intermediate CPU sync.
    /// 
    /// This is the most efficient mode for running many gravity steps:
    /// - Single upload at start
    /// - All computation on GPU
    /// - Single download at end
    /// 
    /// Typical speedup: 10-50x for batchSize >= 10 compared to per-step sync.
    /// </summary>
    /// <param name="graph">The RQGraph to evolve</param>
    /// <param name="batchSize">Number of steps to run on GPU</param>
    /// <param name="dt">Time step</param>
    /// <param name="G">Gravitational coupling constant</param>
    /// <param name="lambda">Cosmological constant</param>
    /// <returns>True if GPU was used, false if CPU fallback</returns>
    public static bool EvolveGravityGpuBatch(
        this RQGraph graph,
        int batchSize,
        double dt,
        double G,
        double lambda)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.GpuGravity == null || batchSize <= 0)
            return false;

        try
        {
            // Check for topology changes
            if (NeedsTopologyUpdate(graph))
            {
                RebuildTopologyBuffers(graph);
            }

            // Upload current state
            var (weights, masses, edgesFrom, edgesTo) = PrepareGpuData(graph);
            graph.GpuGravity.UploadInitialData(weights, masses, edgesFrom, edgesTo);

            // Run batch on GPU (no intermediate sync)
            for (int i = 0; i < batchSize; i++)
            {
                graph.GpuGravity.EvolveFullGpuStep_NoCopy(
                    (float)dt, (float)G, (float)lambda);
            }

            // Sync final results back
            graph.GpuGravity.SyncToHost(weights);
            ApplyWeightsToGraph(graph, weights);

            return true;
        }
        catch (Exception ex)
        {
            //Console.WriteLine($"[GPU] EvolveGravityGpuBatch failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Hybrid mode: Run gravity step with CPU-computed Ollivier-Ricci curvature
    /// and GPU-accelerated weight updates.
    /// 
    /// Use when Ollivier-Ricci is preferred over Forman-Ricci (more geometric,
    /// but more expensive to compute on GPU due to optimal transport).
    /// </summary>
    public static bool EvolveGravityHybrid(
        this RQGraph graph,
        double dt,
        double G,
        double lambda,
        double[] cpuCurvatures)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(cpuCurvatures);

        if (graph.GpuGravity == null)
            return false;

        try
        {
            var (weights, masses, edgesFrom, edgesTo) = PrepareGpuData(graph);

            // Convert curvatures to float
            float[] floatCurvatures = new float[cpuCurvatures.Length];
            for (int i = 0; i < cpuCurvatures.Length; i++)
                floatCurvatures[i] = (float)cpuCurvatures[i];

            graph.GpuGravity.EvolveGravityGpu(
                weights, floatCurvatures, masses, edgesFrom, edgesTo,
                (float)dt, (float)G, (float)lambda);

            ApplyWeightsToGraph(graph, weights);

            return true;
        }
        catch (Exception ex)
        {
            //Console.WriteLine($"[GPU] EvolveGravityHybrid failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if topology buffers need updating.
    /// </summary>
    private static bool NeedsTopologyUpdate(RQGraph graph)
    {
        if (!_lastTopologyVersion.TryGetValue(graph, out int lastVersion))
            return true;

        return graph.TopologyVersion != lastVersion;
    }

    /// <summary>
    /// Rebuild GPU topology buffers after graph structure change.
    /// </summary>
    private static void RebuildTopologyBuffers(RQGraph graph)
    {
        if (graph.GpuGravity == null) return;

        graph.BuildSoAViews();
        graph.GpuGravity.UpdateTopologyBuffers(graph);
        _lastTopologyVersion[graph] = graph.TopologyVersion;

        //Console.WriteLine($"[GPU] Topology buffers updated: version {graph.TopologyVersion}");
    }

    /// <summary>
    /// Prepare flat arrays for GPU transfer.
    /// </summary>
    private static (float[] weights, float[] masses, int[] edgesFrom, int[] edgesTo) PrepareGpuData(RQGraph graph)
    {
        int edgeCount = graph.FlatEdgesFrom.Length;
        int nodeCount = graph.N;

        float[] weights = new float[edgeCount];
        float[] masses = new float[nodeCount];

        // Get correlation masses
        var correlationMass = graph.ComputePerNodeCorrelationMass();

        for (int n = 0; n < nodeCount; n++)
        {
            masses[n] = (float)correlationMass[n];
        }

        for (int e = 0; e < edgeCount; e++)
        {
            int i = graph.FlatEdgesFrom[e];
            int j = graph.FlatEdgesTo[e];
            weights[e] = (float)graph.Weights[i, j];
        }

        return (weights, masses, graph.FlatEdgesFrom, graph.FlatEdgesTo);
    }

    /// <summary>
    /// Apply GPU-computed weights back to graph.
    /// </summary>
    private static void ApplyWeightsToGraph(RQGraph graph, float[] weights)
    {
        for (int e = 0; e < weights.Length; e++)
        {
            int i = graph.FlatEdgesFrom[e];
            int j = graph.FlatEdgesTo[e];
            double w = Math.Clamp(weights[e], 0.0, 1.0);
            graph.Weights[i, j] = w;
            graph.Weights[j, i] = w;
        }
    }
}

/// <summary>
/// Performance tracker for GPU vs CPU comparison.
/// </summary>
public class GpuPerformanceTracker
{
    private readonly Stopwatch _gpuTimer = new();
    private readonly Stopwatch _cpuTimer = new();

    private long _gpuTotalTicks;
    private long _cpuTotalTicks;
    private int _gpuSteps;
    private int _cpuSteps;

    public void StartGpu() => _gpuTimer.Restart();
    public void StopGpu()
    {
        _gpuTimer.Stop();
        _gpuTotalTicks += _gpuTimer.ElapsedTicks;
        _gpuSteps++;
    }

    public void StartCpu() => _cpuTimer.Restart();
    public void StopCpu()
    {
        _cpuTimer.Stop();
        _cpuTotalTicks += _cpuTimer.ElapsedTicks;
        _cpuSteps++;
    }

    public double GpuAverageMs => _gpuSteps > 0
        ? (_gpuTotalTicks * 1000.0 / Stopwatch.Frequency) / _gpuSteps
        : 0;

    public double CpuAverageMs => _cpuSteps > 0
        ? (_cpuTotalTicks * 1000.0 / Stopwatch.Frequency) / _cpuSteps
        : 0;

    public double Speedup => CpuAverageMs > 0 && GpuAverageMs > 0
        ? CpuAverageMs / GpuAverageMs
        : 0;

    public string GetSummary()
    {
        return $"GPU: {GpuAverageMs:F3}ms avg ({_gpuSteps} steps), " +
               $"CPU: {CpuAverageMs:F3}ms avg ({_cpuSteps} steps), " +
               $"Speedup: {Speedup:F1}x";
    }

    public void Reset()
    {
        _gpuTotalTicks = 0;
        _cpuTotalTicks = 0;
        _gpuSteps = 0;
        _cpuSteps = 0;
    }
}
