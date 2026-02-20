using System.Numerics;
using RQSimulation;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.CPU;

/// <summary>
/// CPU module for internal observer implementation as a subsystem of the graph.
/// 
/// RQ-HYPOTHESIS: Relational Observables
/// =====================================
/// Instead of external readout of variables, the "observer" is
/// a designated region of the graph that becomes entangled with
/// the system being measured.
/// 
/// Measurement results are encoded in the observer's local phase,
/// not extracted to external variables.
/// 
/// KEY PRINCIPLES:
/// - No "God's eye view" - observer must be part of the system
/// - Measurement creates correlations (entanglement), not direct readout
/// - Observer accumulates information through phase shifts
/// - Results are relational: observer-relative, not absolute
/// 
/// Based on original InternalObserver implementation (CPU version).
/// </summary>
public sealed class InternalObserverCpuModule : CpuPluginBase
{
    private RQGraph? _graph;
    private HashSet<int> _observerNodes = [];
    private readonly List<ObservationRecord> _observations = [];
    private Random _rng = new();

    public override string Name => "Internal Observer (CPU)";
    public override string Description => "CPU-based relational measurement through graph subsystem entanglement";
    public override string Category => "Quantum";
    public override int Priority => 35;

    /// <summary>
    /// Coupling strength for measurement interaction.
    /// Controls how strongly observer becomes entangled with target.
    /// </summary>
    public double MeasurementCoupling { get; set; } = 0.1;

    /// <summary>
    /// Minimum correlation for measurement to register.
    /// </summary>
    public double MinMeasurementCorrelation { get; set; } = 0.01;

    /// <summary>
    /// All observation records accumulated by this observer.
    /// </summary>
    public IReadOnlyList<ObservationRecord> Observations => _observations;

    /// <summary>
    /// Nodes that comprise the observer subsystem.
    /// </summary>
    public IReadOnlyCollection<int> ObserverNodes => _observerNodes;

    /// <summary>
    /// Total number of measurements performed.
    /// </summary>
    public int MeasurementCount => _observations.Count;

    public override void Initialize(RQGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _observations.Clear();

        // Auto-configure observer with low-degree nodes
        ConfigureObserverAuto(Math.Max(1, graph.N / 10));
    }

    /// <summary>
    /// Configure observer with specific nodes.
    /// </summary>
    public void ConfigureObserver(IEnumerable<int> observerNodes, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(observerNodes);
        
        if (_graph is null)
            throw new InvalidOperationException("Module not initialized");

        _observerNodes = [];
        foreach (int node in observerNodes)
        {
            if (node < 0 || node >= _graph.N)
                throw new ArgumentOutOfRangeException(nameof(observerNodes),
                    $"Node index {node} is out of range [0, {_graph.N})");
            _observerNodes.Add(node);
        }

        if (_observerNodes.Count == 0)
            throw new ArgumentException("Observer must contain at least one node", nameof(observerNodes));

        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _observations.Clear();
    }

    /// <summary>
    /// Auto-configure observer by selecting low-degree nodes.
    /// </summary>
    public void ConfigureObserverAuto(int size, int? seed = null)
    {
        if (_graph is null)
            throw new InvalidOperationException("Module not initialized");

        // Select low-degree nodes as observer (minimal perturbation)
        var degrees = new List<(int node, int degree)>();
        for (int i = 0; i < _graph.N; i++)
        {
            int degree = _graph.Neighbors(i).Count();
            degrees.Add((i, degree));
        }

        degrees.Sort((a, b) => a.degree.CompareTo(b.degree));

        _observerNodes = [];
        for (int i = 0; i < Math.Min(size, degrees.Count); i++)
        {
            _observerNodes.Add(degrees[i].node);
        }

        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _observations.Clear();
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (_observerNodes.Count == 0) return;

        // Perform measurement sweep over all non-observer nodes
        MeasureSweep(null);
    }

