using System;
using System.Collections.Generic;
using RQSimulation;

namespace RqSimForms.Forms.Interfaces.AutoTuning;

/// <summary>
/// Controls gravitational coupling (G) to achieve target spectral dimension.
/// 
/// RQ-HYPOTHESIS PHYSICS:
/// Gravitational coupling G controls the strength of geometry evolution.
/// 
/// Key dynamics:
/// - High G ? Strong curvature flow ? Graph collapses ? d_S ? ?
/// - Low G ? Weak curvature flow ? Graph fragments ? d_S ? 0
/// - Optimal G ? 4D emergence ? d_S ? 4
/// 
/// This controller uses feedback from spectral dimension to adjust G:
/// - If d_S &lt; target: Reduce G (prevent fragmentation)
/// - If d_S &gt; target: Increase G (prevent hyperbolic growth)
/// - Hysteresis prevents oscillation
/// </summary>
public sealed class GravityCouplingController
{
    private readonly AutoTuningConfig _config;

    // State tracking
    private double _currentG;
    private double _targetG;
    private double _previousDimension;
    private double _previousG;

    // PID-like control
    private double _integralError;
    private double _previousError;
    private const double Kp = 0.3;  // Proportional gain
    private const double Ki = 0.05; // Integral gain
    private const double Kd = 0.1;  // Derivative gain

    // Hysteresis and smoothing
    private readonly Queue<double> _gHistory = new();
    private const int HistorySize = 10;
    private bool _inEmergencyMode;
    private int _stableStepsCount;
    private const int StableThreshold = 5;

    // Diagnostics
    private string _lastDiagnostics = "";
    private GravityAdjustmentReason _lastReason = GravityAdjustmentReason.None;

    /// <summary>Current gravitational coupling value.</summary>
    public double CurrentG => _currentG;

    /// <summary>Target gravitational coupling (before adaptation).</summary>
    public double TargetG => _targetG;

    /// <summary>Last adjustment reason.</summary>
    public GravityAdjustmentReason LastReason => _lastReason;

    /// <summary>Diagnostic information.</summary>
    public string LastDiagnostics => _lastDiagnostics;

    /// <summary>Whether controller is in emergency mode.</summary>
    public bool InEmergencyMode => _inEmergencyMode;

    /// <summary>Number of consecutive stable steps.</summary>
    public int StableSteps => _stableStepsCount;

    /// <summary>
    /// Creates a new gravity coupling controller.
    /// </summary>
    public GravityCouplingController(AutoTuningConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _currentG = config.BaseGravitationalCoupling;
        _targetG = config.BaseGravitationalCoupling;
    }

    /// <summary>
    /// Initializes the controller with starting G value.
    /// </summary>
    public void Initialize(double startingG)
    {
        _currentG = startingG;
        _targetG = startingG;
        _previousG = startingG;
        _integralError = 0;
        _previousError = 0;
        _inEmergencyMode = false;
        _stableStepsCount = 0;
        _gHistory.Clear();
    }

