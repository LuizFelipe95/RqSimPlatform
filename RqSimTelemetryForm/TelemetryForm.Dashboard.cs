using RqSimTelemetryForm.Helpers;

namespace RqSimTelemetryForm;

/// <summary>
/// Dashboard tab: live simulation metrics and time-series chart panels.
/// Programmatically creates all dashboard labels and chart panels.
/// </summary>
public partial class TelemetryForm
{
    // ============================================================
    // DASHBOARD LABELS (created programmatically)
    // ============================================================

    private Label _valNodes = null!;
    private Label _valCurrentStep = null!;
    private Label _valTotalSteps = null!;
    private Label _valExcited = null!;
    private Label _valHeavyMass = null!;
    private Label _valLargestCluster = null!;
    private Label _valStrongEdges = null!;
    private Label _valSpectralDim = null!;
    private Label _valEffectiveG = null!;
    private Label _valGSuppression = null!;
    private Label _valNetworkTemp = null!;
    private Label _valQNorm = null!;
    private Label _valEntanglement = null!;
    private Label _valCorrelation = null!;
    private Label _valStatus = null!;
    private Label _valPhase = null!;

    // ============================================================
    // CHART PANELS (Dashboard embedded)
    // ============================================================

    private Panel _panelExcitedChart = null!;
    private Panel _panelHeavyChart = null!;
    private Panel _panelClusterChart = null!;
    private Panel _panelEnergyChart = null!;

    // Charts tab has its own independent panels (same paint handlers)
    private Panel _chartsTabExcited = null!;
    private Panel _chartsTabHeavy = null!;
    private Panel _chartsTabCluster = null!;
    private Panel _chartsTabEnergy = null!;

    // ============================================================
    // INITIALIZATION
    // ============================================================

    private void InitializeDashboardTab()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(5)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // Left side: Live Metrics GroupBox
        var grpMetrics = new GroupBox
        {
            Text = "Live Simulation Metrics",
            Dock = DockStyle.Fill,
            Padding = new Padding(5),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly
        };

