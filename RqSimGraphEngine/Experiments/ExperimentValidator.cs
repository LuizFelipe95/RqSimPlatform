// RQSimulation/Experiments/ExperimentValidator.cs
using System;
using System.Collections.Generic;
using System.Text;

namespace RqSimGraphEngine.Experiments;

/// <summary>
/// Validates experiment results against expected criteria.
/// </summary>
public static class ExperimentValidator
{
    /// <summary>
    /// Validates actual results against expected results and generates summary.
    /// </summary>
    public static ValidationSummary Validate(ExpectedResults expected, ActualResults actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        
        var criteria = new List<ValidationCriterion>();
        
        // === Spectral Dimension Validation ===
        criteria.Add(ValidateSpectralDimensionRange(expected, actual));
        criteria.Add(ValidateSpectralDimensionTarget(expected, actual));
        
        // === Heavy Clusters Validation ===
        criteria.Add(ValidateHeavyClusterCount(expected, actual));
        criteria.Add(ValidateHeavyMass(expected, actual));
        
        // === Largest Cluster Validation (Critical - avoids black hole) ===
        criteria.Add(ValidateLargestClusterFraction(expected, actual));
        
        // === Temperature Validation ===
        criteria.Add(ValidateFinalTemperature(expected, actual));
        
        // === Excited Nodes Validation ===
        criteria.Add(ValidateExcitedFraction(expected, actual));
        
        // === Phase Transition Validation ===
        if (expected.ExpectPhaseTransition)
        {
            criteria.Add(ValidatePhaseTransition(expected, actual));
        }
        
        // === Stabilization Validation ===
        if (expected.ExpectStabilization)
        {
            criteria.Add(ValidateStabilization(expected, actual));
        }
        
        // === Completion Validation ===
        criteria.Add(ValidateCompletion(actual));
        
        // Calculate summary
        int passed = 0;
        int criticalFailed = 0;
        
        foreach (var c in criteria)
        {
            if (c.Passed) passed++;
            else if (c.Level == ValidationLevel.Critical) criticalFailed++;
        }
        
        bool overallPassed = criticalFailed == 0 && passed >= criteria.Count / 2;
        
        var summary = new ValidationSummary
        {
            Passed = overallPassed,
            CriteriaPassed = passed,
            CriteriaTotal = criteria.Count,
            Criteria = criteria,
            Summary = BuildSummaryMessage(overallPassed, passed, criteria.Count, criticalFailed)
        };
        
        return summary;
    }
    
    /// <summary>
    /// Populates ActualResults from current simulation state.
    /// </summary>
    public static ActualResults CollectResults(
        int finalStep,
        int totalStepsPlanned,
        bool completedNormally,
        string? terminationReason,
        double finalSpectralDim,
        double initialSpectralDim,
        int heavyClusterCount,
        double heavyMass,
        int largestCluster,
        int nodeCount,
        double finalTemperature,
        double effectiveG,
        int excitedCount,
        int strongEdges,
        double correlation,
        double qNorm,
        double entanglement,
        double spectralDimVariance,
        double heavyMassVariance,
        double wallClockSeconds)
    {
        double spectralDimChange = finalSpectralDim - initialSpectralDim;
        bool phaseTransition = Math.Abs(spectralDimChange) > 1.0;
        
        // Structure stabilized if variance is low
        bool stabilized = spectralDimVariance < 0.5 && heavyMassVariance < 5.0;
        
        return new ActualResults
        {
            CompletedAt = DateTime.UtcNow,
            FinalStep = finalStep,
            TotalStepsPlanned = totalStepsPlanned,
            CompletedNormally = completedNormally,
            TerminationReason = terminationReason,
            
            FinalSpectralDimension = finalSpectralDim,
            InitialSpectralDimension = initialSpectralDim,
            SpectralDimensionChange = spectralDimChange,
            
            FinalHeavyClusterCount = heavyClusterCount,
            FinalHeavyMass = heavyMass,
            FinalLargestCluster = largestCluster,
            LargestClusterFraction = nodeCount > 0 ? (double)largestCluster / nodeCount : 0,
            
            FinalTemperature = finalTemperature,
            FinalEffectiveG = effectiveG,
            
            FinalExcitedCount = excitedCount,
            ExcitedFraction = nodeCount > 0 ? (double)excitedCount / nodeCount : 0,
            
            FinalStrongEdges = strongEdges,
            FinalCorrelation = correlation,
            FinalQNorm = qNorm,
            FinalEntanglement = entanglement,
            
            SpectralDimensionVarianceLast20Pct = spectralDimVariance,
            HeavyMassVarianceLast20Pct = heavyMassVariance,
            PhaseTransitionOccurred = phaseTransition,
            StructureStabilized = stabilized,
            
            WallClockSeconds = wallClockSeconds
        };
    }
    
