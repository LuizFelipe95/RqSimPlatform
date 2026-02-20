using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        /// <summary>
        /// Вычисляет энтропию запутанности между кластером (Region A) и остальным миром (Region B).
        /// S = -Tr(rho_A * ln rho_A), где rho_A - редуцированная матрица плотности.
        /// </summary>
        public double ComputeEntanglementEntropy(List<int> regionA)
        {
            if (_waveMulti == null) return 0;

            // Упрощенная оценка для чистых состояний на графе:
            // Энтропия ~ Сумма квадратов амплитуд на границе разреза (Cut)
            // Это аппроксимация для графовых состояний (Graph States).

            double boundaryEntropy = 0.0;

            foreach (int i in regionA)
            {
                foreach (int j in Neighbors(i))
                {
                    // Если ребро пересекает границу (j не в Region A)
                    if (!regionA.Contains(j))
                    {
                        // Вклад в запутанность пропорционален весу связи и корреляции состояний
                        double w = Weights[i, j];
                        // S_link ~ -w * ln(w)
                        if (w > 0.001)
                            boundaryEntropy += -w * Math.Log(w);
                    }
                }
            }

            return boundaryEntropy;
        }

        /// <summary>
        /// Проверка Area Law: Возвращает отношение Энтропии к "Площади" границы.
        /// Если ratio ~ const при росте кластера, значит пространство голографично.
        /// </summary>
        public double CheckHolographicPrinciple(List<int> cluster)
        {
            double entropy = ComputeEntanglementEntropy(cluster);

            // "Площадь" в графе = Сумма весов разрезанных ребер
            double area = 0;
            foreach (int i in cluster)
            {
                foreach (int j in Neighbors(i))
                {
                    if (!cluster.Contains(j)) area += Weights[i, j];
                }
            }

            return area > 0 ? entropy / area : 0;
        }

        // ComputeTotalEnergy with string field energy contribution per checklist
        public double ComputeTotalEnergy()
        {
            double energy = 0.0; bool useLocal = _targetDegreePerNode != null && _targetDegreePerNode.Length == N;
            for (int i = 0; i < N; i++)
            {
                int target = useLocal ? _targetDegreePerNode[i] : _targetDegree;
                double diff = _degree[i] - target;
                energy += diff * diff;
            }
            for (int i = 0; i < N; i++)
            {
                foreach (int nb in Neighbors(i)) if (i < nb) energy -= Weights[i, nb] * Weights[i, nb];
            }
            if (_stringEnergy != null && _stringEnergy.GetLength(0) == N)
            {
                double stringE = 0.0;
                for (int i = 0; i < N; i++) for (int j = i + 1; j < N; j++) stringE += _stringEnergy[i, j];
                energy += stringE;
            }
            return energy;
        }

        // Per-node correlation mass distribution (used by gravity wrapper)
        public double[] ComputePerNodeCorrelationMass(double wThreshold = PhysicsConstants.DefaultHeavyClusterThreshold, int minSize = HeavyClusterMinSize)
        {
            var nodeCorrMass = new double[N]; var clusters = GetStrongCorrelationClusters(wThreshold);
            foreach (var cluster in clusters)
            {
                int size = cluster.Count; if (size < minSize) continue;
                double mass = 0.0;
                for (int a = 0; a < cluster.Count; a++)
                {
                    int v = cluster[a];
                    for (int b = a + 1; b < cluster.Count; b++)
                    {
                        int u = cluster[b]; if (!Edges[v, u]) continue;
                        double w = Weights[v, u]; if (w <= wThreshold) continue; mass += (w - wThreshold);
                    }
                }
                if (mass <= 0.0) continue;
                double perNode = mass / size; foreach (int v in cluster) nodeCorrMass[v] += perNode;
            }
            return nodeCorrMass;
        }

        // Measurement back-action helper referenced in Measurement partial
        private void ApplyMeasurementBackAction(List<int> system, List<int> apparatus)
        {
            if (system == null || apparatus == null) return;
            foreach (int i in system)
            {
                foreach (int j in system)
                {
                    if (i == j) continue;
                    if (Edges[i, j])
                    {
                        double w = Weights[i, j] * 0.8; Weights[i, j] = w; Weights[j, i] = w;
                    }
                }
                if (_nodeEnergy != null && i < _nodeEnergy.Length) _nodeEnergy[i] += 1.0;
            }
            foreach (int i in apparatus)
            {
                foreach (int j in apparatus)
                {
                    if (i == j) continue;
                    if (Edges[i, j])
                    {
                        double w = Weights[i, j] * 0.8; Weights[i, j] = w; Weights[j, i] = w;
                    }
                }
                if (_nodeEnergy != null && i < _nodeEnergy.Length) _nodeEnergy[i] += 1.0;
            }
            foreach (int i in system)
            {
                for (int j = 0; j < N; j++)
                {
                    if (system.Contains(j) || apparatus.Contains(j)) continue;
                    if (Edges[i, j]) { double w = Weights[i, j] * 0.9; Weights[i, j] = w; Weights[j, i] = w; }
                }
            }
            foreach (int i in apparatus)
            {
                for (int j = 0; j < N; j++)
                {
                    if (system.Contains(j) || apparatus.Contains(j)) continue;
                    if (Edges[i, j]) { double w = Weights[i, j] * 0.9; Weights[i, j] = w; Weights[j, i] = w; }
                }
            }
            UpdateEdgeDelaysFromDistances();
        }

        // Local curvature helper referenced by Updates partial
        public double GetLocalCurvature(int node)
        {
            double totalDegree = 0; for (int i = 0; i < N; i++) totalDegree += _degree[i];
            double avgDeg = N > 0 ? totalDegree / N : 0.0; double localDeg = _degree[node];
            return avgDeg == 0.0 ? 0.0 : (localDeg - avgDeg) / avgDeg;
        }

        public double GetLocalCorrelationDensity(int node)
        {
            double sumWeights = 0.0; foreach (var nb in Neighbors(node)) sumWeights += Math.Abs(Weights[node, nb]);
            double effEdges = sumWeights; double cellArea = 1.0; return cellArea > 0 ? effEdges / cellArea : 0.0;
        }

        // Heavy mass / correlation helpers required by CoreHelpers
        private void EnforcePlanckCorrelationLimit()
        {
            for (int i = 0; i < N; i++)
            {
                double local = GetLocalCorrelationDensity(i);
                if (local <= 1.0) continue;
                foreach (var nb in Neighbors(i))
                {
                    double w = Weights[i, nb];
                    double newW = w * 0.9;
                    Weights[i, nb] = newW; Weights[nb, i] = newW;
                }
            }
        }

        // Heavy mass delta overloads required by CoreHelpers Step
        public void ApplyHeavyMassDelta(double deltaMass)
        {
            if (N <= 0 || _nodeEnergy == null) return;
            double perNode = deltaMass / N;
            for (int i = 0; i < _nodeEnergy.Length; i++) _nodeEnergy[i] -= perNode;
        }

        public void ApplyHeavyMassDelta(double deltaMass, IEnumerable<int> cluster)
        {
            if (cluster == null) { ApplyHeavyMassDelta(deltaMass); return; }
            var list = cluster.Where(i => i >= 0 && i < N).ToList();
            if (list.Count == 0) { ApplyHeavyMassDelta(deltaMass); return; }
            double share = Math.Abs(deltaMass) / list.Count;
            foreach (int v in list)
            {
                foreach (int nb in Neighbors(v))
                {
                    _nodeEnergy[nb] -= share;
                    if (!Edges[v, nb] && _nodeEnergy[nb] < EnergySewThreshold)
                    {
                        AddEdge(v, nb); Weights[v, nb] = 0.1; Weights[nb, v] = 0.1;
                    }
                }
            }
        }

        // Einstein dynamics stub referenced by examples (simplified)
        public void ApplyEinsteinDynamics()
        {
            if (_nodeEnergy == null || _nodeEnergy.Length != N)
            {
                _nodeEnergy = new double[N];
            }
            for (int i = 0; i < N; i++)
            {
                double baseE = 0.0;
                foreach (var nb in Neighbors(i)) baseE += Weights[i, nb];
                _nodeEnergy[i] = baseE;
            }
        }

        public void UpdateStoredEnergyAndPaths()
        {
            if (LocalPotential == null || StoredEnergy == null) return;
            for (int i = 0; i < N; i++)
            {
                double lp = LocalPotential[i];
                if (lp > 0.7) StoredEnergy[i] += 0.02 * lp; else StoredEnergy[i] *= 0.98;
                if (StoredEnergy[i] < 0) StoredEnergy[i] = 0;
            }
            if (PathWeight != null)
            {
                for (int i = 0; i < N; i++)
                {
                    foreach (int nb in Neighbors(i))
                    {
                        double diff = Math.Abs(LocalPotential[i] - LocalPotential[nb]);
                        double w = Math.Exp(-diff);
                        PathWeight[i, nb] = w;
                        PathWeight[nb, i] = w;
                    }
                }
            }
        }

        public double ComputeEffectiveLightSpeed()
        {
            if (EdgeDelay == null || Coordinates == null) return 0.0;
            double sumDist = 0.0, sumDelay = 0.0; int edgeCount = 0;
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (!Edges[i, j]) continue;
                    double d = GetPhysicalDistance(i, j); double delay = EdgeDelay[i, j]; if (delay <= 0) continue;
                    sumDist += d; sumDelay += delay; edgeCount++;
                }
            }
            if (edgeCount == 0 || sumDelay <= 0) return 0.0;
            double cEff = (sumDist / edgeCount) / (sumDelay / edgeCount);
            return double.IsFinite(cEff) ? cEff : 0.0;
        }

        /// <summary>
        /// Вычисляет долю узлов в наибольшей связной компоненте.
        /// </summary>
        private double ComputeLargestComponentFraction()
        {
            if (N == 0) return 0.0;

            // Union-Find для быстрого определения компонент
            int[] parent = new int[N];
            int[] rank = new int[N];
            for (int i = 0; i < N; i++) { parent[i] = i; rank[i] = 0; }

            int Find(int x)
            {
                if (parent[x] != x) parent[x] = Find(parent[x]);
                return parent[x];
            }

            void Union(int x, int y)
            {
                int px = Find(x), py = Find(y);
                if (px == py) return;
                if (rank[px] < rank[py]) parent[px] = py;
                else if (rank[px] > rank[py]) parent[py] = px;
                else { parent[py] = px; rank[px]++; }
            }

            // Объединяем по рёбрам
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (i < j) Union(i, j);
                }
            }

            // Считаем размер каждой компоненты
            var componentSizes = new Dictionary<int, int>();
            for (int i = 0; i < N; i++)
            {
                int root = Find(i);
                componentSizes.TryGetValue(root, out int size);
                componentSizes[root] = size + 1;
            }

            int maxSize = componentSizes.Values.Max();
            return (double)maxSize / N;
        }
        
        // ============================================================
        // RQ-HYPOTHESIS STAGE 5: SPECTRAL ACTION INTEGRATION
        // ============================================================
        
        /// <summary>
        /// Fast estimation of spectral dimension using degree distribution.
        /// Delegates to SpectralAction.EstimateSpectralDimensionFast.
        /// 
        /// RQ-HYPOTHESIS: d_S ? 2 at Planck scale (UV), d_S ? 4 at large scales (IR)
        /// 
        /// This method provides a quick estimate without expensive eigenvalue
        /// computation or random walks. For more accurate results, use
        /// ComputeSpectralDimension() instead.
        /// </summary>
        /// <param name="walks">Number of random walks (unused in fast method)</param>
        /// <param name="steps">Walk length (unused in fast method)</param>
        /// <returns>Estimated spectral dimension (double precision)</returns>
        public double EstimateSpectralDimensionFast(int walks = 100, int steps = 50)
        {
            return SpectralAction.EstimateSpectralDimensionFast(this, walks, steps);
        }
        
        /// <summary>
        /// Compute spectral action for this graph configuration.
        /// Delegates to SpectralAction.ComputeSpectralAction.
        /// 
        /// RQ-HYPOTHESIS STAGE 5: The spectral action S = Tr(f(D/?)) provides
        /// a physics-motivated action functional that replaces artificial
        /// dimension penalties. 4D spacetime emerges as the energy minimum.
        /// </summary>
        /// <returns>Total spectral action value (double precision)</returns>
        public double ComputeSpectralAction()
        {
            return SpectralAction.ComputeSpectralAction(this);
        }
        
        /// <summary>
        /// Check if graph configuration is near a spectral action minimum.
        /// Returns true if the gradient magnitude is below threshold.
        /// 
        /// Use this to determine when to stop optimization or MCMC sampling.
        /// </summary>
        /// <param name="threshold">Gradient magnitude threshold (default 0.01)</param>
        /// <returns>True if near energy minimum</returns>
        public bool IsNearSpectralActionMinimum(double threshold = 0.01)
        {
            return SpectralAction.IsNearActionMinimum(this, threshold);
        }
    }
}
