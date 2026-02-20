using System;
using System.Collections.Generic;
using RQSimulation;

namespace RqSimForms.Forms.Interfaces.AutoTuning;

/// <summary>
/// Result of a spectral dimension computation with confidence metrics.
/// </summary>
public readonly record struct SpectralResult(
    double Dimension,
    double Confidence,
    string Method,
    double Slope,
    int DataPoints
);

/// <summary>
/// Controller for spectral dimension management in RQ simulations.
/// 
/// RQ-HYPOTHESIS:
/// Spectral dimension d_S characterizes the effective dimensionality
/// of the emergent spacetime via random walk return probability:
///   P(t) ~ t^(-d_S/2)
/// 
/// Target: d_S ? 4 (4D spacetime in IR regime)
/// UV behavior: d_S ? 2 (Planck scale dimensional reduction)
/// 
/// This controller uses the graph's built-in ComputeSpectralDimension
/// method which automatically selects the best algorithm (HeatKernel,
/// RandomWalk, or Laplacian) based on graph characteristics.
/// 
/// The controller tracks history for stable estimation even when
/// individual measurements are noisy.
/// </summary>
public sealed class SpectralDimensionController
{
    private readonly AutoTuningConfig _config;
    private readonly Queue<SpectralResult> _history = new();
    private const int MaxHistorySize = 20;

    // Smoothed estimates
    private double _smoothedDimension;
    private double _smoothedConfidence;
    private int _computationCount;

    // Last computation details
    private SpectralResult _lastResult;
    private string _lastDiagnostics = "";

    /// <summary>Current smoothed spectral dimension estimate.</summary>
    public double CurrentDimension => _smoothedDimension;

    /// <summary>Current confidence in the estimate [0, 1].</summary>
    public double Confidence => _smoothedConfidence;

    /// <summary>Last individual computation result.</summary>
    public SpectralResult LastResult => _lastResult;

    /// <summary>Diagnostic information from last computation.</summary>
    public string LastDiagnostics => _lastDiagnostics;

    /// <summary>Number of computations performed.</summary>
    public int ComputationCount => _computationCount;

    /// <summary>
    /// Creates a new spectral dimension controller.
    /// </summary>
    public SpectralDimensionController(AutoTuningConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _smoothedDimension = config.TargetSpectralDimension;
        _smoothedConfidence = 0.0;
    }

    /// <summary>
    /// Computes spectral dimension using the graph's built-in hybrid method.
    /// 
    /// The RQGraph.ComputeSpectralDimension method automatically selects
    /// the best algorithm based on graph characteristics:
    /// - Dense graphs: Laplacian eigenvalue method
    /// - Sparse graphs: Random walk method  
    /// - Moderate graphs: Heat kernel method
    /// </summary>
    /// <param name="graph">The RQ graph to analyze</param>
    /// <returns>Spectral dimension result with confidence</returns>
    public SpectralResult ComputeHybridSpectralDimension(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.N < 10)
        {
            var fallback = new SpectralResult(2.0, 0.1, "fallback_small", 0, 0);
            UpdateSmoothed(fallback);
            return fallback;
        }

        var diagnostics = new List<string> { $"N={graph.N}" };

        // Use the graph's built-in hybrid computation
        double d_s = graph.ComputeSpectralDimension(
            t_max: _config.SpectralMaxSteps,
            num_walkers: _config.SpectralWalkerCount);

        // Get method used and slope from graph
        string method = graph.LastSpectralMethod;
        double slope = graph.LastSpectralSlope;

        // Estimate confidence based on result quality
        double confidence = EstimateConfidence(d_s, slope, graph.N);

        diagnostics.Add($"d_S={d_s:F2} ({method}), slope={slope:F3}, conf={confidence:F2}");

        var result = new SpectralResult(d_s, confidence, method, slope, _config.SpectralMaxSteps);

        // Update smoothed estimate
        UpdateSmoothed(result);

        // Store diagnostics
        _lastDiagnostics = string.Join("; ", diagnostics);
        _lastResult = result;
        _computationCount++;

