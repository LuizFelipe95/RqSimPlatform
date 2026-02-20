namespace RqSimEngineApi.Contracts;

/// <summary>
/// Default values and factory methods for SimulationParameters.
/// Separated into partial file to keep main struct clean.
/// </summary>
public partial struct SimulationParameters
{
    /// <summary>
    /// Creates parameters with sensible default values.
    /// These defaults match PhysicsConstants where applicable.
    /// </summary>
    public static SimulationParameters Default => new()
    {
        // Time
        DeltaTime = 0.01,
        CurrentTime = 0.0,
        TickId = 0,
        
        // Gravity
        GravitationalCoupling = 0.05,
        RicciFlowAlpha = 0.5,
        LapseFunctionAlpha = 1.0,
        CosmologicalConstant = 0.0,
        VacuumEnergyScale = 0.00005,
        LazyWalkAlpha = 0.1,  // 10% probability of staying at current node
        
        // Thermodynamics
        Temperature = 10.0,
        InverseBeta = 0.1,  // ? = 1/T
        AnnealingRate = 0.995,
        DecoherenceRate = 0.001,
        
        // Gauge
        GaugeCoupling = 1.0,
        WilsonParameter = 1.0,
        GaugeFieldDamping = 0.001,
        
        // Topology
        EdgeCreationProbability = 0.05,
        EdgeDeletionProbability = 0.01,
        TopologyBreakThreshold = 0.001,
        EdgeTrialProbability = 0.02,
        
        // Quantum
        MeasurementThreshold = 0.3,
        ScalarFieldMassSquared = 0.1,
        FermionMass = 0.1,
        PairCreationEnergy = 0.01,
        
        // Spectral
        SpectralCutoff = 1.0,
        TargetSpectralDimension = 4.0,
        SpectralDimensionStrength = 0.1,
        
        // Numerical
        SinkhornIterations = 50,
        SinkhornEpsilon = 0.01,
        ConvergenceThreshold = 1e-6,
        
        // Flags - reasonable defaults
        Flags = (1 << 0)  // UseDoublePrecision = true
              | (1 << 2)  // EnableVacuumReservoir = true
              | (1 << 3)  // EnableOllivierRicci = true
              | (1 << 5)  // EnableHamiltonianGravity = true
              | (1 << 6)  // EnableTopologyCompensation = true
              | (1 << 7)  // EnableWilsonProtection = true
    };

    /// <summary>
    /// Creates parameters optimized for fast visual preview.
    /// Lower precision, fewer iterations.
    /// </summary>
    public static SimulationParameters FastPreview => new()
    {
        DeltaTime = 0.02,  // Larger timestep
        CurrentTime = 0.0,
        TickId = 0,
        
        GravitationalCoupling = 0.05,
        RicciFlowAlpha = 0.3,  // Slower flow for stability
        LapseFunctionAlpha = 1.0,
        CosmologicalConstant = 0.0,
        VacuumEnergyScale = 0.0001,
        LazyWalkAlpha = 0.15,  // Slightly more lazy for stability
        
        Temperature = 5.0,
        InverseBeta = 0.2,
        AnnealingRate = 0.99,
        DecoherenceRate = 0.01,
        
        GaugeCoupling = 1.0,
        WilsonParameter = 1.0,
        GaugeFieldDamping = 0.01,
        
        EdgeCreationProbability = 0.02,
        EdgeDeletionProbability = 0.005,
        TopologyBreakThreshold = 0.01,
        EdgeTrialProbability = 0.01,
        
        MeasurementThreshold = 0.5,
        ScalarFieldMassSquared = 0.1,
        FermionMass = 0.1,
        PairCreationEnergy = 0.1,
        
        SpectralCutoff = 1.0,
        TargetSpectralDimension = 4.0,
        SpectralDimensionStrength = 0.05,
        
        SinkhornIterations = 20,  // Fewer iterations
        SinkhornEpsilon = 0.1,    // Less precise
        ConvergenceThreshold = 1e-4,
        
        Flags = (1 << 2)  // EnableVacuumReservoir only
              | (1 << 5)  // EnableHamiltonianGravity
    };

