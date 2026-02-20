using System;
using System.Collections.Generic;
using System.Linq;
using ComputeSharp;

namespace RQSimulation.Core.Infrastructure;

/// <summary>
/// Multi-GPU cluster manager for RqSimPlatform physics simulation.
/// 
/// ARCHITECTURE (1 + 9 + 10 model):
/// ================================
/// - Physics Node (ID 0): Primary GPU. Stores "Ground Truth" state of the universe.
///   Computes evolution (H, Yang-Mills).
/// - Analysis Cluster (ID 1-9): Workers for SpectralWalkEngine.
///   Receive topology snapshot, compute spectral dimension (d_s), return results.
/// - Sampling Cluster (ID 10-19): Workers for GpuMCMCEngine.
///   Receive topology, run independent Markov chains (Parallel Tempering) for vacuum search.
/// 
/// IMPORTANT: ReadWriteBuffer objects are bound to the GraphicsDevice that created them.
/// Cannot pass a buffer from Device[0] to a kernel on Device[1]. Data must be transferred
/// via ToArray() (CPU array).
/// </summary>
public sealed class ComputeCluster : IDisposable
{
    private bool _disposed;
    private bool _initialized;

    /// <summary>
    /// Primary physics GPU. Stores ground truth state of the universe.
    /// </summary>
    public GraphicsDevice? PhysicsDevice { get; private set; }

    /// <summary>
    /// Worker GPUs for spectral dimension computation (SpectralWalkEngine).
    /// </summary>
    public GraphicsDevice[] SpectralWorkers { get; private set; } = [];

    /// <summary>
    /// Worker GPUs for MCMC sampling (GpuMCMCEngine, Parallel Tempering).
    /// </summary>
    public GraphicsDevice[] McmcWorkers { get; private set; } = [];

    /// <summary>
    /// All available hardware-accelerated devices.
    /// </summary>
    public IReadOnlyList<GraphicsDevice> AllDevices { get; private set; } = [];

    /// <summary>
    /// Total number of available GPUs.
    /// </summary>
    public int TotalGpuCount => AllDevices.Count;

    /// <summary>
    /// Whether the cluster is initialized and ready for use.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Whether multi-GPU mode is available (2+ devices).
    /// </summary>
    public bool IsMultiGpuAvailable => AllDevices.Count >= 2;

    /// <summary>
    /// Whether double precision is supported on the physics device.
    /// </summary>
    public bool IsDoublePrecisionSupported => PhysicsDevice?.IsDoublePrecisionSupportAvailable() ?? false;

    /// <summary>
    /// Configuration for cluster role distribution.
    /// </summary>
    public ClusterConfiguration Configuration { get; private set; } = new();

    /// <summary>
    /// Initialize the compute cluster with automatic device detection and role assignment.
    /// </summary>
    /// <param name="config">Optional configuration for custom role distribution</param>
    /// <exception cref="InvalidOperationException">No hardware-accelerated GPU found</exception>
    public void Initialize(ClusterConfiguration? config = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            return;
        }

        Configuration = config ?? new ClusterConfiguration();

        // Enumerate all hardware-accelerated DX12 devices
        var allDevices = GraphicsDevice.EnumerateDevices()
            .Where(d => d.IsHardwareAccelerated)
            .ToList();

        if (allDevices.Count == 0)
        {
            throw new InvalidOperationException(
                "No hardware-accelerated GPU found. Multi-GPU cluster requires at least one DX12-compatible GPU.");
        }

        AllDevices = allDevices.AsReadOnly();

        // Assign roles based on device count and configuration
        AssignDeviceRoles(allDevices);

        _initialized = true;

