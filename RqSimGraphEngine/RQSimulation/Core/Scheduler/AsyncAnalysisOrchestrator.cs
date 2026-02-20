using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputeSharp;
using RQSimulation.Core.Infrastructure;

namespace RQSimulation.Core.Scheduler;

/// <summary>
/// Orchestrator for asynchronous analysis tasks on multi-GPU cluster.
/// 
/// PURPOSE:
/// ========
/// Distributes GraphSnapshots to worker GPUs without blocking the physics
/// simulation on GPU 0. Workers process tasks asynchronously and report
/// results via events.
/// 
/// ARCHITECTURE:
/// =============
/// - Physics loop (GPU 0) calls OnPhysicsStepCompleted() with snapshot
/// - Orchestrator finds free workers and dispatches tasks
/// - Fire-and-forget pattern: physics continues immediately
/// - Results collected via events or polling
/// 
/// PARALLEL TEMPERING:
/// ===================
/// MCMC workers run at different temperatures for efficient vacuum search.
/// Temperature ladder is configured during initialization.
/// 
/// THREAD SAFETY:
/// ==============
/// - Snapshot queue is thread-safe (ConcurrentQueue)
/// - Worker dispatch is synchronized
/// - Results are reported via thread-safe collections
/// </summary>
public sealed class AsyncAnalysisOrchestrator : IDisposable
{
    private readonly ComputeCluster _cluster;
    private readonly List<SpectralWorker> _spectralWorkers = [];
    private readonly List<McmcWorker> _mcmcWorkers = [];
    private readonly ConcurrentQueue<GraphSnapshot> _pendingSnapshots = new();
    private readonly ConcurrentBag<SpectralResult> _spectralResults = new();
    private readonly ConcurrentBag<McmcResult> _mcmcResults = new();
    private readonly object _dispatchLock = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    private bool _initialized;

    /// <summary>
    /// Number of spectral worker GPUs.
    /// </summary>
    public int SpectralWorkerCount => _spectralWorkers.Count;

    /// <summary>
    /// Number of MCMC worker GPUs.
    /// </summary>
    public int McmcWorkerCount => _mcmcWorkers.Count;

    /// <summary>
    /// Number of pending snapshots in queue.
    /// </summary>
    public int PendingSnapshotCount => _pendingSnapshots.Count;

    /// <summary>
    /// Number of busy spectral workers.
    /// </summary>
    public int BusySpectralWorkers => _spectralWorkers.Count(w => w.IsBusy);

    /// <summary>
    /// Number of busy MCMC workers.
    /// </summary>
    public int BusyMcmcWorkers => _mcmcWorkers.Count(w => w.IsBusy);

    /// <summary>
    /// Configuration for spectral dimension computation.
    /// </summary>
    public SpectralComputeConfig SpectralConfig { get; set; } = SpectralComputeConfig.Default;

    /// <summary>
    /// Configuration for MCMC chain runs.
    /// </summary>
    public McmcChainConfig McmcChainConfig { get; set; } = McmcChainConfig.Default;

    /// <summary>
    /// Event raised when spectral dimension computation completes.
    /// </summary>
    public event EventHandler<SpectralResultEventArgs>? SpectralCompleted;

    /// <summary>
    /// Event raised when MCMC chain run completes.
    /// </summary>
    public event EventHandler<McmcResultEventArgs>? McmcCompleted;

    /// <summary>
    /// Create orchestrator for the given compute cluster.
    /// </summary>
    /// <param name="cluster">Initialized compute cluster</param>
    /// <exception cref="ArgumentNullException">Cluster is null</exception>
    /// <exception cref="InvalidOperationException">Cluster not initialized</exception>
    public AsyncAnalysisOrchestrator(ComputeCluster cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        if (!cluster.IsInitialized)
        {
            throw new InvalidOperationException("ComputeCluster must be initialized before creating orchestrator");
        }

        _cluster = cluster;
    }

    /// <summary>
    /// Initialize all worker GPUs.
    /// </summary>
    /// <param name="maxNodeCount">Maximum graph size for buffer pre-allocation</param>
    public void Initialize(int maxNodeCount = 100_000)
    {
        if (_initialized)
            return;

        InitializeSpectralWorkers(maxNodeCount);
        InitializeMcmcWorkers(maxNodeCount);

        _initialized = true;

        Console.WriteLine($"[AsyncAnalysisOrchestrator] Initialized: {_spectralWorkers.Count} spectral, {_mcmcWorkers.Count} MCMC workers");
    }

