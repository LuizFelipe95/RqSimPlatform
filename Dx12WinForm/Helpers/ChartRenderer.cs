using System;
using System.Drawing;

namespace Dx12WinForm.Helpers;

public static class ChartRenderer
{
    /// <summary>
    /// Fast chart drawing using pre-decimated array data (no LINQ, no List conversions)
    /// </summary>
    public static void DrawSimpleLineChartFast(Graphics g, int width, int height, int[] xSteps, int[] values, string title, Color color)
    {
        g.Clear(Color.White);
        if (xSteps.Length == 0 || values.Length == 0)
        {
            g.DrawString("Нет данных", new Font("Arial", 10), Brushes.Gray, 10, 10);
            return;
        }

        int marginLeft = 50;
        int marginRight = 20;
        int marginTop = 30;
        int marginBottom = 40;
        int plotWidth = width - marginLeft - marginRight;
        int plotHeight = height - marginTop - marginBottom;

        if (plotWidth <= 10 || plotHeight <= 10)
            return;

        int minX = xSteps[0];
        int maxX = xSteps[^1];
        int minY = int.MaxValue, maxY = int.MinValue;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < minY) minY = values[i];
            if (values[i] > maxY) maxY = values[i];
        }

        if (maxY == minY)
        {
            maxY += 1;
            minY -= 1;
        }

        // Draw axes
        using Pen axisPen = new(Color.Black, 1);
        g.DrawLine(axisPen, marginLeft, marginTop, marginLeft, marginTop + plotHeight);
        g.DrawLine(axisPen, marginLeft, marginTop + plotHeight, marginLeft + plotWidth, marginTop + plotHeight);

        using Font f = new("Consolas", 9f);
        g.DrawString(title, f, Brushes.Black, marginLeft, 5);
        g.DrawString(maxY.ToString(), f, Brushes.Black, 5, marginTop - 5);
        g.DrawString(minY.ToString(), f, Brushes.Black, 5, marginTop + plotHeight - 12);
        g.DrawString(minX.ToString(), f, Brushes.Black, marginLeft, marginTop + plotHeight + 5);
        g.DrawString(maxX.ToString(), f, Brushes.Black, marginLeft + plotWidth - 40, marginTop + plotHeight + 5);

        // Draw line
        using Pen linePen = new(color, 1.6f);
        double xRange = maxX - minX;
        double yRange = maxY - minY;
        if (xRange <= 0) xRange = 1;

        float prevX = 0, prevY = 0;
        for (int i = 0; i < xSteps.Length && i < values.Length; i++)
        {
            float x = marginLeft + (float)((xSteps[i] - minX) / xRange * plotWidth);
            float y = marginTop + (float)((maxY - values[i]) / yRange * plotHeight);
            if (i > 0)
                g.DrawLine(linePen, prevX, prevY, x, y);
            prevX = x;
            prevY = y;
        }
    }

    /// <summary>
    /// Fast chart drawing for double arrays
    /// </summary>
    public static void DrawSimpleLineChartFast(Graphics g, int width, int height, int[] xSteps, double[] values, string title, Color color)
    {
        g.Clear(Color.White);
        if (xSteps.Length == 0 || values.Length == 0)
        {
            g.DrawString("Нет данных", new Font("Arial", 10), Brushes.Gray, 10, 10);
            return;
        }

        int marginLeft = 50;
        int marginRight = 20;
        int marginTop = 30;
        int marginBottom = 40;
        int plotWidth = width - marginLeft - marginRight;
        int plotHeight = height - marginTop - marginBottom;

        if (plotWidth <= 10 || plotHeight <= 10)
            return;

        int minX = xSteps[0];
        int maxX = xSteps[^1];
        double minY = double.MaxValue, maxY = double.MinValue;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < minY) minY = values[i];
            if (values[i] > maxY) maxY = values[i];
        }

        if (Math.Abs(maxY - minY) < 1e-9)
        {
            maxY += 1.0;
            minY -= 1.0;
        }

        // Draw axes
        using Pen axisPen = new(Color.Black, 1);
        g.DrawLine(axisPen, marginLeft, marginTop, marginLeft, marginTop + plotHeight);
        g.DrawLine(axisPen, marginLeft, marginTop + plotHeight, marginLeft + plotWidth, marginTop + plotHeight);

        using Font f = new("Consolas", 9f);
        g.DrawString(title, f, Brushes.Black, marginLeft, 5);
        g.DrawString(maxY.ToString("F2"), f, Brushes.Black, 5, marginTop - 5);
        g.DrawString(minY.ToString("F2"), f, Brushes.Black, 5, marginTop + plotHeight - 12);
        g.DrawString(minX.ToString(), f, Brushes.Black, marginLeft, marginTop + plotHeight + 5);
        g.DrawString(maxX.ToString(), f, Brushes.Black, marginLeft + plotWidth - 40, marginTop + plotHeight + 5);

        // Draw line
        using Pen linePen = new(color, 1.6f);
        double xRange = maxX - minX;
        double yRange = maxY - minY;
        if (xRange <= 0) xRange = 1;

        float prevX = 0, prevY = 0;
        for (int i = 0; i < xSteps.Length && i < values.Length; i++)
        {
            float x = marginLeft + (float)((xSteps[i] - minX) / xRange * plotWidth);
            float y = marginTop + (float)((maxY - values[i]) / yRange * plotHeight);
            if (i > 0)
                g.DrawLine(linePen, prevX, prevY, x, y);
            prevX = x;
            prevY = y;
        }
    }

    public static void DrawDualLineChartFast(Graphics g, int width, int height, int[] xSteps, double[] primaryValues, double[] secondaryValues,
        string title, Color primaryColor, Color secondaryColor)
    {
        g.Clear(Color.White);
        if (xSteps.Length == 0 || primaryValues.Length == 0 || secondaryValues.Length == 0)
        {
            g.DrawString("Нет данных", new Font("Arial", 10), Brushes.Gray, 10, 10);
            return;
        }

        int marginLeft = 50;
        int marginRight = 50; // Increased for right axis
        int marginTop = 30;
        int marginBottom = 40;
        int plotWidth = width - marginLeft - marginRight;
        int plotHeight = height - marginTop - marginBottom;

        if (plotWidth <= 10 || plotHeight <= 10)
            return;

        int sampleCount = Math.Min(xSteps.Length, Math.Min(primaryValues.Length, secondaryValues.Length));
        if (sampleCount == 0)
        {
            g.DrawString("Нет данных", new Font("Arial", 10), Brushes.Gray, 10, 10);
            return;
        }

        int minX = xSteps[0];
        int maxX = xSteps[sampleCount - 1];

        // Calculate ranges separately
        double minY1 = double.MaxValue, maxY1 = double.MinValue;
        double minY2 = double.MaxValue, maxY2 = double.MinValue;

        for (int i = 0; i < sampleCount; i++)
        {
            double v1 = primaryValues[i];
            if (v1 < minY1) minY1 = v1;
            if (v1 > maxY1) maxY1 = v1;

            double v2 = secondaryValues[i];
            if (v2 < minY2) minY2 = v2;
            if (v2 > maxY2) maxY2 = v2;
        }

        if (Math.Abs(maxY1 - minY1) < 1e-9) { maxY1 += 1.0; minY1 -= 1.0; }
        if (Math.Abs(maxY2 - minY2) < 1e-9) { maxY2 += 1.0; minY2 -= 1.0; }

        using Pen axisPen = new(Color.Black, 1);
        // Left Y axis
        g.DrawLine(axisPen, marginLeft, marginTop, marginLeft, marginTop + plotHeight);
        // Right Y axis
        g.DrawLine(axisPen, marginLeft + plotWidth, marginTop, marginLeft + plotWidth, marginTop + plotHeight);
        // X axis
        g.DrawLine(axisPen, marginLeft, marginTop + plotHeight, marginLeft + plotWidth, marginTop + plotHeight);

        using Font labelFont = new("Consolas", 9f);
        g.DrawString(title, labelFont, Brushes.Black, marginLeft, 5);

        // Left axis labels (Primary)
        using Brush primaryBrush = new SolidBrush(primaryColor);
        g.DrawString(maxY1.ToString("F2"), labelFont, primaryBrush, 5, marginTop - 5);
        g.DrawString(minY1.ToString("F2"), labelFont, primaryBrush, 5, marginTop + plotHeight - 12);

        // Right axis labels (Secondary)
        using Brush secondaryBrush = new SolidBrush(secondaryColor);
        string maxLabel2 = maxY2.ToString("F2");
        string minLabel2 = minY2.ToString("F2");
        g.DrawString(maxLabel2, labelFont, secondaryBrush, width - marginRight + 5, marginTop - 5);
        g.DrawString(minLabel2, labelFont, secondaryBrush, width - marginRight + 5, marginTop + plotHeight - 12);

        // X axis labels
        g.DrawString(minX.ToString(), labelFont, Brushes.Black, marginLeft, marginTop + plotHeight + 5);
        g.DrawString(maxX.ToString(), labelFont, Brushes.Black, marginLeft + plotWidth - 40, marginTop + plotHeight + 5);

        double xRange = maxX - minX;
        double yRange1 = maxY1 - minY1;
        double yRange2 = maxY2 - minY2;
        if (xRange <= 0) xRange = 1;

        using Pen primaryPen = new(primaryColor, 1.6f);
        using Pen secondaryPen = new(secondaryColor, 1.6f);

        // Draw Primary
        float prevX = 0, prevY = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            float x = marginLeft + (float)((xSteps[i] - minX) / xRange * plotWidth);
            float y = marginTop + (float)((maxY1 - primaryValues[i]) / yRange1 * plotHeight);
            if (i > 0) g.DrawLine(primaryPen, prevX, prevY, x, y);
            prevX = x; prevY = y;
        }

        // Draw Secondary
        prevX = 0; prevY = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            float x = marginLeft + (float)((xSteps[i] - minX) / xRange * plotWidth);
            float y = marginTop + (float)((maxY2 - secondaryValues[i]) / yRange2 * plotHeight);
            if (i > 0) g.DrawLine(secondaryPen, prevX, prevY, x, y);
            prevX = x; prevY = y;
        }

        // Legend
        int legendX = marginLeft + 10;
        int legendY = marginTop + 10;
        using Font legendFont = new("Consolas", 8.5f);

        g.FillRectangle(primaryBrush, legendX, legendY, 12, 12);
        g.DrawString("Energy", legendFont, Brushes.Black, legendX + 18, legendY - 1);

        legendY += 18;
        g.FillRectangle(secondaryBrush, legendX, legendY, 12, 12);
        g.DrawString("Network Temp", legendFont, Brushes.Black, legendX + 18, legendY - 1);
    }
}
