using System;

namespace RQSimulation
{
    /// <summary>
    /// Edge gauge field data structure for phase tracking
    /// </summary>
    public struct EdgeGaugeData
    {
        public double Weight;      // Amplitude of connection
        public double PhaseU1;     // U(1) gauge phase [-π, π]
        
        // For future SU(2)/SU(3) extensions
        // public Matrix2x2 PhaseSU2;  // SU(2) weak gauge
        // public Matrix3x3 PhaseSU3;  // SU(3) color gauge
        
        public EdgeGaugeData(double weight, double phaseU1 = 0.0)
        {
            Weight = weight;
            PhaseU1 = phaseU1;
        }
        
        /// <summary>
        /// Get parallel transport factor for fermion propagation
        /// </summary>
        public System.Numerics.Complex ParallelTransport()
        {
            return System.Numerics.Complex.FromPolarCoordinates(Weight, PhaseU1);
        }
    }
}
