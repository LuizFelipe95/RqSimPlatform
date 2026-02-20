using System;

namespace RQSimulation
{
    /// <summary>
    /// Simulation parameters for the RQ Graph Engine.
    /// 
    /// PARAMETER CATEGORIZATION:
    /// =========================
    /// 1. PHYSICAL: Derived from fundamental constants or experiments
    /// 2. NUMERICAL: Required for stable simulation (timesteps, tolerances)
    /// 3. HEURISTIC: Tuned empirically for good simulation behavior
    /// 
    /// All parameters include documentation of their origin and physical interpretation.
    /// </summary>
    public static partial class PhysicsConstants
    {
        // ============================================================
        // FIXED-POINT ARITHMETIC LIMITS (Safety Bounds for GPU Integers)
        // ============================================================
        // HARD SCIENCE AUDIT v3.2 - SATURATING ARITHMETIC
        // These constants define boundaries for safe fixed-point operations
        // to prevent silent integer overflow in GPU conservation kernels.
        // ============================================================

        /// <summary>
        /// Fixed-point arithmetic limits for GPU integer conservation.
        /// <para><strong>HARD SCIENCE AUDIT v3.2:</strong> Prevents silent integer overflow.</para>
        /// </summary>
        public static class FixedPointLimits
        {
            /// <summary>
            /// Fixed-point scale factor: 2^24 = 16,777,216.
            /// <para>Provides ~7 decimal digits of precision for edge weights and node masses.</para>
            /// <para>Example: 1.0 physical energy = 16,777,216 in fixed-point.</para>
            /// </summary>
            public const int ENERGY_SCALE = 16777216;

            /// <summary>
            /// Safe maximum for int32 accumulators: ~95% of Int32.MaxValue.
            /// <para>Leaves headroom for transient fluctuations during CAS loops.</para>
            /// <para>Value: 2,000,000,000 (vs Int32.MaxValue = 2,147,483,647)</para>
            /// </summary>
            public const int MAX_SAFE_ACCUMULATOR = 2000000000;

            /// <summary>
            /// Maximum physical energy per node before fixed-point saturation.
            /// <para>MAX_SAFE_ACCUMULATOR / ENERGY_SCALE ? 119.2 energy units.</para>
            /// <para>Exceeding this triggers saturation and sets overflow flag.</para>
            /// </summary>
            public const double MAX_PHYSICAL_ENERGY = (double)MAX_SAFE_ACCUMULATOR / ENERGY_SCALE;

            /// <summary>
            /// Minimum value for int32 accumulators (symmetric to MAX_SAFE_ACCUMULATOR).
            /// </summary>
            public const int MIN_SAFE_ACCUMULATOR = -2000000000;

            /// <summary>
            /// Int32 maximum value for overflow detection.
            /// </summary>
            public const int INT32_MAX = 2147483647;

            /// <summary>
            /// Int32 minimum value for underflow detection.
            /// </summary>
            public const int INT32_MIN = -2147483648;
        }

        /// <summary>
        /// Integrity flags for GPU-to-CPU communication of critical states.
        /// <para>These flags are set atomically by GPU kernels and read by CPU validators.</para>
        /// </summary>
        public static class IntegrityFlags
        {
            /// <summary>No integrity issues detected.</summary>
            public const int FLAG_OK = 0;

            /// <summary>Fixed-point overflow detected during atomic add.</summary>
            public const int FLAG_OVERFLOW_DETECTED = 1;

            /// <summary>Fixed-point underflow detected during atomic add.</summary>
            public const int FLAG_UNDERFLOW_DETECTED = 2;

            /// <summary>TDR (Timeout Detection and Recovery) truncated computation.</summary>
            public const int FLAG_TDR_TRUNCATION = 4;

            /// <summary>Energy conservation violation exceeds tolerance.</summary>
            public const int FLAG_CONSERVATION_VIOLATION = 8;

            /// <summary>NaN or Infinity detected in critical buffer.</summary>
            public const int FLAG_NAN_DETECTED = 16;

