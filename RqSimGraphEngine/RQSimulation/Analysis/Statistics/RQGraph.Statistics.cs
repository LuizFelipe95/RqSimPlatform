using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace RQSimulation
{


    public partial class RQGraph
    {
        // Correlation-mass cache lives in Physics partial; use it here.
        public double GetNodeMass(int node) => (_correlationMass != null && node >= 0 && node < N) ? _correlationMass[node] : 0.0;
        public double GetGravitationalTimeDilation(int node) { double m = GetNodeMass(node); return 1.0 / (1.0 + m); }
        private (double X, double Y)[] _previousCoordinates;
        public void SnapshotCoordinates() { if (Coordinates == null || Coordinates.Length != N) return; _previousCoordinates ??= new (double X, double Y)[N]; for (int i = 0; i < N; i++) _previousCoordinates[i] = Coordinates[i]; }
        public (int totalEdges, double avgDegree) GetEdgeStats() { int totalDegree = 0; for (int i = 0; i < N; i++) totalDegree += _degree[i]; int edges = totalDegree / 2; double avgDeg = N > 0 ? (double)totalDegree / N : 0.0; return (edges, avgDeg); }
        public (int clusters, int largestClusterSize) GetOnClusters() { bool[] visited = new bool[N]; int clusters = 0; int largest = 0; for (int i = 0; i < N; i++) { if (State[i] != NodeState.Excited || visited[i]) continue; var q = new Queue<int>(); q.Enqueue(i); visited[i] = true; int size = 0; while (q.Count > 0) { int v = q.Dequeue(); size++; foreach (int u in Neighbors(v)) { if (visited[u] || State[u] != NodeState.Excited) continue; visited[u] = true; q.Enqueue(u); } } clusters++; if (size > largest) largest = size; } return (clusters, largest); }
        public (double avgWeight, double maxWeight, int strongEdges) GetWeightStats(double strongThreshold = 0.8) { double sum = 0.0; double max = 0.0; int strong = 0; int edgeCount = 0; for (int i = 0; i < N; i++) for (int j = i + 1; j < N; j++) { if (!Edges[i, j]) continue; double w = Weights[i, j]; sum += w; edgeCount++; if (w > max) max = w; if (w >= strongThreshold) strong++; } double avg = edgeCount > 0 ? sum / edgeCount : 0.0; return (avg, max, strong); }
        public (double min, double q25, double median, double q75, double max) GetWeightDistribution() { var list = new List<double>(); for (int i = 0; i < N; i++) for (int j = i + 1; j < N; j++) if (Edges[i, j]) list.Add(Weights[i, j]); if (list.Count == 0) return (0, 0, 0, 0, 0); list.Sort(); int c = list.Count; double min = list[0]; double max = list[c - 1]; double median = list[c / 2]; double q25 = list[c / 4]; double q75 = list[3 * c / 4]; return (min, q25, median, q75, max); }
        public double GetWeightQuantile(double fraction) { var list = new List<double>(); for (int i = 0; i < N; i++) for (int j = i + 1; j < N; j++) if (Edges[i, j]) list.Add(Weights[i, j]); if (list.Count == 0) return HeavyClusterThreshold; list.Sort(); int idx = (int)(fraction * (list.Count - 1)); return list[idx]; }
        public double GetRelationalHeavyThreshold(double highQuantile = 0.8) => GetWeightQuantile(highQuantile);
        // Note: GetAdaptiveHeavyThreshold() is defined in CoreHelpers.cs using mean + sigma formula
        // GetWeightQuantile provides percentile-based alternative for specific use cases
        public (int clusters, int largestClusterSize, double avgSize) GetStrongCorrelationClustersDetailed(double threshold) { bool[] visited = new bool[N]; int clusters = 0; int largest = 0; int total = 0; for (int i = 0; i < N; i++) { if (visited[i]) continue; bool hasStrong = false; foreach (int j in Neighbors(i)) { if (Weights[i, j] >= threshold) { hasStrong = true; break; } } if (!hasStrong) continue; clusters++; int size = 0; var q = new Queue<int>(); q.Enqueue(i); visited[i] = true; while (q.Count > 0) { int v = q.Dequeue(); size++; foreach (int u in Neighbors(v)) { if (visited[u] || Weights[v, u] < threshold) continue; visited[u] = true; q.Enqueue(u); } } if (size > largest) largest = size; total += size; } double avgSize = clusters > 0 ? (double)total / clusters : 0.0; return (clusters, largest, avgSize); }
        public (int count, double totalMass, int maxSize, double avgMassPerNode) GetHeavyClusterStatsCorrelationMass(double wThreshold, int minSize) { var clusters = GetStrongCorrelationClusters(wThreshold); int heavyCount = 0; double totalMass = 0.0; int maxSize = 0; double totalMassPerNode = 0.0; int perNodeCount = 0; foreach (var cl in clusters) { if (cl.Count < minSize) continue; double mass = 0.0; for (int a = 0; a < cl.Count; a++) { int v = cl[a]; for (int b = a + 1; b < cl.Count; b++) { int u = cl[b]; if (!Edges[v, u]) continue; double w = Weights[v, u]; if (w <= wThreshold) continue; mass += (w - wThreshold); } } if (mass <= 0.0) continue; heavyCount++; totalMass += mass; if (cl.Count > maxSize) maxSize = cl.Count; totalMassPerNode += mass / cl.Count; perNodeCount++; } double avgMassPerNode = perNodeCount > 0 ? totalMassPerNode / perNodeCount : 0.0; return (heavyCount, totalMass, maxSize, avgMassPerNode); }
        public (double Curvature, double EnergyDensity)[] GetEinsteinStats(double excitedEnergy = 0.5) { var stats = new (double Curvature, double EnergyDensity)[N]; bool useLocal = _targetDegreePerNode != null && _targetDegreePerNode.Length == N; for (int i = 0; i < N; i++) { double target = useLocal ? _targetDegreePerNode[i] : _targetDegree; double curvature = _degree[i] - target; double energyDensity = 0.0; if (State[i] == NodeState.Excited) energyDensity += excitedEnergy; if (_correlationMass != null && _correlationMass.Length == N) energyDensity += _correlationMass[i]; stats[i] = (curvature, energyDensity); } return stats; }
        public (int count, double totalRestMass, double maxRestMass, double avgRestMass) GetHeavyClusterRestMassStats(double wThreshold, int minSize)
        {
            var clusters = GetStrongCorrelationClusters(wThreshold);
            int count = 0; double sum = 0.0; double max = 0.0;
            foreach (var cl in clusters)
            {
                if (cl.Count < minSize) continue;
                double rest = ComputeRestMassOfCluster(cl);
                if (rest <= 0.0) continue;
                count++; sum += rest; if (rest > max) max = rest;
            }
            double avg = count > 0 ? sum / count : 0.0;
            return (count, sum, max, avg);
        }

        public double EstimateSignalSpeed(int sourceNode, int maxSteps = 100)
        {
            if (sourceNode < 0 || sourceNode >= N) return 0.0;
            int[] dist = BFSDistances(sourceNode);
            double t0 = ProperTime != null ? ProperTime[sourceNode] : 0.0;
            int maxD = 0; double maxDt = 0.0;
            for (int step = 0; step < maxSteps; step++)
            {
                UpdateQuantumState();
                UpdateNodeStates();
                for (int i = 0; i < N; i++)
                {
                    if (State[i] == NodeState.Excited && dist[i] > 0)
                    {
                        double dt = (ProperTime != null ? ProperTime[i] : 0.0) - t0;
                        if (dt <= 0.0) continue;
                        if (dist[i] > maxD)
                        {
                            maxD = dist[i]; maxDt = dt;
                        }
                    }
                }
            }
            if (maxDt <= 0.0 || maxD == 0) return 0.0;
            return maxD / maxDt;
        }

        private int[] BFSDistances(int source)
        {
            var dist = new int[N]; for (int i = 0; i < N; i++) dist[i] = -1; dist[source] = 0;
            var q = new Queue<int>(); q.Enqueue(source);
            while (q.Count > 0)
            {
                int v = q.Dequeue();
                foreach (int u in Neighbors(v))
                {
                    if (dist[u] != -1) continue;
                    dist[u] = dist[v] + 1; q.Enqueue(u);
                }
            }
            return dist;
        }

        public (double k, double omega)[] EstimateDispersionOnChain(int[] chainNodes, int steps)
        {
            if (chainNodes == null || chainNodes.Length == 0 || steps <= 1) return Array.Empty<(double k, double omega)>();
            int L = chainNodes.Length;
            var phases = new double[L, steps];
            for (int t = 0; t < steps; t++)
            {
                UpdateQuantumState();
                for (int x = 0; x < L; x++)
                {
                    int i = chainNodes[x];
                    if (_wavefunction == null || _wavefunction.Length != N) { phases[x, t] = 0.0; continue; }
                    Complex psi = _wavefunction[i];
                    phases[x, t] = Math.Atan2(psi.Imaginary, psi.Real);
                }
            }
            var result = new (double k, double omega)[L];
            for (int m = 0; m < L; m++)
            {
                double k = 2.0 * Math.PI * m / L;
                double meanPhi = 0.0; for (int t = 0; t < steps; t++) meanPhi += phases[m, t]; meanPhi /= steps;
                double num = 0.0, den = 0.0; double meanT = (steps - 1) / 2.0;
                for (int t = 0; t < steps; t++)
                {
                    double dt = t - meanT; double dphi = phases[m, t] - meanPhi;
                    num += dt * dphi; den += dt * dt;
                }
                double omega = den > 0 ? num / den : 0.0;
                result[m] = (k, omega);
            }
            return result;
        }

        public double EstimateGravitationalRedshift(int[] heavyCluster, int[] lightRegion, int steps)
        {
            if (heavyCluster == null || lightRegion == null || heavyCluster.Length == 0 || lightRegion.Length == 0) return 0.0;
            double heavyPhase0 = 0.0, lightPhase0 = 0.0;
            foreach (int i in heavyCluster) heavyPhase0 += ProperTime[i];
            foreach (int i in lightRegion) lightPhase0 += ProperTime[i];
            for (int t = 0; t < steps; t++)
            {
                UpdateQuantumState();
                UpdateNodeStates();
            }
            double heavyPhase1 = 0.0, lightPhase1 = 0.0;
            foreach (int i in heavyCluster) heavyPhase1 += ProperTime[i];
            foreach (int i in lightRegion) lightPhase1 += ProperTime[i];
            double heavyFreq = (heavyPhase1 - heavyPhase0) / heavyCluster.Length;
            double lightFreq = (lightPhase1 - lightPhase0) / lightRegion.Length;
            if (lightFreq == 0.0) return 0.0;
            return heavyFreq / lightFreq;
        }
        public int GetLargestClusterSize(Func<NodeState, bool> predicate)
        {
            if (predicate == null) return 0;
            bool[] visited = new bool[N];
            int maxSize = 0;
            for (int i = 0; i < N; i++)
            {
                if (visited[i]) continue;
                if (!predicate(State[i])) continue;
                int size = 0;
                var stack = new Stack<int>();
                stack.Push(i); visited[i] = true;
                while (stack.Count > 0)
                {
                    int v = stack.Pop(); size++;
                    foreach (int nb in Neighbors(v))
                    {
                        if (visited[nb]) continue;
                        if (!predicate(State[nb])) continue;
                        visited[nb] = true; stack.Push(nb);
                    }
                }
                if (size > maxSize) maxSize = size;
            }
            return maxSize;
        }
    }
}
