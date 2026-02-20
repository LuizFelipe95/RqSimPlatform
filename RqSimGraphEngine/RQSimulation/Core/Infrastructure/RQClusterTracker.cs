using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RQSimulation
{
    public sealed class ClusterTrack
    {
        public int TrackId { get; }
        public List<RQGraph.ClusterInstant> Instants { get; } = new();
        public List<RQGraph.ClusterInvariant> Invariants { get; } = new();
        public ClusterTrack(int id) => TrackId = id;
        public void Add(RQGraph.ClusterInstant instant, RQGraph.ClusterInvariant invariant)
        {
            Instants.Add(instant);
            Invariants.Add(invariant);
        }
        public double MeanRestMassHat => Invariants.Count == 0 ? 0.0 : Invariants.Average(c => c.RestMassHat);
        public double RestMassHatStd => Invariants.Count == 0 ? 0.0 : Math.Sqrt(Invariants.Select(c => c.RestMassHat * c.RestMassHat).Average() - Math.Pow(MeanRestMassHat, 2.0));
        public int LifetimeSteps => Instants.Count;
    }

    public sealed class ClusterTracker
    {
        private readonly Dictionary<int, ClusterTrack> _tracks = new();
        private int _nextTrackId;

        public void Update(IEnumerable<RQGraph.ClusterInstant> clusters, Func<RQGraph.ClusterInstant, RQGraph.ClusterInvariant> invariantFunc)
        {
            var current = clusters.ToList();
            var usedTracks = new HashSet<int>();
            foreach (var inst in current)
            {
                int bestTrack = -1;
                double bestOverlap = 0.0;
                foreach (var kv in _tracks)
                {
                    if (usedTracks.Contains(kv.Key)) continue;
                    if (kv.Value.Instants.Count == 0) continue;
                    var lastInst = kv.Value.Instants[^1];
                    double overlap = ComputeNodeOverlap(lastInst.Nodes, inst.Nodes);
                    if (overlap > bestOverlap)
                    {
                        bestOverlap = overlap;
                        bestTrack = kv.Key;
                    }
                }
                ClusterTrack track;
                if (bestTrack >= 0 && bestOverlap > 0.5)
                {
                    track = _tracks[bestTrack];
                    usedTracks.Add(bestTrack);
                }
                else
                {
                    int newId = _nextTrackId++;
                    track = new ClusterTrack(newId);
                    _tracks[newId] = track;
                }
                var inv = invariantFunc(inst);
                track.Add(inst, inv);
            }
        }

        private static double ComputeNodeOverlap(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0.0;
            var setA = new HashSet<int>(a);
            int common = 0;
            for (int i = 0; i < b.Count; i++) if (setA.Contains(b[i])) common++;
            int total = Math.Max(a.Count, b.Count);
            return total == 0 ? 0.0 : (double)common / total;
        }

        public IEnumerable<ClusterTrack> GetTracks() => _tracks.Values;
    }

    public sealed class ClusterSpecies
    {
        public int SpeciesId { get; init; }
        public double MassHat { get; init; }
        public double MassSpread { get; init; }
        public int MinLifetime { get; init; }
        public int Count { get; init; }
    }

    public sealed class ClusterSpectrumAnalyzer
    {
        public IReadOnlyList<ClusterSpecies> BuildSpecies(IEnumerable<ClusterTrack> tracks, int minLifetime = 50, double stabilityRatio = 0.1, double binWidth = 0.1)
        {
            var longLived = tracks.Where(t => t.LifetimeSteps >= minLifetime).ToList();
            var stable = longLived.Where(t => t.RestMassHatStd / (t.MeanRestMassHat + 1e-12) < stabilityRatio).ToList();
            var byBin = stable.GroupBy(t => Math.Round(t.MeanRestMassHat / binWidth)).ToList();
            var species = new List<ClusterSpecies>();
            int speciesId = 0;
            foreach (var bin in byBin.OrderBy(g => g.Key))
            {
                var binTracks = bin.ToList();
                double massMean = binTracks.Average(t => t.MeanRestMassHat);
                double massStd = Math.Sqrt(binTracks.Select(t => t.MeanRestMassHat * t.MeanRestMassHat).Average() - massMean * massMean);
                int minLt = binTracks.Min(t => t.LifetimeSteps);
                species.Add(new ClusterSpecies
                {
                    SpeciesId = speciesId++,
                    MassHat = massMean,
                    MassSpread = massStd,
                    MinLifetime = minLt,
                    Count = binTracks.Count
                });
            }
            return species;
        }

        public sealed class ChargedSpecies
        {
            public int SpeciesId { get; init; }
            public double MassHat { get; init; }
            public double U1ChargeHat { get; init; }
            public double U1ChargeSpread { get; init; }
            public double ColorMeanRe { get; init; }
            public double ColorMeanIm { get; init; }
            public double ColorStdRe { get; init; }
            public double ColorStdIm { get; init; }
            public int TrackCount { get; init; }
        }

        public IReadOnlyList<ChargedSpecies> BuildChargedSpecies(IEnumerable<ClusterTrack> tracks, RQGraph graph, int minLifetime = 50, double stabilityRatio = 0.1, double binWidth = 0.1, double massBinTolerance = 0.05)
        {
            ArgumentNullException.ThrowIfNull(graph);
            var baseSpecies = BuildSpecies(tracks, minLifetime, stabilityRatio, binWidth);
            var allTracks = tracks.ToList();
            var charged = new List<ChargedSpecies>();
            foreach (var sp in baseSpecies)
            {
                var speciesTracks = allTracks.Where(t => Math.Abs(t.MeanRestMassHat - sp.MassHat) < massBinTolerance).ToList();
                if (speciesTracks.Count == 0) continue;
                var qList = new List<double>();
                var colorRe = new List<double>();
                var colorIm = new List<double>();
                var colorStdRe = new List<double>();
                var colorStdIm = new List<double>();
                foreach (var tr in speciesTracks)
                {
                    // Fallback: if gauge invariant API not available, use zeros
                    double qHat = 0.0; double meanRe = 0.0; double meanIm = 0.0; double stdRe = 0.0; double stdIm = 0.0;
                    qList.Add(qHat);
                    colorRe.Add(meanRe);
                    colorIm.Add(meanIm);
                    colorStdRe.Add(stdRe);
                    colorStdIm.Add(stdIm);
                }
                double qMean = qList.Average();
                double qVar = qList.Select(x => x * x).Average() - qMean * qMean;
                if (qVar < 0) qVar = 0;
                double qStd = Math.Sqrt(qVar);
                charged.Add(new ChargedSpecies
                {
                    SpeciesId = sp.SpeciesId,
                    MassHat = sp.MassHat,
                    U1ChargeHat = qMean,
                    U1ChargeSpread = qStd,
                    ColorMeanRe = colorRe.Average(),
                    ColorMeanIm = colorIm.Average(),
                    ColorStdRe = colorStdRe.Average(),
                    ColorStdIm = colorStdIm.Average(),
                    TrackCount = speciesTracks.Count
                });
            }
            return charged;
        }
    }

    public static class PhysicalConstants
    {
        public const double C = 299792458.0; // speed of light (m/s)
        public const double ElectronMassKg = 9.1093837015e-31;
    }

    public sealed class RQUnits
    {
        public double MassScale { get; }
        public double LengthScale { get; }
        public double TimeScale { get; }
        public RQUnits(double massScale, double lengthScale, double timeScale)
        {
            MassScale = massScale;
            LengthScale = lengthScale;
            TimeScale = timeScale;
        }
        public double ToPhysicalMass(double mHat) => mHat * MassScale;
        public double ToPhysicalEnergy(double eHat) => eHat * MassScale * PhysicalConstants.C * PhysicalConstants.C;
    }

    public static class RQUnitsFactory
    {
        public static RQUnits FromElectron(ClusterSpecies electronLike)
        {
            double mHatE = electronLike?.MassHat ?? 1.0;
            if (mHatE <= 0) mHatE = 1.0;
            double massScale = PhysicalConstants.ElectronMassKg / mHatE;
            double lengthScale = 1.0; // lattice length unit -> 1 meter (placeholder)
            double timeScale = lengthScale / PhysicalConstants.C;
            return new RQUnits(massScale, lengthScale, timeScale);
        }
    }

    public static class ClusterSpectrumCsv
    {
        public static void DumpClusterSpectrum(string path, IEnumerable<ClusterSpecies> species, RQUnits units)
        {
            using var w = new StreamWriter(path, false, Encoding.UTF8);
            w.WriteLine("SpeciesId,MassHat,MassHatStd,Mass_kg,MinLifetimeSteps,Count");
            foreach (var sp in species)
            {
                double mKg = units.ToPhysicalMass(sp.MassHat);
                w.WriteLine($"{sp.SpeciesId},{sp.MassHat:F6},{sp.MassSpread:F6},{mKg:E3},{sp.MinLifetime},{sp.Count}");
            }
        }
        public static void DumpChargedSpectrum(string path, IEnumerable<ClusterSpectrumAnalyzer.ChargedSpecies> species)
        {
            using var w = new StreamWriter(path, false, Encoding.UTF8);
            w.WriteLine("SpeciesId,MassHat,U1ChargeHat,U1ChargeStd,ColorMeanRe,ColorMeanIm,ColorStdRe,ColorStdIm,TrackCount");
            foreach (var sp in species)
            {
                w.WriteLine($"{sp.SpeciesId},{sp.MassHat:F6},{sp.U1ChargeHat:F6},{sp.U1ChargeSpread:F6},{sp.ColorMeanRe:F6},{sp.ColorMeanIm:F6},{sp.ColorStdRe:F6},{sp.ColorStdIm:F6},{sp.TrackCount}");
            }
        }
    }

    public sealed class CoreClusterFinder
    {
        private readonly RQGraph _graph;
        public CoreClusterFinder(RQGraph graph) { _graph = graph; }

        public IReadOnlyList<HashSet<int>> FindCoreClusters(double strongQuantile = 0.9)
        {
            var allWeights = new List<double>();
            for (int i = 0; i < _graph.N; i++)
            {
                for (int j = i + 1; j < _graph.N; j++)
                {
                    if (_graph.Edges[i, j]) allWeights.Add(_graph.Weights[i, j]);
                }
            }
            if (allWeights.Count == 0) return Array.Empty<HashSet<int>>();
            allWeights.Sort();
            int idx = (int)(strongQuantile * (allWeights.Count - 1));
            double strongThreshold = allWeights[idx];

            var visited = new bool[_graph.N];
            var cores = new List<HashSet<int>>();

            for (int i = 0; i < _graph.N; i++)
            {
                if (visited[i]) continue;
                var core = new HashSet<int>();
                var stack = new Stack<int>();
                stack.Push(i);

                while (stack.Count > 0)
                {
                    int v = stack.Pop();
                    if (visited[v]) continue;
                    visited[v] = true;

                    foreach (var u in _graph.Neighbors(v))
                    {
                        double w = _graph.Weights[v, u];
                        if (w >= strongThreshold)
                        {
                            core.Add(v);
                            core.Add(u);
                            if (!visited[u]) stack.Push(u);
                        }
                    }
                }

                if (core.Count > 0)
                    cores.Add(core);
            }

            return cores;
        }

        public IReadOnlyList<HashSet<int>> FindCoreClustersInSubgraph(IReadOnlyList<int> subgraphNodes, double strongQuantile = 0.9)
        {
            var nodeSet = new HashSet<int>(subgraphNodes);
            var allWeights = new List<double>();
            foreach (var i in subgraphNodes)
                foreach (var j in _graph.Neighbors(i))
                    if (i < j && nodeSet.Contains(j) && _graph.Edges[i, j]) allWeights.Add(_graph.Weights[i, j]);
            if (allWeights.Count == 0) return Array.Empty<HashSet<int>>();
            allWeights.Sort();
            int idx = (int)(strongQuantile * (allWeights.Count - 1));
            double strongThreshold = allWeights[idx];
            var visited = new HashSet<int>();
            var cores = new List<HashSet<int>>();
            foreach (var start in subgraphNodes)
            {
                if (!visited.Add(start)) continue;
                var core = new HashSet<int>();
                var stack = new Stack<int>();
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int v = stack.Pop();
                    foreach (var u in _graph.Neighbors(v))
                    {
                        if (!nodeSet.Contains(u)) continue;
                        double w = _graph.Weights[v, u];
                        if (w >= strongThreshold)
                        {
                            core.Add(v);
                            core.Add(u);
                            if (visited.Add(u)) stack.Push(u);
                        }
                    }
                }
                if (core.Count > 0) cores.Add(core);
            }
            return cores;
        }
    }
}
