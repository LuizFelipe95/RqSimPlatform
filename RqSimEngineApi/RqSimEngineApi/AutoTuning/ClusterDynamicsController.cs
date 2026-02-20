using System;
using System.Collections.Generic;
using System.Linq;
using RQSimulation;

namespace RqSimForms.Forms.Interfaces.AutoTuning;

/// <summary>
/// Controls cluster dynamics and decoherence to prevent giant cluster formation.
/// 
/// RQ-HYPOTHESIS PHYSICS:
/// Clusters represent correlated quantum subsystems (proto-particles).
/// 
/// Key dynamics:
/// - Healthy: Multiple medium-sized clusters (particle-like behavior)
/// - Unhealthy: Single giant cluster (over-correlation, no structure)
/// - Unhealthy: No clusters (fragmentation, no particles)
/// 
/// Giant cluster formation must be prevented because:
/// - It destroys spectral dimension (d_S ? ? for complete graph)
/// - It eliminates emergent structure
/// - It represents thermal death of the simulated universe
/// 
/// Decoherence is the primary tool to break giant clusters:
/// - Weakens over-correlated edges
/// - Allows natural cluster formation at healthy sizes
/// </summary>
public sealed class ClusterDynamicsController
{
    private readonly AutoTuningConfig _config;

    // Cluster tracking
    private int _largestClusterSize;
    private int _clusterCount;
    private double _clusterRatio; // Largest / N
    private int _nodeCount;

    // Giant cluster persistence tracking
    private int _giantClusterPersistence;
    private int _extremeClusterPersistence;

    // Decoherence state
    private double _currentDecoherence;
    private bool _topologyTunnelingRequested;

    // Diagnostics
    private string _lastDiagnostics = "";
    private ClusterStatus _status = ClusterStatus.Healthy;

    /// <summary>Size of the largest cluster.</summary>
    public int LargestClusterSize => _largestClusterSize;

    /// <summary>Number of clusters detected.</summary>
    public int ClusterCount => _clusterCount;

    /// <summary>Ratio of largest cluster to total nodes.</summary>
    public double ClusterRatio => _clusterRatio;

    /// <summary>Current decoherence rate.</summary>
    public double CurrentDecoherence => _currentDecoherence;

    /// <summary>Current cluster status.</summary>
    public ClusterStatus Status => _status;

    /// <summary>Whether topology tunneling has been requested.</summary>
    public bool TopologyTunnelingRequested => _topologyTunnelingRequested;

    /// <summary>Consecutive steps with giant cluster.</summary>
    public int GiantClusterPersistence => _giantClusterPersistence;

    /// <summary>Diagnostic information.</summary>
    public string LastDiagnostics => _lastDiagnostics;

    /// <summary>
    /// Creates a new cluster dynamics controller.
    /// </summary>
    public ClusterDynamicsController(AutoTuningConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _currentDecoherence = config.BaseDecoherenceRate;
    }

    /// <summary>
    /// Initializes the controller with starting decoherence rate.
    /// </summary>
    public void Initialize(double startingDecoherence)
    {
        _currentDecoherence = startingDecoherence;
        _giantClusterPersistence = 0;
        _extremeClusterPersistence = 0;
        _topologyTunnelingRequested = false;
        _status = ClusterStatus.Healthy;
    }

