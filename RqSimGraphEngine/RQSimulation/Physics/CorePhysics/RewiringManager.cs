using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation.Physics
{
    /// <summary>
    /// Manages topological rewiring of the relational graph with causal
    /// constraints. Implements checklist item #6. Ensures that
    /// rewiring does not break connectivity (no fragmentation) and
    /// restricts creation of new edges to nodes that are already near
    /// neighbours in the graph. Supports energy-based acceptance via
    /// Metropolis criterion.
    /// </summary>
    /// <typeparam name="TNode">Type representing a graph node.</typeparam>
    public sealed class RewiringManager<TNode> where TNode : notnull
    {
        private readonly Func<TNode, IEnumerable<TNode>> _getNeighbours;
        private readonly Func<TNode, TNode, double> _getEdgeWeight;
        private readonly Action<TNode, TNode, bool> _setEdge;
        private readonly Func<double> _getTemperature;
        private readonly EnergyBook? _energyBook;
        private readonly int _frequency;
        private readonly Random _rng;
        private int _stepCounter;

        /// <summary>
        /// Maximum graph distance for edge creation (causal locality).
        /// </summary>
        public int MaxEdgeCreationDistance { get; set; } = 2;

        /// <summary>
        /// Initializes a new instance of the <see cref="RewiringManager{TNode}"/> class.
        /// </summary>
        /// <param name="getNeighbours">Function returning the neighbours of a node.</param>
        /// <param name="getEdgeWeight">Function returning the weight of an edge between two nodes (0 if none).</param>
        /// <param name="setEdge">Action to add or remove an edge between two nodes (third parameter true to add, false to remove).</param>
        /// <param name="getTemperature">Delegate returning current graph temperature for Metropolis decisions.</param>
        /// <param name="energyBook">Optional energy book for conservation checks.</param>
        /// <param name="frequency">Number of steps between rewiring operations.</param>
        /// <param name="seed">Random seed for reproducibility.</param>
        public RewiringManager(
            Func<TNode, IEnumerable<TNode>> getNeighbours,
            Func<TNode, TNode, double> getEdgeWeight,
            Action<TNode, TNode, bool> setEdge,
            Func<double> getTemperature,
            EnergyBook? energyBook = null,
            int frequency = 200,
            int seed = 42)
        {
            ArgumentNullException.ThrowIfNull(getNeighbours);
            ArgumentNullException.ThrowIfNull(getEdgeWeight);
            ArgumentNullException.ThrowIfNull(setEdge);
            ArgumentNullException.ThrowIfNull(getTemperature);

            _getNeighbours = getNeighbours;
            _getEdgeWeight = getEdgeWeight;
            _setEdge = setEdge;
            _getTemperature = getTemperature;
            _energyBook = energyBook;
            _frequency = frequency;
            _rng = new Random(seed);
            _stepCounter = 0;
        }

        /// <summary>
        /// Attempts to rewire the graph every <see cref="_frequency"/> steps.
        /// Respects causal locality and energy constraints.
        /// </summary>
        /// <param name="allNodes">Collection of all nodes in the graph.</param>
        /// <returns>True if a rewiring was performed.</returns>
        public bool MaybeRewire(IEnumerable<TNode> allNodes)
        {
            _stepCounter++;
            if (_stepCounter % _frequency != 0)
                return false;

            var nodes = allNodes.ToList();
            if (nodes.Count < 2) return false;

            // Choose two nodes at random
            var nodeA = nodes[_rng.Next(nodes.Count)];
            var nodeB = nodes[_rng.Next(nodes.Count)];
            if (EqualityComparer<TNode>.Default.Equals(nodeA, nodeB)) return false;

            double currentWeight = _getEdgeWeight(nodeA, nodeB);
            bool proposeAdd = currentWeight <= 0.0;

            // Causal locality: only add edges between nodes within MaxEdgeCreationDistance
            if (proposeAdd && !IsWithinDistance(nodeA, nodeB, MaxEdgeCreationDistance))
                return false;

            // Ensure removal does not disconnect graph
            if (!proposeAdd && !IsRemovalSafe(nodeA, nodeB))
                return false;

            return TryApplyRewiring(nodeA, nodeB, proposeAdd);
        }

        /// <summary>
        /// Forces a rewiring attempt on a specific edge.
        /// </summary>
        /// <param name="nodeA">First node.</param>
        /// <param name="nodeB">Second node.</param>
        /// <param name="proposeAdd">True to add edge, false to remove.</param>
        /// <returns>True if rewiring was accepted.</returns>
        public bool ForceRewire(TNode nodeA, TNode nodeB, bool proposeAdd)
        {
            if (proposeAdd && !IsWithinDistance(nodeA, nodeB, MaxEdgeCreationDistance))
                return false;

            if (!proposeAdd && !IsRemovalSafe(nodeA, nodeB))
                return false;

            return TryApplyRewiring(nodeA, nodeB, proposeAdd);
        }

        /// <summary>
        /// Attempts to apply a rewiring with energy-based Metropolis acceptance.
        /// </summary>
        private bool TryApplyRewiring(TNode nodeA, TNode nodeB, bool proposeAdd)
        {
            // Apply the change
            _setEdge(nodeA, nodeB, proposeAdd);

            // Check energy conservation if book is available
            if (_energyBook != null)
            {
                if (!_energyBook.TryCheckEnergyConservation(out double relativeChange))
                {
                    // Energy violated significantly - use Metropolis criterion
                    double temperature = _getTemperature();
                    if (temperature > 0)
                    {
                        double acceptance = Math.Exp(-relativeChange / temperature);
                        if (_rng.NextDouble() > acceptance)
                        {
                            // Reject: revert change
                            _setEdge(nodeA, nodeB, !proposeAdd);
                            return false;
                        }
                    }
                    else
                    {
                        // T=0: strict conservation, reject
                        _setEdge(nodeA, nodeB, !proposeAdd);
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Determines if two nodes are within a specified graph distance
        /// using breadth-first search.
        /// </summary>
        private bool IsWithinDistance(TNode start, TNode target, int maxDepth)
        {
            if (maxDepth <= 0) return false;

            var visited = new HashSet<TNode> { start };
            var frontier = new Queue<(TNode node, int depth)>();
            frontier.Enqueue((start, 0));

            while (frontier.Count > 0)
            {
                var (node, depth) = frontier.Dequeue();
                if (depth >= maxDepth) continue;

                foreach (var nbr in _getNeighbours(node))
                {
                    if (EqualityComparer<TNode>.Default.Equals(nbr, target))
                        return true;

                    if (visited.Add(nbr))
                        frontier.Enqueue((nbr, depth + 1));
                }
            }

            return false;
        }

        /// <summary>
        /// Determines if removing the edge between two nodes would leave
        /// the graph connected. Uses DFS to check reachability.
        /// </summary>
        private bool IsRemovalSafe(TNode a, TNode b)
        {
            // Temporarily remove edge
            _setEdge(a, b, false);

            // Check connectivity via DFS
            var visited = new HashSet<TNode>();
            var stack = new Stack<TNode>();
            stack.Push(a);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!visited.Add(node)) continue;

                foreach (var nbr in _getNeighbours(node))
                    stack.Push(nbr);
            }

            bool connected = visited.Contains(b);

            // Restore edge
            _setEdge(a, b, true);

            return connected;
        }
    }
}
