using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace RQSimulation
{
    /// <summary>
    /// Legacy API compatibility methods for RQGraph.
    /// Contains methods migrated from OLD/RQGraph.ApiCompat.cs and OLD/RQGraph.SOA.cs for backward compatibility.
    /// These methods are used by ExampleModernSimulation and other legacy code.
    /// </summary>
    public partial class RQGraph
    {
        // === Constants for dimensionless scales ===
        private const int DefaultRefractorySteps = 3;
        private const int DefaultGeometryTrainingSteps = 100;
        private const int DefaultGeometryUpdatesPerStep = 5;
        private const int DefaultFeedbackTrainingSteps = 100;
        private const double DefaultAlphaCorr = 0.1;
        private const double DefaultEdgeCost = 0.01;
        private const double DefaultCorrGain = 0.05;
        private const double DefaultNuclearBondThreshold = 0.6;
        private const double DefaultRelationalDtOffset = 5.0;

        // === SoA (Structure-of-Arrays) storage for GPU optimization ===

        /// <summary>SoA node states (maps to NodeState enum)</summary>
        public byte[] NodeStatesSoA { get; private set; } = Array.Empty<byte>();

        /// <summary>SoA refractory timers</summary>
        public int[] RefractoryTimersSoA { get; private set; } = Array.Empty<int>();

        /// <summary>SoA node energies</summary>
        public double[] NodeEnergiesSoA { get; private set; } = Array.Empty<double>();

        // CSR (Compressed Sparse Row) adjacency format
        /// <summary>CSR row offsets (length = N+1)</summary>
        public int[] CsrOffsets { get; private set; } = Array.Empty<int>();

        /// <summary>CSR column indices</summary>
        public int[] CsrIndices { get; private set; } = Array.Empty<int>();

        /// <summary>Edge weights flat for CSR (parallel to CsrIndices)</summary>
        public double[] EdgeWeightsFlat { get; private set; } = Array.Empty<double>();

        /// <summary>Gauge U(1) phase per edge (parallel to CsrIndices)</summary>
        public double[] EdgePhases { get; private set; } = Array.Empty<double>();

        /// <summary>Momentum buffer for symplectic integration of edge phases</summary>
        public double[] EdgePhaseMomentum { get; private set; } = Array.Empty<double>();

        /// <summary>CSR complex edges with magnitude and phase</summary>
        public ComplexEdge[] CsrEdges { get; private set; } = Array.Empty<ComplexEdge>();

        /// <summary>SoA wavefunction real part</summary>
        public double[] PsiRe { get; private set; } = Array.Empty<double>();

        /// <summary>SoA wavefunction imaginary part</summary>
        public double[] PsiIm { get; private set; } = Array.Empty<double>();

        /// <summary>SoA wavefunction derivative real part</summary>
        public double[] PsiDotRe { get; private set; } = Array.Empty<double>();

        /// <summary>SoA wavefunction derivative imaginary part</summary>
        public double[] PsiDotIm { get; private set; } = Array.Empty<double>();

        /// <summary>
        /// Dynamic refractory steps based on relational time.
        /// Single authoritative baseline for refractory period.
        /// </summary>
        public int DynamicBaseRefractorySteps
        {
            get
            {
                double dt = ComputeRelationalDt();
                double tau = CorrelationTime > 0.0 ? CorrelationTime : dt;
                int steps = (int)Math.Round(tau / Math.Max(1e-9, dt));
                return Math.Max(1, steps);
            }
        }

        /// <summary>
        /// Compute relational time step based on graph topology.
        /// dt = 1 / (offset + avgDegree)
        /// </summary>
        public double ComputeRelationalDt()
        {
            var es = GetEdgeStats();
            double avgDeg = es.avgDegree <= 0 ? 1.0 : es.avgDegree;
            return 1.0 / (DefaultRelationalDtOffset + avgDeg);
        }

        /// <summary>
        /// Configure the number of quantum components for multi-component wavefunction.
        /// </summary>
        public void ConfigureQuantumComponents(int components)
        {
            QuantumComponents = Math.Max(1, components);
            InitQuantumWavefunction();
        }

        /// <summary>
        /// Configure strong gauge (SU(3) for QCD).
        /// </summary>
        public void ConfigureStrongGauge() => ConfigureGaugeDimension(3);

        /// <summary>
        /// Configure weak gauge dimensions (SU(2) for electroweak).
        /// </summary>
        public void ConfigureWeakGaugeDimensions(int su2Dim = 2)
        {
            // RQ-HYPOTHESIS: _gaugeSU2 is now SU2Matrix[,] for proper group structure
            _gaugeSU2 = new Gauge.SU2Matrix[N, N];
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    _gaugeSU2[i, j] = Gauge.SU2Matrix.Identity;
                }
            }
            _gaugeU1 = new double[N, N];
        }

        /// <summary>
        /// Build Structure-of-Arrays views from current graph matrices.
        /// Call after topology changes for GPU optimization.
        /// </summary>
        public void BuildSoAViews()
        {
            // Hot arrays - pinned for GPU access
            NodeStatesSoA = GC.AllocateArray<byte>(N, pinned: true);
            RefractoryTimersSoA = GC.AllocateArray<int>(N, pinned: true);
            NodeEnergiesSoA = GC.AllocateArray<double>(N, pinned: true);

            // Map state to bytes
            for (int i = 0; i < N; i++)
            {
                NodeStatesSoA[i] = (byte)State[i];
                if (_nodeEnergy != null && i < _nodeEnergy.Length)
                    NodeEnergiesSoA[i] = _nodeEnergy[i];
            }

            // Build CSR from adjacency
            int totalEdges = 0;
            var degrees = new int[N];
            for (int i = 0; i < N; i++)
            {
                int deg = 0;
                foreach (var nb in Neighbors(i)) deg++;
                degrees[i] = deg;
                totalEdges += deg;
            }

            CsrOffsets = GC.AllocateArray<int>(N + 1, pinned: true);
            CsrIndices = GC.AllocateArray<int>(totalEdges, pinned: true);
            EdgeWeightsFlat = GC.AllocateArray<double>(totalEdges, pinned: true);
            EdgePhases = GC.AllocateArray<double>(totalEdges, pinned: true);
            EdgePhaseMomentum = GC.AllocateArray<double>(totalEdges, pinned: true);
            CsrEdges = GC.AllocateArray<ComplexEdge>(totalEdges, pinned: true);

            int cursor = 0;
            for (int i = 0; i < N; i++)
            {
                CsrOffsets[i] = cursor;
                foreach (var nb in Neighbors(i))
                {
                    CsrIndices[cursor] = nb;
                    double w = Weights[i, nb];
                    EdgeWeightsFlat[cursor] = w;
                    EdgePhases[cursor] = 0.0;
                    EdgePhaseMomentum[cursor] = 0.0;
                    CsrEdges[cursor] = new ComplexEdge(Math.Abs(w), 0.0);
                    cursor++;
                }
            }
            CsrOffsets[N] = cursor; // sentinel
        }

        /// <summary>
        /// Get U(1) gauge link between nodes as complex number.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Complex GetU1Link(int i, int j)
        {
            if (CsrOffsets == null || CsrOffsets.Length <= i + 1) return Complex.One;
            int start = CsrOffsets[i];
            int end = CsrOffsets[i + 1];
            for (int k = start; k < end; k++)
            {
                if (CsrIndices[k] == j)
                {
                    double theta = CsrEdges[k].Phase;
                    return new Complex(Math.Cos(theta), Math.Sin(theta));
                }
            }
            return Complex.One;
        }

        /// <summary>
        /// Update strong gauge from color currents (SU(3)).
        /// </summary>
        public void UpdateStrongGaugeFromColorCurrents()
        {
            if (GaugeDimension != 3 || _gaugeSU == null || _waveMulti == null) return;

            var edgeStats = GetEdgeStats();
            double avgDegree = edgeStats.avgDegree;
            if (avgDegree <= 0.0) avgDegree = 1.0;
            double kappa = 1.0 / (1.0 + avgDegree);

            int d = 3;
            var updated = new Complex[N, N, d * d];

            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    var J = ColorCurrentOnEdge(i, j);
                    var newMat = new Complex[d * d];

                    for (int r = 0; r < d; r++)
                    {
                        for (int c = 0; c < d; c++)
                        {
                            var Uold = _gaugeSU[i, j, r * d + c];
                            var desired = J[r] * Complex.Conjugate(J[c]);
                            newMat[r * d + c] = Uold + kappa * (desired - Uold);
                        }
                    }

                    ProjectToSUN(newMat);

                    for (int idx = 0; idx < d * d; idx++)
                        updated[i, j, idx] = newMat[idx];
                }
            }

            _gaugeSU = updated;
        }

        // String energy array for confinement strings between quarks
        private double[,]? _stringEnergy;

        /// <summary>
        /// Gets the string energy matrix.
        /// String energy accumulates along edges connecting colored particles.
        /// </summary>
        public double[,]? StringEnergy => _stringEnergy;

        /// <summary>
        /// Global neighbor factor for excitation dynamics adjustment.
        /// Used by AdaptCorrelationTimescales.
        /// </summary>
        public double GlobalNeighbourFactor { get; set; } = 1.0;

        /// <summary>
        /// Global spontaneous factor for excitation dynamics adjustment.
        /// Used by AdaptCorrelationTimescales.
        /// </summary>
        public double GlobalSpontFactor { get; set; } = 1.0;

        /// <summary>
        /// Indicates whether heavy clusters currently exist.
        /// </summary>
        public bool HasHeavyClusters => _hasHeavyClusters;

        /// <summary>
        /// Apply string tension to an edge connecting colored particles.
        /// Implements QCD-like confinement: strong correlation increases tension.
        /// </summary>
        /// <param name="u">First node index</param>
        /// <param name="v">Second node index</param>
        public void ApplyStringTension(int u, int v)
        {
            EnsureStringEnergyArray();

            var stats = GetEdgeStats();
            double tension = 0.1 / (1.0 + stats.avgDegree);

            if (Edges[u, v])
            {
                _stringEnergy![u, v] += tension;
                _stringEnergy[v, u] = _stringEnergy[u, v];
            }
        }

        /// <summary>
        /// Ensures the string energy array is allocated.
        /// </summary>
        private void EnsureStringEnergyArray()
        {
            if (_stringEnergy == null || _stringEnergy.GetLength(0) != N)
            {
                _stringEnergy = new double[N, N];
            }
        }

        /// <summary>
        /// Promote heavy clusters to composite particles with extended logic.
        /// RQ-compliant: Uses energy-based stabilization.
        /// </summary>
        /// <param name="minMass">Minimum mass threshold for cluster promotion</param>
        public void PromoteHeavyClustersToCompositesExtended(double minMass)
        {
            var clusters = GetHeavyClustersByMass(minMass, HeavyClusterThreshold, HeavyClusterMinSize);

            foreach (var cluster in clusters)
            {
                // RQ-compliant: Use energy-based stabilization instead of manual strengthening
                StabilizeClusterViaMetropolisPublic(cluster);

                // Compute cluster mass per checklist
                double clusterMass = 0.0;
                foreach (int u in cluster)
                {
                    foreach (int v in cluster)
                    {
                        if (u < v && Edges[u, v] && Weights[u, v] > HeavyClusterThreshold)
                        {
                            clusterMass += (Weights[u, v] - HeavyClusterThreshold);
                        }
                    }
                }

                double perNodeMass = cluster.Count > 0 ? clusterMass / cluster.Count : 0.0;

                foreach (int node in cluster)
                {
                    PhysicsProperties[node].Type = ParticleType.Composite;
                    PhysicsProperties[node].Mass += perNodeMass;
                    PhysicsProperties[node].Charge = 0;
                    PhysicsProperties[node].Spin = 0;
                }
            }

            _hasHeavyClusters = clusters.Count > 0;
        }

        /// <summary>
        /// Simple version of composite promotion.
        /// RQ-compliant: Uses energy-based stabilization.
        /// </summary>
        /// <param name="minMass">Minimum mass threshold</param>
        public void PromoteHeavyClustersToComposites(double minMass)
        {
            var clusters = GetHeavyClustersByMass(minMass, HeavyClusterThreshold, HeavyClusterMinSize);

            // RQ-compliant: Use energy-based stabilization instead of manual strengthening
            foreach (var c in clusters)
            {
                StabilizeClusterViaMetropolisPublic(c);
            }

            _hasHeavyClusters = clusters.Count > 0;
        }

        /// <summary>
        /// Get clusters with total correlation mass above threshold.
        /// </summary>
        /// <param name="minMass">Minimum total mass</param>
        /// <param name="wThreshold">Weight threshold for strong edges</param>
        /// <param name="minSize">Minimum cluster size</param>
        /// <returns>List of clusters meeting criteria</returns>
        public List<List<int>> GetHeavyClustersByMass(
            double minMass,
            double wThreshold = HeavyClusterThreshold,
            int minSize = HeavyClusterMinSize)
        {
            var clusters = GetStrongCorrelationClusters(wThreshold);
            var result = new List<List<int>>();

            foreach (var c in clusters)
            {
                if (c.Count < minSize) continue;

                double mass = 0.0;
                for (int i = 0; i < c.Count; i++)
                {
                    int v = c[i];
                    for (int j = i + 1; j < c.Count; j++)
                    {
                        int u = c[j];
                        if (!Edges[v, u]) continue;
                        double w = Weights[v, u];
                        if (w > wThreshold)
                        {
                            mass += (w - wThreshold);
                        }
                    }
                }

                if (mass >= minMass)
                {
                    result.Add(c);
                }
            }

            return result;
        }

        /// <summary>
        /// Get statistics for heavy clusters by mass threshold.
        /// </summary>
        /// <param name="minMass">Minimum mass threshold</param>
        /// <param name="wThreshold">Weight threshold for strong edges</param>
        /// <param name="minSize">Minimum cluster size</param>
        /// <returns>Tuple of (count, totalMass, maxMass, meanMass)</returns>
        public (int count, double totalMass, double maxMass, double meanMass) GetHeavyClusterStatsByMass(
            double minMass,
            double wThreshold = HeavyClusterThreshold,
            int minSize = HeavyClusterMinSize)
        {
            var clusters = GetHeavyClustersByMass(minMass, wThreshold, minSize);
            double total = 0.0;
            double max = 0.0;

            foreach (var cluster in clusters)
            {
                double mass = 0.0;
                for (int a = 0; a < cluster.Count; a++)
                {
                    int v = cluster[a];
                    for (int b = a + 1; b < cluster.Count; b++)
                    {
                        int u = cluster[b];
                        if (!Edges[v, u]) continue;
                        double w = Weights[v, u];
                        if (w <= wThreshold) continue;
                        mass += (w - wThreshold);
                    }
                }

                total += mass;
                if (mass > max) max = mass;
            }

            int count = clusters.Count;
            double mean = count > 0 ? total / count : 0.0;

            return (count, total, max, mean);
        }

        /// <summary>
        /// Compute average weight within a cluster.
        /// </summary>
        /// <param name="cluster">List of node indices in the cluster</param>
        /// <returns>Average edge weight within cluster</returns>
        public double ComputeClusterAverageWeight(List<int> cluster)
        {
            double sum = 0.0;
            int cnt = 0;

            for (int a = 0; a < cluster.Count; a++)
            {
                int v = cluster[a];
                for (int b = a + 1; b < cluster.Count; b++)
                {
                    int u = cluster[b];
                    if (!Edges[v, u]) continue;
                    sum += Weights[v, u];
                    cnt++;
                }
            }

            return cnt > 0 ? sum / cnt : 0.0;
        }

        /// <summary>
        /// Adapt correlation timescales based on system state.
        /// Adjusts GlobalNeighbourFactor and GlobalSpontFactor.
        /// </summary>
        public void AdaptCorrelationTimescales()
        {
            var stats = GetEinsteinStats();
            double mean = stats.Average(s => s.EnergyDensity);
            double var = stats.Average(s => (s.EnergyDensity - mean) * (s.EnergyDensity - mean));
            double adj = Math.Clamp(var, 0.0, 5.0);

            GlobalNeighbourFactor = 1.0 / (1.0 + 0.1 * adj);
            GlobalSpontFactor = 1.0 + 0.1 * adj;
        }

        /// <summary>
        /// Adapt correlation timescales based on mass and energy deltas.
        /// </summary>
        /// <param name="heavyMassDeltaEma">EMA of heavy mass change</param>
        /// <param name="energyDeltaEma">EMA of energy change</param>
        public void AdaptCorrelationTimescales(double heavyMassDeltaEma, double energyDeltaEma)
        {
            double adj = Math.Clamp(Math.Abs(heavyMassDeltaEma) + Math.Abs(energyDeltaEma), 0.0, 5.0);
            GlobalNeighbourFactor = 1.0 / (1.0 + 0.1 * adj);
            GlobalSpontFactor = 1.0 + 0.1 * adj;
        }

        /// <summary>
        /// Remove an edge between two nodes.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST: Edge removal returns energy to vacuum pool.
        /// The energy that was spent creating the correlation is returned when it's destroyed.
        /// </summary>
        /// <param name="i">First node index</param>
        /// <param name="j">Second node index</param>
        public void RemoveEdge(int i, int j)
        {
            if (!Edges[i, j]) return;

            // === RQ-HYPOTHESIS CHECKLIST: RETURN EDGE ENERGY TO VACUUM ===
            // When a correlation is destroyed, its energy returns to the vacuum pool.
            // This ensures conservation: edges are "borrowed" from vacuum, not free.
            var ledger = GetEnergyLedger();
            ledger.RegisterRadiation(PhysicsConstants.EdgeCreationCost);

            Edges[i, j] = false;
            Edges[j, i] = false;
            _degree[i]--;
            _degree[j]--;
            TopologyVersion++; // Track topology changes for parallel coloring
            
            // Reset weights to zero
            Weights[i, j] = 0.0;
            Weights[j, i] = 0.0;
        }

        /// <summary>
        /// Ensure graph connectivity by adding edges between components.
        /// </summary>
        public void EnsureConnectivity()
        {
            var (labels, comps) = GetComponentLabels();
            if (comps <= 1) return;

            var firstByComp = new Dictionary<int, int>();
            for (int i = 0; i < N; i++)
            {
                if (!firstByComp.ContainsKey(labels[i]))
                {
                    firstByComp[labels[i]] = i;
                }
            }

            int prev = firstByComp[0];
            for (int c = 1; c < comps; c++)
            {
                int node = firstByComp[c];
                AddEdge(prev, node);
                prev = node;
            }
        }

        /// <summary>
        /// Flip a single node's state.
        /// </summary>
        /// <param name="node">Node index to flip</param>
        public void FlipNode(int node)
        {
            if (node < 0 || node >= N) return;
            State[node] = State[node] == NodeState.Excited ? NodeState.Rest : NodeState.Excited;
        }

        /// <summary>
        /// Get system-apparatus correlation for measurement tracking.
        /// </summary>
        /// <returns>Average correlation between measurement bond nodes</returns>
        public double GetSystemApparatusCorrelation()
        {
            if (_measurementBond == null) return 0.0;

            double sum = 0.0;
            int count = 0;

            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (_measurementBond[i, j])
                    {
                        sum += Weights[i, j];
                        count++;
                    }
                }
            }

            return count > 0 ? sum / count : 0.0;
        }

        /// <summary>
        /// Update confinement string along a path of nodes.
        /// Used for quark confinement dynamics.
        /// </summary>
        /// <param name="path">List of node indices forming the path</param>
        public void UpdateConfinementStringAlongPath(List<int> path)
        {
            if (path == null || path.Count < 2) return;

            for (int k = 0; k < path.Count - 1; k++)
            {
                int u = path[k];
                int v = path[k + 1];
                if (Edges[u, v])
                {
                    ApplyStringTension(u, v);
                }
            }
        }

        /// <summary>
        /// Update boson fields (scalar field diffusion).
        /// </summary>
        /// <param name="dt">Time step</param>
        public void UpdateBosonFields(double dt)
        {
            if (ScalarField == null || ScalarField.Length != N) return;

            for (int i = 0; i < N; i++)
            {
                double lap = 0.0;
                foreach (int j in Neighbors(i))
                {
                    lap += ScalarField[j] - ScalarField[i];
                }
                ScalarField[i] += dt * lap;
            }
        }
    }
}