    /// <summary>
    /// Perform weak measurement via entanglement.
    /// Observer subsystem correlates with target without full collapse.
    /// </summary>
    public bool MeasureObservableInternal(int targetNodeId)
    {
        if (_graph is null || targetNodeId < 0 || targetNodeId >= _graph.N)
            return false;

        // Skip if target is part of observer (self-measurement)
        if (_observerNodes.Contains(targetNodeId))
            return false;

        bool anyMeasurement = false;

        foreach (int observerNode in _observerNodes)
        {
            // Check if observer is connected to target
            if (!_graph.Edges[observerNode, targetNodeId])
                continue;

            // Stronger connection = stronger measurement
            double connectionWeight = _graph.Weights[observerNode, targetNodeId];
            if (connectionWeight < MinMeasurementCorrelation)
                continue;

            // Create entanglement via controlled phase shift
            double phaseShift = ApplyControlledPhase(targetNodeId, observerNode, connectionWeight);

            // Record observation (locally, not externally)
            _observations.Add(new ObservationRecord
            {
                ObserverNode = observerNode,
                TargetNode = targetNodeId,
                CorrelatedPhase = phaseShift,
                ConnectionWeight = connectionWeight,
                Timestamp = DateTime.UtcNow
            });

            anyMeasurement = true;
        }

        return anyMeasurement;
    }

    /// <summary>
    /// Apply controlled phase gate between observer and target.
    /// </summary>
    private double ApplyControlledPhase(int control, int target, double weight)
    {
        if (_graph is null) return 0.0;

        // Get control node phase (what we're measuring)
        double controlPhase = _graph.GetNodePhase(control);
        
        // NUMERICAL GUARD: Check for NaN/Inf
        if (double.IsNaN(controlPhase) || double.IsInfinity(controlPhase))
        {
            controlPhase = 0.0;
        }
        
        if (double.IsNaN(weight) || double.IsInfinity(weight))
        {
            weight = 0.0;
        }

        // Phase shift is proportional to coupling and control phase
        double phaseShift = MeasurementCoupling * weight * controlPhase;
        
        // NUMERICAL GUARD: Check result
        if (double.IsNaN(phaseShift) || double.IsInfinity(phaseShift))
        {
            phaseShift = 0.0;
        }
        
        // Clamp to reasonable range
        const double maxPhaseShift = 2.0 * Math.PI;
        if (phaseShift > maxPhaseShift) phaseShift = maxPhaseShift;
        if (phaseShift < -maxPhaseShift) phaseShift = -maxPhaseShift;

        // Apply phase shift to target (observer node)
        _graph.ShiftNodePhase(target, phaseShift);

        return phaseShift;
    }

