using System;
using System.Threading;
using System.Threading.Tasks;
using ComputeSharp;
using RQSimulation.Core.Infrastructure;
using RQSimulation.GPUOptimized;

namespace RQSimulation.Core.Scheduler;

/// <summary>
/// Worker wrapper for SpectralWalkEngine running on a dedicated GPU.
/// 
/// PURPOSE:
/// ========
/// Manages lifecycle and async execution of spectral dimension computation
/// on a specific GPU in the multi-GPU cluster.
/// 
/// WORKFLOW:
/// =========
/// 1. Receives GraphSnapshot from orchestrator (CPU RAM)
/// 2. Uploads topology to assigned GPU
/// 3. Runs random walk computation asynchronously
/// 4. Reports result via callback/completion
/// 
/// THREAD SAFETY:
/// ==============
/// - IsBusy flag prevents concurrent execution on same worker
/// - ProcessAsync runs computation on ThreadPool, not UI thread
/// </summary>
public sealed class SpectralWorker : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly SpectralWalkEngine _engine;
    private readonly int _workerId;
    private readonly object _lockObj = new();
    private bool _disposed;
    private volatile bool _isBusy;

    /// <summary>
    /// Whether this worker is currently processing a snapshot.
    /// </summary>
    public bool IsBusy => _isBusy;

    /// <summary>
    /// Worker identifier for logging/debugging.
    /// </summary>
    public int WorkerId => _workerId;

    /// <summary>
    /// Name of the assigned GPU device.
    /// </summary>
    public string DeviceName => _device.Name;

    /// <summary>
    /// The GPU device this worker is bound to.
    /// </summary>
    public GraphicsDevice Device => _device;

    /// <summary>
    /// Last computed spectral dimension result.
    /// </summary>
    public SpectralResult? LastResult { get; private set; }

    /// <summary>
    /// Event raised when computation completes.
    /// </summary>
    public event EventHandler<SpectralResultEventArgs>? ComputationCompleted;

    /// <summary>
    /// Create a spectral worker on the specified GPU.
    /// </summary>
    /// <param name="device">Target GPU device</param>
    /// <param name="workerId">Worker identifier</param>
    /// <param name="maxNodeCount">Maximum graph size for buffer pre-allocation</param>
    /// <exception cref="ArgumentNullException">Device is null</exception>
    public SpectralWorker(GraphicsDevice device, int workerId, int maxNodeCount = 100_000)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegative(workerId);

        _device = device;
        _workerId = workerId;

        // Create engine bound to this specific GPU
        _engine = new SpectralWalkEngine(device);

        Console.WriteLine($"[SpectralWorker {_workerId}] Created on {device.Name}");
    }

    /// <summary>
    /// Process a graph snapshot asynchronously.
    /// Returns immediately, computation runs on background thread.
    /// </summary>
    /// <param name="snapshot">Graph topology snapshot</param>
    /// <param name="config">Computation configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when computation is done</returns>
    public async Task<SpectralResult> ProcessAsync(
        GraphSnapshot snapshot,
        SpectralComputeConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Validate())
        {
            throw new ArgumentException("Invalid snapshot data", nameof(snapshot));
        }

        lock (_lockObj)
        {
            if (_isBusy)
            {
                throw new InvalidOperationException($"Worker {_workerId} is already busy");
            }
            _isBusy = true;
        }

        config ??= SpectralComputeConfig.Default;

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var startTime = DateTime.UtcNow;

                // 1. Upload topology from snapshot (CPU -> GPU)
                UploadTopology(snapshot, config.WalkerCount);

                cancellationToken.ThrowIfCancellationRequested();

                // 2. Initialize walkers
                _engine.InitializeWalkersRandom(new Random(config.Seed ?? Environment.TickCount));

                cancellationToken.ThrowIfCancellationRequested();

                // 3. Run random walks
                int[] returns = _engine.RunSteps(config.NumSteps);

                cancellationToken.ThrowIfCancellationRequested();

                // 4. Compute spectral dimension
                double spectralDimension = _engine.ComputeSpectralDimension(returns, config.SkipInitial);

                var endTime = DateTime.UtcNow;

                var result = new SpectralResult
                {
                    SpectralDimension = spectralDimension,
                    TickId = snapshot.TickId,
                    WorkerId = _workerId,
                    ComputeTimeMs = (endTime - startTime).TotalMilliseconds,
                    NodeCount = snapshot.NodeCount,
                    EdgeCount = snapshot.EdgeCount,
                    WalkerCount = config.WalkerCount,
                    NumSteps = config.NumSteps,
                    IsValid = !double.IsNaN(spectralDimension)
                };

                LastResult = result;

                // Fire completion event
                ComputationCompleted?.Invoke(this, new SpectralResultEventArgs(result));

                return result;
            }, cancellationToken);
        }
        finally
        {
            _isBusy = false;
        }
    }

    /// <summary>
    /// Upload topology from snapshot to GPU.
    /// </summary>
    private void UploadTopology(GraphSnapshot snapshot, int walkerCount)
    {
        // Convert double weights to float for SpectralWalkEngine
        float[] floatWeights = new float[snapshot.EdgeWeights.Length];
        for (int i = 0; i < snapshot.EdgeWeights.Length; i++)
        {
            floatWeights[i] = (float)snapshot.EdgeWeights[i];
        }

        // Initialize engine buffers if needed
        _engine.Initialize(
            walkerCount,
            snapshot.NodeCount,
            snapshot.Nnz);

        // Upload CSR topology
        _engine.UpdateTopology(
            snapshot.RowOffsets,
            snapshot.ColIndices,
            floatWeights);
    }

    /// <summary>
    /// Check if worker can accept a new job.
    /// </summary>
    public bool TryReserve()
    {
        lock (_lockObj)
        {
            if (_isBusy)
                return false;

            _isBusy = true;
            return true;
        }
    }

    /// <summary>
    /// Release reservation (call if job not submitted after TryReserve).
    /// </summary>
    public void Release()
    {
        _isBusy = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _engine.Dispose();
        _disposed = true;

        Console.WriteLine($"[SpectralWorker {_workerId}] Disposed");
    }
}

