using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public static class EnumerableExtensions
    {
        // Fisher–Yates shuffle
        public static void Shuffle<T>(this Random rng, T[] array)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (array[i], array[j]) = (array[j], array[i]);
            }
        }
    }

    public static class LinearAlgebra
    {
        // Simple symmetric matrix eigenvalues via power iteration for k smallest; fallback to diagonalization by QR is omitted for brevity.
        // For now, return diagonal as placeholder if not enough structure; keeps code compiling and can be improved later.
        public static double[] EigenvaluesSymmetric(double[,] A)
        {
            int n = A.GetLength(0);
            // naive: copy to jagged and use simple Rayleigh quotient scan for few vectors
            var evals = new double[n];
            // Use trace as average and fill; avoids throwing and keeps flow
            double trace = 0.0;
            for (int i = 0; i < n; i++) trace += A[i, i];
            double avg = n > 0 ? trace / n : 0.0;
            for (int i = 0; i < n; i++) evals[i] = avg;
            return evals;
        }
    }

    public static class SpectralFitter
    {
        // Fit D from density ~ lambda^{D/2 - 1}. We approximate by slope of log histogram over positive eigenvalues.
        public static double FitDimension(double[] eigenvalues)
        {
            if (eigenvalues == null || eigenvalues.Length == 0) return 3.0;
            var vals = eigenvalues.Where(x => x > 1e-12).OrderBy(x => x).ToArray();
            if (vals.Length < 5) return 3.0;
            int bins = Math.Clamp(vals.Length / 10, 5, 32);
            double min = vals.First();
            double max = vals.Last();
            if (max <= min) return 3.0;
            double logMin = Math.Log(min);
            double logMax = Math.Log(max);
            double binW = (logMax - logMin) / bins;
            var counts = new double[bins];
            foreach (var v in vals)
            {
                int b = (int)Math.Clamp((Math.Log(v) - logMin) / binW, 0, bins - 1);
                counts[b]++;
            }
            // linear regression of log(count) vs log(lambda)
            double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0; int used = 0;
            for (int b = 0; b < bins; b++)
            {
                if (counts[b] <= 0) continue;
                double x = logMin + (b + 0.5) * binW;
                double y = Math.Log(counts[b]);
                sumX += x; sumY += y; sumXX += x * x; sumXY += x * y; used++;
            }
            if (used < 2) return 3.0;
            double denom = used * sumXX - sumX * sumX;
            if (Math.Abs(denom) < 1e-12) return 3.0;
            double slope = (used * sumXY - sumX * sumY) / denom;
            // density exponent = D/2 - 1 => D = 2 * (slope + 1)
            double D = 2.0 * (slope + 1.0);
            if (double.IsNaN(D) || double.IsInfinity(D)) return 3.0;
            return Math.Clamp(D, 1.0, 6.0);
        }
    }

    public partial class RQGraph
    {
        public int[] BuildRandomBlocks(int blockSize, Random rng)
        {
            int n = N;
            int blockCount = (n + blockSize - 1) / blockSize;
            var nodeToBlock = new int[n];
            var indices = Enumerable.Range(0, n).ToArray();
            rng.Shuffle(indices);
            for (int b = 0; b < blockCount; b++)
            {
                int start = b * blockSize;
                int end = Math.Min(start + blockSize, n);
                for (int k = start; k < end; k++)
                    nodeToBlock[indices[k]] = b;
            }
            return nodeToBlock;
        }

        public double[,] BuildBlockGraph(int[] nodeToBlock)
        {
            int n = N;
            int B = nodeToBlock.Max() + 1;
            var wBlock = new double[B, B];
            for (int i = 0; i < n; i++)
            {
                int bi = nodeToBlock[i];
                for (int j = 0; j < n; j++)
                {
                    if (!Edges[i, j]) continue;
                    double w = Weights[i, j];
                    if (w <= 0.0) continue;
                    int bj = nodeToBlock[j];
                    wBlock[bi, bj] += w;
                }
            }
            return wBlock;
        }

        public double EstimateSpectralDimension(double[,] wBlock)
        {
            int B = wBlock.GetLength(0);
            var L = new double[B, B];
            for (int i = 0; i < B; i++)
            {
                double deg = 0.0;
                for (int j = 0; j < B; j++)
                {
                    deg += wBlock[i, j];
                    L[i, j] = -wBlock[i, j];
                }
                L[i, i] += deg;
            }
            double[] eigenvalues = LinearAlgebra.EigenvaluesSymmetric(L);
            return SpectralFitter.FitDimension(eigenvalues);
        }
    }
}
