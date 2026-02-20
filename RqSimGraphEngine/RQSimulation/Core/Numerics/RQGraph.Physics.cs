using System;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        public enum ColorCharge { Red, Green, Blue, None }
        public ColorCharge[] ColorCharges; // SU(3) color per node (optional)
        private int[] _isospin; // weak isospin doublet index (0 or 1)

        // Correlation-mass cache
        private double[] _correlationMass;
        private double _avgCorrelationMass;
        
        /// <summary>
        /// Gets the correlation mass array. Returns null if not computed.
        /// Call EnsureCorrelationMassComputed() before accessing.
        /// </summary>
        public double[] CorrelationMass => _correlationMass;
        
        /// <summary>
        /// Gets the average correlation mass.
        /// </summary>
        public double AverageCorrelationMass => _avgCorrelationMass;
        
        /// <summary>
        /// Ensures correlation mass is computed. Call this before accessing CorrelationMass.
        /// </summary>
        public void EnsureCorrelationMassComputed()
        {
            if (_correlationMass == null || _correlationMass.Length != N)
            {
                RecomputeCorrelationMass();
            }
        }

        // Compute node mass strictly from correlation weights (sum of squares invariant)
        private double ComputeNodeMass(int i)
        {
            double sum = 0.0;
            for (int j = 0; j < N; j++)
            {
                double w = Weights[i, j];
                if (w > 0.0) sum += w * w;
            }
            return Math.Sqrt(sum);
        }

        // Recompute correlation mass vector and average (median-based adaptive thresholding not needed here)
        // Made internal to allow GPU engine to call after syncing weights
        internal void RecomputeCorrelationMass()
        {
            if (_correlationMass == null || _correlationMass.Length != N)
                _correlationMass = new double[N];
            double total = 0.0;
            for (int i = 0; i < N; i++)
            {
                double m = ComputeNodeMass(i);
                _correlationMass[i] = m;
                total += m;
            }
            _avgCorrelationMass = N > 0 ? total / N : 0.0;
        }

        /// <summary>
        /// Initialize physics (matter distribution) with given fraction of fermions plus color and isospin assignments.
        /// Mass now derives purely from correlation weights; remove manual mass constants.
        /// </summary>
        public void InitPhysics(double matterFraction)
        {
            if (matterFraction < 0) matterFraction = 0;
            if (matterFraction > 1) matterFraction = 1;
            PhysicsProperties = new NodePhysics[N];
            ColorCharges = new ColorCharge[N];
            _isospin = new int[N];
            if (_targetDegreePerNode == null || _targetDegreePerNode.Length != N)
            {
                _targetDegreePerNode = new int[N];
                for (int i = 0; i < N; i++) _targetDegreePerNode[i] = _targetDegree;
            }
            int matterCount = (int)(N * matterFraction);
            var shuffled = Enumerable.Range(0, N).OrderBy(_ => _rng.Next()).ToArray();
            for (int k = 0; k < N; k++)
            {
                int node = shuffled[k];
                bool isMatter = k < matterCount;
                if (isMatter)
                {
                    PhysicsProperties[node] = new NodePhysics
                    {
                        Type = ParticleType.Fermion,
                        Mass = 0.0, // legacy field no longer used; kept for API compatibility
                        Charge = -1.0,
                        Spin = 0.5,
                        Occupation = 1,
                        MaxOccupation = 1
                    };
                    ColorCharges[node] = (ColorCharge)_rng.Next(3);
                    _isospin[node] = _rng.Next(2);
                }
                else
                {
                    PhysicsProperties[node] = new NodePhysics
                    {
                        Type = ParticleType.Vacuum,
                        Mass = 0.0,
                        Charge = 0.0,
                        Spin = 0.0,
                        Occupation = 0,
                        MaxOccupation = 10
                    };
                    ColorCharges[node] = ColorCharge.None;
                    _isospin[node] = 0;
                }
                _targetDegreePerNode[node] = _targetDegree;
            }
            InitProperTime();
            InitEdgeData();
            RecomputeCorrelationMass();
        }
    }
}
