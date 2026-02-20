using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    /// <summary>
    /// Causal Topology Dynamics: Only allow topology changes within causal neighborhoods
    /// Ensures causality is preserved during graph evolution
    /// </summary>
    public partial class RQGraph
    {
        /// <summary>
        /// Get all nodes within causal future of node i within time dt
        /// Uses graph distance and speed of light constraint
        /// </summary>
        public HashSet<int> GetCausalNeighborhood(int i, double dt)
        {
            var reachable = new HashSet<int>();
            var queue = new Queue<(int node, double time)>();
            queue.Enqueue((i, 0));
            reachable.Add(i);

            double speedOfLight = PhysicsConstants.SpeedOfLight;
            int maxHops = PhysicsConstants.MaxCausalDistance;
            int hopCount = 0;

            while (queue.Count > 0 && hopCount < maxHops)
            {
                int currentLevel = queue.Count;
                hopCount++;

                for (int _ = 0; _ < currentLevel; _++)
                {
                    var (node, time) = queue.Dequeue();

                    foreach (int neighbor in Neighbors(node))
                    {
                        if (reachable.Contains(neighbor)) continue;

                        // Edge length from weight: length = -log(weight)
                        double edgeLength = EdgeLength(node, neighbor);
                        double travelTime = edgeLength / speedOfLight;
                        double newTime = time + travelTime;

                        if (newTime <= dt)
                        {
                            reachable.Add(neighbor);
                            queue.Enqueue((neighbor, newTime));
                        }
                    }
                }
            }

            return reachable;
        }

        /// <summary>
        /// Check if two nodes can be causally connected within time dt
        /// </summary>
        public bool IsCausallyConnectedForRewiring(int i, int j, double dt)
        {
            // Quick check: if already neighbors, always allowed
            if (Edges[i, j]) return true;

            // Quick check: if too far in spectral space, reject
            if (_spectralX != null && i < _spectralX.Length && j < _spectralX.Length)
            {
                double dx = _spectralX[i] - _spectralX[j];
                double dy = _spectralY != null && i < _spectralY.Length && j < _spectralY.Length 
                    ? _spectralY[i] - _spectralY[j] : 0;
                double dz = _spectralZ != null && i < _spectralZ.Length && j < _spectralZ.Length 
                    ? _spectralZ[i] - _spectralZ[j] : 0;
                
                double spectralDist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                
                // If spectral distance too large, definitely not causal
                if (spectralDist > PhysicsConstants.SpeedOfLight * dt * 2)
                    return false;
            }

            // Full check: compute shortest path distance
            double pathDistance = ShortestPathDistance(i, j);
            double lightTravelTime = pathDistance / PhysicsConstants.SpeedOfLight;

            return lightTravelTime <= dt;
        }

        /// <summary>
        /// Propose edge flip with causal constraints
        /// Only flips edges within causal neighborhood.
        /// Uses fast local energy computation to avoid O(N?) complexity.
        /// </summary>
        public void ProposeEdgeFlipCausal(double dt)
        {
            // Choose random node i
            int i = _rng.Next(N);

            // Get causal neighborhood
            var causalNodes = GetCausalNeighborhood(i, dt);
            if (causalNodes.Count <= 1) return; // Only i itself

            // Remove i from options
            causalNodes.Remove(i);

            // Pick random j from causal neighborhood
            int j = causalNodes.ElementAt(_rng.Next(causalNodes.Count));

            // Propose flip
            bool currentlyConnected = Edges[i, j];
            
            // === Checklist C.1: Topological Censorship ===
            // Cannot remove edge with high Wilson flux (gluon field lines)
            if (currentlyConnected && UseTopologicalCensorship)
            {
                double flux = ComputeEdgeFlux(i, j);
                if (Math.Abs(flux) > PhysicsConstants.TopologicalCensorshipFluxThreshold)
                {
                    // Edge removal blocked - would violate charge/color conservation
                    return;
                }
            }

            // Use fast local energy computation instead of full unified energy
            double E_before = ComputeEdgeFlipLocalEnergy(i, j, currentlyConnected);

            // Flip edge
            Edges[i, j] = !currentlyConnected;
            Edges[j, i] = !currentlyConnected;

            double newWeight = 0.0;
            if (Edges[i, j])
            {
                // New edge: initialize with moderate weight
                newWeight = 0.5;
                Weights[i, j] = Weights[j, i] = newWeight;
                
                // Initialize phase if gauge fields active
                if (_edgePhaseU1 != null)
                {
                    _edgePhaseU1[i, j] = _rng.NextDouble() * 2 * Math.PI;
                    _edgePhaseU1[j, i] = -_edgePhaseU1[i, j]; // Antisymmetric
                }
            }

            // Compute energy after using fast local method
            double E_after = ComputeEdgeFlipLocalEnergy(i, j, !currentlyConnected);

            // Metropolis acceptance
            bool accepted = AcceptTopologyChange(E_before, E_after, NetworkTemperature);

            if (!accepted)
            {
                // Revert flip
                Edges[i, j] = currentlyConnected;
                Edges[j, i] = currentlyConnected;

                if (!currentlyConnected)
                {
                    // Restore old weight (was created then removed)
                    Weights[i, j] = Weights[j, i] = 0;
                }
            }
        }

        /// <summary>
        /// Fast local energy computation for edge flip decisions.
        /// Avoids expensive global field computations.
        /// </summary>
        private double ComputeEdgeFlipLocalEnergy(int i, int j, bool connected)
        {
            double energy = 0.0;
            
            if (!connected)
            {
                // Energy cost for missing connection (if nodes are "close")
                double iDegree = Neighbors(i).Count();
                double jDegree = Neighbors(j).Count();
                double avgDegree = (iDegree + jDegree) / 2.0;
                energy += 0.1 * avgDegree; // Cost for disconnection
            }
            else
            {
                double w = Weights[i, j];
                // Binding energy from weight
                energy -= w * w;
                
                // Curvature contribution
                double curv = CalculateGraphCurvature(i, j);
                energy += 0.05 * curv * curv;
            }
            
            return energy;
        }
        
        /// <summary>
        /// Compute flux through an edge (Wilson line phase + Yang-Mills contribution).
        /// High flux indicates strong gauge field on this edge.
        /// Implements RQ-Hypothesis Checklist C.1.
        /// </summary>
        private double ComputeEdgeFlux(int i, int j)
        {
            double flux = 0.0;
            
            // U(1) phase contribution
            if (_edgePhaseU1 != null && i < _edgePhaseU1.GetLength(0) && j < _edgePhaseU1.GetLength(1))
            {
                flux += Math.Abs(_edgePhaseU1[i, j]);
            }
            
            // Non-Abelian gauge field contribution (SU(2)/SU(3))
            // Use the _gaugeSU field which stores the gauge link matrices
            if (_gaugeSU != null && GaugeDimension > 1)
            {
                // Compute trace deviation from identity: |Tr(U - I)|
                int d = GaugeDimension;
                System.Numerics.Complex trace = System.Numerics.Complex.Zero;
                for (int r = 0; r < d; r++)
                {
                    trace += _gaugeSU[i, j, r * d + r];
                }
                // Deviation from identity: Tr(I) = d, so |Tr(U) - d| measures non-triviality
                double traceDeviation = Math.Abs(trace.Real - d) + Math.Abs(trace.Imaginary);
                flux += traceDeviation;
            }
            
            return flux;
        }
        
        /// <summary>
        /// Enable/disable topological censorship (Checklist C.1).
        /// When enabled, edges with high gauge flux cannot be removed.
        /// </summary>
        public bool UseTopologicalCensorship { get; set; } = true;

        /// <summary>
        /// Propose multiple causal edge flips in one step
        /// </summary>
        public void ProposeMultipleCausalFlips(int numFlips, double dt)
        {
            for (int k = 0; k < numFlips; k++)
            {
                ProposeEdgeFlipCausal(dt);
            }
        }

        /// <summary>
        /// Verify that all edges respect causality
        /// Returns list of acausal edges (should be empty)
        /// </summary>
        public List<(int, int)> VerifyNoAcausalEdges(double dt)
        {
            var acausalEdges = new List<(int, int)>();
            double speedOfLight = PhysicsConstants.SpeedOfLight;

            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;

                    // Check if edge length consistent with causality
                    double edgeLength = EdgeLength(i, j);
                    double travelTime = edgeLength / speedOfLight;

                    // An edge exists, so should be reachable in finite time
                    // Check if it's "too long" given the time scale
                    if (travelTime > dt * 10) // Allow factor of 10 for established edges
                    {
                        acausalEdges.Add((i, j));
                    }
                }
            }

            return acausalEdges;
        }

        /// <summary>
        /// Compute maximum signal propagation distance in one timestep
        /// </summary>
        public double ComputeMaxSignalDistance(double dt)
        {
            return PhysicsConstants.SpeedOfLight * dt;
        }

        /// <summary>
        /// Get statistics on causal structure
        /// </summary>
        public (double avgCausalRadius, double maxCausalRadius, int avgCausalNodes) 
            ComputeCausalStatistics(double dt)
        {
            int numSamples = Math.Min(100, N);
            double sumRadius = 0;
            double maxRadius = 0;
            int sumNodes = 0;

            for (int k = 0; k < numSamples; k++)
            {
                int i = _rng.Next(N);
                var causal = GetCausalNeighborhood(i, dt);
                
                int nodeCount = causal.Count;
                sumNodes += nodeCount;

                // Compute max distance in causal region
                double maxDist = 0;
                foreach (int j in causal)
                {
                    if (i == j) continue;
                    double dist = ShortestPathDistance(i, j);
                    maxDist = Math.Max(maxDist, dist);
                }

                sumRadius += maxDist;
                maxRadius = Math.Max(maxRadius, maxDist);
            }

            double avgRadius = sumRadius / numSamples;
            double avgNodes = (double)sumNodes / numSamples;

            return (avgRadius, maxRadius, (int)avgNodes);
        }
        
        /// <summary>
        /// Check if two nodes are causally connected within the current age of the universe.
        /// Implements RQ-Hypothesis Checklist 1.2: Local causality in topology.
        /// </summary>
        public bool IsCausallyConnected(int i, int j)
        {
            // In RQ, distance is defined by weights: d = -ln(w)
            // If "light signal" could not reach, interaction is forbidden.
            
            double dist = GetGraphDistanceWeighted(i, j);
            double c_eff = 1.0; // Effective speed of light on graph
            
            // Use global time approximation for "age of universe" in this context
            // In a fully relational model, this would be the proper time from Big Bang
            double timeSinceBigBang = GlobalTime; 
            
            return dist <= c_eff * timeSinceBigBang;
        }
    }
}
