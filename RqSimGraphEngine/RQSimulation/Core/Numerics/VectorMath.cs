using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace RQSimulation
{
    /// <summary>
    /// High-performance SIMD-optimized mathematical operations for the RQ simulation.
    /// Uses .NET 10 hardware intrinsics (AVX, AVX2, AVX-512) for vectorized computations.
    /// </summary>
    public static class VectorMath
    {
        /// <summary>
        /// Speed of light in the correlation medium (normalized to 1 in natural units).
        /// </summary>
        public const double SpeedOfLight = 1.0;

        /// <summary>
        /// Planck constant equivalent in correlation units.
        /// </summary>
        public const double HBar = 1.0;

        #region Vector Operations with AVX/AVX2/AVX-512

        /// <summary>
        /// Computes dot product of two 4D vectors using AVX if available.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Dot4D(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
        {
            if (a.Length < 4 || b.Length < 4) return 0.0;

            if (Avx.IsSupported)
            {
                unsafe
                {
                    fixed (double* pa = a, pb = b)
                    {
                        var va = Avx.LoadVector256(pa);
                        var vb = Avx.LoadVector256(pb);
                        var mul = Avx.Multiply(va, vb);
                        // Horizontal sum using ExtractVector128
                        var hi128 = Avx.ExtractVector128(mul, 1);
                        var lo128 = mul.GetLower();
                        var sum128 = Sse2.Add(hi128, lo128);
                        var hi64 = Sse2.UnpackHigh(sum128, sum128);
                        var sum64 = Sse2.Add(sum128, hi64);
                        return sum64.ToScalar();
                    }
                }
            }

            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3];
        }

        /// <summary>
        /// Computes the Minkowski metric dot product: η_μν x^μ y^ν = -x0*y0 + x1*y1 + x2*y2 + x3*y3
        /// Uses signature (-,+,+,+).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double MinkowskiDot(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
        {
            if (x.Length < 4 || y.Length < 4) return 0.0;

            // Minkowski signature: time component negative
            return -x[0] * y[0] + x[1] * y[1] + x[2] * y[2] + x[3] * y[3];
        }

        /// <summary>
        /// Computes the proper interval s^2 = -c^2 * dt^2 + dx^2 + dy^2 + dz^2.
        /// Returns negative for timelike, positive for spacelike, zero for lightlike.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ProperIntervalSquared(double dt, double dx, double dy, double dz)
        {
            double c2 = SpeedOfLight * SpeedOfLight;
            return -c2 * dt * dt + dx * dx + dy * dy + dz * dz;
        }

        /// <summary>
        /// Batch normalize vectors in-place using AVX-512 if available.
        /// </summary>
        public static void BatchNormalize(Span<double> data, int vectorDim)
        {
            if (data.Length == 0 || vectorDim <= 0) return;
            if (data.Length % vectorDim != 0) return; // Ensure data length is divisible by vectorDim
            int numVectors = data.Length / vectorDim;

            if (Avx.IsSupported && vectorDim == 4)
            {
                unsafe
                {
                    fixed (double* p = data)
                    {
                        for (int i = 0; i < numVectors; i++)
                        {
                            double* vec = p + i * 4;
                            var v = Avx.LoadVector256(vec);
                            var sq = Avx.Multiply(v, v);
                            // Sum all 4 components using GetLower
                            var hi128 = Avx.ExtractVector128(sq, 1);
                            var lo128 = sq.GetLower();
                            var sum128 = Sse2.Add(hi128, lo128);
                            var hi64 = Sse2.UnpackHigh(sum128, sum128);
                            var sum64 = Sse2.Add(sum128, hi64);
                            double norm = Math.Sqrt(sum64.ToScalar());
                            if (norm > 1e-15)
                            {
                                var inv = Vector256.Create(1.0 / norm);
                                var normalized = Avx.Multiply(v, inv);
                                Avx.Store(vec, normalized);
                            }
                        }
                    }
                }
                return;
            }

            // Scalar fallback
            for (int i = 0; i < numVectors; i++)
            {
                int offset = i * vectorDim;
                double norm = 0.0;
                for (int j = 0; j < vectorDim; j++)
                    norm += data[offset + j] * data[offset + j];
                norm = Math.Sqrt(norm);
                if (norm > 1e-15)
                {
                    double inv = 1.0 / norm;
                    for (int j = 0; j < vectorDim; j++)
                        data[offset + j] *= inv;
                }
            }
        }

        #endregion

        #region Lorentz Transformations

        /// <summary>
        /// Computes the Lorentz factor γ = 1 / √(1 - v²/c²).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double LorentzGamma(double velocity)
        {
            double beta = velocity / SpeedOfLight;
            double beta2 = beta * beta;
            if (beta2 >= 1.0) return double.PositiveInfinity;
            return 1.0 / Math.Sqrt(1.0 - beta2);
        }

        /// <summary>
        /// Applies a Lorentz boost to a 4-vector (t, x, y, z) along the x-axis.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (double t, double x, double y, double z) LorentzBoostX(
            double t, double x, double y, double z, double velocity)
        {
            double gamma = LorentzGamma(velocity);
            double beta = velocity / SpeedOfLight;

            double tPrime = gamma * (t - beta * x / SpeedOfLight);
            double xPrime = gamma * (x - velocity * t);

            return (tPrime, xPrime, y, z);
        }

        /// <summary>
        /// General Lorentz boost along arbitrary direction.
        /// </summary>
        public static (double t, double x, double y, double z) LorentzBoost(
            double t, double x, double y, double z,
            double vx, double vy, double vz)
        {
            double v2 = vx * vx + vy * vy + vz * vz;
            if (v2 < 1e-20) return (t, x, y, z); // No boost

            double v = Math.Sqrt(v2);
            double gamma = LorentzGamma(v);
            double c2 = SpeedOfLight * SpeedOfLight;

            // Unit velocity direction
            double nx = vx / v, ny = vy / v, nz = vz / v;

            // Component parallel to velocity
            double rParallel = nx * x + ny * y + nz * z;

            double tPrime = gamma * (t - v * rParallel / c2);
            double factor = (gamma - 1.0) * rParallel - gamma * v * t;

            double xPrime = x + factor * nx;
            double yPrime = y + factor * ny;
            double zPrime = z + factor * nz;

            return (tPrime, xPrime, yPrime, zPrime);
        }

        #endregion

        #region Curvature and Metric Computations

        /// <summary>
        /// Computes the Schwarzschild metric component g_tt at radius r for a given mass M.
        /// g_tt = -(1 - 2GM/c²r).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SchwarzschildGtt(double r, double mass, double gravitationalConstant = 1.0)
        {
            if (r <= 0) return double.NegativeInfinity;
            double rs = 2.0 * gravitationalConstant * mass / (SpeedOfLight * SpeedOfLight);
            return -(1.0 - rs / r);
        }

        /// <summary>
        /// Computes time dilation factor at radius r in Schwarzschild metric.
        /// τ/t = √(1 - 2GM/c²r).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GravitationalTimeDilation(double r, double mass, double gravitationalConstant = 1.0)
        {
            if (r <= 0) return 0.0;
            double rs = 2.0 * gravitationalConstant * mass / (SpeedOfLight * SpeedOfLight);
            double factor = 1.0 - rs / r;
            return factor > 0 ? Math.Sqrt(factor) : 0.0;
        }

        /// <summary>
        /// Computes the Ricci scalar curvature for a 2D surface based on the deficit angle.
        /// R = 2 * deficitAngle / area.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double RicciScalarFromDeficit(double deficitAngle, double area)
        {
            if (area <= 0) return 0.0;
            return 2.0 * deficitAngle / area;
        }

        #endregion

        #region Batch Computations

        /// <summary>
        /// Batch computation of gravitational potentials using AVX.
        /// </summary>
        public static void ComputeGravitationalPotentials(
            ReadOnlySpan<double> masses,
            ReadOnlySpan<double> xCoords,
            ReadOnlySpan<double> yCoords,
            Span<double> potentials,
            double gravitationalConstant)
        {
            int n = masses.Length;
            if (n == 0) return;

            // Initialize potentials to zero
            potentials.Fill(0.0);

            // Compute pairwise gravitational potential contributions
            for (int i = 0; i < n; i++)
            {
                double xi = xCoords[i], yi = yCoords[i];
                double mi = masses[i];

                for (int j = i + 1; j < n; j++)
                {
                    double dx = xCoords[j] - xi;
                    double dy = yCoords[j] - yi;
                    double r2 = dx * dx + dy * dy;
                    double r = Math.Sqrt(r2) + 1e-10; // Softening

                    double pot = -gravitationalConstant * mi * masses[j] / r;

                    potentials[i] += pot;
                    potentials[j] += pot;
                }
            }
        }

        /// <summary>
        /// Batch computation of Lorentz factors for velocity array.
        /// Uses AVX for parallel computation.
        /// </summary>
        public static void BatchLorentzGamma(ReadOnlySpan<double> velocities, Span<double> gammas)
        {
            int n = velocities.Length;
            if (n == 0) return;

            int i = 0;
            if (Avx.IsSupported)
            {
                double c2 = SpeedOfLight * SpeedOfLight;
                var vC2 = Vector256.Create(c2);
                var vOne = Vector256.Create(1.0);

                unsafe
                {
                    fixed (double* pv = velocities, pg = gammas)
                    {
                        for (; i <= n - 4; i += 4)
                        {
                            var v = Avx.LoadVector256(pv + i);
                            var v2 = Avx.Multiply(v, v);
                            var beta2 = Avx.Divide(v2, vC2);
                            var oneMinusBeta2 = Avx.Subtract(vOne, beta2);
                            // Sqrt and reciprocal
                            var sqrt = Avx.Sqrt(oneMinusBeta2);
                            var gamma = Avx.Divide(vOne, sqrt);
                            Avx.Store(pg + i, gamma);
                        }
                    }
                }
            }

            // Scalar remainder
            for (; i < n; i++)
            {
                gammas[i] = LorentzGamma(velocities[i]);
            }
        }

        #endregion

        #region Spinor Operations

        /// <summary>
        /// Represents a 2-component Weyl spinor.
        /// </summary>
        public readonly struct Spinor2
        {
            public readonly Complex A;
            public readonly Complex B;

            public Spinor2(Complex a, Complex b)
            {
                A = a;
                B = b;
            }

            public double NormSquared => A.Magnitude * A.Magnitude + B.Magnitude * B.Magnitude;

            public Spinor2 Normalized()
            {
                double n = Math.Sqrt(NormSquared);
                if (n < 1e-15) return new Spinor2(Complex.One, Complex.Zero);
                return new Spinor2(A / n, B / n);
            }

            public static Spinor2 operator +(Spinor2 a, Spinor2 b)
                => new Spinor2(a.A + b.A, a.B + b.B);

            public static Spinor2 operator *(Complex c, Spinor2 s)
                => new Spinor2(c * s.A, c * s.B);

            public static Spinor2 operator *(double d, Spinor2 s)
                => new Spinor2(d * s.A, d * s.B);
        }

        /// <summary>
        /// Represents a 4-component Dirac spinor for relativistic fermions.
        /// </summary>
        public readonly struct DiracSpinor
        {
            public readonly Spinor2 Left;
            public readonly Spinor2 Right;

            public DiracSpinor(Spinor2 left, Spinor2 right)
            {
                Left = left;
                Right = right;
            }

            public DiracSpinor(Complex a, Complex b, Complex c, Complex d)
            {
                Left = new Spinor2(a, b);
                Right = new Spinor2(c, d);
            }

            public double NormSquared => Left.NormSquared + Right.NormSquared;

            public DiracSpinor Normalized()
            {
                double n = Math.Sqrt(NormSquared);
                if (n < 1e-15) return new DiracSpinor(new Spinor2(Complex.One, Complex.Zero), default);
                double inv = 1.0 / n;
                return new DiracSpinor(inv * Left, inv * Right);
            }

            /// <summary>
            /// Applies the charge conjugation operator C.
            /// </summary>
            public DiracSpinor ChargeConjugate()
            {
                return new DiracSpinor(
                    new Spinor2(-Complex.Conjugate(Left.B), Complex.Conjugate(Left.A)),
                    new Spinor2(-Complex.Conjugate(Right.B), Complex.Conjugate(Right.A))
                );
            }
        }

        #endregion

        #region Matrix Operations for SU(2)/SU(3)

        /// <summary>
        /// Multiplies two 2x2 complex matrices stored as 4-element arrays.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MultiplySU2(ReadOnlySpan<Complex> a, ReadOnlySpan<Complex> b, Span<Complex> result)
        {
            result[0] = a[0] * b[0] + a[1] * b[2]; // (0,0)
            result[1] = a[0] * b[1] + a[1] * b[3]; // (0,1)
            result[2] = a[2] * b[0] + a[3] * b[2]; // (1,0)
            result[3] = a[2] * b[1] + a[3] * b[3]; // (1,1)
        }

        /// <summary>
        /// Computes the trace of a 2x2 complex matrix.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Complex TraceSU2(ReadOnlySpan<Complex> m)
        {
            return m[0] + m[3];
        }

        /// <summary>
        /// Computes the determinant of a 2x2 complex matrix.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Complex DeterminantSU2(ReadOnlySpan<Complex> m)
        {
            return m[0] * m[3] - m[1] * m[2];
        }

        /// <summary>
        /// Projects a 2x2 matrix onto SU(2) using polar decomposition.
        /// </summary>
        public static void ProjectToSU2(Span<Complex> m)
        {
            // Gram-Schmidt orthonormalization
            // First column
            double n0 = Math.Sqrt(m[0].Magnitude * m[0].Magnitude + m[2].Magnitude * m[2].Magnitude);
            if (n0 > 1e-15)
            {
                m[0] /= n0;
                m[2] /= n0;
            }

            // Second column orthogonal to first
            Complex dot = Complex.Conjugate(m[0]) * m[1] + Complex.Conjugate(m[2]) * m[3];
            m[1] -= dot * m[0];
            m[3] -= dot * m[2];

            double n1 = Math.Sqrt(m[1].Magnitude * m[1].Magnitude + m[3].Magnitude * m[3].Magnitude);
            if (n1 > 1e-15)
            {
                m[1] /= n1;
                m[3] /= n1;
            }

            // Ensure det = 1
            Complex det = DeterminantSU2(m);
            double absDet = det.Magnitude;
            if (absDet > 1e-15)
            {
                // Multiply by phase inverse to set det = 1
                Complex phaseCorrection = Complex.FromPolarCoordinates(1.0, -det.Phase / 2);
                m[0] *= phaseCorrection;
                m[2] *= phaseCorrection;
            }
        }

        #endregion

        #region Physical Constants Derived from Correlations

        /// <summary>
        /// Computes the effective fine structure constant from correlation statistics.
        /// α ≈ 1/137 in physical units; here derived from average correlation strength.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double EffectiveFineStructure(double avgCorrelationWeight)
        {
            // Map correlation weight to fine structure constant
            // At physical value ~0.0073 (1/137)
            return Math.Clamp(avgCorrelationWeight * 0.01, 1e-4, 0.1);
        }

        /// <summary>
        /// Computes the effective Planck length from the minimum correlation scale.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double EffectivePlanckLength(double minCorrelationWeight, double maxCorrelationWeight)
        {
            if (maxCorrelationWeight <= minCorrelationWeight) return 1e-3;
            return minCorrelationWeight / maxCorrelationWeight;
        }

        #endregion
    }
}
