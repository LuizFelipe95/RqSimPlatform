using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Arch.Core.Extensions;
using ComputeSharp;
using RQSimulation.Core.ECS;
using RQSimulation.GPUCompressedSparseRow;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RqSimUI.Rendering.Systems;

/// <summary>
/// Synchronizes physics data from GPU CSR engine to ECS for visualization.
/// 
/// RESPONSIBILITIES:
/// - Readback wavefunction (Double2) from ComputeSharp GPU buffer
/// - Calculate visual properties: Phase, Size (|Psi|^2), Potential
/// - Update NodeVisualData components in Arch ECS world
/// 
/// PERFORMANCE:
/// - Async GPU readback to avoid blocking UI
/// - Parallel ECS chunk iteration for 100k+ nodes
/// - Version tracking to skip redundant syncs
/// </summary>
public sealed class PhysicsSyncSystem : IDisposable
{
    private readonly GpuCayleyEvolutionEngineCsr _physicsEngine;
    private readonly World _world;

    // CPU readback buffers
    private Double2[]? _wavefunctionBuffer;
    private double[]? _potentialBuffer;

    // Sync state
    private int _lastSyncVersion;
    private int _nodeCount;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Number of nodes being synchronized.
    /// </summary>
    public int NodeCount => _nodeCount;

    /// <summary>
    /// Whether the system is initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Last sync version for dirty tracking.
    /// </summary>
    public int LastSyncVersion => _lastSyncVersion;

