using System;

namespace RQSimulation
{
    /// <summary>
    /// Computes the spectral action for dimension stabilization.
    /// 
    /// PHYSICS (Chamseddine-Connes Spectral Action Principle):
    /// ========================================================
    /// The spectral action principle states that the fundamental action
    /// is S = Tr(f(D/?)) where D is the Dirac operator and ? is the UV cutoff.
    /// 
    /// For a graph, we approximate D via the graph Laplacian and
    /// compute the trace over its spectrum. The action expansion yields:
    /// 
    /// S = f? ?? ? ?g d?x                      (cosmological term - volume)
    ///   + f? ?? ? R ?g d?x                    (Einstein-Hilbert - curvature)
    ///   + f? ? (C_????)? ?g d?x               (Weyl curvature - conformal)
    ///   + O(??)                               (higher order terms)
    /// 
    /// RQ-HYPOTHESIS STAGE 5:
    /// =====================
    /// This replaces the artificial DimensionPenalty with a physics-motivated
    /// spectral action. The 4D spacetime should emerge as the configuration
    /// minimizing the total spectral action, not from external forcing.
    /// 
    /// The Mexican hat potential for dimension stabilization:
    /// V(d_S) = ? · (d_S - 4)? · ((d_S - 4)? - 1)
    /// has minima at d_S = 4 and saddle points at d_S ? 3, 5
    /// </summary>
    public static class SpectralAction
    {
        /// <summary>
        /// Compute total spectral action for the graph configuration.
        /// Returns the action value S - lower values are energetically preferred.
        /// 
        /// PHYSICS:
        /// - S_cosmological: Volume term (f? ?? V)
        /// - S_einstein: Curvature integral (f? ?? ?R)
        /// - S_weyl: Weyl tensor contribution (f? ?C?)
        /// - S_dimension: Mexican hat potential for d_S stabilization
        /// </summary>
        /// <param name="graph">The RQGraph to compute action for</param>
        /// <returns>Total spectral action value (double precision)</returns>
        public static double ComputeSpectralAction(RQGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            
            if (graph.N < 3)
                return 0.0;
            
            // Get spectral action constants
            double lambda = PhysicsConstants.SpectralActionConstants.LambdaCutoff;
            double f0 = PhysicsConstants.SpectralActionConstants.F0_Cosmological;
            double f2 = PhysicsConstants.SpectralActionConstants.F2_EinsteinHilbert;
            double f4 = PhysicsConstants.SpectralActionConstants.F4_Weyl;
            
            // Compute geometric quantities
            double volume = ComputeEffectiveVolume(graph);
            double avgCurvature = ComputeAverageCurvature(graph);
            double weylSquared = ComputeWeylSquared(graph);
            
            // Spectral action expansion terms
            // S? = f? · ?? · V (cosmological / volume term)
            double lambda4 = lambda * lambda * lambda * lambda;
            double S_cosmological = f0 * lambda4 * volume;
            
            // S? = f? · ?? · ?R?g (Einstein-Hilbert / curvature term)
            double lambda2 = lambda * lambda;
            double S_einstein = f2 * lambda2 * avgCurvature * volume;
            
            // S? = f? · ?(C_????)??g (Weyl / conformal term)
            double S_weyl = f4 * weylSquared * volume;
            
            // Dimension stabilization via Mexican hat potential
            double S_dimension = ComputeDimensionPotential(graph);
            
            return S_cosmological + S_einstein + S_weyl + S_dimension;
        }
        
        /// <summary>
        /// Compute only the dimension stabilization component of the spectral action.
        /// This replaces the legacy DimensionPenalty with a physics-motivated potential.
        /// 
        /// Mexican hat potential: V(d) = ? · (d - d?)? · ((d - d?)? - w?)
        /// where d? = 4 (target dimension) and w controls the potential width.
        /// 
        /// This potential has:
        /// - Global minimum at d = 4
        /// - Local barriers at d ? 3 and d ? 5
        /// - Smooth energy landscape for gradient-based optimization
        /// </summary>
        /// <param name="graph">The RQGraph to compute potential for</param>
        /// <returns>Dimension potential energy contribution</returns>
        public static double ComputeDimensionPotential(RQGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            
            // Estimate spectral dimension using fast method
            double d_S = EstimateSpectralDimensionFast(graph);
            
            // Get potential parameters
            double d_target = PhysicsConstants.SpectralActionConstants.TargetSpectralDimension;
            double strength = PhysicsConstants.SpectralActionConstants.DimensionPotentialStrength;
            double width = PhysicsConstants.SpectralActionConstants.DimensionPotentialWidth;
            
            // Deviation from target dimension
            double deviation = d_S - d_target;
            double dev2 = deviation * deviation;
            
            // Mexican hat potential: V = ? · (d - 4)? · ((d - 4)? - w?)
            // This has minimum at d = 4 and creates barriers for large deviations
            double potential = strength * dev2 * (dev2 - width * width);
            
            return potential;
        }
        
