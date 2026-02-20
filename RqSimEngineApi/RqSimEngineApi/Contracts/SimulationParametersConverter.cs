namespace RqSimEngineApi.Contracts;

/// <summary>
/// Converter between SimulationParameters (API layer) and DynamicPhysicsParams (engine layer).
/// 
/// WHY TWO TYPES:
/// - SimulationParameters lives in RqSimEngineApi (has UI dependencies)
/// - DynamicPhysicsParams lives in RqSimGraphEngine (no UI dependencies)
/// - This prevents circular references between projects
/// 
/// USAGE:
/// 1. UI creates PhysicsSettingsConfig
/// 2. PhysicsSettingsConfig.ToGpuParameters() ? SimulationParameters
/// 3. SimulationParametersConverter.ToDynamic() ? object for pipeline
/// 4. Pipeline casts to DynamicPhysicsParams (same memory layout)
/// </summary>
public static class SimulationParametersConverter
{
    /// <summary>
    /// Converts SimulationParameters to a boxed object that can be cast to
    /// DynamicPhysicsParams in the engine layer.
    /// 
    /// IMPORTANT: Both structs must have identical memory layout!
    /// </summary>
    public static object ToDynamic(in SimulationParameters source)
    {
        // Since both structs have identical layout, we can copy bytes
        // For simplicity, we create an anonymous object with all fields
        // The pipeline will convert this to DynamicPhysicsParams
        
        return new DynamicPhysicsParamsDto
        {
            DeltaTime = source.DeltaTime,
            CurrentTime = source.CurrentTime,
            TickId = source.TickId,
            
            GravitationalCoupling = source.GravitationalCoupling,
            RicciFlowAlpha = source.RicciFlowAlpha,
            LapseFunctionAlpha = source.LapseFunctionAlpha,
            CosmologicalConstant = source.CosmologicalConstant,
            VacuumEnergyScale = source.VacuumEnergyScale,
            LazyWalkAlpha = source.LazyWalkAlpha,
            
            Temperature = source.Temperature,
            InverseBeta = source.InverseBeta,
            AnnealingRate = source.AnnealingRate,
            DecoherenceRate = source.DecoherenceRate,
            
            GaugeCoupling = source.GaugeCoupling,
            WilsonParameter = source.WilsonParameter,
            GaugeFieldDamping = source.GaugeFieldDamping,
            
            EdgeCreationProbability = source.EdgeCreationProbability,
            EdgeDeletionProbability = source.EdgeDeletionProbability,
            TopologyBreakThreshold = source.TopologyBreakThreshold,
            EdgeTrialProbability = source.EdgeTrialProbability,
            
            MeasurementThreshold = source.MeasurementThreshold,
            ScalarFieldMassSquared = source.ScalarFieldMassSquared,
            FermionMass = source.FermionMass,
            PairCreationEnergy = source.PairCreationEnergy,
            
            SpectralCutoff = source.SpectralCutoff,
            TargetSpectralDimension = source.TargetSpectralDimension,
            SpectralDimensionStrength = source.SpectralDimensionStrength,
            
            SinkhornIterations = source.SinkhornIterations,
            SinkhornEpsilon = source.SinkhornEpsilon,
            ConvergenceThreshold = source.ConvergenceThreshold,

            McmcBeta = 1.0,
            McmcStepsPerCall = 10,
            McmcWeightPerturbation = 0.1,

            Flags = source.Flags
        };
    }

    /// <summary>
    /// Converts SimulationParameters to DynamicPhysicsParamsDto for type-safe transfer.
    /// Use this when passing parameters to PhysicsPipeline.
    /// </summary>
    public static DynamicPhysicsParamsDto ToDto(in SimulationParameters source)
    {
        return (DynamicPhysicsParamsDto)ToDynamic(in source);
    }
}

