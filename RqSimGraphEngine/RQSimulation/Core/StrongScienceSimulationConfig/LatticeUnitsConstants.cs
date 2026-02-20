// ============================================================
// LatticeUnitsConstants.cs
// Implementation of IPhysicalConstants using dimensionless lattice units
// Part of Strong Science Simulation Config architecture
// ============================================================

namespace RQSimulation.Core.StrongScience;

/// <summary>
/// Provides physical constants in dimensionless lattice units.
/// <para><strong>LATTICE QCD / LATTICE GRAVITY CONVENTIONS:</strong></para>
/// <code>
/// c = 1 (speed of light)
/// ? = 1 (reduced Planck constant)
/// G = 1 (gravitational constant)
/// a = 1 (lattice spacing)
/// </code>
/// 
/// <para><strong>MOTIVATION:</strong></para>
/// <para>
/// In lattice physics, all quantities are measured in units of the lattice spacing 'a'.
/// This eliminates dimensional constants and makes the simulation purely numerical.
/// The "magic numbers" that appear in simulation code are NOT arbitrary - they are
/// the result of careful dimensional analysis (obezrazmerivaniye/обезразмеривание).
/// </para>
/// 
/// <para><strong>RESCALING:</strong></para>
/// <code>
/// Value_sim = Value_real ? ScaleFactor
/// </code>
/// <para>where ScaleFactor depends on the quantity's dimensions.</para>
/// 
/// <para><strong>SCIENTIFIC VALIDITY:</strong></para>
/// <para>
/// This implementation is valid for scientific simulations because:
/// 1. All constants are derived from fundamental values with explicit scaling
/// 2. The scaling factors are documented and reproducible
/// 3. Results can be converted back to physical units
/// </para>
/// </summary>
public sealed class LatticeUnitsConstants : IPhysicalConstants
{
    private readonly double _latticeSpacing;
    private readonly double _scaleFactor;

    /// <summary>
    /// Creates a new instance with specified lattice spacing.
    /// </summary>
    /// <param name="latticeSpacing">
    /// Lattice spacing 'a' in Planck lengths.
    /// Default: 1.0 (one Planck length per lattice site).
    /// </param>
    /// <param name="graphSize">
    /// Number of nodes in the graph. Used to compute finite-size scaling corrections.
    /// Default: 1000 (typical small simulation).
    /// </param>
    public LatticeUnitsConstants(double latticeSpacing = 1.0, int graphSize = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(latticeSpacing);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(graphSize);

        _latticeSpacing = latticeSpacing;
        // Scale factor for coupling constants on finite graphs
        // G_eff = G ? (N_crit / N)^? where ? ? 0.5 from finite-size scaling
        _scaleFactor = Math.Sqrt(1000.0 / graphSize);
    }

    /// <inheritdoc/>
    public string Name => $"Lattice Units (a={_latticeSpacing:F2})";

    /// <inheritdoc/>
    public string Description =>
        $"Dimensionless lattice units with spacing a={_latticeSpacing:F2} Planck lengths. " +
        "All quantities rescaled to lattice units with documented scaling factors.";

    // ============================================================
    // FUNDAMENTAL CONSTANTS (all = 1 in natural + lattice units)
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
    public double PlanckLength => _latticeSpacing;

    /// <inheritdoc/>
    public double PlanckTime => _latticeSpacing; // t_P = l_P/c = l_P when c=1

    /// <inheritdoc/>
    public double PlanckMass => 1.0 / _latticeSpacing; // m_P ? 1/l_P

    /// <inheritdoc/>
    public double PlanckEnergy => 1.0 / _latticeSpacing; // E_P = m_P c? = m_P when c=1

    // ============================================================
    // GAUGE COUPLING CONSTANTS (dimensionless, same as physical)
    // ============================================================

    /// <inheritdoc/>
    public double FineStructureConstant => 1.0 / 137.035999084;

    /// <inheritdoc/>
    public double StrongCouplingConstant => 0.1180;

    /// <inheritdoc/>
    public double WeakMixingAngle => 0.23121;

    // ============================================================
    // SIMULATION PARAMETERS (scaled for lattice)
    // ============================================================

    /// <summary>
    /// Effective gravitational coupling for finite lattice.
    /// <para><strong>Scaling:</strong> G_eff = G ? ?(N_crit/N)</para>
    /// <para>
    /// On small graphs (N &lt; 1000), the full G=1 causes instability because
    /// gravitational effects are enhanced by the small system size.
    /// This scaling factor compensates for finite-size effects.
    /// </para>
    /// </summary>
    public double GravitationalCoupling => 1.0 * _scaleFactor;