            /// <summary>64-bit accumulator overflow (global audit).</summary>
            public const int FLAG_64BIT_OVERFLOW = 32;
        }

        // ============================================================
        // SCIENCE MODE SETTINGS
        // ============================================================

        /// <summary>
        /// If true, uses strict scientific validation mode.
        /// If false, operates in "visual sandbox" mode with relaxed constraints.
        /// </summary>
        public static bool ScientificMode = true;

        /// <summary>
        /// If true, uses GPU-accelerated EdgeAnisotropyKernel for per-node anisotropy.
        /// If false, uses CPU-based calculation.
        /// </summary>
        public static bool UseGpuEdgeAnisotropy = true;

        // ============================================================
        // CLUSTER DYNAMICS (Heuristic - tuned for graph coherence)
        // ============================================================

        /// <summary>
        /// Temperature for cluster stabilization in Metropolis algorithm.
        /// Lower values = more deterministic, higher = more stochastic.
        /// Typical range: [0.01, 0.5]. Default chosen for stable clustering.
        /// </summary>
        public const double ClusterStabilizationTemperature = 0.05;

        /// <summary>
        /// Number of Metropolis trials per cluster per step.
        /// More trials = better equilibration but slower.
        /// </summary>
        public const int MetropolisTrialsPerCluster = 10;

        /// <summary>
        /// Threshold correlation above which edges are considered over-correlated.
        /// Used to trigger decoherence mechanisms. Range: [0.5, 0.99]
        /// </summary>
        public const double OvercorrelationThreshold = 0.9;

        /// <summary>
        /// Decay factor for external (cross-cluster) connections.
        /// Applied per step: w_new = w_old * decay. Value < 1 means decay.
        /// </summary>
        public const double ExternalConnectionDecay = 0.99;

        // ============================================================
        // IMPULSE AND EXCITATION (Heuristic - controls signal propagation)
        // ============================================================

        /// <summary>Radius (in hops) for impulse cascade propagation.</summary>
        public const int ImpulseCascadeRadius = 4;

        /// <summary>
        /// Gain factor for phase-resonant excitation transfer.
        /// Derived from quantum mechanics: probability amplitude ~ 0.5 for resonance.
        /// </summary>
        public const double PhaseResonantGain = 0.5;

        /// <summary>Boost factor for impulse-triggered potential changes.</summary>
        public const double ImpulsePotentialBoost = 0.3;

        /// <summary>Energy boost from impulse events.</summary>
        public const double ImpulseEnergyBoost = 0.15;

        /// <summary>
        /// Threshold for phase coherence detection.
        /// cos(??) > threshold means phases are coherent.
        /// </summary>
        public const double PhaseCoherenceThreshold = 0.5;

        // ============================================================
        // UPDATE FREQUENCIES (Numerical - controls simulation fidelity)
        // ============================================================

        /// <summary>
        /// Steps between topology updates (edge flips).
        /// Higher = less quantum decoherence from measurement (Zeno effect).
        /// Physical motivation: topology changes are "measurements".
        /// </summary>
        public const int TopologyUpdateInterval = 5;

        /// <summary>
        /// Divisor for number of edge flips per update: flips = N / divisor.
        /// Controls topology change rate relative to graph size.
        /// </summary>
        public const int TopologyFlipsDivisor = 20;

        /// <summary>
        /// Steps between geometry updates. 1 = every step for smooth evolution.
        /// Geometry (edge weights) should evolve continuously.
        /// </summary>
        public const int GeometryUpdateInterval = 1;

        /// <summary>
        /// Steps between gauge constraint enforcement (Gauss's law).
        /// Too frequent = Zeno effect; too rare = constraint violation drift.
        /// </summary>
        public const int GaugeConstraintInterval = 5;

        /// <summary>
        /// Steps between energy conservation validation checks.
        /// Used for diagnostics and auto-tuning.
        /// </summary>
        public const int EnergyValidationInterval = 20;

        /// <summary>
        /// Steps between topological protection (Betti number) updates.
        /// </summary>
        public const int TopologicalProtectionInterval = 50;

