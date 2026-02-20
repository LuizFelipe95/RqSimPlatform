using RQSimulation;
using RQSimulation.Core.Plugins;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.CPU;

/// <summary>
/// CPU module for MCMC sampler for path integral quantum gravity.
/// Samples configurations satisfying Wheeler-DeWitt constraint.
/// 
/// PHYSICS:
/// - Instead of time evolution, we sample configurations from the partition function
/// - Z = ? D[g] exp(-S_E[g]) where S_E is the Euclidean action
/// - Metropolis-Hastings acceptance: P_accept = min(1, exp(-?S) · q_ratio)
///   where q_ratio = q(x'?x) / q(x?x') corrects for asymmetric proposals
/// 
/// Based on original MCMCSampler implementation.
/// </summary>
public sealed class MCMCSamplerCpuModule : CpuPluginBase, IDynamicPhysicsModule
{
    private RQGraph? _graph;
    private Random _rng = new(42);
    private double _currentAction;

    public override string Name => "MCMC Sampler (CPU)";
    public override string Description => "CPU-based Markov Chain Monte Carlo for path integral quantum gravity";
    public override string Category => "MCMC";
    public override int Priority => 45;

    /// <summary>
    /// Number of MCMC samples per simulation step.
    /// </summary>
    public int SamplesPerStep { get; set; } = 10;

    /// <summary>
    /// Inverse temperature (beta = 1/kT).
    /// </summary>
    public double Beta { get; set; } = 1.0;

    /// <summary>
    /// Weight perturbation magnitude for change moves.
    /// </summary>
    public double WeightPerturbation { get; set; } = 0.1;

    /// <summary>
    /// Minimum weight threshold below which edge is removed.
    /// </summary>
    public double MinWeight { get; set; } = 0.01;

    // Statistics
    public int AcceptedMoves { get; private set; }
    public int RejectedMoves { get; private set; }
    public double AcceptanceRate => AcceptedMoves + RejectedMoves > 0
        ? (double)AcceptedMoves / (AcceptedMoves + RejectedMoves) : 0.0;

    public override void Initialize(RQGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _rng = new Random(42);
        _currentAction = CalculateEuclideanAction();
        AcceptedMoves = 0;
        RejectedMoves = 0;
    }

    /// <summary>
    /// Calculate Euclidean action for current configuration.
    /// S_E = S_gravity + S_matter + S_gauge
    /// </summary>
    public double CalculateEuclideanAction()
    {
        if (_graph is null) return 0.0;

        // Use the constraint-weighted Hamiltonian as the effective Euclidean action
        return _graph.ComputeConstraintWeightedHamiltonian();
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (_graph is null) return;

        // Perform MCMC sampling
        SampleConfigurationSpace(SamplesPerStep, null);
    }

    /// <summary>
    /// Sample configuration space using Metropolis-Hastings.
    /// Applies the Hastings correction q_ratio for asymmetric topology proposals.
    /// </summary>
    public void SampleConfigurationSpace(int samples, Action<int, RQGraph>? onSample = null)
    {
        if (_graph is null) return;

        for (int i = 0; i < samples; i++)
        {
            var (deltaAction, mutationType, existingEdges, missingEdges, applyMove, _) = ProposeMove();

            double qRatio = MCMCSampler.EvaluateHastingsRatio(mutationType, existingEdges, missingEdges);

            // Metropolis-Hastings acceptance criterion
            // P_accept = min(1, exp(-? · ?S) · q_ratio)
            bool accept;
            double acceptArg = Math.Exp(-Beta * deltaAction) * qRatio;
            if (acceptArg >= 1.0)
            {
                accept = true;
            }
            else
            {
                accept = _rng.NextDouble() < acceptArg;
            }

            if (accept)
            {
                applyMove();
                _currentAction += deltaAction;
                AcceptedMoves++;
            }
            else
            {
                RejectedMoves++;
            }

            onSample?.Invoke(i, _graph);
        }
    }

