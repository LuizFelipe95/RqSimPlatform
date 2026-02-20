using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    /// <summary>
    /// Provides a simple kinetic Monte Carlo (KMC) engine that operates on an existing
    /// <see cref="RQGraph"/> instance.  Unlike the discrete time stepping used
    /// throughout the rest of the simulation, the KMC formulation selects
    /// individual state change events based on continuous rate constants and
    /// advances the simulation clock by a random waiting time drawn from an
    /// exponential distribution.  This allows rare events (such as the
    /// spontaneous synthesis or decay of heavy clusters) to be resolved
    /// accurately without the need for extremely small discrete time steps.
    ///
    /// The KMC engine considers three classes of node events:
    /// 1) Excitation: a node in the Rest state becomes Excited due to
    ///    spontaneous fluctuation or stimulation from excited neighbours.
    /// 2) Relaxation: an Excited node transitions to the Refractory state.
    /// 3) Recovery: a Refractory node becomes Rest again after its refractory
    ///    period has elapsed.
    ///
    /// The rate constants used here are derived heuristically from the
    /// probabilities in the discrete UpdateNodeStates() method.  They can be
    /// tuned to match empirical avalanche statistics.  Heavy cluster formation
    /// or decay may also be hooked into this engine by registering custom
    /// kinetic events with their own rates and update handlers.
    ///
    /// Note: This implementation is intentionally conservative: it updates the
    /// RQGraph only at the occurrence of an event and does not modify
    /// topology or correlation weights.  These could be added by calling
    /// RQGraph.UpdateEdges() or UpdateCorrelationWeights() periodically.
    /// </summary>
    public class KineticMonteCarloEngine
    {
        private readonly RQGraph _graph;
        private readonly Random _rng;

        // Simulation clock in arbitrary time units.  KMC steps advance this
        // clock by exponentially distributed waiting times.
        public double Time { get; private set; }

        /// <summary>
        /// Constructs a KMC engine bound to the provided graph.  A
        /// deterministic seed may be supplied to allow reproducible runs.
        /// </summary>
        public KineticMonteCarloEngine(RQGraph graph, int seed = 42)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _rng = new Random(seed);
            Time = 0.0;
        }

        /// <summary>
        /// Advances the simulation until the specified maximum time is
        /// reached.  Events are processed in order of occurrence; after each
        /// event the graph state is updated and the simulation time is
        /// incremented by a random waiting time determined by the total rate.
        /// </summary>
        /// <param name="maxTime">Simulation horizon (inclusive).</param>
        public void Run(double maxTime)
        {
            while (Time < maxTime)
            {
                // Build list of possible events and their rates
                var events = new List<(double rate, Action callback)>();

                // Rest -> Excited events
                for (int i = 0; i < _graph.N; i++)
                {
                    if (_graph.State[i] != NodeState.Rest) continue;
                    double rate = ComputeExcitationRate(i);
                    if (rate > 0.0)
                    {
                        int idx = i; // capture local
                        events.Add((rate, () => ExciteNode(idx)));
                    }
                }
                // Excited -> Refractory events
                for (int i = 0; i < _graph.N; i++)
                {
                    if (_graph.State[i] != NodeState.Excited) continue;
                    // In the discrete simulation this transition always
                    // happens after exactly one step, so use a high rate.
                    double rate = 1.0; // unit rate per time unit
                    int idx = i;
                    events.Add((rate, () => BeginRefractory(idx)));
                }
                // Refractory -> Rest events
                for (int i = 0; i < _graph.N; i++)
                {
                    if (_graph.State[i] != NodeState.Refractory) continue;
                    // Rate inverse to refractory counter; approximate
                    int rCount = _graph.GetRefractoryCounter(i);
                    if (rCount <= 0) rCount = 1;
                    double rate = 1.0 / rCount;
                    int idx = i;
                    events.Add((rate, () => RecoverNode(idx)));
                }
                // Cluster decay kinetic events (rare)
                // Use physics-based excitation instead of manual breaking
                var clusters = _graph.GetStrongCorrelationClusters(_graph.GetAdaptiveHeavyThreshold());
                foreach (var cluster in clusters)
                {
                    if (cluster.Count >= RQGraph.HeavyClusterMinSize)
                    {
                        double rate = 0.1 / cluster.Count; // inverse size
                        events.Add((rate, () => _graph.ExciteOvercorrelatedClusters()));
                    }
                }

                // If no events then abort
                if (events.Count == 0) break;

                double totalRate = events.Sum(e => e.rate);
                // Sample waiting time Δt ~ Exp(totalRate)
                double u = _rng.NextDouble();
                double dt = -Math.Log(1.0 - u) / totalRate;

                // Choose which event occurs
                double r = _rng.NextDouble() * totalRate;
                double accum = 0.0;
                Action selected = null;
                foreach (var (rate, callback) in events)
                {
                    accum += rate;
                    if (r <= accum)
                    {
                        selected = callback;
                        break;
                    }
                }
                // Advance time and execute event
                Time += dt;
                selected?.Invoke();
            }
        }

        /// <summary>
        /// Computes the instantaneous rate for a node in the Rest state to
        /// become Excited.  The rate has two contributions: a baseline
        /// spontaneous term and a neighbour-induced term proportional to the
        /// number of excited neighbours and their correlation weights.  These
        /// heuristics mirror the probabilities in UpdateNodeStates().
        /// </summary>
        private double ComputeExcitationRate(int node)
        {
            // Invariant-based excitation rate: no magic constants; uses local curvature and excited density
            double kNorm = _graph.GetLocalCurvatureNorm(node);
            double rho = _graph.GetLocalExcitedDensity(node);
            double baseRate = 1.0 - Math.Exp(-kNorm);
            double neighbourRate = 1.0 - Math.Exp(-rho);
            double dilation = _graph.GetGravitationalTimeDilation(node);
            return (baseRate + neighbourRate) * dilation;
        }

        /// <summary>
        /// Transitions a node from Rest into the Excited state and resets its
        /// refractory counter.  This method mirrors part of the UpdateNodeStates()
        /// logic but is simplified for the KMC context.
        /// </summary>
        private void ExciteNode(int node)
        {
            if (_graph.State[node] != NodeState.Rest) return;
            _graph.State[node] = NodeState.Excited;
            // Node consumes local energy when excited; heavy excitations
            // could trigger energy redistribution or heavy cluster synthesis
            if (_graph.NodeEnergy != null && node < _graph.NodeEnergy.Length)
            {
                _graph.NodeEnergy[node] += 0.0;
            }
        }

        /// <summary>
        /// Handles the transition from Excited to Refractory.  The refractory
        /// counter is initialised using the graph's RefractorySteps plus a
        /// mass-dependent increment.  During the refractory period the node
        /// cannot excite again.
        /// </summary>
        private void BeginRefractory(int node)
        {
            if (_graph.State[node] != NodeState.Excited) return;
            _graph.State[node] = NodeState.Refractory;
            int extra = 0;
            if (_graph.PhysicsProperties != null && _graph.PhysicsProperties.Length == _graph.N)
            {
                // heavier fermions remain refractory for longer
                extra = (int)Math.Ceiling(_graph.PhysicsProperties[node].Mass);
            }
            _graph.SetRefractoryCounter(node, _graph.DynamicBaseRefractorySteps + extra);
        }

        /// <summary>
        /// Returns a node from the Refractory state to Rest.  Decrements the
        /// refractory counter and resets the node when it reaches zero.
        /// </summary>
        private void RecoverNode(int node)
        {
            if (_graph.State[node] != NodeState.Refractory) return;
            int r = _graph.GetRefractoryCounter(node);
            if (r <= 1)
            {
                _graph.State[node] = NodeState.Rest;
                _graph.SetRefractoryCounter(node, 0);
            }
            else
            {
                _graph.SetRefractoryCounter(node, r - 1);
            }
        }
    }
}