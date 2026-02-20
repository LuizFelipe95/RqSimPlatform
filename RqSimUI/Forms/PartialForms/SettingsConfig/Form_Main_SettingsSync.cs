using RQSimulation;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main - Handles synchronization of UI settings with PhysicsConstants.
/// This file contains methods to initialize additional physics controls and sync UI with constants.
/// </summary>
public partial class Form_Main_RqSim
{
    // === Additional RQ-Hypothesis Numeric Controls ===

    private NumericUpDown numTimeDilationAlpha = null!;

    private NumericUpDown numLapseFunctionAlpha = null!;
    private NumericUpDown numWilsonParameter = null!;
    private NumericUpDown numTopologyDecoherenceInterval = null!;
    private NumericUpDown numTopologyDecoherenceTemperature = null!;
    private NumericUpDown numGaugeTolerance = null!;
    private NumericUpDown numMaxRemovableFlux = null!;
    private NumericUpDown numGeometryInertiaMass = null!;
    private NumericUpDown numGaugeFieldDamping = null!;
    private NumericUpDown numPairCreationMassThreshold = null!;
    private NumericUpDown numPairCreationEnergy = null!;

    // === Spectral Action Controls ===
    private NumericUpDown numSpectralLambdaCutoff = null!;
    private NumericUpDown numSpectralTargetDimension = null!;
    private NumericUpDown numSpectralDimensionPotentialStrength = null!;

    // === MCMC Sampler Controls ===
    private NumericUpDown numMcmcBeta = null!;
    private NumericUpDown numMcmcStepsPerCall = null!;
    private NumericUpDown numMcmcWeightPerturbation = null!;

    // === Sinkhorn Ollivier-Ricci Controls ===
    private NumericUpDown numSinkhornIterations = null!;
    private NumericUpDown numSinkhornEpsilon = null!;
    private NumericUpDown numSinkhornConvergenceThreshold = null!;

    /// <summary>
    /// Right-side TLP for overflow physics controls (Topology Decoherence and below).
    /// </summary>
    private TableLayoutPanel? _tlpPhysicsConstantsRight;

    // === Additional RQ-Hypothesis Checkbox Controls ===
    private CheckBox chkEnableSymplecticGaugeEvolution = null!;
    private CheckBox chkEnableAdaptiveTopologyDecoherence = null!;
    private CheckBox chkEnableWilsonLoopProtection = null!;
    private CheckBox chkEnableSpectralActionMode = null!;
    private CheckBox chkEnableWheelerDeWittStrictMode = null!;
    private CheckBox chkUseHamiltonianGravity = null!;
    private CheckBox chkEnableVacuumEnergyReservoir = null!;
    private CheckBox chkPreferOllivierRicciCurvature = null!;

