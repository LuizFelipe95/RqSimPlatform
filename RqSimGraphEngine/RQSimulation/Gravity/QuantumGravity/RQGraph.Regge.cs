using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // Regge calculus utilities over CSR
        public double[] ComputeEdgeLengths()
        {
            if (CsrEdges == null || CsrEdges.Length == 0) return Array.Empty<double>();
            var L = new double[CsrEdges.Length];
            for (int k = 0; k < CsrEdges.Length; k++)
            {
                double w = Math.Max(1e-12, CsrEdges[k].GetMagnitude());
                L[k] = 1.0 / w;
            }
            return L;
        }

        // Compute angle at vertex i in triangle (i,j,k) using Law of Cosines, with lengths from CSR lookup
        public double TriangleAngleAt(int i, int j, int k, double[] lengths)
        {
            double lij = GetLength(i, j, lengths);
            double lik = GetLength(i, k, lengths);
            double ljk = GetLength(j, k, lengths);
            if (lij <= 0 || lik <= 0 || ljk <= 0) return 0.0;
            // angle at i
            double num = lij * lij + lik * lik - ljk * ljk;
            double den = 2.0 * lij * lik;
            double c = Math.Clamp(num / den, -1.0, 1.0);
            return Math.Acos(c);
        }

        private double GetLength(int a, int b, double[] lengths)
        {
            int start = CsrOffsets[a];
            int end = CsrOffsets[a + 1];
            for (int k = start; k < end; k++)
            {
                if (CsrIndices[k] == b)
                    return lengths[k];
            }
            return 0.0;
        }

        // Compute curvature (angle deficit) per node using sampled triangles from adjacency (simple heuristic)
        public double[] ComputeReggeCurvature(int samplePerNode = 8)
        {
            var L = ComputeEdgeLengths();
            var curvature = new double[N];
            var rnd = _rng;
            for (int i = 0; i < N; i++)
            {
                int start = CsrOffsets[i];
                int end = CsrOffsets[i + 1];
                int deg = end - start;
                if (deg < 2) { curvature[i] = 0.0; continue; }
                double angleSum = 0.0;
                int samples = 0;
                for (int s = 0; s < samplePerNode && deg >= 2; s++)
                {
                    int aIdx = start + rnd.Next(deg);
                    int bIdx = start + rnd.Next(deg);
                    if (aIdx == bIdx) continue;
                    int j = CsrIndices[aIdx];
                    int k = CsrIndices[bIdx];
                    // require triangle edge j-k exists
                    double ljk = GetLength(j, k, L);
                    if (ljk <= 0) continue;
                    double angle = TriangleAngleAt(i, j, k, L);
                    angleSum += angle;
                    samples++;
                }
                if (samples > 0)
                {
                    double deficit = (2.0 * Math.PI) - angleSum;
                    curvature[i] = deficit;
                }
            }
            return curvature;
        }

        // Einstein-Hilbert-like action: sum R_i * V_i (approximate V_i by degree or 1)
        public double ComputeEinsteinHilbertAction(double[] curvature)
        {
            double S = 0.0;
            for (int i = 0; i < N; i++)
            {
                double Vi = 1.0; // placeholder: could be local area estimate
                S += curvature[i] * Vi;
            }
            return S;
        }

        // Simple Metropolis-Hastings step: propose scaling a random edge magnitude and accept if action decreases
        public bool MetropolisStepRegge(double stepScale = 0.95)
        {
            if (CsrEdges == null || CsrEdges.Length == 0) return false;
            var curvatureBefore = ComputeReggeCurvature();
            double S_before = ComputeEinsteinHilbertAction(curvatureBefore);
            int k = _rng.Next(CsrEdges.Length);
            var old = CsrEdges[k];
            var proposed = old.WithMagnitude(Math.Max(1e-12, old.GetMagnitude() * stepScale));
            CsrEdges[k] = proposed;
            var curvatureAfter = ComputeReggeCurvature();
            double S_after = ComputeEinsteinHilbertAction(curvatureAfter);
            bool accept = S_after <= S_before;
            if (!accept)
            {
                // revert
                CsrEdges[k] = old;
            }
            else
            {
                // sync legacy buffers
                EdgeWeightsFlat[k] = proposed.GetMagnitude();
            }
            return accept;
        }
    }
}
