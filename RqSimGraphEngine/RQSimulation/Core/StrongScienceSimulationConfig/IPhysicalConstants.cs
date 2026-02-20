// ============================================================
// IPhysicalConstants.cs
// Strategy pattern interface for physical constants providers
// Part of Strong Science Simulation Config architecture
// ============================================================

namespace RQSimulation.Core.StrongScience;

/// <summary>
/// Strategy interface for physical constants providers.
/// <para><strong>DEPENDENCY INJECTION PATTERN:</strong></para>
/// <para>
/// Physics kernels should NEVER access <c>PhysicsConstants.Fitted.*</c> directly.
/// Instead, constants are injected via this interface, allowing:
/// </para>
/// <list type="bullet">
///   <item><description>Runtime switching between constant sets</description></item>
///   <item><description>Clean separation of fundamental vs fitted values</description></item>
///   <item><description>Shader constant buffer (cbuffer) population</description></item>
///   <item><description>Unit testing with custom constants</description></item>
/// </list>
/// 
/// <para><strong>UNITLESS LATTICE UNITS:</strong></para>
/// <para>
/// In lattice physics, we set c=?=G=k_B=a=1 (Planck units + lattice spacing).
/// All "magic numbers" in simulation are rescaling factors:
/// </para>
/// <code>
/// Value_sim = Value_real ? ScaleFactor
/// </code>
/// <para>
/// The <see cref="RescalingDocumentation"/> property provides formulas for
/// converting between simulation units and physical SI units.
/// </para>
/// </summary>
public interface IPhysicalConstants
{
    /// <summary>
    /// Human-readable name for this constants set.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description explaining the physical basis for these constants.
    /// </summary>
    string Description { get; }

    // ============================================================
    // FUNDAMENTAL CONSTANTS (Planck units: c = ? = G = k_B = 1)
    // ============================================================

    /// <summary>Speed of light. In Planck units: c = 1.</summary>
    double C { get; }

    /// <summary>Reduced Planck constant. In Planck units: ? = 1.</summary>
    double HBar { get; }

    /// <summary>Gravitational constant. In Planck units: G = 1.</summary>
    double G { get; }

    /// <summary>Boltzmann constant. In Planck units: k_B = 1.</summary>
    double KBoltzmann { get; }

    /// <summary>Planck length (fundamental length scale).</summary>
    double PlanckLength { get; }

    /// <summary>Planck time (fundamental time scale).</summary>
    double PlanckTime { get; }

    /// <summary>Planck mass (fundamental mass scale).</summary>
    double PlanckMass { get; }

    /// <summary>Planck energy (fundamental energy scale).</summary>
    double PlanckEnergy { get; }

    // ============================================================
    // GAUGE COUPLING CONSTANTS (dimensionless)
    // ============================================================

    /// <summary>
    /// Fine structure constant ? = e?/(4????c) ? 1/137.036
    /// </summary>
    double FineStructureConstant { get; }

    /// <summary>
    /// Strong coupling constant ?_s(M_Z) ? 0.118
    /// </summary>
    double StrongCouplingConstant { get; }

    /// <summary>
    /// Electroweak mixing angle sin??_W ? 0.231
    /// </summary>
    double WeakMixingAngle { get; }

    // ============================================================
    // SIMULATION PARAMETERS
    // ============================================================

    /// <summary>
    /// Gravitational coupling for simulation.
    /// <para><strong>Physical:</strong> G = 1 (Planck units)</para>
    /// <para><strong>Fitted:</strong> G ? 0.1 (prevents instability on small graphs)</para>
    /// </summary>
    double GravitationalCoupling { get; }

    /// <summary>
    /// Cosmological constant ?.
    /// <para><strong>Physical:</strong> ? ? 1e-122 (tiny, nearly zero)</para>
    /// <para><strong>Fitted:</strong> ? ? 1e-4 (visible effect on visualization)</para>
    /// </summary>
    double CosmologicalConstant { get; }