        /// <summary>
        /// Compute effective volume of the graph.
        /// Volume is defined as the sum of all edge weights (total "connectivity mass").
        /// 
        /// PHYSICS: In spectral geometry, volume ? Tr(1) which for a graph
        /// is proportional to the total weighted edge count.
        /// </summary>
        /// <param name="graph">The RQGraph</param>
        /// <returns>Effective volume (double precision)</returns>
        public static double ComputeEffectiveVolume(RQGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            
            double volume = 0.0;
            int n = graph.N;
            
            for (int i = 0; i < n; i++)
            {
                foreach (int j in graph.Neighbors(i))
                {
                    if (j > i) // Count each edge once
                    {
                        volume += graph.Weights[i, j];
                    }
                }
            }
            
            return volume;
        }
        
        /// <summary>
        /// Compute average scalar curvature of the graph.
        /// Uses the local curvature (degree deviation from mean) as a proxy.
        /// 
        /// PHYSICS: Average curvature ?R? = (1/V) ? R ?g d?x
        /// For a graph, this is the mean of local curvatures.
        /// </summary>
        /// <param name="graph">The RQGraph</param>
        /// <returns>Average curvature (double precision)</returns>
        public static double ComputeAverageCurvature(RQGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            
            int n = graph.N;
            if (n == 0)
                return 0.0;
            
            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                sum += graph.GetLocalCurvature(i);
            }
            
            return sum / n;
        }
        
        /// <summary>
        /// Compute Weyl curvature squared (proxy via curvature variance).
        /// 
        /// PHYSICS: The Weyl tensor C_???? measures the traceless part of
        /// Riemann curvature. For conformal invariance, |C|? = 0 implies
        /// conformally flat spacetime.
        /// 
        /// For a graph, we approximate |C|? by the variance of local curvatures:
        /// |C|? ? Var(R) = ?R?? - ?R??
        /// 
        /// High variance indicates inhomogeneous geometry (non-conformally-flat).
        /// </summary>
        /// <param name="graph">The RQGraph</param>
        /// <returns>Weyl curvature squared proxy (double precision)</returns>
        public static double ComputeWeylSquared(RQGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            
            int n = graph.N;
            if (n == 0)
                return 0.0;
            
            // Compute mean and variance of curvature
            double sum = 0.0;
            double sumSq = 0.0;
            
            for (int i = 0; i < n; i++)
            {
                double R = graph.GetLocalCurvature(i);
                sum += R;
                sumSq += R * R;
            }
            
            double mean = sum / n;
            double variance = (sumSq / n) - (mean * mean);
            
            return Math.Max(0.0, variance);
        }
        
        /// <summary>
        /// Fast estimation of spectral dimension using degree distribution.
        /// 
        /// PHYSICS: For a d-dimensional regular lattice:
        /// - Each node has 2d neighbors
        /// - Return probability P(t) ~ t^(-d/2)
        /// - Spectral dimension d_S = -2 d(log P)/d(log t)
        /// 
        /// We approximate d_S using the average degree:
        /// d_S ? 2 log(k_avg) / log(2 k_avg - 1)
        /// 
        /// This is faster than full random walk or eigenvalue methods.
        /// </summary>
        /// <param name="graph">The RQGraph</param>
        /// <param name="walks">Number of random walks (unused in fast method)</param>
        /// <param name="steps">Walk length (unused in fast method)</param>
        /// <returns>Estimated spectral dimension (double precision)</returns>
        public static double EstimateSpectralDimensionFast(RQGraph graph, int walks = 100, int steps = 50)
        {
            ArgumentNullException.ThrowIfNull(graph);
            
            int n = graph.N;
            if (n < 3)
                return 1.0;
            
            // Compute average degree
            double totalDegree = 0.0;
            for (int i = 0; i < n; i++)
            {
                int degree = 0;
                foreach (var _ in graph.Neighbors(i))
                    degree++;
                totalDegree += degree;
            }
            
            double avgDegree = totalDegree / n;
            
            // Minimum degree for meaningful dimension
            if (avgDegree < 2.0)
                return 1.0;
            
            // Approximate spectral dimension from degree
            // For d-dimensional lattice: k_avg = 2d, so d = k_avg / 2
            // But for general graphs, use logarithmic relation:
            // d_S ? 2 log(k) / log(2k - 1)
            double denominator = Math.Log(2.0 * avgDegree - 1.0);
            if (Math.Abs(denominator) < 1e-10)
                return 2.0;
            
            double d_S = 2.0 * Math.Log(avgDegree) / denominator;
            
            // Clamp to reasonable range [1, 8]
            return Math.Clamp(d_S, 1.0, 8.0);
        }
        
