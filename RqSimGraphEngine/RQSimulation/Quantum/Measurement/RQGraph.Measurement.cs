using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public class MeasurementState
    {
        public bool Configured { get; set; }
        public bool EverTriggered { get; set; }
        public int StepTriggered { get; set; } = -1;
        public double CorrAtTrigger { get; set; }
        public HashSet<int> SystemSet { get; set; } = new();
        public HashSet<int> ApparatusSet { get; set; } = new();
        public int SystemCoreNode { get; set; } = -1;
    }
    internal sealed class MedianTracker
    {
        private readonly List<double> _samples = new();
        public void AddSample(double v) { _samples.Add(v); }
        public double GetMedian()
        {
            if (_samples.Count == 0) return 0.0;
            var arr = _samples.ToArray();
            Array.Sort(arr);
            return arr[arr.Length / 2];
        }
    }
    public struct AvalancheInfo
    {
        public int Size;
        public int ExcitedFinal;
        public double Duration;
        public double Criticality;
        public IReadOnlyList<int> AffectedNodes; // nodes participating
    }
    public partial class RQGraph
    {
        private bool _measurementDone = false;
        private List<int>? _measuredSystem = null;
        private List<int>? _apparatus = null;
        private readonly MedianTracker _criticalityTracker = new();
        private bool[,] _isMeasurementEdge;

        public bool IsMeasurementDone => _measurementDone;

        public void SetMeasurementPair(List<int> system, List<int> apparatus)
        {
            ArgumentNullException.ThrowIfNull(system);
            ArgumentNullException.ThrowIfNull(apparatus);
            _measuredSystem = system;
            _apparatus = apparatus;
            _measurementDone = false;
            if (_measurementBond == null || _measurementBond.GetLength(0) != N)
                _measurementBond = new bool[N, N];
            else
                Array.Clear(_measurementBond, 0, _measurementBond.Length);
        }

        private bool TryGetSafeNode(int index, out int safeIndex)
        {
            if (index < 0 || index >= N)
            {
                safeIndex = -1; return false;
            }
            safeIndex = index; return true;
        }

        public void InitializeMeasurementEdges()
        {
            if (_measurementBond == null) return;
            _isMeasurementEdge = new bool[N, N];
            if (_measuredSystem == null || _apparatus == null) return;
            foreach (int s in _measuredSystem)
                foreach (int a in _apparatus)
                {
                    _isMeasurementEdge[s, a] = true;
                    _isMeasurementEdge[a, s] = true;
                }
            foreach (int a1 in _apparatus)
                foreach (int a2 in _apparatus)
                {
                    if (a1 == a2) continue;
                    _isMeasurementEdge[a1, a2] = true;
                }
        }

        public void PinClusterStructure(IReadOnlyList<int> nodes)
        {
            if (nodes == null || nodes.Count == 0 || _edgePhase == null) return;
            const double beta = 0.2; // TODO replace with invariant-based adaptation
            double sumPhi = 0.0; int cntPhi = 0;
            var nodeSet = new HashSet<int>(nodes);
            foreach (int i in nodes)
            {
                foreach (int j in Neighbors(i))
                {
                    if (!nodeSet.Contains(j) || i >= j) continue;
                    sumPhi += _edgePhase[i, j]; cntPhi++;
                }
            }
            double meanPhi = cntPhi > 0 ? sumPhi / cntPhi : 0.0;
            foreach (int i in nodes)
            {
                foreach (int j in Neighbors(i))
                {
                    if (!nodeSet.Contains(j) || i >= j) continue;
                    double phi = _edgePhase[i, j];
                    double newPhi = phi + beta * (meanPhi - phi);
                    _edgePhase[i, j] = newPhi;
                    _edgePhase[j, i] = -newPhi;
                }
            }
        }

        public bool TryTriggerMeasurementAvalanche(out AvalancheInfo info)
        {
            info = default;
            double thr = AdaptiveHeavyThreshold;
            var clusters = GetStrongCorrelationClusters(thr);
            if (clusters.Count == 0) return false;
            List<int> cluster = clusters.OrderByDescending(c => c.Sum(v => ComputeNodeMass(v))).First();
            int excitedCount = cluster.Count(i => State[i] == NodeState.Excited);
            if (excitedCount == 0) return false;
            double excitationDensity = (double)excitedCount / cluster.Count;
            double sumW = 0.0; long cnt = 0;
            foreach (int i in cluster)
                foreach (int j in cluster)
                {
                    if (i >= j) continue;
                    sumW += Weights[i, j];
                    cnt++;
                }
            double meanW = cnt > 0 ? sumW / cnt : 0.0;
            double crit = excitationDensity * meanW;
            double threshold = _criticalityTracker.GetMedian();
            if (crit < threshold) return false;
            int steps = 0; int maxSteps = 100;
            var frontier = new Queue<int>(cluster.Where(i => State[i] == NodeState.Excited));
            var visited = new HashSet<int>(frontier);
            while (frontier.Count > 0 && steps < maxSteps)
            {
                int batch = frontier.Count;
                for (int b = 0; b < batch; b++)
                {
                    int v = frontier.Dequeue();
                    foreach (int u in Neighbors(v))
                    {
                        if (!cluster.Contains(u)) continue;
                        if (State[u] == NodeState.Rest)
                        {
                            State[u] = NodeState.Excited;
                            if (visited.Add(u)) frontier.Enqueue(u);
                        }
                    }
                }
                steps++;
            }
            int excitedFinal = cluster.Count(i => State[i] == NodeState.Excited);
            info = new AvalancheInfo { Size = cluster.Count, ExcitedFinal = excitedFinal, Duration = steps, Criticality = crit, AffectedNodes = cluster };
            _criticalityTracker.AddSample(crit);
            PerformLocalRenormalization(cluster, sumW);
            return true;
        }

        private void PerformLocalRenormalization(IReadOnlyList<int> nodes, double totalBefore)
        {
            if (nodes == null || nodes.Count == 0) return;
            var nodeSet = new HashSet<int>(nodes);
            var edges = new List<(int i, int j)>();
            foreach (int i in nodes)
                foreach (int j in Neighbors(i))
                    if (i < j && nodeSet.Contains(j)) edges.Add((i, j));
            if (edges.Count == 0) return;
            double sumW = 0.0;
            foreach (var (i, j) in edges) sumW += Weights[i, j];
            if (sumW <= 0.0) return;
            double target = sumW / edges.Count;
            const double alpha = 0.1; // сий алгоритмов TODO: replace with invariant adaptation
            foreach (var (i, j) in edges)
            {
                double w = Weights[i, j];
                double newW = w + alpha * (target - w);
                if (newW < 0.0) newW = 0.0; if (newW > 1.0) newW = 1.0;
                Weights[i, j] = newW; Weights[j, i] = newW;
            }
            double newSum = edges.Sum(e => Weights[e.i, e.j]);
            double scale = newSum > 1e-12 ? sumW / newSum : 1.0;
            if (Math.Abs(scale - 1.0) > 1e-6)
                foreach (var (i, j) in edges)
                {
                    double nw = Weights[i, j] * scale;
                    if (nw > 1.0) nw = 1.0;
                    Weights[i, j] = nw; Weights[j, i] = nw;
                }
        }

        public bool CheckMeasurementEvent()
        {
            if (_measurementDone) return false;
            if (_measuredSystem == null || _apparatus == null) return false;
            if (_measurementBond == null) _measurementBond = new bool[N, N];
            double sum = 0.0; int count = 0;
            foreach (int rawS in _measuredSystem)
            {
                if (!TryGetSafeNode(rawS, out var s)) continue;
                foreach (int rawA in _apparatus)
                {
                    if (!TryGetSafeNode(rawA, out var a)) continue;
                    if (Edges[s, a]) { sum += Weights[s, a]; count++; }
                }
            }
            if (count == 0) return false;
            double avg = sum / count;
            if (avg > _measurementThreshold)
            {
                _measurementDone = true;
                foreach (int rawS in _measuredSystem)
                {
                    if (!TryGetSafeNode(rawS, out var s)) continue;
                    foreach (int rawA in _apparatus)
                    {
                        if (!TryGetSafeNode(rawA, out var a)) continue;
                        if (Edges[s, a])
                        {
                            Weights[s, a] = Weights[a, s] = 1.0;
                            _measurementBond[s, a] = _measurementBond[a, s] = true;
                        }
                    }
                }
                ApplyMeasurementBackAction(_measuredSystem, _apparatus);
                PinClusterStructure(_measuredSystem);
                PinClusterStructure(_apparatus);
                var track = GetLongestClusterTrack();
                if (track != null)
                {
                    var charges = ComputeClusterCharges(track);
                    double massHat = track.MeanRestMassHat;
                    double qHat = charges.U1ChargeHat;
                    var sig = charges.ColorSignature;
                    //Console.WriteLine($"[MEAS-OBS] massHat={massHat:F3} qHat={qHat:F3} color=<{sig.MeanRe:F3},{sig.MeanIm:F3}>±<{sig.StdRe:F3},{sig.StdIm:F3}> loops={sig.LoopCount}");
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// RQ-Hypothesis Checklist Item 6.1: Local Measurement (RQM-specific)
        /// 
        /// In Relational Quantum Mechanics, measurement occurs only when an observer node
        /// interacts with an object node. The rest of the universe remains in superposition
        /// relative to this observer. There is no "God's Eye View" - states are relative.
        /// 
        /// This method performs a local measurement interaction between observer and object,
        /// applying decoherence only to the interacting nodes, not globally.
        /// </summary>
        /// <param name="observerNode">The observer node performing measurement</param>
        /// <param name="objectNode">The object node being measured</param>
        /// <returns>True if measurement interaction occurred</returns>
        public bool PerformLocalMeasurement(int observerNode, int objectNode)
        {
            if (observerNode < 0 || observerNode >= N || objectNode < 0 || objectNode >= N)
                return false;

            // Check if observer and object can interact (must be connected)
            if (!Edges[observerNode, objectNode])
                return false;

            // RQM principle: Measurement happens via correlation buildup
            double correlationStrength = Weights[observerNode, objectNode];
            if (correlationStrength < _measurementThreshold)
                return false;

            // Apply local decoherence to observer-object pair only
            // The rest of the universe maintains superposition relative to other observers
            ApplyLocalDecoherence(observerNode, objectNode);

            // Strengthen the correlation bond (entanglement between observer and object)
            Weights[observerNode, objectNode] = Math.Min(1.0, correlationStrength * 1.2);
            Weights[objectNode, observerNode] = Weights[observerNode, objectNode];

            // Record the measurement interaction
            if (_measurementBond == null) _measurementBond = new bool[N, N];
            _measurementBond[observerNode, objectNode] = true;
            _measurementBond[objectNode, observerNode] = true;

            return true;
        }

        /// <summary>
        /// Apply local decoherence to a pair of interacting nodes.
        /// RQM: Only the observer-object subsystem experiences wavefunction "collapse",
        /// while the rest of the universe remains coherent from other perspectives.
        /// </summary>
        private void ApplyLocalDecoherence(int observer, int objectNode)
        {
            if (_waveMulti == null) return;

            int d = GaugeDimension;

            // Apply decoherence to both observer and object
            foreach (int node in new[] { observer, objectNode })
            {
                // Get current probability amplitude
                double totalProb = 0.0;
                for (int a = 0; a < d; a++)
                {
                    int idx = node * d + a;
                    totalProb += _waveMulti[idx].Magnitude * _waveMulti[idx].Magnitude;
                }

                if (totalProb < 1e-10) continue;

                // Apply partial decoherence (reduce off-diagonal elements)
                double decoherenceRate = PhysicsConstants.MeasurementDecoherenceRate;

                for (int a = 0; a < d; a++)
                {
                    int idx = node * d + a;
                    // Reduce phase coherence while preserving amplitude
                    double magnitude = _waveMulti[idx].Magnitude;
                    double phase = _waveMulti[idx].Phase;

                    // Randomize phase slightly (decoherence)
                    double phaseNoise = (_rng.NextDouble() - 0.5) * 2 * Math.PI * decoherenceRate;
                    phase += phaseNoise;

                    _waveMulti[idx] = System.Numerics.Complex.FromPolarCoordinates(magnitude, phase);
                }
            }

            // Also apply decoherence to neighbors within measurement locality radius
            int localityRadius = PhysicsConstants.MeasurementLocalityRadius;
            var localNodes = new HashSet<int> { observer, objectNode };

            // BFS to find nodes within locality radius
            var queue = new Queue<(int node, int depth)>();
            queue.Enqueue((observer, 0));
            queue.Enqueue((objectNode, 0));
            var visited = new HashSet<int>(localNodes);

            while (queue.Count > 0)
            {
                var (current, depth) = queue.Dequeue();
                if (depth >= localityRadius) continue;

                foreach (int neighbor in Neighbors(current))
                {
                    if (visited.Contains(neighbor)) continue;
                    visited.Add(neighbor);

                    // Apply weaker decoherence to neighbors (inverse square law)
                    double neighborDecoherence = PhysicsConstants.MeasurementDecoherenceRate / ((depth + 1) * (depth + 1));

                    for (int a = 0; a < d; a++)
                    {
                        int idx = neighbor * d + a;
                        double magnitude = _waveMulti[idx].Magnitude;
                        double phase = _waveMulti[idx].Phase;
                        double phaseNoise = (_rng.NextDouble() - 0.5) * 2 * Math.PI * neighborDecoherence;
                        phase += phaseNoise;
                        _waveMulti[idx] = System.Numerics.Complex.FromPolarCoordinates(magnitude, phase);
                    }

                    queue.Enqueue((neighbor, depth + 1));
                }
            }
        }

        /// <summary>
        /// Check for and process all local measurement interactions.
        /// RQM: Multiple observers can measure simultaneously, each with their own
        /// relative state definitions.
        /// </summary>
        public int ProcessLocalMeasurements()
        {
            int measurementCount = 0;

            // Find all potential observer-object pairs with strong enough correlation
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue; // Process each pair once

                    // Check if this pair qualifies for measurement interaction
                    if (Weights[i, j] > _measurementThreshold)
                    {
                        // Randomly determine which node is "observer" and which is "object"
                        // In RQM, this distinction is relative
                        bool iIsObserver = _rng.NextDouble() < 0.5;
                        int observer = iIsObserver ? i : j;
                        int objectNode = iIsObserver ? j : i;

                        if (PerformLocalMeasurement(observer, objectNode))
                        {
                            measurementCount++;
                        }
                    }
                }
            }

            return measurementCount;
        }

        // Otsu-like adaptive measurement threshold without hard-coded bounds
        public void CalibrateMeasurementThreshold()
        {
            var weights = new List<double>();
            for (int i = 0; i < N; i++)
                for (int j = i + 1; j < N; j++)
                    if (Edges[i, j]) weights.Add(Weights[i, j]);

            if (weights.Count < 2)
            {
                _measurementThreshold = weights.Count == 1 ? weights[0] : 0.0;
                return;
            }

            weights.Sort();
            int n = weights.Count;
            double totalSum = 0.0;
            foreach (var w in weights) totalSum += w;

            double bestScore = double.NegativeInfinity;
            double bestThreshold = weights[n / 2]; // start from median

            double sumBg = 0.0;
            int wBg = 0;

            for (int t = 0; t < n - 1; t++)
            {
                double w = weights[t];
                wBg++;
                sumBg += w;

                double wFg = n - wBg;
                if (wBg == 0 || wFg == 0) continue;

                double meanBg = sumBg / wBg;
                double meanFg = (totalSum - sumBg) / wFg;
                double probBg = (double)wBg / n;
                double probFg = 1.0 - probBg;

                double betweenVar = probBg * probFg * (meanBg - meanFg) * (meanBg - meanFg);
                if (betweenVar > bestScore)
                {
                    bestScore = betweenVar;
                    bestThreshold = (w + weights[t + 1]) * 0.5;
                }
            }

            _measurementThreshold = bestThreshold;
        }

        public double MeasurementThreshold => _measurementThreshold;
        public int GetSystemSize() => _measuredSystem?.Count ?? 0;
        public int GetApparatusSize() => _apparatus?.Count ?? 0;
        public bool HasExcitedInSystem() => _measuredSystem != null && _measuredSystem.Any(i => State[i] == NodeState.Excited);
        public bool HasExcitedInApparatus() => _apparatus != null && _apparatus.Any(i => State[i] == NodeState.Excited);
    }
}

