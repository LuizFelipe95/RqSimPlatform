using RQSimulation;
using RQSimulation.GPUOptimized;
using RQSimulation.EventBasedModel;
using RqSimForms.Forms.Interfaces.AutoTuning;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RqSimForms.Forms.Interfaces;

public partial class RqSimEngineApi
{
    // ============================================================
    // AUTO-TUNING SYSTEM v2.0
    // ============================================================
    // Modernized auto-tuning system using specialized controllers:
    // - SpectralDimensionController: Hybrid d_S computation, target 4D
    // - GravityCouplingController: Adaptive G with PID-like control
    // - ClusterDynamicsController: Decoherence and cluster management
    // - VacuumEnergyManager: Prevent premature energy depletion
    // ============================================================

    // === Auto-Tuning State ===
    /// <summary>Whether auto-tuning is enabled (UI checkbox state).</summary>
    public volatile bool AutoTuningEnabled;

    /// <summary>Auto-tuning interval (steps between adjustments).</summary>
    public int AutoTuneInterval { get; set; } = 100;

    /// <summary>Request to trigger topology tunneling (checked by main loop).</summary>
    public volatile bool RequestTopologyTunneling;

    // === Controllers (lazy initialized) ===
    private AutoTuningConfig? _autoTuningConfig;
    private SpectralDimensionController? _spectralController;
    private GravityCouplingController? _gravityController;
    private ClusterDynamicsController? _clusterController;
    private VacuumEnergyManager? _vacuumManager;

    // === Internal state ===
    private readonly Random _autoTuneRng = new(42);
    private int _lastAutoTuneStep;
    private int _lastSpectralComputeStep;
    private double _cachedSpectralDimension;
    private double _cachedSpectralConfidence;

    // Legacy compatibility field
    private int _persistentGiantClusterCount;

    // ============================================================
    // PUBLIC PROPERTIES
    // ============================================================

    /// <summary>Gets or sets the auto-tuning configuration.</summary>
    public AutoTuningConfig AutoTuningConfig
    {
        get => _autoTuningConfig ??= AutoTuningConfig.CreateDefault();
        set => _autoTuningConfig = value ?? AutoTuningConfig.CreateDefault();
    }

    /// <summary>Gets the spectral dimension controller.</summary>
    public SpectralDimensionController SpectralController =>
        _spectralController ??= new SpectralDimensionController(AutoTuningConfig);

    /// <summary>Gets the gravity coupling controller.</summary>
    public GravityCouplingController GravityController =>
        _gravityController ??= new GravityCouplingController(AutoTuningConfig);

    /// <summary>Gets the cluster dynamics controller.</summary>
    public ClusterDynamicsController ClusterController =>
        _clusterController ??= new ClusterDynamicsController(AutoTuningConfig);

    /// <summary>Gets the vacuum energy manager.</summary>
    public VacuumEnergyManager VacuumManager =>
        _vacuumManager ??= new VacuumEnergyManager(AutoTuningConfig);

    /// <summary>Last computed hybrid spectral dimension.</summary>
    public double CachedSpectralDimension => _cachedSpectralDimension;

    /// <summary>Confidence in the spectral dimension estimate.</summary>
    public double SpectralConfidence => _cachedSpectralConfidence;

    // ============================================================
    // INITIALIZATION
    // ============================================================

    /// <summary>
    /// Initializes the auto-tuning system for a new simulation.
    /// Call this when starting a simulation.
    /// </summary>
    public void InitializeAutoTuning()
    {
        // Reset controllers
        SpectralController.Reset();
        GravityController.Reset();
        ClusterController.Reset();
        VacuumManager.Reset();

        // Initialize gravity controller with current G
        GravityController.Initialize(LiveConfig.GravitationalCoupling);
        ClusterController.Initialize(LiveConfig.DecoherenceRate);

        // Initialize vacuum manager if energy ledger is available
        if (EnergyLedger.VacuumPool > 0)
        {
            VacuumManager.Initialize(EnergyLedger.VacuumPool, EnergyLedger.TotalTrackedEnergy);
        }

        // Reset state
        _lastAutoTuneStep = 0;
        _lastSpectralComputeStep = 0;
        _cachedSpectralDimension = AutoTuningConfig.TargetSpectralDimension;
        _cachedSpectralConfidence = 0.0;
        _persistentGiantClusterCount = 0;
        RequestTopologyTunneling = false;
    }

