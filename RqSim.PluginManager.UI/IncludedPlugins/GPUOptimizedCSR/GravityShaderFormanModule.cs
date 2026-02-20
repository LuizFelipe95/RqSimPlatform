using RQSimulation;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.GPUOptimizedCSR;

/// <summary>
/// GPU module for Forman-Ricci curvature calculation (Jost formula).
/// 
/// Jost weighted formula:
///   Ric_F(e) = 2 - w_e · [ Σ_{e'~u, e'≠e} √(1/(w_e·w_{e'})) + Σ_{e''~v, e''≠e} √(1/(w_e·w_{e''})) ]
///
/// NOTE: This is a SCALAR curvature - it cannot describe:
/// - Gravitational waves (spin-2)
/// - Tensor perturbations
/// - Full Riemann curvature
/// 
/// For tensor curvature with spin-2 support, see Ollivier-Ricci implementation.
/// 
/// Based on original GravityShaders.forman implementation.
/// </summary>
public sealed class GravityShaderFormanModule : GpuPluginBase
{
    private RQGraph? _graph;
    private double[]? _curvatures;

    public override string Name => "Forman Curvature (GPU)";
    public override string Description => "GPU-accelerated Forman-Ricci scalar curvature calculation (Jost formula)";
    public override string Category => "Gravity";
    public override int Priority => 40;

    /// <summary>
    /// Coupling constant for gravity evolution.
    /// </summary>
    public double GravityCoupling { get; set; } = 0.1;

    /// <summary>
    /// Last computed curvatures per edge (indexed by flat edge index).
    /// </summary>
    public IReadOnlyList<double>? LastCurvatures => _curvatures;

    /// <summary>
    /// Average curvature across all edges.
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

        // Allocate curvature buffer
        int maxEdges = graph.N * (graph.N - 1) / 2;
        _curvatures = new double[maxEdges];

        // Initial curvature calculation
        ComputeAllCurvatures();
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (_graph is null) return;

        // Recompute curvatures
        ComputeAllCurvatures();

        // Apply gravity-driven weight evolution
        EvolveWeightsByCurvature(dt);
    }

    /// <summary>
    /// Compute Forman-Ricci curvature for all edges.
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
                    _curvatures[edgeIdx] = ComputeFormanCurvature(i, j);
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
    /// Compute Forman-Ricci curvature for edge (u, v) using Jost formula.
    /// 
    /// Jost formula:
    ///   Ric_F(e) = 2 - w_e · [ Σ_{e'~u, e'≠e} √(1/(w_e·w_{e'})) + Σ_{e''~v, e''≠e} √(1/(w_e·w_{e''})) ]
    /// </summary>
    public double ComputeFormanCurvature(int u, int v)
    {
        if (_graph is null || !_graph.Edges[u, v]) return 0.0;

        double w_e = _graph.Weights[u, v];
        if (w_e <= 0.0) return 0.0;

        // Sum over edges incident to u (excluding edge e)
        double sumU = 0.0;
        foreach (int n in _graph.Neighbors(u))
        {
            if (n != v)
            {
                double w_adj = _graph.Weights[u, n];
                if (w_adj > 0.0)
                    sumU += Math.Sqrt(1.0 / (w_e * w_adj));
            }
        }

        // Sum over edges incident to v (excluding edge e)
        double sumV = 0.0;
        foreach (int n in _graph.Neighbors(v))
        {
            if (n != u)
            {
                double w_adj = _graph.Weights[v, n];
                if (w_adj > 0.0)
                    sumV += Math.Sqrt(1.0 / (w_e * w_adj));
            }
        }

        // Jost weighted Forman-Ricci curvature
        return 2.0 - w_e * (sumU + sumV);
    }

    /// <summary>
    /// Evolve edge weights based on curvature (Ricci flow).
    /// dw/dt = -R * w (positive curvature shrinks, negative expands)
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

                    // Ricci flow: dw/dt = -R * coupling * w
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
    /// Get total scalar curvature (Einstein-Hilbert action integrand).
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