    /// <summary>
    /// Computes the adjusted gravitational coupling based on spectral dimension.
    /// </summary>
    /// <param name="currentDimension">Current spectral dimension</param>
    /// <param name="confidence">Confidence in the spectral measurement [0, 1]</param>
    /// <param name="spectralAction">Recommended action from spectral controller</param>
    /// <returns>Adjustment result with new G value</returns>
    public GravityAdjustmentResult ComputeAdjustment(
        double currentDimension,
        double confidence,
        SpectralAction spectralAction)
    {
        var diagnostics = new List<string>();

        // Skip adjustment if confidence is too low
        if (confidence < 0.3)
        {
            diagnostics.Add($"Low confidence ({confidence:F2}), skipping adjustment");
            _lastDiagnostics = string.Join("; ", diagnostics);
            return new GravityAdjustmentResult(
                NewG: _currentG,
                Changed: false,
                Reason: GravityAdjustmentReason.None,
                Diagnostics: _lastDiagnostics
            );
        }

        _previousG = _currentG;
        double newG = _currentG;
        GravityAdjustmentReason reason = GravityAdjustmentReason.None;

        // Calculate error from target
        double error = currentDimension - _config.TargetSpectralDimension;
        double absError = Math.Abs(error);

        diagnostics.Add($"d_S={currentDimension:F2}, target={_config.TargetSpectralDimension:F1}, error={error:F2}");

        // Handle emergency cases first
        if (spectralAction == SpectralAction.EmergencyRecovery)
        {
            // Critical fragmentation - aggressive G reduction
            newG = _config.MinGravitationalCoupling;
            reason = GravityAdjustmentReason.EmergencyFragmentation;
            _inEmergencyMode = true;
            _stableStepsCount = 0;
            diagnostics.Add("EMERGENCY: Fragmentation detected, G ? minimum");
        }
        else if (currentDimension <= _config.CriticalSpectralDimension)
        {
            // Near-critical fragmentation
            newG = _currentG * _config.GravitySuppressionFactor;
            newG = Math.Max(newG, _config.MinGravitationalCoupling);
            reason = GravityAdjustmentReason.FragmentationPrevention;
            _inEmergencyMode = true;
            _stableStepsCount = 0;
            diagnostics.Add($"Critical d_S={currentDimension:F2}, suppressing G");
        }
        else if (currentDimension >= _config.HighSpectralDimension)
        {
            // Hyperbolic regime - increase G to compact
            // Scale boost factor based on how far above the target we are
            double excessDimension = currentDimension - _config.TargetSpectralDimension;
            double aggressiveFactor = 1.0 + (excessDimension / 4.0); // Scales up with deviation
            double effectiveBoost = _config.GravityBoostFactor * Math.Min(aggressiveFactor, 3.0);
            
            // For extreme hyperbolic (d_S > 8), use maximum G immediately
            if (currentDimension >= 8.0)
            {
                newG = _config.MaxGravitationalCoupling;
                reason = GravityAdjustmentReason.ExtremeHyperbolic;
                _inEmergencyMode = true;
                diagnostics.Add($"EXTREME hyperbolic d_S={currentDimension:F2}, G ? maximum");
            }
            else
            {
                newG = _currentG * effectiveBoost;
                newG = Math.Min(newG, _config.MaxGravitationalCoupling);
                reason = GravityAdjustmentReason.HyperbolicCorrection;
                diagnostics.Add($"Hyperbolic d_S={currentDimension:F2}, boosting G (factor={effectiveBoost:F2})");
            }
            _stableStepsCount = 0;
        }
        else
        {
            // Normal operation - use PID-like control
            _inEmergencyMode = false;

            // Only adjust if error exceeds tolerance
            if (absError > _config.SpectralDimensionTolerance)
            {
                // PID terms
                _integralError += error;
                _integralError = Math.Clamp(_integralError, -5.0, 5.0); // Anti-windup
                double derivativeError = error - _previousError;

                // Control signal (negative because higher d_S needs lower G)
                double control = -Kp * error - Ki * _integralError - Kd * derivativeError;

                // Apply control with configured adjustment rate
                double adjustmentFactor = 1.0 + control * (1.0 - _config.GravityAdjustmentRate);
                adjustmentFactor = Math.Clamp(adjustmentFactor, 0.5, 2.0);

                newG = _currentG * adjustmentFactor;

                reason = error > 0 ? GravityAdjustmentReason.DimensionTooHigh
                                   : GravityAdjustmentReason.DimensionTooLow;

                diagnostics.Add($"PID: P={-Kp * error:F3}, I={-Ki * _integralError:F3}, D={-Kd * derivativeError:F3}");
                diagnostics.Add($"Control factor: {adjustmentFactor:F3}");

                _stableStepsCount = 0;
            }
            else
            {
                // Within tolerance - gradual restoration
                _stableStepsCount++;

                if (_stableStepsCount > StableThreshold)
                {
                    // Slowly restore toward target G
                    double restorationRate = 0.02;
                    newG = _currentG + restorationRate * (_targetG - _currentG);
                    reason = GravityAdjustmentReason.Restoration;
                    diagnostics.Add($"Stable for {_stableStepsCount} steps, restoring toward target");
                }
                else
                {
                    reason = GravityAdjustmentReason.None;
                    diagnostics.Add("Within tolerance, maintaining");
                }
            }

            _previousError = error;
        }

        // Clamp to valid range
        newG = Math.Clamp(newG, _config.MinGravitationalCoupling, _config.MaxGravitationalCoupling);

        // Apply smoothing via history
        _gHistory.Enqueue(newG);
        while (_gHistory.Count > HistorySize)
            _gHistory.Dequeue();

        // Smooth only during normal operation (not emergency)
        if (!_inEmergencyMode && _gHistory.Count >= 3)
        {
            double sum = 0;
            foreach (double g in _gHistory)
                sum += g;
            newG = sum / _gHistory.Count;
        }

        // Determine if changed significantly
        bool changed = Math.Abs(newG - _previousG) > _previousG * 0.01;

        _currentG = newG;
        _previousDimension = currentDimension;
        _lastReason = reason;
        _lastDiagnostics = string.Join("; ", diagnostics);

        return new GravityAdjustmentResult(
            NewG: newG,
            Changed: changed,
            Reason: reason,
            Diagnostics: _lastDiagnostics
        );
    }

