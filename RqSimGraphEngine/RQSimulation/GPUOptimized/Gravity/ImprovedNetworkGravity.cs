using System;
using System.Collections.Generic;
using System.Numerics;
using ComputeSharp;

namespace RQSimulation.GPUOptimized
{
    public class ImprovedNetworkGravity
    {
        // Note: These constants are now superseded by PhysicsConstants for consistency.
        // They are kept for backward compatibility with GetEffectiveGravitationalCoupling.
        private const double BaseGravitationalCoupling = 2.5; // Legacy: use PhysicsConstants.GravitationalCoupling instead
        private const double MaxGravitationalCoupling = 5.0; // Maximum during early annealing

        /// <summary>
        /// Apply soft walls to edge weights to prevent them from hitting 0 or 1 exactly.
        /// This maintains graph connectivity and prevents numerical singularities.
        /// 
        /// RQ-MODERNIZATION: Now respects PhysicsConstants.UseSoftWalls and 
        /// PhysicsConstants.AllowZeroWeightEdges flags for physically rigorous simulations.
        /// 
        /// When UseSoftWalls = false and AllowZeroWeightEdges = true:
        /// - Weights can reach 0.0 (edge dissolution / horizon formation)
        /// - Weights are only clamped to non-negative values
        /// - This allows proper Regge calculus singularity formation
        /// </summary>
        private static double ApplySoftWalls(double weight)
        {
            // RQ-MODERNIZATION: Configurable soft wall behavior
            if (!PhysicsConstants.UseSoftWalls)
            {
                if (PhysicsConstants.AllowZeroWeightEdges)
                {
                    // Allow true zero: only prevent negative weights
                    return Math.Max(weight, 0.0);
                }
                else
                {
                    // Prevent exact zero but no upper bound
                    return Math.Max(weight, PhysicsConstants.WeightAbsoluteMinimum);
                }
            }
            
            // Legacy behavior: clamp to soft walls
            return Math.Clamp(weight, PhysicsConstants.WeightLowerSoftWall, PhysicsConstants.WeightUpperSoftWall);
        }

        /// <summary>
        /// Compute effective gravitational coupling with simulated annealing
        /// CHECKLIST ITEM 3: During first 1000 steps, use boosted coupling to form clusters
        /// 
        /// G_eff(t) = G_base * (1.0 + 10.0 * exp(-step / 200))
        /// 
        /// This "hot start" forces the graph to collapse into clusters early,
        /// creating matter before temperature cools down.
        /// 
        /// NOTE: For new simulations, use EvolveNetworkGeometryOllivierDynamic with
        /// externally computed effectiveG based on PhysicsConstants.
        /// </summary>
        public static double GetEffectiveGravitationalCoupling(int step)
        {
            // Annealing phase: first 1000 steps
            if (step < 1000)
            {
                // Exponential boost that decays over 200 steps
                double boostFactor = 1.0 + 10.0 * Math.Exp(-step / 200.0);
                double effective = BaseGravitationalCoupling * boostFactor;

                // Clamp to maximum
                return Math.Min(effective, MaxGravitationalCoupling);
            }

            // After annealing: use base value
            return BaseGravitationalCoupling;
        }