        // ============================================================
        // TOPOLOGICAL PROTECTION (Physical + Heuristic)
        // ============================================================

        /// <summary>
        /// Weight threshold for topologically protected edges.
        /// Edges above this are less likely to be removed.
        /// </summary>
        public const double TopologicalProtectionThreshold = 0.6;

        /// <summary>
        /// Strength of topological protection force.
        /// Prevents removal of edges that would change Betti numbers.
        /// </summary>
        public const double TopologicalProtectionStrength = 0.005;

        /// <summary>
        /// Amplitude of vacuum fluctuations in edge weights.
        /// Derived from uncertainty principle: ?w ~ ? (fine structure).
        /// </summary>
        public const double VacuumFluctuationAmplitude = FineStructureConstant; // ~0.0073

        // ============================================================
        // SPINOR FIELD (Numerical - ensures unitarity)
        // ============================================================

        /// <summary>
        /// Threshold for spinor normalization violation.
        /// If |?|? deviates more than this from 1, correction is applied.
        /// </summary>
        public const double SpinorNormalizationThreshold = 0.0001;

        /// <summary>
        /// Correction rate for spinor normalization.
        /// Gentle correction prevents oscillation artifacts.
        /// </summary>
        public const double SpinorNormalizationCorrectionFactor = 0.2;

        // ============================================================
        // SYMPLECTIC INTEGRATOR (Numerical - stability)
        // ============================================================

        /// <summary>
        /// Safety threshold for symplectic norm preservation.
        /// Symplectic integrators preserve phase space volume; violations indicate instability.
        /// </summary>
        public const double SymplecticNormSafetyThreshold = 0.01;

        /// <summary>
        /// Rate of correction when symplectic norm drifts.
        /// </summary>
        public const double SymplecticNormCorrectionRate = 0.01;

        // ============================================================
        // WEIGHT BOUNDS (Physical + Numerical)
        // ============================================================

        /// <summary>
        /// RQ-MODERNIZATION: Allow edge weights to reach true zero.
        /// When TRUE: Weights can reach 0.0, allowing physical horizon formation.
        /// When FALSE: Legacy behavior with soft walls preventing zero weights.
        /// 
        /// SCIENTIFIC RATIONALE:
        /// In proper Regge calculus, w -> 0 represents a genuine metric singularity
        /// (horizon formation or spacetime fragmentation). Artificial soft walls
        /// prevent physically correct simulation of:
        /// - Black hole horizons
        /// - Topology changes (edge dissolution)
        /// - Spacetime fragmentation (big crunch)
        /// 
        /// Set to TRUE for physically rigorous simulations.
        /// Set to FALSE for numerical stability in exploratory runs.
        /// </summary>
        public const bool AllowZeroWeightEdges = false;

        /// <summary>
        /// RQ-MODERNIZATION: Enable/disable soft walls for edge weights.
        /// When TRUE: Clamp weights to [WeightLowerSoftWall, WeightUpperSoftWall].
        /// When FALSE: Allow weights to evolve freely (may require adaptive timestep).
        /// 
        /// SCIENTIFIC RATIONALE:
        /// Soft walls prevent physics equations from reaching natural conclusions.
        /// Disable for strict adherence to Einstein-Regge equations.
        /// </summary>
        public const bool UseSoftWalls = true;

        /// <summary>
        /// RQ-MODERNIZATION: Enable/disable connectivity protection.
        /// When TRUE: Suppress weight decreases when graph is at fragmentation risk.
        /// When FALSE: Let physics equations determine graph fate.
        /// 
        /// SCIENTIFIC RATIONALE:
        /// If Einstein-Regge equations predict universe fragmentation, the simulator
        /// should allow it rather than artificially preventing it.
        /// </summary>
        public const bool UseConnectivityProtection = true;

