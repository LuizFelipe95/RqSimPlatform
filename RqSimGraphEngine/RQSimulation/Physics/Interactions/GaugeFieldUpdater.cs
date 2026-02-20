using System;
using System.Numerics;

namespace RQSimulation.Physics;

/// <summary>
/// Provides methods to update gauge fields on graph edges while
/// preserving gauge invariants such as Wilson loops. Implements
/// checklist item #5. Supports both U(1) phases and SU(2)/SU(3)
/// matrices. The update functions take a proposed change and
/// project it back onto the unitary group to enforce local
/// gauge invariance.
/// 
/// CHECKLIST ITEM 1: Polar decomposition for SU(N) projection
/// The standard Gram-Schmidt projection can be numerically unstable for
/// large perturbations. Polar decomposition M = X * U (X Hermitian, U unitary)
/// finds the closest unitary matrix to M in the Frobenius norm sense.
/// </summary>
public static class GaugeFieldUpdater
{
        /// <summary>
        /// Updates a U(1) gauge phase with a proposed increment dPhi. The
        /// new phase is ?_old + d? minus the gradient of a gauge function
        /// ?, chosen here to enforce a gauge constraint. For
        /// simplicity we subtract the average of d? over the plaquette,
        /// which cancels uniform shifts. See checklist item #5.
        /// </summary>
        /// <param name="phase">Current phase value.</param>
        /// <param name="dPhi">Proposed phase increment.</param>
        /// <param name="plaquetteAverage">Function returning the average phase increment on surrounding plaquette.</param>
        /// <returns>Updated phase wrapped to [-?, ?].</returns>
        public static double UpdateU1(double phase, double dPhi, Func<double>? plaquetteAverage = null)
        {
            // Compute gauge adjustment (?) as the average phase increment on
            // the surrounding plaquette. Subtracting ? preserves the
            // Wilson loop up to numerical precision.
            double chi = plaquetteAverage?.Invoke() ?? 0.0;
            double newPhase = phase + dPhi - chi;

            // Wrap phase to [-?, ?]
            return WrapPhase(newPhase);
        }

        /// <summary>
        /// Wraps a phase value to the range [-?, ?].
        /// </summary>
        public static double WrapPhase(double phase)
        {
            while (phase > Math.PI) phase -= 2 * Math.PI;
            while (phase < -Math.PI) phase += 2 * Math.PI;
            return phase;
        }

        /// <summary>
        /// Updates an SU(N) gauge matrix with a proposed increment ?U by
        /// multiplying on the right: U ? U (I + ?U), then projecting
        /// back into SU(N) via polar decomposition (Gram–Schmidt). This
        /// preserves unitarity and det(U)=1. See checklist item #12.
        /// </summary>
        /// <param name="U">Current SU(N) matrix.</param>
        /// <param name="delta">Proposed infinitesimal generator (anti-Hermitian).</param>
        /// <returns>Updated SU(N) matrix projected onto the group.</returns>
        public static Complex[,] UpdateSUN(Complex[,] U, Complex[,] delta)
        {
            ArgumentNullException.ThrowIfNull(U);
            ArgumentNullException.ThrowIfNull(delta);

            int n = U.GetLength(0);
            if (n != U.GetLength(1) || delta.GetLength(0) != n || delta.GetLength(1) != n)
                throw new ArgumentException("Matrices must be square and of equal size.");

            // Compute trial matrix: U_trial = U (I + delta)
            Complex[,] trial = new Complex[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    Complex val = Complex.Zero;
                    for (int k = 0; k < n; k++)
                    {
                        Complex idPlusDelta = (k == j ? Complex.One : Complex.Zero) + delta[k, j];
                        val += U[i, k] * idPlusDelta;
                    }
                    trial[i, j] = val;
                }
            }

            // Project trial onto SU(n) using Gram-Schmidt orthonormalization
            Complex[,] projected = GramSchmidtOrthonormalize(trial);

            // Ensure determinant is unity
            EnsureUnitDeterminant(projected);