    /// <summary>
    /// Propose topology change (edge addition/removal/weight change).
    /// Returns proposed action change, mutation type, pool sizes, and apply/revert actions.
    /// </summary>
    public (double deltaAction, TopologyMutationType mutationType, int existingEdges, int missingEdges, Action applyMove, Action revertMove) ProposeMove()
    {
        if (_graph is null)
            return (0, TopologyMutationType.WeightChange, 0, 0, () => { }, () => { });

        // Count edge pools for Hastings ratio
        int existingEdges = CountExistingEdges();
        int maxPossibleEdges = _graph.N * (_graph.N - 1) / 2;
        int missingEdges = maxPossibleEdges - existingEdges;

        // Select random move type: Add Edge, Remove Edge, Change Weight
        int moveType = _rng.Next(3);

        int i = _rng.Next(_graph.N);
        int j = _rng.Next(_graph.N);
        while (i == j) j = _rng.Next(_graph.N);

        // Ensure i < j for consistency
        if (i > j) (i, j) = (j, i);

        bool edgeExists = _graph.Edges[i, j];
        double currentWeight = _graph.Weights[i, j];

        // Adjust move type based on existence
        if (moveType == 0 && edgeExists) moveType = 2; // Can't add, so change weight
        if (moveType == 1 && !edgeExists) moveType = 0; // Can't remove, so add

        double newWeight = currentWeight;
        bool newExists = edgeExists;
        TopologyMutationType mutationType;

        if (moveType == 0) // Add Edge
        {
            newExists = true;
            newWeight = _rng.NextDouble();
            mutationType = TopologyMutationType.AddEdge;
        }
        else if (moveType == 1) // Remove Edge
        {
            newExists = false;
            newWeight = 0.0;
            mutationType = TopologyMutationType.RemoveEdge;
        }
        else // Change Weight
        {
            newWeight = currentWeight + (_rng.NextDouble() - 0.5) * WeightPerturbation;
            newWeight = Math.Clamp(newWeight, 0.0, 1.0);
            if (newWeight < MinWeight)
            {
                newExists = false;
                newWeight = 0.0;
                mutationType = TopologyMutationType.RemoveEdge;
            }
            else
            {
                mutationType = TopologyMutationType.WeightChange;
            }
        }

        // Apply move temporarily
        _graph.Edges[i, j] = newExists;
        _graph.Edges[j, i] = newExists;
        _graph.Weights[i, j] = newWeight;
        _graph.Weights[j, i] = newWeight;

        double newAction = CalculateEuclideanAction();
        double delta = newAction - _currentAction;

        // Revert immediately so we return the "proposal"
        _graph.Edges[i, j] = edgeExists;
        _graph.Edges[j, i] = edgeExists;
        _graph.Weights[i, j] = currentWeight;
        _graph.Weights[j, i] = currentWeight;

        // Capture values for closures
        int ci = i, cj = j;
        bool cNewExists = newExists, cEdgeExists = edgeExists;
        double cNewWeight = newWeight, cCurrentWeight = currentWeight;

        Action apply = () =>
        {
            _graph.Edges[ci, cj] = cNewExists;
            _graph.Edges[cj, ci] = cNewExists;
            _graph.Weights[ci, cj] = cNewWeight;
            _graph.Weights[cj, ci] = cNewWeight;
        };

        Action revert = () =>
        {
            _graph.Edges[ci, cj] = cEdgeExists;
            _graph.Edges[cj, ci] = cEdgeExists;
            _graph.Weights[ci, cj] = cCurrentWeight;
            _graph.Weights[cj, ci] = cCurrentWeight;
        };

        return (delta, mutationType, existingEdges, missingEdges, apply, revert);
    }

    /// <summary>
    /// Count existing edges in the graph.
    /// </summary>
    private int CountExistingEdges()
    {
        if (_graph is null) return 0;

        int count = 0;
        for (int i = 0; i < _graph.N; i++)
        {
            for (int j = i + 1; j < _graph.N; j++)
            {
                if (_graph.Edges[i, j]) count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Reset MCMC statistics.
    /// </summary>
    public void ResetStatistics()
    {
        AcceptedMoves = 0;
        RejectedMoves = 0;
    }

    /// <summary>
    /// Set random seed for reproducibility.
    /// </summary>
    public void SetSeed(int seed)
    {
        _rng = new Random(seed);
    }

    /// <summary>
    /// Updates MCMC parameters from pipeline's per-frame dynamic configuration.
    /// </summary>
    public void UpdateParameters(in DynamicPhysicsParams parameters)
    {
        Beta = parameters.McmcBeta;
        SamplesPerStep = parameters.McmcStepsPerCall;
        WeightPerturbation = parameters.McmcWeightPerturbation;
    }

    public override void Cleanup()
    {
        _graph = null;
        ResetStatistics();
    }
}
