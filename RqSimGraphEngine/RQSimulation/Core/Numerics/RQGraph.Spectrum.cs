using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        /// <summary>
        /// Computes the eigenvalues of the weighted graph Laplacian using the classical Jacobi
        /// eigenvalue algorithm. The Laplacian is defined as L = D - W, where W is the
        /// symmetric matrix of edge weights and D is the diagonal matrix of weighted degrees.
        /// The returned array contains the eigenvalues in ascending order. For large graphs
        /// (N &gt; 200) this routine may be slow; however for moderate sizes it suffices to
        /// demonstrate spectral properties of the emergent geometry.
        /// </summary>
        /// <param name="tolerance">Convergence tolerance for off-diagonal elements.</param>
        /// <param name="maxIterations">Maximum number of Jacobi rotations before giving up.</param>
        /// <returns>Array of Laplacian eigenvalues sorted in ascending order.</returns>
        public double[] ComputeLaplacianEigenvalues(double tolerance = 1e-8, int maxIterations = 10000)
        {
            int n = N;
            if (n == 0)
                return Array.Empty<double>();

            // Build Laplacian matrix: L = D - W
            double[,] L = new double[n, n];
            // diagonal degrees
            for (int i = 0; i < n; i++)
            {
                double degreeSum = 0.0;
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    if (Edges[i, j])
                    {
                        degreeSum += Weights[i, j];
                        L[i, j] = -Weights[i, j];
                    }
                }
                L[i, i] = degreeSum;
            }

            // Copy of L to modify during rotations
            double[,] a = (double[,])L.Clone();
            int iter = 0;
            int p = 0, q = 0;

            while (iter < maxIterations)
            {
                // Find largest off-diagonal element in magnitude
                double maxOff = 0.0;
                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        double absVal = Math.Abs(a[i, j]);
                        if (absVal > maxOff)
                        {
                            maxOff = absVal;
                            p = i;
                            q = j;
                        }
                    }
                }

                // Converged if off-diagonal elements are small
                if (maxOff < tolerance)
                    break;

                // Compute rotation to zero out a[p,q]
                double app = a[p, p];
                double aqq = a[q, q];
                double apq = a[p, q];

                // phi = 0.5 * atan2(2*apq, aqq - app)
                double tau = (aqq - app) / (2.0 * apq);
                double t;
                if (tau >= 0)
                {
                    t = 1.0 / (tau + Math.Sqrt(1 + tau * tau));
                }
                else
                {
                    t = -1.0 / (-tau + Math.Sqrt(1 + tau * tau));
                }
                double c = 1.0 / Math.Sqrt(1.0 + t * t);
                double s = c * t;

                // Update diagonal entries p and q
                double appNew = c * c * app - 2.0 * c * s * apq + s * s * aqq;
                double aqqNew = s * s * app + 2.0 * c * s * apq + c * c * aqq;
                a[p, p] = appNew;
                a[q, q] = aqqNew;
                a[p, q] = 0.0;
                a[q, p] = 0.0;

                // Rotate rows/columns p and q
                for (int k = 0; k < n; k++)
                {
                    if (k == p || k == q)
                        continue;
                    double aik = a[k, p];
                    double ajk = a[k, q];
                    double newAik = c * aik - s * ajk;
                    double newAjk = s * aik + c * ajk;
                    a[k, p] = newAik;
                    a[p, k] = newAik;
                    a[k, q] = newAjk;
                    a[q, k] = newAjk;
                }

                iter++;
            }

            // Extract eigenvalues from diagonal of a
            double[] eigenvalues = new double[n];
            for (int i = 0; i < n; i++)
                eigenvalues[i] = a[i, i];

            // Sort ascending
            Array.Sort(eigenvalues);
            return eigenvalues;
        }
        
        /// <summary>
        /// Compute spectral mass of a cluster as the gap between first and second eigenvalue
        /// of the subgraph Laplacian.
        /// 
        /// RQ-Hypothesis Checklist Item 4.2: Mass as eigenvalue spectrum gap.
        /// Physics: Mass is the energy required to excite the structure (break bonds).
        /// A larger gap indicates more stable (heavier) structure.
        /// </summary>
        /// <param name="clusterNodes">Nodes in the cluster</param>
        /// <returns>Spectral mass (gap between λ₁ and λ₂)</returns>
        public double ComputeSpectralMass(List<int> clusterNodes)
        {
            if (clusterNodes == null || clusterNodes.Count < 3)
                return 0.0;
            
            int n = clusterNodes.Count;
            var nodeSet = new HashSet<int>(clusterNodes);
            
            // Build subgraph Laplacian
            double[,] L = new double[n, n];
            var indexMap = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
                indexMap[clusterNodes[i]] = i;
            
            for (int i = 0; i < n; i++)
            {
                int nodeI = clusterNodes[i];
                double degreeSum = 0.0;
                
                foreach (int nodeJ in Neighbors(nodeI))
                {
                    if (!nodeSet.Contains(nodeJ)) continue;
                    
                    int j = indexMap[nodeJ];
                    L[i, j] = -Weights[nodeI, nodeJ];
                    degreeSum += Weights[nodeI, nodeJ];
                }
                
                L[i, i] = degreeSum;
            }
            
            // Compute eigenvalues using Jacobi (reuse logic)
            double[] eigenvalues = JacobiEigenvalues(L, n);
            
            if (eigenvalues.Length < 2)
                return 0.0;
            
            // Mass = gap between first nonzero eigenvalue (λ₁) and second (λ₂)
            // For connected subgraph, λ₀ ≈ 0 (constant mode)
            // λ₁ is the algebraic connectivity (Fiedler value)
            double lambda1 = eigenvalues.Length > 1 ? eigenvalues[1] : 0.0;
            double lambda2 = eigenvalues.Length > 2 ? eigenvalues[2] : lambda1;
            
            // The gap indicates binding energy
            double spectralGap = lambda2 - lambda1;
            
            // Also add Fiedler value as it indicates connectivity strength
            return lambda1 + spectralGap;
        }
        
        /// <summary>
        /// Compute eigenvalues of a symmetric matrix using Jacobi algorithm.
        /// Helper method for spectral mass computation.
        /// </summary>
        private double[] JacobiEigenvalues(double[,] matrix, int n, double tolerance = 1e-8, int maxIterations = 5000)
        {
            if (n == 0)
                return Array.Empty<double>();
            
            double[,] a = (double[,])matrix.Clone();
            int iter = 0;
            
            while (iter < maxIterations)
            {
                // Find largest off-diagonal element
                double maxOff = 0.0;
                int p = 0, q = 0;
                
                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        double absVal = Math.Abs(a[i, j]);
                        if (absVal > maxOff)
                        {
                            maxOff = absVal;
                            p = i;
                            q = j;
                        }
                    }
                }
                
                if (maxOff < tolerance)
                    break;
                
                // Compute Jacobi rotation
                double app = a[p, p];
                double aqq = a[q, q];
                double apq = a[p, q];
                
                double tau = (aqq - app) / (2.0 * apq);
                double t = tau >= 0 
                    ? 1.0 / (tau + Math.Sqrt(1 + tau * tau))
                    : -1.0 / (-tau + Math.Sqrt(1 + tau * tau));
                double c = 1.0 / Math.Sqrt(1.0 + t * t);
                double s = c * t;
                
                // Update matrix
                a[p, p] = c * c * app - 2.0 * c * s * apq + s * s * aqq;
                a[q, q] = s * s * app + 2.0 * c * s * apq + c * c * aqq;
                a[p, q] = 0.0;
                a[q, p] = 0.0;
                
                for (int k = 0; k < n; k++)
                {
                    if (k == p || k == q) continue;
                    double aik = a[k, p];
                    double ajk = a[k, q];
                    a[k, p] = a[p, k] = c * aik - s * ajk;
                    a[k, q] = a[q, k] = s * aik + c * ajk;
                }
                
                iter++;
            }
            
            // Extract eigenvalues
            double[] eigenvalues = new double[n];
            for (int i = 0; i < n; i++)
                eigenvalues[i] = a[i, i];
            
            Array.Sort(eigenvalues);
            return eigenvalues;
        }
        
        /// <summary>
        /// Compute spectral masses for all clusters and return summary.
        /// RQ-Hypothesis Checklist Item 4.2.
        /// </summary>
        public List<(int ClusterId, int Size, double SpectralMass, int BettiNumber)> GetSpectralMassSummary()
        {
            var summary = new List<(int, int, double, int)>();
            var clusters = GetStrongCorrelationClusters(AdaptiveHeavyThreshold);
            
            for (int clusterId = 0; clusterId < clusters.Count; clusterId++)
            {
                var cluster = clusters[clusterId];
                
                if (cluster.Count >= HeavyClusterMinSize)
                {
                    double spectralMass = ComputeSpectralMass(cluster);
                    int betti = ComputeBettiNumber(cluster);
                    
                    summary.Add((clusterId, cluster.Count, spectralMass, betti));
                }
            }
            
            return summary;
        }
    }
}