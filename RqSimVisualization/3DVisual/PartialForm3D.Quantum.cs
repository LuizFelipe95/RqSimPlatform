using RQSimulation;

namespace RqSimVisualization;

/// <summary>
/// Extension for PartialForm3D to support quantum probability visualization from CSR engine.
/// </summary>
public partial class RqSimVisualizationForm
{
    /// <summary>
    /// Quantum probability values for 3D visualization.
    /// Populated from graph.QuantumProbability during UpdateSnapshot.
    /// </summary>
    private double[]? _quantumProbability3D;

    /// <summary>
    /// Update VisualSnapshot with quantum probability if available.
    /// Called from UpdateSnapshot in PartialForm3D.cs
    /// </summary>
    private void UpdateQuantumVisualization(RQGraph graph)
    {
        if (graph.HasQuantumProbability)
        {
            _quantumProbability3D = graph.QuantumProbability;
        }
        else
        {
            _quantumProbability3D = null;
        }
    }

    /// <summary>
    /// Get color for quantum probability visualization.
    /// Uses a gradient from blue (low) to red (high probability).
    /// Called from Panel3D_Paint when _visualMode == "Quantum"
    /// </summary>
    private Color GetQuantumColor(int nodeIndex, int alpha = 255)
    {
        if (_quantumProbability3D == null || nodeIndex < 0 || nodeIndex >= _quantumProbability3D.Length)
        {
            return Color.FromArgb(alpha, 50, 50, 50);
        }

        double prob = _quantumProbability3D[nodeIndex];
        
        // Normalize to 0-1 range (assuming max probability is around 1/N for uniform distribution)
        // Boost by N to make differences visible
        double normalizedProb = Math.Clamp(prob * _quantumProbability3D.Length, 0, 1);

        // Color gradient: Blue (0) -> Cyan (0.25) -> Green (0.5) -> Yellow (0.75) -> Red (1)
        int r, g, b;
        if (normalizedProb < 0.25)
        {
            double t = normalizedProb / 0.25;
            r = 0;
            g = (int)(255 * t);
            b = 255;
        }
        else if (normalizedProb < 0.5)
        {
            double t = (normalizedProb - 0.25) / 0.25;
            r = 0;
            g = 255;
            b = (int)(255 * (1 - t));
        }
        else if (normalizedProb < 0.75)
        {
            double t = (normalizedProb - 0.5) / 0.25;
            r = (int)(255 * t);
            g = 255;
            b = 0;
        }
        else
        {
            double t = (normalizedProb - 0.75) / 0.25;
            r = 255;
            g = (int)(255 * (1 - t));
            b = 0;
        }

        return Color.FromArgb(alpha, r, g, b);
    }

    /// <summary>
    /// Initialize quantum mode in 3D visualization ComboBox.
    /// Call after _cmbVisMode is created.
    /// </summary>
    private void InitializeQuantumVisualizationMode()
    {
        // Add Quantum mode if not already present
        if (_cmbVisMode != null && !_cmbVisMode.Items.Contains("Quantum"))
        {
            _cmbVisMode.Items.Add("Quantum");
        }
    }
}