        /// <summary>
        /// Compute the gradient of spectral action with respect to edge weight.
        /// Used for optimization-based topology evolution.
        /// 
        /// ?S/?w_ij ? (S(w_ij + ?) - S(w_ij - ?)) / (2?)
        /// 
        /// This uses numerical differentiation with small ?.
        /// </summary>
        /// <param name="graph">The RQGraph</param>
        /// <param name="i">First node of edge</param>
        /// <param name="j">Second node of edge</param>
        /// <param name="epsilon">Differentiation step size (default 1e-4)</param>
        /// <returns>Gradient component for edge (i,j)</returns>
        public static double ComputeActionGradient(RQGraph graph, int i, int j, double epsilon = 1e-4)
        {
            ArgumentNullException.ThrowIfNull(graph);
            
            if (!graph.Edges[i, j])
                return 0.0;
            
            double originalWeight = graph.Weights[i, j];
            
            // Forward evaluation: w + ?
            graph.Weights[i, j] = originalWeight + epsilon;
            graph.Weights[j, i] = originalWeight + epsilon;
            double S_plus = ComputeSpectralAction(graph);
            
            // Backward evaluation: w - ?
            graph.Weights[i, j] = originalWeight - epsilon;
            graph.Weights[j, i] = originalWeight - epsilon;
            double S_minus = ComputeSpectralAction(graph);
            
            // Restore original weight
            graph.Weights[i, j] = originalWeight;
            graph.Weights[j, i] = originalWeight;
            
            // Central difference gradient
            return (S_plus - S_minus) / (2.0 * epsilon);
        }
        
        /// <summary>
        /// Check if the current configuration is near a spectral action minimum.
        /// Returns true if gradient magnitude is below threshold.
        /// </summary>
        /// <param name="graph">The RQGraph</param>
        /// <param name="threshold">Gradient magnitude threshold (default 0.01)</param>
        /// <returns>True if near energy minimum</returns>
        public static bool IsNearActionMinimum(RQGraph graph, double threshold = 0.01)
        {
            ArgumentNullException.ThrowIfNull(graph);
            
            double gradientMagnitude = 0.0;
            int edgeCount = 0;
            
            for (int i = 0; i < graph.N; i++)
            {
                foreach (int j in graph.Neighbors(i))
                {
                    if (j > i)
                    {
                        double grad = ComputeActionGradient(graph, i, j);
                        gradientMagnitude += grad * grad;
                        edgeCount++;
                    }
                }
            }
            
            if (edgeCount == 0)
                return true;
            
            double rmsGradient = Math.Sqrt(gradientMagnitude / edgeCount);
            return rmsGradient < threshold;
        }

        /// <summary>
        /// Computes the spectral action contribution of a single node.
        ///
        /// The per-node decomposition is:
        ///   S_node(i) = S_volume(i) + S_curvature(i) + S_vacuum(i)
        ///
        /// Where:
        /// - S_volume(i)    = ?_{j ? N(i)} w_{ij} / 2   (half-edge weight sum)
        /// - S_curvature(i) = R_i ? S_volume(i)          (local curvature ? local volume)
        /// - S_vacuum(i)    = E_vac(i)                   (vacuum field energy at node)
        ///
        /// This decomposition satisfies: ?_i S_node(i) ? S_total (up to global constants).
        /// </summary>
        /// <param name="graph">The RQ graph</param>
        /// <param name="nodeIndex">Index of the node</param>
        /// <returns>Spectral action contribution of this node</returns>
        public static double ComputeNodeSpectralContribution(RQGraph graph, int nodeIndex)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if (nodeIndex < 0 || nodeIndex >= graph.N)
            {
                return 0.0;
            }

            // Local volume: half-sum of incident edge weights (each edge shared by two nodes)
            double localVolume = 0.0;
            foreach (int j in graph.Neighbors(nodeIndex))
            {
                localVolume += graph.Weights[nodeIndex, j];
            }
            localVolume *= 0.5;

            // Local curvature contribution
            double localCurvature = graph.GetLocalCurvature(nodeIndex);

            // Spectral action constants
            double lambda = PhysicsConstants.SpectralActionConstants.LambdaCutoff;
            double f0 = PhysicsConstants.SpectralActionConstants.F0_Cosmological;
            double f2 = PhysicsConstants.SpectralActionConstants.F2_EinsteinHilbert;

            double lambda4 = lambda * lambda * lambda * lambda;
            double lambda2 = lambda * lambda;

            // Cosmological (volume) term
            double sVolume = f0 * lambda4 * localVolume;

            // Einstein-Hilbert (curvature ? volume) term
            double sCurvature = f2 * lambda2 * localCurvature * localVolume;

            // Vacuum energy term (from vacuum field if initialized)
            var vacField = graph.VacuumEnergyField;
            double sVacuum = vacField.Length > nodeIndex ? vacField[nodeIndex] : 0.0;

            return sVolume + sCurvature + sVacuum;
        }
    }
}
