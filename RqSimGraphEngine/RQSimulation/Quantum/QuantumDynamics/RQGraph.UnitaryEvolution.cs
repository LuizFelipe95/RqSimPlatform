using System;
using System.Numerics;

namespace RQSimulation
{
    /// <summary>
    /// Implements proper unitary quantum evolution using Crank-Nicolson method.
    /// 
    /// QUANTUM THEORIST TASK LIST - Item 1: Unitarity
    /// 
    /// Problem: The Euler method psi_new = psi - i*dt*H*psi does NOT preserve norm.
    /// Manual normalization destroys phase coherence and breaks quantum interference.
    /// 
    /// Solution: Use Crank-Nicolson implicit midpoint method:
    ///   (1 + i*dt*H/2) * psi_new = (1 - i*dt*H/2) * psi_old
    /// 
    /// This is equivalent to U = exp(-i*H*dt) to second order in dt,
    /// and preserves ||psi||? exactly (to machine precision).
    /// 
    /// For small graphs (N &lt; 200), we use direct matrix inversion.
    /// For larger graphs, we use iterative conjugate gradient solver.
    /// </summary>
    public partial class RQGraph
    {
        /// <summary>
        /// Evolve quantum state using Crank-Nicolson method (exactly unitary).
        /// No manual normalization needed - norm is preserved automatically.
        /// </summary>
        /// <param name="dt">Time step</param>
        /// <param name="maxIterations">Max iterations for iterative solver (large N)</param>
        /// <param name="tolerance">Convergence tolerance for iterative solver</param>
        public void EvolveQuantumUnitaryCrankNicolson(double dt, int maxIterations = 100, double tolerance = 1e-10)
        {
            if (_waveMulti == null) return;

            int d = GaugeDimension;
            int len = N * d;

            // Build Hamiltonian matrix (graph Laplacian + potential)
            var H = ComputeGraphLaplacian();

            // Add potential diagonal terms from correlation mass
            if (_correlationMass != null && _correlationMass.Length == N)
            {
                for (int i = 0; i < N; i++)
                {
                    H[i, i] += _correlationMass[i];
                }
            }

            // For each gauge component, apply Crank-Nicolson evolution
            var psiNew = new Complex[len];

            for (int a = 0; a < d; a++)
            {
                // Extract component a
                var psi_a = new Complex[N];
                for (int i = 0; i < N; i++)
                {
                    psi_a[i] = _waveMulti[i * d + a];
                }

                // Apply Crank-Nicolson: (1 + i*dt*H/2)^(-1) * (1 - i*dt*H/2) * psi
                Complex[] psi_new_a;

                if (N < 200)
                {
                    // Direct method for small graphs
                    psi_new_a = CrankNicolsonDirect(H, psi_a, dt);
                }
                else
                {
                    // Iterative method for large graphs
                    psi_new_a = CrankNicolsonIterative(H, psi_a, dt, maxIterations, tolerance);
                }

                // Store result
                for (int i = 0; i < N; i++)
                {
                    psiNew[i * d + a] = psi_new_a[i];
                }
            }

            _waveMulti = psiNew;

            // Verify unitarity (optional diagnostic)
#if DEBUG
            double norm = 0.0;
            for (int i = 0; i < len; i++)
            {
                norm += _waveMulti[i].Magnitude * _waveMulti[i].Magnitude;
            }
            double normDrift = Math.Abs(norm - 1.0);
            if (normDrift > 1e-8)
            {
                //Console.WriteLine($"[WARNING] Unitarity violation: ||psi||? = {norm:F10}, drift = {normDrift:E3}");
            }
#endif
        }

        /// <summary>
        /// Direct Crank-Nicolson for small matrices.
        /// Computes: psi_new = (1 + i*dt*H/2)^(-1) * (1 - i*dt*H/2) * psi
        /// </summary>
        private Complex[] CrankNicolsonDirect(double[,] H, Complex[] psi, double dt)
        {
            int n = psi.Length;
            var halfDt = dt / 2.0;

            // Build matrices A = (1 + i*dt*H/2) and B = (1 - i*dt*H/2)
            var A = new Complex[n, n];
            var B = new Complex[n, n];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    Complex iH = Complex.ImaginaryOne * H[i, j] * halfDt;
                    A[i, j] = (i == j ? Complex.One : Complex.Zero) + iH;
                    B[i, j] = (i == j ? Complex.One : Complex.Zero) - iH;
                }
            }

