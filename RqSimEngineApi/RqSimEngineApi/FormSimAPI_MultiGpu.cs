using System;
using System.Threading;
using System.Threading.Tasks;
using RQSimulation;
using RQSimulation.Core.Infrastructure;
using RQSimulation.Core.Scheduler;
using RQSimulation.GPUOptimized;

namespace RqSimForms.Forms.Interfaces;

/// <summary>
/// Multi-GPU cluster integration for RqSimEngineApi.
/// 
/// ARCHITECTURE (1 Master + N Workers):
/// =====================================
/// - Physics GPU (Device 0): Primary computation - OptimizedGpuSimulationEngine
/// - Spectral Workers (Device 1-N/2): SpectralWalkEngine for d_s computation
/// - MCMC Workers (Device N/2+1 - N): GpuMCMCEngine for vacuum sampling
/// 
/// This architecture bypasses PCIe bottleneck by:
/// - Rare snapshot transfers (every 100+ steps)
/// - Heavy independent computations on each GPU
/// - Fire-and-forget async dispatch
/// </summary>
public partial class RqSimEngineApi
{
    // === Multi-GPU Infrastructure ===

    /// <summary>
    /// Multi-GPU compute cluster manager.
    /// </summary>
    public ComputeCluster? GpuCluster { get; private set; }

    /// <summary>
    /// Async analysis orchestrator for distributing work to worker GPUs.
    /// </summary>
    public AsyncAnalysisOrchestrator? MultiGpuOrchestrator { get; private set; }

    /// <summary>
    /// Whether Multi-GPU mode is enabled and active.
    /// </summary>
    public bool IsMultiGpuActive => GpuCluster?.IsMultiGpuAvailable == true && MultiGpuOrchestrator != null;

    /// <summary>
    /// Number of GPUs in the cluster.
    /// </summary>
    public int GpuClusterSize => GpuCluster?.TotalGpuCount ?? 1;

    /// <summary>
    /// Snapshot dispatch interval (steps between dispatches to workers).
    /// </summary>
    public int MultiGpuSnapshotInterval { get; set; } = 100;

    /// <summary>
    /// Last tick when snapshot was dispatched.
    /// </summary>
    private long _lastSnapshotTick;

    /// <summary>
    /// Configuration for Multi-GPU cluster.
    /// </summary>
    public MultiGpuConfig MultiGpuSettings { get; } = new();

    /// <summary>
    /// Initialize Multi-GPU cluster for parallel analysis.
    /// Call after GPU is detected but before simulation starts.
    /// </summary>
    /// <returns>True if Multi-GPU mode was enabled</returns>
    public bool InitializeMultiGpuCluster()
    {
        if (!GpuAvailable)
        {
            OnConsoleLog?.Invoke("[MultiGPU] GPU not available, skipping cluster initialization\n");
            return false;
        }

        try
        {
            // Create and initialize cluster
            GpuCluster = new ComputeCluster();

            var config = new ClusterConfiguration
            {
                SpectralWorkerCount = MultiGpuSettings.SpectralWorkerCount,
                McmcWorkerCount = MultiGpuSettings.McmcWorkerCount,
                MaxGraphSize = MultiGpuSettings.MaxGraphSize
            };

            GpuCluster.Initialize(config);

            if (!GpuCluster.IsMultiGpuAvailable)
            {
                OnConsoleLog?.Invoke($"[MultiGPU] Single GPU detected: {GpuCluster.PhysicsDevice?.Name}\n");
                OnConsoleLog?.Invoke("[MultiGPU] Multi-GPU mode disabled (requires 2+ GPUs)\n");
                return false;
            }

            // Create orchestrator
            MultiGpuOrchestrator = new AsyncAnalysisOrchestrator(GpuCluster);
            MultiGpuOrchestrator.Initialize(MultiGpuSettings.MaxGraphSize);

            // Configure orchestrator
            MultiGpuOrchestrator.SpectralConfig = new SpectralComputeConfig
            {
                NumSteps = MultiGpuSettings.SpectralSteps,
                WalkerCount = MultiGpuSettings.SpectralWalkers,
                SkipInitial = 10
            };

            MultiGpuOrchestrator.McmcChainConfig = new McmcChainConfig
            {
                NumSamples = MultiGpuSettings.McmcSamples,
                ThinningInterval = MultiGpuSettings.McmcThinning,
                ProposalsPerStep = 100
            };

            // Subscribe to results
            MultiGpuOrchestrator.SpectralCompleted += OnSpectralResultReceived;
            MultiGpuOrchestrator.McmcCompleted += OnMcmcResultReceived;

            var caps = GpuCluster.GetCapabilities();
            OnConsoleLog?.Invoke($"[MultiGPU] Cluster initialized: {caps.TotalDevices} GPUs\n");
            OnConsoleLog?.Invoke($"[MultiGPU] Physics: {caps.PhysicsDeviceName}\n");
            OnConsoleLog?.Invoke($"[MultiGPU] Spectral workers: {caps.SpectralWorkerCount}\n");
            OnConsoleLog?.Invoke($"[MultiGPU] MCMC workers: {caps.McmcWorkerCount}\n");
            OnConsoleLog?.Invoke($"[MultiGPU] Double precision: {(caps.IsDoublePrecisionSupported ? "Yes" : "No")}\n");

            return true;
        }
        catch (Exception ex)
        {
            OnConsoleLog?.Invoke($"[MultiGPU] Initialization failed: {ex.Message}\n");
            DisposeMultiGpuCluster();
            return false;
        }
    }