        /// <summary>
        /// RQ-MODERNIZATION: Use unbounded linear flow for gravity evolution.
        /// When TRUE: Use linear Euler step without Tanh saturation (unbounded flow).
        /// When FALSE: Use Tanh-bounded flow (legacy, more stable but artificially saturated).
        /// 
        /// TERMINOLOGICAL NOTE:
        /// This is NOT a symplectic integrator. True symplectic integration requires:
        /// - A momentum variable (? or p) in addition to position (w)
        /// - Velocity Verlet or leapfrog scheme preserving phase space volume
        /// 
        /// This flag simply removes the Tanh() saturation from the flow equation,
        /// allowing the physics equations to evolve without artificial velocity limits.
        /// 
        /// For true Hamiltonian gravity with momentum, see GeometryMomenta module
        /// which implements second-order dynamics with EdgeMomentum buffer.
        /// 
        /// STABILITY NOTE:
        /// When TRUE, may require smaller timesteps (adaptive dt) to prevent instability.
        /// </summary>
        public const bool UseUnboundedFlow = false;

        /// <summary>
        /// RQ-MODERNIZATION: Treat spacetime singularities as valid physics results.
        /// When TRUE: NaN/Infinity weights are interpreted as singularity formation.
        /// When FALSE: NaN/Infinity trigger exceptions (legacy error handling).
        /// 
        /// SCIENTIFIC RATIONALE:
        /// Metric singularities (w -> 0, curvature -> infinity) are physical predictions
        /// of General Relativity. They indicate horizon formation, big crunch, or
        /// vacuum decay. The simulator should recognize these as results, not errors.
        /// </summary>
        public const bool AllowSingularityFormation = true;


        /// <summary>
        /// RQ-MODERNIZATION: Maximum consecutive steps with singularity before termination.
        /// After this many steps with singularity indicators, simulation terminates gracefully
        /// with a "Spacetime Singularity" result.
        /// </summary>
        public const int SingularityGracePeriodSteps = 5;

        /// <summary>
        /// Soft lower wall for edge weights.
        /// Weights approaching zero trigger topological transition (edge removal).
        /// Value chosen to prevent numerical issues while allowing dynamics.
        /// 
        /// NOTE: Only applied when UseSoftWalls = true.
        /// </summary>
        public const double WeightLowerSoftWall = 0.01;

        /// <summary>
        /// Soft upper wall for edge weights.
        /// Prevents runaway edge strengthening.
        /// 
        /// NOTE: Only applied when UseSoftWalls = true.
        /// </summary>
        public const double WeightUpperSoftWall = 2.0;

        /// <summary>
        /// Absolute minimum weight before edge is removed.
        /// Used when UseSoftWalls = false but AllowZeroWeightEdges = false.
        /// </summary>
        public const double WeightAbsoluteMinimum = 0.001;

        /// <summary>
        /// Absolute maximum weight (hard cap).
        /// </summary>
        public const double WeightAbsoluteMaximum = 5.0;

        // ============================================================
        // LOCALITY AND CAUSALITY (Physical)
        // ============================================================

        /// <summary>Maximum local subgraph size for local computations.</summary>
        public const int MaxLocalSubgraphSize = 20;

        /// <summary>Maximum causal distance (in hops) for signal propagation per step.</summary>
        public const int MaxCausalDistance = 10;

        /// <summary>Speed of light in Planck units. c = 1 by construction.</summary>
        public const double SpeedOfLight = 1.0;

        /// <summary>
        /// Maximum hop distance for edge creation to respect causality.
        /// Edges can only connect nodes within this many hops (light cone).
        /// </summary>
        public const int CausalMaxHops = 3;

        // ============================================================
        // TIME DILATION (Physical - from General Relativity)
        // ============================================================

        /// <summary>
        /// Coupling between mass and time dilation: N = 1/(1 + ? M/M_P)
        /// Derived from Schwarzschild metric in weak field limit.
        /// </summary>
        public const double TimeDilationMassCoupling = 0.1;

        /// <summary>
        /// Coupling between curvature and time dilation.
        /// N = 1/(1 + ? |R|) where R is Ricci scalar.
        /// </summary>
        public const double TimeDilationCurvatureCoupling = 0.05;

