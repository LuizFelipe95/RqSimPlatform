// RQSimulation/Experiments/ExperimentDefinition.cs
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RqSimGraphEngine.Experiments;

/// <summary>
/// Complete experiment definition including configuration, expected results,
/// and actual results for JSON serialization and result validation.
/// </summary>
public sealed class ExperimentDefinition
{
    /// <summary>Unique experiment identifier</summary>
    public Guid ExperimentId { get; set; } = Guid.NewGuid();
    
    /// <summary>Display name of the experiment</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Detailed description of experiment goals</summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>When experiment was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>When experiment was last run</summary>
    public DateTime? LastRunAt { get; set; }
    
    /// <summary>Startup configuration for simulation</summary>
    public ExperimentConfig Config { get; set; } = new();
    
    /// <summary>Expected results (predictions)</summary>
    public ExpectedResults Expected { get; set; } = new();
    
    /// <summary>Actual results from last run (null if not run yet)</summary>
    public ActualResults? Actual { get; set; }
    
    /// <summary>Validation result from last run</summary>
    public ValidationSummary? Validation { get; set; }
    
    /// <summary>User notes about experiment</summary>
    public string Notes { get; set; } = string.Empty;
    
    /// <summary>Tags for categorization</summary>
    public List<string> Tags { get; set; } = [];
}

/// <summary>
/// Experiment configuration - mirrors StartupConfig with JSON serialization support.
/// </summary>
public sealed class ExperimentConfig
{
    // === Basic Graph Parameters ===
    public int NodeCount { get; set; } = 300;
    public double InitialEdgeProb { get; set; } = 0.05;
    public double InitialExcitedProb { get; set; } = 0.10;
    public int TargetDegree { get; set; } = 8;
    
    // === Simulation Parameters ===
    public int TotalSteps { get; set; } = 5000;
    public double Temperature { get; set; } = 10.0;
    public double LambdaState { get; set; } = 0.5;
    public double EdgeTrialProbability { get; set; } = 0.02;
    public double MeasurementThreshold { get; set; } = 0.3;
    
    // === Physics Parameters ===
    public double GravitationalCoupling { get; set; } = 0.025;
    public double VacuumEnergyScale { get; set; } = 0.0001;
    public double HotStartTemperature { get; set; } = 5.0;
    public double AnnealingCoolingRate { get; set; } = 0.995;
    public double DecoherenceRate { get; set; } = 0.005;
    public double AdaptiveThresholdSigma { get; set; } = 1.5;
    public double WarmupDuration { get; set; } = 200;
    public double GravityTransitionDuration { get; set; } = 137;
    
    // === Physics Modules ===
    public bool UseSpectralGeometry { get; set; } = true;
    public bool UseNetworkGravity { get; set; } = true;
    public bool UseQuantumDrivenStates { get; set; } = true;
    public bool UseSpinorField { get; set; } = false;
    public bool UseVacuumFluctuations { get; set; } = true;
    public bool UseHotStartAnnealing { get; set; } = true;
    public bool UseTopologicalProtection { get; set; } = true;
    
    // === Fractal Topology ===
    public int FractalLevels { get; set; } = 0;
    public int FractalBranchFactor { get; set; } = 0;
    
    /// <summary>
    /// Converts to StartupConfig for experiment loading.
    /// </summary>
    public StartupConfig ToStartupConfig()
    {
        return new StartupConfig
        {
            NodeCount = NodeCount,
            InitialEdgeProb = InitialEdgeProb,
            InitialExcitedProb = InitialExcitedProb,
            TargetDegree = TargetDegree,
            TotalSteps = TotalSteps,
            Temperature = Temperature,
            LambdaState = LambdaState,
            EdgeTrialProbability = EdgeTrialProbability,
            MeasurementThreshold = MeasurementThreshold,
            GravitationalCoupling = GravitationalCoupling,
            VacuumEnergyScale = VacuumEnergyScale,
            HotStartTemperature = HotStartTemperature,
            AnnealingCoolingRate = AnnealingCoolingRate,
            DecoherenceRate = DecoherenceRate,
            AdaptiveThresholdSigma = AdaptiveThresholdSigma,
            WarmupDuration = WarmupDuration,
            GravityTransitionDuration = GravityTransitionDuration,
            UseSpectralGeometry = UseSpectralGeometry,
            UseNetworkGravity = UseNetworkGravity,
            UseQuantumDrivenStates = UseQuantumDrivenStates,
            UseSpinorField = UseSpinorField,
            UseVacuumFluctuations = UseVacuumFluctuations,
            UseHotStartAnnealing = UseHotStartAnnealing,
            UseTopologicalProtection = UseTopologicalProtection,
            FractalLevels = FractalLevels,
            FractalBranchFactor = FractalBranchFactor
        };
    }
    