    /// <summary>
    /// Analyzes cluster state and computes decoherence adjustments.
    /// </summary>
    /// <param name="graph">The RQ graph</param>
    /// <param name="largestClusterSize">Size of largest cluster (pre-computed)</param>
    /// <param name="clusterCount">Number of clusters (pre-computed)</param>
    /// <returns>Cluster adjustment result</returns>
    public ClusterAdjustmentResult AnalyzeAndAdjust(
        RQGraph graph,
        int largestClusterSize,
        int clusterCount)
    {
        ArgumentNullException.ThrowIfNull(graph);

        _nodeCount = graph.N;
        _largestClusterSize = largestClusterSize;
        _clusterCount = clusterCount;
        _clusterRatio = (double)largestClusterSize / Math.Max(1, _nodeCount);

        var diagnostics = new List<string>
        {
            $"Clusters: {clusterCount}, Largest: {largestClusterSize}/{_nodeCount} ({_clusterRatio:P0})"
        };

        ClusterStatus previousStatus = _status;
        double newDecoherence = _currentDecoherence;
        var actions = new List<ClusterAction>();

        // Determine status based on cluster ratio
        if (_clusterRatio >= _config.ExtremeClusterThreshold)
        {
            _status = ClusterStatus.Extreme;
            _extremeClusterPersistence++;
            _giantClusterPersistence++;
        }
        else if (_clusterRatio >= _config.EmergencyClusterThreshold)
        {
            _status = ClusterStatus.Emergency;
            _extremeClusterPersistence = 0;
            _giantClusterPersistence++;
        }
        else if (_clusterRatio >= _config.GiantClusterThreshold)
        {
            _status = ClusterStatus.Giant;
            _extremeClusterPersistence = 0;
            _giantClusterPersistence++;
        }
        else
        {
            // Cluster is under control
            if (_clusterRatio < _config.GiantClusterThreshold * 0.5)
            {
                // Definitely healthy
                _giantClusterPersistence = 0;
                _extremeClusterPersistence = 0;
                _status = clusterCount >= 3 ? ClusterStatus.Healthy : ClusterStatus.TooFew;
            }
            else
            {
                // Borderline - reduce persistence slowly
                _giantClusterPersistence = Math.Max(0, _giantClusterPersistence - 1);
                _extremeClusterPersistence = 0;
                _status = ClusterStatus.Recovering;
            }
        }

        // Take action based on status
        switch (_status)
        {
            case ClusterStatus.Extreme:
                diagnostics.Add($"EXTREME: persist={_extremeClusterPersistence}");
                newDecoherence = _config.MaxDecoherenceRate;
                actions.Add(ClusterAction.MaxDecoherence);
                actions.Add(ClusterAction.ReduceGravity);

                // Trigger topology tunneling after persistent extreme cluster
                if (_extremeClusterPersistence >= _config.TopologyTunnelingTriggerCount)
                {
                    _topologyTunnelingRequested = true;
                    actions.Add(ClusterAction.TopologyTunneling);
                    diagnostics.Add("TOPOLOGY TUNNELING TRIGGERED");

                    // Apply decoherence directly to graph
                    int weakened = ApplyGiantClusterDecoherence(graph);
                    diagnostics.Add($"Weakened {weakened} edges");

                    // Perform topology tunneling
                    int removed = PerformTopologyTunneling(graph, 0.30);
                    diagnostics.Add($"Tunneled {removed} edges");
                }
                else
                {
                    // Apply decoherence
                    int weakened = ApplyGiantClusterDecoherence(graph);
                    actions.Add(ClusterAction.ApplyDecoherence);
                    diagnostics.Add($"Weakened {weakened} edges");
                }
                break;

            case ClusterStatus.Emergency:
                diagnostics.Add("EMERGENCY cluster detected");
                newDecoherence = Math.Min(_config.MaxDecoherenceRate,
                    _currentDecoherence * _config.ClusterDecoherenceBoost + _config.BaseDecoherenceRate);
                actions.Add(ClusterAction.BoostDecoherence);
                actions.Add(ClusterAction.ReduceGravity);

                // Apply decoherence
                int weakeningCount = ApplyGiantClusterDecoherence(graph);
                actions.Add(ClusterAction.ApplyDecoherence);
                diagnostics.Add($"Weakened {weakeningCount} edges");

                // Inject noise into giant clusters
                InjectDecoherenceNoise(graph, diagnostics);
                break;

            case ClusterStatus.Giant:
                diagnostics.Add("Giant cluster forming");
                newDecoherence = Math.Min(0.05,
                    _currentDecoherence * 1.5 + _config.BaseDecoherenceRate * 0.5);
                actions.Add(ClusterAction.IncreaseDecoherence);

                int gentleWeakening = ApplyGiantClusterDecoherence(graph);
                if (gentleWeakening > 0)
                {
                    actions.Add(ClusterAction.ApplyDecoherence);
                    diagnostics.Add($"Gentle weakening: {gentleWeakening} edges");
                }
                break;

            case ClusterStatus.Recovering:
                diagnostics.Add($"Recovering from giant cluster (persist={_giantClusterPersistence})");
                // Slowly reduce decoherence
                newDecoherence = Math.Max(_config.BaseDecoherenceRate,
                    _currentDecoherence * 0.9);
                break;

            case ClusterStatus.TooFew:
                diagnostics.Add("Too few clusters - may need threshold adjustment");
                // Reduce decoherence to allow cluster formation
                newDecoherence = Math.Max(_config.MinDecoherenceRate,
                    _currentDecoherence * 0.7);
                actions.Add(ClusterAction.ReduceDecoherence);
                break;

            case ClusterStatus.Healthy:
                // Healthy - restore normal decoherence gradually
                newDecoherence = _currentDecoherence +
                    0.1 * (_config.BaseDecoherenceRate - _currentDecoherence);
                _topologyTunnelingRequested = false;
                break;
        }

        // Clamp decoherence
        newDecoherence = Math.Clamp(newDecoherence,
            _config.MinDecoherenceRate, _config.MaxDecoherenceRate);

        bool changed = Math.Abs(newDecoherence - _currentDecoherence) > _currentDecoherence * 0.01;
        _currentDecoherence = newDecoherence;
        _lastDiagnostics = string.Join("; ", diagnostics);

        return new ClusterAdjustmentResult(
            NewDecoherence: newDecoherence,
            Changed: changed,
            Status: _status,
            StatusChanged: _status != previousStatus,
            RecommendedActions: actions.ToArray(),
            Diagnostics: _lastDiagnostics
        );
    }