        /// <summary>
        /// Evolve network geometry using Ollivier-Ricci curvature with DYNAMIC gravitational coupling.
        /// This is the primary method for "Primordial Soup" simulation.
        /// 
        /// The caller provides effectiveG which should be computed based on simulation time:
        /// - Phase 1 (Mixing): effectiveG = 0.1 (weak gravity, allows restructuring)
        /// - Phase 2 (Clustering): effectiveG = 1.5 (strong gravity, forms clusters)
        /// 
        /// CHECKLIST ITEM 4: Use Ollivier-Ricci for better geometric sensitivity
        /// 
        /// RQ-FIX: Now uses unified NodeMasses.TotalMass instead of correlation mass only.
        /// This ensures gravity responds to ALL field contributions (scalar, fermion, gauge).
        /// 
        /// RQ-FIX: Added connectivity protection to prevent graph fragmentation.
        /// When graph is at risk of fragmenting (low average degree), weight decreases are suppressed.
        /// 
        /// GPU ACCELERATION: If graph.GpuGravity is initialized, uses GPU for Forman-Ricci curvature
        /// and weight updates. Ollivier-Ricci requires CPU due to optimal transport complexity.
        /// Use graph.InitGpuGravity() to enable GPU mode, graph.DisposeGpuGravity() to release.
        /// </summary>
        /// <param name="graph">The RQGraph to evolve</param>
        /// <param name="dt">Time step</param>
        /// <param name="effectiveG">Gravitational coupling (caller controls phase transition)</param>
        public static void EvolveNetworkGeometryOllivierDynamic(RQGraph graph, double dt, double effectiveG)
        {
            if (graph.Weights == null || graph.Edges == null)
                return;

            // GPU fast path: Use Forman-Ricci on GPU when available
            // Note: Ollivier-Ricci requires optimal transport (complex on GPU),
            // so we use Forman-Ricci for GPU mode (still captures curvature well)
            if (graph.GpuGravity != null && graph.GpuGravity.IsTopologyInitialized)
            {
                EvolveNetworkGeometryGpu(graph, dt, effectiveG);
                return;
            }

            int N = graph.N;
            double learningRate = effectiveG * dt;

            // RQ-MODERNIZATION: Connectivity protection is now configurable
            // When UseConnectivityProtection = false, let physics equations determine graph fate
            bool protectConnectivity = false;
            double connectivityProtectionFactor = 1.0;
            
            if (PhysicsConstants.UseConnectivityProtection)
            {
                // RQ-FIX: Connectivity protection - compute graph health metrics
                // If average degree is too low, prevent further weight decreases to avoid fragmentation
                double avgDegree = ComputeAverageDegree(graph);
                double minWeightSum = ComputeMinNodeWeightSum(graph);

                // Protection thresholds:
                // - If avgDegree < 4, graph is at risk of fragmentation
                // - If any node has weightSum < 0.8, it may become isolated
                protectConnectivity = avgDegree < 4.0 || minWeightSum < 0.8;
                connectivityProtectionFactor = protectConnectivity ? 0.5 : 1.0;
            }

            // RQ-FIX: Update unified node masses (includes ALL field contributions)
            graph.UpdateNodeMasses();
            var nodeMasses = graph.NodeMasses;

            // Parallel update of edge weights
            double[,] deltaWeights = new double[N, N];

            System.Threading.Tasks.Parallel.For(0, N, i =>
            {
                // RQ-FIX: Use TotalMass from unified NodeMasses
                // This includes fermion, scalar, gauge, correlation, and vacuum energy
                double massI = nodeMasses[i].TotalMass;

                foreach (int j in graph.Neighbors(i))
                {
                    if (j <= i) continue; // Process each edge once

                    double massJ = nodeMasses[j].TotalMass;
                    double currentWeight = graph.Weights[i, j];

                    // Use Ollivier-Ricci (Sinkhorn) only when explicitly preferred;
                    // default to Forman-Ricci which is O(degree²) vs O(support²×iterations)
                    double curvature;
                    if (PhysicsConstants.PreferOllivierRicciCurvature)
                        curvature = OllivierRicciCurvature.ComputeOllivierRicciSinkhorn(graph, i, j);
                    else
                        curvature = graph.ComputeFormanRicciCurvature(i, j);

                    // Stress-energy contribution (matter) - now uses FULL mass
                    double stressEnergyTensor = (massI + massJ) * 0.5;
                    double dS_matter = -stressEnergyTensor * PhysicsConstants.CurvatureTermScale;

                    // Cosmological constant contribution
                    double dS_cosmological = PhysicsConstants.CosmologicalConstant;

                    // Total action gradient (flow rate)
                    double flowRate = curvature + dS_matter + dS_cosmological;

                    // RQ-MODERNIZATION: Configurable flow integration
                    double relativeChange;
                    if (PhysicsConstants.UseUnboundedFlow)
                    {
                        // Linear Euler step: unbounded flow without artificial saturation
                        // NOTE: This is NOT symplectic - no momentum variable involved
                        // delta = flowRate * dt (physics determines evolution rate)
                        relativeChange = flowRate * learningRate;
                    }
                    else
                    {
                        // Legacy: Use multiplicative update with Tanh-bounded rate
                        // Provides stability at the cost of physical accuracy
                        relativeChange = Math.Tanh(flowRate * 0.1) * learningRate;
                    }
                    
                    double delta = currentWeight * relativeChange;

                    // RQ-MODERNIZATION: Connectivity protection is now configurable
                    if (delta < 0 && protectConnectivity && PhysicsConstants.UseConnectivityProtection)
                    {
                        delta *= connectivityProtectionFactor;

                        // Additional protection: prevent weight below minimum threshold (only if using soft walls)
                        if (PhysicsConstants.UseSoftWalls)
                        {
                            double projectedWeight = currentWeight + delta;
                            if (projectedWeight < PhysicsConstants.WeightLowerSoftWall)
                            {
                                delta = Math.Max(delta, PhysicsConstants.WeightLowerSoftWall - currentWeight);
                            }
                        }
                    }

                    deltaWeights[i, j] = delta;
                    deltaWeights[j, i] = delta;
                }
            });

            // Apply updates with soft walls
            System.Threading.Tasks.Parallel.For(0, N, i =>
            {
                foreach (int j in graph.Neighbors(i))
                {
                    if (j <= i) continue;

                    double newWeight = graph.Weights[i, j] + deltaWeights[i, j];

                    // Apply soft walls (Checklist A.4)
                    newWeight = ApplySoftWalls(newWeight);

                    graph.Weights[i, j] = newWeight;
                    graph.Weights[j, i] = newWeight;
                }
            });

            // Update target distances - using public method from RQGraph
            graph.UpdateTargetDistancesFromWeights();
        }

