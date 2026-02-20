using System;
using System.Collections.Generic;
using RQSimulation;
using RQSimulation.Analysis.VacuumEnergy;

namespace RqSimForms.Forms.Interfaces.AutoTuning;

/// <summary>
/// Manages vacuum energy pool to prevent premature simulation termination.
/// 
/// RQ-HYPOTHESIS CONTEXT:
/// The vacuum energy pool represents the available energy for:
/// - Topology changes (edge creation/removal)
/// - Particle pair creation (Hawking-like processes)
/// - Field fluctuations
/// 
/// Problem: Simulation terminates when vacuum energy is depleted.
/// Solution: Active management through:
/// 1. Monitoring depletion rate
/// 2. Recycling energy from decaying structures
/// 3. Adjusting processes that consume energy
/// 4. Emergency injection (optional, violates strict conservation)
/// 
/// Note: Strict Wheeler-DeWitt mode (H_total |?? = 0) forbids external injection.
/// This manager can operate in both modes.
/// </summary>
public sealed class VacuumEnergyManager
{
    private readonly AutoTuningConfig _config;

    // Energy tracking
    private double _initialEnergy;
    private double _currentVacuumPool;
    private double _previousVacuumPool;
    private double _peakVacuumPool;

    // Depletion analysis
    private readonly Queue<double> _depletionHistory = new();
    private const int DepletionHistorySize = 20;
    private double _averageDepletionRate;
    private int _stepsUntilDepletion;

    // Energy flow tracking
    private double _totalEnergyConsumed;
    private double _totalEnergyRecycled;
    private double _totalEmergencyInjection;

    // Status
    private EnergyStatus _status = EnergyStatus.Healthy;
    private string _lastDiagnostics = "";

    /// <summary>Current vacuum energy pool.</summary>
    public double CurrentVacuumPool => _currentVacuumPool;

    /// <summary>Current vacuum pool as fraction of initial.</summary>
    public double VacuumFraction => _initialEnergy > 0 ? _currentVacuumPool / _initialEnergy : 0;

    /// <summary>Average energy depletion rate per step.</summary>
    public double DepletionRate => _averageDepletionRate;

    /// <summary>Estimated steps until vacuum depletion at current rate.</summary>
    public int StepsUntilDepletion => _stepsUntilDepletion;

    /// <summary>Current energy status.</summary>
    public EnergyStatus Status => _status;

    /// <summary>Diagnostic information.</summary>
    public string LastDiagnostics => _lastDiagnostics;

    /// <summary>Total energy consumed since initialization.</summary>
    public double TotalConsumed => _totalEnergyConsumed;

    /// <summary>Total energy recycled back to vacuum.</summary>
    public double TotalRecycled => _totalEnergyRecycled;

    /// <summary>Total emergency energy injected (if allowed).</summary>
    public double TotalEmergencyInjection => _totalEmergencyInjection;

