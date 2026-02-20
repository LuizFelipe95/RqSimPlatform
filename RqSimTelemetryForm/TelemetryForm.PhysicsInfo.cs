using RQSimulation;

namespace RqSimTelemetryForm;

/// <summary>
/// Physics Constants tab: read-only display of all PhysicsConstants values,
/// organized into logical groups. Ported from RqSimUI Form_Main_PhysicsInfo.cs.
/// </summary>
public partial class TelemetryForm
{
    private Panel _panelPhysicsConstants = null!;
    private TableLayoutPanel _tlpPhysicsConstants = null!;

    // Live parameter labels in the top summary section (refreshed on timer tick)
    private Label _liveGravCoupling = null!;
    private Label _liveVacuumEnergy = null!;
    private Label _liveDecoherenceRate = null!;
    private Label _liveTemperature = null!;
    private Label _liveHotStartTemp = null!;
    private Label _liveLapseAlpha = null!;
    private Label _liveWilsonParam = null!;
    private Label _liveGeometryInertia = null!;

    // Live labels inside the constants sections (overridable params show runtime values)
    private Label _constGravCoupling = null!;
    private Label _constLapseAlpha = null!;
    private Label _constWilsonParam = null!;
    private Label _constGeometryMomentumMass = null!;

    /// <summary>
    /// Initializes a scrollable panel displaying ALL physics constants from PhysicsConstants.
    /// Read-only labels show const values; provides a complete reference for users.
    /// </summary>
    private void InitializePhysicsConstantsTab()
    {
        _panelPhysicsConstants = new Panel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle
        };