    /// <summary>
    /// Gets the warmup-adjusted G for a given step.
    /// During warmup phase, G is suppressed to allow initial structure formation.
    /// </summary>
    public double GetWarmupAdjustedG(int step, int warmupDuration, int transitionDuration)
    {
        double warmupG = PhysicsConstants.WarmupGravitationalCoupling;

        if (step < warmupDuration)
        {
            return warmupG;
        }
        else if (step < warmupDuration + transitionDuration)
        {
            // Linear interpolation
            double t = (double)(step - warmupDuration) / transitionDuration;
            return warmupG + t * (_currentG - warmupG);
        }
        else
        {
            return _currentG;
        }
    }

    /// <summary>
    /// Updates the target G value. Call when user changes base coupling.
    /// </summary>
    public void UpdateTargetG(double newTarget)
    {
        _targetG = Math.Clamp(newTarget, _config.MinGravitationalCoupling, _config.MaxGravitationalCoupling);
    }

    /// <summary>
    /// Resets the controller state.
    /// </summary>
    public void Reset()
    {
        _currentG = _config.BaseGravitationalCoupling;
        _targetG = _config.BaseGravitationalCoupling;
        _previousG = _currentG;
        _previousDimension = _config.TargetSpectralDimension;
        _integralError = 0;
        _previousError = 0;
        _inEmergencyMode = false;
        _stableStepsCount = 0;
        _gHistory.Clear();
    }
}

/// <summary>
/// Reason for gravitational coupling adjustment.
/// </summary>
public enum GravityAdjustmentReason
{
    /// <summary>No adjustment made.</summary>
    None,

    /// <summary>d_S too low - reducing G to prevent fragmentation.</summary>
    DimensionTooLow,

    /// <summary>d_S too high - increasing G to prevent hyperbolic growth.</summary>
    DimensionTooHigh,

    /// <summary>Emergency fragmentation prevention.</summary>
    FragmentationPrevention,

    /// <summary>Emergency fragmentation - critical mode.</summary>
    EmergencyFragmentation,

    /// <summary>Correcting hyperbolic (over-connected) regime.</summary>
    HyperbolicCorrection,

    /// <summary>Extreme hyperbolic regime (d_S >= 8) - emergency correction.</summary>
    ExtremeHyperbolic,

    /// <summary>Restoring toward target during stable period.</summary>
    Restoration
}

/// <summary>
/// Result of gravity coupling adjustment.
/// </summary>
public readonly record struct GravityAdjustmentResult(
    double NewG,
    bool Changed,
    GravityAdjustmentReason Reason,
    string Diagnostics
);