    /// <summary>
    /// Creates a new vacuum energy manager.
    /// </summary>
    public VacuumEnergyManager(AutoTuningConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Initializes the manager with current energy state.
    /// </summary>
    public void Initialize(double initialVacuumPool, double totalEnergy)
    {
        _initialEnergy = totalEnergy;
        _currentVacuumPool = initialVacuumPool;
        _previousVacuumPool = initialVacuumPool;
        _peakVacuumPool = initialVacuumPool;

        _depletionHistory.Clear();
        _averageDepletionRate = 0;
        _stepsUntilDepletion = int.MaxValue;

        _totalEnergyConsumed = 0;
        _totalEnergyRecycled = 0;
        _totalEmergencyInjection = 0;

        _status = EnergyStatus.Healthy;
        _lastDiagnostics = "Initialized";
    }

    /// <summary>
    /// Resets the manager state. Call when starting a new simulation.
    /// </summary>
    public void Reset()
    {
        _initialEnergy = 0;
        _currentVacuumPool = 0;
        _previousVacuumPool = 0;
        _peakVacuumPool = 0;

        _depletionHistory.Clear();
        _averageDepletionRate = 0;
        _stepsUntilDepletion = int.MaxValue;

        _totalEnergyConsumed = 0;
        _totalEnergyRecycled = 0;
        _totalEmergencyInjection = 0;

        _status = EnergyStatus.Healthy;
        _lastDiagnostics = "";
    }

    /// <summary>
    /// Updates the manager with current vacuum pool state.
    /// Call this every tuning interval.
    /// </summary>
    /// <param name="currentVacuumPool">Current vacuum energy from EnergyLedger</param>
    /// <param name="graph">The RQ graph (for recycling opportunities)</param>
    /// <param name="ledger">The energy ledger (for injection if needed)</param>
    /// <returns>Management result with recommended actions</returns>
    public VacuumManagementResult Update(double currentVacuumPool, RQGraph graph, EnergyLedger? ledger)
    {
        _previousVacuumPool = _currentVacuumPool;
        _currentVacuumPool = currentVacuumPool;

        if (currentVacuumPool > _peakVacuumPool)
            _peakVacuumPool = currentVacuumPool;

        // Calculate depletion
        double depletion = _previousVacuumPool - currentVacuumPool;
        if (depletion > 0)
            _totalEnergyConsumed += depletion;

        // Track depletion history
        _depletionHistory.Enqueue(depletion);
        while (_depletionHistory.Count > DepletionHistorySize)
            _depletionHistory.Dequeue();

        // Calculate average depletion rate
        double sum = 0;
        foreach (double d in _depletionHistory)
            sum += d;
        _averageDepletionRate = _depletionHistory.Count > 0 ? sum / _depletionHistory.Count : 0;

        // Estimate steps until depletion
        if (_averageDepletionRate > 1e-10)
        {
            _stepsUntilDepletion = (int)(currentVacuumPool / _averageDepletionRate);
        }
        else
        {
            _stepsUntilDepletion = int.MaxValue;
        }

        // Determine status
        double fraction = VacuumFraction;
        EnergyStatus previousStatus = _status;

        if (fraction <= _config.CriticalVacuumFraction)
            _status = EnergyStatus.Critical;
        else if (fraction <= _config.WarningVacuumFraction)
            _status = EnergyStatus.Warning;
        else if (fraction >= _config.TargetVacuumFraction)
            _status = EnergyStatus.Healthy;
        else
            _status = EnergyStatus.Low;

        var actions = new List<EnergyAction>();
        var diagnostics = new List<string>
        {
            $"Vacuum: {currentVacuumPool:F2}/{_initialEnergy:F2} ({fraction:P1})",
            $"Rate: {_averageDepletionRate:F4}/step"
        };

        // Take action based on status
        if (_status == EnergyStatus.Critical && _config.EnableVacuumEnergyManagement)
        {
            diagnostics.Add("CRITICAL: Vacuum nearly depleted");

            // Emergency injection if allowed
            if (_config.AllowEmergencyEnergyInjection && ledger != null)
            {
                double injectionAmount = _initialEnergy * _config.EmergencyInjectionFraction;
                if (ledger.RecordExternalInjection(injectionAmount, "VacuumEmergency"))
                {
                    _totalEmergencyInjection += injectionAmount;
                    _currentVacuumPool += injectionAmount;
                    actions.Add(EnergyAction.EmergencyInjection);
                    diagnostics.Add($"Emergency injection: {injectionAmount:F2}");
                }
            }

            // Always recommend reducing energy-consuming processes
            actions.Add(EnergyAction.ReduceTopologyChanges);
            actions.Add(EnergyAction.ReduceEdgeCreation);
        }
        else if (_status == EnergyStatus.Warning && _config.EnableVacuumEnergyManagement)
        {
            diagnostics.Add("WARNING: Vacuum energy low");
            
            // Proactive injection before reaching critical
            if (_config.EnableProactiveEnergyInjection && 
                fraction <= _config.ProactiveInjectionThreshold && 
                ledger != null)
            {
                double injectionAmount = _initialEnergy * _config.ProactiveInjectionFraction;
                if (ledger.RecordExternalInjection(injectionAmount, "ProactiveInjection"))
                {
                    _totalEmergencyInjection += injectionAmount;
                    _currentVacuumPool += injectionAmount;
                    actions.Add(EnergyAction.ProactiveInjection);
                    diagnostics.Add($"Proactive injection: {injectionAmount:F2}");
                }
            }
            else
            {
                actions.Add(EnergyAction.ReduceEdgeCreation);
            }

            // Try to recycle energy from decaying structures
            if (graph != null && _config.EnergyRecyclingRate > 0)
            {
                double recycled = RecycleEnergyFromGraph(graph);
                if (recycled > 0)
                {
                    _totalEnergyRecycled += recycled;
                    if (ledger != null)
                    {
                        ledger.RegisterRadiation(recycled);
                    }
                    _currentVacuumPool += recycled;
                    actions.Add(EnergyAction.Recycling);
                    diagnostics.Add($"Recycled: {recycled:F4}");
                }
            }
        }
        else if (_status == EnergyStatus.Low)
        {
            diagnostics.Add($"Low vacuum, ~{_stepsUntilDepletion} steps remaining");

            // Proactive recycling
            if (graph != null && _config.EnergyRecyclingRate > 0)
            {
                double recycled = RecycleEnergyFromGraph(graph) * 0.5; // Less aggressive
                if (recycled > 0)
                {
                    _totalEnergyRecycled += recycled;
                    if (ledger != null)
                    {
                        ledger.RegisterRadiation(recycled);
                    }
                    _currentVacuumPool += recycled;
                    actions.Add(EnergyAction.Recycling);
                    diagnostics.Add($"Proactive recycling: {recycled:F4}");
                }
            }
        }

        _lastDiagnostics = string.Join("; ", diagnostics);

        return new VacuumManagementResult(
            Status: _status,
            StatusChanged: _status != previousStatus,
            VacuumFraction: fraction,
            StepsUntilDepletion: _stepsUntilDepletion,
            RecommendedActions: actions.ToArray(),
            Diagnostics: _lastDiagnostics
        );
    }

    /// <summary>
    /// Gets recommended parameter adjustments based on energy status.
    /// </summary>
    public VacuumParameterAdjustments GetParameterAdjustments()
    {
        return _status switch
        {
            EnergyStatus.Critical => new VacuumParameterAdjustments(
                EdgeTrialProbMultiplier: 0.1,      // Drastically reduce edge creation
                TopologyChangeMultiplier: 0.1,     // Drastically reduce topology changes
                DecoherenceMultiplier: 0.5,        // Reduce decoherence (also costs energy)
                GravityMultiplier: 0.8             // Slightly reduce gravity
            ),
            EnergyStatus.Warning => new VacuumParameterAdjustments(
                EdgeTrialProbMultiplier: 0.5,
                TopologyChangeMultiplier: 0.5,
                DecoherenceMultiplier: 0.8,
                GravityMultiplier: 0.9
            ),
            EnergyStatus.Low => new VacuumParameterAdjustments(
                EdgeTrialProbMultiplier: 0.8,
                TopologyChangeMultiplier: 0.8,
                DecoherenceMultiplier: 0.9,
                GravityMultiplier: 1.0
            ),
            _ => new VacuumParameterAdjustments(1.0, 1.0, 1.0, 1.0)
        };
    }

    /// <summary>
    /// Predicts vacuum state at future step.
    /// </summary>
    public (double predictedVacuum, EnergyStatus predictedStatus) Predict(int stepsAhead)
    {
        double predicted = _currentVacuumPool - (_averageDepletionRate * stepsAhead);
        predicted = Math.Max(0, predicted);

        double fraction = _initialEnergy > 0 ? predicted / _initialEnergy : 0;

        EnergyStatus status;
        if (fraction <= _config.CriticalVacuumFraction)
            status = EnergyStatus.Critical;
        else if (fraction <= _config.WarningVacuumFraction)
            status = EnergyStatus.Warning;
        else if (fraction >= _config.TargetVacuumFraction)
            status = EnergyStatus.Healthy;
        else
            status = EnergyStatus.Low;

        return (predicted, status);
    }

    // ============================================================
    // PRIVATE METHODS
    // ============================================================

    /// <summary>
    /// Attempts to recycle energy from decaying/weak structures in the graph.
    /// </summary>
    private double RecycleEnergyFromGraph(RQGraph graph)
    {
        // Find weak edges that can be "harvested" for energy
        double recycledEnergy = 0;
        double harvestThreshold = 0.1; // Edges below this weight are candidates

        for (int i = 0; i < graph.N; i++)
        {
            foreach (int j in graph.Neighbors(i))
            {
                if (j <= i) continue; // Process each edge once

                double weight = graph.Weights[i, j];
                if (weight > 0 && weight < harvestThreshold)
                {
                    // Energy proportional to weight, scaled by recycling rate
                    double potentialEnergy = weight * PhysicsConstants.EdgeCreationCost * _config.EnergyRecyclingRate;

                    // Only harvest a fraction of very weak edges
                    if (weight < harvestThreshold * 0.5)
                    {
                        recycledEnergy += potentialEnergy * 0.3; // Partial harvest
                    }
                }
            }
        }

        return Math.Min(recycledEnergy, _initialEnergy * 0.01); // Cap at 1% per cycle
    }

    /// <summary>
    /// Calculates average Ollivier-Ricci curvature across all edges.
    /// Delegates to <see cref="VacuumEnergyAnalyzer.CalculateAverageRicciCurvature"/> in core.
    /// </summary>
    public static double CalculateAverageRicciCurvature(RQGraph graph, double sampleFraction = 1.0)
        => VacuumEnergyAnalyzer.CalculateAverageRicciCurvature(graph, sampleFraction);

    /// <summary>
    /// Calculates Ricci curvature statistics across all edges.
    /// Delegates to <see cref="VacuumEnergyAnalyzer.CalculateRicciCurvatureStats"/> in core.
    /// </summary>
    public static (double Mean, double Variance, double Min, double Max) CalculateRicciCurvatureStats(RQGraph graph)
        => VacuumEnergyAnalyzer.CalculateRicciCurvatureStats(graph);

    /// <summary>
    /// Checks if the graph curvature is within tolerance of flat space.
    /// Delegates to <see cref="VacuumEnergyAnalyzer.IsRicciFlat"/> in core.
    /// </summary>
    public static bool IsRicciFlat(RQGraph graph, double targetCurvature = 0.0, double tolerance = 0.1)
        => VacuumEnergyAnalyzer.IsRicciFlat(graph, targetCurvature, tolerance);
}

/// <summary>
/// Energy status levels.
/// </summary>
public enum EnergyStatus
{
    /// <summary>Vacuum energy is at healthy levels.</summary>
    Healthy,