/// <summary>
/// Configuration for spectral dimension computation.
/// </summary>
public sealed class SpectralComputeConfig
{
    /// <summary>Number of random walk steps.</summary>
    public int NumSteps { get; init; } = 100;

    /// <summary>Number of parallel walkers.</summary>
    public int WalkerCount { get; init; } = 10_000;

    /// <summary>Initial steps to skip (thermalization).</summary>
    public int SkipInitial { get; init; } = 10;

    /// <summary>RNG seed (null = use system time).</summary>
    public int? Seed { get; init; }

    /// <summary>Default configuration.</summary>
    public static SpectralComputeConfig Default { get; } = new();
}

/// <summary>
/// Result of spectral dimension computation.
/// </summary>
public readonly record struct SpectralResult
{
    /// <summary>Computed spectral dimension d_s.</summary>
    public double SpectralDimension { get; init; }

    /// <summary>Simulation tick when snapshot was taken.</summary>
    public long TickId { get; init; }

    /// <summary>Worker that performed computation.</summary>
    public int WorkerId { get; init; }

    /// <summary>Computation time in milliseconds.</summary>
    public double ComputeTimeMs { get; init; }

    /// <summary>Number of nodes in processed graph.</summary>
    public int NodeCount { get; init; }

    /// <summary>Number of edges in processed graph.</summary>
    public int EdgeCount { get; init; }

    /// <summary>Number of walkers used.</summary>
    public int WalkerCount { get; init; }

    /// <summary>Number of steps executed.</summary>
    public int NumSteps { get; init; }

    /// <summary>Whether result is valid (not NaN).</summary>
    public bool IsValid { get; init; }
}

/// <summary>
/// Event args for spectral computation completion.
/// </summary>
public sealed class SpectralResultEventArgs : EventArgs
{
    /// <summary>Computation result.</summary>
    public SpectralResult Result { get; }

    public SpectralResultEventArgs(SpectralResult result)
    {
        Result = result;
    }
}
