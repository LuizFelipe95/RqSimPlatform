using System;

namespace RQSimulation
{
    /// <summary>
    /// RQ-Hypothesis Constants (Relational Quantum Hypothesis)
    /// 
    /// The RQ-Hypothesis proposes that spacetime emerges from quantum correlations
    /// on a dynamical graph. Key features:
    /// 
    /// 1. GEOMETRY IS QUANTUM: Edge weights represent quantum amplitudes
    /// 2. TOPOLOGY IS DYNAMIC: Graph structure evolves with physics
    /// 3. TIME IS RELATIONAL: No external clock; time emerges from correlations
    /// 4. DIMENSION EMERGES: 4D spacetime is energetically preferred
    /// 5. MATTER COUPLES TO GEOMETRY: Fields live on edges, backreact on graph
    /// 
    /// These constants control the experimental features that test the RQ-Hypothesis.
    /// </summary>
    public static partial class PhysicsConstants
    {
        // ============================================================
        // RQ-HYPOTHESIS CORE CONSTANTS
        // ============================================================

        /// <summary>
        /// Enable Hamiltonian Gravity (Symplectic Integration).
        /// When TRUE: Uses 2nd order dynamics with inertia (wave equation)
        /// When FALSE: Uses 1st order gradient descent (diffusion equation)
        /// 
        /// Physical motivation: Gravity should propagate, not just relax.
        /// </summary>
        public const bool UseHamiltonianGravity = true;

        /// <summary>
        /// Inertial mass of the geometry itself.
        /// Determines how resistant the metric is to changes from curvature forces.
        /// Larger values = slower geometry evolution.
        /// 
        /// Dimensional analysis: [M_geom] = [Energy ? Time?] = 1 in Planck units
        /// </summary>
        public const double GeometryInertiaMass = 10.0;

        /// <summary>
        /// Coupling constant for Yukawa interaction (Higgs-Fermion).
        /// Controls mass generation for fermions: m_f = y_f ? v
        /// where v is Higgs VEV.
        /// </summary>
        public const double YukawaCoupling = 0.5;

        /// <summary>
        /// Coupling constant for Topological Mass generation.
        /// Additional mass from topological defects (monopoles, vortices).
        /// </summary>
        public const double TopoMassCoupling = 0.1;

        /// <summary>
        /// Enable vacuum energy reservoir for RQ-Hypothesis.
        /// When TRUE: Vacuum energy is dynamic (conserved, can be borrowed)
        /// When FALSE: Vacuum energy is fixed (simplified model)
        /// </summary>
        public const bool EnableVacuumEnergyReservoir = true;

        /// <summary>
        /// Base rate for vacuum fluctuations = ?? (three-loop process).
        /// Vacuum pair creation requires energy ~ 2m_e, suppressed by ??.
        /// Physical: ?? ? 3.9 ? 10??
        /// </summary>
        public static readonly double VacuumFluctuationBaseRate =
            FineStructureConstant * FineStructureConstant * FineStructureConstant;

        /// <summary>
        /// Curvature enhancement for vacuum fluctuations = 4?.
        /// Near horizon, fluctuation rate ? surface area factor.
        /// </summary>
        public const double CurvatureCouplingFactor = 4.0 * Math.PI;

        /// <summary>
        /// Hawking radiation enhancement factor = 2.
        /// Pair creation: one particle escapes, one falls in.
        /// From Hawking's original calculation.
        /// </summary>
        public const double HawkingRadiationEnhancement = 2.0;

        /// <summary>
        /// Threshold for pair creation = 2 (two particles from vacuum).
        /// E_threshold = 2 ? m_Planck = 2 in natural units.
        /// </summary>
        public const double PairCreationEnergyThreshold = 2.0;

        // ============================================================
        // RQ-HYPOTHESIS VACUUM PARAMETERS
        // ============================================================

        /// <summary>Fraction of initial energy in vacuum pool.</summary>
        public const double InitialVacuumPoolFraction = 0.1;