    #region Individual Validations
    
    private static ValidationCriterion ValidateSpectralDimensionRange(ExpectedResults expected, ActualResults actual)
    {
        bool inRange = actual.FinalSpectralDimension >= expected.SpectralDimensionMin &&
                      actual.FinalSpectralDimension <= expected.SpectralDimensionMax;
        
        return new ValidationCriterion
        {
            Name = "Spectral Dimension (d_S) Range",
            Passed = inRange,
            Expected = $"[{expected.SpectralDimensionMin:F1}, {expected.SpectralDimensionMax:F1}]",
            Actual = $"{actual.FinalSpectralDimension:F2}",
            Message = inRange 
                ? "d_S within expected range" 
                : actual.FinalSpectralDimension < expected.SpectralDimensionMin 
                    ? "d_S too low - possible fragmentation"
                    : "d_S too high - hyperbolic/tree-like structure",
            Level = ValidationLevel.Critical
        };
    }
    
    private static ValidationCriterion ValidateSpectralDimensionTarget(ExpectedResults expected, ActualResults actual)
    {
        double deviation = Math.Abs(actual.FinalSpectralDimension - expected.SpectralDimensionTarget);
        double tolerance = (expected.SpectralDimensionMax - expected.SpectralDimensionMin) / 4;
        bool nearTarget = deviation <= tolerance;
        
        return new ValidationCriterion
        {
            Name = "Spectral Dimension Target",
            Passed = nearTarget,
            Expected = $"~{expected.SpectralDimensionTarget:F1} (±{tolerance:F1})",
            Actual = $"{actual.FinalSpectralDimension:F2}",
            Message = nearTarget 
                ? "d_S close to target dimension"
                : $"d_S deviates by {deviation:F2} from target",
            Level = ValidationLevel.Important
        };
    }
    
    private static ValidationCriterion ValidateHeavyClusterCount(ExpectedResults expected, ActualResults actual)
    {
        bool inRange = actual.FinalHeavyClusterCount >= expected.HeavyClusterCountMin &&
                      actual.FinalHeavyClusterCount <= expected.HeavyClusterCountMax;
        
        return new ValidationCriterion
        {
            Name = "Heavy Cluster Count",
            Passed = inRange,
            Expected = $"[{expected.HeavyClusterCountMin}, {expected.HeavyClusterCountMax}]",
            Actual = $"{actual.FinalHeavyClusterCount}",
            Message = inRange 
                ? "Cluster count within expected range"
                : actual.FinalHeavyClusterCount < expected.HeavyClusterCountMin
                    ? "Too few clusters - no structure formation"
                    : "Too many clusters - excessive fragmentation",
            Level = ValidationLevel.Important
        };
    }
    
    private static ValidationCriterion ValidateHeavyMass(ExpectedResults expected, ActualResults actual)
    {
        bool inRange = actual.FinalHeavyMass >= expected.HeavyMassMin &&
                      actual.FinalHeavyMass <= expected.HeavyMassMax;
        
        return new ValidationCriterion
        {
            Name = "Heavy Mass (Binding Energy)",
            Passed = inRange,
            Expected = $"[{expected.HeavyMassMin:F1}, {expected.HeavyMassMax:F1}]",
            Actual = $"{actual.FinalHeavyMass:F2}",
            Message = inRange 
                ? "Heavy mass within expected range"
                : "Heavy mass outside expected bounds",
            Level = ValidationLevel.Important
        };
    }
    
