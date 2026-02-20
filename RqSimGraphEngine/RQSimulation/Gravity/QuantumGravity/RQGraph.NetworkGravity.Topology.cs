using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // RQ-HYPOTHESIS CHECKLIST ITEM 1: Thread-safe queue for edge removal
        // Gravity can now "cut" the graph topology when correlations drop below Planck scale
        private readonly ConcurrentQueue<(int i, int j)> _gravityEdgeRemovalQueue = new();

        /// <summary>
        /// Process queued edge removals from gravity evolution.
        /// RQ-HYPOTHESIS CHECKLIST ITEM 1: Gravity-Graphity Unification
        /// 
        /// When gravity weakens an edge below Planck scale, it is topologically removed.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 2: GAUGE-AWARE REMOVAL
        /// ====================================================
        /// Before removing an edge, check if it carries non-trivial gauge flux.
        /// If so, redistribute the flux to alternate paths or block removal if
        /// the edge is a topological bridge (would create a gauge monopole).
        /// 
        /// RQ-HYPOTHESIS CHECKLIST: LOCAL PARTICLE CREATION
        /// =================================================
        /// Instead of dumping energy to a global radiation pool, we inject it
        /// locally into the scalar field at the endpoints. This:
        /// 1. Conserves energy (1st law)
        /// 2. Conserves locality (no action at a distance)
        /// 3. Creates "particles" (field excitations) at the break point
        /// </summary>
        private void ProcessGravityEdgeRemovalQueue()
        {
            var ledger = GetEnergyLedger();
            int removedCount = 0;
            int blockedCount = 0;

            while (_gravityEdgeRemovalQueue.TryDequeue(out var edge))
            {
                int i = edge.i;
                int j = edge.j;

                // Check if edge still exists (may have been removed by another process)
                if (!Edges[i, j])
                    continue;

                // RQ-HYPOTHESIS CHECKLIST ITEM 2: Check gauge flux before removal
                // If edge carries non-trivial flux, attempt flux redistribution
                if (HasNonTrivialFlux(i, j))
                {
                    if (!Physics.GaugeAwareTopology.CanRemoveEdgeSafely(this, i, j, out int pathLength))
                    {
                        // Edge is a topological bridge with flux - cannot remove
                        // This preserves Gauss law (charge conservation)
                        blockedCount++;
                        continue;
                    }

                    // Redistribute flux before removal
                    if (!Physics.GaugeAwareTopology.RemoveEdgeWithFluxRedistribution(this, i, j, out _))
                    {
                        blockedCount++;
                        continue;
                    }
                }

                // Calculate energy stored in the edge (correlation energy)
                double edgeWeight = Weights[i, j];
                double releasedEnergy = edgeWeight * edgeWeight; // E ~ w? for correlation
                releasedEnergy = Math.Max(releasedEnergy, PhysicsConstants.EdgeCreationCost);

                // Remove the edge topologically
                Edges[i, j] = false;
                Edges[j, i] = false;
                Weights[i, j] = 0.0;
                Weights[j, i] = 0.0;
                _degree[i]--;
                _degree[j]--;

                // Clear gauge phase data for removed edge
                if (_edgePhaseU1 != null)
                {
                    _edgePhaseU1[i, j] = 0.0;
                    _edgePhaseU1[j, i] = 0.0;
                }
                if (_edgeMomentumU1 != null)
                {
                    _edgeMomentumU1[i, j] = 0.0;
                    _edgeMomentumU1[j, i] = 0.0;
                }

                // RQ-HYPOTHESIS CHECKLIST ITEM 5: LOCAL PARTICLE CREATION
                // Instead of global radiation, inject energy into scalar field locally
                // This creates a localized excitation (particle) at the break point
                if (_scalarMomentum != null && i < _scalarMomentum.Length && j < _scalarMomentum.Length)
                {
                    // Split energy between both endpoints
                    double kick = Math.Sqrt(releasedEnergy / 2.0);
                    
                    // Random sign to conserve average momentum (no net momentum creation)
                    double signI = _rng.NextDouble() > 0.5 ? 1.0 : -1.0;
                    double signJ = _rng.NextDouble() > 0.5 ? 1.0 : -1.0;
                    
                    _scalarMomentum[i] += signI * kick;
                    _scalarMomentum[j] += signJ * kick;
                    
                    // Track energy conversion for ledger (topology ? matter)
                    ledger.ConvertTopologyToMatter(releasedEnergy);
                }
                else
                {
                    // Fallback: return to vacuum pool if scalar field not initialized
                    ledger.RegisterRadiation(releasedEnergy);
                }
                
                removedCount++;
            }

            // If any edges were removed, invalidate topology caches
            if (removedCount > 0)
            {
                InvalidateTopologyCache();
                InvalidateParity(); // RQ-HYPOTHESIS CHECKLIST ITEM 1: Update topological parity
                
                // RQ-HYPOTHESIS CHECKLIST ITEM 5: Correct wavefunction after topology change
                CorrectWavefunctionAfterTopologyChange();
            }
        }
    }
}
