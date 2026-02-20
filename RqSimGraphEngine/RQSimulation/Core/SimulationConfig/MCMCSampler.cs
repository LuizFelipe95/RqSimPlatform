using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RQSimulation
{
    /// <summary>
    /// Topology mutation type for Metropolis-Hastings q_ratio computation.
    /// </summary>
    public enum TopologyMutationType
    {
        /// <summary>Add a new edge to the graph.</summary>
        AddEdge = 0,
        /// <summary>Remove an existing edge from the graph.</summary>
        RemoveEdge = 1,
        /// <summary>Symmetric weight perturbation on an existing edge.</summary>
        WeightChange = 2
    }

    /// <summary>
    /// MCMC sampler for path integral quantum gravity.
    /// Samples configurations satisfying Wheeler-DeWitt constraint.
    /// 
    /// PHYSICS:
    /// - Instead of time evolution, we sample configurations from the partition function
    /// - Z = ? D[g] exp(-S_E[g]) where S_E is the Euclidean action
    /// - Metropolis-Hastings acceptance: P_accept = min(1, exp(-?S) · q_ratio)
    ///   where q_ratio = q(x'?x) / q(x?x') corrects for asymmetric proposals
    /// </summary>
    public sealed class MCMCSampler
    {
        private readonly RQGraph _graph;
        private readonly Random _rng;
        private double _currentAction;
        
        // Statistics
        public int AcceptedMoves { get; private set; }
        public int RejectedMoves { get; private set; }
        public double AcceptanceRate => AcceptedMoves + RejectedMoves > 0 
            ? (double)AcceptedMoves / (AcceptedMoves + RejectedMoves) : 0.0;
        
        public MCMCSampler(RQGraph graph, int seed = 42)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _rng = new Random(seed);
            _currentAction = CalculateEuclideanAction();
        }

        /// <summary>
        /// Compute the Hastings ratio q(x'?x)/q(x?x') for a topology mutation.
        /// Corrects for asymmetric proposal distributions in add/remove moves.
        /// </summary>
        /// <param name="mutationType">Type of proposed topology change.</param>
        /// <param name="existingEdgeCount">Number of existing edges before the move.</param>
        /// <param name="missingEdgeCount">Number of missing (possible but absent) edges before the move.</param>
        /// <returns>The Hastings correction factor q_ratio.</returns>
        public static double EvaluateHastingsRatio(
            TopologyMutationType mutationType,
            int existingEdgeCount,
            int missingEdgeCount)
        {
            return mutationType switch
            {
                // Add edge: forward picks from N_missing, backward removes from (N_existing+1)
                TopologyMutationType.AddEdge => missingEdgeCount > 0
                    ? (double)missingEdgeCount / (existingEdgeCount + 1)
                    : 1.0,

                // Remove edge: forward picks from N_existing, backward adds from (N_missing+1)
                TopologyMutationType.RemoveEdge => existingEdgeCount > 0
                    ? (double)existingEdgeCount / (missingEdgeCount + 1)
                    : 1.0,

                // Symmetric weight perturbation: no correction needed
                TopologyMutationType.WeightChange => 1.0,

                _ => 1.0
            };
        }

        /// <summary>
        /// Count existing edges in the graph.
        /// </summary>
        private int CountExistingEdges()
        {
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
        /// Calculate Euclidean action for current configuration.
        /// S_E = S_gravity + S_matter + S_gauge
        /// </summary>
        public double CalculateEuclideanAction()
        {
            // Use the constraint-weighted Hamiltonian as the effective Euclidean action
            // This ensures we sample configurations near the constraint surface H ? 0
            return _graph.ComputeConstraintWeightedHamiltonian();
        }
        
        /// <summary>
        /// Sample configuration space using Metropolis-Hastings.
        /// Applies the Hastings correction q_ratio for asymmetric topology proposals.
        /// </summary>
        public void SampleConfigurationSpace(int samples, Action<int, RQGraph>? onSample = null)
        {
            for (int i = 0; i < samples; i++)
            {
                var (deltaAction, mutationType, existingEdges, missingEdges, applyMove, _) = ProposeMove();

                double qRatio = EvaluateHastingsRatio(mutationType, existingEdges, missingEdges);

                // Metropolis-Hastings acceptance criterion
                // P_accept = min(1, exp(-?S) · q_ratio)
                bool accept;
                double acceptArg = Math.Exp(-deltaAction) * qRatio;
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
        /// Pool sizes (existingEdges, missingEdges) are needed for Hastings ratio computation.
        /// </summary>
        public (double deltaAction, TopologyMutationType mutationType, int existingEdges, int missingEdges, Action applyMove, Action revertMove) ProposeMove()
        {
            // Count edge pools for Hastings ratio
            int existingEdges = CountExistingEdges();
            int maxPossibleEdges = _graph.N * (_graph.N - 1) / 2;
            int missingEdges = maxPossibleEdges - existingEdges;

            // 1. Select random move type: Add Edge, Remove Edge, Change Weight
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
                newWeight = currentWeight + (_rng.NextDouble() - 0.5) * 0.1;
                newWeight = Math.Clamp(newWeight, 0.0, 1.0);
                if (newWeight < 0.01)
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

            // Temporarily apply to compute exact ?S
            _graph.Edges[i, j] = newExists;
            _graph.Edges[j, i] = newExists;
            _graph.Weights[i, j] = newWeight;
            _graph.Weights[j, i] = newWeight;

            double newAction = CalculateEuclideanAction();
            double delta = newAction - _currentAction;

            // Revert immediately
            _graph.Edges[i, j] = edgeExists;
            _graph.Edges[j, i] = edgeExists;
            _graph.Weights[i, j] = currentWeight;
            _graph.Weights[j, i] = currentWeight;

            Action apply = () =>
            {
                _graph.Edges[i, j] = newExists;
                _graph.Edges[j, i] = newExists;
                _graph.Weights[i, j] = newWeight;
                _graph.Weights[j, i] = newWeight;
            };

            Action revert = () =>
            {
                _graph.Edges[i, j] = edgeExists;
                _graph.Edges[j, i] = edgeExists;
                _graph.Weights[i, j] = currentWeight;
                _graph.Weights[j, i] = currentWeight;
            };

            return (delta, mutationType, existingEdges, missingEdges, apply, revert);
        }
    }
}
