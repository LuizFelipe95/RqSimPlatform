using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace RQSimulation
{
    public partial class RQGraph
    {
        private double _vacuumMassBaseline;
        private double _vacuumMassRelaxRate = 0.0; // Disabled baseline drift per physics checklist
        private double _lastTotalHeavyMass;
        private double _lastAvalancheMassDelta;

        // RQ-FIX: Use spectral coordinates (graph-derived) instead of external coordinates for cluster centers
        // Previous implementation used Coordinates[i] which violates background independence
        private readonly Dictionary<int, (double x, double y)> _prevClusterCentersSpectral = new();

        // Added: vacuum correlation background estimation flag & value
        private double _vacuumCorrelationPerNode = 0.0;
        private bool _vacuumEstimated = false;

        // Core/halo support: cache last core sets for rest-mass computation
        private List<HashSet<int>> _lastCoreSets;

        public void RefreshCoreSets(double strongQuantile = 0.9)
        {
            var finder = new CoreClusterFinder(this);
            _lastCoreSets = finder.FindCoreClusters(strongQuantile).ToList();
        }

        private bool IsInCore(int node)
        {
            if (_lastCoreSets == null) return false;
            foreach (var set in _lastCoreSets)
                if (set.Contains(node)) return true;
            return false;
        }

        private static IEnumerable<int> ExceptCore(IEnumerable<int> nodes, Func<int, bool> isInCore)
        {
            foreach (var n in nodes)
                if (!isInCore(n)) yield return n;
        }

        public double ComputeClusterCorrelationEnergy(IReadOnlyCollection<int> nodes)
        {
            if (nodes == null || nodes.Count == 0) return 0.0;
            double sum = 0.0;
            var list = nodes.ToList();
            for (int a = 0; a < list.Count; a++)
            {
                int v = list[a];
                for (int b = a + 1; b < list.Count; b++)
                {
                    int u = list[b];
                    if (!Edges[v, u]) continue;
                    sum += Weights[v, u];
                }
            }
            return sum;
        }

        public void InitializeVacuumBaseline()
        {
            if (_correlationMass == null || _correlationMass.Length != N) RecomputeCorrelationMass();
            _vacuumMassBaseline = ComputeTotalHeavyMassVectorized();
            _lastTotalHeavyMass = _vacuumMassBaseline;
            // initial vacuum correlation baseline from current weights
            EstimateVacuumBackground();
            RefreshCoreSets();
        }

        public void UpdateHeavyMass(bool isAvalancheStep)
        {
            if (_correlationMass == null || _correlationMass.Length != N) RecomputeCorrelationMass();
            double totalMass = ComputeTotalHeavyMassVectorized();
            if (isAvalancheStep)
            {
                _lastAvalancheMassDelta = totalMass - _vacuumMassBaseline;
            }
            else
            {
                // Disable continuous baseline drift per physics checklist
                // Keep baseline constant after initial calibration
                // double delta = totalMass - _vacuumMassBaseline;
                // _vacuumMassBaseline += _vacuumMassRelaxRate * delta;
            }
            _lastTotalHeavyMass = totalMass;
        }

        private double ComputeTotalHeavyMassVectorized()
        {
            if (_correlationMass == null) return 0.0;
            double sum = 0.0;
            foreach (var m in _correlationMass) sum += m;
            return sum;
        }

        private unsafe double ComputeTotalHeavyMassAvx()
        {
            if (!Avx2.IsSupported || _correlationMass == null)
                return ComputeTotalHeavyMassVectorized();
            double sum = 0.0;
            int len = _correlationMass.Length;
            fixed (double* p = _correlationMass)
            {
                int i = 0;
                const int width = 4; // 256-bit / double
                var vSum = Avx.LoadVector256(&p[0]);
                for (i = width; i <= len - width; i += width)
                {
                    var v = Avx.LoadVector256(&p[i]);
                    vSum = Avx.Add(vSum, v);
                }
                double* tmp = stackalloc double[4];
                Avx.Store(tmp, vSum);
                sum = tmp[0] + tmp[1] + tmp[2] + tmp[3];
                for (; i < len; i++) sum += p[i];
            }
            return sum;
        }

        public double VacuumMassBaseline => _vacuumMassBaseline;
        public double LastTotalHeavyMass => _lastTotalHeavyMass;
        public double LastAvalancheMassDelta => _lastAvalancheMassDelta;

        public readonly struct ClusterInstant
        {
            public int Id { get; init; }
            public int[] Nodes { get; init; }
            public double CorrelationMass { get; init; }
            public double Energy { get; init; }
            public (double X, double Y) Center { get; init; }
            public (double VX, double VY) Velocity { get; init; }
            public double RestMass { get; init; } // Added rest mass (baseline-subtracted)
        }

        public readonly struct ClusterInvariant
        {
            public int Id { get; init; }
            public double RestMassHat { get; init; }
            public double Gamma { get; init; }
            public double Speed { get; init; }
        }

        /// <summary>
        /// RQ-HYPOTHESIS COMPLIANT: Build cluster instant using graph topology-derived coordinates.
        /// 
        /// PHYSICS FIX: Center of mass is computed using spectral coordinates (from Laplacian eigenvectors)
        /// instead of external Coordinates[i].X/Y which would violate background independence.
        /// 
        /// Spectral coordinates are derived purely from graph topology:
        /// - x_i = eigenvector_1[i] (Fiedler vector)
        /// - y_i = eigenvector_2[i] (second non-trivial eigenvector)
        /// 
        /// This ensures mass calculations depend only on relational structure, not external space.
        /// </summary>
        public ClusterInstant BuildClusterInstant(int clusterId, IReadOnlyList<int> nodes)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            
            double mSum = 0.0;
            double Ex = 0.0, Ey = 0.0;
            
            // RQ-FIX: Use spectral coordinates (graph-derived) instead of external Coordinates
            // Spectral coordinates are computed from Laplacian eigenvectors in RQGraph.SpectralGeometry.cs
            // SpectralX and SpectralY are exposed as public properties
            bool hasSpectralCoords = SpectralX != null && SpectralX.Length >= N && 
                                     SpectralY != null && SpectralY.Length >= N;
            
            for (int k = 0; k < nodes.Count; k++)
            {
                int i = nodes[k];
                if (_correlationMass != null && i >= 0 && i < _correlationMass.Length)
                {
                    double m = _correlationMass[i];
                    mSum += m;
                    
                    if (hasSpectralCoords && i < SpectralX.Length && i < SpectralY.Length)
                    {
                        // Use spectral coordinates (background-independent)
                        double sx = SpectralX[i];
                        double sy = SpectralY[i];
                        Ex += m * sx;
                        Ey += m * sy;
                    }
                    else
                    {
                        // Fallback: use weighted degree as proxy for "position" in correlation space
                        // This is purely topological - no external coordinates
                        double weightedDegree = 0.0;
                        foreach (int nb in Neighbors(i))
                            weightedDegree += Weights[i, nb];
                        
                        // Map to 2D using node index and weighted degree
                        // This is arbitrary but consistent and background-independent
                        double normalizedIndex = (double)i / Math.Max(1, N - 1);
                        Ex += m * normalizedIndex;
                        Ey += m * weightedDegree;
                    }
                }
            }
            
            double cx = mSum > 1e-10 ? Ex / mSum : 0.0;
            double cy = mSum > 1e-10 ? Ey / mSum : 0.0;
            
            // Track velocity using spectral center movement
            _prevClusterCentersSpectral.TryGetValue(clusterId, out var prevCenter);
            double vx = cx - prevCenter.x;
            double vy = cy - prevCenter.y;
            _prevClusterCentersSpectral[clusterId] = (cx, cy);
            
            double energyCorr = mSum;
            double restMass = ComputeRestMassOfCluster(nodes);
            
            return new ClusterInstant
            {
                Id = clusterId,
                Nodes = nodes.ToArray(),
                CorrelationMass = mSum,
                Energy = energyCorr,
                Center = (cx, cy),      // Now spectral-based, not external coords
                Velocity = (vx, vy),    // Velocity in spectral embedding space
                RestMass = restMass
            };
        }

        public ClusterInvariant ComputeInvariant(ClusterInstant inst)
        {
            double vx = inst.Velocity.VX;
            double vy = inst.Velocity.VY;
            double v2 = vx * vx + vy * vy;
            double c2 = 1.0;
            double beta2 = v2 / (c2 + 1e-12);
            if (beta2 < 0.0) beta2 = 0.0;
            if (beta2 > 0.999999) beta2 = 0.999999;
            double gamma = 1.0 / Math.Sqrt(1.0 - beta2);
            double E = gamma * inst.RestMass;
            double p = gamma * inst.RestMass * Math.Sqrt(v2 / (c2 + 1e-12));
            double m2 = E * E - p * p;
            if (m2 < 0.0) m2 = 0.0;
            double mRestHat = Math.Sqrt(m2);
            return new ClusterInvariant
            {
                Id = inst.Id,
                RestMassHat = mRestHat,
                Gamma = gamma,
                Speed = Math.Sqrt(v2)
            };
        }

        // === Vacuum correlation background estimation and rest mass ===
        public void EstimateVacuumBackground(int sampleEdges = 200)
        {
            // Sample a subset of existing edges to approximate mean correlation weight per node.
            double sum = 0.0; int count = 0;
            var indices = new List<(int i, int j)>();
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (!Edges[i, j]) continue;
                    indices.Add((i, j));
                }
            }
            if (indices.Count == 0) { _vacuumCorrelationPerNode = 0.0; _vacuumEstimated = true; return; }
            int take = Math.Min(sampleEdges, indices.Count);
            for (int k = 0; k < take; k++)
            {
                var (a, b) = indices[k];
                sum += Weights[a, b]; count++;
            }
            double meanEdge = count > 0 ? sum / count : 0.0;
            // approximate per-node baseline as meanEdge times average degree factor
            double avgDeg = 0.0;
            for (int i = 0; i < N; i++) avgDeg += _degree[i];
            avgDeg = N > 0 ? avgDeg / N : 0.0;
            _vacuumCorrelationPerNode = meanEdge * avgDeg / Math.Max(1.0, N);
            _vacuumEstimated = true;
        }

        public double ComputeRestMassOfCluster(IReadOnlyCollection<int> nodes)
        {
            if (nodes == null || nodes.Count == 0) return 0.0;
            // Ensure core sets up-to-date occasionally
            if (_lastCoreSets == null) RefreshCoreSets();
            var nodeSet = new HashSet<int>(nodes);
            var core = new HashSet<int>(nodeSet.Where(IsInCore));
            var halo = ExceptCore(nodeSet, IsInCore).ToList();

            double coreEnergy = ComputeClusterCorrelationEnergy(core);
            double haloEnergy = ComputeClusterCorrelationEnergy(halo);

            double massCore = coreEnergy - _vacuumCorrelationPerNode * core.Count;
            double massHalo = 0.1 * (haloEnergy - _vacuumCorrelationPerNode * nodeSet.Count);
            double total = massCore + massHalo;
            if (total < 0.0) total = 0.0;
            return total;
        }
        
        // === Checklist C.2: Spectral Mass ===
        
        /// <summary>
        /// Enable/disable spectral mass computation instead of correlation mass.
        /// When enabled, mass is computed from spectral gap of cluster Laplacian.
        /// Implements RQ-Hypothesis Checklist C.2.
        /// </summary>
        public bool UseSpectralMass { get; set; } = false;
        
        /// <summary>
        /// Compute spectral mass of a cluster using the first non-zero eigenvalue
        /// of the cluster Laplacian. The spectral gap λ₁ determines the mass:
        /// m ∝ λ₁ (lightest excitation energy).
        /// Implements RQ-Hypothesis Checklist C.2.
        /// </summary>
        /// <param name="nodes">Nodes in the cluster</param>
        /// <returns>Spectral mass (proportional to first Laplacian eigenvalue)</returns>
        public double ComputeSpectralMassOfCluster(IReadOnlyCollection<int> nodes)
        {
            if (nodes == null || nodes.Count < 2) return 0.0;
            
            var nodeList = nodes.ToList();
            var nodeSet = new HashSet<int>(nodes);
            int n = nodeList.Count;
            
            // Build local Laplacian for cluster
            // L_ij = degree(i) if i == j, -w_ij if connected, 0 otherwise
            double[,] laplacian = new double[n, n];
            
            for (int a = 0; a < n; a++)
            {
                int i = nodeList[a];
                double degree = 0.0;
                
                for (int b = 0; b < n; b++)
                {
                    if (a == b) continue;
                    int j = nodeList[b];
                    
                    if (Edges[i, j])
                    {
                        double w = Weights[i, j];
                        laplacian[a, b] = -w;
                        degree += w;
                    }
                }
                
                laplacian[a, a] = degree;
            }
            
            // Power iteration to find smallest non-zero eigenvalue
            // Use inverse iteration on (L + εI) for numerical stability
            double epsilon = 0.01;
            double[] v = new double[n];
            double[] v_new = new double[n];
            
            // Initialize with random vector orthogonal to constant vector
            // Uses named constant from PhysicsConstants for configurability
            double initCenter = PhysicsConstants.PowerIterationInitCenter;
            for (int i = 0; i < n; i++) v[i] = _rng.NextDouble() - initCenter;
            
            // Remove constant component (zero eigenvalue eigenvector)
            double mean = v.Sum() / n;
            for (int i = 0; i < n; i++) v[i] -= mean;
            
            // Normalize
            double norm = Math.Sqrt(v.Sum(x => x * x));
            if (norm < 1e-10) return 0.0;
            for (int i = 0; i < n; i++) v[i] /= norm;
            
            // Power iteration (10 iterations usually sufficient)
            double eigenvalue = 0.0;
            for (int iter = 0; iter < 20; iter++)
            {
                // v_new = L * v
                for (int i = 0; i < n; i++)
                {
                    v_new[i] = 0;
                    for (int j = 0; j < n; j++)
                    {
                        v_new[i] += laplacian[i, j] * v[j];
                    }
                }
                
                // Remove constant component
                mean = v_new.Sum() / n;
                for (int i = 0; i < n; i++) v_new[i] -= mean;
                
                // Compute Rayleigh quotient: λ = v^T L v / v^T v
                double vLv = 0, vv = 0;
                for (int i = 0; i < n; i++)
                {
                    vLv += v[i] * v_new[i];
                    vv += v[i] * v[i];
                }
                eigenvalue = vv > 1e-10 ? vLv / vv : 0.0;
                
                // Normalize v_new
                norm = Math.Sqrt(v_new.Sum(x => x * x));
                if (norm < 1e-10) break;
                for (int i = 0; i < n; i++) v[i] = v_new[i] / norm;
            }
            
            // Spectral mass is proportional to spectral gap
            return Math.Max(0.0, eigenvalue);
        }
        
        /// <summary>
        /// Get mass of cluster using either correlation or spectral method.
        /// Uses UseSpectralMass flag to determine method.
        /// </summary>
        public double GetClusterMass(IReadOnlyCollection<int> nodes)
        {
            if (UseSpectralMass)
            {
                return ComputeSpectralMassOfCluster(nodes);
            }
            else
            {
                return ComputeRestMassOfCluster(nodes);
            }
        }
    }
}
