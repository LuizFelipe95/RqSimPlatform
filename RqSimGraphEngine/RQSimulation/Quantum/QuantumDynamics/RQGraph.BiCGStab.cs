using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace RQSimulation
{
    /// <summary>
    /// BiCGStab (Biconjugate Gradient Stabilized) solver for sparse linear systems.
    /// Used for Cayley-form unitary evolution: (1 + iH*dt/2) * ?_new = (1 - iH*dt/2) * ?_old
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 1: Unitary Evolution
    /// ==================================================
    /// The Cayley form U = (1 - iH*dt/2)(1 + iH*dt/2)^-1 preserves unitarity exactly
    /// because it maps the anti-Hermitian operator -iH to a unitary matrix.
    /// 
    /// Unlike explicit Euler (?_new = ? - iH*dt*?) which violates ||?|| = 1,
    /// the Cayley scheme guarantees ||?_new|| = ||?_old|| for any dt.
    /// </summary>
    public partial class RQGraph
    {
        // Solver parameters
        private const int MaxBiCGStabIterations = 100;
        private const double BiCGStabTolerance = 1e-10;

        /// <summary>
        /// Solve linear system (I + alpha*H) * x = b using BiCGStab algorithm.
        /// Used for Cayley-form quantum evolution.
        /// 
        /// The system is: (1 + iH*dt/2) * ?_new = b
        /// where b = (1 - iH*dt/2) * ?_old was precomputed.
        /// </summary>
        /// <param name="H">Hamiltonian matrix (graph Laplacian + potential)</param>
        /// <param name="b">Right-hand side vector</param>
        /// <param name="alpha">Coefficient for iH term (typically i*dt/2)</param>
        /// <returns>Solution vector x</returns>
        public Complex[] SolveLinearSystem_BiCGStab(double[,] H, Complex[] b, Complex alpha)
        {
            int n = b.Length;
            var x = new Complex[n];
            
            // Initial guess: x = b (good for small alpha)
            Array.Copy(b, x, n);

            // r = b - A*x where A = I + alpha*H
            var r = new Complex[n];
            ApplyOperator(H, x, alpha, r);
            for (int i = 0; i < n; i++)
                r[i] = b[i] - r[i];

            // Check if already converged
            double rNorm = Norm(r);
            if (rNorm < BiCGStabTolerance)
                return x;

            // r_hat = r (shadow residual)
            var rHat = new Complex[n];
            Array.Copy(r, rHat, n);

            var p = new Complex[n];
            var v = new Complex[n];
            var s = new Complex[n];
            var t = new Complex[n];

            Complex rho = Complex.One;
            Complex alpha_cg = Complex.One;
            Complex omega = Complex.One;

            Array.Copy(r, p, n);

            for (int iter = 0; iter < MaxBiCGStabIterations; iter++)
            {
                // rho_new = <r_hat, r>
                Complex rhoNew = InnerProduct(rHat, r);
                
                if (rhoNew.Magnitude < 1e-15)
                {
                    // Breakdown - restart with current solution
                    break;
                }

                if (iter > 0)
                {
                    // beta = (rho_new / rho) * (alpha_cg / omega)
                    Complex beta = (rhoNew / rho) * (alpha_cg / omega);
                    
                    // p = r + beta * (p - omega * v)
                    for (int i = 0; i < n; i++)
                        p[i] = r[i] + beta * (p[i] - omega * v[i]);
                }

                rho = rhoNew;

                // v = A * p
                ApplyOperator(H, p, alpha, v);

                // alpha_cg = rho / <r_hat, v>
                Complex rHatV = InnerProduct(rHat, v);
                if (rHatV.Magnitude < 1e-15)
                    break;

                alpha_cg = rho / rHatV;

                // s = r - alpha_cg * v
                for (int i = 0; i < n; i++)
                    s[i] = r[i] - alpha_cg * v[i];

                // Check for convergence
                double sNorm = Norm(s);
                if (sNorm < BiCGStabTolerance)
                {
                    // x = x + alpha_cg * p
                    for (int i = 0; i < n; i++)
                        x[i] += alpha_cg * p[i];
                    break;
                }

                // t = A * s
                ApplyOperator(H, s, alpha, t);

                // omega = <t, s> / <t, t>
                Complex tDotS = InnerProduct(t, s);
                Complex tDotT = InnerProduct(t, t);
                
                if (tDotT.Magnitude < 1e-15)
                    break;

                omega = tDotS / tDotT;

                // x = x + alpha_cg * p + omega * s
                for (int i = 0; i < n; i++)
                    x[i] += alpha_cg * p[i] + omega * s[i];

                // r = s - omega * t
                for (int i = 0; i < n; i++)
                    r[i] = s[i] - omega * t[i];

                // Check for convergence
                rNorm = Norm(r);
                if (rNorm < BiCGStabTolerance)
                    break;

                if (omega.Magnitude < 1e-15)
                    break;
            }

            return x;
        }

        /// <summary>
        /// Apply sparse Hamiltonian operator with graph structure.
        /// Computes result = (I + alpha*H) * input for each gauge component.
        /// 
        /// Uses graph sparsity: H[i,j] != 0 only for connected nodes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyHamiltonianSparse(double[,] H, Complex[] input, int gaugeIndex, 
            int gaugeDim, Complex alpha, Complex[] result)
        {
            int baseIdx = gaugeIndex;
            
            for (int i = 0; i < N; i++)
            {
                int idx = i * gaugeDim + gaugeIndex;
                if (idx >= input.Length) continue;

                Complex Hpsi = Complex.Zero;
                
                // Diagonal term
                Hpsi += H[i, i] * input[idx];
                
                // Off-diagonal terms (only for connected nodes)
                foreach (int j in Neighbors(i))
                {
                    int jdx = j * gaugeDim + gaugeIndex;
                    if (jdx < input.Length)
                        Hpsi += H[i, j] * input[jdx];
                }
                
                // result = (I + alpha*H) * input
                result[idx] = input[idx] + alpha * Hpsi;
            }
        }

        /// <summary>
        /// Apply operator A = I + alpha*H to vector.
        /// </summary>
        private void ApplyOperator(double[,] H, Complex[] input, Complex alpha, Complex[] result)
        {
            int n = input.Length;
            int d = GaugeDimension;
            
            // Process each gauge component separately
            Parallel.For(0, n, i =>
            {
                int nodeIdx = i / d;
                if (nodeIdx >= N)
                {
                    result[i] = input[i];
                    return;
                }

                Complex Hpsi = Complex.Zero;
                
                // Diagonal term
                Hpsi += H[nodeIdx, nodeIdx] * input[i];
                
                // Off-diagonal terms (only for connected nodes - sparse)
                int gaugeOffset = i % d;
                foreach (int j in Neighbors(nodeIdx))
                {
                    int jdx = j * d + gaugeOffset;
                    if (jdx < n)
                        Hpsi += H[nodeIdx, j] * input[jdx];
                }
                
                // result = (I + alpha*H) * input
                result[i] = input[i] + alpha * Hpsi;
            });
        }

        /// <summary>
        /// Compute complex inner product <a, b> = sum(conj(a_i) * b_i)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Complex InnerProduct(Complex[] a, Complex[] b)
        {
            Complex result = Complex.Zero;
            for (int i = 0; i < a.Length; i++)
                result += Complex.Conjugate(a[i]) * b[i];
            return result;
        }

        /// <summary>
        /// Compute L2 norm of complex vector.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Norm(Complex[] v)
        {
            double sum = 0;
            for (int i = 0; i < v.Length; i++)
            {
                double mag = v[i].Magnitude;
                sum += mag * mag;
            }
            return Math.Sqrt(sum);
        }

        /// <summary>
        /// Apply H*? at specific index (for efficient sparse computation).
        /// Used in Cayley-form evolution to compute (1 ± iH*dt/2)*?.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Complex ApplyHamiltonianAtIndex(double[,] H, Complex[] psi, int index)
        {
            int d = GaugeDimension;
            int nodeIdx = index / d;
            int gaugeOffset = index % d;
            
            if (nodeIdx >= N)
                return Complex.Zero;

            Complex result = Complex.Zero;
            
            // Diagonal term
            result += H[nodeIdx, nodeIdx] * psi[index];
            
            // Off-diagonal terms (sparse - only neighbors)
            foreach (int j in Neighbors(nodeIdx))
            {
                int jdx = j * d + gaugeOffset;
                if (jdx < psi.Length)
                    result += H[nodeIdx, j] * psi[jdx];
            }
            
            return result;
        }
    }
}
