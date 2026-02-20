using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // RQ-HYPOTHESIS v2.0: Geometry Momenta for Hamiltonian Gravity
        private double[,] _geometryMomenta;
        
        // Thread-safe topology lock
        private readonly object _topologyLock = new object();

        /// <summary>
        /// Evolve network geometry based on curvature and stress-energy (mass).
        /// Replaces coordinate-based EvolveMetricFromEinstein with relational dynamics.
        /// 
        /// RQ-Hypothesis Compliant (Item 4): Uses gradient descent on action S.
        /// dW/dt = -? * dS/dW where S = S_geometry + S_matter
        /// S_geometry = ? R_ij * w_ij (discrete Einstein-Hilbert)
        /// S_matter = stress-energy contribution from correlation mass
        /// 
        /// GPU Modes:
        /// - Full GPU: Curvature AND gravity computed on GPU (fastest, requires topology init)
        /// - Hybrid GPU: Curvature on CPU, gravity on GPU
        /// - CPU: All computation on CPU (fallback)
        /// </summary>
        public void EvolveNetworkGeometry(double dt)
        {
            if (Weights == null || Edges == null)
                return;

            // GPU acceleration if available
            if (GpuGravity != null)
            {
                // Check if full GPU mode is available (topology buffers initialized)
                if (GpuGravity.IsTopologyInitialized)
                {
                    // FULL GPU MODE: Curvature AND gravity computed on GPU
                    // This eliminates the CPU bottleneck entirely!
                    float[] weights = GetAllWeightsFlat();
                    float[] masses = GetNodeMasses();
                    int[] edgesFrom = FlatEdgesFrom;
                    int[] edgesTo = FlatEdgesTo;
                    
                    GpuGravity.EvolveFullGpuStep(
                        weights,
                        masses,
                        edgesFrom,
                        edgesTo,
                        (float)dt,
                        (float)PhysicsConstants.GravitationalCoupling,
                        (float)PhysicsConstants.CosmologicalConstant
                    );
                    
                    UpdateWeightsFromFlat(weights);
                    UpdateTargetDistancesFromWeights();
                    return;
                }
                else
                {
                    // HYBRID GPU MODE: Curvature on CPU, gravity on GPU
                    // Falls back to this when topology buffers not initialized
                    float[] weights = GetAllWeightsFlat();
                    float[] curvatures = GetAllCurvaturesFlat(); // CPU bottleneck
                    float[] masses = GetNodeMasses();
                    int[] edgesFrom = FlatEdgesFrom;
                    int[] edgesTo = FlatEdgesTo;
                    
                    GpuGravity.EvolveGravityGpu(
                        weights,
                        curvatures,
                        masses,
                        edgesFrom,
                        edgesTo,
                        (float)dt,
                        (float)PhysicsConstants.GravitationalCoupling,
                        (float)PhysicsConstants.CosmologicalConstant
                    );
                    
                    UpdateWeightsFromFlat(weights);
                    UpdateTargetDistancesFromWeights();
                    return;
                }
            }

            // CPU implementation with updated constants from PhysicsConstants
            // RQ-HYPOTHESIS FIX: Use unified NodeMasses instead of just correlation mass
            // This ensures gravity responds to ALL field contributions (Einstein equations)
            
            lock (_topologyLock)
            {
                // 1. Calculate changes (Ricci flow)
                var deltaWeights = CalculateRicciFlowChanges(dt);

                // 2. Apply changes and prune edges
                ApplyWeightsAndPruneEdges(deltaWeights);
            }
        }

        private double[,] CalculateRicciFlowChanges(double dt)
        {
            UpdateNodeMasses();

            double[,] deltaWeights = new double[N, N];
            // Use updated GravitationalCoupling from PhysicsConstants
            double learningRate = PhysicsConstants.GravitationalCoupling * dt;

            // Parallelize outer loop, process j>i only to avoid races
            System.Threading.Tasks.Parallel.For(0, N, i =>
            {
                // RQ-FIX: Use GetNodeTotalMass from UnifiedMass.cs which includes:
                // - Fermion mass (Dirac spinors)
                // - Scalar field energy (Higgs)
                // - Gauge field energy (photons, gluons)
                // - Correlation mass (topological)
                // - Vacuum energy (cosmological)
                double massI = GetNodeTotalMass(i);
                double volI = GetLocalVolume(i);

                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;

                    double massJ = GetNodeTotalMass(j);
                    double volJ = GetLocalVolume(j);
                    
                    double dS_geometry;
                    if (PhysicsConstants.PreferOllivierRicciCurvature)
                    {
                        dS_geometry = CalculateOllivierRicciCurvature(i, j);
                    }
                    else
                    {
                        dS_geometry = CalculateGraphCurvature(i, j);
                    }

                    // Stress-energy tensor now includes ALL field contributions
                    double stressEnergyTensor = (massI + massJ) * 0.5;
                    double dS_matter = -stressEnergyTensor * PhysicsConstants.CurvatureTermScale;
                    
                    // RQ-HYPOTHESIS CHECKLIST ITEM 2: REMOVED BARRIER POTENTIAL
                    // ==========================================================
                    // OLD: Artificial "barrierForce" that prevented weights from reaching zero
                    // NEW: Pure gradient flow from Einstein-Hilbert action
                    //
                    // PHYSICS JUSTIFICATION:
                    // The barrier potential V ~ 1/w? has no basis in Einstein equations.
                    // It was a phenomenological "crutch" that prevented natural dynamics.
                    // 
                    // Instead, we allow weights to evolve freely under Ricci flow:
                    //   dw/dt = -? * (R_ij - T_ij)
                    // 
                    // When w ? 0, the edge undergoes TOPOLOGICAL TRANSITION (Quantum Graphity):
                    // - Edge disappears from graph (topology change)
                    // - This is the correct discrete analog of singularity formation
                    // - Energy conservation: edge energy returns to vacuum pool
                    
                    double dS_total = dS_geometry + dS_matter;

                    // Pure gradient descent on action - NO barrier force
                    double delta = -learningRate * dS_total;

                    // Safe write for unique [i,j] pair and symmetric [j,i]
                    deltaWeights[i, j] = delta;
                    deltaWeights[j, i] = delta;
                }
            });
            
            return deltaWeights;
        }

        private void ApplyWeightsAndPruneEdges(double[,] deltaWeights)
        {
            // Apply weight updates with bounds checking (parallelize outer loop, j>i)
            System.Threading.Tasks.Parallel.For(0, N, i =>
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;

                    double newWeight = Weights[i, j] + deltaWeights[i, j];
                    
                    // RQ-HYPOTHESIS CHECKLIST ITEM 2: Topological Transition (Quantum Graphity)
                    // =====================================================================
                    // When weight drops below Planck threshold, the edge DISAPPEARS.
                    // This is physical: at Planck scale, classical geometry breaks down.
                    // 
                    // Instead of artificially holding the edge alive, we let it die.
                    // This creates emergent "quantum foam" topology fluctuations.
                    if (newWeight < PhysicsConstants.PlanckWeightThreshold)
                    {
                        // Queue for topological removal (cannot modify graph inside Parallel.For)
                        newWeight = 0.0;
                        _gravityEdgeRemovalQueue.Enqueue((i, j));
                    }
                    else if (newWeight > 1.0)
                    {
                        // Saturation: maximum correlation/entanglement reached
                        newWeight = 1.0;
                    }
                    
                    // Safety check: no negative weights (should not happen with proper physics)
                    if (newWeight < 0)
                    {
                        newWeight = 0.0;
                        _gravityEdgeRemovalQueue.Enqueue((i, j));
                    }
                    
                    Weights[i, j] = newWeight;
                    Weights[j, i] = newWeight;
                }
            });

            // RQ-HYPOTHESIS CHECKLIST ITEM 1: Process edge removal queue
            // After parallel loop, remove edges that fell below Planck threshold.
            // Energy is returned to vacuum pool (energy conservation).
            ProcessGravityEdgeRemovalQueue();

            UpdateTargetDistancesFromWeights();
        }

        /// <summary>
        /// Evolve geometry for a single edge using gradient descent on Hamiltonian.
        /// The weight change is driven by local curvature: dw/dt ? R_ij
        /// Positive curvature increases weight (contracts space), negative decreases.
        /// Implements checklist item 3.2: Geodesic geodinamics via gradient descent.
        /// </summary>
        /// <param name="i">First node of edge</param>
        /// <param name="j">Second node of edge</param>
        /// <param name="learningRate">Step size for gradient descent</param>
        public void EvolveGeometry(int i, int j, double learningRate)
        {
            if (!Edges[i, j] || i == j)
                return;
            
            // dH/dw ? -R_ij (curvature dictates metric change)
            // Positive curvature ? increase weight (contract space, strengthen link)
            // Negative curvature ? decrease weight (expand space, weaken link)
            // RQ-FIX: Use CalculateGraphCurvature which delegates to Ollivier-Ricci if configured
            double curvature = CalculateGraphCurvature(i, j);
            
            // Weight update: w += ? * R (gradient ascent on curvature)
            double newWeight = Weights[i, j] + learningRate * curvature;
            
            // Clamp to valid range [0, 1]
            newWeight = Math.Clamp(newWeight, 0.0, 1.0);
            
            Weights[i, j] = newWeight;
            Weights[j, i] = newWeight; // Symmetric
        }
        
        /// <summary>
        /// Evolve entire graph geometry using curvature-driven gradient descent.
        /// All edges are updated according to their local Forman-Ricci curvature.
        /// Implements checklist item 3.2: Full geometrodynamics step.
        /// </summary>
        /// <param name="learningRate">Step size for gradient descent</param>
        public void EvolveGeometryFull(double learningRate)
        {
            if (Weights == null || Edges == null)
                return;

            // RQ-HYPOTHESIS v2.0: Use Hamiltonian Gravity if enabled
            if (PhysicsConstants.UseHamiltonianGravity)
            {
                EvolveGeometryHamiltonian(learningRate);
                return;
            }
            
            // Buffer for weight updates (to avoid order-dependent artifacts)
            double[,] deltaWeights = new double[N, N];
            
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue; // Each edge once
                    
                    double curvature = ComputeFormanRicciCurvature(i, j);
                    deltaWeights[i, j] = learningRate * curvature;
                    deltaWeights[j, i] = deltaWeights[i, j];
                }
            }
            
            // Apply updates
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;
                    
                    double newWeight = Weights[i, j] + deltaWeights[i, j];
                    newWeight = Math.Clamp(newWeight, 0.0, 1.0);
                    Weights[i, j] = newWeight;
                    Weights[j, i] = newWeight;
                }
            }
            
            // Update derived quantities
            UpdateTargetDistancesFromWeights();
        }
        
        /// <summary>
        /// Evolve geometry using Hamiltonian dynamics (Symplectic Integrator).
        /// Replaces gradient descent with 2nd order dynamics: d?w/dt? ~ Curvature.
        /// This gives the geometry "inertia" and allows for gravitational waves.
        /// </summary>
        public void EvolveGeometryHamiltonian(double dt)
        {
            if (_geometryMomenta == null || _geometryMomenta.GetLength(0) != N)
            {
                _geometryMomenta = new double[N, N];
            }

            // Step 1: Half-step position update (Drift)
            // w(t + dt/2) = w(t) + (p / m) * dt/2
            Parallel.For(0, N, i => {
                foreach (var j in Neighbors(i))
                {
                    if (j <= i) continue;
                    double p = _geometryMomenta[i, j];
                    double dw = (p / PhysicsConstants.GeometryInertiaMass) * 0.5 * dt;
                    
                    Weights[i, j] += dw;
                    Weights[j, i] += dw;
                }
            });

            // Step 2: Compute Forces (Kick)
            // F = -dH/dw ~ Curvature
            // We compute curvature at the intermediate position
            var forces = new double[N, N];
            Parallel.For(0, N, i => {
                foreach (var j in Neighbors(i))
                {
                    if (j <= i) continue;
                    // Force is proportional to curvature
                    // Positive curvature -> Attraction -> Increase weight (reduce distance)
                    // But wait, weight ~ 1/distance. 
                    // If we want to contract space (increase weight), force should be positive.
                    double curvature = CalculateGraphCurvature(i, j);
                    forces[i, j] = curvature; 
                    // Note: We might need scaling here, but learningRate/dt handles it in the caller usually.
                    // Here we assume force is directly the curvature term.
                }
            });

            // Step 3: Full-step momentum update
            // p(t + dt) = p(t) + F * dt
            Parallel.For(0, N, i => {
                foreach (var j in Neighbors(i))
                {
                    if (j <= i) continue;
                    
                    double f = forces[i, j];
                    _geometryMomenta[i, j] += f * dt;
                    _geometryMomenta[j, i] = _geometryMomenta[i, j];

                    // Damping (Hubble friction or similar) to prevent runaway
                    _geometryMomenta[i, j] *= 0.999;
                    _geometryMomenta[j, i] *= 0.999;
                }
            });

            // Step 4: Half-step position update (Drift)
            // w(t + dt) = w(t + dt/2) + (p / m) * dt/2
            Parallel.For(0, N, i => {
                foreach (var j in Neighbors(i))
                {
                    if (j <= i) continue;
                    
                    double p = _geometryMomenta[i, j];
                    double dw = (p / PhysicsConstants.GeometryInertiaMass) * 0.5 * dt;
                    
                    double newWeight = Weights[i, j] + dw;
                    newWeight = Math.Clamp(newWeight, 0.0, 1.0); // Keep weights physical
                    
                    Weights[i, j] = newWeight;
                    Weights[j, i] = newWeight;
                }
            });
            
            // Update derived quantities
            UpdateTargetDistancesFromWeights();
        }

        /// <summary>
        /// Compute total kinetic energy in geometry momenta (gravitational wave energy).
        /// K = (1/2M) ? ?_ij? (Checklist F.3)
        /// </summary>
        public double ComputeGeometryKineticEnergy()
        {
            if (_geometryMomenta == null) return 0.0;
            
            double energy = 0.0;
            double mass = PhysicsConstants.GeometryInertiaMass;
            
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;
                    double p = _geometryMomenta[i, j];
                    energy += 0.5 * p * p / mass;
                }
            }
            
            return energy;
        }

        /// <summary>
        /// Initialize geometry momenta for Hamiltonian gravity.
        /// </summary>
        public void InitGeometryMomenta()
        {
            _geometryMomenta = new double[N, N];
        }
    }
}