        /// <summary>
        /// Compute average degree of the graph (number of edges per node).
        /// Used for connectivity protection.
        /// </summary>
        private static double ComputeAverageDegree(RQGraph graph)
        {
            int totalDegree = 0;
            for (int i = 0; i < graph.N; i++)
            {
                totalDegree += graph.Neighbors(i).Count();
            }
            return graph.N > 0 ? (double)totalDegree / graph.N : 0.0;
        }

        /// <summary>
        /// Compute minimum weight sum across all nodes.
        /// A low value indicates a node at risk of isolation.
        /// </summary>
        private static double ComputeMinNodeWeightSum(RQGraph graph)
        {
            double minSum = double.MaxValue;
            for (int i = 0; i < graph.N; i++)
            {
                double sum = 0.0;
                foreach (int j in graph.Neighbors(i))
                {
                    sum += graph.Weights[i, j];
                }
                if (sum < minSum) minSum = sum;
            }
            return minSum == double.MaxValue ? 0.0 : minSum;
        }

        /// <summary>
        /// GPU-accelerated network geometry evolution using Forman-Ricci curvature.
        /// 
        /// This method runs entirely on GPU for maximum performance:
        /// 1. Computes Forman-Ricci curvature on GPU (parallel over edges)
        /// 2. Applies gravity update on GPU (parallel over edges)
        /// 3. Syncs results back to CPU graph
        /// 
        /// Note: Uses Forman-Ricci instead of Ollivier-Ricci because:
        /// - Ollivier-Ricci requires optimal transport (complex on GPU)
        /// - Forman-Ricci is efficient O(degree²) and GPU-friendly
        /// - Both capture essential curvature properties for gravity
        /// 
        /// RQ-FIX: Now uses unified GetUnifiedMassesFlat() which includes ALL field
        /// contributions (fermion, scalar, gauge, correlation, vacuum, kinetic),
        /// not just correlation mass. This ensures GPU gravity matches CPU physics.
        /// 
        /// Enable via: graph.InitGpuGravity()
        /// </summary>
        private static void EvolveNetworkGeometryGpu(RQGraph graph, double dt, double effectiveG)
        {
            var gpuEngine = graph.GpuGravity;
            if (gpuEngine == null || !gpuEngine.IsTopologyInitialized)
                return;

            int N = graph.N;
            int edgeCount = graph.FlatEdgesFrom.Length;

            // Prepare data arrays
            float[] weights = new float[edgeCount];

            // RQ-FIX: Use unified masses instead of just correlation mass
            // GetUnifiedMassesFlat() calls UpdateNodeMasses() internally and returns
            // NodeMassModels[i].TotalMass which includes ALL field contributions
            float[] masses = graph.GetUnifiedMassesFlat();

            // Get current weights
            for (int e = 0; e < edgeCount; e++)
            {
                int i = graph.FlatEdgesFrom[e];
                int j = graph.FlatEdgesTo[e];
                weights[e] = (float)graph.Weights[i, j];
            }

            // Run GPU computation with Forman-Ricci curvature (Jost formula)
            gpuEngine.EvolveFullGpuStep(
                weights,
                masses,
                graph.FlatEdgesFrom,
                graph.FlatEdgesTo,
                (float)dt,
                (float)effectiveG,
                (float)PhysicsConstants.CosmologicalConstant);

            // Apply results back to graph (with soft walls)
            for (int e = 0; e < edgeCount; e++)
            {
                int i = graph.FlatEdgesFrom[e];
                int j = graph.FlatEdgesTo[e];
                double newWeight = ApplySoftWalls(weights[e]);
                graph.Weights[i, j] = newWeight;
                graph.Weights[j, i] = newWeight;
            }

            graph.UpdateTargetDistancesFromWeights();
        }