    private static ValidationCriterion ValidateLargestClusterFraction(ExpectedResults expected, ActualResults actual)
    {
        bool inRange = actual.LargestClusterFraction >= expected.LargestClusterFractionMin &&
                      actual.LargestClusterFraction <= expected.LargestClusterFractionMax;
        
        string status = inRange 
            ? "No giant cluster (good)"
            : actual.LargestClusterFraction > expected.LargestClusterFractionMax
                ? "GIANT CLUSTER DETECTED - possible black hole formation"
                : "Largest cluster smaller than expected";
        
        return new ValidationCriterion
        {
            Name = "Largest Cluster Fraction",
            Passed = inRange,
            Expected = $"[{expected.LargestClusterFractionMin:P0}, {expected.LargestClusterFractionMax:P0}]",
            Actual = $"{actual.LargestClusterFraction:P1} ({actual.FinalLargestCluster} nodes)",
            Message = status,
            Level = ValidationLevel.Critical
        };
    }
    
    private static ValidationCriterion ValidateFinalTemperature(ExpectedResults expected, ActualResults actual)
    {
        bool low = actual.FinalTemperature <= expected.FinalTemperatureMax;
        
        return new ValidationCriterion
        {
            Name = "Final Temperature",
            Passed = low,
            Expected = $"? {expected.FinalTemperatureMax:F2}",
            Actual = $"{actual.FinalTemperature:F3}",
            Message = low 
                ? "System cooled to stable state"
                : "System still hot - annealing may need more time",
            Level = ValidationLevel.Important
        };
    }
    
    private static ValidationCriterion ValidateExcitedFraction(ExpectedResults expected, ActualResults actual)
    {
        bool inRange = actual.ExcitedFraction >= expected.ExcitedFractionMin &&
                      actual.ExcitedFraction <= expected.ExcitedFractionMax;
        
        return new ValidationCriterion
        {
            Name = "Excited Fraction",
            Passed = inRange,
            Expected = $"[{expected.ExcitedFractionMin:P0}, {expected.ExcitedFractionMax:P0}]",
            Actual = $"{actual.ExcitedFraction:P1} ({actual.FinalExcitedCount} nodes)",
            Message = inRange 
                ? "Excited node count as expected"
                : "Unexpected excited node activity",
            Level = ValidationLevel.Info
        };
    }
    
    private static ValidationCriterion ValidatePhaseTransition(ExpectedResults expected, ActualResults actual)
    {
        return new ValidationCriterion
        {
            Name = "Phase Transition",
            Passed = actual.PhaseTransitionOccurred,
            Expected = "d_S change > 1.0",
            Actual = $"?d_S = {actual.SpectralDimensionChange:F2}",
            Message = actual.PhaseTransitionOccurred 
                ? "Phase transition occurred as expected"
                : "No significant phase transition detected",
            Level = ValidationLevel.Important
        };
    }
    
    private static ValidationCriterion ValidateStabilization(ExpectedResults expected, ActualResults actual)
    {
        return new ValidationCriterion
        {
            Name = "Structure Stabilization",
            Passed = actual.StructureStabilized,
            Expected = "Low variance in final 20%",
            Actual = $"d_S var={actual.SpectralDimensionVarianceLast20Pct:F3}, mass var={actual.HeavyMassVarianceLast20Pct:F2}",
            Message = actual.StructureStabilized 
                ? "Structure stabilized at end"
                : "Structure still fluctuating - may need longer run",
            Level = ValidationLevel.Important
        };
    }
    
    private static ValidationCriterion ValidateCompletion(ActualResults actual)
    {
        double completionRate = actual.TotalStepsPlanned > 0 
            ? (double)actual.FinalStep / actual.TotalStepsPlanned 
            : 0;
        
        return new ValidationCriterion
        {
            Name = "Simulation Completion",
            Passed = actual.CompletedNormally && completionRate >= 0.95,
            Expected = "100% completed",
            Actual = $"{completionRate:P0} ({actual.FinalStep}/{actual.TotalStepsPlanned})",
            Message = actual.CompletedNormally 
                ? "Simulation completed normally"
                : $"Early termination: {actual.TerminationReason ?? "unknown"}",
            Level = actual.CompletedNormally ? ValidationLevel.Info : ValidationLevel.Critical
        };
    }
    