    // ============================================================
    // MAIN AUTO-TUNING ENTRY POINT
    // ============================================================

    /// <summary>
    /// Performs comprehensive auto-tuning based on current simulation metrics.
    /// 
    /// RQ-HYPOTHESIS GOAL: Achieve spectral dimension d_S ? 4 (4D spacetime)
    /// 
    /// This method orchestrates all controllers:
    /// 1. VacuumEnergyManager: Prevent energy depletion
    /// 2. SpectralDimensionController: Compute/track d_S with confidence
    /// 3. GravityCouplingController: Adjust G based on d_S feedback
    /// 4. ClusterDynamicsController: Manage decoherence and clusters
    /// 
    /// Priority order:
    /// 1. ENERGY CRISIS - Prevent simulation death
    /// 2. FRAGMENTATION - d_S too low, graph breaking apart
    /// 3. GIANT CLUSTER - Over-correlation destroying structure
    /// 4. DIMENSION TUNING - Approach target d_S = 4
    /// 5. PARAMETER RESTORATION - Return to normal when healthy
    /// 6. EXPLORATION - Small perturbations for phase space exploration
    /// </summary>
    /// <returns>Description of adjustments made, or null if no tuning needed</returns>
    public string? PerformAutoTuning(int step, double spectralDim, int excitedCount, int clusterCount,
        int largestCluster, double heavyMass, int nodeCount)
    {
        if (!AutoTuningEnabled) return null;
        if (step - _lastAutoTuneStep < AutoTuneInterval) return null;
        if (step < AutoTuningConfig.WarmupSteps) return null;

        _lastAutoTuneStep = step;
        var adjustments = new List<string>();
        var graph = SimulationEngine?.Graph;

        if (graph == null || nodeCount == 0)
            return null;

        // ============================================================
        // STEP 1: VACUUM ENERGY MANAGEMENT
        // ============================================================
        if (AutoTuningConfig.EnableVacuumManager)
        {
            var vacuumResult = VacuumManager.Update(
                EnergyLedger.VacuumPool,
                graph,
                EnergyLedger);

            if (vacuumResult.StatusChanged || vacuumResult.Status == EnergyStatus.Critical)
            {
                adjustments.Add($"[Energy] {vacuumResult.Status}: {vacuumResult.VacuumFraction:P1}");

                // Apply energy-based parameter adjustments
                var energyAdjust = VacuumManager.GetParameterAdjustments();
                if (energyAdjust.EdgeTrialProbMultiplier < 1.0)
                {
                    double oldProb = LiveConfig.EdgeTrialProb;
                    LiveConfig.EdgeTrialProb *= energyAdjust.EdgeTrialProbMultiplier;
                    LiveConfig.EdgeTrialProb = Math.Max(AutoTuningConfig.MinEdgeTrialProb, LiveConfig.EdgeTrialProb);
                    adjustments.Add($"EdgeProb: {oldProb:F4}->{LiveConfig.EdgeTrialProb:F4} (energy)");
                }

                // Critical energy - take emergency action
                if (vacuumResult.Status == EnergyStatus.Critical)
                {
                    OnConsoleLog?.Invoke($"[AUTO-TUNE] ENERGY CRITICAL: {vacuumResult.Diagnostics}\n");
                    LiveConfig.MarkUpdated();
                    return string.Join("; ", adjustments);
                }
            }
        }

        // ============================================================
        // STEP 2: SPECTRAL DIMENSION COMPUTATION (periodic)
        // ============================================================
        if (AutoTuningConfig.EnableSpectralController &&
            (step - _lastSpectralComputeStep >= AutoTuningConfig.SpectralComputeInterval ||
             _cachedSpectralConfidence < 0.3))
        {
            _lastSpectralComputeStep = step;

            // Use hybrid spectral computation
            var spectralResult = SpectralController.ComputeHybridSpectralDimension(graph);
            _cachedSpectralDimension = SpectralController.CurrentDimension;
            _cachedSpectralConfidence = SpectralController.Confidence;

            adjustments.Add($"[d_S] {_cachedSpectralDimension:F2} (conf={_cachedSpectralConfidence:F2}, {spectralResult.Method})");

            // Use the fresh spectral value
            spectralDim = _cachedSpectralDimension;
        }
        else if (spectralDim <= 0 || double.IsNaN(spectralDim))
        {
            // Use cached value if provided value is invalid
            spectralDim = _cachedSpectralDimension;
        }

        // ============================================================
        // STEP 3: GRAVITY COUPLING ADJUSTMENT
        // ============================================================
        if (AutoTuningConfig.EnableGravityController && _cachedSpectralConfidence > 0.2)
        {
            AutoTuning.SpectralAction spectralAction = SpectralController.GetRecommendedAction();

            // Handle extreme hyperbolic regime with emergency actions
            if (spectralAction == AutoTuning.SpectralAction.EmergencyCompaction)
            {
                OnConsoleLog?.Invoke($"[AUTO-TUNE] EXTREME HYPERBOLIC: d_S={spectralDim:F2}, initiating emergency compaction\n");

                // Maximize gravity coupling
                double oldG = LiveConfig.GravitationalCoupling;
                LiveConfig.GravitationalCoupling = AutoTuningConfig.MaxGravitationalCoupling;
                adjustments.Add($"[G] {oldG:F4}->{LiveConfig.GravitationalCoupling:F4} (EMERGENCY COMPACTION)");

                // Reduce edge creation to prevent further expansion
                LiveConfig.EdgeTrialProb = AutoTuningConfig.MinEdgeTrialProb;
                adjustments.Add($"[EdgeProb] Minimized (prevent expansion)");

                // Increase decoherence to break up large structures
                LiveConfig.DecoherenceRate = Math.Min(
                    AutoTuningConfig.MaxDecoherenceRate,
                    LiveConfig.DecoherenceRate * 2.0);
                adjustments.Add($"[Decoherence] Boosted (break structures)");

                // Ensure energy is available for this process
                if (AutoTuningConfig.EnableVacuumManager && VacuumManager.Status != EnergyStatus.Healthy)
                {
                    // Force energy injection for hyperbolic correction
                    double injectionAmount = EnergyLedger.TotalTrackedEnergy * 0.15;
                    if (EnergyLedger.RecordExternalInjection(injectionAmount, "HyperbolicEmergency"))
                    {
                        adjustments.Add($"[Energy] Emergency injection {injectionAmount:F2}");
                    }
                }
            }
            else
            {
                var gravityResult = GravityController.ComputeAdjustment(
                    spectralDim,
                    _cachedSpectralConfidence,
                    spectralAction);

                if (gravityResult.Changed)
                {
                    double oldG = LiveConfig.GravitationalCoupling;
                    LiveConfig.GravitationalCoupling = gravityResult.NewG;
                    adjustments.Add($"[G] {oldG:F4}->{gravityResult.NewG:F4} ({gravityResult.Reason})");
                }
            }
        }

        // ============================================================
        // STEP 4: CLUSTER DYNAMICS AND DECOHERENCE
        // ============================================================
        if (AutoTuningConfig.EnableClusterController)
        {
            var clusterResult = ClusterController.AnalyzeAndAdjust(graph, largestCluster, clusterCount);

            if (clusterResult.Changed || clusterResult.StatusChanged)
            {
                double oldDec = LiveConfig.DecoherenceRate;
                LiveConfig.DecoherenceRate = clusterResult.NewDecoherence;

                if (Math.Abs(oldDec - clusterResult.NewDecoherence) > oldDec * 0.05)
                {
                    adjustments.Add($"[Decoherence] {oldDec:F4}->{clusterResult.NewDecoherence:F4} ({clusterResult.Status})");
                }

                // Apply cluster-based gravity adjustment
                double gMultiplier = ClusterController.GetGravityMultiplier();
                if (gMultiplier < 0.9)
                {
                    LiveConfig.GravitationalCoupling *= gMultiplier;
                    LiveConfig.GravitationalCoupling = Math.Max(
                        AutoTuningConfig.MinGravitationalCoupling,
                        LiveConfig.GravitationalCoupling);
                }

                // Apply edge trial probability adjustment
                double edgeMultiplier = ClusterController.GetEdgeTrialMultiplier();
                if (Math.Abs(edgeMultiplier - 1.0) > 0.1)
                {
                    LiveConfig.EdgeTrialProb *= edgeMultiplier;
                    LiveConfig.EdgeTrialProb = Math.Clamp(
                        LiveConfig.EdgeTrialProb,
                        AutoTuningConfig.MinEdgeTrialProb,
                        AutoTuningConfig.MaxEdgeTrialProb);
                }

                // Handle topology tunneling request
                if (ClusterController.TopologyTunnelingRequested)
                {
                    RequestTopologyTunneling = true;
                    adjustments.Add("[TOPOLOGY TUNNELING TRIGGERED]");
                    ClusterController.ClearTopologyTunnelingRequest();
                }
            }

            // Legacy compatibility
            _persistentGiantClusterCount = ClusterController.GiantClusterPersistence;
        }

        // ============================================================
        // STEP 5: ACTIVITY BALANCE (excited ratio)
        // ============================================================
        double excitedRatio = (double)excitedCount / Math.Max(1, nodeCount);

        if (excitedRatio > 0.6 && spectralDim >= AutoTuningConfig.WarningSpectralDimension)
        {
            // Hyperactive - increase decoherence
            double oldDec = LiveConfig.DecoherenceRate;
            LiveConfig.DecoherenceRate = Math.Min(
                AutoTuningConfig.MaxDecoherenceRate,
                LiveConfig.DecoherenceRate * 1.3);
            adjustments.Add($"Hyperactive {excitedRatio:P0}: Decoherence->{LiveConfig.DecoherenceRate:F4}");
        }
        else if (excitedRatio < 0.03 && step > AutoTuningConfig.WarmupSteps + 100)
        {
            // Frozen - reduce decoherence to allow activity
            double oldDec = LiveConfig.DecoherenceRate;
            LiveConfig.DecoherenceRate = Math.Max(
                AutoTuningConfig.MinDecoherenceRate,
                LiveConfig.DecoherenceRate * 0.7);
            adjustments.Add($"Frozen {excitedRatio:P0}: Decoherence->{LiveConfig.DecoherenceRate:F4}");
        }

        // ============================================================
        // STEP 6: CLUSTER FORMATION TUNING
        // ============================================================
        if (clusterCount < 3 && heavyMass < 50 && step > AutoTuningConfig.WarmupSteps &&
            spectralDim >= AutoTuningConfig.WarningSpectralDimension)
        {
            double oldThreshold = LiveConfig.AdaptiveThresholdSigma;
            LiveConfig.AdaptiveThresholdSigma = Math.Max(0.3, LiveConfig.AdaptiveThresholdSigma * 0.85);
            adjustments.Add($"Few clusters: Threshold {oldThreshold:F2}->{LiveConfig.AdaptiveThresholdSigma:F2}");
        }

        // ============================================================
        // STEP 7: EXPLORATION (when stable)
        // ============================================================
        if (AutoTuningConfig.EnableExploration &&
            adjustments.Count == 0 &&
            SpectralController.IsHealthy() &&
            ClusterController.Status == ClusterStatus.Healthy)
        {
            if (_autoTuneRng.NextDouble() < AutoTuningConfig.ExplorationProbability)
            {
                string exploration = PerformExploration();
                if (!string.IsNullOrEmpty(exploration))
                {
                    adjustments.Add(exploration);
                }
            }
        }

        // ============================================================
        // FINALIZE
        // ============================================================
        if (adjustments.Count == 0)
            return null;

        LiveConfig.MarkUpdated();
        return string.Join("; ", adjustments);
    }

