// ============================================================
// PlanckScaleConstants.cs
// Implementation of IPhysicalConstants using fundamental Planck-scale values
// Part of Strong Science Simulation Config architecture
// ============================================================

namespace RQSimulation.Core.StrongScience;

/// <summary>
/// Provides fundamental physical constants in natural Planck units.
/// <para><strong>SCIENTIFIC MODE ONLY:</strong></para>
/// <para>
/// This implementation provides ONLY fundamental constants from CODATA 2022.
/// No fitted/tuned values are included. Using this with a scientific simulation
/// guarantees that results are not contaminated by visualization-oriented parameters.
/// </para>
/// 
/// <para><strong>PLANCK UNITS:</strong></para>
/// <code>
/// c = 1 (speed of light)
/// ? = 1 (reduced Planck constant)
/// G = 1 (gravitational constant)
/// k_B = 1 (Boltzmann constant)
/// </code>
/// 
/// <para><strong>DERIVED SCALES:</strong></para>
/// <code>
/// l_P = ?(?G/c?) = 1.616 ? 10??? m ? 1
/// t_P = ?(?G/c?) = 5.391 ? 10??? s ? 1
/// m_P = ?(?c/G)  = 2.176 ? 10?? kg ? 1
/// E_P = m_P c?   = 1.956 ? 10? J  ? 1
/// T_P = E_P/k_B  = 1.417 ? 10?? K ? 1
/// </code>
/// </summary>
public sealed class PlanckScaleConstants : IPhysicalConstants
{
    /// <inheritdoc/>
    public string Name => "Planck Scale (Fundamental)";

    /// <inheritdoc/>
    public string Description =>
        "Fundamental physical constants in natural Planck units (c=?=G=k_B=1). " +
        "CODATA 2022 values for dimensionless coupling constants. " +
        "No fitted parameters - suitable for scientific simulations.";

    // ============================================================
    // FUNDAMENTAL CONSTANTS (Planck units: c = ? = G = k_B = 1)
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
    // GAUGE COUPLING CONSTANTS (CODATA 2022)
    // ============================================================

    /// <summary>
    /// Fine structure constant ? = 1/137.035999084(21)
    /// CODATA 2022 recommended value.
    /// </summary>
    public double FineStructureConstant => 1.0 / 137.035999084;

    /// <summary>
    /// Strong coupling constant ?_s(M_Z) = 0.1180 ± 0.0009
    /// PDG 2022 value at Z mass scale.
    /// </summary>
    public double StrongCouplingConstant => 0.1180;

    /// <summary>
    /// Electroweak mixing angle sin??_W = 0.23121 ± 0.00004
    /// PDG 2022 MS-bar scheme at M_Z.
    /// </summary>
    public double WeakMixingAngle => 0.23121;

    // ============================================================
    // SIMULATION PARAMETERS (Physical values)
    // ============================================================

    /// <summary>
    /// Gravitational coupling G = 1 in Planck units.
    /// <para><strong>WARNING:</strong> Using G=1 on small graphs (N&lt;1000) may cause
    /// numerical instability. Consider using <see cref="LatticeUnitsConstants"/> 
    /// for finite-size effects.</para>
    /// </summary>
    public double GravitationalCoupling => 1.0;

    /// <summary>
    /// Cosmological constant ? ? 1.1 ? 10???? (in Planck units).
    /// <para>This is the PHYSICAL value - effectively zero for simulation purposes.</para>
    /// <para>The "cosmological constant problem" is that ?_measured ? ?_predicted.</para>
    /// </summary>
    public double CosmologicalConstant => 1.1e-122;

    /// <summary>
    /// Physical vacuum energy density ?_vac ? 5.96 ? 10???? in Planck units.
    /// <para>Derived from ?: ?_vac = ? c?/(8?G)</para>
    /// </summary>
    public double VacuumEnergyDensity => 5.96e-127;

    /// <summary>
    /// Information flow rate = c = 1 (causality constraint).
    /// </summary>
    public double InformationFlowRate => 1.0;

    /// <summary>
    /// Metric relaxation rate / Hamiltonian momentum term mass.
    /// <para><strong>Physical interpretation:</strong> In ADM formalism, the metric
    /// momentum is ?^{ij} = (?L/??_{ij}). The "mass" here is the coefficient
    /// in the kinetic term: T = (1/2M) ?^{ij} ?_{ij}</para>
    /// <para><strong>Physical value:</strong> M = 1 in Planck units (c=G=?=1).</para>
    /// </summary>
    public double MetricRelaxationRate => 1.0;

    // ============================================================
    // RESCALING DOCUMENTATION
    // ============================================================

    /// <inheritdoc/>
    public string RescalingDocumentation => """
        PLANCK SCALE CONSTANTS - UNIT CONVERSION
        =========================================
        
        This constants set uses natural Planck units where c = ? = G = k_B = 1.
        All dimensionful quantities are measured in Planck units.
        
        CONVERSION TO SI UNITS:
        -----------------------
        Length:      L_SI = L_sim ? 1.616255 ? 10??? m
        Time:        t_SI = t_sim ? 5.391247 ? 10??? s
        Mass:        m_SI = m_sim ? 2.176434 ? 10?? kg
        Energy:      E_SI = E_sim ? 1.956086 ? 10? J
        Temperature: T_SI = T_sim ? 1.416785 ? 10?? K
        
        DIMENSIONLESS CONSTANTS (CODATA 2022):
        --------------------------------------
        Fine structure:    ? = 1/137.035999084(21)
        Strong coupling:   ?_s(M_Z) = 0.1180(9)
        Weak mixing:       sin??_W = 0.23121(4)
        
        MASS RATIOS (PDG 2022):
        -----------------------
        Electron:  m_e/m_P = 4.1855 ? 10???
        Proton:    m_p/m_P = 7.685 ? 10???
        W boson:   M_W/m_P = 6.58 ? 10???
        Z boson:   M_Z/m_P = 7.47 ? 10???
        Higgs:     M_H/m_P = 1.02 ? 10???
        """;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, (double Value, string Documentation)> GetAllConstants()
    {
        return new Dictionary<string, (double, string)>
        {
            ["c"] = (C, "Speed of light (Planck units)"),
            ["hbar"] = (HBar, "Reduced Planck constant (Planck units)"),
            ["G"] = (G, "Gravitational constant (Planck units)"),
            ["k_B"] = (KBoltzmann, "Boltzmann constant (Planck units)"),
            ["l_P"] = (PlanckLength, "Planck length"),
            ["t_P"] = (PlanckTime, "Planck time"),
            ["m_P"] = (PlanckMass, "Planck mass"),
            ["E_P"] = (PlanckEnergy, "Planck energy"),
            ["alpha"] = (FineStructureConstant, "Fine structure constant (CODATA 2022)"),
            ["alpha_s"] = (StrongCouplingConstant, "Strong coupling at M_Z (PDG 2022)"),
            ["sin2_theta_W"] = (WeakMixingAngle, "Weak mixing angle sin??_W (PDG 2022)"),
            ["G_coupling"] = (GravitationalCoupling, "Gravitational coupling (physical)"),
            ["Lambda"] = (CosmologicalConstant, "Cosmological constant (physical)"),
            ["rho_vac"] = (VacuumEnergyDensity, "Vacuum energy density (physical)"),
            ["v_info"] = (InformationFlowRate, "Information flow rate (causality)"),
            ["M_metric"] = (MetricRelaxationRate, "Metric relaxation rate (ADM mass)")
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
}