    private void InitializeSpectralWorkers(int maxNodeCount)
    {
        for (int i = 0; i < _cluster.SpectralWorkers.Length; i++)
        {
            var device = _cluster.SpectralWorkers[i];
            var worker = new SpectralWorker(device, i, maxNodeCount);

            worker.ComputationCompleted += OnSpectralCompleted;

            _spectralWorkers.Add(worker);
        }
    }

    private void InitializeMcmcWorkers(int maxNodeCount)
    {
        // Set up temperature ladder for Parallel Tempering
        // Higher index = higher temperature = more exploration
        double[] betas = GenerateTemperatureLadder(_cluster.McmcWorkers.Length);

        for (int i = 0; i < _cluster.McmcWorkers.Length; i++)
        {
            var device = _cluster.McmcWorkers[i];
            var worker = new McmcWorker(device, i, seed: 42 + i * 1000);

            // Assign temperature from ladder
            worker.Beta = betas[i];

            worker.ChainCompleted += OnMcmcCompleted;

            _mcmcWorkers.Add(worker);
        }
    }

    /// <summary>
    /// Generate temperature ladder for Parallel Tempering.
    /// Uses geometric spacing: T_i = T_min * (T_max/T_min)^(i/(n-1))
    /// </summary>
    private static double[] GenerateTemperatureLadder(int count, double tMin = 0.5, double tMax = 10.0)
    {
        if (count <= 0)
            return [];

        if (count == 1)
            return [1.0 / tMin]; // Return beta = 1/T

        double[] betas = new double[count];
        double ratio = Math.Pow(tMax / tMin, 1.0 / (count - 1));

        for (int i = 0; i < count; i++)
        {
            double t = tMin * Math.Pow(ratio, i);
            betas[i] = 1.0 / t; // beta = 1/T
        }

        return betas;
    }

    /// <summary>
    /// Called from physics loop when a step completes.
    /// Dispatches snapshot to available workers (fire-and-forget).
    /// </summary>
    /// <param name="snapshot">Current graph state snapshot</param>
    public void OnPhysicsStepCompleted(GraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!_initialized)
        {
            Console.WriteLine("[AsyncAnalysisOrchestrator] Warning: Not initialized, ignoring snapshot");
            return;
        }

