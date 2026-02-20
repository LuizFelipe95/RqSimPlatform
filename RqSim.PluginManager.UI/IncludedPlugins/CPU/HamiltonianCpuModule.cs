using RQSimulation;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.CPU;

/// <summary>
/// CPU module for network Hamiltonian calculations in Quantum Graphity dynamics.
/// 
/// H = H_links + H_nodes + H_cosmological + H_dimensionality
/// 
/// Where:
/// - H_links: Cost of having edges (prefers sparse graphs)
/// - H_nodes: Matter contribution (correlation mass)
/// - H_cosmological: Lambda * V term (prevents collapse/explosion)
/// - H_dimensionality: Penalty for deviating from 4D scaling
/// 
/// RQ-HYPOTHESIS CHECKLIST ITEM 4: 4D Dimension Driver
/// ===================================================
/// Adds penalty term when local volume growth deviates from r^4 scaling.
/// 
/// Based on original RQGraph.Hamiltonian implementation.
/// </summary>
public sealed class HamiltonianCpuModule : CpuPluginBase
{
    private RQGraph? _graph;

    public override string Name => "Network Hamiltonian (CPU)";
    public override string Description => "CPU-based network Hamiltonian with gravity, matter, and 4D emergence";
    public override string Category => "Gravity";
    public override int Priority => 10;

    // Network Hamiltonian parameters
    public double LinkCostCoeff { get; set; } = 0.1;
    public double LengthCostCoeff { get; set; } = 0.05;
    public double MatterCouplingCoeff { get; set; } = 0.2;
    public double CosmologicalConstant { get; set; } = 0.001;
    public double GravitationalConstantInverse { get; set; } = 10.0; // 1/G

    /// <summary>
    /// Last computed total Hamiltonian value.
    /// </summary>
    public double LastHamiltonian { get; private set; }

    public override void Initialize(RQGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        LastHamiltonian = ComputeNetworkHamiltonian();
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        // Update cached Hamiltonian value
        LastHamiltonian = ComputeNetworkHamiltonian();
    }

