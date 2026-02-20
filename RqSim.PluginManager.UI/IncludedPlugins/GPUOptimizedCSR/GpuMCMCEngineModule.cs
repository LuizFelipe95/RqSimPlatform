using RqSimEngineApi.Contracts;
using RQSimulation;
using RQSimulation.Core.Plugins;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.GPUOptimizedCSR;

/// <summary>
/// GPU-accelerated MCMC engine module for path integral quantum gravity.
/// 
/// RQ-HYPOTHESIS STAGE 4: MCMC SAMPLING
/// ====================================
/// Markov Chain Monte Carlo for sampling configurations from:
///   Z = ? D[g] exp(-S_E[g])
/// 
/// PARALLELIZATION STRATEGY:
/// ========================
/// MCMC moves are inherently sequential, but we parallelize:
/// 1. Euclidean action computation (sum over edges/nodes)
/// 2. Batched proposal evaluation (compute ?S for many proposals)
/// 3. Parallel Tempering (optional: K replicas at different T)
/// 
/// EXECUTION STAGE: Forces
/// ======================
/// MCMC runs during the Forces stage to propose topology changes.
/// These changes affect the graph structure which other modules then use.
/// 
/// GPU CONTEXT INJECTION:
/// =====================
/// This module supports shared GPU context via SetDeviceContext().
/// Currently uses CPU fallback, but can be upgraded to GPU when context is available.
/// 
/// NOTE: This version uses simple energy-based Metropolis criterion.
/// For gauge-invariant version, see the refactored GpuMCMCEngine in main codebase.
/// </summary>
public sealed class GpuMCMCEngineModule : GpuPluginBase, IDynamicPhysicsModule
{
    private RQGraph? _graph;
    private Random _rng = new();

    // Edge data (CPU buffers for now, GPU acceleration when context is available)
    private double[]? _weightsCpu;
    private int[]? _edgeExistsCpu;
    private int _edgeCount;
    private double _currentAction;

    // Statistics
    private int _acceptedMoves;
    private int _rejectedMoves;

    public override string Name => "GPU MCMC Engine";
    public override string Description => "GPU-accelerated MCMC with Metropolis criterion for path integral sampling";
    public override string Category => "MCMC";
    
    /// <summary>
    /// MCMC runs during Forces stage to propose topology changes before integration.
    /// </summary>
    public override ExecutionStage Stage => ExecutionStage.Forces;
    
    public override int Priority => 45;

    /// <summary>
    /// Inverse temperature (beta = 1/kT).
    /// </summary>
    public double Beta { get; set; } = 1.0;

    /// <summary>
    /// Link cost coefficient for Euclidean action.
    /// </summary>
    public double LinkCostCoeff { get; set; } = 0.1;

    /// <summary>
    /// Weight perturbation magnitude for change moves.
    /// </summary>
    public double WeightPerturbation { get; set; } = 0.1;

    /// <summary>
    /// Minimum weight threshold below which edge is removed.
    /// </summary>
    public double MinWeight { get; set; } = 0.01;

    /// <summary>
    /// Number of MCMC steps per simulation step.
    /// </summary>
    public int StepsPerCall { get; set; } = 10;

    /// <summary>
    /// Acceptance rate for monitoring convergence.
    /// </summary>
    public double AcceptanceRate => _acceptedMoves + _rejectedMoves > 0
        ? (double)_acceptedMoves / (_acceptedMoves + _rejectedMoves) : 0.0;

    public override void Initialize(RQGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _rng = new Random();

        // Build edge list from graph
        BuildEdgeList();

        // Calculate initial action
        _currentAction = CalculateEuclideanAction();

        _acceptedMoves = 0;
        _rejectedMoves = 0;
        
        // Log GPU context status
        if (HasDeviceContext)
        {
            System.Diagnostics.Trace.WriteLine($"[{Name}] GPU context available, double precision: {DeviceContext!.IsDoublePrecisionSupported}");
        }
        else
        {
            System.Diagnostics.Trace.WriteLine($"[{Name}] No GPU context, using CPU fallback");
        }
    }

    private void BuildEdgeList()
    {
        if (_graph is null) return;

        // Count edges
        int count = 0;
        for (int i = 0; i < _graph.N; i++)
        {
            for (int j = i + 1; j < _graph.N; j++)
            {
                if (_graph.Edges[i, j])
                    count++;
            }
        }

        _edgeCount = count > 0 ? count : _graph.N * (_graph.N - 1) / 2;

        // Allocate buffers
        _weightsCpu = new double[_edgeCount];
        _edgeExistsCpu = new int[_edgeCount];

        // Populate edge data
        int idx = 0;
        for (int i = 0; i < _graph.N && idx < _edgeCount; i++)
        {
            for (int j = i + 1; j < _graph.N && idx < _edgeCount; j++)
            {
                _weightsCpu[idx] = _graph.Weights[i, j];
                _edgeExistsCpu[idx] = _graph.Edges[i, j] ? 1 : 0;
                idx++;
            }
        }
    }