    /// <summary>Vacuum energy is below target but above warning.</summary>
    Low,

    /// <summary>Vacuum energy is low - take preventive action.</summary>
    Warning,

    /// <summary>Vacuum energy is critically low - emergency measures needed.</summary>
    Critical
}

/// <summary>
/// Recommended actions for energy management.
/// </summary>
public enum EnergyAction
{
    /// <summary>No action needed.</summary>
    None,

    /// <summary>Reduce edge creation probability.</summary>
    ReduceEdgeCreation,

    /// <summary>Reduce topology change rate.</summary>
    ReduceTopologyChanges,

    /// <summary>Recycle energy from decaying structures.</summary>
    Recycling,

    /// <summary>Emergency energy injection (violates strict conservation).</summary>
    EmergencyInjection,

    /// <summary>Proactive energy injection to prevent crisis.</summary>
    ProactiveInjection
}

/// <summary>
/// Result of vacuum management update.
/// </summary>
public readonly record struct VacuumManagementResult(
    EnergyStatus Status,
    bool StatusChanged,
    double VacuumFraction,
    int StepsUntilDepletion,
    EnergyAction[] RecommendedActions,
    string Diagnostics
);

/// <summary>
/// Recommended parameter adjustments based on vacuum energy status.
/// </summary>
public readonly record struct VacuumParameterAdjustments(
    double EdgeTrialProbMultiplier,
    double TopologyChangeMultiplier,
    double DecoherenceMultiplier,
    double GravityMultiplier
);