    /// <summary>
    /// Creates from StartupConfig.
    /// </summary>
    public static ExperimentConfig FromStartupConfig(StartupConfig config)
    {
        return new ExperimentConfig
        {
            NodeCount = config.NodeCount,
            InitialEdgeProb = config.InitialEdgeProb,
            InitialExcitedProb = config.InitialExcitedProb,
            TargetDegree = config.TargetDegree,
            TotalSteps = config.TotalSteps,
            Temperature = config.Temperature,
            LambdaState = config.LambdaState,
            EdgeTrialProbability = config.EdgeTrialProbability,
            MeasurementThreshold = config.MeasurementThreshold,
            GravitationalCoupling = config.GravitationalCoupling,
            VacuumEnergyScale = config.VacuumEnergyScale,
            HotStartTemperature = config.HotStartTemperature,
            AnnealingCoolingRate = config.AnnealingCoolingRate,
            DecoherenceRate = config.DecoherenceRate,
            AdaptiveThresholdSigma = config.AdaptiveThresholdSigma,
            WarmupDuration = config.WarmupDuration,
            GravityTransitionDuration = config.GravityTransitionDuration,
            UseSpectralGeometry = config.UseSpectralGeometry,
            UseNetworkGravity = config.UseNetworkGravity,
            UseQuantumDrivenStates = config.UseQuantumDrivenStates,
            UseSpinorField = config.UseSpinorField,
            UseVacuumFluctuations = config.UseVacuumFluctuations,
            UseHotStartAnnealing = config.UseHotStartAnnealing,
            UseTopologicalProtection = config.UseTopologicalProtection,
            FractalLevels = config.FractalLevels,
            FractalBranchFactor = config.FractalBranchFactor
        };
    }
}

/// <summary>
/// Expected results for experiment - defines predictions and acceptable ranges.
/// </summary>
public sealed class ExpectedResults
{
    // === Spectral Dimension ===
    /// <summary>Expected final spectral dimension (e.g., 4.0 for 4D spacetime)</summary>
    public double SpectralDimensionTarget { get; set; } = 4.0;
    
    /// <summary>Minimum acceptable d_S (e.g., 3.0)</summary>
    public double SpectralDimensionMin { get; set; } = 2.0;
    
    /// <summary>Maximum acceptable d_S (e.g., 5.0)</summary>
    public double SpectralDimensionMax { get; set; } = 6.0;
    
    // === Heavy Clusters (Mass) ===
    /// <summary>Expected heavy cluster count</summary>
    public int HeavyClusterCountMin { get; set; } = 1;
    
    /// <summary>Maximum expected clusters (too many = fragmentation)</summary>
    public int HeavyClusterCountMax { get; set; } = 50;
    
    /// <summary>Expected heavy mass (binding energy)</summary>
    public double HeavyMassMin { get; set; } = 0.0;
    
    /// <summary>Maximum heavy mass</summary>
    public double HeavyMassMax { get; set; } = 100.0;
    
    // === Largest Cluster ===
    /// <summary>Minimum largest cluster as fraction of N (e.g., 0.05 = 5%)</summary>
    public double LargestClusterFractionMin { get; set; } = 0.0;
    
    /// <summary>Maximum largest cluster as fraction (e.g., 0.30 = 30%, avoids black hole)</summary>
    public double LargestClusterFractionMax { get; set; } = 0.30;
    
    // === Network Temperature ===
    /// <summary>Expected final temperature (should be low for stable structure)</summary>
    public double FinalTemperatureMax { get; set; } = 1.0;
    