        LogClusterStatus();
    }

    /// <summary>
    /// Initialize for single-GPU mode (fallback).
    /// </summary>
    public void InitializeSingleGpu()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            return;
        }

        PhysicsDevice = GraphicsDevice.GetDefault();
        AllDevices = [PhysicsDevice];
        SpectralWorkers = [];
        McmcWorkers = [];
        Configuration = new ClusterConfiguration { Mode = ClusterMode.SingleGpu };

        _initialized = true;

        Console.WriteLine($"[ComputeCluster] Single GPU mode: {PhysicsDevice.Name}");
        Console.WriteLine($"[ComputeCluster] Double precision: {(IsDoublePrecisionSupported ? "Supported" : "Not supported")}");
    }

    private void AssignDeviceRoles(List<GraphicsDevice> devices)
    {
        int deviceCount = devices.Count;

        // Device 0 is always the physics node
        PhysicsDevice = devices[0];

        if (deviceCount == 1)
        {
            // Single GPU mode - no workers
            SpectralWorkers = [];
            McmcWorkers = [];
            Configuration.Mode = ClusterMode.SingleGpu;
            return;
        }

        Configuration.Mode = ClusterMode.MultiGpu;

        // Calculate worker distribution
        int remainingDevices = deviceCount - 1;

        if (Configuration.SpectralWorkerCount > 0 && Configuration.McmcWorkerCount > 0)
        {
            // Use explicit configuration
            int spectralCount = Math.Min(Configuration.SpectralWorkerCount, remainingDevices);
            int mcmcCount = Math.Min(Configuration.McmcWorkerCount, remainingDevices - spectralCount);

            SpectralWorkers = devices.Skip(1).Take(spectralCount).ToArray();
            McmcWorkers = devices.Skip(1 + spectralCount).Take(mcmcCount).ToArray();
        }
        else
        {
            // Auto-distribute: 50/50 split with preference for spectral workers
            int spectralCount = (remainingDevices + 1) / 2;
            int mcmcCount = remainingDevices - spectralCount;

            SpectralWorkers = devices.Skip(1).Take(spectralCount).ToArray();
            McmcWorkers = devices.Skip(1 + spectralCount).Take(mcmcCount).ToArray();
        }
    }

    private void LogClusterStatus()
    {
        Console.WriteLine($"[ComputeCluster] Initialized with {AllDevices.Count} GPU(s)");
        Console.WriteLine($"[ComputeCluster] Mode: {Configuration.Mode}");
        Console.WriteLine($"[ComputeCluster] Physics Device: {PhysicsDevice?.Name ?? "None"}");
        Console.WriteLine($"[ComputeCluster] Double precision: {(IsDoublePrecisionSupported ? "Supported" : "Not supported")}");
        Console.WriteLine($"[ComputeCluster] Spectral Workers: {SpectralWorkers.Length}");

        for (int i = 0; i < SpectralWorkers.Length; i++)
        {
            Console.WriteLine($"  [{i}] {SpectralWorkers[i].Name}");
        }

        Console.WriteLine($"[ComputeCluster] MCMC Workers: {McmcWorkers.Length}");

        for (int i = 0; i < McmcWorkers.Length; i++)
        {
            Console.WriteLine($"  [{i}] {McmcWorkers[i].Name}");
        }
    }

    /// <summary>
    /// Get a specific device by index (0 = physics, 1+ = workers).
    /// </summary>
    /// <param name="index">Device index</param>
    /// <returns>GraphicsDevice at the specified index</returns>
    /// <exception cref="ArgumentOutOfRangeException">Index out of range</exception>
    public GraphicsDevice GetDevice(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized)
        {
            throw new InvalidOperationException("Cluster not initialized. Call Initialize() first.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, AllDevices.Count);

        return AllDevices[index];
    }

    /// <summary>
    /// Get summary of cluster capabilities.
    /// </summary>
    public ClusterCapabilities GetCapabilities()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return new ClusterCapabilities
        {
            TotalDevices = AllDevices.Count,
            PhysicsDeviceName = PhysicsDevice?.Name ?? "None",
            SpectralWorkerCount = SpectralWorkers.Length,
            McmcWorkerCount = McmcWorkers.Length,
            IsDoublePrecisionSupported = IsDoublePrecisionSupported,
            Mode = Configuration.Mode,
            TotalVramMb = AllDevices.Sum(d => (long)(d.DedicatedMemorySize / (1024 * 1024)))
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Note: GraphicsDevice instances from EnumerateDevices() are managed by ComputeSharp
        // and should not be explicitly disposed here. Only dispose devices you explicitly created.
        // However, we clear our references.

        PhysicsDevice = null;
        SpectralWorkers = [];
        McmcWorkers = [];
        AllDevices = [];

        _initialized = false;
        _disposed = true;
    }
}

/// <summary>
/// Cluster operation mode.
/// </summary>
public enum ClusterMode
{
    /// <summary>Single GPU mode (fallback).</summary>
    SingleGpu,

    /// <summary>Multi-GPU cluster mode.</summary>
    MultiGpu
}

/// <summary>
/// Configuration for compute cluster role distribution.
/// </summary>
public sealed class ClusterConfiguration
{
    /// <summary>
    /// Current cluster mode.
    /// </summary>
    public ClusterMode Mode { get; set; } = ClusterMode.SingleGpu;

    /// <summary>
    /// Number of GPUs to allocate for spectral dimension computation.
    /// 0 = auto-distribute.
    /// </summary>
    public int SpectralWorkerCount { get; set; }

    /// <summary>
    /// Number of GPUs to allocate for MCMC sampling.
    /// 0 = auto-distribute.
    /// </summary>
    public int McmcWorkerCount { get; set; }

    /// <summary>
    /// Maximum graph size (nodes) for worker engines.
    /// Used for pre-allocation of buffers.
    /// </summary>
    public int MaxGraphSize { get; set; } = 100_000;
}

/// <summary>
/// Summary of cluster capabilities.
/// </summary>
public readonly record struct ClusterCapabilities
{
    /// <summary>Total available GPU devices.</summary>
    public int TotalDevices { get; init; }

    /// <summary>Name of the physics device.</summary>
    public string PhysicsDeviceName { get; init; }

    /// <summary>Number of spectral dimension workers.</summary>
    public int SpectralWorkerCount { get; init; }

    /// <summary>Number of MCMC sampling workers.</summary>
    public int McmcWorkerCount { get; init; }

    /// <summary>Whether double precision is supported on physics device.</summary>
    public bool IsDoublePrecisionSupported { get; init; }

    /// <summary>Cluster operation mode.</summary>
    public ClusterMode Mode { get; init; }

    /// <summary>Total VRAM across all devices (MB).</summary>
    public long TotalVramMb { get; init; }
}
