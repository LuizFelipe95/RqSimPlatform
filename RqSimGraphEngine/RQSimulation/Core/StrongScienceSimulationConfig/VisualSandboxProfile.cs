// ============================================================
// VisualSandboxProfile.cs
// Implementation of ISimulationProfile for demo/sandbox mode
// Part of Strong Science Simulation Config architecture
// ============================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RQSimulation.Core.StrongScience;

/// <summary>
/// Profile for visual demonstration and interactive sandbox mode.
/// <para><strong>PRIORITIES:</strong></para>
/// <list type="bullet">
///   <item><description>60 FPS visual smoothness</description></item>
///   <item><description>No vertex "explosions" or graph fragmentation</description></item>
///   <item><description>Interactive parameter adjustment via UI sliders</description></item>
///   <item><description>Stable, aesthetically pleasing simulations</description></item>
/// </list>
/// 
/// <para><strong>COMPROMISES:</strong></para>
/// <list type="bullet">
///   <item><description>Uses fitted constants (not physical values)</description></item>
///   <item><description>Soft walls prevent singularities (physically incorrect)</description></item>
///   <item><description>Artificial viscosity for stability (non-Hamiltonian)</description></item>
///   <item><description>Parameters can change mid-simulation</description></item>
/// </list>
/// 
/// <para><strong>NOT FOR:</strong></para>
/// <list type="bullet">
///   <item><description>Publishable results</description></item>
///   <item><description>Hypothesis testing</description></item>
///   <item><description>Quantitative predictions</description></item>
/// </list>
/// </summary>
public sealed class VisualSandboxProfile : ISimulationProfile
{
    private readonly FittedConstants _constants;
    private string? _cachedHash;

    /// <summary>
    /// Creates a new visual sandbox profile with default fitted constants.
    /// </summary>
    public VisualSandboxProfile()
    {
        _constants = new FittedConstants();
    }

    /// <summary>
    /// Creates a visual sandbox profile with customizable fitted constants.
    /// </summary>
    /// <param name="configureConstants">Action to configure the constants.</param>
    public VisualSandboxProfile(Action<FittedConstants> configureConstants)
    {
        _constants = new FittedConstants();
        configureConstants(_constants);
    }

    /// <inheritdoc/>
    public string ProfileName => "VisualSandbox";

    /// <inheritdoc/>
    public bool IsStrictValidationEnabled => false;

    /// <inheritdoc/>
    public IPhysicalConstants Constants => _constants;

    /// <inheritdoc/>
    public bool AllowInteractiveRewiring => true;

    /// <inheritdoc/>
    public bool UseSoftWalls => true;

    /// <inheritdoc/>
    public bool UseArtificialViscosity => true;

    /// <inheritdoc/>
    public NumericalPrecision Precision => NumericalPrecision.Single;

    /// <summary>
    /// Gets the mutable fitted constants for UI binding.
    /// </summary>
    public FittedConstants MutableConstants => _constants;

    /// <inheritdoc/>
    public string GetConfigurationHash()
    {
        // Invalidate cache if constants have changed
        // (In sandbox mode, constants can change via UI)
        return ComputeConfigurationHash();
    }

    /// <inheritdoc/>
    public void Validate()
    {
        // Sandbox mode has no validation requirements
        // All configurations are allowed
    }

    /// <summary>
    /// Computes SHA256 hash of the current configuration.
    /// </summary>
    private string ComputeConfigurationHash()
    {
        var configData = new
        {
            Profile = nameof(VisualSandboxProfile),
            Timestamp = DateTime.UtcNow.Ticks, // Changes every call to indicate mutable state
            Constants = _constants.GetAllConstants()
                .ToDictionary(k => k.Key, v => v.Value.Value)
        };

        string json = JsonSerializer.Serialize(configData, new JsonSerializerOptions { WriteIndented = false });
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Resets all constants to default fitted values.
    /// </summary>
    public void ResetToDefaults()
    {
        _constants.ResetToDefaults();
        _cachedHash = null;
    }
}

/// <summary>
/// Mutable fitted constants for visual sandbox mode.
/// <para><strong>WARNING:</strong> These values are tuned for visual appeal,
/// NOT physical accuracy. Do not use for scientific simulations.</para>
/// </summary>
public sealed class FittedConstants : IPhysicalConstants
{
    /// <inheritdoc/>
    public string Name => "Fitted (Visual)";