    /// <summary>
    /// Perform measurement sweep over a set of target nodes.
    /// </summary>
    public int MeasureSweep(IEnumerable<int>? targetNodes = null)
    {
        if (_graph is null) return 0;

        int count = 0;

        if (targetNodes == null)
        {
            // Measure all non-observer nodes
            for (int i = 0; i < _graph.N; i++)
            {
                if (_observerNodes.Contains(i)) continue;
                if (MeasureObservableInternal(i)) count++;
            }
        }
        else
        {
            foreach (int node in targetNodes)
            {
                if (MeasureObservableInternal(node)) count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Extract measurement statistics from observer's internal state.
    /// </summary>
    public double GetObserverExpectationValue()
    {
        if (_graph is null) return 0.0;

        double sum = 0.0;
        int count = 0;

        foreach (int node in _observerNodes)
        {
            Complex psi = _graph.GetNodeWavefunction(node);
            
            // NUMERICAL GUARD: Skip NaN/Inf values
            if (double.IsNaN(psi.Real) || double.IsInfinity(psi.Real) ||
                double.IsNaN(psi.Imaginary) || double.IsInfinity(psi.Imaginary))
            {
                continue;
            }
            
            sum += psi.Magnitude;
            count++;
        }

        return count > 0 ? sum / count : 0.0;
    }

    /// <summary>
    /// Get total accumulated phase in observer subsystem.
    /// </summary>
    public double GetObserverTotalPhase()
    {
        if (_graph is null) return 0.0;

        double totalPhase = 0.0;

        foreach (int node in _observerNodes)
        {
            totalPhase += _graph.GetNodePhase(node);
        }

        return totalPhase;
    }

    /// <summary>
    /// Get correlation between observer and a target region.
    /// </summary>
    public double GetCorrelationWithRegion(IEnumerable<int> targetNodes)
    {
        ArgumentNullException.ThrowIfNull(targetNodes);
        if (_graph is null) return 0.0;

        // Compute mean phases for both regions
        double observerMean = 0.0;
        int observerCount = 0;
        foreach (int node in _observerNodes)
        {
            observerMean += _graph.GetNodePhase(node);
            observerCount++;
        }
        if (observerCount == 0) return 0.0;
        observerMean /= observerCount;

        double targetMean = 0.0;
        int targetCount = 0;
        foreach (int node in targetNodes)
        {
            if (node < 0 || node >= _graph.N) continue;
            targetMean += _graph.GetNodePhase(node);
            targetCount++;
        }
        if (targetCount == 0) return 0.0;
        targetMean /= targetCount;

        // Compute covariance and variances
        double covariance = 0.0;
        double observerVar = 0.0;
        double targetVar = 0.0;

        foreach (int obs in _observerNodes)
        {
            double obsPhase = _graph.GetNodePhase(obs) - observerMean;
            observerVar += obsPhase * obsPhase;

            foreach (int tgt in targetNodes)
            {
                if (tgt < 0 || tgt >= _graph.N) continue;
                if (!_graph.Edges[obs, tgt]) continue;

                double tgtPhase = _graph.GetNodePhase(tgt) - targetMean;
                covariance += obsPhase * tgtPhase;
            }
        }

        foreach (int tgt in targetNodes)
        {
            if (tgt < 0 || tgt >= _graph.N) continue;
            double tgtPhase = _graph.GetNodePhase(tgt) - targetMean;
            targetVar += tgtPhase * tgtPhase;
        }

        double denominator = Math.Sqrt(observerVar * targetVar);
        if (denominator < 1e-12) return 0.0;

        return covariance / denominator;
    }

    /// <summary>
    /// Compute mutual information between observer and target.
    /// I(O;T) = S(O) + S(T) - S(O,T)
    /// </summary>
    public double GetMutualInformation(IEnumerable<int> targetNodes)
    {
        ArgumentNullException.ThrowIfNull(targetNodes);
        if (_graph is null) return 0.0;

        double observerEntropy = ComputePhaseEntropy(_observerNodes);

        var targetSet = new HashSet<int>();
        foreach (int n in targetNodes)
        {
            if (n >= 0 && n < _graph.N)
                targetSet.Add(n);
        }
        if (targetSet.Count == 0) return 0.0;

        double targetEntropy = ComputePhaseEntropy(targetSet);

        // Joint entropy
        var jointSet = new HashSet<int>(_observerNodes);
        foreach (int n in targetSet) jointSet.Add(n);
        double jointEntropy = ComputePhaseEntropy(jointSet);

        double mutualInfo = observerEntropy + targetEntropy - jointEntropy;
        return Math.Max(0.0, mutualInfo);
    }

    private double ComputePhaseEntropy(IEnumerable<int> nodes)
    {
        if (_graph is null) return 0.0;

        const int numBins = 16;
        int[] bins = new int[numBins];
        int total = 0;

        foreach (int node in nodes)
        {
            double phase = _graph.GetNodePhase(node);
            
            // NUMERICAL GUARD: Skip NaN/Inf phases
            if (double.IsNaN(phase) || double.IsInfinity(phase))
            {
                continue;
            }
            
            double normalizedPhase = (phase % (2.0 * Math.PI) + 2.0 * Math.PI) % (2.0 * Math.PI);
            int bin = (int)(normalizedPhase / (2.0 * Math.PI) * numBins);
            if (bin >= numBins) bin = numBins - 1;
            if (bin < 0) bin = 0;
            bins[bin]++;
            total++;
        }

        if (total == 0) return 0.0;

        double entropy = 0.0;
        for (int i = 0; i < numBins; i++)
        {
            if (bins[i] == 0) continue;
            double p = (double)bins[i] / total;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    public override void Cleanup()
    {
        _observations.Clear();
        _observerNodes.Clear();
        _graph = null;
    }
}

/// <summary>
/// Record of a single observation event.
/// </summary>
public struct ObservationRecord
{
    public int ObserverNode;
    public int TargetNode;
    public double CorrelatedPhase;
    public double ConnectionWeight;
    public DateTime Timestamp;
}
