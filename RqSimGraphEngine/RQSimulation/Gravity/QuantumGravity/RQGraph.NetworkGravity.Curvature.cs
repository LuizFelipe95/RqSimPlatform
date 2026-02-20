using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        /// <summary>
        /// Calculate graph curvature for edge (i,j) using Forman-Ricci approximation.
        /// This is the DEFAULT curvature used in hot paths (time dilation, energy, evolution).
        /// O(degree²) per edge — safe for per-node-per-neighbor calls every simulation step.
        ///
        /// For more accurate Ollivier-Ricci curvature (Sinkhorn optimal transport),
        /// use CalculateOllivierRicciCurvature() or the dedicated pipeline modules
        /// (OllivierRicciCpuModule, SinkhornOllivierRicciGpuModule).
        /// </summary>
        public double CalculateGraphCurvature(int i, int j)
        {
            return GPUOptimized.FormanRicciCurvature.ComputeFormanRicci(this, i, j);
        }

        /// <summary>
        /// Calculate local volume (weighted degree) of a node.
        /// Used for volume constraint in gravity.
        /// </summary>
        public double GetLocalVolume(int i)
        {
            double vol = 0.0;
            foreach (int j in Neighbors(i))
            {
                vol += Weights[i, j];
            }
            return vol;
        }

        /// <summary>
        /// Calculate Ollivier-Ricci curvature for edge (i,j)
        /// This is the NEW implementation (CHECKLIST ITEM 4)
        /// More sensitive to geometry than Forman-Ricci
        /// </summary>
        public double CalculateOllivierRicciCurvature(int i, int j)
        {
            // Delegate to GPU-optimized implementation
            return GPUOptimized.OllivierRicciCurvature.ComputeOllivierRicciSinkhorn(this, i, j);
        }

        /// <summary>
        /// Compute average curvature
        /// </summary>
        public double ComputeAverageCurvature()
        {
            if (Edges == null || Weights == null)
                return 0.0;

            double totalCurvature = 0.0;
            int edgeCount = 0;

            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue; // Count each edge once

                    totalCurvature += CalculateGraphCurvature(i, j);
                    edgeCount++;
                }
            }

            return edgeCount > 0 ? totalCurvature / edgeCount : 0.0;
        }

        /// <summary>
        /// Compute curvature scalar (sum of all edge curvatures)
        /// Analogous to Ricci scalar R in GR
        /// </summary>
        public double ComputeCurvatureScalar()
        {
            if (Edges == null || Weights == null)
                return 0.0;

            double scalar = 0.0;

            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;

                    scalar += CalculateGraphCurvature(i, j);
                }
            }

            return scalar;
        }
    }
}
