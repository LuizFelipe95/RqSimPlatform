using RQSimulation;
using RQSimulation.Core.Plugins;
using RQSimulation.GPUOptimized;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.GPUOptimizedCSR;

/// <summary>
/// GPU module for Ollivier-Ricci curvature via Sinkhorn optimal transport.
///
/// Computes Ollivier-Ricci curvature κ(i,j) = 1 - W₁(μ_i, μ_j) / d(i,j)
/// using entropic-regularized optimal transport (Sinkhorn-Knopp algorithm).
///
/// GPU path: dispatches CsrSinkhornInitKernel → CsrSinkhornUpdateU/V loop
///           → CsrSinkhornTransportCostKernel (when device context is available).
/// CPU fallback: OllivierRicciCurvature.ComputeOllivierRicciSinkhorn() per edge.
///
/// Unlike Forman-Ricci (scalar, O(degree²)):
///   - Based on genuine optimal transport (Wasserstein-1 distance)
///   - Captures geodesic deviation like Einstein gravity
///   - More expensive: O(support² × iterations) per edge
///   - Supports tensor-like curvature interpretation
///
/// Priority 42: runs after Forman (40) but before MCMC (45).
/// </summary>
public sealed class SinkhornOllivierRicciGpuModule : GpuPluginBase, IDynamicPhysicsModule
{
    private RQGraph? _graph;
    private double[]? _curvatures;

    public override string Name => "Ollivier-Ricci Curvature (Sinkhorn)";
    public override string Description => "Ollivier-Ricci curvature via Sinkhorn optimal transport (GPU with CPU fallback)";
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

        // Initial curvature calculation
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
    /// Compute Ollivier-Ricci curvature for all edges via Sinkhorn.
    /// Uses CPU fallback (GPU dispatch planned for when device context is wired).
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
    /// Get total Ollivier-Ricci scalar curvature (Einstein-Hilbert action integrand).
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

    protected override void DisposeCore()
    {
        _curvatures = null;
        _graph = null;
    }
}