        /// <summary>Minimum time dilation factor (prevents time freeze).</summary>
        public const double MinTimeDilation = 0.1;

        /// <summary>Maximum time dilation factor.</summary>
        public const double MaxTimeDilation = 2.0;

        /// <summary>
        /// Entropic time dilation coefficient (RQ-Hypothesis).
        /// N = exp(-? S) where S is local entropy.
        /// Controls sensitivity of lapse function to entropy.
        /// </summary>
        public const double TimeDilationAlpha = 0.5;

        /// <summary>
        /// Lapse function curvature coupling (RQ-Hypothesis).
        /// Controls gravitational time dilation: N = 1/(1 + ? |R|)
        /// ? = 1.0 corresponds to natural Planck units.
        /// </summary>
        public const double LapseFunctionAlpha = 1.0;

        // ============================================================
        // TOPOLOGICAL CENSORSHIP (Physical - from GR singularity theorems)
        // ============================================================

        /// <summary>
        /// Flux threshold for topological censorship.
        /// High flux regions (black hole interiors) are "censored".
        /// </summary>
        public const double TopologicalCensorshipFluxThreshold = 0.5;

        // ============================================================
        // SPECTRAL DIMENSION & GRAPH HEALTH (Diagnostic)
        // ============================================================

        /// <summary>
        /// Critical spectral dimension below which graph is fragmented.
        /// d_S < 1.5 indicates disconnection into 1D chains.
        /// </summary>
        public const double CriticalSpectralDimension = 1.5;

        /// <summary>
        /// Warning spectral dimension for triggering recovery.
        /// </summary>
        public const double WarningSpectralDimension = 2.5;

        /// <summary>
        /// Giant cluster threshold (fraction of total nodes).
        /// Clusters larger than this trigger decoherence.
        /// </summary>
        public const double GiantClusterThreshold = 0.3;

        /// <summary>
        /// Edge fraction to add when recovering from fragmentation.
        /// </summary>
        public const double FragmentationRecoveryEdgeFraction = 0.05;

        // ============================================================
        // DECOHERENCE (Physical - quantum measurement theory)
        // ============================================================

        /// <summary>
        /// Emergency threshold for giant cluster decoherence.
        /// </summary>
        public const double EmergencyGiantClusterThreshold = 0.5;

        /// <summary>
        /// Rate of decoherence applied to giant clusters.
        /// Derived from typical decoherence timescales in condensed matter.
        /// </summary>
        public const double GiantClusterDecoherenceRate = 0.1;

        /// <summary>
        /// Maximum fraction of edges to decohere per step.
        /// Prevents catastrophic graph destruction.
        /// </summary>
        public const double MaxDecoherenceEdgesFraction = 0.1;

        // ============================================================
        // EDGE QUANTIZATION (RQ-Hypothesis - discrete geometry)
        // ============================================================

        /// <summary>
        /// Quantum of edge weight (minimum discrete change).
        /// In Planck units, geometry changes in discrete steps.
        /// </summary>
        public const double EdgeWeightQuantum = 0.01;

        /// <summary>
        /// Energy cost of random number generation (Maxwell's demon).
        /// Implements Landauer limit for computational thermodynamics.
        /// </summary>
        public const double RngStepCost = 0.001;

        // ============================================================
        // WARMUP AND ANNEALING (Numerical - thermalization)
        // ============================================================

        /// <summary>
        /// Gravitational coupling during warmup phase.
        /// Higher value allows faster initial equilibration.
        /// </summary>
        public const double WarmupGravitationalCoupling = 0.5;

        /// <summary>
        /// Number of steps for initial warmup (thermalization).
        /// </summary>
        public const int WarmupDuration = 100;

        /// <summary>
        /// Steps for gravity coupling to transition from warmup to final value.
        /// Uses smooth interpolation to avoid discontinuities.
        /// </summary>
        public const int GravityTransitionDuration = 50;

