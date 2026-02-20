using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        private void InitProperTime()
        {
            ProperTime = new double[N];
            for (int i = 0; i < N; i++) ProperTime[i] = 0.0;
        }

        private void InitEdgeData()
        {
            EdgeDelay = new double[N, N];
            EdgeDirection = new sbyte[N, N];
        }

        /// <summary>
        /// RQ-HYPOTHESIS: Initializes random coordinates FOR VISUALIZATION PURPOSES ONLY.
        /// 
        /// WARNING: These coordinates are external/background coordinates that violate
        /// the relational principle of RQ-Hypothesis. They MUST NOT be used in:
        /// - Distance calculations (use ShortestPathDistance or GetGraphDistanceWeighted)
        /// - Mass calculations (use spectral coordinates from Laplacian eigenvectors)
        /// - Physics evolution (all physics must be topology-based)
        /// 
        /// These coordinates are used ONLY by the UI drawing system for visualization.
        /// The actual "positions" in RQ-Hypothesis emerge from correlation structure,
        /// computed via spectral geometry (SpectralCoordinates property).
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
        public void InitCoordinatesRandom(double range = 1.0)
        {
            if (N <= 0) return;
#pragma warning disable CS0618 // Suppress obsolete warning - this method IS the UI initializer
            if (Coordinates == null || Coordinates.Length != N) Coordinates = new (double X, double Y)[N];
            for (int i = 0; i < N; i++)
            {
                double x = (_rng.NextDouble() * 2.0 - 1.0) * range;
                double y = (_rng.NextDouble() * 2.0 - 1.0) * range;
                Coordinates[i] = (x, y);
            }
#pragma warning restore CS0618
        }

        private void InitClocks(double fraction = 0.05)
        {
            _clockNodes.Clear();
            var heavy = GetStrongCorrelationClusters(AdaptiveHeavyThreshold);
            var largest = heavy.OrderByDescending(c => c.Count).FirstOrDefault();
            if (largest != null && largest.Count > 0)
            {
                foreach (int idx in largest)
                {
                    if (PhysicsProperties != null && PhysicsProperties.Length == N)
                        PhysicsProperties[idx].IsClock = true;
                    _clockNodes.Add(idx);
                }
                return;
            }
            int count = Math.Max(1, (int)(N * fraction));
            for (int i = 0; i < count; i++)
            {
                int idx = _rng.Next(N);
                if (PhysicsProperties != null && PhysicsProperties.Length == N)
                    PhysicsProperties[idx].IsClock = true;
                _clockNodes.Add(idx);
            }
        }

        public void ComputeFractalLevels()
        {
            if (N <= 0) { FractalLevel = Array.Empty<int>(); return; }
            FractalLevel = new int[N];
            for (int i = 0; i < N; i++)
            {
                int deg = _degree != null && i < _degree.Length ? _degree[i] : 0;
                FractalLevel[i] = (int)Math.Log(Math.Max(1, deg), 2);
            }
        }

        public void InitFractalTopology(int levels, int branchFactor)
        {
            if (N <= 0) return;
            int[] nodeIndices = Enumerable.Range(0, N).ToArray();
            // initialise domain & structural mass if needed
            if (Domain == null || Domain.Length != N)
            {
                Domain = new int[N];
                for (int i = 0; i < N; i++) Domain[i] = _rng.Next(levels * 4);
            }
            // RQ-FIX: Removed StructuralMass initialization
            for (int lvl = 1; lvl <= levels; lvl++)
            {
                int groupCount = (int)Math.Pow(branchFactor, lvl);
                int size = Math.Max(1, N / groupCount);
                for (int g = 0; g < groupCount; g++)
                {
                    var group = nodeIndices.Skip(g * size).Take(size).ToList();
                    foreach (int i in group)
                        foreach (int j in group)
                        {
                            if (i < j && _rng.NextDouble() < 0.5)
                            {
                                AddEdge(i, j);
                                double w = 0.15 + 0.45 * _rng.NextDouble();
                                Weights[i, j] = w; Weights[j, i] = w;
                            }
                        }
                }
                for (int g1 = 0; g1 < groupCount; g1++)
                {
                    for (int g2 = g1 + 1; g2 < groupCount; g2++)
                    {
                        if (_rng.NextDouble() < 0.1)
                        {
                            int a = g1 * size;
                            int b = g2 * size;
                            if (a < N && b < N)
                            {
                                AddEdge(nodeIndices[a], nodeIndices[b]);
                                double w = 0.15 + 0.25 * _rng.NextDouble();
                                Weights[nodeIndices[a], nodeIndices[b]] = w;
                                Weights[nodeIndices[b], nodeIndices[a]] = w;
                            }
                        }
                    }
                }
            }
            UpdateEdgeDelaysFromDistances();
            // initialize local potential after topology defined
            if (LocalPotential == null || LocalPotential.Length != N)
            {
                LocalPotential = new double[N];
                for (int i = 0; i < N; i++) LocalPotential[i] = _rng.NextDouble() * 0.1;
            }
        }

        public void RelaxCoordinatesFromCorrelation(double eta)
        {
            if (Coordinates == null || Coordinates.Length != N) return; if (eta <= 0) return;
            UpdateTargetDistancesFromWeights();
            var dX = new double[N]; var dY = new double[N];
            for (int i = 0; i < N; i++)
            {
                double fx = 0.0, fy = 0.0; double xi = Coordinates[i].X; double yi = Coordinates[i].Y;
                for (int j = 0; j < N; j++)
                {
                    if (!Edges[i, j] || i == j) continue;
                    double dx = Coordinates[j].X - xi; double dy = Coordinates[j].Y - yi; double r = Math.Sqrt(dx * dx + dy * dy) + 1e-9;
                    double target = _targetDistance[i, j]; if (target <= 0.0) continue;
                    double diff = r - target; double k = diff / r; fx += k * dx; fy += k * dy;
                }
                dX[i] = -eta * fx; dY[i] = -eta * fy;
            }
            for (int i = 0; i < N; i++) Coordinates[i] = (Coordinates[i].X + dX[i], Coordinates[i].Y + dY[i]);
        }

        /// <summary>
        /// Updates target distances from edge weights.
        /// Called after weight updates to keep distance matrix synchronized.
        /// Uses formula: d_ij = -log(w_ij + ?)
        /// </summary>
        public void UpdateTargetDistancesFromWeights()
        {
            if (_targetDistance == null || _targetDistance.GetLength(0) != N || _targetDistance.GetLength(1) != N)
                _targetDistance = new double[N, N];
            for (int i = 0; i < N; i++)
            {
                for (int j = 0; j < N; j++)
                {
                    if (!Edges[i, j] || i == j) { _targetDistance[i, j] = 0.0; continue; }
                    double w = Weights[i, j]; _targetDistance[i, j] = w <= 0.0 ? 0.0 : -Math.Log(w + EpsDist);
                }
            }
        }

        private void SetupEdgeMetric(int i, int j)
        {
            double d = GetPhysicalDistance(i, j);
            double avgDeg = 0.0; for (int k = 0; k < N; k++) avgDeg += _degree[k];
            avgDeg = N > 0 ? avgDeg / N : 1.0;
            double maxSignalSpeed = 1.0 / Math.Max(1.0, avgDeg);
            double delay = d / maxSignalSpeed;
            EdgeDelay[i, j] = delay; EdgeDelay[j, i] = delay;
            EdgeDirection[i, j] = 1; EdgeDirection[j, i] = -1;
        }

        private void UpdateEdgeDelaysFromDistances()
        {
            if (EdgeDelay == null || EdgeDelay.GetLength(0) != N || EdgeDelay.GetLength(1) != N)
                EdgeDelay = new double[N, N];
            if (_targetDistance == null || _targetDistance.GetLength(0) != N || _targetDistance.GetLength(1) != N)
                UpdateTargetDistancesFromWeights();
            for (int i = 0; i < N; i++)
            {
                for (int j = 0; j < N; j++)
                {
                    if (!Edges[i, j] || i == j) { EdgeDelay[i, j] = 0.0; continue; }
                    EdgeDelay[i, j] = _targetDistance[i, j];
                }
            }
        }
    }
}
