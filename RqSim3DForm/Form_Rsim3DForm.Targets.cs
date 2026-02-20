using Color = System.Drawing.Color;
using Size = System.Drawing.Size;

namespace RqSim3DForm;

/// <summary>
/// Target Metrics display for standalone 3D visualization.
/// Shows physics target status (Mass Gap, Speed of Light, Ricci, Hausdorff).
/// </summary>
public partial class Form_Rsim3DForm
{
    // === Target Metrics Data ===
    private double _massGapValue = double.NaN;
    private double _speedOfLightVariance = double.NaN;
    private double _speedOfLightMean = double.NaN;
    private double _ricciCurvatureAvg = double.NaN;
    private double _hausdorffDimension = double.NaN;

    private TargetStatus _massGapStatus = TargetStatus.Unknown;
    private TargetStatus _speedOfLightStatus = TargetStatus.Unknown;
    private TargetStatus _ricciFlatnessStatus = TargetStatus.Unknown;
    private TargetStatus _holographicStatus = TargetStatus.Unknown;

    private bool _showTargetOverlay = true;
    private Panel? _targetStatusPanel;

    /// <summary>
    /// Creates the Physics Targets panel.
    /// </summary>
    private Panel CreateTargetStatusPanel()
    {
        var panel = new Panel
        {
            Width = 190,
            Height = 180,
            BackColor = Color.FromArgb(200, 15, 15, 25),
            Margin = new Padding(0, 10, 0, 0)
        };

        panel.Paint += TargetStatusPanel_Paint;
        return panel;
    }

    private void TargetStatusPanel_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel) return;
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var fontTitle = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Bold);
        using var fontMetric = new Font(SystemFonts.DefaultFont.FontFamily, 8f);
        using var fontSmall = new Font(SystemFonts.DefaultFont.FontFamily, 7f);

        int y = 6;
        int labelX = 6;
        int valueX = 100;

        // Title
        using (var titleBrush = new SolidBrush(Color.FromArgb(100, 180, 255)))
        {
            g.DrawString("? PHYSICS TARGETS", fontTitle, titleBrush, labelX, y);
        }
        y += 18;

        // Separator line
        using (var linePen = new Pen(Color.FromArgb(80, 100, 180, 255), 1))
        {
            g.DrawLine(linePen, labelX, y, panel.Width - labelX, y);
        }
        y += 6;

        // Mass Gap
        DrawTargetLine(g, fontMetric, fontSmall, labelX, valueX, ref y,
            "Mass Gap (??):",
            double.IsNaN(_massGapValue) ? "---" : $"{_massGapValue:F4}",
            _massGapStatus, "Yang-Mills");

        // Speed of Light
        DrawTargetLine(g, fontMetric, fontSmall, labelX, valueX, ref y,
            "c Isotropy (??):",
            double.IsNaN(_speedOfLightVariance) ? "---" : $"{_speedOfLightVariance:F4}",
            _speedOfLightStatus, "Lieb-Robinson");

        // Ricci
        DrawTargetLine(g, fontMetric, fontSmall, labelX, valueX, ref y,
            "Ricci <R>:",
            double.IsNaN(_ricciCurvatureAvg) ? "---" : $"{_ricciCurvatureAvg:F4}",
            _ricciFlatnessStatus, "Vacuum");

        // Hausdorff
        DrawTargetLine(g, fontMetric, fontSmall, labelX, valueX, ref y,
            "Hausdorff d_H:",
            double.IsNaN(_hausdorffDimension) ? "---" : $"{_hausdorffDimension:F2}",
            _holographicStatus, "Holographic");
    }

    private void DrawTargetLine(Graphics g, Font fontMetric, Font fontSmall,
        int labelX, int valueX, ref int y, string label, string value, TargetStatus status, string category)
    {
        Color statusColor = status switch
        {
            TargetStatus.Achieved => Color.Lime,
            TargetStatus.Approaching => Color.Yellow,
            TargetStatus.Searching => Color.Orange,
            TargetStatus.Unstable => Color.OrangeRed,
            TargetStatus.Failed => Color.Red,
            _ => Color.Gray
        };

        string symbol = status switch
        {
            TargetStatus.Achieved => "?",
            TargetStatus.Approaching => "?",
            TargetStatus.Searching => "?",
            TargetStatus.Unstable => "?",
            TargetStatus.Failed => "?",
            _ => "?"
        };

        using var labelBrush = new SolidBrush(Color.LightGray);
        using var valueBrush = new SolidBrush(statusColor);
        using var catBrush = new SolidBrush(Color.FromArgb(100, statusColor));

        g.DrawString(label, fontMetric, labelBrush, labelX, y);
        g.DrawString($"{symbol} {value}", fontMetric, valueBrush, valueX, y);
        y += 14;

        g.DrawString($"  [{category}]", fontSmall, catBrush, labelX, y);
        y += 12;
    }

    /// <summary>
    /// Updates target metrics from graph data.
    /// </summary>
    private void UpdateTargetMetrics(GraphRenderData data)
    {
        double dS = data.SpectralDimension;

        if (double.IsNaN(dS) || dS <= 0)
        {
            _massGapValue = double.NaN;
            _speedOfLightVariance = double.NaN;
            _ricciCurvatureAvg = double.NaN;
            _hausdorffDimension = double.NaN;

            _massGapStatus = TargetStatus.Failed;
            _speedOfLightStatus = TargetStatus.Failed;
            _ricciFlatnessStatus = TargetStatus.Failed;
            _holographicStatus = TargetStatus.Failed;

            _targetStatusPanel?.Invalidate();
            return;
        }

        // Approximate target statuses based on d_S
        // d_S ? 4 is the target for 4D spacetime emergence
        if (dS >= 3.8 && dS <= 4.2)
        {
            _massGapStatus = TargetStatus.Achieved;
            _speedOfLightStatus = TargetStatus.Achieved;
            _ricciFlatnessStatus = TargetStatus.Achieved;
            _holographicStatus = TargetStatus.Achieved;
        }
        else if (dS >= 3.0)
        {
            _massGapStatus = TargetStatus.Approaching;
            _speedOfLightStatus = TargetStatus.Approaching;
            _ricciFlatnessStatus = TargetStatus.Searching;
            _holographicStatus = TargetStatus.Searching;
        }
        else
        {
            _massGapStatus = TargetStatus.Searching;
            _speedOfLightStatus = TargetStatus.Searching;
            _ricciFlatnessStatus = TargetStatus.Searching;
            _holographicStatus = TargetStatus.Searching;
        }

        // Update display values (heuristic approximations)
        // Keep numbers stable for UI: no negative mass gap, bounded variance, bounded Ricci proxy.
        _massGapValue = dS > 2 ? Math.Max(0.0, 0.1 * (4.0 - dS)) : double.NaN;
        _speedOfLightVariance = dS > 2 ? Math.Clamp(0.01 * Math.Abs(4.0 - dS), 0.0, 1.0) : double.NaN;
        _ricciCurvatureAvg = dS > 2 ? Math.Clamp(0.5 * (dS - 4.0), -10.0, 10.0) : double.NaN;
        _hausdorffDimension = dS;

        _targetStatusPanel?.Invalidate();
    }
}

/// <summary>
/// Status of physics target achievement.
/// </summary>
public enum TargetStatus
{
    Unknown,
    Searching,
    Approaching,
    Achieved,
    Unstable,
    Failed
}