        /// <summary>Initial vacuum energy (in Planck units).</summary>
        public const double InitialVacuumEnergy = 1000.0;

        /// <summary>
        /// Prefer Ollivier-Ricci curvature over Forman-Ricci.
        /// Ollivier-Ricci uses Sinkhorn optimal transport — more accurate on discrete graphs
        /// but O(support? ? iterations) per edge. Only enable when pipeline modules
        /// (OllivierRicciCpuModule, SinkhornOllivierRicciGpuModule) handle the heavy lifting,
        /// or when the simulation can tolerate slower per-edge curvature.
        ///
        /// Default: false (use fast Forman-Ricci in hot paths).
        /// Toggle from UI via "Prefer Ollivier-Ricci Curvature" checkbox.
        /// </summary>
        public static bool PreferOllivierRicciCurvature = false;

        /// <summary>
        /// Minimum edge weight before topological transition (edge removal).
        /// In Planck units, this is the "Planck scale" for geometry.
        /// </summary>
        public const double PlanckWeightThreshold = 0.01;

        /// <summary>Energy cost to create a new edge (pair creation).</summary>
        public const double EdgeCreationCost = 0.1;

        /// <summary>
        /// Effective mass for geometry momentum evolution.
        /// Same as GeometryInertiaMass for consistency.
        /// </summary>
        public const double GeometryMomentumMass = GeometryInertiaMass;

        // ============================================================
        // RQ-HYPOTHESIS EXPERIMENTAL FLAGS (Checklist Compliance)
        // ============================================================

        /// <summary>
        /// FIX #1: Natural Dimension Emergence
        /// ===================================
        /// When TRUE: Disable DimensionPenalty - allow spectral dimension
        /// to emerge naturally from Ricci curvature + matter coupling.
        /// 
        /// Use for TESTING whether 4D spacetime is dynamically preferred.
        /// If graph collapses (d_S ? ?) or fragments (d_S ? 1),
        /// the S_EH and S_matter balance is incorrect.
        /// 
        /// When FALSE: Use DimensionPenalty as scaffold toward 4D.
        /// </summary>
        public const bool EnableNaturalDimensionEmergence = true;

        /// <summary>
        /// FIX #2: Lapse-Synchronized Geometry
        /// ===================================
        /// When TRUE: Geometry evolution dt is scaled by edge lapse:
        ///   dt_edge = dt_global ? ?(N_i ? N_j)
        /// Near "black holes" (high entropy), geometry evolves slowly.
        /// 
        /// When FALSE: Geometry uses global dt everywhere.
        /// Simpler but can cause matter-geometry desynchronization.
        /// </summary>
        public const bool EnableLapseSynchronizedGeometry = true;

        /// <summary>
        /// FIX #3: Topological Parity for Fermions
        /// =======================================
        /// When TRUE: Use dynamic graph 2-coloring for staggered fermion parity
        /// instead of array index (i % 2). Background-independent.
        /// 
        /// When FALSE: Use simple i % 2 parity.
        /// Faster but violates background independence.
        /// 
        /// Enable for strict RQ-compliance (adds O(N) overhead per topology change).
        /// </summary>
        public const bool EnableTopologicalParity = false;

        /// <summary>
        /// FIX #4: Plaquette-Based Yang-Mills
        /// ==================================
        /// When TRUE: Use plaquette (triangle Wilson loop) for field strength.
        /// - Gauge-invariant by construction
        /// - Well-defined on arbitrary graph topology
        /// - Consistent with lattice QCD
        /// 
        /// When FALSE: Use neighbor-based curl (faster, less rigorous).
        /// </summary>
        public const bool EnablePlaquetteYangMills = false;

        /// <summary>
        /// FIX #5: Topology Energy Compensation
        /// ====================================
        /// When TRUE: Energy stored in fields on removed edges is captured
        /// and transferred to vacuum/radiation pool.
        /// Ensures strict energy conservation during topology changes.
        /// 
        /// When FALSE: Field energy on removed edges is lost (violates 1st law).
        /// </summary>
        public const bool EnableTopologyEnergyCompensation = true;