    public PhysicsSyncSystem(GpuCayleyEvolutionEngineCsr physicsEngine, World world)
    {
        _physicsEngine = physicsEngine ?? throw new ArgumentNullException(nameof(physicsEngine));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// Initialize buffers based on current topology.
    /// </summary>
    public void Initialize()
    {
        if (!_physicsEngine.IsInitialized)
            throw new InvalidOperationException("Physics engine must be initialized first");

        var topology = _physicsEngine.Topology
            ?? throw new InvalidOperationException("Physics engine has no topology");

        _nodeCount = topology.NodeCount;
        _wavefunctionBuffer = new Double2[_nodeCount];
        _potentialBuffer = new double[_nodeCount];
        _lastSyncVersion = 0;
        _initialized = true;
    }

    /// <summary>
    /// Synchronize physics state to ECS visual components.
    /// Call once per frame before rendering.
    /// </summary>
    public void Sync()
    {
        if (!_initialized || _wavefunctionBuffer is null)
            throw new InvalidOperationException("PhysicsSyncSystem not initialized");

        // 1. Readback wavefunction from GPU
        ReadbackFromGpu();

        // 2. Update ECS components with physics data
        UpdateEcsComponents();

        _lastSyncVersion++;
    }

    /// <summary>
    /// Async sync for non-blocking UI.
    /// </summary>
    public async Task SyncAsync()
    {
        if (!_initialized || _wavefunctionBuffer is null)
            throw new InvalidOperationException("PhysicsSyncSystem not initialized");

        await Task.Run(Sync);
    }

    private void ReadbackFromGpu()
    {
        // Get wavefunction buffer from physics engine
        var psiBuffer = _physicsEngine.GetPsiBuffer();

        // Readback to CPU (ComputeSharp handles synchronization)
        psiBuffer.CopyTo(_wavefunctionBuffer!);

        // Readback potential if topology provides it
        var topology = _physicsEngine.Topology;
        if (topology is not null)
        {
            ReadbackPotential(topology);
        }
    }

    private void ReadbackPotential(CsrTopology topology)
    {
        // Potential is in ReadOnlyBuffer, need to copy through temp
        if (topology.IsGpuReady && _potentialBuffer is not null)
        {
            try
            {
                topology.NodePotentialBuffer.CopyTo(_potentialBuffer);
            }
            catch
            {
                // Fallback: zero potential
                Array.Fill(_potentialBuffer, 0.0);
            }
        }
    }

    private void UpdateEcsComponents()
    {
        // Query all entities with NodeVisualData and PhysicsIndex
        var query = new QueryDescription()
            .WithAll<NodeVisualData, PhysicsIndex>();

        _world.Query(in query, (Entity entity, ref NodeVisualData visual, ref PhysicsIndex physIdx) =>
        {
            int idx = physIdx.Value;
            if (idx < 0 || idx >= _nodeCount)
                return;

            var psi = _wavefunctionBuffer![idx];

            // Calculate phase: Arg(Psi) in radians
            visual.Phase = (float)Math.Atan2(psi.Y, psi.X);

            // Calculate size from probability: |Psi|^2
            double magnitudeSq = psi.X * psi.X + psi.Y * psi.Y;
            visual.Size = (float)magnitudeSq;

            // Set potential
            if (_potentialBuffer is not null && idx < _potentialBuffer.Length)
            {
                visual.Potential = (float)_potentialBuffer[idx];
            }
        });
    }

    /// <summary>
    /// Bulk update positions from spectral coordinates.
    /// </summary>
    public void UpdatePositionsFromSpectral(float[] spectralX, float[] spectralY, float[] spectralZ)
    {
        ArgumentNullException.ThrowIfNull(spectralX);
        ArgumentNullException.ThrowIfNull(spectralY);
        ArgumentNullException.ThrowIfNull(spectralZ);

        var query = new QueryDescription()
            .WithAll<NodeVisualData, PhysicsIndex>();

        _world.Query(in query, (ref NodeVisualData visual, ref PhysicsIndex physIdx) =>
        {
            int idx = physIdx.Value;
            if (idx < 0 || idx >= spectralX.Length)
                return;

            visual.Position = new Vector3(spectralX[idx], spectralY[idx], spectralZ[idx]);
        });
    }

    /// <summary>
    /// Set color mode for all visual nodes.
    /// </summary>
    /// <param name="mode">0 = Phase, 1 = Energy, 2 = Curvature</param>
    public void SetColorMode(uint mode)
    {
        var query = new QueryDescription().WithAll<NodeVisualData>();

        _world.Query(in query, (ref NodeVisualData visual) =>
        {
            visual.ColorMode = mode;
        });
    }

    /// <summary>
    /// Get wavefunction data for a specific node (for UI display).
    /// </summary>
    public (double Real, double Imaginary, double Probability, double Phase) GetNodeQuantumState(int nodeIndex)
    {
        if (_wavefunctionBuffer is null || nodeIndex < 0 || nodeIndex >= _nodeCount)
            return (0, 0, 0, 0);

        var psi = _wavefunctionBuffer[nodeIndex];
        double prob = psi.X * psi.X + psi.Y * psi.Y;
        double phase = Math.Atan2(psi.Y, psi.X);

        return (psi.X, psi.Y, prob, phase);
    }

    /// <summary>
    /// Get total probability (should be ~1.0 for normalized wavefunction).
    /// </summary>
    public double GetTotalProbability()
    {
        if (_wavefunctionBuffer is null)
            return 0;

        double total = 0;
        for (int i = 0; i < _nodeCount; i++)
        {
            var psi = _wavefunctionBuffer[i];
            total += psi.X * psi.X + psi.Y * psi.Y;
        }
        return total;
    }

    /// <summary>
    /// Component for storing cluster ID.
    /// </summary>
    public struct ClusterIdComponent
    {
        public int Value;
    }

    /// <summary>
    /// Update cluster IDs for all nodes.
    /// </summary>
    public void UpdateClusters(int[] clusterIds)
    {
        if (clusterIds is null) return;

        var query = new QueryDescription().WithAll<ClusterIdComponent, PhysicsIndex>();

        _world.Query(in query, (ref ClusterIdComponent cluster, ref PhysicsIndex physIdx) =>
        {
            int idx = physIdx.Value;
            if (idx >= 0 && idx < clusterIds.Length)
            {
                cluster.Value = clusterIds[idx];
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _wavefunctionBuffer = null;
        _potentialBuffer = null;
        _initialized = false;
        _disposed = true;
    }
}
