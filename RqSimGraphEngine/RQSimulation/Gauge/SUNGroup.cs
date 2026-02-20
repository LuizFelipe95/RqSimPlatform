using System;
using System.Numerics;

namespace RQSimulation.Gauge;

/// <summary>
/// SU(N) gauge group implementation for arbitrary N.
///
/// CHECKLIST ITEM 44 (11.3): Gauge Group Support Extension
/// ==========================================================
/// Generic implementation of IGaugeGroup for SU(N) with arbitrary dimension N.
///
/// SU(N) consists of N×N complex unitary matrices with determinant +1.
/// The Lie algebra su(N) has dimension N² - 1 (number of generators).
///
/// CONSTRUCTION:
/// =============
/// Generators for SU(N) are traceless Hermitian matrices satisfying:
/// - Tr(T_a) = 0
/// - Tr(T_a T_b) = (1/2) δ_ab
/// - [T_a, T_b] = i f_abc T_c
///
/// For N=2: 3 Pauli matrices
/// For N=3: 8 Gell-Mann matrices
/// For N>3: Generalized construction via Cartan subalgebra + roots
///
/// APPLICATIONS:
/// =============
/// - SU(4): Grand Unified Theories (GUTs)
/// - SU(5): Georgi-Glashow model
/// - SU(N) large-N limit: 't Hooft expansion, AdS/CFT
///
/// PERFORMANCE NOTE:
/// =================
/// This generic implementation uses dense N×N complex matrices.
/// For N=2 and N=3, prefer specialized SU2Group and SU3Group
/// implementations which are significantly more efficient.
/// </summary>
public sealed class SUNGroup : IGaugeGroup<Complex[,]>
{
    private readonly int _n;
    private readonly int _generatorCount;
    private readonly Complex[,] _identity;
    private readonly Complex[][,] _generators;

    /// <summary>
    /// Create SU(N) group for given dimension N.
    /// </summary>
    /// <param name="n">Dimension N (must be ≥ 2)</param>
    public SUNGroup(int n)
    {
        if (n < 2)
            throw new ArgumentException("SU(N) requires N ≥ 2", nameof(n));

        _n = n;
        _generatorCount = n * n - 1;

        // Construct identity matrix
        _identity = new Complex[n, n];
        for (int i = 0; i < n; i++)
            _identity[i, i] = Complex.One;

        // Construct generators
        _generators = ConstructGenerators(n);
    }

    /// <inheritdoc/>
    public Complex[,] Identity => (Complex[,])_identity.Clone();

    /// <inheritdoc/>
    public int Dimension => _n;

    /// <inheritdoc/>
    public int GeneratorCount => _generatorCount;