        var metricsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 18,
            AutoScroll = true
        };
        metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        for (int i = 0; i < 18; i++)
            metricsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        int row = 0;
        AddMetricRow(metricsLayout, "Nodes:", ref _valNodes, row++);
        AddMetricRow(metricsLayout, "Current Step:", ref _valCurrentStep, row++);
        AddMetricRow(metricsLayout, "Total Steps:", ref _valTotalSteps, row++);
        AddMetricRow(metricsLayout, "Status:", ref _valStatus, row++);
        AddMetricRow(metricsLayout, "Phase:", ref _valPhase, row++);

        // Separator
        AddSeparator(metricsLayout, row++);

        AddMetricRow(metricsLayout, "Excited:", ref _valExcited, row++);
        AddMetricRow(metricsLayout, "Heavy Mass:", ref _valHeavyMass, row++);
        AddMetricRow(metricsLayout, "Largest Cluster:", ref _valLargestCluster, row++);
        AddMetricRow(metricsLayout, "Strong Edges:", ref _valStrongEdges, row++);

        // Separator
        AddSeparator(metricsLayout, row++);

        AddMetricRow(metricsLayout, "d_S:", ref _valSpectralDim, row++);
        AddMetricRow(metricsLayout, "Effective G:", ref _valEffectiveG, row++);
        AddMetricRow(metricsLayout, "g Suppression:", ref _valGSuppression, row++);
        AddMetricRow(metricsLayout, "Temp:", ref _valNetworkTemp, row++);
        AddMetricRow(metricsLayout, "QNorm:", ref _valQNorm, row++);
        AddMetricRow(metricsLayout, "Entanglement:", ref _valEntanglement, row++);
        AddMetricRow(metricsLayout, "Correlation:", ref _valCorrelation, row++);

        grpMetrics.Controls.Add(metricsLayout);
        mainLayout.Controls.Add(grpMetrics, 0, 0);

        // Right side: Charts (2x2 grid)
        InitializeChartsTab();
        mainLayout.Controls.Add(CreateChartsPanel(), 1, 0);

        _tabDashboard.Controls.Add(mainLayout);
    }

    private static void AddMetricRow(TableLayoutPanel tlp, string labelText, ref Label valueLabel, int row)
    {
        var lbl = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(3)
        };

        valueLabel = new Label
        {
            Text = "—",
            AutoSize = true,
            Font = new Font("Consolas", 10F, FontStyle.Bold),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3)
        };

        tlp.Controls.Add(lbl, 0, row);
        tlp.Controls.Add(valueLabel, 1, row);
    }

    private static void AddSeparator(TableLayoutPanel tlp, int row)
    {
        var sep = new Label
        {
            Height = 2,
            Dock = DockStyle.Fill,
            BackColor = SystemColors.ControlDark
        };
        tlp.Controls.Add(sep, 0, row);
        tlp.SetColumnSpan(sep, 2);
    }

    // ============================================================
    // CHARTS TAB (also embedded in Dashboard)
    // ============================================================

    private Panel CreateChartsPanel()
    {
        var chartsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        chartsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        chartsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        chartsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        chartsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        _panelExcitedChart = CreateChartPanel("Excited Nodes");
        _panelExcitedChart.Paint += PanelExcitedChart_Paint;
        chartsLayout.Controls.Add(_panelExcitedChart, 0, 0);

        _panelHeavyChart = CreateChartPanel("Heavy Mass");
        _panelHeavyChart.Paint += PanelHeavyChart_Paint;
        chartsLayout.Controls.Add(_panelHeavyChart, 1, 0);

        _panelClusterChart = CreateChartPanel("Largest Cluster");
        _panelClusterChart.Paint += PanelClusterChart_Paint;
        chartsLayout.Controls.Add(_panelClusterChart, 0, 1);

        _panelEnergyChart = CreateChartPanel("Energy vs Temp");
        _panelEnergyChart.Paint += PanelEnergyChart_Paint;
        chartsLayout.Controls.Add(_panelEnergyChart, 1, 1);

        return chartsLayout;
    }

    private static Panel CreateChartPanel(string accessibleName)
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(4),
            BackColor = Color.White,
            AccessibleName = accessibleName,
            AccessibleDescription = $"Time series chart: {accessibleName}"
        };
    }

    private void InitializeChartsTab()
    {
        // Charts tab gets its own standalone 2x2 grid
        if (_tabCharts.Controls.Count > 0) return;

        var chartsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        chartsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        chartsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        chartsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        chartsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        _chartsTabExcited = CreateChartPanel("Excited Nodes");
        _chartsTabExcited.Paint += PanelExcitedChart_Paint;
        chartsLayout.Controls.Add(_chartsTabExcited, 0, 0);

        _chartsTabHeavy = CreateChartPanel("Heavy Mass");
        _chartsTabHeavy.Paint += PanelHeavyChart_Paint;
        chartsLayout.Controls.Add(_chartsTabHeavy, 1, 0);

        _chartsTabCluster = CreateChartPanel("Largest Cluster");
        _chartsTabCluster.Paint += PanelClusterChart_Paint;
        chartsLayout.Controls.Add(_chartsTabCluster, 0, 1);

        _chartsTabEnergy = CreateChartPanel("Energy vs Temp");
        _chartsTabEnergy.Paint += PanelEnergyChart_Paint;
        chartsLayout.Controls.Add(_chartsTabEnergy, 1, 1);

        _tabCharts.Controls.Add(chartsLayout);
    }

    // ============================================================
    // DASHBOARD UPDATE
    // ============================================================

    private void UpdateDashboard(int step, int totalSteps, int excited, double heavyMass,
        int largestCluster, int strongEdges, string phase, double qNorm,
        double entanglement, double correlation)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateDashboard(step, totalSteps, excited, heavyMass, largestCluster,
                strongEdges, phase, qNorm, entanglement, correlation));
            return;
        }

        int nodeCount = _simApi.Dispatcher.LiveNodeCount;
        _valNodes.Text = nodeCount > 0
            ? nodeCount.ToString()
            : _simulationEngine?.Graph?.N.ToString() ?? "0";
        _valTotalSteps.Text = totalSteps.ToString();
        _valCurrentStep.Text = step.ToString();
        _valExcited.Text = excited.ToString();
        _valHeavyMass.Text = heavyMass.ToString("F2");
        _valLargestCluster.Text = largestCluster.ToString();
        _valStrongEdges.Text = strongEdges.ToString();
        _valPhase.Text = phase;
        _valQNorm.Text = qNorm.ToString("F6");
        _valEntanglement.Text = entanglement.ToString("F6");
        _valCorrelation.Text = correlation.ToString("F6");
        _valStatus.Text = _simApi.IsModernRunning ? "Running..." : "Ready";

        // Spectral metrics
        double spectralDim = _simApi.LiveSpectralDim;
        double effectiveG = _simApi.LiveEffectiveG;
        double networkTemp = _simApi.LiveTemp;

        double targetG = _simApi.LastConfig?.GravitationalCoupling ?? 0.2;
        double gSuppression = targetG > 0 ? effectiveG / targetG : 1.0;
        gSuppression = Math.Clamp(gSuppression, 0.0, 2.0);

        _valSpectralDim.Text = spectralDim.ToString("F3");
        _valEffectiveG.Text = effectiveG.ToString("F4");
        _valGSuppression.Text = gSuppression.ToString("F3");
        _valNetworkTemp.Text = networkTemp.ToString("F3");

        ColorCodeSpectralDim(spectralDim);

        // Color-code gSuppression
        if (gSuppression < 0.3)
            _valGSuppression.ForeColor = Color.Red;
        else if (gSuppression < 0.7)
            _valGSuppression.ForeColor = Color.DarkOrange;
        else
            _valGSuppression.ForeColor = SystemColors.ControlText;

        // Color-code LargestCluster by ratio
        double clusterRatio = _simApi.Dispatcher.LiveClusterRatio;
        if (clusterRatio >= 0.7)
            _valLargestCluster.ForeColor = Color.Red;
        else if (clusterRatio >= 0.5)
            _valLargestCluster.ForeColor = Color.DarkOrange;
        else
            _valLargestCluster.ForeColor = Color.Green;

        _valLargestCluster.Text = $"{largestCluster} ({clusterRatio:P0})";
    }

    private void UpdateStatusBar(int currentStep, int totalSteps, int currentOn, double avgExcited, double heavyMass)
    {
        _statusLabelSteps.Text = $"Step: {currentStep}/{totalSteps}";
        _statusLabelExcited.Text = $"Excited: {currentOn} (avg {avgExcited:F2})";

        double clusterRatio = _simApi.Dispatcher.LiveClusterRatio;
        double avgDegree = _simApi.Dispatcher.LiveAvgDegree;
        int edgeCount = _simApi.Dispatcher.LiveEdgeCount;
        int componentCount = _simApi.Dispatcher.LiveComponentCount;

        _statusLabelTopology.Text = $"Giant:{clusterRatio:P0} | E:{edgeCount} | <k>:{avgDegree:F1} | Comp:{componentCount}";
    }

    // ============================================================
    // CHART PAINT HANDLERS
    // ============================================================

    private void PanelExcitedChart_Paint(object? sender, PaintEventArgs e)
    {
        Panel panel = (Panel)sender!;

        if (!_hasApiConnection && !_isExternalSimulation)
        {
            DrawWaitingForConnection(e.Graphics, panel, "Excited Nodes");
            return;
        }

        if (_isExternalSimulation)
        {
            int[] steps = _ipcTimeSeries.GetDecimatedSteps();
            int[] values = _ipcTimeSeries.GetDecimatedExcited();
            if (steps.Length == 0)
            {
                DrawNoDataYet(e.Graphics, panel, "Excited Nodes");
                return;
            }
            ChartRenderer.DrawSimpleLineChartFast(e.Graphics, panel.Width, panel.Height,
                steps, values, "Excited nodes", Color.Red);
            return;
        }

        var data = _simApi.Dispatcher.ForceGetDisplayDataImmediate(timeoutMs: 20);
        if (data.DecimatedSteps.Length == 0)
        {
            DrawNoDataYet(e.Graphics, panel, "Excited Nodes");
            return;
        }
        ChartRenderer.DrawSimpleLineChartFast(e.Graphics, panel.Width, panel.Height,
            data.DecimatedSteps, data.DecimatedExcited, "Excited nodes", Color.Red);
    }

    private void PanelHeavyChart_Paint(object? sender, PaintEventArgs e)
    {
        Panel panel = (Panel)sender!;

        if (!_hasApiConnection && !_isExternalSimulation)
        {
            DrawWaitingForConnection(e.Graphics, panel, "Heavy Mass");
            return;
        }

        if (_isExternalSimulation)
        {
            int[] steps = _ipcTimeSeries.GetDecimatedSteps();
            double[] values = _ipcTimeSeries.GetDecimatedHeavyMass();
            if (steps.Length == 0)
            {
                DrawNoDataYet(e.Graphics, panel, "Heavy Mass");
                return;
            }
            ChartRenderer.DrawSimpleLineChartFast(e.Graphics, panel.Width, panel.Height,
                steps, values, "Heavy mass", Color.DarkOrange);
            return;
        }

        var data = _simApi.Dispatcher.ForceGetDisplayDataImmediate(timeoutMs: 20);
        if (data.DecimatedSteps.Length == 0)
        {
            DrawNoDataYet(e.Graphics, panel, "Heavy Mass");
            return;
        }
        ChartRenderer.DrawSimpleLineChartFast(e.Graphics, panel.Width, panel.Height,
            data.DecimatedSteps, data.DecimatedHeavyMass, "Heavy mass", Color.DarkOrange);
    }

    private void PanelClusterChart_Paint(object? sender, PaintEventArgs e)
    {
        Panel panel = (Panel)sender!;

        if (!_hasApiConnection && !_isExternalSimulation)
        {
            DrawWaitingForConnection(e.Graphics, panel, "Largest Cluster");
            return;
        }

        if (_isExternalSimulation)
        {
            int[] steps = _ipcTimeSeries.GetDecimatedSteps();
            int[] values = _ipcTimeSeries.GetDecimatedLargestCluster();
            if (steps.Length == 0)
            {
                DrawNoDataYet(e.Graphics, panel, "Largest Cluster");
                return;
            }
            ChartRenderer.DrawSimpleLineChartFast(e.Graphics, panel.Width, panel.Height,
                steps, values, "Largest cluster size", Color.Blue);
            return;
        }

        var data = _simApi.Dispatcher.ForceGetDisplayDataImmediate(timeoutMs: 20);
        if (data.DecimatedSteps.Length == 0)
        {
            DrawNoDataYet(e.Graphics, panel, "Largest Cluster");
            return;
        }
        ChartRenderer.DrawSimpleLineChartFast(e.Graphics, panel.Width, panel.Height,
            data.DecimatedSteps, data.DecimatedLargestCluster, "Largest cluster size", Color.Blue);
    }

    private void PanelEnergyChart_Paint(object? sender, PaintEventArgs e)
    {
        Panel panel = (Panel)sender!;

        if (!_hasApiConnection && !_isExternalSimulation)
        {
            DrawWaitingForConnection(e.Graphics, panel, "Energy vs Temp");
            return;
        }

        if (_isExternalSimulation)
        {
            int[] steps = _ipcTimeSeries.GetDecimatedSteps();
            double[] energy = _ipcTimeSeries.GetDecimatedEnergy();
            double[] temp = _ipcTimeSeries.GetDecimatedNetworkTemp();
            if (steps.Length == 0)
            {
                DrawNoDataYet(e.Graphics, panel, "Energy vs Temp");
                return;
            }

            ChartRenderer.DrawDualLineChartFast(
                e.Graphics,
                panel.Width,
                panel.Height,
                steps, energy, temp,
                "Energy vs Network Temp",
                Color.ForestGreen,
                Color.MediumSlateBlue);
            return;
        }

        var data = _simApi.Dispatcher.ForceGetDisplayDataImmediate(timeoutMs: 20);
        if (data.DecimatedSteps.Length == 0)
        {
            DrawNoDataYet(e.Graphics, panel, "Energy vs Temp");
            return;
        }

        if (data.DecimatedNetworkTemp.Length == 0)
        {
            ChartRenderer.DrawSimpleLineChartFast(e.Graphics, panel.Width, panel.Height,
                data.DecimatedSteps, data.DecimatedEnergy, "Total energy", Color.Green);
            return;
        }

        ChartRenderer.DrawDualLineChartFast(
            e.Graphics,
            panel.Width,
            panel.Height,
            data.DecimatedSteps,
            data.DecimatedEnergy,
            data.DecimatedNetworkTemp,
            "Energy vs Network Temp",
            Color.ForestGreen,
            Color.MediumSlateBlue);
    }

    // ============================================================
    // CHART PLACEHOLDER DRAWING
    // ============================================================

    private static void DrawWaitingForConnection(Graphics g, Panel panel, string chartTitle)
    {
        g.Clear(Color.FromArgb(250, 250, 252));

        using Font titleFont = new("Segoe UI", 10F, FontStyle.Bold);
        g.DrawString(chartTitle, titleFont, Brushes.DarkSlateGray, 10, 8);

        string msg = "\u23F3 Waiting for simulation connection...";
        using Font msgFont = new("Segoe UI", 9F, FontStyle.Italic);
        SizeF msgSize = g.MeasureString(msg, msgFont);
        float x = (panel.Width - msgSize.Width) / 2;
        float y = (panel.Height - msgSize.Height) / 2;
        g.DrawString(msg, msgFont, Brushes.Gray, x, y);

        string hint = "Launch RqSimConsole or connect via RqSimUI";
        SizeF hintSize = g.MeasureString(hint, msgFont);
        g.DrawString(hint, msgFont, Brushes.LightGray,
            (panel.Width - hintSize.Width) / 2, y + msgSize.Height + 4);
    }

    private static void DrawNoDataYet(Graphics g, Panel panel, string chartTitle)
    {
        g.Clear(Color.White);

        using Font titleFont = new("Segoe UI", 10F, FontStyle.Bold);
        g.DrawString(chartTitle, titleFont, Brushes.DarkSlateGray, 10, 8);

        string msg = "No data yet — waiting for simulation steps...";
        using Font msgFont = new("Segoe UI", 9F);
        SizeF msgSize = g.MeasureString(msg, msgFont);
        g.DrawString(msg, msgFont, Brushes.Gray,
            (panel.Width - msgSize.Width) / 2,
            (panel.Height - msgSize.Height) / 2);
    }

    private static void DrawExternalModeMessage(Graphics g, Panel panel, string chartTitle)
    {
        g.Clear(Color.FromArgb(252, 252, 248));

        using Font titleFont = new("Segoe UI", 10F, FontStyle.Bold);
        g.DrawString(chartTitle, titleFont, Brushes.DarkSlateGray, 10, 8);

        string msg = "Console mode (IPC)";
        using Font msgFont = new("Segoe UI", 9F, FontStyle.Italic);
        SizeF msgSize = g.MeasureString(msg, msgFont);
        float cx = (panel.Width - msgSize.Width) / 2;
        float cy = (panel.Height - msgSize.Height) / 2 - 10;
        g.DrawString(msg, msgFont, Brushes.Gray, cx, cy);

        string hint = "Time-series charts require API connection (RqSimUI)";
        SizeF hintSize = g.MeasureString(hint, msgFont);
        g.DrawString(hint, msgFont, Brushes.DarkGray,
            (panel.Width - hintSize.Width) / 2, cy + msgSize.Height + 4);
    }
}
