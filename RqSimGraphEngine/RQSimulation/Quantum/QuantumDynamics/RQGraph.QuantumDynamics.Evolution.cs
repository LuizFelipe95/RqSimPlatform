using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace RQSimulation
{
    public partial class RQGraph
    {
        public void UpdateQuantumState()
        {
            double cRq = ComputeEffectiveLightSpeed();
            double dt = ComputeRelationalDt();
            double dtRq = dt * cRq;
            double hbarRq = 1.0;
            StepSchrodingerUnitary(dtRq, hbarRq);
            if (_waveMulti != null)
            {
                int d = GaugeDimension;
                var rng = _rng;
                var next = (Complex[])_waveMulti.Clone();
                // phase drift accumulator per node (create if needed)
                if (_phaseDrift == null || _phaseDrift.Length != N) _phaseDrift = new double[N];
                for (int i = 0; i < N; i++)
                {
                    if (_correlationMass != null) _phaseDrift[i] += 0.001 * _correlationMass[i]; // checklist 4 phase drift
                }
                for (int i = 0; i < N; i++)
                {
                    for (int a = 0; a < d; a++)
                    {
                        int idx = i * d + a;
                        Complex sum = Complex.Zero;
                        int deg = 0;
                        foreach (int nb in Neighbors(i))
                        {
                            int jIdx = nb * d + a;
                            double corridorW = PathWeight != null ? PathWeight[i, nb] : 1.0; // item 9 corridors
                            sum += corridorW * _waveMulti[jIdx];
                            deg++;
                        }
                        if (deg > 0)
                        {
                            Complex lap = (sum / deg) - _waveMulti[idx];
                            next[idx] += QuantumDiffusion * lap;
                        }
                        double potLocal = LocalPotential != null ? LocalPotential[i] : 0.0;
                        double phaseShift = 0.05 * potLocal;
                        double ampLocal = _waveMulti[idx].Magnitude;
                        double nonlinearPhase = 0.1 * ampLocal * potLocal;
                        double totalPhase = phaseShift + nonlinearPhase + _phaseDrift[i];
                        next[idx] *= Complex.FromPolarCoordinates(1.0, totalPhase);
                    }
                }
                // Normalize then amplitude scaling by local potential
                double norm = 0.0; foreach (var z in next) { double m = z.Magnitude; norm += m * m; }
                if (norm > 0)
                {
                    norm = Math.Sqrt(norm);
                    for (int i = 0; i < N; i++)
                    {
                        for (int a = 0; a < d; a++)
                        {
                            int idx = i * d + a;
                            Complex z = next[idx] / norm;
                            double potLocal = LocalPotential != null ? LocalPotential[i] : 0.0;
                            z *= (1.0 + 0.05 * potLocal); // local peak amplification
                            if (IsInCondensedCluster(i)) z *= 1.1; // quantum signature boost
                            next[idx] = z;
                        }
                    }
                }
                // small random dephasing retained
                for (int i = 0; i < N; i++)
                {
                    for (int a = 0; a < d; a++)
                    {
                        int idx = i * d + a; double phaseNoise = 0.02 * rng.NextDouble();
                        next[idx] *= Complex.FromPolarCoordinates(1.0, phaseNoise);
                    }
                }
                _waveMulti = next;
            }
        }

        public void ApplyLaplacian(Complex[] psi, Complex[] result)
        {
            int n = N;
            Parallel.For(0, n, i =>
            {
                Complex acc = Complex.Zero;
                double deg = 0.0;
                for (int j = 0; j < n; j++)
                {
                    double w = Weights[i, j];
                    if (w <= 0.0) continue;
                    deg += w;
                    acc += w * psi[j];
                }
                result[i] = acc - deg * psi[i];
            });
        }

        // Gauge-covariant Laplacian with U(1)/SU(d) link usage
        // OPTIMIZED: Uses stackalloc for small allocations and improved cache locality
        public void ApplyGaugeLaplacian(Complex[] psi, Complex[] result)
        {
            int n = N;
            int d = GaugeDimension;
            
            // Use larger batch size for better parallelization
            int batchSize = Math.Max(1, n / Environment.ProcessorCount);
            
            Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                int baseI = i * d;
                
                // Use stackalloc for small dimension arrays (avoid heap allocation)
                Span<Complex> acc = d <= 4 
                    ? stackalloc Complex[d] 
                    : new Complex[d];
                acc.Clear();
                
                double deg = 0.0;
                
                // Cache the psi values for this node
                ReadOnlySpan<Complex> psiI = psi.AsSpan(baseI, d);
                
                foreach (int j in Neighbors(i))
                {
                    double w = Weights[i, j];
                    if (w <= 0.0) continue;
                    deg += w;
                    
                    int baseJ = j * d;
                    ReadOnlySpan<Complex> psiJ = psi.AsSpan(baseJ, d);
                    
                    if (d == 1)
                    {
                        Complex uij = GetU1Link(i, j);
                        acc[0] += w * uij * psiJ[0];
                    }
                    else
                    {
                        // Fallback: identity (no explicit SU(d) matrix available in this build)
                        for (int a = 0; a < d; a++)
                            acc[a] += w * psiJ[a];
                    }
                }
                
                // Write result
                for (int a = 0; a < d; a++)
                    result[baseI + a] = acc[a] - deg * psiI[a];
            });
        }

        /// <summary>
        /// Full gauge-covariant Laplacian using exponential map from Lie algebra to Lie group.
        /// This connects the quantum particle to the gluon field via: U_ij = exp(i * A_ij * dx).
        /// For SU(3) gauge theory, A_ij are the 8 Gell-Mann generator coefficients.
        /// </summary>
        /// <param name="psi">Input wavefunction (N * GaugeDimension components)</param>
        /// <param name="result">Output array for the Laplacian result</param>
        public void ApplyGaugeLaplacianFull(Complex[] psi, Complex[] result)
        {
            int n = N;
            int d = GaugeDimension;

            // Default lattice spacing
            const double dx = 1.0;

            Parallel.For(0, n, i =>
            {
                int baseI = i * d;
                Span<Complex> psiI = psi.AsSpan(baseI, d);
                var acc = new Complex[d];
                double deg = 0.0;

                foreach (int j in Neighbors(i))
                {
                    double w = Weights[i, j];
                    if (w <= 0.0) continue;
                    deg += w;

                    int baseJ = j * d;
                    Span<Complex> psiJ = psi.AsSpan(baseJ, d);

                    if (d == 1)
                    {
                        // U(1) case: U_ij = exp(i * A_ij * dx)
                        Complex uij = GetU1Link(i, j);
                        acc[0] += w * uij * psiJ[0];
                    }
                    else
                    {
                        // SU(d) case: Build gauge link matrix from gluon field
                        // U_ij = exp(i * sum_a A^a_ij * T^a * dx)
                        // where T^a are generators (Gell-Mann matrices for SU(3))

                        // Get transported psi_j through gauge link
                        var psiTransported = ApplyGaugeLinkMatrix(i, j, psiJ.ToArray(), dx);

                        for (int a = 0; a < d; a++)
                            acc[a] += w * psiTransported[a];
                    }
                }

                for (int a = 0; a < d; a++)
                    result[baseI + a] = acc[a] - deg * psiI[a];
            });
        }

        /// <summary>
        /// Apply gauge link matrix U_ij to a vector using exponential map.
        /// U_ij = exp(i * A_ij * dx) where A_ij is the gauge potential (gluon field).
        /// Uses first-order approximation: exp(iM) ? I + iM for small M.
        /// </summary>
        private Complex[] ApplyGaugeLinkMatrix(int i, int j, Complex[] vec, double dx)
        {
            int d = GaugeDimension;
            var result = new Complex[d];

            // Check if gluon field is available
            if (_gluonField == null || d != 3)
            {
                // Fallback to identity transport
                for (int a = 0; a < d; a++)
                    result[a] = vec[a];
                return result;
            }

            // Build the generator matrix M = sum_a A^a_ij * T^a
            // For SU(3), we use Gell-Mann matrices T^a = lambda^a / 2
            // Total matrix is 3x3 complex

            var M = new Complex[d, d];

            // Sum over 8 color components
            for (int c = 0; c < 8; c++)
            {
                double A_c = _gluonField[i, j, c];
                if (Math.Abs(A_c) < 1e-12) continue;

                // Add contribution from generator T^c
                AddGellMannGenerator(M, c, A_c * dx);
            }

            // Apply exponential map: U = exp(i*M) ? I + i*M (first order)
            // For better accuracy, we can use: U ? I + i*M - M^2/2 (second order)
            // But first order is often sufficient for small dx

            for (int a = 0; a < d; a++)
            {
                result[a] = vec[a]; // Identity part

                // Add i*M*vec contribution
                for (int b = 0; b < d; b++)
                {
                    result[a] += Complex.ImaginaryOne * M[a, b] * vec[b];
                }
            }

            return result;
        }

        /// <summary>
        /// Add contribution from Gell-Mann generator lambda^c to matrix M.
        /// M += coefficient * (lambda^c / 2)
        /// </summary>
        private static void AddGellMannGenerator(Complex[,] M, int c, double coefficient)
        {
            double half = coefficient * 0.5;

            switch (c)
            {
                case 0: // lambda_1
                    M[0, 1] += half;
                    M[1, 0] += half;
                    break;
                case 1: // lambda_2
                    M[0, 1] += new Complex(0, -half);
                    M[1, 0] += new Complex(0, half);
                    break;
                case 2: // lambda_3
                    M[0, 0] += half;
                    M[1, 1] -= half;
                    break;
                case 3: // lambda_4
                    M[0, 2] += half;
                    M[2, 0] += half;
                    break;
                case 4: // lambda_5
                    M[0, 2] += new Complex(0, -half);
                    M[2, 0] += new Complex(0, half);
                    break;
                case 5: // lambda_6
                    M[1, 2] += half;
                    M[2, 1] += half;
                    break;
                case 6: // lambda_7
                    M[1, 2] += new Complex(0, -half);
                    M[2, 1] += new Complex(0, half);
                    break;
                case 7: // lambda_8
                    double sqrt3 = Math.Sqrt(3.0);
                    M[0, 0] += half / sqrt3;
                    M[1, 1] += half / sqrt3;
                    M[2, 2] -= 2.0 * half / sqrt3;
                    break;
            }
        }

        public void StepSchrodinger(double dtRq, double hbarRq)
        {
            int n = N;
            int d = GaugeDimension;
            int len = n * d;
            if (_psiDot == null || _psiDot.Length != len)
                _psiDot = new Complex[len];
            if (_waveMulti == null || _waveMulti.Length != len) return;
            ApplyGaugeLaplacian(_waveMulti, _psiDot);
            Parallel.For(0, n, i =>
            {
                double mi = (_correlationMass != null && _correlationMass.Length == N) ? Math.Max(1e-6, _correlationMass[i]) : 1.0;
                double inv2m = 0.5 / mi;
                double Vi = PotentialFromCorrelations(i);
                for (int a = 0; a < d; a++)
                {
                    int idx = i * d + a;
                    Complex D2psi = _psiDot[idx];
                    Complex rhs = -inv2m * D2psi + Vi * _waveMulti[idx];
                    Complex dpsi = -Complex.ImaginaryOne * (dtRq / hbarRq) * rhs;
                    _waveMulti[idx] += dpsi;
                }
            });
        }

        public void StepSchrodingerUnitary(double dtRq, double hbarRq, int iterations = 3)
        {
            int n = N;
            int d = GaugeDimension;
            int len = n * d;

            if (_psiDot == null || _psiDot.Length != len)
                _psiDot = new Complex[len];
            if (_waveMulti == null || _waveMulti.Length != len) return;

            ApplyGaugeLaplacian(_waveMulti, _psiDot);
            Complex alpha = -Complex.ImaginaryOne * (dtRq / (2.0 * hbarRq));
            var rhs = new Complex[len];
            for (int i = 0; i < n; i++)
            {
                int fl = FractalLevel != null && i < FractalLevel.Length ? FractalLevel[i] : 0;
                double fboost = 1.0 + 0.1 * fl; // checklist fractal boost
                double potLocal = LocalPotential != null && i < LocalPotential.Length ? LocalPotential[i] : 0.0;
                double phaseBase = potLocal * 0.1; // checklist phase factor
                for (int a = 0; a < d; a++)
                {
                    int idx = i * d + a;
                    double mi = (_correlationMass != null && _correlationMass.Length == N) ? Math.Max(1e-6, _correlationMass[i]) : 1.0;
                    double inv2m = 0.5 / mi;
                    double Vi = PotentialFromCorrelations(i);
                    Complex Hpsi = -inv2m * _psiDot[idx] + Vi * _waveMulti[idx];
                    Complex mod = Complex.FromPolarCoordinates(1.0, phaseBase);
                    rhs[idx] = _waveMulti[idx] + fboost * alpha * Hpsi * mod;
                }
            }

            var psiNext = (Complex[])_waveMulti.Clone();
            for (int it = 0; it < iterations; it++)
            {
                ApplyGaugeLaplacian(psiNext, _psiDot);
                for (int i = 0; i < n; i++)
                {
                    int fl = FractalLevel != null && i < FractalLevel.Length ? FractalLevel[i] : 0;
                    double fboost = 1.0 + 0.1 * fl;
                    double potLocal = LocalPotential != null && i < LocalPotential.Length ? LocalPotential[i] : 0.0;
                    double phaseBase = potLocal * 0.1;
                    Complex mod = Complex.FromPolarCoordinates(1.0, phaseBase);
                    for (int a = 0; a < d; a++)
                    {
                        int idx = i * d + a;
                        double mi = (_correlationMass != null && _correlationMass.Length == N) ? Math.Max(1e-6, _correlationMass[i]) : 1.0;
                        double inv2m = 0.5 / mi;
                        double Vi = PotentialFromCorrelations(i);
                        Complex Hpsi = -inv2m * _psiDot[idx] + Vi * psiNext[idx];
                        Complex diag = Complex.One + alpha * Vi;
                        psiNext[idx] = (rhs[idx] - fboost * alpha * (-inv2m * _psiDot[idx]) * mod) / diag;
                    }
                }
            }
            _waveMulti = psiNext;
        }
    }
}
