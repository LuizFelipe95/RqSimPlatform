using System;

namespace RQSimulation
{
    /// <summary>
    /// Relationally defined couplings and scales for the quantum / gauge sectors.
    /// All parameters here are derived from the internal graph structure only
    /// (degrees, weights, heavy clusters) and therefore do not introduce any
    /// external tuning independent of the RQ-hypothesis.
    /// </summary>
    public partial class RQGraph
    {
        private double _weakSpinCoupling; // backing field
        public double WeakSpinCoupling => _weakSpinCoupling; // expose computed value

        /// <summary>
        /// Configure quantum and weak couplings from graph statistics.
        /// Uses only relational quantities (average degree, weights) keeping all scales O(1).
        /// </summary>
        public void ConfigureRelationalCouplings()
        {
            var edgeStats = GetEdgeStats();
            double avgDegree = edgeStats.avgDegree <= 0 ? 1.0 : edgeStats.avgDegree;

            // Hebbian and decay scaling purely relational: O(1/N) and O(1/N^2)
            _hebbRate = 1.0 / Math.Max(1, N);
            _decayRate = 1.0 / (Math.Max(1, N) * Math.Max(1, N));

            // Dynamic quantum coupling & dt are provided via read-only computed properties
            double qc = QuantumCoupling; // 1/avgDegree

            _weakSpinCoupling = (QuantumComponents >= 5) ? 0.5 * qc : 0.0;
        }

        /// <summary>
        /// Compute an effective inverse gauge temperature beta purely from relational data.
        /// Higher average degree and correlation weights correspond to a colder (more
        /// ordered) gauge sector.  The result is clipped to a modest range to avoid
        /// numerical instabilities but no external physical scale is introduced.
        /// </summary>
        public double ComputeRelationalGaugeBeta()
        {
            var edgeStats = GetEdgeStats();
            double avgDegree = edgeStats.avgDegree;
            if (avgDegree <= 0.0)
                avgDegree = 1.0;

            var weightStats = GetWeightStats(0.0);
            double avgW = weightStats.avgWeight;

            double beta = avgDegree / (1.0 + avgW);
            if (beta < 0.5) beta = 0.5;
            if (beta > 10.0) beta = 10.0;
            return beta;
        }
    }
}