    private double CalculateEuclideanAction()
    {
        if (_weightsCpu is null || _edgeExistsCpu is null) return 0.0;

        double action = 0.0;
        for (int i = 0; i < _edgeCount; i++)
        {
            if (_edgeExistsCpu[i] == 0) continue;
            double w = _weightsCpu[i];
            action += LinkCostCoeff * (1.0 - w * w);
        }
        return action;
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (_graph is null || _weightsCpu is null || _edgeExistsCpu is null) return;

        // Perform MCMC steps
        for (int step = 0; step < StepsPerCall; step++)
        {
            DoMetropolisStep();
        }

        // Sync back to graph
        SyncToGraph();
    }

    /// <summary>
    /// Perform one Metropolis-Hastings step with Hastings ratio correction.
    /// NOTE: This version does NOT check gauge invariance.
    /// </summary>
    private void DoMetropolisStep()
    {
        if (_weightsCpu is null || _edgeExistsCpu is null || _edgeCount == 0) return;

        // Select random edge to modify
        int edgeIdx = _rng.Next(_edgeCount);
        double currentWeight = _weightsCpu[edgeIdx];

        // Propose new weight
        double proposedWeight;
        int moveType = _rng.Next(3);
        TopologyMutationType mutationType;

        if (_edgeExistsCpu[edgeIdx] == 0)
        {
            // Edge doesn't exist - propose adding
            proposedWeight = _rng.NextDouble();
            mutationType = TopologyMutationType.AddEdge;
        }
        else if (moveType == 0)
        {
            // Remove edge
            proposedWeight = 0.0;
            mutationType = TopologyMutationType.RemoveEdge;
        }
        else
        {
            // Perturb weight
            proposedWeight = currentWeight + (_rng.NextDouble() - 0.5) * WeightPerturbation;
            proposedWeight = Math.Clamp(proposedWeight, 0.0, 1.0);
            if (proposedWeight < MinWeight)
            {
                proposedWeight = 0.0;
                mutationType = TopologyMutationType.RemoveEdge;
            }
            else
            {
                mutationType = TopologyMutationType.WeightChange;
            }
        }

        // Compute ?S (local approximation for speed)
        double deltaS = LinkCostCoeff * (currentWeight * currentWeight - proposedWeight * proposedWeight);

        // Compute Hastings ratio for asymmetric topology proposals
        int existingEdges = CountExistingEdges();
        int missingEdges = _edgeCount - existingEdges;
        double qRatio = MCMCSampler.EvaluateHastingsRatio(mutationType, existingEdges, missingEdges);

        // Metropolis-Hastings criterion: P_accept = min(1, exp(-?·?S) · q_ratio)
        bool accept;
        double acceptArg = Math.Exp(-Beta * deltaS) * qRatio;
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
            _weightsCpu[edgeIdx] = proposedWeight;
            _edgeExistsCpu[edgeIdx] = proposedWeight >= MinWeight ? 1 : 0;
            _currentAction += deltaS;
            _acceptedMoves++;
        }
        else
        {
            _rejectedMoves++;
        }
    }

    /// <summary>
    /// Count edges that currently exist in the edge array.
    /// </summary>
    private int CountExistingEdges()
    {
        if (_edgeExistsCpu is null) return 0;

        int count = 0;
        for (int e = 0; e < _edgeCount; e++)
        {
            if (_edgeExistsCpu[e] != 0) count++;
        }

        return count;
    }

    private void SyncToGraph()
    {
        if (_graph is null || _weightsCpu is null || _edgeExistsCpu is null) return;

        int idx = 0;
        for (int i = 0; i < _graph.N && idx < _edgeCount; i++)
        {
            for (int j = i + 1; j < _graph.N && idx < _edgeCount; j++)
            {
                bool exists = _edgeExistsCpu[idx] != 0;
                _graph.Edges[i, j] = exists;
                _graph.Edges[j, i] = exists;
                _graph.Weights[i, j] = _weightsCpu[idx];
                _graph.Weights[j, i] = _weightsCpu[idx];
                idx++;
            }
        }
    }

    /// <summary>
    /// Reset statistics.
    /// </summary>
    public void ResetStatistics()
    {
        _acceptedMoves = 0;
        _rejectedMoves = 0;
    }

    /// <summary>
    /// Updates MCMC parameters from pipeline's per-frame dynamic configuration.
    /// </summary>
    public void UpdateParameters(in DynamicPhysicsParams parameters)
    {
        Beta = parameters.McmcBeta;
        StepsPerCall = parameters.McmcStepsPerCall;
        WeightPerturbation = parameters.McmcWeightPerturbation;
    }

    protected override void DisposeCore()
    {
        _weightsCpu = null;
        _edgeExistsCpu = null;
        _graph = null;
    }
}