    // === Excited Nodes ===
    /// <summary>Expected excited fraction at end (e.g., low = stable, high = active)</summary>
    public double ExcitedFractionMin { get; set; } = 0.0;
    
    /// <summary>Maximum excited fraction</summary>
    public double ExcitedFractionMax { get; set; } = 0.5;
    
    // === Custom Criteria ===
    /// <summary>Custom expected behavior description</summary>
    public string CustomCriteria { get; set; } = string.Empty;
    
    /// <summary>Whether phase transition (d_S change > 1) should occur</summary>
    public bool ExpectPhaseTransition { get; set; } = true;
    
    /// <summary>Whether structure should stabilize (low variance in last 20%)</summary>
    public bool ExpectStabilization { get; set; } = true;
}

/// <summary>
/// Actual results from experiment run.
/// </summary>
public sealed class ActualResults
{
    /// <summary>When simulation completed</summary>
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>Final step reached</summary>
    public int FinalStep { get; set; }
    
    /// <summary>Total steps planned</summary>
    public int TotalStepsPlanned { get; set; }
    
    /// <summary>Whether simulation completed normally</summary>
    public bool CompletedNormally { get; set; }
    
    /// <summary>Reason for early termination (if any)</summary>
    public string? TerminationReason { get; set; }
    
    // === Final Metrics ===
    public double FinalSpectralDimension { get; set; }
    public double InitialSpectralDimension { get; set; }
    public double SpectralDimensionChange { get; set; }
    
    public int FinalHeavyClusterCount { get; set; }
    public double FinalHeavyMass { get; set; }
    public int FinalLargestCluster { get; set; }
    public double LargestClusterFraction { get; set; }
    
    public double FinalTemperature { get; set; }
    public double FinalEffectiveG { get; set; }
    
    public int FinalExcitedCount { get; set; }
    public double ExcitedFraction { get; set; }
    
    public int FinalStrongEdges { get; set; }
    public double FinalCorrelation { get; set; }
    public double FinalQNorm { get; set; }
    public double FinalEntanglement { get; set; }
    
    // === Time Series Statistics ===
    public double SpectralDimensionVarianceLast20Pct { get; set; }
    public double HeavyMassVarianceLast20Pct { get; set; }
    public bool PhaseTransitionOccurred { get; set; }
    public bool StructureStabilized { get; set; }
    
    // === Wall Clock ===
    public double WallClockSeconds { get; set; }
}

/// <summary>
/// Summary of validation comparing expected vs actual results.
/// </summary>
public sealed class ValidationSummary
{
    /// <summary>Overall pass/fail status</summary>
    public bool Passed { get; set; }
    
    /// <summary>Number of criteria passed</summary>
    public int CriteriaPassed { get; set; }
    
    /// <summary>Total number of criteria checked</summary>
    public int CriteriaTotal { get; set; }
    
    /// <summary>Pass rate as percentage</summary>
    public double PassRate => CriteriaTotal > 0 ? (double)CriteriaPassed / CriteriaTotal * 100 : 0;
    
    /// <summary>Individual validation results</summary>
    public List<ValidationCriterion> Criteria { get; set; } = [];
    
    /// <summary>Overall summary message</summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// Single validation criterion result.
/// </summary>
public sealed class ValidationCriterion
{
    /// <summary>Name of the criterion</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Whether this criterion passed</summary>
    public bool Passed { get; set; }
    
    /// <summary>Expected value or range description</summary>
    public string Expected { get; set; } = string.Empty;
    
    /// <summary>Actual value</summary>
    public string Actual { get; set; } = string.Empty;
    
    /// <summary>Additional message</summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>Importance level: Critical, Important, Info</summary>
    public ValidationLevel Level { get; set; } = ValidationLevel.Important;
}

/// <summary>
/// Importance level for validation criterion.
/// </summary>
public enum ValidationLevel
{
    /// <summary>Must pass for experiment to be valid</summary>
    Critical,
    
    /// <summary>Should pass but not fatal</summary>
    Important,
    
    /// <summary>Informational only</summary>
    Info
}