        /// <summary>
        /// Initial temperature for simulated annealing.
        /// </summary>
        public const double InitialAnnealingTemperature = 1.0;

        /// <summary>
        /// Final (target) temperature for simulated annealing.
        /// </summary>
        public const double FinalAnnealingTemperature = 0.01;

        /// <summary>
        /// Time constant for exponential annealing: T(t) = T_0 exp(-t/?)
        /// </summary>
        public const double AnnealingTimeConstant = 100.0;

        // ============================================================
        // FIELD PARAMETERS (Physical)
        // ============================================================

        /// <summary>
        /// Diffusion rate for scalar field.
        /// Controls smoothing of field gradients.
        /// </summary>
        public const double FieldDiffusionRate = 0.1;

        /// <summary>
        /// Dirac coupling constant for fermion-geometry interaction.
        /// Derived from Dirac equation on curved spacetime.
        /// </summary>
        public const double DiracCoupling = 0.5;

        /// <summary>
        /// Energy barrier for edge creation (pair production).
        /// Related to rest mass energy of created pair.
        /// </summary>
        public const double EdgeCreationBarrier = 0.5;

        /// <summary>
        /// Energy barrier for edge annihilation.
        /// </summary>
        public const double EdgeAnnihilationBarrier = 0.5;

        // ============================================================
        // COUPLING CONSTANTS (Physical)
        // ============================================================

        /// <summary>Minimum cluster size for physics calculations.</summary>
        public const int MinimumClusterSize = 5;

        /// <summary>
        /// Gauge coupling constant for U(1) electromagnetism.
        /// Related to fine structure constant: g_EM = ?(4??) ? 0.303
        /// </summary>
        public static readonly double GaugeCouplingConstant = Math.Sqrt(4 * Math.PI * FineStructureConstant);

        /// <summary>
        /// Gravitational coupling in simulation units.
        /// Scaled for numerical tractability (physical G = 1 in Planck units).
        /// </summary>
        public const double GravitationalCoupling = 1;

        /// <summary>
        /// Cosmological constant for simulation.
        /// Much larger than physical value for visible effects.
        /// </summary>
        public const double CosmologicalConstant = 0.0001;

        /// <summary>
        /// Penalty factor for degree deviation from target.
        /// </summary>
        public const double DegreePenaltyFactor = 0.01;

        /// <summary>
        /// Base timestep for integration (dt in Planck time units).
        /// Small enough for stability, large enough for efficiency.
        /// </summary>
        public const double BaseTimestep = 0.01;

        /// <summary>
        /// Mass parameter for Klein-Gordon equation.
        /// In Planck units, typical particle masses are much smaller than 1.
        /// </summary>
        public const double KleinGordonMass = 0.1;

        /// <summary>
        /// Scale factor for curvature term in field equations.
        /// </summary>
        public const double CurvatureTermScale = 0.05;

        /// <summary>
        /// Tolerance for energy conservation validation.
        /// Violations larger than this are logged.
        /// </summary>
        public const double EnergyConservationTolerance = 1e-6;

        // ============================================================
        // VACUUM FLUCTUATIONS (Physical - QFT)
        // ============================================================

        /// <summary>
        /// Scale for vacuum fluctuations in local potential.
        /// Derived from zero-point energy: ?E ~ ??/2
        /// </summary>
        public const double VacuumFluctuationScale = 0.02;

        // ============================================================
        // WILSON FERMION (Physical - Lattice QCD)
        // ============================================================

        /// <summary>
        /// Wilson mass penalty for same-parity (same sublattice) edges.
        /// Large value suppresses fermion doubling modes.
        /// </summary>
        public const double WilsonMassPenalty = 10.0;

        /// <summary>
        /// Wilson parameter r in W = -(r/2) · ??.
        /// r = 1.0 is standard in lattice QCD.
        /// </summary>
        public const double WilsonParameter = 1.0;

        // ============================================================
        // HAWKING RADIATION (Physical - Black Hole Thermodynamics)
        // ============================================================

