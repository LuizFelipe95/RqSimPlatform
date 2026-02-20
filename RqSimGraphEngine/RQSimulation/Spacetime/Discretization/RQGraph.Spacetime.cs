using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RQSimulation
{
    /// <summary>
    /// Implements relativistic spacetime dynamics based on the RQ hypothesis.
    /// Spacetime emerges from correlation structure; curvature arises from
    /// correlation density gradients.
    /// 
    /// RQ-FIX: Removed external coordinate arrays (_nodeTimeCoord, etc.) to enforce
    /// background independence. All physics must be relational.
    /// </summary>
    public partial class RQGraph
    {
        // Spacetime interval accumulator
        private double[]? _properTimeAccum;

        // Causal structure tracking
        private bool[,]? _causallyConnected;
        private double[,]? _lightConeDistance;

        /// <summary>
        /// Initializes relativistic spacetime coordinates from 2D layout.
        /// RQ-FIX: Coordinates are now purely for visualization, not physics.
        /// </summary>
        public void InitSpacetimeCoordinates()
        {
            _properTimeAccum = new double[N];
            for (int i = 0; i < N; i++)
            {
                _properTimeAccum[i] = 0.0;
            }

            InitCausalStructure();
        }

        /// <summary>
        /// Initialize causal structure matrices.
        /// </summary>
        private void InitCausalStructure()
        {
            _causallyConnected = new bool[N, N];
            _lightConeDistance = new double[N, N];

            // Initially, adjacent nodes are potentially causally connected
            for (int i = 0; i < N; i++)
            {
                for (int j = 0; j < N; j++)
                {
                    if (i == j)
                    {
                        _causallyConnected[i, j] = true;
                        _lightConeDistance[i, j] = 0.0;
                    }
                    else if (Edges[i, j])
                    {
                        _causallyConnected[i, j] = true;
                        // Use graph distance for light cone initialization
                        double d = GetGraphDistanceWeighted(i, j);
                        _lightConeDistance[i, j] = d;
                    }
                }
            }
        }

        /// <summary>
        /// Checks if two nodes are causally connected (within each other's light cones).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AreCausallyConnected(int i, int j)
        {
            if (_causallyConnected == null) return true;
            return _causallyConnected[i, j];
        }

        /// <summary>
        /// Updates causal structure based on current spacetime coordinates.
        /// RQ-Hypothesis Compliant: Uses topological graph distance for causality checks.
        /// </summary>
        public void UpdateCausalStructure()
        {
            // RQ-FIX: Use relational time difference instead of coordinate time
            // This requires tracking proper time history or using path integrals
            // For now, we approximate using current proper time difference
            
            if (_properTimeAccum == null) return;

            Parallel.For(0, N, i =>
            {
                for (int j = 0; j < N; j++)
                {
                    if (i == j) continue;

                    // Relational check: d(i,j) <= c * |tau_i - tau_j|
                    // This is a simplified light cone check in proper time
                    double dist = GetGraphDistanceWeighted(i, j);
                    double dTau = Math.Abs(_properTimeAccum[i] - _properTimeAccum[j]);
                    
                    bool causal = dist <= VectorMath.SpeedOfLight * dTau;

                    _causallyConnected![i, j] = causal;

                    if (causal)
                    {
                        _lightConeDistance![i, j] = dist;
                    }
                }
            });
        }

        /// <summary>
        /// Computes enhanced gravitational time dilation for a node using Schwarzschild metric.
        /// This provides a more accurate version than the simple one in Statistics.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetSchwarzschildTimeDilation(int node)
        {
            if (_correlationMass == null || node >= _correlationMass.Length)
                return 1.0;

            double localMass = _correlationMass[node];
            
            // Effective "radius" from average link distance
            double r = 1.0;
            int count = 0;
            foreach (int nb in Neighbors(node))
            {
                double d = GetGraphDistanceWeighted(node, nb);
                if (d > 0) { r += d; count++; }
            }
            if (count > 0) r = r / count;
            if (r < 0.01) r = 0.01;

            // Schwarzschild-like time dilation
            double rs = 2.0 * localMass / (VectorMath.SpeedOfLight * VectorMath.SpeedOfLight);
            double factor = 1.0 - rs / r;
            return factor > 0 ? Math.Sqrt(factor) : 0.01;
        }

        /// <summary>
        /// Advances proper time for all nodes accounting for gravitational time dilation.
        /// </summary>
        public void AdvanceProperTime(double globalDt)
        {
            if (_properTimeAccum == null)
                return;

            for (int i = 0; i < N; i++)
            {
                double dilation = GetGravitationalTimeDilation(i);
                double localDt = globalDt * dilation;

                _properTimeAccum[i] += localDt;
            }
        }

        /// <summary>
        /// Computes geodesic deviation (relative acceleration magnitude) between two nearby nodes
        /// due to spacetime curvature.
        /// RQ-Hypothesis Compliant: Uses topological graph distance instead of coordinates.
        /// Returns scalar magnitude of tidal acceleration.
        /// </summary>
        public double ComputeGeodesicDeviationScalar(int i, int j)
        {
            if (_correlationMass == null)
                return 0.0;

            // Topological distance
            double r = GetGraphDistanceWeighted(i, j);
            if (r < 1e-10) return 0.0;

            // Curvature tensor component approximation from mass gradient
            // In RQ, mass is correlation density, so gradient implies curvature gradient
            double mi = _correlationMass[i];
            double mj = _correlationMass[j];
            double massGradient = (mj - mi) / r;

            // Tidal acceleration magnitude ~ R * separation
            // a = -R * d
            // Here we approximate R ~ massGradient (Newtonian-like tidal force gradient)
            double curvature = massGradient * 0.1; 
            double a = curvature * r;

            return a;
        }

        // Tolerance for light cone constraint check (accounts for numerical precision and lattice discretization)
        private const double LightConeTolerance = 0.1;

        /// <summary>
        /// Propagates signals respecting the light cone constraint.
        /// Updates node states only if causally connected.
        /// </summary>
        public void PropagateSignalsWithCausality()
        {
            if (_causallyConnected == null) return;

            var newStates = new NodeState[N];
            Array.Copy(State, newStates, N);

            for (int i = 0; i < N; i++)
            {
                if (State[i] != NodeState.Rest) continue;

                int excitedNeighborCount = 0;
                double weightSum = 0.0;

                foreach (int j in Neighbors(i))
                {
                    // Only propagate if causally connected
                    if (!_causallyConnected[i, j]) continue;
                    if (State[j] != NodeState.Excited) continue;

                    // Check light cone constraint
                    // RQ-FIX: Use proper time difference
                    double dt = _properTimeAccum != null ? Math.Abs(_properTimeAccum[i] - _properTimeAccum[j]) : 0;
                    double dr = GetGraphDistanceWeighted(i, j);
                    if (dr > VectorMath.SpeedOfLight * dt + LightConeTolerance) continue;

                    excitedNeighborCount++;
                    weightSum += Weights[i, j];
                }

                if (excitedNeighborCount > 0)
                {
                    double prob = weightSum / (1.0 + weightSum);
                    if (_rng.NextDouble() < prob)
                    {
                        newStates[i] = NodeState.Excited;
                    }
                }
            }

            Array.Copy(newStates, State, N);
        }

        /// <summary>
        /// Computes the Ricci tensor component R_00 for a node based on local correlation structure.
        /// </summary>
        public double ComputeLocalRicci00(int node)
        {
            if (_correlationMass == null) return 0.0;

            // R_00 ≈ 4πG * (ρ + 3p/c^2) in GR
            // Here ρ is the local correlation mass density
            double localMass = _correlationMass[node];
            int deg = Degree(node);
            if (deg == 0) return 0.0;

            // Estimate local volume from average link distance
            double avgDist = 0.0;
            foreach (int nb in Neighbors(node))
            {
                avgDist += GetGraphDistanceWeighted(node, nb);
            }
            avgDist /= deg;

            double localVolume = Math.PI * avgDist * avgDist;  // Approximate as disk
            double density = localMass / Math.Max(1e-10, localVolume);

            // Include pressure estimate from edge tensions
            double pressure = 0.0;
            foreach (int nb in Neighbors(node))
            {
                double w = Weights[node, nb];
                pressure += w * w;
            }
            pressure /= deg;

            double c2 = VectorMath.SpeedOfLight * VectorMath.SpeedOfLight;
            return 4.0 * Math.PI * (density + 3.0 * pressure / c2);
        }
    }
}