    // ============================================================
    // HELPER METHODS
    // ============================================================

    /// <summary>
    /// Performs small random exploration of parameter space.
    /// Only called when system is stable and healthy.
    /// </summary>
    private string PerformExploration()
    {
        int param = _autoTuneRng.Next(3);
        double range = AutoTuningConfig.ExplorationRange;
        double factor = 1.0 - range + _autoTuneRng.NextDouble() * 2.0 * range;

        switch (param)
        {
            case 0:
                {
                    double oldG = LiveConfig.GravitationalCoupling;
                    LiveConfig.GravitationalCoupling = Math.Clamp(
                        LiveConfig.GravitationalCoupling * factor,
                        AutoTuningConfig.MinGravitationalCoupling,
                        AutoTuningConfig.MaxGravitationalCoupling);
                    return $"Explore: G {oldG:F4}->{LiveConfig.GravitationalCoupling:F4}";
                }
            case 1:
                {
                    double oldTemp = LiveConfig.HotStartTemperature;
                    LiveConfig.HotStartTemperature = Math.Clamp(
                        LiveConfig.HotStartTemperature * factor,
                        AutoTuningConfig.MinTemperature,
                        AutoTuningConfig.MaxTemperature);
                    return $"Explore: Temp {oldTemp:F2}->{LiveConfig.HotStartTemperature:F2}";
                }
            case 2:
                {
                    double oldDec = LiveConfig.DecoherenceRate;
                    LiveConfig.DecoherenceRate = Math.Clamp(
                        LiveConfig.DecoherenceRate * factor,
                        AutoTuningConfig.MinDecoherenceRate,
                        AutoTuningConfig.MaxDecoherenceRate);
                    return $"Explore: Decoherence {oldDec:F4}->{LiveConfig.DecoherenceRate:F4}";
                }
        }

        return "";
    }