        // ============================================================
        // SYMPLECTIC YANG-MILLS DYNAMICS
        // Second-order wave equations for gauge fields
        // ============================================================

        /// <summary>
        /// Planck constant squared (?? = 1 in natural units).
        /// Used for quantum pressure: F = ??/(m ? w?)
        /// </summary>
        public const double PlanckConstantSqr = HBar * HBar;

        /// <summary>
        /// Effective inertial mass for U(1) gauge momentum.
        /// Controls oscillation frequency of electromagnetic field.
        /// </summary>
        public const double GaugeMomentumMassU1 = 1.0;

        /// <summary>
        /// Effective inertial mass for SU(2) weak gauge momentum.
        /// </summary>
        public const double GaugeMomentumMassSU2 = 1.0;

        /// <summary>
        /// Effective inertial mass for SU(3) gluon gauge momentum.
        /// </summary>
        public const double GaugeMomentumMassSU3 = 1.0;

        /// <summary>
        /// Damping coefficient for gauge field oscillations.
        /// 0 = pure Hamiltonian dynamics, >0 = dissipative.
        /// </summary>
        public const double GaugeFieldDamping = 0.001;

        /// <summary>
        /// Enable symplectic (second-order) gauge evolution.
        /// TRUE: Wave equation (photons propagate)
        /// FALSE: Diffusion equation (fields decay)
        /// </summary>
        public const bool EnableSymplecticGaugeEvolution = true;

        // ============================================================
        // ZENO EFFECT PREVENTION
        // Separation of time scales: T_topology >> T_quantum
        // ============================================================

        /// <summary>
        /// FIX #6: Topology Decoherence Interval
        /// =====================================
        /// Topology changes are "measurements" that decohere superpositions.
        /// Too frequent = Zeno effect (quantum evolution freezes).
        /// 
        /// This sets quantum steps between topology updates.
        /// Physical: quantum coherence ~ 10-100 steps, topology 10? slower.
        /// </summary>
        public const int TopologyDecoherenceInterval = 10;

        /// <summary>
        /// Enable adaptive topology update rate based on field amplitude.
        /// TRUE: P_flip ? exp(-|?|? / kT) - high amplitude = less flipping
        /// FALSE: Fixed interval from TopologyUpdateInterval
        /// </summary>
        public const bool EnableAdaptiveTopologyDecoherence = false;

        /// <summary>
        /// Base temperature for adaptive topology flip probability.
        /// Lower = more suppression of high-amplitude flips.
        /// </summary>
        public const double TopologyDecoherenceTemperature = 1.0;

        /// <summary>
        /// Minimum amplitude? below which topology flips are allowed.
        /// Edges with |?|? > this are "locked" by quantum coherence.
        /// </summary>
        public const double TopologyFlipAmplitudeThreshold = 0.1;

        // ============================================================
        // WILSON LOOPS GAUGE PROTECTION
        // ============================================================

        /// <summary>
        /// Tolerance for gauge phase to be considered "trivial".
        /// Phases < this can be removed without flux redistribution.
        /// 0.1 rad ? 6 degrees.
        /// </summary>
        public const double GaugeTolerance = 0.1;

        /// <summary>
        /// Enable strict Wilson loop protection for edge removal.
        /// TRUE: Edges with significant flux cannot be removed without redistribution
        /// FALSE: Legacy - flux can "disappear" (violates Gauss law)
        /// </summary>
        public const bool EnableWilsonLoopProtection = true;

        /// <summary>
        /// Maximum removable flux without explicit redistribution.
        /// ?/4 ? 45° corresponds to significant physical charge.
        /// </summary>
        public const double MaxRemovableFlux = Math.PI / 4.0;

        // ============================================================
        // SPECTRAL ACTION (Chamseddine-Connes Principle)
        // Dimension stabilization via spectral action
        // ============================================================

