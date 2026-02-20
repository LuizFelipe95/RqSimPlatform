using System.Runtime.InteropServices;

namespace RQSimulation.Core.Plugins;

/// <summary>
/// GPU-compatible physics parameters for dynamic configuration.
/// 
/// This is a LOCAL copy of the structure from RqSimEngineApi.
/// Both structures must be kept in sync.
/// 
/// WHY LOCAL COPY:
/// - Avoids circular dependency between RqSimGraphEngine and RqSimEngineApi
/// - RqSimGraphEngine is a core library without UI dependencies
/// - Conversion happens at the boundary (pipeline entry point)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DynamicPhysicsParams
{
    // === Time & Evolution ===
    public double DeltaTime;
    public double CurrentTime;
    public long TickId;

    // === Gravity & Geometry ===
    public double GravitationalCoupling;
    public double RicciFlowAlpha;
    public double LapseFunctionAlpha;
    public double CosmologicalConstant;
    public double VacuumEnergyScale;
    public double LazyWalkAlpha;

    // === Thermodynamics ===
    public double Temperature;
    public double InverseBeta;
    public double AnnealingRate;
    public double DecoherenceRate;

    // === Gauge Fields ===
    public double GaugeCoupling;
    public double WilsonParameter;
    public double GaugeFieldDamping;

    // === Topology Dynamics ===
    public double EdgeCreationProbability;
    public double EdgeDeletionProbability;
    public double TopologyBreakThreshold;
    public double EdgeTrialProbability;

    // === Quantum Fields ===
    public double MeasurementThreshold;
    public double ScalarFieldMassSquared;
    public double FermionMass;
    public double PairCreationEnergy;

    // === Spectral Geometry ===
    public double SpectralCutoff;
    public double TargetSpectralDimension;
    public double SpectralDimensionStrength;

    // === Numerical (Sinkhorn) ===
    public int SinkhornIterations;
    public double SinkhornEpsilon;
    public double ConvergenceThreshold;

    // === MCMC Metropolis-Hastings ===
    public double McmcBeta;
    public int McmcStepsPerCall;
    public double McmcWeightPerturbation;

    // === Flags ===
    public int Flags;

    /// <summary>
    /// Creates default parameters matching PhysicsConstants.
    /// </summary>
    public static DynamicPhysicsParams Default => new()
    {
        DeltaTime = 0.01,
        CurrentTime = 0.0,
        TickId = 0,
        
        GravitationalCoupling = 0.05,
        RicciFlowAlpha = 0.5,
        LapseFunctionAlpha = 1.0,
        CosmologicalConstant = 0.0,
        VacuumEnergyScale = 0.00005,
        LazyWalkAlpha = 0.1,
        
        Temperature = 10.0,
        InverseBeta = 0.1,
        AnnealingRate = 0.995,
        DecoherenceRate = 0.001,
        
        GaugeCoupling = 1.0,
        WilsonParameter = 1.0,
        GaugeFieldDamping = 0.001,
        
        EdgeCreationProbability = 0.05,
        EdgeDeletionProbability = 0.01,
        TopologyBreakThreshold = 0.001,
        EdgeTrialProbability = 0.02,
        
        MeasurementThreshold = 0.3,
        ScalarFieldMassSquared = 0.1,
        FermionMass = 0.1,
        PairCreationEnergy = 0.01,
        
        SpectralCutoff = 1.0,
        TargetSpectralDimension = 4.0,
        SpectralDimensionStrength = 0.1,
        
        SinkhornIterations = 50,
        SinkhornEpsilon = 0.01,
        ConvergenceThreshold = 1e-6,

        McmcBeta = 1.0,
        McmcStepsPerCall = 10,
        McmcWeightPerturbation = 0.1,

        Flags = 0b11101101  // Default flags
    };

    // === Flag accessors ===
    public bool UseDoublePrecision
    {
        readonly get => (Flags & (1 << 0)) != 0;
        set => Flags = value ? Flags | (1 << 0) : Flags & ~(1 << 0);
    }
    
    public bool ScientificMode
    {
        readonly get => (Flags & (1 << 1)) != 0;
        set => Flags = value ? Flags | (1 << 1) : Flags & ~(1 << 1);
    }
    
    public bool EnableOllivierRicci
    {
        readonly get => (Flags & (1 << 3)) != 0;
        set => Flags = value ? Flags | (1 << 3) : Flags & ~(1 << 3);
    }
}

/// <summary>
/// Interface for physics modules that support dynamic configuration.
/// </summary>
public interface IDynamicPhysicsModule
{
    /// <summary>
    /// Updates module with current frame's parameters.
    /// Called before ExecuteStep each frame.
    /// </summary>
    void UpdateParameters(in DynamicPhysicsParams parameters);
}
