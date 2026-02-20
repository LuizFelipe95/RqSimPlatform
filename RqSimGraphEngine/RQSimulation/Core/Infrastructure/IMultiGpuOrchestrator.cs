using System;
using System.Threading;
using System.Threading.Tasks;

namespace RQSimulation.Core.Infrastructure;

/// <summary>
/// Interface for multi-GPU orchestration in RqSimPlatform.
/// 
/// Defines the contract for integration with UI/Console applications
/// that need to coordinate physics simulation with analysis workers.
/// </summary>
public interface IMultiGpuOrchestrator : IDisposable
{
    /// <summary>
    /// Whether the orchestrator is initialized and ready.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Whether multi-GPU mode is active.
    /// </summary>
    bool IsMultiGpuActive { get; }

    /// <summary>
    /// Number of spectral dimension worker GPUs.
    /// </summary>
    int SpectralWorkerCount { get; }

    /// <summary>
    /// Number of MCMC worker GPUs.
    /// </summary>
    int McmcWorkerCount { get; }

    /// <summary>
    /// Initialize the orchestrator with compute cluster.
    /// </summary>
    /// <param name="maxNodeCount">Maximum graph size for buffer pre-allocation</param>
    void Initialize(int maxNodeCount = 100_000);

    /// <summary>
    /// Notify orchestrator that a physics step completed.
    /// </summary>
    /// <param name="snapshot">Current graph state snapshot</param>
    void OnPhysicsStepCompleted(GraphSnapshot snapshot);

    /// <summary>
    /// Get latest spectral dimension result.
    /// </summary>
    /// <returns>Latest result or null if none available</returns>
    Scheduler.SpectralResult? GetLatestSpectralResult();

    /// <summary>
    /// Get cluster status summary.
    /// </summary>
    Scheduler.ClusterStatus GetStatus();

    /// <summary>
    /// Wait for all workers to complete.
    /// </summary>
    Task WaitForCompletionAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel all running tasks.
    /// </summary>
    void CancelAll();

    /// <summary>
    /// Clear collected results.
    /// </summary>
    void ClearResults();
}

/// <summary>
/// Extension methods for multi-GPU integration.
/// </summary>
public static class MultiGpuExtensions
{
    /// <summary>
    /// Create and initialize a multi-GPU orchestrator.
    /// </summary>
    /// <param name="cluster">Compute cluster to use</param>
    /// <param name="maxNodeCount">Maximum graph size</param>
    /// <returns>Initialized orchestrator</returns>
    public static Scheduler.AsyncAnalysisOrchestrator CreateOrchestrator(
        this ComputeCluster cluster,
        int maxNodeCount = 100_000)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        if (!cluster.IsInitialized)
        {
            throw new InvalidOperationException("Cluster must be initialized before creating orchestrator");
        }

        var orchestrator = new Scheduler.AsyncAnalysisOrchestrator(cluster);
        orchestrator.Initialize(maxNodeCount);
        return orchestrator;
    }

    /// <summary>
    /// Check if snapshot should be dispatched based on tick interval.
    /// </summary>
    /// <param name="tickId">Current simulation tick</param>
    /// <param name="interval">Dispatch interval (default: every 100 ticks)</param>
    /// <returns>True if snapshot should be dispatched</returns>
    public static bool ShouldDispatch(long tickId, int interval = 100)
    {
        return tickId % interval == 0;
    }
}

/// <summary>
/// Factory for creating multi-GPU infrastructure.
/// </summary>
public static class MultiGpuFactory
{
    /// <summary>
    /// Create a compute cluster with automatic device detection.
    /// </summary>
    /// <param name="preferMultiGpu">If true, prefers multi-GPU mode when available</param>
    /// <returns>Initialized compute cluster</returns>
    public static ComputeCluster CreateCluster(bool preferMultiGpu = true)
    {
        var cluster = new ComputeCluster();

        try
        {
            if (preferMultiGpu)
            {
                cluster.Initialize();

                if (!cluster.IsMultiGpuAvailable)
                {
                    Console.WriteLine("[MultiGpuFactory] Multi-GPU not available, falling back to single GPU");
                }
            }
            else
            {
                cluster.InitializeSingleGpu();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MultiGpuFactory] Multi-GPU init failed: {ex.Message}. Falling back to single GPU.");
            cluster.InitializeSingleGpu();
        }

        return cluster;
    }

    /// <summary>
    /// Create complete multi-GPU pipeline.
    /// </summary>
    /// <param name="maxNodeCount">Maximum graph size</param>
    /// <returns>Tuple of (cluster, orchestrator) ready for use</returns>
    public static (ComputeCluster Cluster, Scheduler.AsyncAnalysisOrchestrator? Orchestrator) CreatePipeline(
        int maxNodeCount = 100_000)
    {
        var cluster = CreateCluster(preferMultiGpu: true);

        Scheduler.AsyncAnalysisOrchestrator? orchestrator = null;

        if (cluster.IsMultiGpuAvailable)
        {
            orchestrator = new Scheduler.AsyncAnalysisOrchestrator(cluster);
            orchestrator.Initialize(maxNodeCount);
        }

        return (cluster, orchestrator);
    }
}