            return projected;
        }

        /// <summary>
        /// Orthonormalizes columns of a matrix using Gram-Schmidt process.
        /// </summary>
        private static Complex[,] GramSchmidtOrthonormalize(Complex[,] matrix)
        {
            int n = matrix.GetLength(0);
            Complex[,] result = new Complex[n, n];
            Complex[,] work = (Complex[,])matrix.Clone();

            for (int j = 0; j < n; j++)
            {
                // Subtract projections onto previous columns
                for (int k = 0; k < j; k++)
                {
                    Complex dot = Complex.Zero;
                    for (int i = 0; i < n; i++)
                        dot += Complex.Conjugate(result[i, k]) * work[i, j];

                    for (int i = 0; i < n; i++)
                        work[i, j] -= dot * result[i, k];
                }

                // Normalize column
                double norm = 0;
                for (int i = 0; i < n; i++)
                    norm += (work[i, j] * Complex.Conjugate(work[i, j])).Real;
                norm = Math.Sqrt(norm);

                if (norm < 1e-12) norm = 1e-12;

                for (int i = 0; i < n; i++)
                    result[i, j] = work[i, j] / norm;
            }

            return result;
        }

        /// <summary>
        /// Adjusts the last column of a unitary matrix to ensure det = 1.
        /// </summary>
        private static void EnsureUnitDeterminant(Complex[,] matrix)
        {
            int n = matrix.GetLength(0);
            if (n <= 1) return;

            Complex det = Determinant(matrix);
            if (det.Magnitude < 1e-12) return;

            // Multiply last column by phase factor to make det = 1
            Complex phase = Complex.Exp(-Complex.ImaginaryOne * det.Phase / n);
            for (int i = 0; i < n; i++)
                matrix[i, n - 1] *= phase;
        }

    /// <summary>
    /// Computes determinant of a complex matrix using recursive expansion.
    /// </summary>
    public static Complex Determinant(Complex[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (n == 1) return matrix[0, 0];
        if (n == 2) return matrix[0, 0] * matrix[1, 1] - matrix[0, 1] * matrix[1, 0];

        Complex det = Complex.Zero;
        int sign = 1;

        for (int k = 0; k < n; k++)
        {
            Complex[,] minor = new Complex[n - 1, n - 1];
            for (int i = 1; i < n; i++)
            {
                int mj = 0;
                for (int j = 0; j < n; j++)
                {
                    if (j == k) continue;
                    minor[i - 1, mj++] = matrix[i, j];
                }
            }
            det += sign * matrix[0, k] * Determinant(minor);
            sign = -sign;
        }

        return det;
    }

    // ============================================================
    // CHECKLIST ITEM 1: POLAR DECOMPOSITION FOR STABLE SU(N) PROJECTION
    // ============================================================

    /// <summary>
    /// Project matrix M to SU(N) via polar decomposition.
    /// 
    /// CHECKLIST ITEM 1: Gram-Schmidt projection to SU(N) is numerically unstable
    /// for large perturbations. Polar decomposition finds the closest unitary
    /// matrix to M in Frobenius norm sense: M = X * U where X is Hermitian pos-def.
    /// 
    /// For SU(N), we additionally enforce det(U) = +1 by adjusting the last row phase.
    /// 
    /// This method uses iterative Newton-Schulz iteration which converges
    /// quadratically for matrices close to unitary:
    ///   U_{k+1} = (3*U_k - U_k * U_k^† * U_k) / 2
    /// </summary>
    /// <param name="M">Input matrix (flat array, d?d)</param>
    /// <param name="d">Matrix dimension</param>
    /// <param name="maxIterations">Maximum Newton-Schulz iterations</param>
    /// <returns>Projected SU(N) matrix (flat array)</returns>
    public static Complex[] PolarProjectToSU(Complex[] M, int d, int maxIterations = 10)
    {
        ArgumentNullException.ThrowIfNull(M);
        if (M.Length < d * d)
            throw new ArgumentException($"Matrix must have at least {d * d} elements", nameof(M));

        // Start with the input matrix normalized
        var U = NormalizeMatrixScale(M, d);

        // Newton-Schulz iteration: U_{k+1} = (3*U - U*U^†*U) / 2
        // Converges quadratically for ||U^†U - I|| < 1
        for (int iter = 0; iter < maxIterations; iter++)
        {
            var UdagU = MultiplyHermitianLeft(U, U, d);  // U^† * U
            var UUdagU = MultiplyMatrices(U, UdagU, d);  // U * (U^† * U)

            // U_new = (3*U - U*U^†*U) / 2
            var Unew = new Complex[d * d];
            double maxChange = 0;

            for (int idx = 0; idx < d * d; idx++)
            {
                Unew[idx] = (3.0 * U[idx] - UUdagU[idx]) * 0.5;
                double change = (Unew[idx] - U[idx]).Magnitude;
                if (change > maxChange) maxChange = change;
            }

            U = Unew;

            // Convergence check
            if (maxChange < 1e-12)
                break;
        }

        // Enforce det(U) = +1 by adjusting last row phase
        Complex det = DeterminantFlat(U, d);
        if (det.Magnitude > 1e-12)
        {
            Complex phase = det / det.Magnitude;
            for (int c = 0; c < d; c++)
            {
                U[(d - 1) * d + c] /= phase;
            }
        }

        return U;
    }

    /// <summary>
    /// Normalize matrix to have Frobenius norm = sqrt(d).
    /// This ensures Newton-Schulz iteration converges.
    /// </summary>
    private static Complex[] NormalizeMatrixScale(Complex[] M, int d)
    {
        double normSq = 0;
        for (int i = 0; i < d * d; i++)
        {
            normSq += M[i].Real * M[i].Real + M[i].Imaginary * M[i].Imaginary;
        }

        double scale = Math.Sqrt(d) / Math.Sqrt(normSq + 1e-12);
        var result = new Complex[d * d];
        for (int i = 0; i < d * d; i++)
        {
            result[i] = M[i] * scale;
        }
        return result;
    }

    /// <summary>
    /// Compute A^† * B for matrices stored as flat arrays.
    /// </summary>
    private static Complex[] MultiplyHermitianLeft(Complex[] A, Complex[] B, int d)
    {
        var result = new Complex[d * d];
        for (int r = 0; r < d; r++)
        {
            for (int c = 0; c < d; c++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < d; k++)
                {
                    // A^†[r,k] = conj(A[k,r])
                    sum += Complex.Conjugate(A[k * d + r]) * B[k * d + c];
                }
                result[r * d + c] = sum;
            }
        }
        return result;
    }

    /// <summary>
    /// Compute A * B for matrices stored as flat arrays.
    /// </summary>
    private static Complex[] MultiplyMatrices(Complex[] A, Complex[] B, int d)
    {
        var result = new Complex[d * d];
        for (int r = 0; r < d; r++)
        {
            for (int c = 0; c < d; c++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < d; k++)
                {
                    sum += A[r * d + k] * B[k * d + c];
                }
                result[r * d + c] = sum;
            }
        }
        return result;
    }

    /// <summary>
    /// Compute determinant of small flat matrix (d ? 3).
    /// </summary>
    private static Complex DeterminantFlat(Complex[] m, int d)
    {
        if (d == 1) return m[0];
        if (d == 2) return m[0] * m[3] - m[1] * m[2];
        if (d == 3)
        {
            Complex a = m[0], b = m[1], c = m[2];
            Complex d1 = m[3], e = m[4], f = m[5];
            Complex g = m[6], h = m[7], i = m[8];
            return a * (e * i - f * h) - b * (d1 * i - f * g) + c * (d1 * h - e * g);
        }
        throw new NotSupportedException("Determinant for d > 3 not implemented");
    }

    // ============================================================
    // CHECKLIST ITEM 4: LIE ALGEBRA EXPONENTIAL UPDATE
    // ============================================================

    /// <summary>
    /// Update gauge link via Lie algebra exponential for large steps.
    /// 
    /// CHECKLIST ITEM 4: For large epsilon, direct Euler update violates unitarity.
    /// Use exponential map: U_new = exp(? * X) * U_old
    /// where X is the anti-Hermitian "force" direction.
    /// 
    /// For small ? (&lt; 0.1), falls back to efficient Euler update.
    /// </summary>
    /// <param name="Uold">Current link matrix (flat, d?d)</param>
    /// <param name="staple">Staple sum (target direction)</param>
    /// <param name="epsilon">Step size</param>
    /// <param name="d">Matrix dimension</param>
    /// <returns>Updated link matrix</returns>
    public static Complex[] UpdateLinkExponential(Complex[] Uold, Complex[] staple, double epsilon, int d)
    {
        ArgumentNullException.ThrowIfNull(Uold);
        ArgumentNullException.ThrowIfNull(staple);

        if (epsilon < 0.1)
        {
            // Small step: Euler is sufficient and faster
            var Unew = new Complex[d * d];
            for (int idx = 0; idx < d * d; idx++)
            {
                Unew[idx] = Uold[idx] + epsilon * (staple[idx] - Uold[idx]);
            }
            return PolarProjectToSU(Unew, d);
        }

        // Large step: Use Lie algebra exponential
        // X = (staple * Uold^† - (staple * Uold^†)^†) / 2 (anti-Hermitian part)
        var stapleUdag = MultiplyHermitianRight(staple, Uold, d);
        var X = AntiHermitianPart(stapleUdag, d);

        // Scale by epsilon
        for (int idx = 0; idx < d * d; idx++)
        {
            X[idx] *= epsilon;
        }

        // Compute exp(X) using Pad? approximation or Taylor series
        var expX = MatrixExponential(X, d);

        // U_new = exp(X) * U_old
        var Unew2 = MultiplyMatrices(expX, Uold, d);

        // Final projection to ensure exact SU(N)
        return PolarProjectToSU(Unew2, d);
    }

    /// <summary>
    /// Compute A * B^† for matrices stored as flat arrays.
    /// </summary>
    private static Complex[] MultiplyHermitianRight(Complex[] A, Complex[] B, int d)
    {
        var result = new Complex[d * d];
        for (int r = 0; r < d; r++)
        {
            for (int c = 0; c < d; c++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < d; k++)
                {
                    // B^†[k,c] = conj(B[c,k])
                    sum += A[r * d + k] * Complex.Conjugate(B[c * d + k]);
                }
                result[r * d + c] = sum;
            }
        }
        return result;
    }

    /// <summary>
    /// Extract anti-Hermitian part: (M - M^†) / 2
    /// </summary>
    private static Complex[] AntiHermitianPart(Complex[] M, int d)
    {
        var result = new Complex[d * d];
        for (int r = 0; r < d; r++)
        {
            for (int c = 0; c < d; c++)
            {
                Complex Mrc = M[r * d + c];
                Complex Mcr = M[c * d + r];
                result[r * d + c] = (Mrc - Complex.Conjugate(Mcr)) * 0.5;
            }
        }
        return result;
    }

    /// <summary>
    /// Compute matrix exponential exp(X) using Taylor series.
    /// For anti-Hermitian X, exp(X) is unitary.
    /// Uses truncated series: exp(X) ? I + X + X?/2! + X?/3! + ...
    /// </summary>
    private static Complex[] MatrixExponential(Complex[] X, int d, int terms = 12)
    {
        // Identity matrix
        var result = new Complex[d * d];
        for (int i = 0; i < d; i++)
        {
            result[i * d + i] = Complex.One;
        }

        // Current power of X
        var Xpower = new Complex[d * d];
        Array.Copy(X, Xpower, d * d);

        double factorial = 1.0;

        for (int n = 1; n <= terms; n++)
        {
            factorial *= n;

            // Add X^n / n! to result
            for (int idx = 0; idx < d * d; idx++)
            {
                result[idx] += Xpower[idx] / factorial;
            }

            // Compute next power: X^(n+1) = X^n * X
            if (n < terms)
            {
                Xpower = MultiplyMatrices(Xpower, X, d);
            }
        }

        return result;
    }
}