        return result;
    }

    /// <summary>
    /// Gets the deviation from target spectral dimension.
    /// Positive = too high (hyperbolic), Negative = too low (fragmenting).
    /// </summary>
    public double GetDeviationFromTarget()
    {
        return _smoothedDimension - _config.TargetSpectralDimension;
    }

    /// <summary>
    /// Determines if spectral dimension is in healthy range.
    /// </summary>
    public bool IsHealthy()
    {
        double dev = Math.Abs(GetDeviationFromTarget());
        return dev <= _config.SpectralDimensionTolerance &&
               _smoothedConfidence >= _config.SpectralConfidenceThreshold;
    }

    /// <summary>
    /// Determines if spectral dimension indicates fragmentation risk.
    /// </summary>
    public bool IsFragmenting()
    {
        return _smoothedDimension <= _config.CriticalSpectralDimension;
    }

    /// <summary>
    /// Determines if spectral dimension indicates hyperbolic regime.
    /// </summary>
    public bool IsHyperbolic()
    {
        return _smoothedDimension >= _config.HighSpectralDimension;
    }

    /// <summary>
    /// Gets recommended action based on current spectral state.
    /// </summary>
    public SpectralAction GetRecommendedAction()
    {
        if (_smoothedDimension <= _config.CriticalSpectralDimension)
            return SpectralAction.EmergencyRecovery;

        if (_smoothedDimension <= _config.WarningSpectralDimension)
            return SpectralAction.ReduceGravity;

        // Extreme hyperbolic regime - emergency action needed
        if (_smoothedDimension >= 8.0)
            return SpectralAction.EmergencyCompaction;

        if (_smoothedDimension >= _config.HighSpectralDimension)
            return SpectralAction.IncreaseGravity;

        if (Math.Abs(GetDeviationFromTarget()) <= _config.SpectralDimensionTolerance)
            return SpectralAction.Maintain;

        if (_smoothedDimension < _config.TargetSpectralDimension)
            return SpectralAction.SlightlyReduceGravity;

        return SpectralAction.SlightlyIncreaseGravity;
    }

    /// <summary>
    /// Resets the controller state.
    /// </summary>
    public void Reset()
    {
        _history.Clear();
        _smoothedDimension = _config.TargetSpectralDimension;
        _smoothedConfidence = 0.0;
        _computationCount = 0;
    }

    // ============================================================
    // PRIVATE METHODS
    // ============================================================

    private static double EstimateConfidence(double dimension, double slope, int nodeCount)
    {
        // Confidence based on:
        // 1. Whether dimension is in reasonable range [1, 8]
        // 2. Whether slope was well-determined (not near zero)
        // 3. Proximity to expected physical values (2, 3, 4)
        // 4. Graph size (larger = more reliable)

        if (dimension <= 0 || double.IsNaN(dimension) || double.IsInfinity(dimension))
            return 0.0;

        double conf = 1.0;

        // Penalty for extreme values
        if (dimension < 1.5 || dimension > 6.0)
            conf *= 0.7;
        if (dimension < 1.0 || dimension > 8.0)
            conf *= 0.5;

        // Bonus for physically meaningful values
        if (dimension >= 1.8 && dimension <= 4.5)
            conf *= 1.1;

        // Slope quality (should be negative for diffusion)
        if (Math.Abs(slope) < 0.1)
            conf *= 0.7;
        if (slope >= 0)
            conf *= 0.3;

        // Size bonus
        if (nodeCount >= 100)
            conf *= 1.1;
        if (nodeCount >= 500)
            conf *= 1.1;

        return Math.Clamp(conf, 0.0, 1.0);
    }

    private void UpdateSmoothed(SpectralResult result)
    {
        // Add to history
        _history.Enqueue(result);
        while (_history.Count > MaxHistorySize)
            _history.Dequeue();

        // EMA update
        double alpha = _config.SpectralSmoothingAlpha;

        if (_computationCount == 0)
        {
            // First computation - initialize directly
            _smoothedDimension = result.Dimension;
            _smoothedConfidence = result.Confidence;
        }
        else
        {
            // Weight update by confidence
            double effectiveAlpha = alpha * result.Confidence;
            _smoothedDimension = effectiveAlpha * result.Dimension +
                                 (1.0 - effectiveAlpha) * _smoothedDimension;

            _smoothedConfidence = alpha * result.Confidence +
                                  (1.0 - alpha) * _smoothedConfidence;
        }
    }
}

/// <summary>
/// Recommended action based on spectral dimension state.
/// </summary>
public enum SpectralAction
{
    /// <summary>Maintain current parameters - dimension is healthy.</summary>
    Maintain,

    /// <summary>Slightly reduce G - dimension is a bit low.</summary>
    SlightlyReduceGravity,

    /// <summary>Slightly increase G - dimension is a bit high.</summary>
    SlightlyIncreaseGravity,

    /// <summary>Reduce G significantly - approaching fragmentation.</summary>
    ReduceGravity,

    /// <summary>Increase G significantly - hyperbolic regime.</summary>
    IncreaseGravity,

    /// <summary>Emergency recovery needed - critical fragmentation.</summary>
    EmergencyRecovery,

    /// <summary>Emergency compaction needed - extreme hyperbolic regime.</summary>
    EmergencyCompaction
}
