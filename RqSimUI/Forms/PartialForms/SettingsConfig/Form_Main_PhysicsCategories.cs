using RQSimulation;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main - Categorized Physics UI.
/// Provides organized display of physics constants by category with collapsible sections.
/// </summary>
public partial class Form_Main_RqSim
{
    private Panel? _physicsNavigationPanel;
    private Dictionary<string, GroupBox> _physicsCategories = new();

    /// <summary>
    /// Creates a navigation panel for physics categories.
    /// Allows quick navigation between different physics parameter sections.
    /// </summary>
    private void InitializePhysicsCategoryNavigation()
    {
        // Create navigation panel at top of Settings tab
        _physicsNavigationPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 35,
            BackColor = Color.FromArgb(240, 245, 250),
            BorderStyle = BorderStyle.FixedSingle
        };

        var flowNav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true
        };

        // Create category buttons
        var categories = new[]
        {
            ("??", "Fundamental", "Jump to fundamental physics constants"),
            ("?", "Gauge", "Jump to gauge coupling constants"),
            ("??", "Simulation", "Jump to simulation parameters"),
            ("??", "RQ-Hyp", "Jump to RQ-Hypothesis experimental flags"),
            ("??", "Parameters", "Jump to RQ-Hypothesis parameters"),
            ("??", "Spectral", "Jump to Spectral Action (NCG) constants"),
            ("???", "WDW", "Jump to Wheeler-DeWitt constraint"),
            ("??", "Health", "Jump to Graph Health parameters"),
            ("?", "Higgs", "Jump to Higgs mechanism parameters")
        };

        foreach (var (icon, name, tooltip) in categories)
        {
            var btn = new Button
            {
                Text = $"{icon} {name}",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Margin = new Padding(2),
                Tag = name
            };
            btn.FlatAppearance.BorderColor = Color.LightGray;
            btn.Click += PhysicsCategoryButton_Click;

            var tt = new ToolTip();
            tt.SetToolTip(btn, tooltip);

            flowNav.Controls.Add(btn);
        }

        _physicsNavigationPanel.Controls.Add(flowNav);
        tabPage_Settings.Controls.Add(_physicsNavigationPanel);
        _physicsNavigationPanel.BringToFront();
    }

    private void PhysicsCategoryButton_Click(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string categoryName)
            return;

        // Find corresponding GroupBox and scroll to it
        if (_physicsCategories.TryGetValue(categoryName, out var groupBox))
        {
            groupBox.Focus();
            settingsMainLayout.ScrollControlIntoView(groupBox);
        }

        // Visual feedback - highlight the clicked button
        if (_physicsNavigationPanel != null)
        {
            foreach (Control ctrl in _physicsNavigationPanel.Controls)
            {
                if (ctrl is FlowLayoutPanel flow)
                {
                    foreach (Control c in flow.Controls)
                    {
                        if (c is Button b)
                        {
                            b.BackColor = b == btn ? Color.FromArgb(200, 220, 255) : Color.White;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Creates a summary panel showing key physics constants in real-time.
    /// Useful for monitoring during simulation.
    /// </summary>
    private GroupBox CreatePhysicsSummaryPanel()
    {
        var grpSummary = new GroupBox
        {
            Text = "?? Active Physics Configuration",
            Dock = DockStyle.Top,
            Height = 150,
            BackColor = Color.FromArgb(250, 252, 255)
        };

        var tlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 5,
            Padding = new Padding(5)
        };

        // Column styles
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        // Row 0: Gauge couplings
        AddSummaryItem(tlp, 0, 0, "? (EM):", $"{PhysicsConstants.FineStructureConstant:E3}");
        AddSummaryItem(tlp, 0, 1, "?_s:", $"{PhysicsConstants.StrongCouplingConstant:F3}");
        AddSummaryItem(tlp, 0, 2, "sin??_W:", $"{PhysicsConstants.WeakMixingAngle:F4}");
        AddSummaryItem(tlp, 0, 3, "G:", $"{PhysicsConstants.GravitationalCoupling:F3}");

        // Row 1: RQ-Hypothesis flags
        AddSummaryFlag(tlp, 1, 0, "Hamiltonian", PhysicsConstants.UseHamiltonianGravity);
        AddSummaryFlag(tlp, 1, 1, "Nat.Dim", PhysicsConstants.EnableNaturalDimensionEmergence);
        AddSummaryFlag(tlp, 1, 2, "Lapse-Sync", PhysicsConstants.EnableLapseSynchronizedGeometry);
        AddSummaryFlag(tlp, 1, 3, "WilsonLoop", PhysicsConstants.EnableWilsonLoopProtection);

        // Row 2: Key parameters
        AddSummaryItem(tlp, 2, 0, "Target d_S:", $"{PhysicsConstants.SpectralActionConstants.TargetSpectralDimension:F1}");
        AddSummaryItem(tlp, 2, 1, "Geom.Mass:", $"{PhysicsConstants.GeometryInertiaMass:F1}");
        AddSummaryItem(tlp, 2, 2, "Wilson r:", $"{PhysicsConstants.WilsonParameter:F1}");
        AddSummaryItem(tlp, 2, 3, "Lapse ?:", $"{PhysicsConstants.LapseFunctionAlpha:F2}");

        // Row 3: Simulation params
        AddSummaryItem(tlp, 3, 0, "dt:", $"{PhysicsConstants.BaseTimestep:F3}");
        AddSummaryItem(tlp, 3, 1, "Warmup:", $"{PhysicsConstants.WarmupDuration}");
        AddSummaryItem(tlp, 3, 2, "TopUpdate:", $"{PhysicsConstants.TopologyUpdateInterval}");
        AddSummaryItem(tlp, 3, 3, "EdgeQ:", $"{PhysicsConstants.EdgeWeightQuantum:F3}");

        // Row 4: Health thresholds
        AddSummaryItem(tlp, 4, 0, "Crit.d_S:", $"{PhysicsConstants.CriticalSpectralDimension:F1}");
        AddSummaryItem(tlp, 4, 1, "GiantClust:", $"{PhysicsConstants.GiantClusterThreshold:P0}");
        AddSummaryItem(tlp, 4, 2, "Decoh.Rate:", $"{PhysicsConstants.GiantClusterDecoherenceRate:F2}");
        AddSummaryItem(tlp, 4, 3, "VacFluc:", $"{PhysicsConstants.VacuumFluctuationScale:F3}");

        grpSummary.Controls.Add(tlp);
        return grpSummary;
    }

    private void AddSummaryItem(TableLayoutPanel tlp, int row, int col, string label, string value)
    {
        var pnl = new Panel { Dock = DockStyle.Fill };
        
        var lbl = new Label
        {
            Text = label,
            Font = new Font(Font.FontFamily, 8, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(2, 2)
        };
        
        var val = new Label
        {
            Text = value,
            Font = new Font("Consolas", 8),
            ForeColor = Color.DarkBlue,
            AutoSize = true,
            Location = new Point(2, 15)
        };

        pnl.Controls.AddRange(new Control[] { lbl, val });
        tlp.Controls.Add(pnl, col, row);
    }

    private void AddSummaryFlag(TableLayoutPanel tlp, int row, int col, string label, bool enabled)
    {
        var pnl = new Panel { Dock = DockStyle.Fill };
        
        var lbl = new Label
        {
            Text = $"{(enabled ? "?" : "?")} {label}",
            Font = new Font(Font.FontFamily, 8),
            ForeColor = enabled ? Color.DarkGreen : Color.Gray,
            AutoSize = true,
            Location = new Point(2, 8)
        };

        pnl.Controls.Add(lbl);
        tlp.Controls.Add(pnl, col, row);
    }

    /// <summary>
    /// Gets a formatted string describing the current physics configuration.
    /// Useful for logging and export.
    /// </summary>
    public string GetPhysicsConfigurationSummary()
    {
        var sb = new System.Text.StringBuilder();
        
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine("           RQ-SIMULATOR PHYSICS CONFIGURATION              ");
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine();

        sb.AppendLine("???? FUNDAMENTAL CONSTANTS (Planck units) ??????????????????");
        sb.AppendLine($"? c = {PhysicsConstants.C}, ? = {PhysicsConstants.HBar}, G = {PhysicsConstants.G}, k_B = {PhysicsConstants.KBoltzmann}");
        sb.AppendLine($"? l_P = {PhysicsConstants.PlanckLength}, t_P = {PhysicsConstants.PlanckTime}, m_P = {PhysicsConstants.PlanckMass}");
        sb.AppendLine("????????????????????????????????????????????????????????????");
        sb.AppendLine();

        sb.AppendLine("???? GAUGE COUPLINGS (dimensionless) ???????????????????????");
        sb.AppendLine($"? ? = 1/{1.0/PhysicsConstants.FineStructureConstant:F2} ? {PhysicsConstants.FineStructureConstant:E4}");
        sb.AppendLine($"? ?_s(M_Z) = {PhysicsConstants.StrongCouplingConstant:F4}");
        sb.AppendLine($"? sin??_W = {PhysicsConstants.WeakMixingAngle:F5}");
        sb.AppendLine($"? g_W = {PhysicsConstants.WeakCouplingConstant:F4}, g' = {PhysicsConstants.HyperchargeCoupling:F4}");
        sb.AppendLine("????????????????????????????????????????????????????????????");
        sb.AppendLine();

        sb.AppendLine("???? RQ-HYPOTHESIS FLAGS ??????????????????????????????????");
        sb.AppendLine($"? Hamiltonian Gravity:        {Flag(PhysicsConstants.UseHamiltonianGravity)}");
        sb.AppendLine($"? Natural Dimension:          {Flag(PhysicsConstants.EnableNaturalDimensionEmergence)}");
        sb.AppendLine($"? Lapse-Synchronized:         {Flag(PhysicsConstants.EnableLapseSynchronizedGeometry)}");
        sb.AppendLine($"? Topological Parity:         {Flag(PhysicsConstants.EnableTopologicalParity)}");
        sb.AppendLine($"? Topology Energy Comp:       {Flag(PhysicsConstants.EnableTopologyEnergyCompensation)}");
        sb.AppendLine($"? Plaquette Yang-Mills:       {Flag(PhysicsConstants.EnablePlaquetteYangMills)}");
        sb.AppendLine($"? Symplectic Gauge:           {Flag(PhysicsConstants.EnableSymplecticGaugeEvolution)}");
        sb.AppendLine($"? Wilson Loop Protection:     {Flag(PhysicsConstants.EnableWilsonLoopProtection)}");
        sb.AppendLine($"? Vacuum Energy Reservoir:    {Flag(PhysicsConstants.EnableVacuumEnergyReservoir)}");
        sb.AppendLine($"? Ollivier-Ricci Curvature:   {Flag(PhysicsConstants.PreferOllivierRicciCurvature)}");
        sb.AppendLine("????????????????????????????????????????????????????????????");
        sb.AppendLine();

        sb.AppendLine("???? SPECTRAL ACTION (NCG) ????????????????????????????????");
        sb.AppendLine($"? Enabled: {Flag(PhysicsConstants.SpectralActionConstants.EnableSpectralActionMode)}");
        sb.AppendLine($"? ?_cutoff = {PhysicsConstants.SpectralActionConstants.LambdaCutoff}");
        sb.AppendLine($"? Target d_S = {PhysicsConstants.SpectralActionConstants.TargetSpectralDimension}");
        sb.AppendLine($"? f? = {PhysicsConstants.SpectralActionConstants.F0_Cosmological}, f? = {PhysicsConstants.SpectralActionConstants.F2_EinsteinHilbert}, f? = {PhysicsConstants.SpectralActionConstants.F4_Weyl}");
        sb.AppendLine("????????????????????????????????????????????????????????????");
        sb.AppendLine();

        sb.AppendLine("???? WHEELER-DEWITT CONSTRAINT ???????????????????????????");
        sb.AppendLine($"? Strict Mode: {Flag(PhysicsConstants.WheelerDeWittConstants.EnableStrictMode)}");
        sb.AppendLine($"? ? = {PhysicsConstants.WheelerDeWittConstants.GravitationalCoupling}");
        sb.AppendLine($"? Tolerance = {PhysicsConstants.WheelerDeWittConstants.ConstraintTolerance}");
        sb.AppendLine($"? ?_Lagrange = {PhysicsConstants.WheelerDeWittConstants.ConstraintLagrangeMultiplier}");
        sb.AppendLine("????????????????????????????????????????????????????????????");
        sb.AppendLine();

        sb.AppendLine("???????????????????????????????????????????????????????????");
        
        return sb.ToString();
    }

    private static string Flag(bool value) => value ? "? ENABLED" : "? disabled";
}