/// <summary>
/// Data Transfer Object for passing parameters to engine.
/// Has same fields as DynamicPhysicsParams for easy conversion.
/// </summary>
public class DynamicPhysicsParamsDto
{
    public double DeltaTime { get; init; }
    public double CurrentTime { get; init; }
    public long TickId { get; init; }
    
    public double GravitationalCoupling { get; init; }
    public double RicciFlowAlpha { get; init; }
    public double LapseFunctionAlpha { get; init; }
    public double CosmologicalConstant { get; init; }
    public double VacuumEnergyScale { get; init; }
    public double LazyWalkAlpha { get; init; }
    
    public double Temperature { get; init; }
    public double InverseBeta { get; init; }
    public double AnnealingRate { get; init; }
    public double DecoherenceRate { get; init; }
    
    public double GaugeCoupling { get; init; }
    public double WilsonParameter { get; init; }
    public double GaugeFieldDamping { get; init; }
    
    public double EdgeCreationProbability { get; init; }
    public double EdgeDeletionProbability { get; init; }
    public double TopologyBreakThreshold { get; init; }
    public double EdgeTrialProbability { get; init; }
    
    public double MeasurementThreshold { get; init; }
    public double ScalarFieldMassSquared { get; init; }
    public double FermionMass { get; init; }
    public double PairCreationEnergy { get; init; }
    
    public double SpectralCutoff { get; init; }
    public double TargetSpectralDimension { get; init; }
    public double SpectralDimensionStrength { get; init; }
    
    public int SinkhornIterations { get; init; }
    public double SinkhornEpsilon { get; init; }
    public double ConvergenceThreshold { get; init; }

    public double McmcBeta { get; init; }
    public int McmcStepsPerCall { get; init; }
    public double McmcWeightPerturbation { get; init; }

    public int Flags { get; init; }

    /// <summary>
    /// Converts this DTO to DynamicPhysicsParams struct for pipeline consumption.
    /// Call this in RqSimGraphEngine layer where DynamicPhysicsParams is accessible.
    /// </summary>
    public RQSimulation.Core.Plugins.DynamicPhysicsParams ToDynamicPhysicsParams()
    {
        return new RQSimulation.Core.Plugins.DynamicPhysicsParams
        {
            DeltaTime = DeltaTime,
            CurrentTime = CurrentTime,
            TickId = TickId,
            
            GravitationalCoupling = GravitationalCoupling,
            RicciFlowAlpha = RicciFlowAlpha,
            LapseFunctionAlpha = LapseFunctionAlpha,
            CosmologicalConstant = CosmologicalConstant,
            VacuumEnergyScale = VacuumEnergyScale,
            LazyWalkAlpha = LazyWalkAlpha,
            
            Temperature = Temperature,
            InverseBeta = InverseBeta,
            AnnealingRate = AnnealingRate,
            DecoherenceRate = DecoherenceRate,
            
            GaugeCoupling = GaugeCoupling,
            WilsonParameter = WilsonParameter,
            GaugeFieldDamping = GaugeFieldDamping,
            
            EdgeCreationProbability = EdgeCreationProbability,
            EdgeDeletionProbability = EdgeDeletionProbability,
            TopologyBreakThreshold = TopologyBreakThreshold,
            EdgeTrialProbability = EdgeTrialProbability,
            
            MeasurementThreshold = MeasurementThreshold,
            ScalarFieldMassSquared = ScalarFieldMassSquared,
            FermionMass = FermionMass,
            PairCreationEnergy = PairCreationEnergy,
            
            SpectralCutoff = SpectralCutoff,
            TargetSpectralDimension = TargetSpectralDimension,
            SpectralDimensionStrength = SpectralDimensionStrength,
            
            SinkhornIterations = SinkhornIterations,
            SinkhornEpsilon = SinkhornEpsilon,
            ConvergenceThreshold = ConvergenceThreshold,

            McmcBeta = McmcBeta,
            McmcStepsPerCall = McmcStepsPerCall,
            McmcWeightPerturbation = McmcWeightPerturbation,

            Flags = Flags
        };
    }
}