    /// <summary>
    /// Computes annealing temperature for given step using dynamic ?.
    /// 
    /// RQ-FIX: Uses PhysicsConstants.ComputeAnnealingTimeConstant(totalSteps)
    /// instead of fixed ?=18779 which was too slow for typical simulations.
    /// 
    /// Formula: T(t) = T_f + (T_i - T_f) ? exp(-t/?)
    /// where ? = totalSteps / 5 ensures ~99% cooling by simulation end.
    /// </summary>
    public double ComputeAnnealingTemperature(int step, double startTemp, int totalSteps)
    {
        double finalTemp = PhysicsConstants.FinalAnnealingTemperature;

        // RQ-FIX: Use dynamic ? based on simulation length
        double annealingTau = PhysicsConstants.ComputeAnnealingTimeConstant(totalSteps);

        return finalTemp + (startTemp - finalTemp) * Math.Exp(-step / annealingTau);
    }

    /// <summary>
    /// Computes effective gravitational coupling with warmup.
    /// Uses GravityController's warmup-adjusted G if available.
    /// </summary>
    public double ComputeEffectiveG(int step, double targetG)
    {
        // If gravity controller is initialized, use its warmup logic
        if (_gravityController != null)
        {
            return _gravityController.GetWarmupAdjustedG(
                step,
                (int)LiveConfig.WarmupDuration,
                (int)LiveConfig.GravityTransitionDuration);
        }

        // Fallback to original logic
        double warmupG = PhysicsConstants.WarmupGravitationalCoupling;
        double warmupEnd = PhysicsConstants.WarmupDuration;
        double transitionDuration = PhysicsConstants.GravityTransitionDuration;

        if (step < warmupEnd)
            return warmupG;
        else if (step < warmupEnd + transitionDuration)
            return warmupG + (targetG - warmupG) * ((step - warmupEnd) / transitionDuration);
        else
            return targetG;
    }

    /// <summary>
    /// Gets a summary of the current auto-tuning state for diagnostics.
    /// </summary>
    public string GetAutoTuningDiagnostics()
    {
        if (!AutoTuningEnabled)
            return "Auto-tuning disabled";

        var parts = new List<string>
        {
            $"d_S={_cachedSpectralDimension:F2} (conf={_cachedSpectralConfidence:F2})",
            $"G={LiveConfig.GravitationalCoupling:F4}",
            $"Dec={LiveConfig.DecoherenceRate:F4}",
            $"Cluster: {ClusterController.Status}",
            $"Energy: {VacuumManager.Status}"
        };

        if (GravityController.InEmergencyMode)
            parts.Add("EMERGENCY_G");

        if (ClusterController.TopologyTunnelingRequested)
            parts.Add("TUNNELING_PENDING");

        return string.Join(" | ", parts);
    }
}
