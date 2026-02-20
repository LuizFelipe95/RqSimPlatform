namespace RQSimulation;

/// <summary>
/// Extension for quantum probability visualization data.
/// Used to store |?|? values from CSR engine for 3D visualization.
/// </summary>
public partial class RQGraph
{
    /// <summary>
    /// Quantum probability density |?|? for each node.
    /// Populated by CSR engine sync for visualization.
    /// Can be used in 3D visualization to show quantum state intensity.
    /// </summary>
    public double[]? QuantumProbability { get; set; }

    /// <summary>
    /// Get quantum probability for a node, or 0 if not available.
    /// </summary>
    public double GetQuantumProbability(int nodeIndex)
    {
        if (QuantumProbability == null || nodeIndex < 0 || nodeIndex >= QuantumProbability.Length)
            return 0.0;
        
        return QuantumProbability[nodeIndex];
    }

    /// <summary>
    /// Check if quantum probability data is available.
    /// </summary>
    public bool HasQuantumProbability => QuantumProbability != null && QuantumProbability.Length == N;
}