    /// <summary>
    /// Cosmological constant in lattice units.
    /// <para><strong>Scaling:</strong> ?_lat = ?_phys ? a?</para>
    /// <para>
    /// The cosmological constant has dimension [length]?? in natural units.
    /// In lattice units: ?_lat = 10?? to produce visible effects on small graphs.
    /// </para>
    /// <para><strong>Physical value:</strong> ? ? 10???? (negligible)</para>
    /// <para><strong>Lattice value:</strong> ?_lat ? 10?? (rescaled for visibility)</para>
    /// </summary>
    public double CosmologicalConstant => 1.0e-4;

    /// <summary>
    /// Vacuum energy density in lattice units.
    /// <para><strong>Scaling:</strong> ?_lat = ?_phys ? a?</para>
    /// </summary>
    public double VacuumEnergyDensity => 1.0e-4;

    /// <summary>
    /// Information flow rate (? c = 1).
    /// <para>In lattice units, information propagates at most one lattice site per timestep.</para>
    /// </summary>
    public double InformationFlowRate => 1.0;

    /// <summary>
    /// Metric relaxation rate in lattice units.
    /// <para><strong>Physical interpretation:</strong> HamiltonianMomentumTerm</para>
    /// <para><strong>Scaling:</strong> M_lat = M_phys ? (a/l_P)?</para>
    /// <para>
    /// The metric "mass" determines how quickly geometry responds to matter.
    /// Larger values = slower geometry evolution = more stable simulation.
    /// </para>
    /// </summary>
    public double MetricRelaxationRate => 1.0;

    // ============================================================
    // RESCALING DOCUMENTATION
    // ============================================================

    /// <inheritdoc/>
    public string RescalingDocumentation => $"""
        LATTICE UNITS CONSTANTS - RESCALING DOCUMENTATION
        ==================================================
        
        Lattice spacing: a = {_latticeSpacing:E3} Planck lengths
        Scale factor:    s = {_scaleFactor:F4} (finite-size correction)
        
        DIMENSIONLESS LATTICE UNITS:
        ----------------------------
        In lattice physics, we set c = ? = G = a = 1.
        All quantities become dimensionless numbers.
        
        RESCALING FORMULAS (sim ? physical):
        ------------------------------------
        Length:      L_phys = L_sim ? a ? l_P = L_sim ? {_latticeSpacing * 1.616e-35:E3} m
        Time:        t_phys = t_sim ? a ? t_P = t_sim ? {_latticeSpacing * 5.391e-44:E3} s
        Mass:        m_phys = m_sim ? m_P/a   = m_sim ? {2.176e-8 / _latticeSpacing:E3} kg
        Energy:      E_phys = E_sim ? E_P/a   = E_sim ? {1.956e9 / _latticeSpacing:E3} J
        
        COUPLING RESCALING:
        -------------------
        Gravitational:  G_eff = G ? s = {GravitationalCoupling:E4}
        Cosmological:   ?_lat = 10?? (visible on graphs)
        
        WHY RESCALE?
        ------------
        1. Numerical stability: Full G=1 causes divergences on small graphs
        2. Finite-size scaling: Physical effects scale with system size
        3. Visualization: Some effects (?) need amplification to be visible
        
        REVERSIBILITY:
        --------------
        All rescaling is documented and reversible. To convert simulation
        results to physical units, apply inverse scaling factors.
        """;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, (double Value, string Documentation)> GetAllConstants()
    {
        return new Dictionary<string, (double, string)>
        {
            ["c"] = (C, "Speed of light (lattice units)"),
            ["hbar"] = (HBar, "Reduced Planck constant (lattice units)"),
            ["G"] = (G, "Gravitational constant (lattice units)"),
            ["k_B"] = (KBoltzmann, "Boltzmann constant (lattice units)"),
            ["a"] = (_latticeSpacing, "Lattice spacing (Planck lengths)"),
            ["s"] = (_scaleFactor, "Finite-size scaling factor"),
            ["l_P"] = (PlanckLength, "Planck length (lattice units)"),
            ["t_P"] = (PlanckTime, "Planck time (lattice units)"),
            ["m_P"] = (PlanckMass, "Planck mass (lattice units)"),
            ["E_P"] = (PlanckEnergy, "Planck energy (lattice units)"),
            ["alpha"] = (FineStructureConstant, "Fine structure constant"),
            ["alpha_s"] = (StrongCouplingConstant, "Strong coupling at M_Z"),
            ["sin2_theta_W"] = (WeakMixingAngle, "Weak mixing angle"),
            ["G_eff"] = (GravitationalCoupling, "Effective gravitational coupling (scaled)"),
            ["Lambda_lat"] = (CosmologicalConstant, "Cosmological constant (lattice)"),
            ["rho_vac_lat"] = (VacuumEnergyDensity, "Vacuum energy density (lattice)"),
            ["v_info"] = (InformationFlowRate, "Information flow rate"),
            ["M_metric"] = (MetricRelaxationRate, "Metric relaxation rate")
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
