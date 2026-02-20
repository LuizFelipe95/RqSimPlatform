using System;
using System.Collections.Generic;

namespace RQSimulation.Analysis.VacuumEnergy;

/// <summary>
/// Detects thermalization (equilibration) of a simulation run
/// by monitoring stability of spectral dimension and curvature.
///
/// RQ-HYPOTHESIS CONTEXT:
/// The vacuum scaling experiment requires measurements taken at thermal
/// equilibrium, not during transient dynamics. This detector watches:
/// 1. Spectral dimension d_S — must stabilize (low variance over a window)
/// 2. Average Ricci curvature — must stop drifting
/// 3. Minimum warmup steps — prevents false early detection
///
/// Thermalization criterion:
///   variance(d_S, last W steps) &lt; σ²_threshold
///   AND |drift(curvature, last W steps)| &lt; drift_threshold
///   AND step ≥ minWarmup
/// </summary>
public sealed class ThermalizationDetector
{
    private readonly int _windowSize;
    private readonly int _minWarmupSteps;
    private readonly double _spectralVarianceThreshold;
    private readonly double _curvatureDriftThreshold;

    private readonly Queue<double> _spectralHistory = new();
    private readonly Queue<double> _curvatureHistory = new();

    /// <summary>Whether the simulation has reached thermal equilibrium.</summary>
    public bool IsThermalized { get; private set; }

    /// <summary>Step at which thermalization was first detected (−1 if not yet).</summary>
    public int ThermalizationStep { get; private set; } = -1;

    /// <summary>Current spectral dimension variance over the window.</summary>
    public double CurrentSpectralVariance { get; private set; }

    /// <summary>Current curvature drift (slope) over the window.</summary>
    public double CurrentCurvatureDrift { get; private set; }

    /// <summary>
    /// Creates a new thermalization detector.
    /// </summary>
    /// <param name="windowSize">Number of steps in the sliding analysis window</param>
    /// <param name="minWarmupSteps">Minimum steps before thermalization can be declared</param>
    /// <param name="spectralVarianceThreshold">Max variance of d_S to declare stable</param>
    /// <param name="curvatureDriftThreshold">Max |drift| of curvature to declare stable</param>
    public ThermalizationDetector(
        int windowSize = 50,
        int minWarmupSteps = 200,
        double spectralVarianceThreshold = 0.1,
        double curvatureDriftThreshold = 0.01)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(minWarmupSteps);

        _windowSize = windowSize;
        _minWarmupSteps = minWarmupSteps;
        _spectralVarianceThreshold = spectralVarianceThreshold;
        _curvatureDriftThreshold = curvatureDriftThreshold;
    }

    /// <summary>
    /// Updates the detector with measurements from the current step.
    /// Call once per tuning/measurement interval.
    /// </summary>
    /// <param name="step">Current simulation step</param>
    /// <param name="spectralDimension">Current spectral dimension d_S</param>
    /// <param name="avgCurvature">Current average Ollivier-Ricci curvature</param>
    public void Update(int step, double spectralDimension, double avgCurvature)
    {
        if (IsThermalized)
        {
            return; // Already detected — no need to keep checking
        }

        // Append to histories
        _spectralHistory.Enqueue(spectralDimension);
        _curvatureHistory.Enqueue(avgCurvature);

        // Trim to window
        while (_spectralHistory.Count > _windowSize)
        {
            _spectralHistory.Dequeue();
        }
        while (_curvatureHistory.Count > _windowSize)
        {
            _curvatureHistory.Dequeue();
        }

        // Need full window + warmup to make a determination
        if (_spectralHistory.Count < _windowSize || step < _minWarmupSteps)
        {
            return;
        }

        // Check spectral dimension stability
        CurrentSpectralVariance = ComputeVariance(_spectralHistory);

        // Check curvature drift (linear slope)
        CurrentCurvatureDrift = ComputeLinearDrift(_curvatureHistory);

        if (CurrentSpectralVariance < _spectralVarianceThreshold &&
            Math.Abs(CurrentCurvatureDrift) < _curvatureDriftThreshold)
        {
            IsThermalized = true;
            ThermalizationStep = step;
        }
    }

    /// <summary>
    /// Resets the detector for a new simulation run.
    /// </summary>
    public void Reset()
    {
        IsThermalized = false;
        ThermalizationStep = -1;
        CurrentSpectralVariance = 0;
        CurrentCurvatureDrift = 0;
        _spectralHistory.Clear();
        _curvatureHistory.Clear();
    }

    // ============================================================
    // Statistics helpers
    // ============================================================

    private static double ComputeVariance(Queue<double> values)
    {
        int count = values.Count;
        if (count < 2) return double.MaxValue;

        double sum = 0;
        double sumSq = 0;

        foreach (double v in values)
        {
            sum += v;
            sumSq += v * v;
        }

        double mean = sum / count;
        return (sumSq / count) - (mean * mean);
    }

    /// <summary>
    /// Computes the linear drift (slope) of values in the queue
    /// using simple linear regression: slope = Cov(x,y) / Var(x)
    /// where x = [0, 1, ... n-1].
    /// </summary>
    private static double ComputeLinearDrift(Queue<double> values)
    {
        int n = values.Count;
        if (n < 2) return double.MaxValue;

        // For x = [0..n-1], mean_x = (n-1)/2, Var(x) = (n²-1)/12
        double meanX = (n - 1) / 2.0;
        double varX = ((double)n * n - 1.0) / 12.0;

        double sumY = 0;
        double sumXY = 0;
        int i = 0;

        foreach (double y in values)
        {
            sumY += y;
            sumXY += i * y;
            i++;
        }

        double meanY = sumY / n;
        double covXY = (sumXY / n) - (meanX * meanY);

        return varX > 1e-15 ? covXY / varX : 0.0;
    }
}