        _tlpPhysicsConstants = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Padding = new Padding(5)
        };
        _tlpPhysicsConstants.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
        _tlpPhysicsConstants.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));

        int row = 0;

        // ============================================================
        // LIVE SIMULATION PARAMETERS (updated from LiveConfig)
        // ============================================================
        AddPhysicsSectionHeader("▶ Live Simulation Parameters (from UI / Apply)", ref row, Color.LimeGreen);
        AddLiveParamRow("Gravitational Coupling:", ref _liveGravCoupling, ref row);
        AddLiveParamRow("Vacuum Energy Scale:", ref _liveVacuumEnergy, ref row);
        AddLiveParamRow("Decoherence Rate:", ref _liveDecoherenceRate, ref row);
        AddLiveParamRow("Temperature:", ref _liveTemperature, ref row);
        AddLiveParamRow("Hot Start Temperature:", ref _liveHotStartTemp, ref row);
        AddLiveParamRow("Lapse Function Alpha:", ref _liveLapseAlpha, ref row);
        AddLiveParamRow("Wilson Parameter:", ref _liveWilsonParam, ref row);
        AddLiveParamRow("Geometry Inertia Mass:", ref _liveGeometryInertia, ref row);

        AddPhysicsSectionHeader("", ref row, Color.Gray); // spacer

        // ============================================================
        // FUNDAMENTAL CONSTANTS (Planck Units)
        // ============================================================
        AddPhysicsSectionHeader("══ Fundamental Constants (Planck Units) ══", ref row, Color.DarkBlue);
        AddPhysicsConstantRow("Speed of Light (c)", PhysicsConstants.C.ToString("G"), ref row);
        AddPhysicsConstantRow("Reduced Planck Constant (ℏ)", PhysicsConstants.HBar.ToString("G"), ref row);
        AddPhysicsConstantRow("Gravitational Constant (G)", PhysicsConstants.G.ToString("G"), ref row);
        AddPhysicsConstantRow("Planck Length", PhysicsConstants.PlanckLength.ToString("G"), ref row);
        AddPhysicsConstantRow("Planck Time", PhysicsConstants.PlanckTime.ToString("G"), ref row);
        AddPhysicsConstantRow("Planck Mass", PhysicsConstants.PlanckMass.ToString("G"), ref row);
        AddPhysicsConstantRow("Planck Energy", PhysicsConstants.PlanckEnergy.ToString("G"), ref row);

        // ============================================================
        // GAUGE COUPLING CONSTANTS
        // ============================================================
        AddPhysicsSectionHeader("══ Gauge Coupling Constants ══", ref row, Color.DarkGreen);
        AddPhysicsConstantRow("Fine Structure Constant (α)", $"{PhysicsConstants.FineStructureConstant:E6} (~1/137)", ref row);
        AddPhysicsConstantRow("Strong Coupling Constant (α_s)", PhysicsConstants.StrongCouplingConstant.ToString("F4"), ref row);
        AddPhysicsConstantRow("Weak Mixing Angle (sin²θ_W)", PhysicsConstants.WeakMixingAngle.ToString("F4"), ref row);
        AddPhysicsConstantRow("Weak Coupling Constant (g_W)", PhysicsConstants.WeakCouplingConstant.ToString("F4"), ref row);
        AddPhysicsConstantRow("Gauge Coupling Constant (e)", PhysicsConstants.GaugeCouplingConstant.ToString("F4"), ref row);

        // ============================================================
        // RQ-HYPOTHESIS v2.0 CONSTANTS
        // ============================================================
        AddPhysicsSectionHeader("══ RQ-Hypothesis v2.0 ══", ref row, Color.DarkMagenta);
        AddPhysicsConstantRow("Use Hamiltonian Gravity", PhysicsConstants.UseHamiltonianGravity.ToString(), ref row);
        AddPhysicsConstantRow("Geometry Inertia Mass", PhysicsConstants.GeometryInertiaMass.ToString("F2"), ref row);
        AddPhysicsConstantRow("Yukawa Coupling", PhysicsConstants.YukawaCoupling.ToString("F4"), ref row);
        AddPhysicsConstantRow("Topological Mass Coupling", PhysicsConstants.TopoMassCoupling.ToString("F4"), ref row);
        AddPhysicsConstantRow("Enable Vacuum Energy Reservoir", PhysicsConstants.EnableVacuumEnergyReservoir.ToString(), ref row);
        AddPhysicsConstantRow("Initial Vacuum Energy", PhysicsConstants.InitialVacuumEnergy.ToString("F1"), ref row);
        AddPhysicsConstantRow("Initial Vacuum Pool Fraction", PhysicsConstants.InitialVacuumPoolFraction.ToString("F4"), ref row);
        AddPhysicsConstantRow("Vacuum Fluctuation Base Rate (α³)", PhysicsConstants.VacuumFluctuationBaseRate.ToString("E8"), ref row);
        AddPhysicsConstantRow("Curvature Coupling Factor (4π)", PhysicsConstants.CurvatureCouplingFactor.ToString("F4"), ref row);
        AddPhysicsConstantRow("Hawking Radiation Enhancement", PhysicsConstants.HawkingRadiationEnhancement.ToString("F1"), ref row);
        AddPhysicsConstantRow("Pair Creation Energy Threshold", PhysicsConstants.PairCreationEnergyThreshold.ToString("F1"), ref row);

        // ============================================================
        // RQ-HYPOTHESIS EXPERIMENTAL FLAGS
        // ============================================================
        AddPhysicsSectionHeader("══ RQ-Hypothesis Experimental Flags ══", ref row, Color.DarkOrchid);
        AddPhysicsConstantRow("Enable Natural Dimension Emergence", PhysicsConstants.EnableNaturalDimensionEmergence.ToString(), ref row);
        AddPhysicsConstantRow("Enable Topological Parity", PhysicsConstants.EnableTopologicalParity.ToString(), ref row);
        AddPhysicsConstantRow("Enable Lapse-Synchronized Geometry", PhysicsConstants.EnableLapseSynchronizedGeometry.ToString(), ref row);
        AddPhysicsConstantRow("Enable Topology Energy Compensation", PhysicsConstants.EnableTopologyEnergyCompensation.ToString(), ref row);
        AddPhysicsConstantRow("Enable Plaquette Yang-Mills", PhysicsConstants.EnablePlaquetteYangMills.ToString(), ref row);
        AddPhysicsConstantRow("Enable Symplectic Gauge Evolution", PhysicsConstants.EnableSymplecticGaugeEvolution.ToString(), ref row);
        AddPhysicsConstantRow("Enable Adaptive Topology Decoherence", PhysicsConstants.EnableAdaptiveTopologyDecoherence.ToString(), ref row);
        AddPhysicsConstantRow("Prefer Ollivier-Ricci Curvature", PhysicsConstants.PreferOllivierRicciCurvature.ToString(), ref row);

        // ============================================================
        // SYMPLECTIC YANG-MILLS DYNAMICS
        // ============================================================
        AddPhysicsSectionHeader("══ Symplectic Yang-Mills Dynamics ══", ref row, Color.Indigo);
        AddPhysicsConstantRow("Planck Constant² (ℏ²)", PhysicsConstants.PlanckConstantSqr.ToString("F1"), ref row);
        AddPhysicsConstantRow("Landauer Limit (ln2)", PhysicsConstants.LandauerLimit.ToString("F4"), ref row);
        AddPhysicsConstantRow("Gauge Momentum Mass U(1)", PhysicsConstants.GaugeMomentumMassU1.ToString("F2"), ref row);
        AddPhysicsConstantRow("Gauge Momentum Mass SU(2)", PhysicsConstants.GaugeMomentumMassSU2.ToString("F2"), ref row);
        AddPhysicsConstantRow("Gauge Momentum Mass SU(3)", PhysicsConstants.GaugeMomentumMassSU3.ToString("F2"), ref row);
        AddPhysicsConstantRow("Gauge Field Damping", PhysicsConstants.GaugeFieldDamping.ToString("E4"), ref row);

        // ============================================================
        // TOPOLOGY DECOHERENCE (ZENO EFFECT)
        // ============================================================
        AddPhysicsSectionHeader("══ Topology Decoherence (Zeno Effect) ══", ref row, Color.DarkSlateGray);
        AddPhysicsConstantRow("Topology Decoherence Interval", PhysicsConstants.TopologyDecoherenceInterval.ToString(), ref row);
        AddPhysicsConstantRow("Topology Decoherence Temperature", PhysicsConstants.TopologyDecoherenceTemperature.ToString("F4"), ref row);
        AddPhysicsConstantRow("Topology Flip Amplitude Threshold", PhysicsConstants.TopologyFlipAmplitudeThreshold.ToString("F4"), ref row);

        // ============================================================
        // WILSON LOOPS GAUGE PROTECTION
        // ============================================================
        AddPhysicsSectionHeader("══ Wilson Loops Gauge Protection ══", ref row, Color.DarkCyan);
        AddPhysicsConstantRow("Gauge Tolerance (rad)", PhysicsConstants.GaugeTolerance.ToString("F4"), ref row);
        AddPhysicsConstantRow("Enable Wilson Loop Protection", PhysicsConstants.EnableWilsonLoopProtection.ToString(), ref row);
        AddPhysicsConstantRow("Max Removable Flux (rad)", PhysicsConstants.MaxRemovableFlux.ToString("F4"), ref row);

        // ============================================================
        // SPECTRAL ACTION (CHAMSEDDINE-CONNES)
        // ============================================================
        AddPhysicsSectionHeader("══ Spectral Action (Chamseddine-Connes) ══", ref row, Color.Purple);
        AddPhysicsConstantRow("Lambda Cutoff", PhysicsConstants.SpectralActionConstants.LambdaCutoff.ToString("F2"), ref row);
        AddPhysicsConstantRow("F₀ Cosmological", PhysicsConstants.SpectralActionConstants.F0_Cosmological.ToString("F2"), ref row);
        AddPhysicsConstantRow("F₂ Einstein-Hilbert", PhysicsConstants.SpectralActionConstants.F2_EinsteinHilbert.ToString("F2"), ref row);
        AddPhysicsConstantRow("F₄ Weyl", PhysicsConstants.SpectralActionConstants.F4_Weyl.ToString("F2"), ref row);
        AddPhysicsConstantRow("Target Spectral Dimension", PhysicsConstants.SpectralActionConstants.TargetSpectralDimension.ToString("F1"), ref row);
        AddPhysicsConstantRow("Dimension Potential Strength", PhysicsConstants.SpectralActionConstants.DimensionPotentialStrength.ToString("F4"), ref row);
        AddPhysicsConstantRow("Dimension Potential Width", PhysicsConstants.SpectralActionConstants.DimensionPotentialWidth.ToString("F4"), ref row);
        AddPhysicsConstantRow("Enable Spectral Action Mode", PhysicsConstants.SpectralActionConstants.EnableSpectralActionMode.ToString(), ref row);

        // ============================================================
        // WHEELER-DEWITT CONSTRAINT
        // ============================================================
        AddPhysicsSectionHeader("══ Wheeler-DeWitt Constraint ══", ref row, Color.Maroon);
        AddPhysicsConstantRow("WdW Gravitational Coupling", PhysicsConstants.WheelerDeWittConstants.GravitationalCoupling.ToString("F4"), ref row);
        AddPhysicsConstantRow("WdW Constraint Lagrange Multiplier", PhysicsConstants.WheelerDeWittConstants.ConstraintLagrangeMultiplier.ToString("F1"), ref row);
        AddPhysicsConstantRow("WdW Constraint Tolerance", PhysicsConstants.WheelerDeWittConstants.ConstraintTolerance.ToString("E4"), ref row);
        AddPhysicsConstantRow("WdW Enable Strict Mode", PhysicsConstants.WheelerDeWittConstants.EnableStrictMode.ToString(), ref row);
        AddPhysicsConstantRow("WdW Enable Violation Logging", PhysicsConstants.WheelerDeWittConstants.EnableViolationLogging.ToString(), ref row);

        // ============================================================
        // GRAVITY AND CURVATURE
        // ============================================================
        AddPhysicsSectionHeader("══ Gravity and Curvature ══", ref row, Color.SaddleBrown);
        AddLiveConstantRow("Gravitational Coupling", PhysicsConstants.GravitationalCoupling.ToString("F4"), ref _constGravCoupling, ref row);
        AddPhysicsConstantRow("Warmup Gravitational Coupling", PhysicsConstants.WarmupGravitationalCoupling.ToString("F4"), ref row);
        AddPhysicsConstantRow("Warmup Duration", PhysicsConstants.WarmupDuration.ToString(), ref row);
        AddPhysicsConstantRow("Gravity Transition Duration (1/α)", PhysicsConstants.GravityTransitionDuration.ToString(), ref row);
        AddPhysicsConstantRow("Cosmological Constant (Λ)", PhysicsConstants.CosmologicalConstant.ToString("E4"), ref row);
        AddLiveConstantRow("Lapse Function Alpha", PhysicsConstants.LapseFunctionAlpha.ToString("F4"), ref _constLapseAlpha, ref row);
        AddLiveConstantRow("Geometry Momentum Mass", PhysicsConstants.GeometryMomentumMass.ToString("F1"), ref _constGeometryMomentumMass, ref row);
        AddPhysicsConstantRow("Geometry Damping", PhysicsConstants.GeometryDamping.ToString("F4"), ref row);
        AddPhysicsConstantRow("Curvature Term Scale", PhysicsConstants.CurvatureTermScale.ToString("F4"), ref row);

        // ============================================================
        // TIME DILATION
        // ============================================================
        AddPhysicsSectionHeader("══ Time Dilation ══", ref row, Color.Teal);
        AddPhysicsConstantRow("Time Dilation Alpha (entropy)", PhysicsConstants.TimeDilationAlpha.ToString("F4"), ref row);
        AddPhysicsConstantRow("Time Dilation Mass Coupling", PhysicsConstants.TimeDilationMassCoupling.ToString("F4"), ref row);
        AddPhysicsConstantRow("Time Dilation Curvature Coupling", PhysicsConstants.TimeDilationCurvatureCoupling.ToString("F4"), ref row);
        AddPhysicsConstantRow("Min Time Dilation", PhysicsConstants.MinTimeDilation.ToString("F4"), ref row);
        AddPhysicsConstantRow("Max Time Dilation", PhysicsConstants.MaxTimeDilation.ToString("F4"), ref row);
        AddPhysicsConstantRow("Speed of Light (network)", PhysicsConstants.SpeedOfLight.ToString("F1"), ref row);
        AddPhysicsConstantRow("Max Causal Distance", PhysicsConstants.MaxCausalDistance.ToString(), ref row);
        AddPhysicsConstantRow("Base Timestep", PhysicsConstants.BaseTimestep.ToString("F4"), ref row);

        // ============================================================
        // WILSON LATTICE FERMION
        // ============================================================
        AddPhysicsSectionHeader("══ Wilson Lattice Fermion ══", ref row, Color.SteelBlue);
        AddPhysicsConstantRow("Causal Max Hops", PhysicsConstants.CausalMaxHops.ToString(), ref row);
        AddPhysicsConstantRow("Wilson Mass Penalty", PhysicsConstants.WilsonMassPenalty.ToString("F2"), ref row);
        AddLiveConstantRow("Wilson Parameter (r)", PhysicsConstants.WilsonParameter.ToString("F2"), ref _constWilsonParam, ref row);

        // ============================================================
        // HAWKING RADIATION / PAIR CREATION
        // ============================================================
        AddPhysicsSectionHeader("══ Hawking Radiation / Pair Creation ══", ref row, Color.Crimson);
        AddPhysicsConstantRow("Pair Creation Mass Threshold", PhysicsConstants.PairCreationMassThreshold.ToString("F4"), ref row);
        AddPhysicsConstantRow("Pair Creation Energy", PhysicsConstants.PairCreationEnergy.ToString("F4"), ref row);

        // ============================================================
        // FIELD THEORY
        // ============================================================
        AddPhysicsSectionHeader("══ Field Theory ══", ref row, Color.DarkOliveGreen);
        AddPhysicsConstantRow("Field Diffusion Rate", PhysicsConstants.FieldDiffusionRate.ToString("F4"), ref row);
        AddPhysicsConstantRow("Field Decay Rate", PhysicsConstants.FieldDecayRate.ToString("E6"), ref row);
        AddPhysicsConstantRow("Klein-Gordon Mass (μ²)", PhysicsConstants.KleinGordonMass.ToString("F4"), ref row);
        AddPhysicsConstantRow("Dirac Coupling (λ_D)", PhysicsConstants.DiracCoupling.ToString("F4"), ref row);
        AddPhysicsConstantRow("Spinor Normalization Threshold", PhysicsConstants.SpinorNormalizationThreshold.ToString("E4"), ref row);
        AddPhysicsConstantRow("Spinor Norm Correction Factor", PhysicsConstants.SpinorNormalizationCorrectionFactor.ToString("F4"), ref row);

        // ============================================================
        // HIGGS POTENTIAL
        // ============================================================
        AddPhysicsSectionHeader("══ Higgs Potential ══", ref row, Color.DarkOrange);
        AddPhysicsConstantRow("Higgs μ²", PhysicsConstants.HiggsMuSquared.ToString("F4"), ref row);
        AddPhysicsConstantRow("Higgs λ", PhysicsConstants.HiggsLambda.ToString("F4"), ref row);
        AddPhysicsConstantRow("Higgs VEV", PhysicsConstants.HiggsVEV.ToString("F4"), ref row);

        // ============================================================
        // CLUSTER DYNAMICS
        // ============================================================
        AddPhysicsSectionHeader("══ Cluster Dynamics ══", ref row, Color.Sienna);
        AddPhysicsConstantRow("Default Heavy Cluster Threshold", PhysicsConstants.DefaultHeavyClusterThreshold.ToString("F4"), ref row);
        AddPhysicsConstantRow("Adaptive Threshold Sigma", PhysicsConstants.AdaptiveThresholdSigma.ToString("F2"), ref row);
        AddPhysicsConstantRow("Minimum Cluster Size", PhysicsConstants.MinimumClusterSize.ToString(), ref row);
        AddPhysicsConstantRow("Cluster Stabilization Temperature", PhysicsConstants.ClusterStabilizationTemperature.ToString("F4"), ref row);
        AddPhysicsConstantRow("Metropolis Trials per Cluster", PhysicsConstants.MetropolisTrialsPerCluster.ToString(), ref row);
        AddPhysicsConstantRow("Overcorrelation Threshold", PhysicsConstants.OvercorrelationThreshold.ToString("F4"), ref row);

        // ============================================================
        // GRAPH HEALTH (RQ-HYPOTHESIS)
        // ============================================================
        AddPhysicsSectionHeader("══ Graph Health (RQ-Hypothesis) ══", ref row, Color.DarkRed);
        AddPhysicsConstantRow("Critical Spectral Dimension", PhysicsConstants.CriticalSpectralDimension.ToString("F2"), ref row);
        AddPhysicsConstantRow("Warning Spectral Dimension", PhysicsConstants.WarningSpectralDimension.ToString("F2"), ref row);
        AddPhysicsConstantRow("Giant Cluster Threshold (%N)", PhysicsConstants.GiantClusterThreshold.ToString("P0"), ref row);
        AddPhysicsConstantRow("Emergency Giant Cluster Threshold", PhysicsConstants.EmergencyGiantClusterThreshold.ToString("P0"), ref row);
        AddPhysicsConstantRow("Giant Cluster Decoherence Rate", PhysicsConstants.GiantClusterDecoherenceRate.ToString("F4"), ref row);
        AddPhysicsConstantRow("Max Decoherence Edges Fraction", PhysicsConstants.MaxDecoherenceEdgesFraction.ToString("P0"), ref row);
        AddPhysicsConstantRow("Fragmentation Recovery Edge Frac", PhysicsConstants.FragmentationRecoveryEdgeFraction.ToString("P0"), ref row);
        AddPhysicsConstantRow("Fragmentation Grace Period Steps", PhysicsConstants.FragmentationGracePeriodSteps.ToString(), ref row);

        // ============================================================
        // EDGE QUANTIZATION / TOPOLOGY
        // ============================================================
        AddPhysicsSectionHeader("══ Edge Quantization / Topology ══", ref row, Color.DarkSlateBlue);
        AddPhysicsConstantRow("Edge Weight Quantum", PhysicsConstants.EdgeWeightQuantum.ToString("F4"), ref row);
        AddPhysicsConstantRow("RNG Step Cost", PhysicsConstants.RngStepCost.ToString("E6"), ref row);
        AddPhysicsConstantRow("Edge Creation Cost", PhysicsConstants.EdgeCreationCost.ToString("F4"), ref row);
        AddPhysicsConstantRow("Planck Weight Threshold", PhysicsConstants.PlanckWeightThreshold.ToString("E6"), ref row);
        AddPhysicsConstantRow("Edge Creation Barrier", PhysicsConstants.EdgeCreationBarrier.ToString("F4"), ref row);
        AddPhysicsConstantRow("Edge Annihilation Barrier", PhysicsConstants.EdgeAnnihilationBarrier.ToString("F4"), ref row);
        AddPhysicsConstantRow("Weight Lower Soft Wall", PhysicsConstants.WeightLowerSoftWall.ToString("F4"), ref row);
        AddPhysicsConstantRow("Weight Upper Soft Wall", PhysicsConstants.WeightUpperSoftWall.ToString("F4"), ref row);
        AddPhysicsConstantRow("Weight Absolute Minimum", PhysicsConstants.WeightAbsoluteMinimum.ToString("E4"), ref row);
        AddPhysicsConstantRow("Weight Absolute Maximum", PhysicsConstants.WeightAbsoluteMaximum.ToString("F4"), ref row);

        // ============================================================
        // UPDATE INTERVALS
        // ============================================================
        AddPhysicsSectionHeader("══ Update Intervals ══", ref row, Color.Gray);
        AddPhysicsConstantRow("Topology Update Interval", PhysicsConstants.TopologyUpdateInterval.ToString(), ref row);
        AddPhysicsConstantRow("Topology Flips Divisor", PhysicsConstants.TopologyFlipsDivisor.ToString(), ref row);
        AddPhysicsConstantRow("Geometry Update Interval", PhysicsConstants.GeometryUpdateInterval.ToString(), ref row);
        AddPhysicsConstantRow("Gauge Constraint Interval", PhysicsConstants.GaugeConstraintInterval.ToString(), ref row);
        AddPhysicsConstantRow("Energy Validation Interval", PhysicsConstants.EnergyValidationInterval.ToString(), ref row);
        AddPhysicsConstantRow("Topological Protection Interval", PhysicsConstants.TopologicalProtectionInterval.ToString(), ref row);

        // ============================================================
        // ANNEALING
        // ============================================================
        AddPhysicsSectionHeader("══ Hot Start & Annealing ══", ref row, Color.OrangeRed);
        AddPhysicsConstantRow("Initial Annealing Temperature", PhysicsConstants.InitialAnnealingTemperature.ToString("F1"), ref row);
        AddPhysicsConstantRow("Final Annealing Temperature", PhysicsConstants.FinalAnnealingTemperature.ToString("E4"), ref row);
        AddPhysicsConstantRow("Physical Annealing Time Const", PhysicsConstants.PhysicalAnnealingTimeConstant.ToString("F1"), ref row);

        // ============================================================
        // ENERGY WEIGHTS
        // ============================================================
        AddPhysicsSectionHeader("══ Energy Weights (Unified Hamiltonian) ══", ref row, Color.DarkGoldenrod);
        AddPhysicsConstantRow("Scalar Field Energy Weight", PhysicsConstants.ScalarFieldEnergyWeight.ToString("F1"), ref row);
        AddPhysicsConstantRow("Fermion Field Energy Weight", PhysicsConstants.FermionFieldEnergyWeight.ToString("F1"), ref row);
        AddPhysicsConstantRow("Gauge Field Energy Weight", PhysicsConstants.GaugeFieldEnergyWeight.ToString("F1"), ref row);
        AddPhysicsConstantRow("Yang-Mills Field Energy Weight", PhysicsConstants.YangMillsFieldEnergyWeight.ToString("F1"), ref row);
        AddPhysicsConstantRow("Graph Link Energy Weight", PhysicsConstants.GraphLinkEnergyWeight.ToString("F1"), ref row);
        AddPhysicsConstantRow("Gravity Curvature Energy Weight", PhysicsConstants.GravityCurvatureEnergyWeight.ToString("F1"), ref row);
        AddPhysicsConstantRow("Cluster Binding Energy Weight", PhysicsConstants.ClusterBindingEnergyWeight.ToString("F1"), ref row);

        _panelPhysicsConstants.Controls.Add(_tlpPhysicsConstants);

        var grpAllConstants = new GroupBox
        {
            Text = "All Physics Constants (Read-Only Reference)",
            Dock = DockStyle.Fill,
            Padding = new Padding(5)
        };
        grpAllConstants.Controls.Add(_panelPhysicsConstants);

        _tabPhysicsConstants.Controls.Add(grpAllConstants);
    }



    private void button_CopyConstantsToClipboard_Click(object sender, EventArgs e)
    {
        try
        {
            string constantsText = $"Physics Constants:\n";// +

            // Get all labels from the TableLayoutPanel and format them as "Name: Value"
                        
            foreach (Control control in _tlpPhysicsConstants.Controls)
            {
                if (control is Label lbl && lbl.Font.Bold == false) // skip section headers (bold labels)
                {
                    int col = _tlpPhysicsConstants.GetColumn(control);
                    int row = _tlpPhysicsConstants.GetRow(control);
                    // Only process the first column (names) and get the corresponding value from the second column
                    if (col == 0)
                    {
                        string name = lbl.Text.TrimEnd(':'); // remove trailing colon
                        // Find the value label in the same row but next column
                        foreach (Control sibling in _tlpPhysicsConstants.Controls)
                        {
                            if (_tlpPhysicsConstants.GetRow(sibling) == row && _tlpPhysicsConstants.GetColumn(sibling) == 1 && sibling is Label valueLabel)
                            {
                                string value = valueLabel.Text;
                                constantsText += $"{name}: {value}\n";
                                break;
                            }
                        }
                    }
                }

            }

            Clipboard.SetText(constantsText);
            MessageBox.Show("Physics constants copied to clipboard!", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

    }



    // ============================================================
    // HELPER METHODS (standalone, not shared with RqSimUI)
    // ============================================================

    private void AddPhysicsSectionHeader(string text, ref int row, Color color)
    {
        _tlpPhysicsConstants.RowCount = row + 1;
        _tlpPhysicsConstants.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = color,
            Font = new Font(Font, FontStyle.Bold),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 10, 0, 3)
        };
        _tlpPhysicsConstants.Controls.Add(label, 0, row);
        _tlpPhysicsConstants.SetColumnSpan(label, 2);
        row++;
    }

    private void AddPhysicsConstantRow(string name, string value, ref int row)
    {
        _tlpPhysicsConstants.RowCount = row + 1;
        _tlpPhysicsConstants.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblName = new Label
        {
            Text = name + ":",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = SystemColors.GrayText
        };
        var lblValue = new Label
        {
            Text = value,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Consolas", 9F)
        };
        _tlpPhysicsConstants.Controls.Add(lblName, 0, row);
        _tlpPhysicsConstants.Controls.Add(lblValue, 1, row);
        row++;
    }

    private void AddLiveParamRow(string name, ref Label valueLabel, ref int row)
    {
        _tlpPhysicsConstants.RowCount = row + 1;
        _tlpPhysicsConstants.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblName = new Label
        {
            Text = name,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Color.LimeGreen
        };
        valueLabel = new Label
        {
            Text = "—",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Consolas", 10F, FontStyle.Bold),
            ForeColor = Color.DarkGreen
        };
        _tlpPhysicsConstants.Controls.Add(lblName, 0, row);
        _tlpPhysicsConstants.Controls.Add(valueLabel, 1, row);
        row++;
    }

    /// <summary>
    /// Adds a constant row whose value label can be updated at runtime.
    /// Initially shows the static default; refreshed with LiveConfig value on timer tick.
    /// </summary>
    private void AddLiveConstantRow(string name, string defaultValue, ref Label valueLabel, ref int row)
    {
        _tlpPhysicsConstants.RowCount = row + 1;
        _tlpPhysicsConstants.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblName = new Label
        {
            Text = name + ":",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = SystemColors.GrayText
        };
        valueLabel = new Label
        {
            Text = defaultValue,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Consolas", 9F)
        };
        _tlpPhysicsConstants.Controls.Add(lblName, 0, row);
        _tlpPhysicsConstants.Controls.Add(valueLabel, 1, row);
        row++;
    }

    /// <summary>
    /// Refreshes live parameter labels from LiveConfig.
    /// Called from the UI update timer tick.
    /// </summary>
    private void RefreshLivePhysicsParams()
    {
        if (!_hasApiConnection) return;

        var liveConfig = _simApi.LiveConfig;

        // Top summary section
        _liveGravCoupling.Text = liveConfig.GravitationalCoupling.ToString("F4");
        _liveVacuumEnergy.Text = liveConfig.VacuumEnergyScale.ToString("E4");
        _liveDecoherenceRate.Text = liveConfig.DecoherenceRate.ToString("F6");
        _liveTemperature.Text = liveConfig.Temperature.ToString("F4");
        _liveHotStartTemp.Text = liveConfig.HotStartTemperature.ToString("F2");
        _liveLapseAlpha.Text = liveConfig.LapseFunctionAlpha.ToString("F4");
        _liveWilsonParam.Text = liveConfig.WilsonParameter.ToString("F4");
        _liveGeometryInertia.Text = liveConfig.GeometryInertiaMass.ToString("F4");

        // Overridable params inside the constants sections
        _constGravCoupling.Text = liveConfig.GravitationalCoupling.ToString("F4");
        _constLapseAlpha.Text = liveConfig.LapseFunctionAlpha.ToString("F4");
        _constWilsonParam.Text = liveConfig.WilsonParameter.ToString("F2");
        _constGeometryMomentumMass.Text = liveConfig.GeometryInertiaMass.ToString("F1");
        // GeometryDamping and CurvatureTermScale are not in LiveConfig — keep static defaults
    }
}