        // Try to dispatch immediately to free workers
        DispatchToFreeWorkers(snapshot);
    }

    /// <summary>
    /// Dispatch snapshot to all free workers.
    /// </summary>
    private void DispatchToFreeWorkers(GraphSnapshot snapshot)
    {
        lock (_dispatchLock)
        {
            // Find free spectral worker
            var freeSpectralWorker = _spectralWorkers.FirstOrDefault(w => !w.IsBusy);
            if (freeSpectralWorker != null)
            {
                // Fire-and-forget: don't await, physics continues
                _ = DispatchSpectralAsync(freeSpectralWorker, snapshot);
            }

            // Find free MCMC worker
            var freeMcmcWorker = _mcmcWorkers.FirstOrDefault(w => !w.IsBusy);
            if (freeMcmcWorker != null)
            {
                // Fire-and-forget
                _ = DispatchMcmcAsync(freeMcmcWorker, snapshot);
            }
        }
    }

    private async Task DispatchSpectralAsync(SpectralWorker worker, GraphSnapshot snapshot)
    {
        try
        {
            await worker.ProcessAsync(snapshot, SpectralConfig, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AsyncAnalysisOrchestrator] Spectral worker {worker.WorkerId} error: {ex.Message}");
        }
    }

    private async Task DispatchMcmcAsync(McmcWorker worker, GraphSnapshot snapshot)
    {
        try
        {
            await worker.RunChainAsync(snapshot, McmcChainConfig, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AsyncAnalysisOrchestrator] MCMC worker {worker.WorkerId} error: {ex.Message}");
        }
    }

    private void OnSpectralCompleted(object? sender, SpectralResultEventArgs e)
    {
        _spectralResults.Add(e.Result);
        SpectralCompleted?.Invoke(this, e);
    }

    private void OnMcmcCompleted(object? sender, McmcResultEventArgs e)
    {
        _mcmcResults.Add(e.Result);
        McmcCompleted?.Invoke(this, e);
    }

    /// <summary>
    /// Get all collected spectral dimension results.
    /// </summary>
    public IReadOnlyList<SpectralResult> GetSpectralResults()
    {
        return _spectralResults.ToArray();
    }

    /// <summary>
    /// Get all collected MCMC results.
    /// </summary>
    public IReadOnlyList<McmcResult> GetMcmcResults()
    {
        return _mcmcResults.ToArray();
    }

    /// <summary>
    /// Get latest spectral dimension result.
    /// </summary>
    public SpectralResult? GetLatestSpectralResult()
    {
        return _spectralResults
            .OrderByDescending(r => r.TickId)
            .FirstOrDefault();
    }

    /// <summary>
    /// Get latest MCMC results from all workers (for Parallel Tempering analysis).
    /// </summary>
    public IReadOnlyList<McmcResult> GetLatestMcmcResults()
    {
        var latestTick = _mcmcResults.Max(r => (long?)r.TickId) ?? -1;

        return _mcmcResults
            .Where(r => r.TickId == latestTick)
            .OrderBy(r => r.WorkerId)
            .ToArray();
    }

    /// <summary>
    /// Clear all collected results.
    /// </summary>
    public void ClearResults()
    {
        while (_spectralResults.TryTake(out _)) { }
        while (_mcmcResults.TryTake(out _)) { }
    }

    /// <summary>
    /// Get cluster status summary.
    /// </summary>
    public ClusterStatus GetStatus()
    {
        return new ClusterStatus
        {
            SpectralWorkerCount = _spectralWorkers.Count,
            McmcWorkerCount = _mcmcWorkers.Count,
            BusySpectralWorkers = BusySpectralWorkers,
            BusyMcmcWorkers = BusyMcmcWorkers,
            PendingSnapshots = PendingSnapshotCount,
            TotalSpectralResults = _spectralResults.Count,
            TotalMcmcResults = _mcmcResults.Count,
            SpectralWorkerStatus = _spectralWorkers.Select(w => new WorkerStatus
            {
                WorkerId = w.WorkerId,
                DeviceName = w.DeviceName,
                IsBusy = w.IsBusy,
                LastResultTick = w.LastResult?.TickId ?? -1
            }).ToArray(),
            McmcWorkerStatus = _mcmcWorkers.Select(w => new WorkerStatus
            {
                WorkerId = w.WorkerId,
                DeviceName = w.DeviceName,
                IsBusy = w.IsBusy,
                LastResultTick = w.LastResult?.TickId ?? -1,
                Beta = w.Beta,
                Temperature = w.Temperature
            }).ToArray()
        };
    }

    /// <summary>
    /// Wait for all workers to complete current tasks.
    /// </summary>
    /// <param name="timeout">Maximum wait time</param>
    public async Task WaitForCompletionAsync(TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            if (BusySpectralWorkers == 0 && BusyMcmcWorkers == 0)
                return;

            await Task.Delay(50);
        }
    }

    /// <summary>
    /// Cancel all running tasks.
    /// </summary>
    public void CancelAll()
    {
        _cts.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _cts.Cancel();

        foreach (var worker in _spectralWorkers)
        {
            worker.ComputationCompleted -= OnSpectralCompleted;
            worker.Dispose();
        }

        foreach (var worker in _mcmcWorkers)
        {
            worker.ChainCompleted -= OnMcmcCompleted;
            worker.Dispose();
        }

        _spectralWorkers.Clear();
        _mcmcWorkers.Clear();
        _cts.Dispose();

        _disposed = true;

        Console.WriteLine("[AsyncAnalysisOrchestrator] Disposed");
    }
}

/// <summary>
/// Status summary for the cluster.
/// </summary>
public sealed class ClusterStatus
{
    public int SpectralWorkerCount { get; init; }
    public int McmcWorkerCount { get; init; }
    public int BusySpectralWorkers { get; init; }
    public int BusyMcmcWorkers { get; init; }
    public int PendingSnapshots { get; init; }
    public int TotalSpectralResults { get; init; }
    public int TotalMcmcResults { get; init; }
    public WorkerStatus[] SpectralWorkerStatus { get; init; } = [];
    public WorkerStatus[] McmcWorkerStatus { get; init; } = [];
}

/// <summary>
/// Status of an individual worker.
/// </summary>
public sealed class WorkerStatus
{
    public int WorkerId { get; init; }
    public string DeviceName { get; init; } = "";
    public bool IsBusy { get; init; }
    public long LastResultTick { get; init; }
    public double? Beta { get; init; }
    public double? Temperature { get; init; }
}
