
namespace RqSimVisualization;

/// <summary>
/// CSR visualization legend panel.
/// Displays spectral interpretation legend similar to GDI+ mode.
/// </summary>
public partial class RqSimVisualizationForm
{
    private DoubleBufferedPanel? _csrLegendPanel;

    /// <summary>
    /// Creates and initializes the CSR legend panel.
    /// Call from InitializeCsrVisualizationControls.
    /// </summary>
    private DoubleBufferedPanel CreateCsrLegendPanel()
    {
        var legendPanel = new DoubleBufferedPanel
        {
            Size = new Size(170, 120),
            BackColor = Color.FromArgb(120, 20, 20, 20),
            Margin = new Padding(0, 10, 0, 0)
        };
        legendPanel.Paint += CsrLegend_Paint;
        return legendPanel;
    }

    /// <summary>
    /// Paint handler for CSR legend panel.
    /// Mirrors GDI+ PnlLegend_Paint functionality.
    /// </summary>
    private void CsrLegend_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel)
            return;

        Graphics g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        string title = "Spectral Interpretation:";
        var items = new (string Label, Color Color)[]
        {
            ("d_S < 1.5: Dust", Color.Gray),
            ("d_S ? 2: Filament", Color.Cyan),
            ("d_S ? 3: Membrane", Color.Yellow),
            ("d_S ? 4: Bulk (Target)", Color.Lime),
            ("d_S > 4.5: Complex", Color.Magenta)
        };

        int y = 2;
        using var brushText = new SolidBrush(Color.LightGray);
        using var font = new Font(SystemFonts.DefaultFont.FontFamily, 8f);

        // Draw Title
        g.DrawString(title, font, brushText, 0, y);
        y += 18;

        int boxSize = 10;
        using var borderPen = new Pen(Color.White, 1);

        foreach (var item in items)
        {
            // Draw color box
            using (var brush = new SolidBrush(item.Color))
            {
                g.FillRectangle(brush, 1, y + 2, boxSize, boxSize);
            }
            g.DrawRectangle(borderPen, 1, y + 2, boxSize, boxSize);

            // Draw label text
            g.DrawString(item.Label, font, brushText, boxSize + 5, y);
            y += 18;
        }
    }
}