    /// <summary>
    /// Initializes additional physics controls on tlpPhysicsConstants panel.
    /// Called after InitializeGraphHealthControls() to add more advanced parameters.
    /// </summary>
    private void InitializeAdvancedPhysicsControls()
    {
        // === Create split layout: two TLPs side by side inside grpPhysicsConstants ===
        _tlpPhysicsConstantsRight = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Name = "_tlpPhysicsConstantsRight"
        };
        _tlpPhysicsConstantsRight.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 73.333F));
        _tlpPhysicsConstantsRight.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26.667F));

        var splitWrapper = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Name = "_tlpPhysicsConstantsSplit"
        };
        splitWrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        splitWrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        splitWrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // Reparent: move tlpPhysicsConstants into left cell of split
        grpPhysicsConstants.Controls.Remove(tlpPhysicsConstants);
        tlpPhysicsConstants.Dock = DockStyle.Top;
        splitWrapper.Controls.Add(tlpPhysicsConstants, 0, 0);
        splitWrapper.Controls.Add(_tlpPhysicsConstantsRight, 1, 0);
        grpPhysicsConstants.Controls.Add(splitWrapper);

        // === LEFT column: Lapse Function + Wilson Fermion ===
        int startRow = tlpPhysicsConstants.RowCount;

        AddPhysicsHeader(tlpPhysicsConstants, "─── Lapse Function ───", ref startRow, Color.DarkSlateBlue);

        AddNumericControl(tlpPhysicsConstants, "Lapse Function α:", ref numLapseFunctionAlpha,
            (decimal)PhysicsConstants.LapseFunctionAlpha, 0.01m, 0.0m, 10.0m, 2, ref startRow,
            "Controls gravitational time dilation: N = 1/(1 + α|R|)");

        AddNumericControl(tlpPhysicsConstants, "Time Dilation α (entropy):", ref numTimeDilationAlpha,
            (decimal)PhysicsConstants.TimeDilationAlpha, 0.1m, 0.0m, 5.0m, 2, ref startRow,
            "Entropic time dilation: N = exp(-α·S)");

        AddPhysicsHeader(tlpPhysicsConstants, "─── Wilson Fermion ───", ref startRow, Color.SteelBlue);

        AddNumericControl(tlpPhysicsConstants, "Wilson Parameter (r):", ref numWilsonParameter,
            (decimal)PhysicsConstants.WilsonParameter, 0.1m, 0.0m, 2.0m, 2, ref startRow,
            "Wilson term coefficient for fermion doubling suppression");

        // === RIGHT column: Topology Decoherence and below ===
        int rightRow = 0;

        AddPhysicsHeader(_tlpPhysicsConstantsRight, "─── Topology Decoherence ───", ref rightRow, Color.DarkCyan);

        AddNumericControl(_tlpPhysicsConstantsRight, "Topology Decoh. Interval:", ref numTopologyDecoherenceInterval,
            PhysicsConstants.TopologyDecoherenceInterval, 1, 1, 100, 0, ref rightRow,
            "Steps between topology updates (Zeno effect prevention)");

        AddNumericControl(_tlpPhysicsConstantsRight, "Topology Decoh. Temp:", ref numTopologyDecoherenceTemperature,
            (decimal)PhysicsConstants.TopologyDecoherenceTemperature, 0.1m, 0.01m, 10.0m, 2, ref rightRow,
            "Base temperature for adaptive topology flip probability");

        AddPhysicsHeader(_tlpPhysicsConstantsRight, "─── Gauge Protection ───", ref rightRow, Color.DarkMagenta);

        AddNumericControl(_tlpPhysicsConstantsRight, "Gauge Tolerance (rad):", ref numGaugeTolerance,
            (decimal)PhysicsConstants.GaugeTolerance, 0.01m, 0.01m, 1.0m, 3, ref rightRow,
            "Threshold for trivial gauge phase (Wilson loops)");

        AddNumericControl(_tlpPhysicsConstantsRight, "Max Removable Flux (rad):", ref numMaxRemovableFlux,
            (decimal)PhysicsConstants.MaxRemovableFlux, 0.1m, 0.1m, 3.14m, 2, ref rightRow,
            "Maximum flux for edge removal without redistribution");

        AddPhysicsHeader(_tlpPhysicsConstantsRight, "─── Geometry Inertia ───", ref rightRow, Color.DarkOliveGreen);

        AddNumericControl(_tlpPhysicsConstantsRight, "Geometry Inertia Mass:", ref numGeometryInertiaMass,
            (decimal)PhysicsConstants.GeometryInertiaMass, 1.0m, 0.1m, 100.0m, 1, ref rightRow,
            "Inertial mass of geometry (Hamiltonian gravity)");

        AddNumericControl(_tlpPhysicsConstantsRight, "Gauge Field Damping:", ref numGaugeFieldDamping,
            (decimal)PhysicsConstants.GaugeFieldDamping, 0.0001m, 0.0m, 0.1m, 4, ref rightRow,
            "Damping coefficient for gauge oscillations");

        AddPhysicsHeader(_tlpPhysicsConstantsRight, "─── Hawking Radiation ───", ref rightRow, Color.Crimson);

        AddNumericControl(_tlpPhysicsConstantsRight, "Pair Creation Mass Thresh:", ref numPairCreationMassThreshold,
            (decimal)PhysicsConstants.PairCreationMassThreshold, 0.01m, 0.001m, 1.0m, 3, ref rightRow,
            "Mass threshold for spontaneous pair creation");

        AddNumericControl(_tlpPhysicsConstantsRight, "Pair Creation Energy:", ref numPairCreationEnergy,
            (decimal)PhysicsConstants.PairCreationEnergy, 0.001m, 0.001m, 0.1m, 4, ref rightRow,
            "Energy extracted from geometry per pair creation");

        AddPhysicsHeader(_tlpPhysicsConstantsRight, "─── Spectral Action ───", ref rightRow, Color.Indigo);

        AddNumericControl(_tlpPhysicsConstantsRight, "Spectral Λ Cutoff:", ref numSpectralLambdaCutoff,
            (decimal)PhysicsConstants.SpectralActionConstants.LambdaCutoff, 0.1m, 0.1m, 10.0m, 2, ref rightRow,
            "UV cutoff scale for spectral action");

        AddNumericControl(_tlpPhysicsConstantsRight, "Target Spectral Dim:", ref numSpectralTargetDimension,
            (decimal)PhysicsConstants.SpectralActionConstants.TargetSpectralDimension, 0.5m, 1.0m, 10.0m, 1, ref rightRow,
            "Target dimension for spectral action minimum");

        AddNumericControl(_tlpPhysicsConstantsRight, "Dim Potential Strength:", ref numSpectralDimensionPotentialStrength,
            (decimal)PhysicsConstants.SpectralActionConstants.DimensionPotentialStrength, 0.01m, 0.0m, 1.0m, 3, ref rightRow,
            "Coupling for dimension stabilization potential");

        // === LEFT column: MCMC Sampler ===
        AddPhysicsHeader(tlpPhysicsConstants, "─── MCMC Sampler ───", ref startRow, Color.DarkOrange);

        AddNumericControl(tlpPhysicsConstants, "MCMC Beta (1/kT):", ref numMcmcBeta,
            1.0m, 0.1m, 0.01m, 100.0m, 2, ref startRow,
            "Inverse temperature for MCMC Metropolis-Hastings acceptance");

        AddNumericControl(tlpPhysicsConstants, "Steps Per Call:", ref numMcmcStepsPerCall,
            10m, 1m, 1m, 1000m, 0, ref startRow,
            "Number of MCMC proposal steps per simulation step");

        AddNumericControl(tlpPhysicsConstants, "Weight Perturbation:", ref numMcmcWeightPerturbation,
            0.1m, 0.01m, 0.001m, 1.0m, 3, ref startRow,
            "Magnitude of edge weight perturbation for MCMC proposals");

        // === RIGHT column: Sinkhorn (Ollivier-Ricci) ===
        AddPhysicsHeader(_tlpPhysicsConstantsRight, "─── Sinkhorn (Ollivier-Ricci) ───", ref rightRow, Color.Teal);

        AddNumericControl(_tlpPhysicsConstantsRight, "Sinkhorn Iterations:", ref numSinkhornIterations,
            50m, 10m, 10m, 500m, 0, ref rightRow,
            "Maximum iterations for Sinkhorn optimal transport");

        AddNumericControl(_tlpPhysicsConstantsRight, "Sinkhorn Epsilon:", ref numSinkhornEpsilon,
            0.01m, 0.001m, 0.0001m, 1.0m, 4, ref rightRow,
            "Entropic regularization ε for Sinkhorn algorithm");

        AddNumericControl(_tlpPhysicsConstantsRight, "Convergence Threshold:", ref numSinkhornConvergenceThreshold,
            0.000001m, 0.000001m, 0.0000001m, 0.01m, 7, ref rightRow,
            "Convergence threshold for Sinkhorn iterations");
    }


    /// <summary>
    /// Initializes additional RQ-Hypothesis experimental flag checkboxes.
    /// Called after InitializeRQExperimentalFlagsControls() to add more flags.
    /// </summary>
    private void InitializeAdditionalRQFlags()
    {
        // === Enable Symplectic Gauge Evolution ===
        chkEnableSymplecticGaugeEvolution = new CheckBox
        {
            AutoSize = true,
            Text = "Symplectic Gauge Evolution",
            Checked = PhysicsConstants.EnableSymplecticGaugeEvolution,
            Name = "chkEnableSymplecticGaugeEvolution",
            Margin = new Padding(3)
        };
        chkEnableSymplecticGaugeEvolution.CheckedChanged += OnRQExperimentalFlagChanged;
        flpPhysics.Controls.Add(chkEnableSymplecticGaugeEvolution);

        // === Enable Adaptive Topology Decoherence ===
        chkEnableAdaptiveTopologyDecoherence = new CheckBox
        {
            AutoSize = true,
            Text = "Adaptive Topology Decoherence",
            Checked = PhysicsConstants.EnableAdaptiveTopologyDecoherence,
            Name = "chkEnableAdaptiveTopologyDecoherence",
            Margin = new Padding(3)
        };
        chkEnableAdaptiveTopologyDecoherence.CheckedChanged += OnRQExperimentalFlagChanged;
        flpPhysics.Controls.Add(chkEnableAdaptiveTopologyDecoherence);

        // === Enable Wilson Loop Protection ===
        chkEnableWilsonLoopProtection = new CheckBox
        {
            AutoSize = true,
            Text = "Wilson Loop Protection",
            Checked = PhysicsConstants.EnableWilsonLoopProtection,
            Name = "chkEnableWilsonLoopProtection",
            Margin = new Padding(3)
        };
        chkEnableWilsonLoopProtection.CheckedChanged += OnRQExperimentalFlagChanged;
        flpPhysics.Controls.Add(chkEnableWilsonLoopProtection);

        // === Enable Spectral Action Mode ===
        chkEnableSpectralActionMode = new CheckBox
        {
            AutoSize = true,
            Text = "Spectral Action Mode",
            Checked = PhysicsConstants.SpectralActionConstants.EnableSpectralActionMode,
            Name = "chkEnableSpectralActionMode",
            Margin = new Padding(3)
        };
        chkEnableSpectralActionMode.CheckedChanged += OnRQExperimentalFlagChanged;
        flpPhysics.Controls.Add(chkEnableSpectralActionMode);

        // === Enable Wheeler-DeWitt Strict Mode ===
        chkEnableWheelerDeWittStrictMode = new CheckBox
        {
            AutoSize = true,
            Text = "Wheeler-DeWitt Strict Mode",
            Checked = PhysicsConstants.WheelerDeWittConstants.EnableStrictMode,
            Name = "chkEnableWheelerDeWittStrictMode",
            Margin = new Padding(3)
        };
        chkEnableWheelerDeWittStrictMode.CheckedChanged += OnRQExperimentalFlagChanged;
        flpPhysics.Controls.Add(chkEnableWheelerDeWittStrictMode);

        // === Use Hamiltonian Gravity ===
        chkUseHamiltonianGravity = new CheckBox
        {
            AutoSize = true,
            Text = "Use Hamiltonian Gravity",
            Checked = PhysicsConstants.UseHamiltonianGravity,
            Name = "chkUseHamiltonianGravity",
            Margin = new Padding(3)
        };
        chkUseHamiltonianGravity.CheckedChanged += OnRQExperimentalFlagChanged;
        flpPhysics.Controls.Add(chkUseHamiltonianGravity);

        // === Enable Vacuum Energy Reservoir ===
        chkEnableVacuumEnergyReservoir = new CheckBox
        {
            AutoSize = true,
            Text = "Vacuum Energy Reservoir",
            Checked = PhysicsConstants.EnableVacuumEnergyReservoir,
            Name = "chkEnableVacuumEnergyReservoir",
            Margin = new Padding(3)
        };
        chkEnableVacuumEnergyReservoir.CheckedChanged += OnRQExperimentalFlagChanged;
        flpPhysics.Controls.Add(chkEnableVacuumEnergyReservoir);

        // === Prefer Ollivier-Ricci Curvature ===
        chkPreferOllivierRicciCurvature = new CheckBox
        {
            AutoSize = true,
            Text = "Prefer Ollivier-Ricci Curvature",
            Checked = PhysicsConstants.PreferOllivierRicciCurvature,
            Name = "chkPreferOllivierRicciCurvature",
            Margin = new Padding(3)
        };
        chkPreferOllivierRicciCurvature.CheckedChanged += OnRQExperimentalFlagChanged;
        flpPhysics.Controls.Add(chkPreferOllivierRicciCurvature);
    }

    #region Helper Methods for Control Creation

    private void AddPhysicsHeader(TableLayoutPanel target, string text, ref int row, Color color)
    {
        target.RowCount = row + 1;
        target.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = color,
            Font = new Font(Font, FontStyle.Bold),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 8, 0, 2)
        };
        target.Controls.Add(label, 0, row);
        target.SetColumnSpan(label, 2);
        row++;
    }

    private void AddNumericControl(TableLayoutPanel target, string labelText, ref NumericUpDown? control,
        decimal value, decimal increment, decimal min, decimal max, int decimals,
        ref int row, string? tooltip = null)
    {
        target.RowCount = row + 1;
        target.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        control = new NumericUpDown
        {
            DecimalPlaces = decimals,
            Increment = increment,
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Dock = DockStyle.Fill
        };
        control.ValueChanged += OnAdvancedPhysicsParameterChanged;

        if (tooltip != null)
        {
            var tt = new ToolTip();
            tt.SetToolTip(control, tooltip);
            tt.SetToolTip(label, tooltip);
        }

        target.Controls.Add(label, 0, row);
        target.Controls.Add(control, 1, row);
        row++;
    }

    #endregion



    // === Graph Health UI Controls (RQ-Hypothesis compliance) ===
    private NumericUpDown numGiantClusterThreshold = null!;
    private NumericUpDown numEmergencyGiantClusterThreshold = null!;
    private NumericUpDown numGiantClusterDecoherenceRate = null!;
    private NumericUpDown numMaxDecoherenceEdgesFraction = null!;
    private NumericUpDown numCriticalSpectralDimension = null!;
    private NumericUpDown numWarningSpectralDimension = null!;

    // === RQ-Hypothesis Checklist Constants (Energy/Quantization) ===
    private NumericUpDown numEdgeWeightQuantum = null!;
    private NumericUpDown numRngStepCost = null!;
    private NumericUpDown numEdgeCreationCost = null!;
    private NumericUpDown numInitialVacuumEnergy = null!;

    // === AutoTuning Controls ===
    private NumericUpDown numAutoTuneTargetDimension = null!;
    private NumericUpDown numAutoTuneDimensionTolerance = null!;
    private NumericUpDown numAutoTuneEnergyRecyclingRate = null!;
    private NumericUpDown numAutoTuneGravityAdjustmentRate = null!;
    private NumericUpDown numAutoTuneExplorationProb = null!;

    // === All Physics Constants Panel (read-only display) ===
    private Panel panelAllPhysicsConstants = null!;
    private TableLayoutPanel tlpAllPhysicsConstants = null!;


    /// <summary>
    /// Synchronizes all UI controls with current PhysicsConstants values.
    /// Call this to refresh UI after loading a preset or configuration.
    /// </summary>
    public void SyncUIWithPhysicsConstants()
    {
        // Disable event handlers temporarily
        SuspendControlEvents();

        try
        {
            // === tlpSimParams controls ===
            // (values from Designer defaults, keep as is)

            // === tlpPhysicsConstants controls ===
            SetNumericValueSafe(numInitialEdgeProb, 0.035m); // Default from Designer
            SetNumericValueSafe(numGravitationalCoupling, (decimal)PhysicsConstants.GravitationalCoupling);
            SetNumericValueSafe(numVacuumEnergyScale, (decimal)PhysicsConstants.VacuumFluctuationScale);
            SetNumericValueSafe(numDecoherenceRate, (decimal)PhysicsConstants.GiantClusterDecoherenceRate);
            SetNumericValueSafe(numAdaptiveThresholdSigma, (decimal)PhysicsConstants.AdaptiveThresholdSigma);
            SetNumericValueSafe(numWarmupDuration, PhysicsConstants.WarmupDuration);
            SetNumericValueSafe(numGravityTransitionDuration, PhysicsConstants.GravityTransitionDuration);

            // === Graph Health controls ===
            SetNumericValueSafe(numGiantClusterThreshold, (decimal)PhysicsConstants.GiantClusterThreshold);
            SetNumericValueSafe(numEmergencyGiantClusterThreshold, (decimal)PhysicsConstants.EmergencyGiantClusterThreshold);
            SetNumericValueSafe(numGiantClusterDecoherenceRate, (decimal)PhysicsConstants.GiantClusterDecoherenceRate);
            SetNumericValueSafe(numMaxDecoherenceEdgesFraction, (decimal)PhysicsConstants.MaxDecoherenceEdgesFraction);
            SetNumericValueSafe(numCriticalSpectralDimension, (decimal)PhysicsConstants.CriticalSpectralDimension);
            SetNumericValueSafe(numWarningSpectralDimension, (decimal)PhysicsConstants.WarningSpectralDimension);

            // === RQ Checklist controls ===
            SetNumericValueSafe(numEdgeWeightQuantum, (decimal)PhysicsConstants.EdgeWeightQuantum);
            SetNumericValueSafe(numRngStepCost, (decimal)PhysicsConstants.RngStepCost);
            SetNumericValueSafe(numEdgeCreationCost, (decimal)PhysicsConstants.EdgeCreationCost);
            SetNumericValueSafe(numInitialVacuumEnergy, (decimal)PhysicsConstants.InitialVacuumEnergy);

            // === Advanced Physics controls ===
            SetNumericValueSafe(numLapseFunctionAlpha, (decimal)PhysicsConstants.LapseFunctionAlpha);
            SetNumericValueSafe(numWilsonParameter, (decimal)PhysicsConstants.WilsonParameter);
            SetNumericValueSafe(numTopologyDecoherenceInterval, PhysicsConstants.TopologyDecoherenceInterval);
            SetNumericValueSafe(numTopologyDecoherenceTemperature, (decimal)PhysicsConstants.TopologyDecoherenceTemperature);
            SetNumericValueSafe(numGaugeTolerance, (decimal)PhysicsConstants.GaugeTolerance);
            SetNumericValueSafe(numMaxRemovableFlux, (decimal)PhysicsConstants.MaxRemovableFlux);
            SetNumericValueSafe(numGeometryInertiaMass, (decimal)PhysicsConstants.GeometryInertiaMass);
            SetNumericValueSafe(numGaugeFieldDamping, (decimal)PhysicsConstants.GaugeFieldDamping);
            SetNumericValueSafe(numPairCreationMassThreshold, (decimal)PhysicsConstants.PairCreationMassThreshold);
            SetNumericValueSafe(numPairCreationEnergy, (decimal)PhysicsConstants.PairCreationEnergy);

            // === Spectral Action controls ===
            SetNumericValueSafe(numSpectralLambdaCutoff, (decimal)PhysicsConstants.SpectralActionConstants.LambdaCutoff);
            SetNumericValueSafe(numSpectralTargetDimension, (decimal)PhysicsConstants.SpectralActionConstants.TargetSpectralDimension);
            SetNumericValueSafe(numSpectralDimensionPotentialStrength, (decimal)PhysicsConstants.SpectralActionConstants.DimensionPotentialStrength);

            // === MCMC Sampler controls ===
            if (_currentPhysicsConfig is not null)
            {
                SetNumericValueSafe(numMcmcBeta, (decimal)_currentPhysicsConfig.McmcBeta);
                SetNumericValueSafe(numMcmcStepsPerCall, _currentPhysicsConfig.McmcStepsPerCall);
                SetNumericValueSafe(numMcmcWeightPerturbation, (decimal)_currentPhysicsConfig.McmcWeightPerturbation);

                // === Sinkhorn Ollivier-Ricci controls ===
                SetNumericValueSafe(numSinkhornIterations, _currentPhysicsConfig.SinkhornIterations);
                SetNumericValueSafe(numSinkhornEpsilon, (decimal)_currentPhysicsConfig.SinkhornEpsilon);
                SetNumericValueSafe(numSinkhornConvergenceThreshold, (decimal)_currentPhysicsConfig.SinkhornConvergenceThreshold);
            }

            // === Sync checkboxes with PhysicsConstants ===
            SyncCheckboxesWithConstants();
        }
        finally
        {
            ResumeControlEvents();
        }

        AppendSysConsole("[Settings] UI synchronized with PhysicsConstants\n");
    }

    private void SyncCheckboxesWithConstants()
    {
        // RQ Experimental flags
        if (chkEnableNaturalDimensionEmergence != null)
            chkEnableNaturalDimensionEmergence.Checked = PhysicsConstants.EnableNaturalDimensionEmergence;
        if (chkEnableTopologicalParity != null)
            chkEnableTopologicalParity.Checked = PhysicsConstants.EnableTopologicalParity;
        if (chkEnableLapseSynchronizedGeometry != null)
            chkEnableLapseSynchronizedGeometry.Checked = PhysicsConstants.EnableLapseSynchronizedGeometry;
        if (chkEnableTopologyEnergyCompensation != null)
            chkEnableTopologyEnergyCompensation.Checked = PhysicsConstants.EnableTopologyEnergyCompensation;
        if (chkEnablePlaquetteYangMills != null)
            chkEnablePlaquetteYangMills.Checked = PhysicsConstants.EnablePlaquetteYangMills;

        // Additional RQ flags
        if (chkEnableSymplecticGaugeEvolution != null)
            chkEnableSymplecticGaugeEvolution.Checked = PhysicsConstants.EnableSymplecticGaugeEvolution;
        if (chkEnableAdaptiveTopologyDecoherence != null)
            chkEnableAdaptiveTopologyDecoherence.Checked = PhysicsConstants.EnableAdaptiveTopologyDecoherence;
        if (chkEnableWilsonLoopProtection != null)
            chkEnableWilsonLoopProtection.Checked = PhysicsConstants.EnableWilsonLoopProtection;
        if (chkEnableSpectralActionMode != null)
            chkEnableSpectralActionMode.Checked = PhysicsConstants.SpectralActionConstants.EnableSpectralActionMode;
        if (chkEnableWheelerDeWittStrictMode != null)
            chkEnableWheelerDeWittStrictMode.Checked = PhysicsConstants.WheelerDeWittConstants.EnableStrictMode;
        if (chkUseHamiltonianGravity != null)
            chkUseHamiltonianGravity.Checked = PhysicsConstants.UseHamiltonianGravity;
        if (chkEnableVacuumEnergyReservoir != null)
            chkEnableVacuumEnergyReservoir.Checked = PhysicsConstants.EnableVacuumEnergyReservoir;
        if (chkPreferOllivierRicciCurvature != null)
            chkPreferOllivierRicciCurvature.Checked = PhysicsConstants.PreferOllivierRicciCurvature;
    }

    private bool _eventsSupressed = false;

    private void SuspendControlEvents()
    {
        _eventsSupressed = true;
    }

    private void ResumeControlEvents()
    {
        _eventsSupressed = false;
    }

    /// <summary>
    /// Saves current UI settings to shared location for RqSimConsole to read.
    /// Delegates to the unified settings serializer that writes a complete
    /// <see cref="RqSimPlatform.Contracts.ServerModeSettingsDto"/> covering all 70+ fields.
    /// </summary>
    private void SaveSharedSettingsForConsole()
    {
        SaveUnifiedSettingsFile();
    }
}
