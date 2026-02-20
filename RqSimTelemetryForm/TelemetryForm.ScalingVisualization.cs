using System.Drawing.Drawing2D;
using System.Globalization;
using RqSimGraphEngine.Experiments.Definitions;

namespace RqSimTelemetryForm;

/// <summary>
/// Vacuum Energy Scaling visualization (Log-Log chart).
/// Provides log-log chart of vacuum energy vs N, linear regression with slope alpha,
/// scientific verdict display, and export buttons (CSV, JSON, Report).
/// </summary>
public partial class TelemetryForm
{
    // ============================================================
    // SCALING VISUALIZATION STATE
    // ============================================================

    private Panel? _scalingChartPanel;
    private Label? _lblScalingVerdict;
    private Label? _lblScalingAlpha;
    private Label? _lblScalingRSquared;
    private Button? _btnScalingExport;
    private GroupBox? _grpScalingViz;
    private List<ScalingDataPoint> _scalingData = [];

    // ============================================================
    // INITIALIZATION
    // ============================================================

    private void InitializeScalingVisualization(Control parent)
    {
        _grpScalingViz = new GroupBox
        {
            Text = "Vacuum Energy Scaling (Log-Log)",
            Dock = DockStyle.Bottom,
            Height = 340,
            Padding = new Padding(6)
        };

        var outerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75F));
        outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // Left: Chart panel
        _scalingChartPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(3)
        };
        _scalingChartPanel.Paint += ScalingChartPanel_Paint;
        outerLayout.Controls.Add(_scalingChartPanel, 0, 0);

        // Right: Stats + Export
        var statsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(4)
        };
        statsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        statsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblScalingAlpha = new Label
        {
            Text = "\u03B1 (slope): \u2014",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        statsLayout.Controls.Add(_lblScalingAlpha, 0, 0);

        _lblScalingRSquared = new Label
        {
            Text = "R\u00B2: \u2014",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        statsLayout.Controls.Add(_lblScalingRSquared, 0, 1);

        _lblScalingVerdict = new Label
        {
            Text = "Verdict: No data",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        statsLayout.Controls.Add(_lblScalingVerdict, 0, 2);

        statsLayout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 3);

        _btnScalingExport = new Button
        {
            Text = "Export Results...",
            Dock = DockStyle.Fill,
            Height = 30
        };
        _btnScalingExport.Click += BtnScalingExport_Click;
        statsLayout.Controls.Add(_btnScalingExport, 0, 4);

        var btnClear = new Button
        {
            Text = "Clear Data",
            Dock = DockStyle.Fill,
            Height = 28
        };
        btnClear.Click += BtnScalingClear_Click;
        statsLayout.Controls.Add(btnClear, 0, 5);

        outerLayout.Controls.Add(statsLayout, 1, 0);
        _grpScalingViz.Controls.Add(outerLayout);
        parent.Controls.Add(_grpScalingViz);
    }

    /// <summary>
    /// Updates the scaling visualization with new data points. Thread-safe.
    /// </summary>
    public void UpdateScalingVisualization(IReadOnlyList<ScalingDataPoint> data)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateScalingVisualization(data));
            return;
        }

        _scalingData = [.. data];
        RefreshScalingStats();
        _scalingChartPanel?.Invalidate();
    }

    /// <summary>
    /// Adds a single data point to the scaling visualization. Thread-safe.
    /// </summary>
    public void AddScalingDataPoint(ScalingDataPoint point)
    {
        if (InvokeRequired)
        {
            Invoke(() => AddScalingDataPoint(point));
            return;
        }

        _scalingData.Add(point);
        RefreshScalingStats();
        _scalingChartPanel?.Invalidate();
    }

    // ============================================================
    // STATS REFRESH
    // ============================================================

    private void RefreshScalingStats()
    {
        if (_scalingData.Count < 2)
        {
            _lblScalingAlpha!.Text = "\u03B1 (slope): \u2014";
            _lblScalingRSquared!.Text = "R\u00B2: \u2014";
            _lblScalingVerdict!.Text = _scalingData.Count == 0
                ? "Verdict: No data"
                : "Verdict: Need \u2265 2 points for regression";
            _lblScalingVerdict.ForeColor = SystemColors.GrayText;
            return;
        }

        ScalingResultExporter.RegressionResult reg = ScalingResultExporter.ComputeLogLogRegression(_scalingData);

        _lblScalingAlpha!.Text = $"\u03B1 (slope): {reg.Alpha:F4}";
        _lblScalingRSquared!.Text = $"R\u00B2: {reg.RSquared:F4}";

        string verdict = ScalingResultExporter.InterpretAlpha(reg.Alpha);
        _lblScalingVerdict!.Text = $"Verdict: {verdict}";

        _lblScalingVerdict.ForeColor = reg.Alpha switch
        {
            < -0.85 => Color.DarkGreen,
            < -0.35 => Color.DarkOrange,
            _ => Color.DarkRed
        };
    }

    // ============================================================
    // CHART RENDERING
    // ============================================================

    private void ScalingChartPanel_Paint(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        Rectangle bounds = _scalingChartPanel!.ClientRectangle;

        int marginLeft = 70;
        int marginBottom = 40;
        int marginTop = 25;
        int marginRight = 20;

        Rectangle chartRect = new(
            marginLeft, marginTop,
            bounds.Width - marginLeft - marginRight,
            bounds.Height - marginTop - marginBottom);

        if (chartRect.Width < 40 || chartRect.Height < 40) return;

        using Pen borderPen = new(Color.LightGray);
        g.DrawRectangle(borderPen, chartRect);

        using Font titleFont = new(Font.FontFamily, 9F, FontStyle.Bold);
        g.DrawString("\u27E8\u03B5_vac\u27E9 vs N (Log-Log Scale)", titleFont, Brushes.Black, marginLeft, 4);

        if (_scalingData.Count == 0)
        {
            using Font emptyFont = new(Font.FontFamily, 10F, FontStyle.Italic);
            string msg = "Run Vacuum Energy Scaling experiment to see results";
            SizeF msgSize = g.MeasureString(msg, emptyFont);
            g.DrawString(msg, emptyFont, Brushes.Gray,
                chartRect.X + (chartRect.Width - msgSize.Width) / 2,
                chartRect.Y + (chartRect.Height - msgSize.Height) / 2);
            return;
        }

        // Compute log-log bounds
        double minLogN = double.MaxValue, maxLogN = double.MinValue;
        double minLogE = double.MaxValue, maxLogE = double.MinValue;
        List<(double logN, double logE)> logPoints = [];

        foreach (ScalingDataPoint p in _scalingData)
        {
            if (p.N <= 0 || p.AvgVacuumEnergy <= 0) continue;

            double logN = Math.Log10(p.N);
            double logE = Math.Log10(p.AvgVacuumEnergy);
            logPoints.Add((logN, logE));

            if (logN < minLogN) minLogN = logN;
            if (logN > maxLogN) maxLogN = logN;
            if (logE < minLogE) minLogE = logE;
            if (logE > maxLogE) maxLogE = logE;
        }

        if (logPoints.Count == 0) return;

        double rangeN = maxLogN - minLogN;
        double rangeE = maxLogE - minLogE;
        if (rangeN < 0.1) rangeN = 1;
        if (rangeE < 0.1) rangeE = 1;

        minLogN -= rangeN * 0.1;
        maxLogN += rangeN * 0.1;
        minLogE -= rangeE * 0.15;
        maxLogE += rangeE * 0.15;

        float MapX(double logN) => (float)(chartRect.X + (logN - minLogN) / (maxLogN - minLogN) * chartRect.Width);
        float MapY(double logE) => (float)(chartRect.Bottom - (logE - minLogE) / (maxLogE - minLogE) * chartRect.Height);

        // Grid lines
        using Font axisFont = new(Font.FontFamily, 7.5F);
        using Pen gridPen = new(Color.FromArgb(230, 230, 230)) { DashStyle = DashStyle.Dot };

        for (double logN = Math.Ceiling(minLogN); logN <= maxLogN; logN += 0.5)
        {
            float x = MapX(logN);
            g.DrawLine(gridPen, x, chartRect.Top, x, chartRect.Bottom);
            g.DrawString($"10^{logN:F1}", axisFont, Brushes.Gray, x - 15, chartRect.Bottom + 3);
        }

        for (double logE = Math.Ceiling(minLogE * 2) / 2; logE <= maxLogE; logE += 0.5)
        {
            float y = MapY(logE);
            g.DrawLine(gridPen, chartRect.Left, y, chartRect.Right, y);
            g.DrawString($"{logE:F1}", axisFont, Brushes.Gray, 5, y - 7);
        }

        // Axis labels
        using Font labelFont = new(Font.FontFamily, 8F);
        g.DrawString("log\u2081\u2080(N)", labelFont, Brushes.Black,
            chartRect.X + chartRect.Width / 2 - 20, chartRect.Bottom + 20);

        using var vertFormat = new StringFormat { FormatFlags = StringFormatFlags.DirectionVertical };
        g.DrawString("log\u2081\u2080(\u27E8\u03B5_vac\u27E9)", labelFont, Brushes.Black,
            2, chartRect.Y + chartRect.Height / 2 - 30, vertFormat);

        // Regression line
        if (logPoints.Count >= 2)
        {
            ScalingResultExporter.RegressionResult reg = ScalingResultExporter.ComputeLogLogRegression(_scalingData);

            float x1 = MapX(minLogN);
            float y1 = MapY(reg.Alpha * minLogN + reg.Beta);
            float x2 = MapX(maxLogN);
            float y2 = MapY(reg.Alpha * maxLogN + reg.Beta);

            using Pen regPen = new(Color.FromArgb(180, Color.Blue), 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawLine(regPen, x1, y1, x2, y2);

            float midX = (x1 + x2) / 2;
            float midY = (y1 + y2) / 2;
            using Font slopeFont = new(Font.FontFamily, 8F, FontStyle.Italic);
            g.DrawString($"\u03B1 = {reg.Alpha:F3}", slopeFont, Brushes.Blue, midX + 5, midY - 15);
        }

        // Reference slopes
        DrawReferenceSlope(g, chartRect, minLogN, maxLogN, minLogE, maxLogE, MapX, MapY,
            -0.5, "\u03B1=-0.5", Color.FromArgb(100, Color.Gray));
        DrawReferenceSlope(g, chartRect, minLogN, maxLogN, minLogE, maxLogE, MapX, MapY,
            -1.0, "\u03B1=-1.0", Color.FromArgb(100, Color.Green));

        // Data points
        using SolidBrush pointBrush = new(Color.FromArgb(220, Color.DarkRed));
        using Pen pointOutline = new(Color.DarkRed, 1.2f);

        foreach ((double logN, double logE) in logPoints)
        {
            float px = MapX(logN);
            float py = MapY(logE);
            g.FillEllipse(pointBrush, px - 4, py - 4, 8, 8);
            g.DrawEllipse(pointOutline, px - 4, py - 4, 8, 8);
        }
    }

    private static void DrawReferenceSlope(
        Graphics g, Rectangle chartRect,
        double minLogN, double maxLogN, double minLogE, double maxLogE,
        Func<double, float> mapX, Func<double, float> mapY,
        double slope, string label, Color color)
    {
        double centerLogN = (minLogN + maxLogN) / 2;
        double centerLogE = (minLogE + maxLogE) / 2;
        double beta = centerLogE - slope * centerLogN;

        float x1 = mapX(minLogN);
        float y1 = mapY(slope * minLogN + beta);
        float x2 = mapX(maxLogN);
        float y2 = mapY(slope * maxLogN + beta);

        y1 = Math.Clamp(y1, chartRect.Top, chartRect.Bottom);
        y2 = Math.Clamp(y2, chartRect.Top, chartRect.Bottom);

        using Pen refPen = new(color, 1f) { DashStyle = DashStyle.DashDot };
        g.DrawLine(refPen, x1, y1, x2, y2);

        using Font refFont = new("Segoe UI", 7F);
        using SolidBrush refBrush = new(color);
        g.DrawString(label, refFont, refBrush, x2 - 35, y2 - 12);
    }

    // ============================================================
    // EXPORT EVENT HANDLERS
    // ============================================================

    private void BtnScalingExport_Click(object? sender, EventArgs e)
    {
        if (_scalingData.Count == 0)
        {
            MessageBox.Show(this, "No scaling data to export.", "Export",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using FolderBrowserDialog dlg = new()
        {
            Description = "Select folder for scaling experiment export",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            string[] files = ScalingResultExporter.ExportFullReport(_scalingData, dlg.SelectedPath);

            string message = $"Exported {files.Length} files:\n" + string.Join("\n", files);
            AppendSysConsole($"[ScalingExport] {message}\n");
            MessageBox.Show(this, message, "Export Complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"Export failed: {ex.Message}", "Export Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnScalingClear_Click(object? sender, EventArgs e)
    {
        _scalingData.Clear();
        RefreshScalingStats();
        _scalingChartPanel?.Invalidate();
    }
}