    /// <inheritdoc/>
    public string Description =>
        "Fitted constants tuned for visual stability and 60 FPS performance. " +
        "NOT SUITABLE for scientific simulations.";

    // ============================================================
    // FUNDAMENTAL CONSTANTS (same as Planck, for interface compliance)
    // ============================================================

    /// <inheritdoc/>
    public double C => 1.0;

    /// <inheritdoc/>
    public double HBar => 1.0;

    /// <inheritdoc/>
    public double G => 1.0;

    /// <inheritdoc/>
    public double KBoltzmann => 1.0;

    /// <inheritdoc/>
    public double PlanckLength => 1.0;

    /// <inheritdoc/>
    public double PlanckTime => 1.0;

    /// <inheritdoc/>
    public double PlanckMass => 1.0;

    /// <inheritdoc/>
    public double PlanckEnergy => 1.0;

    // ============================================================
    // GAUGE COUPLING CONSTANTS (simplified for visuals)
    // ============================================================

    /// <inheritdoc/>
    public double FineStructureConstant => 1.0 / 137.0; // Simplified

    /// <inheritdoc/>
    public double StrongCouplingConstant => 0.12; // Rounded

    /// <inheritdoc/>
    public double WeakMixingAngle => 0.23; // Rounded

    // ============================================================
    // SIMULATION PARAMETERS (FITTED for visual stability)
    // ============================================================

    /// <summary>
    /// Gravitational coupling.
    /// <para><strong>Fitted:</strong> G = 0.1 (10% of physical, prevents instability)</para>
    /// </summary>
    public double GravitationalCoupling { get; set; } = 0.1;

    /// <summary>
    /// Cosmological constant.
    /// <para><strong>Fitted:</strong> ? = 10?? (visible effect on graph expansion)</para>
    /// </summary>
    public double CosmologicalConstant { get; set; } = 1.0e-4;

    /// <summary>
    /// Vacuum energy density.
    /// <para><strong>Fitted:</strong> ? = 1000 (drives Metropolis dynamics visibly)</para>
    /// </summary>
    public double VacuumEnergyDensity { get; set; } = 1000.0;

    /// <summary>
    /// Information flow rate.
    /// <para><strong>Fitted:</strong> v = 0.5c (synchronized with 60 FPS rendering)</para>
    /// </summary>
    public double InformationFlowRate { get; set; } = 0.5;

    /// <summary>
    /// Metric relaxation rate (GeometryInertia).
    /// <para><strong>Fitted:</strong> M = 10 (damping prevents oscillations)</para>
    /// </summary>
    public double MetricRelaxationRate { get; set; } = 10.0;

    // ============================================================
    // ADDITIONAL VISUAL PARAMETERS
    // ============================================================

    /// <summary>
    /// Edge weight lower soft wall.
    /// <para><strong>Purpose:</strong> Prevents edges from reaching zero weight.</para>
    /// </summary>
    public double WeightLowerSoftWall { get; set; } = 0.01;

    /// <summary>
    /// Edge weight upper soft wall.
    /// <para><strong>Purpose:</strong> Prevents runaway edge strengthening.</para>
    /// </summary>
    public double WeightUpperSoftWall { get; set; } = 2.0;

    /// <summary>
    /// Tanh saturation factor for bounded flows.
    /// <para><strong>Purpose:</strong> Limits velocity of geometry evolution.</para>
    /// </summary>
    public double TanhSaturationScale { get; set; } = 1.0;