    /// <summary>
    /// Compute the network Hamiltonian H = H_links + H_nodes.
    /// </summary>
    public double ComputeNetworkHamiltonian()
    {
        if (_graph is null) return 0.0;

        double H_links = 0.0;
        double H_nodes = 0.0;

        int n = _graph.N;

        // H_links: Sum over all edges
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (!_graph.Edges[i, j]) continue;

                double w = _graph.Weights[i, j];

                // Link existence cost (sparse graph preference)
                H_links += LinkCostCoeff * (1.0 - w * w);

                // Length cost (strong links = short effective distance)
                double effectiveLength = w > 1e-10 ? 1.0 / w : 10.0;
                H_links += LengthCostCoeff * effectiveLength;
            }
        }

        // H_nodes: Matter contribution
        var correlationMass = _graph.CorrelationMass;
        if (correlationMass is not null && correlationMass.Length == n)
        {
            for (int i = 0; i < n; i++)
            {
                double mass = correlationMass[i];
                double curvature = GetLocalCurvature(i);
                H_nodes += MatterCouplingCoeff * mass * (1.0 - Math.Abs(curvature));
            }
        }

        return H_links + H_nodes;
    }

    /// <summary>
    /// Compute local Hamiltonian contribution for a single edge (i,j).
    /// Used for efficient delta-energy calculation.
    /// </summary>
    public double ComputeLocalHamiltonian(int i, int j)
    {
        if (_graph is null) return 0.0;

        double H_local = 0.0;

        // 1. Edge energy contribution
        if (_graph.Edges[i, j])
        {
            double w = _graph.Weights[i, j];

            H_local += LinkCostCoeff * (1.0 - w * w);

            double effectiveLength = w > 1e-10 ? 1.0 / w : 10.0;
            H_local += LengthCostCoeff * effectiveLength;

            // 2. Gravity contribution: Ricci curvature
            double ricci = CalculateApproximateRicci(i, j);
            H_local -= GravitationalConstantInverse * ricci;
        }

        // 3. Matter coupling for affected nodes
        var correlationMass = _graph.CorrelationMass;
        if (correlationMass is not null && correlationMass.Length == _graph.N)
        {
            double mass_i = correlationMass[i];
            double curvature_i = GetLocalCurvature(i);
            H_local += MatterCouplingCoeff * mass_i * (1.0 - Math.Abs(curvature_i));

            double mass_j = correlationMass[j];
            double curvature_j = GetLocalCurvature(j);
            H_local += MatterCouplingCoeff * mass_j * (1.0 - Math.Abs(curvature_j));
        }

        // 4. Cosmological constant term
        double localVolume = 0.0;
        foreach (int k in _graph.Neighbors(i))
            localVolume += _graph.Weights[i, k];
        foreach (int k in _graph.Neighbors(j))
            localVolume += _graph.Weights[j, k];
        if (_graph.Edges[i, j])
            localVolume -= _graph.Weights[i, j];

        H_local += CosmologicalConstant * localVolume;

        // 5. Dimensionality penalty (4D emergence)
        H_local += ComputeDimensionalityPenalty(i, j);

        return H_local;
    }

    /// <summary>
    /// Calculate approximate Ricci curvature for edge (i,j).
    /// </summary>
    public double CalculateApproximateRicci(int i, int j)
    {
        if (_graph is null || !_graph.Edges[i, j]) return 0.0;

        double w_e = _graph.Weights[i, j];
        double w_i = 0.0;
        double w_j = 0.0;

        foreach (int n in _graph.Neighbors(i)) w_i += _graph.Weights[i, n];
        foreach (int n in _graph.Neighbors(j)) w_j += _graph.Weights[j, n];

        w_i -= w_e;
        w_j -= w_e;

        // Count triangles
        double triangles = 0.0;
        foreach (int n_i in _graph.Neighbors(i))
        {
            if (n_i == j) continue;
            if (_graph.Edges[j, n_i])
            {
                double w_in = _graph.Weights[i, n_i];
                double w_jn = _graph.Weights[j, n_i];
                triangles += Math.Sqrt(w_in * w_jn);
            }
        }

        return w_e * (triangles - (w_i + w_j) * 0.1);
    }

    private double GetLocalCurvature(int node)
    {
        if (_graph is null) return 0.0;

        double totalCurvature = 0.0;
        int edgeCount = 0;

        foreach (int neighbor in _graph.Neighbors(node))
        {
            totalCurvature += CalculateApproximateRicci(node, neighbor);
            edgeCount++;
        }

        return edgeCount > 0 ? totalCurvature / edgeCount : 0.0;
    }

    private double ComputeDimensionalityPenalty(int i, int j)
    {
        if (_graph is null) return 0.0;

        // Check if natural dimension emergence is enabled
        if (PhysicsConstants.EnableNaturalDimensionEmergence)
        {
            return 0.0;
        }

        int v1_i = _graph.Neighbors(i).Count();
        int v1_j = _graph.Neighbors(j).Count();

        if (v1_i < 2 || v1_j < 2)
            return 0.0;

        int v2_i = CountSecondNeighbors(i);

        double growthRatio_i = (double)v2_i / v1_i;

        // Target ratio for 4D: V(r=2)/V(r=1) ? 2^3 = 8
        double deviation = growthRatio_i - PhysicsConstants.TargetGrowthRatio4D;

        return PhysicsConstants.DimensionPenalty * deviation * deviation;
    }

    private int CountSecondNeighbors(int node)
    {
        if (_graph is null) return 0;

        var visited = new HashSet<int> { node };
        var firstNeighbors = new List<int>();

        foreach (int n1 in _graph.Neighbors(node))
        {
            visited.Add(n1);
            firstNeighbors.Add(n1);
        }

        int secondNeighborCount = 0;
        foreach (int n1 in firstNeighbors)
        {
            foreach (int n2 in _graph.Neighbors(n1))
            {
                if (!visited.Contains(n2))
                {
                    visited.Add(n2);
                    secondNeighborCount++;
                }
            }
        }

        return secondNeighborCount;
    }

    public override void Cleanup()
    {
        _graph = null;
        LastHamiltonian = 0.0;
    }
}
