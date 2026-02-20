using RQSimulation;
using RQSimulation.Core.Plugins;
using RQSimulation.GPUOptimized;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.CPU;

/// <summary>
/// CPU module for Ollivier-Ricci curvature via Sinkhorn optimal transport.
///
/// Pure CPU implementation — no GPU context required. Suitable for:
///   - Machines without GPU or without double-precision support
///   - ServerMode console pipeline (no device context)
///   - Validation/testing against GPU results
///
/// Delegates to OllivierRicciCurvature.ComputeOllivierRicciSinkhorn() which implements:
///   1. Lazy random walk distributions μ_i, μ_j
///   2. Cost matrix via graph distances
///   3. Gibbs kernel K = exp(-C/ε)
///   4. Sinkhorn-Knopp iterations for optimal transport
///   5. κ(i,j) = 1 - W₁/d(i,j)
///
/// Priority 42: same as GPU counterpart (only one should be active at a time).
/// </summary>
public sealed class OllivierRicciCpuModule : CpuPluginBase, IDynamicPhysicsModule
{
    private RQGraph? _graph;
    private double[]? _curvatures;

    public override string Name => "Ollivier-Ricci Curvature (CPU)";
    public override string Description => "CPU Ollivier-Ricci curvature via Sinkhorn optimal transport";
    public override string Category => "Curvature";
    public override int Priority => 42;

    /// <summary>
    /// Maximum Sinkhorn iterations for convergence.
    /// </summary>
    public int SinkhornIterations { get; set; } = 50;

    /// <summary>
    /// Entropic regularization ε. Smaller = more accurate but slower.
    /// </summary>
    public double SinkhornEpsilon { get; set; } = 0.01;

    /// <summary>
    /// Convergence tolerance for Sinkhorn scaling vectors.
    /// </summary>
    public double ConvergenceThreshold { get; set; } = 1e-6;

    /// <summary>
    /// Lazy random walk parameter α (probability of staying at current node).
    /// </summary>
    public double LazyWalkAlpha { get; set; } = 0.1;

    /// <summary>
    /// Coupling constant for Ricci-flow weight evolution.
    /// </summary>
    public double GravityCoupling { get; set; } = 0.1;

    /// <summary>
    /// Last computed curvatures per edge (indexed by flat edge index).
    /// </summary>
    public IReadOnlyList<double>? LastCurvatures => _curvatures;

    /// <summary>
    /// Average Ollivier-Ricci curvature across all edges.
    /// </summary>
    public double AverageCurvature
    {
        get
        {
            if (_curvatures is null || _curvatures.Length == 0) return 0.0;
            double sum = 0.0;
            int count = 0;
            for (int i = 0; i < _curvatures.Length; i++)
            {
                if (_curvatures[i] != 0.0)
                {
                    sum += _curvatures[i];
                    count++;
                }
            }
            return count > 0 ? sum / count : 0.0;
        }
    }

    public override void Initialize(RQGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));

        int maxEdges = graph.N * (graph.N - 1) / 2;
        _curvatures = new double[maxEdges];

        ComputeAllCurvatures();
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (_graph is null) return;

        ComputeAllCurvatures();
        EvolveWeightsByCurvature(dt);
    }

    /// <summary>
    /// Updates Sinkhorn parameters from pipeline's per-frame dynamic configuration.
    /// </summary>
    public void UpdateParameters(in DynamicPhysicsParams parameters)
    {
        SinkhornIterations = parameters.SinkhornIterations;
        SinkhornEpsilon = parameters.SinkhornEpsilon;
        ConvergenceThreshold = parameters.ConvergenceThreshold;
        LazyWalkAlpha = parameters.LazyWalkAlpha;
    }

    /// <summary>
    /// Compute Ollivier-Ricci curvature for all edges using Sinkhorn.
    /// </summary>
    private void ComputeAllCurvatures()
    {
        if (_graph is null || _curvatures is null) return;

        int edgeIdx = 0;
        for (int i = 0; i < _graph.N; i++)
        {
            for (int j = i + 1; j < _graph.N; j++)
            {
                if (edgeIdx >= _curvatures.Length) break;

                if (_graph.Edges[i, j])
                {
                    _curvatures[edgeIdx] = OllivierRicciCurvature.ComputeOllivierRicciSinkhorn(
                        _graph, i, j,
                        lazyWalkAlpha: LazyWalkAlpha,
                        epsilon: SinkhornEpsilon,
                        maxIterations: SinkhornIterations,
                        convergenceTol: ConvergenceThreshold);
                }
                else
                {
                    _curvatures[edgeIdx] = 0.0;
                }
                edgeIdx++;
            }
        }
    }

    /// <summary>
    /// Evolve edge weights based on Ollivier-Ricci curvature (Ricci flow).
    /// dw/dt = -κ · coupling · w
    /// </summary>
    private void EvolveWeightsByCurvature(double dt)
    {
        if (_graph is null || _curvatures is null) return;

        int edgeIdx = 0;
        for (int i = 0; i < _graph.N; i++)
        {
            for (int j = i + 1; j < _graph.N; j++)
            {
                if (edgeIdx >= _curvatures.Length) break;

                if (_graph.Edges[i, j])
                {
                    double curvature = _curvatures[edgeIdx];
                    double w = _graph.Weights[i, j];

                    double dw = -curvature * GravityCoupling * w * dt;
                    double newW = Math.Clamp(w + dw, 0.01, 1.0);

                    _graph.Weights[i, j] = newW;
                    _graph.Weights[j, i] = newW;
                }
                edgeIdx++;
            }
        }
    }

    /// <summary>
    /// Get total Ollivier-Ricci scalar curvature.
    /// </summary>
    public double GetTotalScalarCurvature()
    {
        if (_curvatures is null) return 0.0;

        double total = 0.0;
        for (int i = 0; i < _curvatures.Length; i++)
        {
            total += _curvatures[i];
        }
        return total;
    }

    public override void Cleanup()
    {
        _curvatures = null;
        _graph = null;
    }
}