    // ============================================================
    // INTERFACE IMPLEMENTATION
    // ============================================================

    /// <inheritdoc/>
    public string RescalingDocumentation => """
        FITTED CONSTANTS - VISUAL MODE DOCUMENTATION
        =============================================
        
        WARNING: These constants are tuned for visual stability, NOT physical accuracy.
        DO NOT use for scientific simulations or publishable results.
        
        FITTED VALUES:
        --------------
        Gravitational coupling:   G = 0.1 (physical: 1.0)
            Reason: Full G=1 causes vertex explosions on small graphs
        
        Cosmological constant:    ? = 10?? (physical: 10????)
            Reason: Physical ? is invisible; fitted ? shows expansion
        
        Vacuum energy density:    ? = 1000 (physical: 10????)
            Reason: Drives visible Metropolis dynamics
        
        Information flow rate:    v = 0.5c (physical: c)
            Reason: Synchronized with 60 FPS rendering pipeline
        
        Metric relaxation rate:   M = 10 (physical: 1)
            Reason: Damping prevents geometry oscillations
        
        SOFT WALLS:
        -----------
        Weight lower bound: 0.01 (prevents horizon formation)
        Weight upper bound: 2.0 (prevents runaway growth)
        
        These are NOT rescalable to physical units.
        Use StrictScienceProfile for scientific work.
        """;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, (double Value, string Documentation)> GetAllConstants()
    {
        return new Dictionary<string, (double, string)>
        {
            ["c"] = (C, "Speed of light"),
            ["hbar"] = (HBar, "Reduced Planck constant"),
            ["G"] = (G, "Gravitational constant"),
            ["k_B"] = (KBoltzmann, "Boltzmann constant"),
            ["alpha"] = (FineStructureConstant, "Fine structure constant (simplified)"),
            ["alpha_s"] = (StrongCouplingConstant, "Strong coupling (rounded)"),
            ["sin2_theta_W"] = (WeakMixingAngle, "Weak mixing angle (rounded)"),
            ["G_fitted"] = (GravitationalCoupling, "FITTED: Gravitational coupling"),
            ["Lambda_fitted"] = (CosmologicalConstant, "FITTED: Cosmological constant"),
            ["rho_vac_fitted"] = (VacuumEnergyDensity, "FITTED: Vacuum energy density"),
            ["v_info_fitted"] = (InformationFlowRate, "FITTED: Information flow rate"),
            ["M_metric_fitted"] = (MetricRelaxationRate, "FITTED: Metric relaxation rate"),
            ["w_min"] = (WeightLowerSoftWall, "FITTED: Weight lower soft wall"),
            ["w_max"] = (WeightUpperSoftWall, "FITTED: Weight upper soft wall"),
            ["tanh_scale"] = (TanhSaturationScale, "FITTED: Tanh saturation scale")
        };
    }

    /// <inheritdoc/>
    public ConstantBufferData GetConstantBuffer()
    {
        return new ConstantBufferData(
            c: C,
            hbar: HBar,
            g: G,
            kb: KBoltzmann,
            alpha: FineStructureConstant,
            alphaS: StrongCouplingConstant,
            sinThetaW: WeakMixingAngle,
            gGrav: GravitationalCoupling,
            lambda: CosmologicalConstant,
            rhoVac: VacuumEnergyDensity,
            vInfo: InformationFlowRate,
            mGeom: MetricRelaxationRate
        );
    }

    /// <summary>
    /// Resets all fitted parameters to their defaults.
    /// </summary>
    public void ResetToDefaults()
    {
        GravitationalCoupling = 0.1;
        CosmologicalConstant = 1.0e-4;
        VacuumEnergyDensity = 1000.0;
        InformationFlowRate = 0.5;
        MetricRelaxationRate = 10.0;
        WeightLowerSoftWall = 0.01;
        WeightUpperSoftWall = 2.0;
        TanhSaturationScale = 1.0;
    }
}
