using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RQSimulation
{
    // Core type declarations (single source)
    public enum ParticleType { Vacuum, Fermion, Boson, Composite }
    public enum NodeState { Rest = 0, Excited = 1, Refractory = 2 }
    public struct NodePhysics
    {
        public ParticleType Type;
        public double Mass;
        public double Charge;
        public double Spin;
        public int Occupation;
        public int MaxOccupation;
        public bool IsClock;
    }

    public partial class RQGraph
    {
        public const double HeavyClusterThreshold = PhysicsConstants.DefaultHeavyClusterThreshold;
        public const int HeavyClusterMinSize = 3;

        public int N { get; private set; }
        public bool[,] Edges { get; private set; }
        public double[,] Weights { get; private set; }
        public double[] NodeWeights { get; private set; } // Node weights for weighted Forman-Ricci curvature
        public NodeState[] State { get; private set; }

        /// <summary>
        /// Incremented each time graph topology changes (edges added/removed).
        /// Used by ParallelEventEngine to detect when coloring needs recomputation.
        /// </summary>
        public int TopologyVersion { get; private set; }
        private NodeState[] _nextState;
        private bool[,] _measurementBond;
        private int[] _degree;
        private int[] _charges;
        private int[] _refractoryCounter;
        private readonly Random _rng;
        private readonly int _targetDegree;
        private readonly double _lambdaState;
        private double _temperature;
        private readonly double _edgeTrialProbability;
        private double _measurementThreshold;
        private int[] _targetDegreePerNode;
        public NodePhysics[] PhysicsProperties;

        /// <summary>
        /// DEPRECATED: Use graph-based distances (ShortestPathDistance, GetGraphDistance) for physics calculations.
        /// Coordinates are only for visualization purposes and should not affect physical dynamics.
        /// </summary>
        [Obsolete("Use ONLY for rendering, not for physics! Use ShortestPathDistance() or GetGraphDistance() instead.")]
        public (double X, double Y)[] Coordinates;
        private double[] _nodeEnergy;
        public double[] NodeEnergy => _nodeEnergy;
        private double _adaptiveHeavyThreshold = HeavyClusterThreshold;
        public double AdaptiveHeavyThreshold => _adaptiveHeavyThreshold;
        public double[] ProperTime;
        public double[,] EdgeDelay;
        public sbyte[,] EdgeDirection;
        private double[,] _targetDistance;
        private const double EpsDist = 1e-6;
        public double EnergyBreakThreshold { get; set; } = 5.0;
        public double EnergySewThreshold { get; set; } = 1.0;
        private double _hebbRate = 0.25;
        private double _decayRate = 0.002;
        private bool _hasHeavyClusters;

        // Add missing public properties/fields required by other partials and examples
        public int QuantumComponents { get; private set; } = 1;
        public double CorrelationTime { get; private set; } = 0.0;
        public double EdgeDecayLength { get; set; } = 0.2;
        public double PropagationLength { get; set; } = 0.1;
        public double EnergyDiffusionRate { get; set; } = 0.05;

        public int[] FractalLevel; // fractal level per node
        public double[] LocalPotential; // local potential per node
        public double[] StoredEnergy; // checklist energy accumulator
        public bool[] ParticleTag;    // checklist particle birth marker
        public double[,] PathWeight;  // checklist dynamic corridor weights
        // Removed StructuralMass as per RQ-Hypothesis unification
        public int[] Domain; // checklist item 3 large-scale domains

        /// <summary>
        /// GPU Gravity Engine for accelerated network geometry evolution.
        /// When non-null, EvolveNetworkGeometry will use GPU computation.
        /// 
        /// NOTE: Currently prepared but not active due to pre-existing GPU shader
        /// compilation issues (missing ComputeSharp descriptors). Once shaders are
        /// fixed, GPU computation will activate automatically for 10-100x speedup.
        /// </summary>
        public RQSimulation.GPUOptimized.GpuGravityEngine? GpuGravity { get; set; }

        // Missing private fields referenced in InitClocks
        private List<int> _clockNodes = new();

        /// <summary>
        /// DEPRECATED: This method uses external coordinates which violates RQ-hypothesis.
        /// Use GetGraphDistanceWeighted() for physics calculations instead.
        /// </summary>
        [Obsolete("Use GetGraphDistanceWeighted() for physics, this method is for rendering only.")]
        public double GetPhysicalDistance(int a, int b)
        {
#pragma warning disable CS0618 // Suppress obsolete warning for Coordinates access
            if (Coordinates == null || a < 0 || b < 0 || a >= N || b >= N) return 0.0;
            var (x1, y1) = Coordinates[a]; var (x2, y2) = Coordinates[b];
#pragma warning restore CS0618
            double dx = x1 - x2; double dy = y1 - y2; return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Compute graph distance using shortest path with metric d = -ln(w).
        /// This is the RQ-compliant distance measure based purely on graph topology.
        /// Implements checklist item 1.1: Background-independent distance.
        /// </summary>
        /// <param name="startNode">Starting node index</param>
        /// <param name="endNode">Ending node index</param>
        /// <returns>Topological distance based on weighted shortest path</returns>
        public double GetGraphDistanceWeighted(int startNode, int endNode)
        {
            // Delegate to existing Dijkstra implementation
            return ShortestPathDistance(startNode, endNode);
        }

        public enum LocalPhase { Cold, Warm, Hot }
        public LocalPhase[] NodePhase { get; set; }
        public double GlobalExcitationRegulator { get; set; } = 1.0;

        public enum GraphPhase { Quiet, MetaStable, Active }
        public GraphPhase CurrentPhase { get; set; } = GraphPhase.Quiet;

        /// <summary>
        /// Determines whether a node is a vacuum node (no significant matter content).
        ///
        /// A node is vacuum if:
        /// - Its ParticleType is Vacuum, OR
        /// - Its correlation mass is below the threshold
        /// AND it is not part of a heavy cluster (degree-based heuristic).
        /// </summary>
        /// <param name="nodeIndex">Index of the node to check</param>
        /// <param name="massThreshold">Mass below which a node counts as vacuum</param>
        /// <returns>True if the node is a vacuum node</returns>
        public bool IsVacuumNode(int nodeIndex, double massThreshold = 1e-6)
        {
            if (nodeIndex < 0 || nodeIndex >= N)
            {
                return false;
            }

            // Check particle type if physics properties are initialized
            if (PhysicsProperties != null && PhysicsProperties.Length > nodeIndex)
            {
                if (PhysicsProperties[nodeIndex].Type != ParticleType.Vacuum)
                {
                    return false;
                }
            }

            // Check correlation mass
            double mass = GetNodeMass(nodeIndex);
            return mass < massThreshold;
        }

        /// <summary>
        /// Global energy ledger for strict conservation.
        /// </summary>
        private readonly EnergyLedger _ledger = new EnergyLedger();
        public EnergyLedger Ledger => _ledger;
    }
}