            // Compute b = B * psi (right-hand side)
            var b = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                b[i] = Complex.Zero;
                for (int j = 0; j < n; j++)
                {
                    b[i] += B[i, j] * psi[j];
                }
            }

            // Solve A * psi_new = b using Gaussian elimination with partial pivoting
            return SolveLinearSystemComplex(A, b);
        }

        /// <summary>
        /// Iterative Crank-Nicolson using conjugate gradient for large matrices.
        /// Solves: (1 + i*dt*H/2) * psi_new = (1 - i*dt*H/2) * psi
        /// </summary>
        private Complex[] CrankNicolsonIterative(double[,] H, Complex[] psi, double dt, int maxIter, double tol)
        {
            int n = psi.Length;
            var halfDt = dt / 2.0;

            // Compute right-hand side: b = (1 - i*dt*H/2) * psi
            var b = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                b[i] = psi[i];
                for (int j = 0; j < n; j++)
                {
                    b[i] -= Complex.ImaginaryOne * halfDt * H[i, j] * psi[j];
                }
            }

            // Iterative solve: A * x = b where A = (1 + i*dt*H/2)
            // Use BiCGSTAB for complex non-Hermitian systems
            return BiCGSTABSolve(H, halfDt, b, psi, maxIter, tol);
        }

        /// <summary>
        /// BiCGSTAB solver for complex linear system (1 + i*alpha*H) * x = b
        /// </summary>
        private Complex[] BiCGSTABSolve(double[,] H, double alpha, Complex[] b, Complex[] x0, int maxIter, double tol)
        {
            int n = b.Length;
            var x = (Complex[])x0.Clone();

            // r = b - A*x
            var r = new Complex[n];
            ApplyAMatrix(H, alpha, x, r);
            for (int i = 0; i < n; i++) r[i] = b[i] - r[i];

            var r0 = (Complex[])r.Clone();
            var p = (Complex[])r.Clone();
            var v = new Complex[n];
            var s = new Complex[n];
            var t = new Complex[n];

            Complex rho = DotProductComplex(r0, r);
            Complex rhoOld;

            for (int iter = 0; iter < maxIter; iter++)
            {
                // v = A * p
                ApplyAMatrix(H, alpha, p, v);

                Complex alpha_cg = rho / DotProductComplex(r0, v);

                // s = r - alpha * v
                for (int i = 0; i < n; i++) s[i] = r[i] - alpha_cg * v[i];

                // Check convergence
                double sNorm = NormComplex(s);
                if (sNorm < tol)
                {
                    for (int i = 0; i < n; i++) x[i] += alpha_cg * p[i];
                    return x;
                }

                // t = A * s
                ApplyAMatrix(H, alpha, s, t);

                Complex omega = DotProductComplex(t, s) / DotProductComplex(t, t);

                // x = x + alpha*p + omega*s
                for (int i = 0; i < n; i++) x[i] += alpha_cg * p[i] + omega * s[i];

                // r = s - omega * t
                for (int i = 0; i < n; i++) r[i] = s[i] - omega * t[i];

                // Check convergence
                double rNorm = NormComplex(r);
                if (rNorm < tol) return x;

                rhoOld = rho;
                rho = DotProductComplex(r0, r);

                Complex beta = (rho / rhoOld) * (alpha_cg / omega);

                // p = r + beta * (p - omega * v)
                for (int i = 0; i < n; i++) p[i] = r[i] + beta * (p[i] - omega * v[i]);
            }

            return x; // Return best solution after max iterations
        }

        /// <summary>
        /// Apply matrix A = (1 + i*alpha*H) to vector x, store in result
        /// </summary>
        private void ApplyAMatrix(double[,] H, double alpha, Complex[] x, Complex[] result)
        {
            int n = x.Length;
            for (int i = 0; i < n; i++)
            {
                result[i] = x[i]; // Identity part
                for (int j = 0; j < n; j++)
                {
                    result[i] += Complex.ImaginaryOne * alpha * H[i, j] * x[j];
                }
            }
        }

        /// <summary>
        /// Complex dot product (conjugate first argument)
        /// </summary>
        private static Complex DotProductComplex(Complex[] a, Complex[] b)
        {
            Complex sum = Complex.Zero;
            for (int i = 0; i < a.Length; i++)
            {
                sum += Complex.Conjugate(a[i]) * b[i];
            }
            return sum;
        }

        /// <summary>
        /// L2 norm of complex vector
        /// </summary>
        private static double NormComplex(Complex[] v)
        {
            double sum = 0.0;
            for (int i = 0; i < v.Length; i++)
            {
                sum += v[i].Magnitude * v[i].Magnitude;
            }
            return Math.Sqrt(sum);
        }

        /// <summary>
        /// Solve complex linear system A*x = b using Gaussian elimination with partial pivoting
        /// </summary>
        private Complex[] SolveLinearSystemComplex(Complex[,] A, Complex[] b)
        {
            int n = b.Length;

            // Create augmented matrix [A|b]
            var aug = new Complex[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    aug[i, j] = A[i, j];
                }
                aug[i, n] = b[i];
            }

            // Forward elimination with partial pivoting
            for (int col = 0; col < n; col++)
            {
                // Find pivot
                int maxRow = col;
                double maxVal = aug[col, col].Magnitude;
                for (int row = col + 1; row < n; row++)
                {
                    if (aug[row, col].Magnitude > maxVal)
                    {
                        maxVal = aug[row, col].Magnitude;
                        maxRow = row;
                    }
                }

                // Swap rows
                if (maxRow != col)
                {
                    for (int j = col; j <= n; j++)
                    {
                        (aug[col, j], aug[maxRow, j]) = (aug[maxRow, j], aug[col, j]);
                    }
                }

                // Eliminate
                if (aug[col, col].Magnitude < 1e-15)
                {
                    continue; // Skip near-singular column
                }

                for (int row = col + 1; row < n; row++)
                {
                    Complex factor = aug[row, col] / aug[col, col];
                    for (int j = col; j <= n; j++)
                    {
                        aug[row, j] -= factor * aug[col, j];
                    }
                }
            }

            // Back substitution
            var x = new Complex[n];
            for (int i = n - 1; i >= 0; i--)
            {
                x[i] = aug[i, n];
                for (int j = i + 1; j < n; j++)
                {
                    x[i] -= aug[i, j] * x[j];
                }
                if (aug[i, i].Magnitude > 1e-15)
                {
                    x[i] /= aug[i, i];
                }
            }

            return x;
        }

        /// <summary>
        /// Alternative: Taylor expansion of U = exp(-i*H*dt) to specified order.
        /// More accurate than Euler (order 1), less complex than Crank-Nicolson.
        /// Order 2: U ? 1 - i*H*dt - (H*dt)?/2
        /// Order 4: U ? 1 - i*H*dt - (H*dt)?/2 + i*(H*dt)?/6 + (H*dt)?/24
        /// </summary>
        /// <param name="dt">Time step</param>
        /// <param name="order">Taylor expansion order (2, 4, or 6 recommended)</param>
        public void EvolveQuantumTaylor(double dt, int order = 4)
        {
            if (_waveMulti == null) return;

            int d = GaugeDimension;
            int len = N * d;

            // Build Hamiltonian
            var H = ComputeGraphLaplacian();
            if (_correlationMass != null && _correlationMass.Length == N)
            {
                for (int i = 0; i < N; i++)
                {
                    H[i, i] += _correlationMass[i];
                }
            }

            var psiNew = new Complex[len];

            for (int a = 0; a < d; a++)
            {
                // Extract component
                var psi = new Complex[N];
                for (int i = 0; i < N; i++)
                {
                    psi[i] = _waveMulti[i * d + a];
                }

                // Apply Taylor series: sum_{k=0}^{order} (-i*H*dt)^k / k!
                var result = (Complex[])psi.Clone();
                var term = (Complex[])psi.Clone();

                for (int k = 1; k <= order; k++)
                {
                    // term = (-i*dt/k) * H * term_prev
                    var newTerm = new Complex[N];
                    Complex coeff = -Complex.ImaginaryOne * dt / k;

                    for (int i = 0; i < N; i++)
                    {
                        newTerm[i] = Complex.Zero;
                        for (int j = 0; j < N; j++)
                        {
                            newTerm[i] += H[i, j] * term[j];
                        }
                        newTerm[i] *= coeff;
                    }

                    term = newTerm;

                    // Add to result
                    for (int i = 0; i < N; i++)
                    {
                        result[i] += term[i];
                    }
                }

                // Store
                for (int i = 0; i < N; i++)
                {
                    psiNew[i * d + a] = result[i];
                }
            }

            _waveMulti = psiNew;

            // Taylor expansion doesn't preserve norm exactly - normalize if drift > threshold
            double norm = 0.0;
            for (int i = 0; i < len; i++)
            {
                norm += _waveMulti[i].Magnitude * _waveMulti[i].Magnitude;
            }

            double drift = Math.Abs(norm - 1.0);
            if (drift > PhysicsConstants.SpinorNormalizationThreshold)
            {
                // Gentle renormalization only when needed
                double correction = 1.0 / Math.Sqrt(norm);
                for (int i = 0; i < len; i++)
                {
                    _waveMulti[i] *= correction;
                }
            }
        }
    }
}