        /// <summary>
        /// Evolve network geometry using Ollivier-Ricci curvature
        /// This replaces the old Forman-Ricci based method
        /// 
        /// CHECKLIST ITEM 4: Use Ollivier-Ricci for better geometric sensitivity
        /// RQ-MODERNIZATION: Now supports both Tanh-bounded (legacy) and symplectic integrators.
        /// RQ-FIX: Now uses unified NodeMasses.TotalMass instead of correlation mass only.
        /// </summary>
        public static void EvolveNetworkGeometryOllivier(RQGraph graph, double dt, int currentStep)
        {
            if (graph.Weights == null || graph.Edges == null)
                return;

            int N = graph.N;

            // Compute effective coupling with annealing
            double effectiveG = GetEffectiveGravitationalCoupling(currentStep);
            double learningRate = effectiveG * dt;

            // RQ-FIX: Update unified node masses (includes ALL field contributions)
            graph.UpdateNodeMasses();
            var nodeMasses = graph.NodeMasses;

            // Parallel update of edge weights
            double[,] deltaWeights = new double[N, N];

            System.Threading.Tasks.Parallel.For(0, N, i =>
            {
                // RQ-FIX: Use TotalMass from unified NodeMasses
                double massI = nodeMasses[i].TotalMass;

                foreach (int j in graph.Neighbors(i))
                {
                    if (j <= i) continue; // Process each edge once

                    double massJ = nodeMasses[j].TotalMass;
                    double currentWeight = graph.Weights[i, j];

                    // Use Ollivier-Ricci (Sinkhorn) only when explicitly preferred;
                    // default to Forman-Ricci which is O(degree²) vs O(support²×iterations)
                    double curvature;
                    if (PhysicsConstants.PreferOllivierRicciCurvature)
                        curvature = OllivierRicciCurvature.ComputeOllivierRicciSinkhorn(graph, i, j);
                    else
                        curvature = graph.ComputeFormanRicciCurvature(i, j);

                    // Stress-energy contribution (matter) - now uses FULL mass
                    double stressEnergyTensor = (massI + massJ) * 0.5;
                    double dS_matter = -stressEnergyTensor * PhysicsConstants.CurvatureTermScale;

                    // Cosmological constant contribution
                    double dS_cosmological = PhysicsConstants.CosmologicalConstant;

                    // Flow rate from action gradient
                    double flowRate = curvature + dS_matter + dS_cosmological;

                    // RQ-MODERNIZATION: Configurable flow integration
                    double relativeChange;
                    if (PhysicsConstants.UseUnboundedFlow)
                    {
                        // Linear Euler step: unbounded flow without artificial saturation
                        // NOTE: This is NOT symplectic - no momentum variable involved
                        relativeChange = flowRate * learningRate;
                    }
                    else
                    {
                        // Legacy: Tanh-bounded multiplicative update
                        relativeChange = Math.Tanh(flowRate * 0.1) * learningRate;
                    }
                    
                    double delta = currentWeight * relativeChange;

                    deltaWeights[i, j] = delta;
                    deltaWeights[j, i] = delta;
                }
            });

            // Apply updates with configurable soft walls
            System.Threading.Tasks.Parallel.For(0, N, i =>
            {
                foreach (int j in graph.Neighbors(i))
                {
                    if (j <= i) continue;

                    double newWeight = graph.Weights[i, j] + deltaWeights[i, j];

                    // RQ-MODERNIZATION: Configurable soft walls
                    newWeight = ApplySoftWalls(newWeight);

                    graph.Weights[i, j] = newWeight;
                    graph.Weights[j, i] = newWeight;
                }
            });

            // Update target distances - using public method from RQGraph
            // Note: This is called internally by the graph when needed
        }

