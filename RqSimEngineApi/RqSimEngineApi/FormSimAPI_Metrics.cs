using RQSimulation;
using RQSimulation.GPUOptimized;
using RQSimulation.EventBasedModel;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RqSimUI.FormSimAPI.Interfaces;

namespace RqSimForms.Forms.Interfaces;

public partial class RqSimEngineApi
{
    // === Metrics Dispatcher (thread-safe, double-buffered) ===
    public MetricsDispatcher Dispatcher { get; } = new();

    // === Legacy Live Metrics (for compatibility, delegate to Dispatcher) ===
    public int LiveStep => Dispatcher.LiveStep;
    public int LiveExcited => Dispatcher.LiveExcited;
    public double LiveHeavyMass => Dispatcher.LiveHeavyMass;
    public int LiveLargestCluster => Dispatcher.LiveLargestCluster;
    public int LiveStrongEdges => Dispatcher.LiveStrongEdges;
    public double LiveQNorm => Dispatcher.LiveQNorm;
    public double LiveEntanglement => Dispatcher.LiveEntanglement;
    public double LiveCorrelation => Dispatcher.LiveCorrelation;
    public double LiveSpectralDim => Dispatcher.LiveSpectralDim;
    public double LiveTemp => Dispatcher.LiveTemp;
    public double LiveEffectiveG => Dispatcher.LiveEffectiveG;
    public double LiveAdaptiveThreshold => Dispatcher.LiveAdaptiveThreshold;
    public int LiveTotalSteps => Dispatcher.LiveTotalSteps;

    // Cluster statistics accessors
    public int LiveHeavyClusterCount => Dispatcher.LiveHeavyClusterCount;
    public int LiveTotalClusters => Dispatcher.LiveTotalClusters;
    public double LiveAvgClusterMass => Dispatcher.LiveAvgClusterMass;
    public double LiveMaxClusterMass => Dispatcher.LiveMaxClusterMass;
    public double LiveAvgDegree => Dispatcher.LiveAvgDegree;

    // === Time Series Buffers (full resolution, for export) ===
    public readonly List<int> SeriesSteps = [];
    public readonly List<int> SeriesExcited = [];
    public readonly List<double> SeriesHeavyMass = [];
    public readonly List<int> SeriesLargestCluster = [];
    public readonly List<double> SeriesEnergy = [];
    public readonly List<int> SeriesHeavyCount = [];
    public readonly List<double> SeriesAvgDist = [];
    public readonly List<double> SeriesDensity = [];
    public readonly List<double> SeriesCorr = [];
    public readonly List<int> SeriesStrongEdges = [];
    public readonly List<double> SeriesQNorm = [];
    public readonly List<double> SeriesQEnergy = [];
    public readonly List<double> SeriesEntanglement = [];
    public readonly List<double> SeriesSpectralDimension = [];
    public readonly List<double> SeriesNetworkTemperature = [];
    public readonly List<double> SeriesEffectiveG = [];
    public readonly List<double> SeriesAdaptiveThreshold = [];

    // === Synthesis Analysis ===
    public List<(int volume, double deltaMass)>? SynthesisData { get; set; }
    public int SynthesisCount { get; set; }
    public int FissionCount { get; set; }

