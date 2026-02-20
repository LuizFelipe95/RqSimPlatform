using System;
using System.Numerics;

namespace RQSimulation
{
    /// <summary>
    /// RQGraph partial class with accessor methods for GPU observer engines.
    /// These methods provide safe access to internal wavefunction data.
    /// </summary>
    public partial class RQGraph
    {
        /// <summary>
        /// Get the multi-component wavefunction array for GPU observer engine.
        /// Returns null if not initialized.
        /// </summary>
        /// <returns>Reference to internal _waveMulti array</returns>
        public Complex[]? GetWaveMulti() => _waveMulti;
        
        /// <summary>
        /// Get the single-component wavefunction array for GPU observer engine.
        /// Returns null if not initialized.
        /// </summary>
        /// <returns>Reference to internal _wavefunction array</returns>
        public Complex[]? GetWavefunctionForObserver() => _wavefunction;
        
        /// <summary>
        /// Get probability density |?|? at a specific node.
        /// </summary>
        /// <param name="nodeId">Node index</param>
        /// <returns>Probability density, or 0 if invalid</returns>
        public double GetNodeProbability(int nodeId)
        {
            if (nodeId < 0 || nodeId >= N)
                return 0.0;
            
            double prob = 0.0;
            
            // Sum over all gauge components
            if (_waveMulti != null)
            {
                int d = GaugeDimension;
                int startIdx = nodeId * d;
                for (int a = 0; a < d && startIdx + a < _waveMulti.Length; a++)
                {
                    double mag = _waveMulti[startIdx + a].Magnitude;
                    prob += mag * mag;
                }
                return prob;
            }
            
            // Single-component wavefunction
            if (_wavefunction != null && nodeId < _wavefunction.Length)
            {
                double mag = _wavefunction[nodeId].Magnitude;
                return mag * mag;
            }
            
            return 0.0;
        }
        
        /// <summary>
        /// Get the total probability (normalization) of the wavefunction.
        /// </summary>
        /// <returns>Sum of |?_i|? over all nodes</returns>
        public double GetTotalProbability()
        {
            double total = 0.0;
            
            if (_waveMulti != null)
            {
                foreach (var z in _waveMulti)
                {
                    double mag = z.Magnitude;
                    total += mag * mag;
                }
                return total;
            }
            
            if (_wavefunction != null)
            {
                foreach (var z in _wavefunction)
                {
                    double mag = z.Magnitude;
                    total += mag * mag;
                }
                return total;
            }
            
            return 1.0; // Default normalization
        }
    }
}