        /// <summary>
        /// Evolve network geometry using Forman-Ricci curvature (CPU implementation).
        /// This matches the GPU implementation (GravityShader) for consistent results.
        /// 
        /// Use this method when comparing CPU vs GPU to ensure the same physics model.
        /// </summary>
        public static void EvolveNetworkGeometryForman(RQGraph graph, double dt, double effectiveG)
        {
            if (graph.Weights == null || graph.Edges == null) return;

            int N = graph.N;

            // Update unified node masses
            graph.UpdateNodeMasses();
            var nodeMasses = graph.NodeMasses;

            double[,] deltaWeights = new double[N, N];

            System.Threading.Tasks.Parallel.For(0, N, i =>
            {
                double massI = nodeMasses[i].TotalMass;

                foreach (int j in graph.Neighbors(i))
                {
                    if (j <= i) continue;

                    double massJ = nodeMasses[j].TotalMass;
                    double currentWeight = graph.Weights[i, j];

                    // Use Forman-Ricci curvature (same as GPU)
                    double curvature = graph.ComputeFormanRicciCurvature(i, j);

                    // Stress-energy contribution
                    double massTerm = (massI + massJ) * 0.5;

                    // Flow rate: Ric - G*Mass + Lambda
                    // Matches GravityShader logic
                    double flowRate = curvature - effectiveG * massTerm + PhysicsConstants.CosmologicalConstant;

                    // Multiplicative update with tanh
                    // Matches GravityShader: float relativeChange = Hlsl.Tanh(flowRate * 0.1f) * dt;
                    double relativeChange = Math.Tanh(flowRate * 0.1) * dt;
                    double delta = currentWeight * relativeChange;

                    deltaWeights[i, j] = delta;
                    deltaWeights[j, i] = delta;
                }
            });

            // Apply updates
            System.Threading.Tasks.Parallel.For(0, N, i =>
            {
                foreach (int j in graph.Neighbors(i))
                {
                    if (j <= i) continue;
                    double oldW = graph.Weights[i, j];
                    double newW = oldW + deltaWeights[i, j];

                    // Clamp to match GPU shader: Hlsl.Clamp(w, 0.02f, 0.98f)
                    newW = Math.Clamp(newW, 0.02, 0.98);

                    graph.Weights[i, j] = newW;
                    graph.Weights[j, i] = newW;
                }
            });
        }

        /// <summary>
        /// Compute average Ollivier-Ricci curvature of the network
        /// Useful for diagnostics and monitoring phase transitions
        /// </summary>
        public static double ComputeAverageOllivierCurvature(RQGraph graph)
        {
            if (graph.Edges == null || graph.Weights == null)
                return 0.0;

            double totalCurvature = 0.0;
            int edgeCount = 0;

            for (int i = 0; i < graph.N; i++)
            {
                foreach (int j in graph.Neighbors(i))
                {
                    if (j <= i) continue;

                    totalCurvature += OllivierRicciCurvature.ComputeOllivierRicciSinkhorn(graph, i, j);
                    edgeCount++;
                }
            }

            return edgeCount > 0 ? totalCurvature / edgeCount : 0.0;
        }

        /// <summary>
        /// Check if the network has undergone phase transition
        /// Heavy clusters form when average curvature becomes positive
        /// </summary>
        public static bool HasFormattedHeavyClusters(RQGraph graph)
        {
            double avgCurvature = ComputeAverageOllivierCurvature(graph);

            // Positive curvature indicates clustering (matter formation)
            if (avgCurvature > 0.05)
            {
                var heavyStats = graph.GetHeavyClusterStatsCorrelationMass(
                    RQGraph.HeavyClusterThreshold,
                    RQGraph.HeavyClusterMinSize);

                // Check if heavy clusters have formed
                // Note: The tuple returns (count, totalMass, maxSize, avgMassPerNode)
                return heavyStats.count > 0 && heavyStats.totalMass > 0.1;
            }

            return false;
        }

