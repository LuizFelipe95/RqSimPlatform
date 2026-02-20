using System;
using System.Collections.Generic;
using System.Numerics;

namespace RQSimulation
{
    /// <summary>
    /// RQ-Hypothesis Checklist Item 1.2: Local Unitary Updates
    /// 
    /// Replaces global synchronous updates with local unitary operators acting on random subgraphs.
    /// In General Relativity there is no global "now" - events are local.
    /// This implementation ensures updates are localized and causal.
    /// </summary>
    public partial class RQGraph
    {
        /// <summary>
        /// Apply local unitary update to a random subgraph.
        /// RQ-Compliant: No global synchronization, only local causal updates.
        /// Implements Checklist Item 1.2.
        /// </summary>
        /// <param name="subgraphSize">Maximum size of the subgraph to update</param>
        /// <param name="dt">Time step (should be from relational time)</param>
        public void ApplyLocalUnitaryUpdate(int subgraphSize = 0, double dt = 0.01)
        {
            if (_waveMulti == null) return;
            
            if (subgraphSize <= 0)
                subgraphSize = PhysicsConstants.MaxLocalSubgraphSize;
            
            // Select a random seed node
            int seedNode = _rng.Next(N);
            
            // Build local subgraph using BFS (respecting causality)
            var subgraph = BuildCausalSubgraph(seedNode, subgraphSize, dt);
            if (subgraph.Count == 0) return;
            
            // Apply local Hamiltonian evolution to the subgraph
            ApplySubgraphHamiltonian(subgraph, dt);
        }
        
        /// <summary>
        /// Build a causally connected subgraph starting from seed node.
        /// Only includes nodes within light cone (respecting edge delays).
        /// </summary>
        private List<int> BuildCausalSubgraph(int seedNode, int maxSize, double dt)
        {
            var subgraph = new List<int> { seedNode };
            var visited = new HashSet<int> { seedNode };
            var queue = new Queue<int>();
            queue.Enqueue(seedNode);
            
            while (queue.Count > 0 && subgraph.Count < maxSize)
            {
                int current = queue.Dequeue();
                
                foreach (int neighbor in Neighbors(current))
                {
                    if (visited.Contains(neighbor)) continue;
                    
                    // Check causal connectivity (edge delay must be compatible with dt)
                    double delay = EdgeDelay != null ? EdgeDelay[current, neighbor] : 0.0;
                    if (delay > dt * PhysicsConstants.MaxCausalDistance) continue;
                    
                    visited.Add(neighbor);
                    subgraph.Add(neighbor);
                    queue.Enqueue(neighbor);
                    
                    if (subgraph.Count >= maxSize) break;
                }
            }
            
            return subgraph;
        }
        
        /// <summary>
        /// Apply local Hamiltonian evolution to subgraph.
        /// Uses first-order approximation: |ψ'⟩ = (I - iHdt)|ψ⟩, then normalize for unitarity.
        /// </summary>
        private void ApplySubgraphHamiltonian(List<int> subgraph, double dt)
        {
            if (_waveMulti == null) return;
            
            int d = GaugeDimension;
            
            // Build local Hamiltonian matrix for subgraph
            int n = subgraph.Count;
            var H = new double[n, n];
            
            // Fill Laplacian-like Hamiltonian for subgraph
            var indexMap = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
                indexMap[subgraph[i]] = i;
            
            for (int i = 0; i < n; i++)
            {
                int nodeI = subgraph[i];
                double diagonal = 0.0;
                
                foreach (int nodeJ in Neighbors(nodeI))
                {
                    if (indexMap.TryGetValue(nodeJ, out int j))
                    {
                        H[i, j] = -Weights[nodeI, nodeJ];
                        diagonal += Weights[nodeI, nodeJ];
                    }
                }
                
                H[i, i] = diagonal;
                
                // Add mass term if available
                if (_correlationMass != null && _correlationMass.Length == N)
                    H[i, i] += _correlationMass[nodeI];
            }
            
            // Apply evolution to each gauge component
            for (int a = 0; a < d; a++)
            {
                // Extract subgraph wavefunction component
                var psiSub = new Complex[n];
                for (int i = 0; i < n; i++)
                {
                    int nodeI = subgraph[i];
                    psiSub[i] = _waveMulti[nodeI * d + a];
                }
                
                // Apply (I - iHdt)|ψ⟩
                var psiNew = new Complex[n];
                for (int i = 0; i < n; i++)
                {
                    Complex Hpsi = Complex.Zero;
                    for (int j = 0; j < n; j++)
                    {
                        Hpsi += H[i, j] * psiSub[j];
                    }
                    psiNew[i] = psiSub[i] - Complex.ImaginaryOne * dt * Hpsi;
                }
                
                // Normalize for unitarity
                double norm = 0.0;
                for (int i = 0; i < n; i++)
                {
                    norm += psiNew[i].Magnitude * psiNew[i].Magnitude;
                }
                
                if (norm > 1e-10)
                {
                    // Preserve original norm of subgraph
                    double origNorm = 0.0;
                    for (int i = 0; i < n; i++)
                        origNorm += psiSub[i].Magnitude * psiSub[i].Magnitude;
                    
                    double scale = Math.Sqrt(origNorm / norm);
                    for (int i = 0; i < n; i++)
                        psiNew[i] *= scale;
                }
                
                // Write back to global wavefunction
                for (int i = 0; i < n; i++)
                {
                    int nodeI = subgraph[i];
                    _waveMulti[nodeI * d + a] = psiNew[i];
                }
            }
        }
        
        /// <summary>
        /// Perform multiple local unitary updates, covering approximately the full graph.
        /// RQ-Compliant alternative to global synchronous update.
        /// </summary>
        /// <param name="coverage">Approximate fraction of graph to update (default 1.0 = full coverage)</param>
        /// <param name="dt">Time step from relational time</param>
        public void StepLocalUnitary(double coverage = 1.0, double dt = 0.01)
        {
            if (_waveMulti == null) return;
            
            int subgraphSize = PhysicsConstants.MaxLocalSubgraphSize;
            int numUpdates = Math.Max(1, (int)(N * coverage / subgraphSize));
            
            for (int i = 0; i < numUpdates; i++)
            {
                ApplyLocalUnitaryUpdate(subgraphSize, dt);
            }
        }
        
        // Note: StepAsynchronous() is defined in RQGraph.AsynchronousTime.cs
        // The existing implementation handles per-node proper time evolution properly.
        
        /// <summary>
        /// Compute local time dilation factor based on mass and curvature.
        /// GR-like effect: time flows slower in regions of high mass/curvature.
        /// Used by local unitary updates to determine per-node time steps.
        /// </summary>
        private double ComputeLocalTimeDilationFactor(int node)
        {
            double mass = _correlationMass != null && node < _correlationMass.Length 
                ? _correlationMass[node] : 0.0;
            double avgMass = _avgCorrelationMass > 0 ? _avgCorrelationMass : 1.0;
            
            double curvature = Math.Abs(GetLocalCurvature(node));
            
            // Time dilation: τ = t * sqrt(1 - 2M/r) ≈ 1 - M/r for weak fields
            // Simplified: lower rate near high mass/curvature
            double massFactor = 1.0 / (1.0 + PhysicsConstants.TimeDilationMassCoupling * mass / avgMass);
            double curvatureFactor = 1.0 / (1.0 + PhysicsConstants.TimeDilationCurvatureCoupling * curvature);
            
            return Math.Clamp(massFactor * curvatureFactor, 
                PhysicsConstants.MinTimeDilation, PhysicsConstants.MaxTimeDilation);
        }
    }
}