    /// <summary>
    /// Dispatch snapshot to worker GPUs if interval has elapsed.
    /// Call this from physics loop after each batch of steps.
    /// </summary>
    /// <param name="currentTick">Current simulation tick</param>
    public void TryDispatchSnapshot(long currentTick)
    {
        if (!IsMultiGpuActive || OptimizedGpuEngine == null)
            return;

        if (currentTick - _lastSnapshotTick < MultiGpuSnapshotInterval)
            return;

        try
        {
            // Download snapshot from physics GPU
            var snapshot = OptimizedGpuEngine.DownloadSnapshot(currentTick);

            // Dispatch to workers (fire-and-forget)
            MultiGpuOrchestrator!.OnPhysicsStepCompleted(snapshot);

            _lastSnapshotTick = currentTick;
        }
        catch (Exception ex)
        {
            // Don't fail physics loop on snapshot errors
            OnConsoleLog?.Invoke($"[MultiGPU] Snapshot dispatch error: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Get latest spectral dimension from Multi-GPU workers.
    /// </summary>
    public double? GetMultiGpuSpectralDimension()
    {
        var result = MultiGpuOrchestrator?.GetLatestSpectralResult();
        return result?.IsValid == true ? result.Value.SpectralDimension : null;
    }

    /// <summary>
    /// Get Multi-GPU cluster status.
    /// </summary>
    public ClusterStatus? GetMultiGpuStatus()
    {
        return MultiGpuOrchestrator?.GetStatus();
    }

    /// <summary>
    /// Get all spectral results collected so far.
    /// </summary>
    public IReadOnlyList<SpectralResult> GetAllSpectralResults()
    {
        return MultiGpuOrchestrator?.GetSpectralResults() ?? [];
    }

    /// <summary>
    /// Get all MCMC results collected so far.
    /// </summary>
    public IReadOnlyList<McmcResult> GetAllMcmcResults()
    {
        return MultiGpuOrchestrator?.GetMcmcResults() ?? [];
    }

    /// <summary>
    /// Wait for all Multi-GPU workers to complete current tasks.
    /// </summary>
    public async Task WaitForMultiGpuWorkersAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (MultiGpuOrchestrator == null)
            return;

        await MultiGpuOrchestrator.WaitForCompletionAsync(timeout);
    }

    /// <summary>
    /// Clear collected Multi-GPU results.
    /// </summary>
    public void ClearMultiGpuResults()
    {
        MultiGpuOrchestrator?.ClearResults();
    }

    /// <summary>
    /// Dispose Multi-GPU cluster resources.
    /// </summary>
    public void DisposeMultiGpuCluster()
    {
        if (MultiGpuOrchestrator != null)
        {
            MultiGpuOrchestrator.SpectralCompleted -= OnSpectralResultReceived;
            MultiGpuOrchestrator.McmcCompleted -= OnMcmcResultReceived;
            MultiGpuOrchestrator.Dispose();
            MultiGpuOrchestrator = null;
        }

        GpuCluster?.Dispose();
        GpuCluster = null;

        _lastSnapshotTick = 0;
    }

    // === Event Handlers ===

    /// <summary>
    /// Latest spectral dimension from Multi-GPU workers.
    /// </summary>
    private double _multiGpuSpectralDim = double.NaN;

    /// <summary>
    /// Latest MCMC energy from Multi-GPU workers.
    /// </summary>
    private double _multiGpuMcmcEnergy = double.NaN;

    /// <summary>
    /// Latest spectral dimension computed by Multi-GPU cluster.
    /// </summary>
    public double MultiGpuSpectralDimension => _multiGpuSpectralDim;

    /// <summary>
    /// Latest MCMC mean energy computed by Multi-GPU cluster.
    /// </summary>
    public double MultiGpuMcmcEnergy => _multiGpuMcmcEnergy;

    private void OnSpectralResultReceived(object? sender, SpectralResultEventArgs e)
    {
        // Update local cache with latest spectral dimension
        if (e.Result.IsValid)
        {
            _multiGpuSpectralDim = e.Result.SpectralDimension;
            OnConsoleLog?.Invoke($"[MultiGPU] Spectral d_s={e.Result.SpectralDimension:F4} " +
                                 $"(worker {e.Result.WorkerId}, {e.Result.ComputeTimeMs:F1}ms)\n");
        }
    }

    private void OnMcmcResultReceived(object? sender, McmcResultEventArgs e)
    {
        _multiGpuMcmcEnergy = e.Result.MeanEnergy;
        OnConsoleLog?.Invoke($"[MultiGPU] MCMC E={e.Result.MeanEnergy:F4}±{e.Result.StdEnergy:F4} " +
                             $"(worker {e.Result.WorkerId}, T={e.Result.Temperature:F2})\n");
    }
}

/// <summary>
/// Configuration for Multi-GPU cluster.
/// </summary>
public sealed class MultiGpuConfig
{
    /// <summary>Enable Multi-GPU mode.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Number of GPUs for spectral dimension (0 = auto).</summary>
    public int SpectralWorkerCount { get; set; } = 0;

    /// <summary>Number of GPUs for MCMC sampling (0 = auto).</summary>
    public int McmcWorkerCount { get; set; } = 0;

    /// <summary>Maximum graph size for buffer pre-allocation.</summary>
    public int MaxGraphSize { get; set; } = 100_000;

    /// <summary>Snapshot dispatch interval (steps).</summary>
    public int SnapshotInterval { get; set; } = 100;

    /// <summary>Number of random walk steps for spectral dimension.</summary>
    public int SpectralSteps { get; set; } = 100;

    /// <summary>Number of walkers for spectral dimension.</summary>
    public int SpectralWalkers { get; set; } = 10_000;

    /// <summary>Number of MCMC samples to collect.</summary>
    public int McmcSamples { get; set; } = 100;

    /// <summary>MCMC thinning interval.</summary>
    public int McmcThinning { get; set; } = 10;
}