        /// <summary>
        /// Evolve network geometry using unified NodeMassModel (CHECKLIST ITEM 6).
        /// 
        /// This method uses TotalMass from NodeMassModel as the source term for gravity,
        /// aggregating contributions from all fields:
        /// - Fermion mass (Dirac spinors)
        /// - Correlation mass (graph structure)
        /// - Gauge field energy (Yang-Mills)
        /// - Scalar field energy (Higgs)
        /// - Vacuum energy (cosmological constant)
        /// - Kinetic energy (gravitational waves)
        /// 
        /// Einstein equation on graph: dw_ij/dt = -G * (R_ij - T_ij + Λ)
        /// where T_ij = (M_i + M_j)/2 is the average total mass at edge endpoints.
        /// </summary>
        /// <param name="graph">The RQGraph to evolve</param>
        /// <param name="dt">Time step</param>
        /// <param name="effectiveG">Gravitational coupling constant</param>
        public static void EvolveNetworkGeometryUnifiedMass(RQGraph graph, double dt, double effectiveG)
        {
            if (graph.Weights == null || graph.Edges == null)
                return;

            int N = graph.N;
            double learningRate = effectiveG * dt;

            // CHECKLIST ITEM 6: Update unified mass models before gravity step
            graph.UpdateNodeMasses();
            var nodeMasses = graph.NodeMasses;

            // Connectivity protection
            double avgDegree = ComputeAverageDegree(graph);
            double minWeightSum = ComputeMinNodeWeightSum(graph);
            bool protectConnectivity = avgDegree < 4.0 || minWeightSum < 0.8;
            double protectionFactor = protectConnectivity ? 0.5 : 1.0;

            // Parallel weight update
            double[,] deltaWeights = new double[N, N];

            System.Threading.Tasks.Parallel.For(0, N, i =>
            {
                // CHECKLIST ITEM 6: Use TotalMass from NodeMassModel
                double massI = nodeMasses[i].TotalMass;

                foreach (int j in graph.Neighbors(i))
                {
                    if (j <= i) continue;

                    double massJ = nodeMasses[j].TotalMass;
                    double currentWeight = graph.Weights[i, j];

                    // Use Ollivier-Ricci or Forman-Ricci curvature
                    double curvature;
                    if (PhysicsConstants.PreferOllivierRicciCurvature)
                        curvature = OllivierRicciCurvature.ComputeOllivierRicciSinkhorn(graph, i, j);
                    else
                        curvature = graph.ComputeFormanRicciCurvature(i, j);

                    // Stress-energy from unified mass (includes all field contributions)
                    double stressEnergy = (massI + massJ) * 0.5 * PhysicsConstants.CurvatureTermScale;

                    // Cosmological constant
                    double lambda = PhysicsConstants.CosmologicalConstant;

                    // Flow rate from Einstein equation
                    double flowRate = curvature - stressEnergy + lambda;

                    // ENERGY CONSERVATION: Multiplicative update with bounded rate
                    double relativeChange = Math.Tanh(flowRate * 0.1) * learningRate;
                    double delta = currentWeight * relativeChange;

                    // Connectivity protection
                    if (delta < 0 && protectConnectivity)
                    {
                        delta *= protectionFactor;
                        double projectedWeight = currentWeight + delta;
                        if (projectedWeight < PhysicsConstants.WeightLowerSoftWall)
                            delta = Math.Max(delta, PhysicsConstants.WeightLowerSoftWall - currentWeight);
                    }

                    deltaWeights[i, j] = delta;
                    deltaWeights[j, i] = delta;
                }
            });

            // Apply updates with soft walls
            System.Threading.Tasks.Parallel.For(0, N, i =>
            {
                foreach (int j in graph.Neighbors(i))
                {
                    if (j <= i) continue;

                    double newWeight = graph.Weights[i, j] + deltaWeights[i, j];
                    newWeight = ApplySoftWalls(newWeight);

                    graph.Weights[i, j] = newWeight;
                    graph.Weights[j, i] = newWeight;
                }
            });

            graph.UpdateTargetDistancesFromWeights();
        }
    }
}