    #endregion
    
    private static string BuildSummaryMessage(bool passed, int passedCount, int total, int criticalFailed)
    {
        var sb = new StringBuilder();
        
        if (passed)
        {
            sb.Append("? EXPERIMENT PASSED");
            sb.Append($" ({passedCount}/{total} criteria met)");
        }
        else
        {
            sb.Append("? EXPERIMENT FAILED");
            if (criticalFailed > 0)
            {
                sb.Append($" - {criticalFailed} critical criteria failed");
            }
            sb.Append($" ({passedCount}/{total} criteria met)");
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Generates human-readable validation report.
    /// </summary>
    public static string GenerateReport(ExperimentDefinition experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        
        var sb = new StringBuilder();
        
        sb.AppendLine($"???????????????????????????????????????????????????");
        sb.AppendLine($"  EXPERIMENT REPORT: {experiment.Name}");
        sb.AppendLine($"???????????????????????????????????????????????????");
        sb.AppendLine();
        
        sb.AppendLine($"ID: {experiment.ExperimentId}");
        sb.AppendLine($"Created: {experiment.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
        if (experiment.LastRunAt.HasValue)
        {
            sb.AppendLine($"Last Run: {experiment.LastRunAt:yyyy-MM-dd HH:mm:ss} UTC");
        }
        sb.AppendLine();
        
        sb.AppendLine("DESCRIPTION:");
        sb.AppendLine(experiment.Description);
        sb.AppendLine();
        
        if (experiment.Validation != null)
        {
            sb.AppendLine("???????????????????????????????????????????????????");
            sb.AppendLine($"  {experiment.Validation.Summary}");
            sb.AppendLine($"  Pass Rate: {experiment.Validation.PassRate:F1}%");
            sb.AppendLine("???????????????????????????????????????????????????");
            sb.AppendLine();
            
            sb.AppendLine("CRITERIA RESULTS:");
            foreach (var criterion in experiment.Validation.Criteria)
            {
                string icon = criterion.Passed ? "?" : "?";
                string level = criterion.Level switch
                {
                    ValidationLevel.Critical => "[CRITICAL]",
                    ValidationLevel.Important => "[IMPORTANT]",
                    _ => "[INFO]"
                };
                
                sb.AppendLine($"  {icon} {criterion.Name} {level}");
                sb.AppendLine($"      Expected: {criterion.Expected}");
                sb.AppendLine($"      Actual:   {criterion.Actual}");
                sb.AppendLine($"      {criterion.Message}");
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("NO VALIDATION RESULTS - Experiment not yet run");
        }
        
        if (experiment.Actual != null)
        {
            sb.AppendLine("???????????????????????????????????????????????????");
            sb.AppendLine("  FINAL METRICS");
            sb.AppendLine("???????????????????????????????????????????????????");
            sb.AppendLine($"  Spectral Dimension: {experiment.Actual.FinalSpectralDimension:F3}");
            sb.AppendLine($"  Heavy Clusters: {experiment.Actual.FinalHeavyClusterCount}");
            sb.AppendLine($"  Heavy Mass: {experiment.Actual.FinalHeavyMass:F2}");
            sb.AppendLine($"  Largest Cluster: {experiment.Actual.LargestClusterFraction:P1}");
            sb.AppendLine($"  Temperature: {experiment.Actual.FinalTemperature:F3}");
            sb.AppendLine($"  Effective G: {experiment.Actual.FinalEffectiveG:F4}");
            sb.AppendLine($"  Wall Clock: {experiment.Actual.WallClockSeconds:F1} seconds");
        }
        
        if (!string.IsNullOrWhiteSpace(experiment.Notes))
        {
            sb.AppendLine();
            sb.AppendLine("NOTES:");
            sb.AppendLine(experiment.Notes);
        }
        
        return sb.ToString();
    }
}