        /// <summary>
        /// Mass threshold for spontaneous pair creation near horizons.
        /// P_pair = exp(-2? m_eff / T_Unruh)
        /// </summary>
        public const double PairCreationMassThreshold = 0.1;

        /// <summary>
        /// Energy extracted from geometry per pair creation event.
        /// Represents backreaction on spacetime curvature.
        /// </summary>
        public const double PairCreationEnergy = 0.01;

        // ============================================================
        // ENERGY WEIGHTS (Relative importance in total energy)
        // ============================================================

        /// <summary>Weight of scalar field contribution to total energy.</summary>
        public const double ScalarFieldEnergyWeight = 1.0;

        /// <summary>Weight of fermion field contribution to total energy.</summary>
        public const double FermionFieldEnergyWeight = 1.0;

        /// <summary>Weight of gauge field contribution to total energy.</summary>
        public const double GaugeFieldEnergyWeight = 1.0;

        /// <summary>Weight of graph link contribution to total energy.</summary>
        public const double GraphLinkEnergyWeight = 1.0;

        /// <summary>Weight of Yang-Mills field contribution to total energy.</summary>
        public const double YangMillsFieldEnergyWeight = 1.0;

        /// <summary>Weight of gravity/curvature contribution to total energy.</summary>
        public const double GravityCurvatureEnergyWeight = 1.0;

        /// <summary>Weight of cluster binding contribution to total energy.</summary>
        public const double ClusterBindingEnergyWeight = 1.0;

        // ============================================================
        // HIGGS PARAMETERS (Physical - Standard Model)
        // ============================================================

        /// <summary>
        /// Higgs ?? parameter (negative for spontaneous symmetry breaking).
        /// V(?) = ??|?|? + ?|?|?, ?? < 0 gives Mexican hat potential.
        /// </summary>
        public const double HiggsMuSquared = -1.0;

        /// <summary>
        /// Higgs quartic coupling ?.
        /// Determines Higgs self-interaction strength.
        /// </summary>
        public const double HiggsLambda = 0.1;

        /// <summary>
        /// Higgs vacuum expectation value: v = ?(-??/?)
        /// In physical units: v ? 246 GeV
        /// </summary>
        public static readonly double HiggsVEV = Math.Sqrt(-HiggsMuSquared / HiggsLambda);

        // ============================================================
        // GAUGE PARAMETERS
        // ============================================================

        /// <summary>Scale factor for gauge current contribution.</summary>
        public const double GaugeCurrentScaleFactor = 1.0;

        /// <summary>Weight for plaquette (Wilson loop) action contribution.</summary>
        public const double PlaquetteWeight = 1.0;

        /// <summary>Default mass for graph nodes.</summary>
        public const double DefaultNodeMass = 1.0;

        // ============================================================
        // TOPOLOGICAL AND FLUX PARAMETERS
        // ============================================================

        /// <summary>Threshold for flux phase to be considered significant.</summary>
        public const double FluxPhaseThreshold = 0.1;

        /// <summary>Grace period (steps) before fragmentation triggers recovery.</summary>
        public const int FragmentationGracePeriodSteps = 10;

        /// <summary>Minimum reduction in weight per decoherence event.</summary>
        public const double MinDecoherenceWeightReduction = 0.01;

        // ============================================================
        // SPECTRAL AND DIMENSION PARAMETERS
        // ============================================================

        /// <summary>Initial center for power iteration in spectral analysis.</summary>
        public const double PowerIterationInitCenter = 0.5;

        /// <summary>Target growth ratio for 4D spacetime emergence.</summary>
        public const double TargetGrowthRatio4D = 4.0;

        /// <summary>
        /// Penalty coefficient for dimension deviation.
        /// Used when spectral action mode is disabled.
        /// </summary>
        public const double DimensionPenalty = 0.1;

        // ============================================================
        // ADDITIONAL UI/DISPLAY CONSTANTS
        // ============================================================

        /// <summary>Fraction of total steps used for warmup.</summary>
        public const double WarmupFraction = 0.1;