    /// <inheritdoc/>
    public Complex[,] Multiply(Complex[,] a, Complex[,] b)
    {
        if (a.GetLength(0) != _n || a.GetLength(1) != _n ||
            b.GetLength(0) != _n || b.GetLength(1) != _n)
            throw new ArgumentException($"Matrix dimensions must be {_n}×{_n}");

        var result = new Complex[_n, _n];
        for (int i = 0; i < _n; i++)
        {
            for (int j = 0; j < _n; j++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < _n; k++)
                {
                    sum += a[i, k] * b[k, j];
                }
                result[i, j] = sum;
            }
        }
        return result;
    }

    /// <inheritdoc/>
    public Complex[,] Conjugate(Complex[,] element)
    {
        if (element.GetLength(0) != _n || element.GetLength(1) != _n)
            throw new ArgumentException($"Matrix dimensions must be {_n}×{_n}");

        var result = new Complex[_n, _n];
        for (int i = 0; i < _n; i++)
        {
            for (int j = 0; j < _n; j++)
            {
                result[i, j] = Complex.Conjugate(element[j, i]);
            }
        }
        return result;
    }

    /// <inheritdoc/>
    public Complex Trace(Complex[,] element)
    {
        if (element.GetLength(0) != _n || element.GetLength(1) != _n)
            throw new ArgumentException($"Matrix dimensions must be {_n}×{_n}");

        Complex sum = Complex.Zero;
        for (int i = 0; i < _n; i++)
        {
            sum += element[i, i];
        }
        return sum;
    }

    /// <inheritdoc/>
    public Complex[,] Exponential(double[] algebraCoefficients)
    {
        if (algebraCoefficients.Length != _generatorCount)
            throw new ArgumentException(
                $"SU({_n}) requires {_generatorCount} Lie algebra coefficients, got {algebraCoefficients.Length}",
                nameof(algebraCoefficients));

        // Construct X = i Σ θ_a T_a (anti-Hermitian matrix)
        var X = new Complex[_n, _n];
        for (int a = 0; a < _generatorCount; a++)
        {
            if (Math.Abs(algebraCoefficients[a]) < 1e-15) continue;

            Complex coeff = Complex.ImaginaryOne * algebraCoefficients[a];
            var gen = _generators[a];

            for (int i = 0; i < _n; i++)
            {
                for (int j = 0; j < _n; j++)
                {
                    X[i, j] += coeff * gen[i, j];
                }
            }
        }

        // Compute exp(X) using scaling and squaring method
        return MatrixExponential(X);
    }

    /// <inheritdoc/>
    public Complex[,] ComputePlaquette(Complex[,] u1, Complex[,] u2, Complex[,] u3, Complex[,] u4)
    {
        // P = U1 * U2 * U3† * U4†
        var u3Dag = Conjugate(u3);
        var u4Dag = Conjugate(u4);

        var temp1 = Multiply(u1, u2);
        var temp2 = Multiply(u3Dag, u4Dag);
        return Multiply(temp1, temp2);
    }

    /// <inheritdoc/>
    public double TraceDistance(Complex[,] a, Complex[,] b)
    {
        // d(U, V) = |1 - Re(Tr(U† * V)) / N|
        var aDag = Conjugate(a);
        var product = Multiply(aDag, b);
        double traceReal = Trace(product).Real;

        double normalized = traceReal / _n;
        return Math.Abs(1.0 - normalized);
    }

    /// <inheritdoc/>
    public Complex[,] ProjectToGroup(Complex[,] element)
    {
        if (element.GetLength(0) != _n || element.GetLength(1) != _n)
            throw new ArgumentException($"Matrix dimensions must be {_n}×{_n}");

        // Use iterative polar decomposition to project onto U(N)
        var U = (Complex[,])element.Clone();

        for (int iter = 0; iter < 10; iter++)
        {
            var Udag = Conjugate(U);
            var UdagU = Multiply(Udag, U);
            var UUdagU = Multiply(U, UdagU);

            // Newton-Schulz iteration: U_new = (3U - U*U†*U) / 2
            double maxChange = 0;
            for (int i = 0; i < _n; i++)
            {
                for (int j = 0; j < _n; j++)
                {
                    var Unew = (3.0 * U[i, j] - UUdagU[i, j]) * 0.5;
                    maxChange = Math.Max(maxChange, (Unew - U[i, j]).Magnitude);
                    U[i, j] = Unew;
                }
            }

            if (maxChange < 1e-12) break;
        }

        // Enforce det = +1 by multiplying by phase factor
        var det = Determinant(U);
        if (det.Magnitude > 1e-12)
        {
            Complex phase = Complex.Pow(det / det.Magnitude, 1.0 / _n);
            for (int i = 0; i < _n; i++)
            {
                for (int j = 0; j < _n; j++)
                {
                    U[i, j] /= phase;
                }
            }
        }

        return U;
    }

    /// <inheritdoc/>
    public Complex[,] RandomNearIdentity(Random random, double scale)
    {
        var theta = new double[_generatorCount];
        for (int a = 0; a < _generatorCount; a++)
        {
            theta[a] = (random.NextDouble() - 0.5) * 2.0 * scale;
        }
        return Exponential(theta);
    }

    /// <inheritdoc/>
    public Complex[,] GetGenerator(int index)
    {
        if (index < 0 || index >= _generatorCount)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"SU({_n}) has {_generatorCount} generators (0-{_generatorCount - 1}), got {index}");

        return (Complex[,])_generators[index].Clone();
    }

    /// <inheritdoc/>
    public double GetStructureConstant(int a, int b, int c)
    {
        if (a < 0 || a >= _generatorCount ||
            b < 0 || b >= _generatorCount ||
            c < 0 || c >= _generatorCount)
            return 0.0;

        // Compute f_abc = 2i Tr([T_a, T_b] T_c)
        var Ta = _generators[a];
        var Tb = _generators[b];
        var Tc = _generators[c];

        // [T_a, T_b] = T_a T_b - T_b T_a
        var TaTb = Multiply(Ta, Tb);
        var TbTa = Multiply(Tb, Ta);

        var commutator = new Complex[_n, _n];
        for (int i = 0; i < _n; i++)
        {
            for (int j = 0; j < _n; j++)
            {
                commutator[i, j] = TaTb[i, j] - TbTa[i, j];
            }
        }

        // Tr([T_a, T_b] T_c)
        var product = Multiply(commutator, Tc);
        Complex trace = Trace(product);

        // f_abc = 2i Tr([T_a, T_b] T_c) => Im(trace) * 2
        return trace.Imaginary * 2.0;
    }

    // ================================================================
    // PRIVATE HELPER METHODS
    // ================================================================

    private Complex[,] MatrixExponential(Complex[,] X)
    {
        // Scale matrix to have small norm
        double norm = MatrixNorm(X);
        int scalings = 0;
        while (norm > 0.5)
        {
            for (int i = 0; i < _n; i++)
                for (int j = 0; j < _n; j++)
                    X[i, j] *= 0.5;
            norm *= 0.5;
            scalings++;
        }

        // Taylor series: exp(X) ≈ I + X + X²/2! + X³/3! + ...
        var result = Identity;
        var term = Identity;

        for (int k = 1; k <= 15; k++)
        {
            term = Multiply(term, X);
            double factorial = Factorial(k);

            for (int i = 0; i < _n; i++)
            {
                for (int j = 0; j < _n; j++)
                {
                    result[i, j] += term[i, j] / factorial;
                }
            }

            // Check convergence
            if (MatrixNorm(term) < 1e-15) break;
        }

        // Square back: exp(X) = (exp(X/2^s))^(2^s)
        for (int s = 0; s < scalings; s++)
        {
            result = Multiply(result, result);
        }

        return result;
    }

    private double MatrixNorm(Complex[,] M)
    {
        double sum = 0;
        for (int i = 0; i < _n; i++)
        {
            for (int j = 0; j < _n; j++)
            {
                sum += M[i, j].Magnitude * M[i, j].Magnitude;
            }
        }
        return Math.Sqrt(sum);
    }

    private Complex Determinant(Complex[,] M)
    {
        if (_n == 2)
        {
            return M[0, 0] * M[1, 1] - M[0, 1] * M[1, 0];
        }
        else if (_n == 3)
        {
            return M[0, 0] * (M[1, 1] * M[2, 2] - M[1, 2] * M[2, 1])
                 - M[0, 1] * (M[1, 0] * M[2, 2] - M[1, 2] * M[2, 0])
                 + M[0, 2] * (M[1, 0] * M[2, 1] - M[1, 1] * M[2, 0]);
        }
        else
        {
            // LU decomposition for larger matrices (simplified)
            Complex det = Complex.One;
            var L = (Complex[,])M.Clone();

            for (int k = 0; k < _n; k++)
            {
                det *= L[k, k];
                if (L[k, k].Magnitude < 1e-15) return Complex.Zero;

                for (int i = k + 1; i < _n; i++)
                {
                    L[i, k] /= L[k, k];
                    for (int j = k + 1; j < _n; j++)
                    {
                        L[i, j] -= L[i, k] * L[k, j];
                    }
                }
            }

            return det;
        }
    }

    private static double Factorial(int n)
    {
        double result = 1.0;
        for (int i = 2; i <= n; i++) result *= i;
        return result;
    }

    private static Complex[][,] ConstructGenerators(int n)
    {
        int genCount = n * n - 1;
        var generators = new Complex[genCount][,];

        int index = 0;

        // Off-diagonal symmetric generators (real parts)
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                var gen = new Complex[n, n];
                gen[i, j] = Complex.One;
                gen[j, i] = Complex.One;
                generators[index++] = gen;
            }
        }

        // Off-diagonal antisymmetric generators (imaginary parts)
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                var gen = new Complex[n, n];
                gen[i, j] = -Complex.ImaginaryOne;
                gen[j, i] = Complex.ImaginaryOne;
                generators[index++] = gen;
            }
        }

        // Diagonal generators (Cartan subalgebra)
        for (int k = 0; k < n - 1; k++)
        {
            var gen = new Complex[n, n];
            double norm = Math.Sqrt(2.0 / (k + 1) / (k + 2));

            for (int i = 0; i <= k; i++)
            {
                gen[i, i] = norm;
            }
            gen[k + 1, k + 1] = -(k + 1) * norm;

            generators[index++] = gen;
        }

        return generators;
    }
}
