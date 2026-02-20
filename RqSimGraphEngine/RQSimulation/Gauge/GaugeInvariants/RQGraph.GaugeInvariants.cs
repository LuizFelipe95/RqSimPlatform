using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace RQSimulation
{
    /// <summary>
    /// Gauge-invariant computations (U(1) Wilson loops, SU(N) Wilson loops, cluster charges)
    /// </summary>
    public partial class RQGraph
    {
        public struct ColorChargeSignature
        {
            public double MeanRe;
            public double MeanIm;
            public double StdRe;
            public double StdIm;
            public int LoopCount;
        }

        public sealed class ClusterCharges
        {
            public double U1ChargeHat { get; init; }
            public double U1ChargeStd { get; init; }
            public ColorChargeSignature ColorSignature { get; init; }
        }

        public Complex ComputeU1WilsonLoop(IReadOnlyList<int> loopNodes)
        {
            if (_edgePhase == null) return Complex.One;
            if (loopNodes == null || loopNodes.Count < 2) return Complex.One;
            double phaseSum = 0.0;
            int L = loopNodes.Count;
            for (int k = 0; k < L; k++)
            {
                int i = loopNodes[k];
                int j = loopNodes[(k + 1) % L];
                phaseSum += _edgePhase[i, j];
            }
            return Complex.FromPolarCoordinates(1.0, phaseSum);
        }

        public List<int[]> FindU1TrianglesInCluster(IReadOnlyList<int> clusterNodes)
        {
            var result = new List<int[]>();
            if (clusterNodes == null || clusterNodes.Count == 0) return result;
            var set = new HashSet<int>(clusterNodes);
            foreach (int i in clusterNodes)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i || !set.Contains(j)) continue;
                    foreach (int k in Neighbors(j))
                    {
                        if (k <= j || !set.Contains(k)) continue;
                        if (Edges[k, i])
                        {
                            result.Add(new[] { i, j, k });
                        }
                    }
                }
            }
            return result;
        }

        public double ComputeU1Charge(IReadOnlyList<int> clusterNodes)
        {
            var loops = FindU1TrianglesInCluster(clusterNodes);
            if (loops.Count == 0) return 0.0;
            double totalPhase = 0.0;
            foreach (var loop in loops)
            {
                var W = ComputeU1WilsonLoop(loop);
                double phi = Math.Atan2(W.Imaginary, W.Real);
                totalPhase += phi;
            }
            return totalPhase / (2.0 * Math.PI);
        }

        private Complex[,] IdentityMatrix(int d)
        {
            var I = new Complex[d, d];
            for (int i = 0; i < d; i++) I[i, i] = Complex.One;
            return I;
        }
        private Complex[,] GetGaugeMatrix(int i, int j)
        {
            int d = GaugeDimension;
            var M = new Complex[d, d];
            for (int r = 0; r < d; r++)
            {
                for (int c = 0; c < d; c++)
                {
                    int k = r * d + c;
                    M[r, c] = _gaugeSU[i, j, k];
                }
            }
            return M;
        }
        private Complex[,] Multiply(Complex[,] A, Complex[,] B)
        {
            int d = A.GetLength(0);
            var C = new Complex[d, d];
            for (int i = 0; i < d; i++)
            {
                for (int j = 0; j < d; j++)
                {
                    Complex sum = Complex.Zero;
                    for (int k = 0; k < d; k++) sum += A[i, k] * B[k, j];
                    C[i, j] = sum;
                }
            }
            return C;
        }
        private Complex Trace(Complex[,] M)
        {
            int d = M.GetLength(0); Complex tr = Complex.Zero; for (int i = 0; i < d; i++) tr += M[i, i]; return tr;
        }

        public Complex ComputeSUNWilsonLoop(IReadOnlyList<int> loopNodes)
        {
            if (_gaugeSU == null || GaugeDimension <= 1 || loopNodes == null || loopNodes.Count == 0)
                return Complex.One;
            int d = GaugeDimension;
            int L = loopNodes.Count;
            var U = IdentityMatrix(d);
            for (int k = 0; k < L; k++)
            {
                int i = loopNodes[k];
                int j = loopNodes[(k + 1) % L];
                var Uij = GetGaugeMatrix(i, j);
                U = Multiply(U, Uij);
            }
            Complex tr = Trace(U);
            return tr / d;
        }

        public ColorChargeSignature ComputeColorCharge(IReadOnlyList<int> clusterNodes)
        {
            var loops = FindU1TrianglesInCluster(clusterNodes);
            if (loops.Count == 0) return default;
            var re = new List<double>();
            var im = new List<double>();
            foreach (var loop in loops)
            {
                var W = ComputeSUNWilsonLoop(loop);
                re.Add(W.Real);
                im.Add(W.Imaginary);
            }
            double meanRe = Mean(re);
            double meanIm = Mean(im);
            double stdRe = Std(re, meanRe);
            double stdIm = Std(im, meanIm);
            return new ColorChargeSignature { MeanRe = meanRe, MeanIm = meanIm, StdRe = stdRe, StdIm = stdIm, LoopCount = loops.Count };
        }

        private static double Mean(List<double> data)
        {
            if (data.Count == 0) return 0.0; double s = 0.0; for (int i = 0; i < data.Count; i++) s += data[i]; return s / data.Count;
        }
        private static double Std(List<double> data, double mean)
        {
            if (data.Count == 0) return 0.0; double s2 = 0.0; for (int i = 0; i < data.Count; i++) { double d = data[i] - mean; s2 += d * d; } return Math.Sqrt(s2 / data.Count);
        }

        public ClusterCharges ComputeClusterCharges(ClusterTrack track)
        {
            ArgumentNullException.ThrowIfNull(track);
            var qValues = new List<double>();
            var colorSignatures = new List<ColorChargeSignature>();
            foreach (var inst in track.Instants)
            {
                double q = ComputeU1Charge(inst.Nodes);
                qValues.Add(q);
                var color = ComputeColorCharge(inst.Nodes);
                colorSignatures.Add(color);
            }
            double qMean = 0.0, qStd = 0.0;
            if (qValues.Count > 0)
            {
                qMean = qValues.Average();
                double meanSq = qValues.Select(x => x * x).Average();
                qStd = Math.Sqrt(Math.Max(0.0, meanSq - qMean * qMean));
            }
            double meanRe = colorSignatures.Count == 0 ? 0.0 : colorSignatures.Average(c => c.MeanRe);
            double meanIm = colorSignatures.Count == 0 ? 0.0 : colorSignatures.Average(c => c.MeanIm);
            double stdRe = colorSignatures.Count == 0 ? 0.0 : colorSignatures.Average(c => c.StdRe);
            double stdIm = colorSignatures.Count == 0 ? 0.0 : colorSignatures.Average(c => c.StdIm);
            var colorAvg = new ColorChargeSignature { MeanRe = meanRe, MeanIm = meanIm, StdRe = stdRe, StdIm = stdIm, LoopCount = colorSignatures.Sum(c => c.LoopCount) };
            return new ClusterCharges { U1ChargeHat = qMean, U1ChargeStd = qStd, ColorSignature = colorAvg };
        }

        // 2.2: Topological index via U(1) loops and topological mass binding
        public int ComputeTopologicalIndex(IReadOnlyList<int> clusterNodes)
        {
            var loops = FindU1TrianglesInCluster(clusterNodes);
            int winding = 0;
            foreach (var loop in loops)
            {
                var W = ComputeU1WilsonLoop(loop);
                double phi = Math.Atan2(W.Imaginary, W.Real);
                if (phi > Math.PI / 2) winding++;
                else if (phi < -Math.PI / 2) winding--;
            }
            return winding;
        }

        public double ComputeTopologicalMass(IReadOnlyList<int> nodes)
        {
            double restMassCorr = ComputeRestMassOfCluster(nodes);
            int Q = ComputeTopologicalIndex(nodes);
            double topoMass = Math.Abs(Q);
            return Math.Max(restMassCorr, topoMass);
        }

        public double ComputeLocalU1Curvature(int node)
        {
            var cluster = new List<int> { node };
            foreach (int nb in Neighbors(node)) cluster.Add(nb);
            var loops = FindU1TrianglesInCluster(cluster);
            if (loops.Count == 0) return 0.0;
            double sumAbsPhi = 0.0;
            foreach (var loop in loops)
            {
                var W = ComputeU1WilsonLoop(loop);
                double phi = Math.Atan2(W.Imaginary, W.Real);
                sumAbsPhi += Math.Abs(phi);
            }
            return sumAbsPhi / loops.Count;
        }
        public double ComputeLocalColorCurvature(int node)
        {
            var cluster = new List<int> { node };
            foreach (int nb in Neighbors(node)) cluster.Add(nb);
            var signature = ComputeColorCharge(cluster);
            return 0.5 * (signature.StdRe + signature.StdIm);
        }
        public double ComputeLocalCurvature(int node)
        {
            double u1 = ComputeLocalU1Curvature(node);
            double col = ComputeLocalColorCurvature(node);
            return u1 + col;
        }
        
        /// <summary>
        /// Compute Wilson loop for a closed path of arbitrary length.
        /// W = ∏_edges U_ij = exp(i * Σ φ_ij)
        /// For U(1) gauge theory, this measures the magnetic flux enclosed by the loop.
        /// Implements checklist item 5.2: Flux conservation monitoring.
        /// </summary>
        /// <param name="loopNodes">Ordered list of nodes forming a closed path</param>
        /// <returns>Complex Wilson loop value</returns>
        public Complex ComputeWilsonLoop(List<int> loopNodes)
        {
            if (loopNodes == null || loopNodes.Count < 2)
                return Complex.One;
            
            // Initialize edge phase array if needed
            if (_edgePhaseU1 == null && _edgePhase != null)
            {
                // Use existing _edgePhase
            }
            
            Complex product = Complex.One;
            int L = loopNodes.Count;
            
            for (int k = 0; k < L; k++)
            {
                int u = loopNodes[k];
                int v = loopNodes[(k + 1) % L];
                
                // Get U(1) link variable: U_uv = exp(i * φ_uv)
                Complex linkVariable = GetLinkVariable(u, v);
                product *= linkVariable;
            }
            
            return product;
        }
        
        /// <summary>
        /// Compute Wilson loop phase for a closed path.
        /// Returns the total accumulated phase around the loop.
        /// This should be stable in time for vacuum (flux conservation).
        /// Implements checklist item 5.2: Flux conservation monitoring.
        /// </summary>
        /// <param name="loopNodes">Ordered list of nodes forming a closed path</param>
        /// <returns>Phase in radians (should be conserved for physical states)</returns>
        public double ComputeWilsonLoopPhase(List<int> loopNodes)
        {
            Complex W = ComputeWilsonLoop(loopNodes);
            return W.Phase;
        }
        
        /// <summary>
        /// Check flux conservation by computing variance of Wilson loops.
        /// For physical (gauge-invariant) states, all Wilson loops should have
        /// stable values over time. Large variance indicates gauge constraint violation.
        /// Implements checklist item 5.2: Flux conservation validation.
        /// </summary>
        /// <returns>Average deviation from zero flux (should be small)</returns>
        public double ComputeFluxConservationMetric()
        {
            var loops = ComputeAllWilsonLoops();
            if (loops.Count == 0)
                return 0.0;
            
            // Compute mean phase
            double meanPhase = 0.0;
            foreach (var kv in loops)
            {
                meanPhase += kv.Value;
            }
            meanPhase /= loops.Count;
            
            // Compute variance
            double variance = 0.0;
            foreach (var kv in loops)
            {
                double diff = kv.Value - meanPhase;
                variance += diff * diff;
            }
            variance /= loops.Count;
            
            return Math.Sqrt(variance);
        }
    }
}