    /// <summary>
    /// Vacuum energy density.
    /// <para><strong>Physical:</strong> ?_vac ? 1e-27 kg/m?</para>
    /// <para><strong>Fitted:</strong> ?_vac ? 1000 (drives Metropolis dynamics)</para>
    /// </summary>
    double VacuumEnergyDensity { get; }

    /// <summary>
    /// Information flow rate (causality constraint).
    /// <para><strong>Physical:</strong> v ? c = 1</para>
    /// <para><strong>Fitted:</strong> v ? 0.5 (synchronizes with CPU tick rate)</para>
    /// </summary>
    double InformationFlowRate { get; }

    /// <summary>
    /// Geometry inertia / Metric relaxation rate.
    /// <para><strong>Physical interpretation:</strong> Hamiltonian momentum term mass</para>
    /// <para><strong>Fitted:</strong> M_geom ? 10 (damping for stable visualization)</para>
    /// <para><strong>Scientific name:</strong> MetricRelaxationRate or HamiltonianMomentumTerm</para>
    /// </summary>
    double MetricRelaxationRate { get; }

    // ============================================================
    // RESCALING DOCUMENTATION
    // ============================================================

    /// <summary>
    /// Documentation for unit rescaling formulas.
    /// <para>Contains formulas for converting between simulation and physical units:</para>
    /// <code>
    /// Length: L_phys = L_sim ? (1.616e-35 m)
    /// Time:   t_phys = t_sim ? (5.391e-44 s)
    /// Mass:   m_phys = m_sim ? (2.176e-8 kg)
    /// Energy: E_phys = E_sim ? (1.956e9 J)
    /// </code>
    /// </summary>
    string RescalingDocumentation { get; }

    /// <summary>
    /// Gets a dictionary of all constants for serialization or cbuffer population.
    /// <para>Keys are constant names, values are (value, documentation) tuples.</para>
    /// </summary>
    IReadOnlyDictionary<string, (double Value, string Documentation)> GetAllConstants();

    /// <summary>
    /// Gets constants formatted for GPU constant buffer (cbuffer).
    /// <para>Returns a float4-aligned struct suitable for HLSL cbuffer.</para>
    /// </summary>
    ConstantBufferData GetConstantBuffer();
}

/// <summary>
/// GPU-aligned constant buffer data structure.
/// <para>Aligned to float4 boundaries for HLSL cbuffer compatibility.</para>
/// </summary>
public readonly struct ConstantBufferData
{
    // float4[0]: Fundamental constants
    public readonly float C;
    public readonly float HBar;
    public readonly float G_Newton;
    public readonly float KBoltzmann;

    // float4[1]: Coupling constants
    public readonly float FineStructure;
    public readonly float StrongCoupling;
    public readonly float WeakMixing;
    public readonly float GravitationalCoupling;

    // float4[2]: Simulation parameters
    public readonly float CosmologicalConstant;
    public readonly float VacuumEnergyDensity;
    public readonly float InformationFlowRate;
    public readonly float MetricRelaxationRate;

    public ConstantBufferData(
        double c, double hbar, double g, double kb,
        double alpha, double alphaS, double sinThetaW, double gGrav,
        double lambda, double rhoVac, double vInfo, double mGeom)
    {
        C = (float)c;
        HBar = (float)hbar;
        G_Newton = (float)g;
        KBoltzmann = (float)kb;
        FineStructure = (float)alpha;
        StrongCoupling = (float)alphaS;
        WeakMixing = (float)sinThetaW;
        GravitationalCoupling = (float)gGrav;
        CosmologicalConstant = (float)lambda;
        VacuumEnergyDensity = (float)rhoVac;
        InformationFlowRate = (float)vInfo;
        MetricRelaxationRate = (float)mGeom;
    }

    /// <summary>
    /// Size in bytes (3 ? float4 = 48 bytes).
    /// </summary>
    public static int SizeInBytes => 48;
}
