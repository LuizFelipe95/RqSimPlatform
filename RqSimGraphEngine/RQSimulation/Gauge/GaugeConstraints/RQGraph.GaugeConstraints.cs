using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    /// <summary>
    /// Gauge Constraints: Enforce gauge invariance and Gauss law
    /// Ensures Wilson loops are conserved and ∇·E = ρ
    /// </summary>
    public partial class RQGraph
    {
        /// <summary>
        /// Compute Wilson loop around plaquette (i,j,k)
        /// Returns phase in [0, 2π)
        /// </summary>
        public double ComputeWilsonLoop(int i, int j, int k)
        {
            if (!Edges[i, j] || !Edges[j, k] || !Edges[k, i])
                return 0;

            if (_edgePhaseU1 == null)
                return 0;

            // Sum phases around loop
            double phase = _edgePhaseU1[i, j]
                         + _edgePhaseU1[j, k]
                         + _edgePhaseU1[k, i];

            // Make compact: bring to [0, 2π)
            phase = phase % (2 * Math.PI);
            if (phase < 0) phase += 2 * Math.PI;

            return phase;
        }

        /// <summary>
        /// Compute all Wilson loops (triangles) in the graph
        /// </summary>
        public Dictionary<(int, int, int), double> ComputeAllWilsonLoops()
        {
            var loops = new Dictionary<(int, int, int), double>();

            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;

                    foreach (int k in Neighbors(i))
                    {
                        if (k <= j || !Edges[j, k]) continue;

                        // Triangle i-j-k exists
                        double loop = ComputeWilsonLoop(i, j, k);
                        loops[(i, j, k)] = loop;
                    }
                }
            }

            return loops;
        }

        /// <summary>
        /// Project gauge phases to satisfy Gauss law: ∇·E = ρ
        /// This is the U(1) gauge constraint
        /// </summary>
        public void EnforceGaussLaw()
        {
            if (_edgePhaseU1 == null) return;

            // 1. Compute divergence of electric field and charge density at each node
            double[] divergence = new double[N];
            double[] chargeDensity = new double[N];

            for (int i = 0; i < N; i++)
            {
                // Electric field E ~ ∂φ/∂t, but we approximate with spatial gradient
                // Divergence: sum of outgoing minus incoming
                double div = 0;
                foreach (int j in Neighbors(i))
                {
                    // Field component along edge i→j
                    div += (_edgePhaseU1[i, j] - _edgePhaseU1[j, i]);
                }
                divergence[i] = div;

                // Charge from fermion density
                chargeDensity[i] = ComputeFermionDensity(i);
            }

            // 2. Solve Poisson equation: ∇²χ = ∇·E - ρ
            //    This gives the gauge transformation needed to enforce Gauss law
            double[] gaugeTransform = SolvePoissonEquation(divergence, chargeDensity);

            // 3. Apply gauge transformation: φ'[i,j] = φ[i,j] + (χ[j] - χ[i])
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    _edgePhaseU1[i, j] += gaugeTransform[j] - gaugeTransform[i];

                    // Make compact: mod 2π
                    _edgePhaseU1[i, j] = _edgePhaseU1[i, j] % (2 * Math.PI);
                    if (_edgePhaseU1[i, j] < 0)
                        _edgePhaseU1[i, j] += 2 * Math.PI;
                }
            }
        }

        /// <summary>
        /// Solve Laplace equation ∇²χ = b using Gauss-Seidel iteration
        /// </summary>
        private double[] SolvePoissonEquation(double[] divergence, double[] charge)
        {
            double[] chi = new double[N];
            double[] b = new double[N];

            // Right-hand side: ∇·E - ρ
            for (int i = 0; i < N; i++)
                b[i] = divergence[i] - charge[i];

            // Gauss-Seidel iteration for ∇²χ = b
            const int maxIter = 100;
            const double tolerance = 1e-6;

            for (int iter = 0; iter < maxIter; iter++)
            {
                double maxChange = 0;

                for (int i = 0; i < N; i++)
                {
                    double sum = 0;
                    int degree = 0;

                    foreach (int j in Neighbors(i))
                    {
                        sum += chi[j];
                        degree++;
                    }

                    if (degree == 0) continue;

                    // χ_i = (sum_j χ_j - b_i) / degree
                    double chi_new = (sum - b[i]) / degree;
                    maxChange = Math.Max(maxChange, Math.Abs(chi_new - chi[i]));
                    chi[i] = chi_new;
                }

                // Check convergence
                if (maxChange < tolerance)
                    break;
            }

            return chi;
        }

        /// <summary>
        /// Compute average Wilson loop flux (should be ~0 for physical states)
        /// </summary>
        public double ComputeAverageWilsonLoopFlux()
        {
            var loops = ComputeAllWilsonLoops();
            if (loops.Count == 0) return 0;

            // Average deviation from 0 or 2π (trivial loops)
            double sumDeviation = 0;
            foreach (var phase in loops.Values)
            {
                // Minimum distance to 0 or 2π
                double dev = Math.Min(phase, 2 * Math.PI - phase);
                sumDeviation += dev;
            }

            return sumDeviation / loops.Count;
        }

        /// <summary>
        /// Verify Gauss law satisfaction: |∇·E - ρ| < tolerance
        /// </summary>
        public double ComputeGaussLawViolation()
        {
            if (_edgePhaseU1 == null) return 0;

            double maxViolation = 0;

            for (int i = 0; i < N; i++)
            {
                // Divergence
                double div = 0;
                foreach (int j in Neighbors(i))
                {
                    div += (_edgePhaseU1[i, j] - _edgePhaseU1[j, i]);
                }

                // Charge
                double rho = ComputeFermionDensity(i);

                // Violation
                double violation = Math.Abs(div - rho);
                maxViolation = Math.Max(maxViolation, violation);
            }

            return maxViolation;
        }
    }
}