    /// <summary>
    /// Collects metrics from current graph state (full version - expensive)
    /// </summary>
    public (int excited, double heavyMass, int heavyCount, int largestCluster, double energy,
            int strongEdges, double correlation, double qNorm, double entanglement,
            int totalClusters, double avgClusterMass, double maxClusterMass, double avgDegree)
        CollectMetrics()
    {
        if (SimulationEngine?.Graph == null)
            return (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var graph = SimulationEngine.Graph;

        int excited = graph.State.Count(s => s == NodeState.Excited);
        double effectiveThreshold = Math.Min(graph.GetAdaptiveHeavyThreshold(), RQGraph.HeavyClusterThreshold);
        var heavyStats = graph.GetHeavyClusterStatsCorrelationMass(effectiveThreshold, RQGraph.HeavyClusterMinSize);
        double heavyMass = heavyStats.totalMass;
        int heavyCount = heavyStats.count;

        var clusters = graph.GetStrongCorrelationClusters(effectiveThreshold);
        int largestCluster = clusters.Count > 0 ? clusters.Max(c => c.Count) : 0;
        int totalClusters = clusters.Count;

        // Compute cluster mass statistics
        double avgClusterMass = 0.0;
        double maxClusterMass = 0.0;
        if (clusters.Count > 0)
        {
            double totalMassSum = 0.0;
            foreach (var cluster in clusters)
            {
                double clusterMass = 0.0;
                foreach (int u in cluster)
                {
                    foreach (int v in cluster)
                    {
                        if (u < v && graph.Edges[u, v])
                            clusterMass += graph.Weights[u, v];
                    }
                }
                totalMassSum += clusterMass;
                if (clusterMass > maxClusterMass)
                    maxClusterMass = clusterMass;
            }
            avgClusterMass = totalMassSum / clusters.Count;
        }

        double energy = graph.ComputeTotalEnergy();

        var weightStats = graph.GetWeightStats(effectiveThreshold);
        int strongEdges = weightStats.strongEdges;
        double correlation = weightStats.avgWeight;
        double qNorm = graph.GetQuantumNorm();

        // Compute average degree
        var edgeStats = graph.GetEdgeStats();
        double avgDegree = edgeStats.avgDegree;

        var largestClusterNodes = clusters.Count > 0 ? clusters.OrderByDescending(c => c.Count).First() : new List<int>();
        double entanglement = largestClusterNodes.Count > 0 ? graph.ComputeEntanglementEntropy(largestClusterNodes) : 0.0;

        return (excited, heavyMass, heavyCount, largestCluster, energy, strongEdges, correlation, qNorm, entanglement,
                totalClusters, avgClusterMass, maxClusterMass, avgDegree);
    }

    /// <summary>
    /// Collects only lightweight metrics (fast - O(N) instead of O(N?))
    /// Use this for steps where full metrics are not needed
    /// </summary>
    public int CollectExcitedCount()
    {
        if (SimulationEngine?.Graph == null) return 0;
        return SimulationEngine.Graph.State.Count(s => s == NodeState.Excited);
    }

    /// <summary>
    /// Stores metrics to time series buffers and dispatcher.
    /// Called from calculation thread - minimal lock time.
    /// </summary>
    public void StoreMetrics(int step, int excited, double heavyMass, int heavyCount, int largestCluster,
                             double energy, int strongEdges, double correlation, double qNorm, double entanglement,
                             double spectralDim, double temp, double effectiveG, double threshold,
                             int totalClusters = 0, double avgClusterMass = 0.0, double maxClusterMass = 0.0, double avgDegree = 0.0,
                             int edgeCount = 0, int componentCount = 1)
    {
        // Store to full-resolution lists (for export)
        SeriesSteps.Add(step);
        SeriesExcited.Add(excited);
        SeriesHeavyMass.Add(heavyMass);
        SeriesLargestCluster.Add(largestCluster);
        SeriesEnergy.Add(energy);
        SeriesHeavyCount.Add(heavyCount);
        SeriesStrongEdges.Add(strongEdges);
        SeriesCorr.Add(correlation);
        SeriesQNorm.Add(qNorm);
        SeriesEntanglement.Add(entanglement);
        SeriesSpectralDimension.Add(spectralDim);
        SeriesNetworkTemperature.Add(temp);
        SeriesEffectiveG.Add(effectiveG);
        SeriesAdaptiveThreshold.Add(threshold);

        // Get node count for ratios
        int nodeCount = SimulationEngine?.Graph?.N ?? 0;

        // Update dispatcher (thread-safe, will be decimated for UI)
        // NOTE: Pass Dispatcher.LiveTotalSteps instead of LastConfig.TotalSteps
        // because in event-based mode, LiveTotalSteps is set to sweepCount, not config steps
        int totalStepsForUi = Dispatcher.LiveTotalSteps > 0 ? Dispatcher.LiveTotalSteps : (LastConfig?.TotalSteps ?? 0);
        Dispatcher.UpdateLiveMetrics(step, excited, heavyMass, largestCluster, strongEdges,
            qNorm, entanglement, correlation, spectralDim, temp, effectiveG, totalStepsForUi, threshold,
            heavyCount, totalClusters, avgClusterMass, maxClusterMass, avgDegree,
            edgeCount, componentCount, nodeCount);

        Dispatcher.AppendTimeSeriesPoint(step, excited, heavyMass, heavyCount, largestCluster,
            energy, strongEdges, correlation, qNorm, entanglement, spectralDim, temp, effectiveG, threshold);

        // Update OpenTelemetry observable gauges so MeterListener can poll them
        RQSimulation.Core.Observability.RqSimPlatformTelemetry.UpdateGraphMetrics(nodeCount, edgeCount);
        RQSimulation.Core.Observability.RqSimPlatformTelemetry.UpdatePhysicsMetrics(
            vacuumEnergy: energy,
            spectralDimension: spectralDim);
    }

    /// <summary>
    /// Clears all time series buffers and dispatcher
    /// </summary>
    public void ClearTimeSeries()
    {
        SeriesSteps.Clear();
        SeriesExcited.Clear();
        SeriesHeavyMass.Clear();
        SeriesLargestCluster.Clear();
        SeriesEnergy.Clear();
        SeriesHeavyCount.Clear();
        SeriesStrongEdges.Clear();
        SeriesCorr.Clear();
        SeriesQNorm.Clear();
        SeriesEntanglement.Clear();
        SeriesSpectralDimension.Clear();
        SeriesNetworkTemperature.Clear();
        SeriesEffectiveG.Clear();
        SeriesAdaptiveThreshold.Clear();
        SeriesAvgDist.Clear();
        SeriesDensity.Clear();
        SeriesQEnergy.Clear();

        // Clear dispatcher for new simulation
        Dispatcher.Clear();
    }

    /// <summary>
    /// Creates decimated time series for compact export (max ~1000 points)
    /// </summary>
    public DecimatedDynamics GetDecimatedDynamics(int maxPoints = 1000)
    {
        int totalPoints = SeriesSteps.Count;
        if (totalPoints == 0)
            return new DecimatedDynamics();

        int stride = Math.Max(1, totalPoints / maxPoints);
        int resultCount = (totalPoints + stride - 1) / stride;

        int[] steps = new int[resultCount];
        int[] excited = new int[resultCount];
        double[] energy = new double[resultCount];
        double[] heavyMass = new double[resultCount];
        int[] largestCluster = new int[resultCount];
        int[] strongEdges = new int[resultCount];
        double[] spectralDim = new double[resultCount];
        double[] temp = new double[resultCount];

        for (int i = 0, j = 0; i < totalPoints && j < resultCount; i += stride, j++)
        {
            steps[j] = SeriesSteps[i];
            excited[j] = SeriesExcited[i];
            energy[j] = i < SeriesEnergy.Count ? SeriesEnergy[i] : 0;
            heavyMass[j] = i < SeriesHeavyMass.Count ? SeriesHeavyMass[i] : 0;
            largestCluster[j] = i < SeriesLargestCluster.Count ? SeriesLargestCluster[i] : 0;
            strongEdges[j] = i < SeriesStrongEdges.Count ? SeriesStrongEdges[i] : 0;
            spectralDim[j] = i < SeriesSpectralDimension.Count ? SeriesSpectralDimension[i] : 0;
            temp[j] = i < SeriesNetworkTemperature.Count ? SeriesNetworkTemperature[i] : 0;
        }

        return new DecimatedDynamics
        {
            TotalPoints = totalPoints,
            DecimationStride = stride,
            Steps = steps,
            Excited = excited,
            Energy = energy,
            HeavyMass = heavyMass,
            LargestCluster = largestCluster,
            StrongEdges = strongEdges,
            SpectralDimension = spectralDim,
            NetworkTemperature = temp
        };
    }

    /// <summary>
    /// Compact decimated time series for export
    /// </summary>
    public record DecimatedDynamics
    {
        public int TotalPoints { get; init; }
        public int DecimationStride { get; init; }
        public int[] Steps { get; init; } = [];
        public int[] Excited { get; init; } = [];
        public double[] Energy { get; init; } = [];
        public double[] HeavyMass { get; init; } = [];
        public int[] LargestCluster { get; init; } = [];
        public int[] StrongEdges { get; init; } = [];
        public double[] SpectralDimension { get; init; } = [];
        public double[] NetworkTemperature { get; init; } = [];
    }
}