    /// <summary>
    /// Gets recommended gravity multiplier based on cluster state.
    /// </summary>
    public double GetGravityMultiplier()
    {
        return _status switch
        {
            ClusterStatus.Extreme => 0.1,    // Near-zero gravity
            ClusterStatus.Emergency => 0.5,   // Half gravity
            ClusterStatus.Giant => 0.8,       // Reduced gravity
            ClusterStatus.Recovering => 0.9,  // Slightly reduced
            ClusterStatus.TooFew => 1.2,      // Boost to form clusters
            _ => 1.0                          // Normal
        };
    }

    /// <summary>
    /// Gets recommended edge trial probability multiplier.
    /// </summary>
    public double GetEdgeTrialMultiplier()
    {
        return _status switch
        {
            ClusterStatus.Extreme => 0.3,     // Prevent reconnection
            ClusterStatus.Emergency => 0.5,
            ClusterStatus.Giant => 0.7,
            ClusterStatus.TooFew => 1.5,      // Boost for connectivity
            _ => 1.0
        };
    }

    /// <summary>
    /// Clears the topology tunneling request after it has been processed.
    /// </summary>
    public void ClearTopologyTunnelingRequest()
    {
        _topologyTunnelingRequested = false;
    }

    /// <summary>
    /// Resets the controller state.
    /// </summary>
    public void Reset()
    {
        _currentDecoherence = _config.BaseDecoherenceRate;
        _giantClusterPersistence = 0;
        _extremeClusterPersistence = 0;
        _topologyTunnelingRequested = false;
        _status = ClusterStatus.Healthy;
        _largestClusterSize = 0;
        _clusterCount = 0;
        _clusterRatio = 0;
    }

    // ============================================================
    // PRIVATE METHODS
    // ============================================================

    /// <summary>
    /// Applies decoherence to over-correlated edges in the graph.
    /// </summary>
    private int ApplyGiantClusterDecoherence(RQGraph graph)
    {
        try
        {
            return graph.ApplyGiantClusterDecoherence();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Performs topology tunneling by removing a fraction of internal cluster edges.
    /// </summary>
    private int PerformTopologyTunneling(RQGraph graph, double removalFraction)
    {
        try
        {
            return graph.PerformTopologyTunneling(removalFraction);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Injects decoherence noise into giant clusters.
    /// </summary>
    private void InjectDecoherenceNoise(RQGraph graph, List<string> diagnostics)
    {
        try
        {
            var clusters = graph.GetStrongCorrelationClusters(graph.GetAdaptiveHeavyThreshold());
            int giantThreshold = (int)(_nodeCount * _config.EmergencyClusterThreshold);

            foreach (var cluster in clusters.Where(c => c.Count >= giantThreshold))
            {
                double amplitude = Math.Clamp(_currentDecoherence * 10.0, 0.05, 0.25);
                graph.InjectDecoherenceIntoCluster(cluster, noiseAmplitude: amplitude);
                diagnostics.Add($"Noise injected into cluster size {cluster.Count} (amp={amplitude:F3})");
            }
        }
        catch
        {
            // Ignore errors in noise injection
        }
    }
}

/// <summary>
/// Cluster health status.
/// </summary>
public enum ClusterStatus
{
    /// <summary>Healthy cluster distribution.</summary>
    Healthy,

    /// <summary>Too few clusters detected.</summary>
    TooFew,

    /// <summary>Giant cluster detected (needs attention).</summary>
    Giant,

    /// <summary>Emergency - giant cluster is large.</summary>
    Emergency,

    /// <summary>Extreme - giant cluster dominates, topology tunneling may be needed.</summary>
    Extreme,

    /// <summary>Recovering from giant cluster state.</summary>
    Recovering
}

/// <summary>
/// Recommended actions for cluster management.
/// </summary>
public enum ClusterAction
{
    /// <summary>No action.</summary>
    None,

    /// <summary>Increase decoherence rate.</summary>
    IncreaseDecoherence,

    /// <summary>Boost decoherence significantly.</summary>
    BoostDecoherence,

    /// <summary>Set decoherence to maximum.</summary>
    MaxDecoherence,

    /// <summary>Reduce decoherence to allow cluster formation.</summary>
    ReduceDecoherence,

    /// <summary>Apply decoherence directly to graph edges.</summary>
    ApplyDecoherence,

    /// <summary>Reduce gravitational coupling.</summary>
    ReduceGravity,

    /// <summary>Perform topology tunneling (last resort).</summary>
    TopologyTunneling
}

/// <summary>
/// Result of cluster dynamics adjustment.
/// </summary>
public readonly record struct ClusterAdjustmentResult(
    double NewDecoherence,
    bool Changed,
    ClusterStatus Status,
    bool StatusChanged,
    ClusterAction[] RecommendedActions,
    string Diagnostics
);