        /// <summary>Scale factor for Fubini-Study metric calculations.</summary>
        public const double FubiniStudyScale = 1.0;

        /// <summary>Scale factor for cluster momentum calculations.</summary>
        public const double ClusterMomentumScale = 1.0;

        /// <summary>Rate of topology tunneling events.</summary>
        public const double TopologyTunnelingRate = 0.01;

        /// <summary>Default fraction of steps for annealing.</summary>
        public const double DefaultAnnealingFraction = 0.1;

        /// <summary>Physical annealing time constant.</summary>
        public const double PhysicalAnnealingTimeConstant = 100.0;

        /// <summary>Damping coefficient for geometry evolution.</summary>
        public const double GeometryDamping = 0.999;

        /// <summary>Critical threshold for gravity suppression.</summary>
        public const double CriticalGravitySuppression = 0.5;

        /// <summary>Tolerance for U(1) gauge flux conservation.</summary>
        public const double GaugeFluxTolerance = 0.1;

        /// <summary>Tolerance for SU(3) color flux conservation.</summary>
        public const double ColorFluxTolerance = 0.1;

        /// <summary>Radius for radiation distribution from pair creation.</summary>
        public const int RadiationDistributionRadius = 3;

        /// <summary>E = mc? conversion factor (c = 1 in natural units).</summary>
        public const double C2_EnergyMassConversion = 1.0;

        /// <summary>Probability of excitation from signal propagation.</summary>
        public const double SignalExcitationProbability = 0.1;

        /// <summary>Strength factor for signal propagation.</summary>
        public const double SignalStrengthFactor = 1.0;

        /// <summary>Frequency for field harmonic oscillator modes.</summary>
        public const double FieldHarmonicFrequency = 1.0;

        /// <summary>Small epsilon for curvature regularization (prevents 1/0).</summary>
        public const double CurvatureRegularizationEpsilon = 1e-6;

        /// <summary>Fraction of nodes used as internal clock subsystem.</summary>
        public const double ClockSubsystemFraction = 0.1;

        /// <summary>Threshold for clock tick detection.</summary>
        public const double ClockTickThreshold = 0.5;

        /// <summary>Minimum Betti number for topological protection.</summary>
        public const int MinBettiForProtection = 1;

        /// <summary>Threshold for spectral gap (topology stability).</summary>
        public const double SpectralGapThreshold = 0.1;

        /// <summary>Fraction of graph for local updates.</summary>
        public const double LocalUpdateFraction = 0.1;

        /// <summary>Radius for measurement locality (quantum measurement).</summary>
        public const int MeasurementLocalityRadius = 3;

        /// <summary>Rate of measurement-induced decoherence.</summary>
        public const double MeasurementDecoherenceRate = 0.1;

        /// <summary>Scale for symplectic phase calculation.</summary>
        public const double SymplecticPhaseScale = 1.0;

        /// <summary>Lagrange multiplier for volume constraint.</summary>
        public const double VolumeConstraintLambda = 0.01;

        /// <summary>Target volume for volume constraint.</summary>
        public const double TargetVolume = 1000.0;

        /// <summary>Rate of field amplitude decay (energy loss).</summary>
        public const double FieldDecayRate = 0.001;

        /// <summary>
        /// Compute annealing time constant based on total steps.
        /// ? = totalSteps ? DefaultAnnealingFraction
        /// This gives a smooth exponential cooling schedule.
        /// </summary>
        /// <param name="totalSteps">Total simulation steps</param>
        /// <returns>Annealing time constant ?</returns>
        public static double ComputeAnnealingTimeConstant(int totalSteps)
        {
            return totalSteps * DefaultAnnealingFraction;
        }

        // ============================================================
        // DEPRECATED (kept for compatibility)
        // ============================================================

        /// <summary>
        /// [DEPRECATED] Barrier potential removed in favor of topological transitions.
        /// </summary>
        [Obsolete("Barrier potential removed. Edges now undergo topological transition at PlanckWeightThreshold.")]
        public const double WeightBarrierStrength = 0.001;
    }
}