    /// <summary>
    /// Creates parameters for high-precision scientific runs.
    /// </summary>
    public static SimulationParameters Scientific => new()
    {
        DeltaTime = 0.001,  // Small timestep
        CurrentTime = 0.0,
        TickId = 0,
        
        GravitationalCoupling = 0.05,
        RicciFlowAlpha = 0.5,
        LapseFunctionAlpha = 1.0,
        CosmologicalConstant = 0.0,
        VacuumEnergyScale = 0.00005,
        LazyWalkAlpha = 0.1,  // Standard value
        
        Temperature = 10.0,
        InverseBeta = 0.1,
        AnnealingRate = 0.999,
        DecoherenceRate = 0.0001,
        
        GaugeCoupling = 1.0,
        WilsonParameter = 1.0,
        GaugeFieldDamping = 0.0001,
        
        EdgeCreationProbability = 0.05,
        EdgeDeletionProbability = 0.01,
        TopologyBreakThreshold = 0.0001,
        EdgeTrialProbability = 0.02,
        
        MeasurementThreshold = 0.3,
        ScalarFieldMassSquared = 0.1,
        FermionMass = 0.1,
        PairCreationEnergy = 0.01,
        
        SpectralCutoff = 1.0,
        TargetSpectralDimension = 4.0,
        SpectralDimensionStrength = 0.1,
        
        SinkhornIterations = 100,  // More iterations
        SinkhornEpsilon = 0.001,   // Higher precision
        ConvergenceThreshold = 1e-8,
        
        Flags = (1 << 0)  // UseDoublePrecision
              | (1 << 1)  // ScientificMode
              | (1 << 2)  // EnableVacuumReservoir
              | (1 << 3)  // EnableOllivierRicci
              | (1 << 4)  // EnableSpectralAction
              | (1 << 5)  // EnableHamiltonianGravity
              | (1 << 6)  // EnableTopologyCompensation
              | (1 << 7)  // EnableWilsonProtection
    };

    /// <summary>
    /// Validates parameters are within reasonable bounds.
    /// Returns list of validation errors (empty if valid).
    /// </summary>
    public readonly List<string> Validate()
    {
        var errors = new List<string>();
        
        if (DeltaTime <= 0 || DeltaTime > 1.0)
            errors.Add($"DeltaTime must be in (0, 1], got {DeltaTime}");
        
        if (GravitationalCoupling < 0 || GravitationalCoupling > 10)
            errors.Add($"GravitationalCoupling must be in [0, 10], got {GravitationalCoupling}");
        
        if (RicciFlowAlpha < 0 || RicciFlowAlpha > 2)
            errors.Add($"RicciFlowAlpha must be in [0, 2], got {RicciFlowAlpha}");
        
        if (Temperature <= 0)
            errors.Add($"Temperature must be > 0, got {Temperature}");
        
        if (SinkhornIterations < 1 || SinkhornIterations > 1000)
            errors.Add($"SinkhornIterations must be in [1, 1000], got {SinkhornIterations}");
        
        if (SinkhornEpsilon <= 0 || SinkhornEpsilon > 1)
            errors.Add($"SinkhornEpsilon must be in (0, 1], got {SinkhornEpsilon}");
        
        if (EdgeCreationProbability < 0 || EdgeCreationProbability > 1)
            errors.Add($"EdgeCreationProbability must be in [0, 1], got {EdgeCreationProbability}");
        
        if (EdgeDeletionProbability < 0 || EdgeDeletionProbability > 1)
            errors.Add($"EdgeDeletionProbability must be in [0, 1], got {EdgeDeletionProbability}");
        
        return errors;
    }

    /// <summary>
    /// Creates a copy with modified values using a builder pattern.
    /// </summary>
    public readonly SimulationParameters With(
        double? deltaTime = null,
        double? gravitationalCoupling = null,
        double? ricciFlowAlpha = null,
        double? temperature = null,
        double? lazyWalkAlpha = null,
        int? sinkhornIterations = null)
    {
        var copy = this;
        if (deltaTime.HasValue) copy.DeltaTime = deltaTime.Value;
        if (gravitationalCoupling.HasValue) copy.GravitationalCoupling = gravitationalCoupling.Value;
        if (ricciFlowAlpha.HasValue) copy.RicciFlowAlpha = ricciFlowAlpha.Value;
        if (temperature.HasValue) copy.Temperature = temperature.Value;
        if (lazyWalkAlpha.HasValue) copy.LazyWalkAlpha = lazyWalkAlpha.Value;
        if (sinkhornIterations.HasValue) copy.SinkhornIterations = sinkhornIterations.Value;
        return copy;
    }
}