        /// <summary>
        /// Spectral Action Constants from Noncommutative Geometry.
        /// 
        /// The spectral action S = Tr(f(D/?)) expands as:
        ///   S = f? ?? ? ?g d?x                     (cosmological)
        ///     + f? ?? ? R ?g d?x                   (Einstein-Hilbert)
        ///     + f? ? (C_????)? ?g d?x              (Weyl curvature)
        ///     + ...
        /// 
        /// 4D spacetime emerges as energy minimum of this action.
        /// </summary>
        public static class SpectralActionConstants
        {
            /// <summary>UV cutoff scale (Planck scale in simulation).</summary>
            public const double LambdaCutoff = 1.0;

            /// <summary>
            /// f? coefficient: Cosmological constant term.
            /// S? = f? ? ?? ? V (volume term)
            /// </summary>
            public const double F0_Cosmological = 1.0;

            /// <summary>
            /// f? coefficient: Einstein-Hilbert term.
            /// S? = f? ? ?? ? ?R?g d?x (curvature term)
            /// </summary>
            public const double F2_EinsteinHilbert = 1.0;

            /// <summary>
            /// f? coefficient: Weyl curvature term.
            /// S? = f? ? ?(C_????)??g d?x
            /// </summary>
            public const double F4_Weyl = 0.5;

            /// <summary>
            /// Target spectral dimension for energy minimum.
            /// 4D emerges naturally from these coefficients.
            /// </summary>
            public const double TargetSpectralDimension = 4.0;

            /// <summary>
            /// Coupling strength for dimension stabilization potential.
            /// </summary>
            public const double DimensionPotentialStrength = 0.1;

            /// <summary>
            /// Width parameter for Mexican hat potential minima.
            /// </summary>
            public const double DimensionPotentialWidth = 1.0;

            /// <summary>
            /// Enable strict spectral action mode.
            /// TRUE: Use spectral action instead of DimensionPenalty
            /// FALSE: Fall back to legacy dimension penalty
            /// </summary>
            public const bool EnableSpectralActionMode = true;
        }

        // ============================================================
        // WHEELER-DEWITT CONSTRAINT
        // Hamiltonian constraint: H_total ? 0
        // ============================================================

        /// <summary>
        /// Wheeler-DeWitt Constraint Constants.
        /// 
        /// In quantum gravity, H generates diffeomorphisms (gauge transformations).
        /// Physical states satisfy H|?? = 0 (Wheeler-DeWitt equation).
        /// Time emerges from correlations, not external clock.
        /// 
        /// Implementation:
        /// - H_total = H_gravity + ? ? H_matter ? 0
        /// - Violations are penalized or rejected
        /// </summary>
        public static class WheelerDeWittConstants
        {
            /// <summary>
            /// Gravitational coupling (? = 8?G in natural units).
            /// Controls matter-geometry coupling in constraint.
            /// </summary>
            public const double GravitationalCoupling = 0.1;

            /// <summary>
            /// Lagrange multiplier for constraint enforcement.
            /// Higher = stricter H ? 0 enforcement.
            /// </summary>
            public const double ConstraintLagrangeMultiplier = 10.0;

            /// <summary>
            /// Tolerance for considering constraint satisfied.
            /// |H_total| < tolerance means configuration is physical.
            /// </summary>
            public const double ConstraintTolerance = 0.01;

            /// <summary>
            /// Enable strict Wheeler-DeWitt mode.
            /// TRUE: External energy injection forbidden, H_total ? 0 enforced
            /// FALSE: Legacy mode with vacuum reservoir
            /// </summary>
            public const bool EnableStrictMode = false;

            /// <summary>
            /// Enable constraint violation logging for diagnostics.
            /// </summary>
            public const bool EnableViolationLogging = true;

            /// <summary>
            /// Maximum allowed constraint violation before rejecting move.
            /// </summary>
            public const double MaxAllowedViolation = 1.0;
        }
    }
}
