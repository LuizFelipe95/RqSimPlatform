// ============================================================
// EdgeAnisotropyGpuModule.cs
// GPU module for per-node edge anisotropy computation
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using RQSimulation;
using RQSimulation.Core.Plugins;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.GPUOptimizedCSR;

/// <summary>
/// GPU module for computing per-node edge anisotropy.
/// 
/// Anisotropy measures directional variation in edge weights around each node.
/// High anisotropy indicates preferential directions (like crystalline structure).
/// Low anisotropy indicates isotropic connections (like fluid).
/// 
/// Physics interpretation:
/// - In GR terms: anisotropy relates to shear components of the stress-energy tensor
/// - In graph terms: measures deviation from uniform connectivity
/// 
/// Formula per node:
/// A_i = sqrt(Var(w_ij)) / Mean(w_ij)
/// where w_ij are weights of edges incident to node i.
/// 
/// NOTE: Current implementation uses optimized CPU computation.
/// GPU version (EdgeAnisotropyKernelDouble) can be enabled for large graphs
/// when ComputeSharp shader compilation is properly configured.
/// </summary>
public sealed class EdgeAnisotropyGpuModule : GpuPluginBase
{
    private RQGraph? _graph;
    private double[]? _nodeAnisotropy;
    private bool _gpuEnabled = true;

    public override string Name => "Edge Anisotropy (GPU)";
    public override string Description => "Per-node edge anisotropy computation (coefficient of variation)";
    public override string Category => "Geometry";
    public override int Priority => 35;
    public override ExecutionStage Stage => ExecutionStage.PostProcess;
    public override ExecutionType ExecutionType => ExecutionType.GPU;

    /// <summary>
    /// Enable/disable GPU acceleration.
    /// When disabled, falls back to CPU computation.
    /// </summary>
    public bool GpuEnabled
    {
        get => _gpuEnabled;
        set => _gpuEnabled = value;
    }

    /// <summary>
    /// Per-node anisotropy values from last computation.
    /// </summary>
    public IReadOnlyList<double>? NodeAnisotropy => _nodeAnisotropy;

    /// <summary>
    /// Average anisotropy across all nodes.
    /// </summary>
    public double AverageAnisotropy
    {
        get
        {
            if (_nodeAnisotropy is null || _nodeAnisotropy.Length == 0) return 0.0;
            double sum = 0.0;
            for (int i = 0; i < _nodeAnisotropy.Length; i++)
                sum += _nodeAnisotropy[i];
            return sum / _nodeAnisotropy.Length;
        }
    }

    /// <summary>
    /// Maximum anisotropy across all nodes.
    /// </summary>
    public double MaxAnisotropy
    {
        get
        {
            if (_nodeAnisotropy is null || _nodeAnisotropy.Length == 0) return 0.0;
            double max = 0.0;
            for (int i = 0; i < _nodeAnisotropy.Length; i++)
                if (_nodeAnisotropy[i] > max) max = _nodeAnisotropy[i];
            return max;
        }
    }

    public override void Initialize(RQGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _nodeAnisotropy = new double[graph.N];

        // Check global setting
        _gpuEnabled = PhysicsConstants.UseGpuEdgeAnisotropy;

        // Initial computation
        ComputeAllAnisotropy();
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (_graph is null || _nodeAnisotropy is null) return;

        ComputeAllAnisotropy();
    }

    /// <summary>
    /// Compute anisotropy for all nodes using optimized CPU computation.
    /// GPU acceleration can be enabled for large graphs when shader is ready.
    /// </summary>
    private void ComputeAllAnisotropy()
    {
        if (_graph is null || _nodeAnisotropy is null) return;

        // Use parallel CPU computation for better performance
        System.Threading.Tasks.Parallel.For(0, _graph.N, i =>
        {
            _nodeAnisotropy[i] = ComputeNodeAnisotropy(i);
        });
    }

    /// <summary>
    /// Compute anisotropy for a single node.
    /// A_i = StdDev(w_ij) / Mean(w_ij) (coefficient of variation)
    /// </summary>
    private double ComputeNodeAnisotropy(int node)
    {
        if (_graph is null) return 0.0;

        var neighbors = _graph.Neighbors(node).ToList();
        if (neighbors.Count < 2) return 0.0;

        // Compute mean
        double sum = 0.0;
        foreach (int n in neighbors)
            sum += _graph.Weights[node, n];
        double mean = sum / neighbors.Count;

        if (mean <= 1e-10) return 0.0;

        // Compute variance
        double variance = 0.0;
        foreach (int n in neighbors)
        {
            double diff = _graph.Weights[node, n] - mean;
            variance += diff * diff;
        }
        variance /= neighbors.Count;

        // Coefficient of variation
        return Math.Sqrt(variance) / mean;
    }

    protected override void DisposeCore()
    {
        // No GPU resources to dispose in current implementation
    }
}
