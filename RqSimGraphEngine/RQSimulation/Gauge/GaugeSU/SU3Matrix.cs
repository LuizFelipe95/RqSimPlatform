using System;
using System.Numerics;

namespace RQSimulation.Gauge;

/// <summary>
/// Full SU(3) matrix representation for strong gauge fields (QCD).
/// 
/// CHECKLIST ITEM 1: Replace real gluon field arrays with proper SU(3) matrices.
/// 
/// SU(3) is the gauge group for strong interactions (QCD). Each link variable
/// U_ij is a 3?3 unitary matrix with det(U) = +1.
/// 
/// The 8 Gell-Mann matrices ?_a (a=1..8) generate the su(3) Lie algebra:
///   [?_a/2, ?_b/2] = i f_{abc} ?_c/2
/// 
/// where f_{abc} are the SU(3) structure constants.
/// 
/// Any SU(3) element can be written as:
///   U = exp(i ?_a ?_a/2)
/// 
/// This class provides:
/// - 3?3 complex matrix storage
/// - Gell-Mann generators
/// - Exponential map: su(3) ? SU(3)
/// - Group operations (multiply, inverse, trace)
/// </summary>
public struct SU3Matrix
{
    // Matrix elements stored as 3?3 complex array (row-major)
    // M[r,c] = Elements[r * 3 + c]
    private Complex[] _elements;

    /// <summary>
    /// Access matrix element at row r, column c.
    /// </summary>
    public Complex this[int r, int c]
    {
        get => _elements[r * 3 + c];
        set => _elements[r * 3 + c] = value;
    }

    /// <summary>
    /// Create SU(3) identity matrix.
    /// </summary>
    public static SU3Matrix Identity
    {
        get
        {
            var m = new SU3Matrix { _elements = new Complex[9] };
            m._elements[0] = Complex.One;  // (0,0)
            m._elements[4] = Complex.One;  // (1,1)
            m._elements[8] = Complex.One;  // (2,2)
            return m;
        }
    }

    /// <summary>
    /// Create SU(3) matrix from 9 complex elements (row-major order).
    /// </summary>
    public SU3Matrix(Complex[] elements)
    {
        if (elements.Length != 9)
            throw new ArgumentException("SU(3) matrix requires 9 elements", nameof(elements));
        _elements = (Complex[])elements.Clone();
    }

    /// <summary>
    /// Create SU(3) matrix with explicit elements.
    /// </summary>
    public SU3Matrix(
        Complex m00, Complex m01, Complex m02,
        Complex m10, Complex m11, Complex m12,
        Complex m20, Complex m21, Complex m22)
    {
        _elements =
        [
            m00, m01, m02,
            m10, m11, m12,
            m20, m21, m22
        ];
    }

    /// <summary>
    /// Get flat array copy of matrix elements.
    /// </summary>
    public Complex[] ToFlatArray()
    {
        return (Complex[])(_elements ?? Identity._elements).Clone();
    }

    // ================================================================
    // GELL-MANN MATRICES (?_1 through ?_8)
    // Generators of su(3) Lie algebra
    // ================================================================

    /// <summary>
    /// Gell-Mann matrix ?_1 (off-diagonal real).
    /// ?_1 = [[0, 1, 0], [1, 0, 0], [0, 0, 0]]
    /// </summary>
    public static readonly Complex[,] Lambda1 =
    {
        { 0, 1, 0 },
        { 1, 0, 0 },
        { 0, 0, 0 }
    };

    /// <summary>
    /// Gell-Mann matrix ?_2 (off-diagonal imaginary).
    /// ?_2 = [[0, -i, 0], [i, 0, 0], [0, 0, 0]]
    /// </summary>
    public static readonly Complex[,] Lambda2 =
    {
        { 0, -Complex.ImaginaryOne, 0 },
        { Complex.ImaginaryOne, 0, 0 },
        { 0, 0, 0 }
    };

    /// <summary>
    /// Gell-Mann matrix ?_3 (diagonal, Cartan).
    /// ?_3 = [[1, 0, 0], [0, -1, 0], [0, 0, 0]]
    /// </summary>
    public static readonly Complex[,] Lambda3 =
    {
        { 1, 0, 0 },
        { 0, -1, 0 },
        { 0, 0, 0 }
    };

    /// <summary>
    /// Gell-Mann matrix ?_4 (1-3 coupling real).
    /// ?_4 = [[0, 0, 1], [0, 0, 0], [1, 0, 0]]
    /// </summary>
    public static readonly Complex[,] Lambda4 =
    {
        { 0, 0, 1 },
        { 0, 0, 0 },
        { 1, 0, 0 }
    };

    /// <summary>
    /// Gell-Mann matrix ?_5 (1-3 coupling imaginary).
    /// ?_5 = [[0, 0, -i], [0, 0, 0], [i, 0, 0]]
    /// </summary>
    public static readonly Complex[,] Lambda5 =
    {
        { 0, 0, -Complex.ImaginaryOne },
        { 0, 0, 0 },
        { Complex.ImaginaryOne, 0, 0 }
    };

    /// <summary>
    /// Gell-Mann matrix ?_6 (2-3 coupling real).
    /// ?_6 = [[0, 0, 0], [0, 0, 1], [0, 1, 0]]
    /// </summary>
    public static readonly Complex[,] Lambda6 =
    {
        { 0, 0, 0 },
        { 0, 0, 1 },
        { 0, 1, 0 }
    };

    /// <summary>
    /// Gell-Mann matrix ?_7 (2-3 coupling imaginary).
    /// ?_7 = [[0, 0, 0], [0, 0, -i], [0, i, 0]]
    /// </summary>
    public static readonly Complex[,] Lambda7 =
    {
        { 0, 0, 0 },
        { 0, 0, -Complex.ImaginaryOne },
        { 0, Complex.ImaginaryOne, 0 }
    };

    /// <summary>
    /// Gell-Mann matrix ?_8 (diagonal, Cartan).
    /// ?_8 = (1/?3) [[1, 0, 0], [0, 1, 0], [0, 0, -2]]
    /// </summary>
    public static readonly Complex[,] Lambda8;

    static SU3Matrix()
    {
        double inv_sqrt3 = 1.0 / Math.Sqrt(3.0);
        Lambda8 = new Complex[,]
        {
            { inv_sqrt3, 0, 0 },
            { 0, inv_sqrt3, 0 },
            { 0, 0, -2.0 * inv_sqrt3 }
        };
    }

    /// <summary>
    /// Get Gell-Mann matrix by index (1-8).
    /// </summary>
    public static Complex[,] GetGellMann(int index)
    {
        return index switch
        {
            1 => Lambda1,
            2 => Lambda2,
            3 => Lambda3,
            4 => Lambda4,
            5 => Lambda5,
            6 => Lambda6,
            7 => Lambda7,
            8 => Lambda8,
            _ => throw new ArgumentOutOfRangeException(nameof(index), "Index must be 1-8")
        };
    }

    // ================================================================
    // SU(3) STRUCTURE CONSTANTS f_{abc}
    // [?_a, ?_b] = 2i f_{abc} ?_c
    // ================================================================

    /// <summary>
    /// SU(3) structure constants f_{abc}.
    /// Non-zero values (antisymmetric):
    /// f_123 = 1
    /// f_147 = f_165 = f_246 = f_257 = f_345 = f_376 = 1/2
    /// f_458 = f_678 = ?3/2
    /// </summary>
    private static readonly double[,,] _structureConstants = InitStructureConstants();

    private static double[,,] InitStructureConstants()
    {
        var f = new double[8, 8, 8];

        // f_123 = 1 (and antisymmetric permutations)
        SetAntiSymmetric(f, 0, 1, 2, 1.0);

        // f_147 = 1/2
        SetAntiSymmetric(f, 0, 3, 6, 0.5);

        // f_156 = -1/2 (equivalent to f_165 = 1/2)
        SetAntiSymmetric(f, 0, 4, 5, -0.5);

        // f_246 = 1/2
        SetAntiSymmetric(f, 1, 3, 5, 0.5);

        // f_257 = 1/2
        SetAntiSymmetric(f, 1, 4, 6, 0.5);

        // f_345 = 1/2
        SetAntiSymmetric(f, 2, 3, 4, 0.5);

        // f_367 = -1/2 (equivalent to f_376 = 1/2)
        SetAntiSymmetric(f, 2, 5, 6, -0.5);

        // f_458 = ?3/2
        double sqrt3_2 = Math.Sqrt(3.0) / 2.0;
        SetAntiSymmetric(f, 3, 4, 7, sqrt3_2);

        // f_678 = ?3/2
        SetAntiSymmetric(f, 5, 6, 7, sqrt3_2);

        return f;
    }

    private static void SetAntiSymmetric(double[,,] f, int a, int b, int c, double value)
    {
        // All antisymmetric permutations
        f[a, b, c] = value;
        f[b, c, a] = value;
        f[c, a, b] = value;
        f[b, a, c] = -value;
        f[a, c, b] = -value;
        f[c, b, a] = -value;
    }

    /// <summary>
    /// Get structure constant f_{abc} (0-indexed: a,b,c ? [0,7]).
    /// </summary>
    public static double GetStructureConstant(int a, int b, int c)
    {
        if (a < 0 || a > 7 || b < 0 || b > 7 || c < 0 || c > 7)
            return 0.0;
        return _structureConstants[a, b, c];
    }

    // ================================================================
    // EXPONENTIAL MAP: su(3) ? SU(3)
    // U = exp(i ?_a ?_a/2) where ?_a are 8 real parameters
    // ================================================================

    /// <summary>
    /// Create SU(3) matrix from Lie algebra coefficients via exponential map.
    /// 
    /// CHECKLIST ITEM 1: Exponential mapping from generator coefficients to SU(3).
    /// 
    /// U = exp(i ?_a ?_a/2)
    /// 
    /// Uses Pad? approximation for numerical stability.
    /// </summary>
    /// <param name="theta">8 real coefficients for Gell-Mann generators</param>
    public static SU3Matrix Exp(double[] theta)
    {
        if (theta.Length != 8)
            throw new ArgumentException("Requires 8 Lie algebra coefficients", nameof(theta));

        // Construct X = i ?_a ?_a/2 (anti-Hermitian matrix)
        var X = new Complex[9];

        for (int a = 0; a < 8; a++)
        {
            if (Math.Abs(theta[a]) < 1e-15) continue;

            var lambda = GetGellMann(a + 1);
            Complex coeff = Complex.ImaginaryOne * theta[a] * 0.5;

            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    X[r * 3 + c] += coeff * lambda[r, c];
                }
            }
        }

        // Compute exp(X) using Taylor series with scaling
        return MatrixExp(X);
    }

    /// <summary>
    /// Create SU(3) matrix from single generator rotation.
    /// U = exp(i ? ?_a/2)
    /// </summary>
    /// <param name="generatorIndex">Generator index (1-8)</param>
    /// <param name="angle">Rotation angle ?</param>
    public static SU3Matrix FromGenerator(int generatorIndex, double angle)
    {
        var theta = new double[8];
        theta[generatorIndex - 1] = angle;
        return Exp(theta);
    }

    /// <summary>
    /// Compute matrix exponential exp(X) using scaling and squaring.
    /// </summary>
    private static SU3Matrix MatrixExp(Complex[] X)
    {
        // Scale matrix to have small norm
        double norm = MatrixNorm(X);
        int scalings = 0;
        while (norm > 0.5)
        {
            for (int i = 0; i < 9; i++) X[i] *= 0.5;
            norm *= 0.5;
            scalings++;
        }

        // Taylor series: exp(X) ? I + X + X?/2! + X?/3! + ...
        var result = new Complex[9];
        var term = new Complex[9];
        var Xpower = new Complex[9];

        // Identity
        result[0] = result[4] = result[8] = Complex.One;
        term[0] = term[4] = term[8] = Complex.One;
        Array.Copy(X, Xpower, 9);

        for (int n = 1; n <= 15; n++)
        {
            // term = X^n / n!
            term = MultiplyMatrices(term, X);
            for (int i = 0; i < 9; i++)
            {
                result[i] += term[i] / Factorial(n);
            }

            // Check convergence
            double termNorm = MatrixNorm(term);
            if (termNorm < 1e-15) break;
        }

        // Square back: exp(X) = (exp(X/2^s))^(2^s)
        for (int s = 0; s < scalings; s++)
        {
            result = MultiplyMatrices(result, result);
        }

        // Project onto SU(3) to clean up numerical errors
        return new SU3Matrix(result).ProjectToSU3();
    }

    private static double MatrixNorm(Complex[] m)
    {
        double sum = 0;
        for (int i = 0; i < m.Length; i++)
        {
            sum += m[i].Magnitude * m[i].Magnitude;
        }
        return Math.Sqrt(sum);
    }

    private static double Factorial(int n)
    {
        double result = 1.0;
        for (int i = 2; i <= n; i++) result *= i;
        return result;
    }

    // ================================================================
    // GROUP OPERATIONS
    // ================================================================

    /// <summary>
    /// Matrix multiplication: C = A * B
    /// </summary>
    public SU3Matrix Multiply(SU3Matrix other)
    {
        var result = new Complex[9];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < 3; k++)
                {
                    sum += this[r, k] * other[k, c];
                }
                result[r * 3 + c] = sum;
            }
        }
        return new SU3Matrix(result);
    }

    /// <summary>
    /// Hermitian conjugate (inverse for unitary matrix): U† = U??
    /// </summary>
    public SU3Matrix Dagger()
    {
        var result = new Complex[9];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                result[r * 3 + c] = Complex.Conjugate(this[c, r]);
            }
        }
        return new SU3Matrix(result);
    }

    /// <summary>
    /// Compute trace Tr(U).
    /// </summary>
    public Complex Trace()
    {
        return this[0, 0] + this[1, 1] + this[2, 2];
    }

    /// <summary>
    /// Compute real part of trace Re(Tr(U)).
    /// For SU(3), Tr(I) = 3. Values near 3 indicate identity (no flux).
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 2: Used for gauge flux magnitude calculation.
    /// |flux| = (3 - Re(Tr(U))) / 6, normalized to [0, 1].
    /// </summary>
    public double TraceReal()
    {
        return this[0, 0].Real + this[1, 1].Real + this[2, 2].Real;
    }

    /// <summary>
    /// Compute determinant det(U).
    /// For SU(3), det(U) = 1.
    /// </summary>
    public Complex Determinant()
    {
        Complex a = this[0, 0], b = this[0, 1], c = this[0, 2];
        Complex d = this[1, 0], e = this[1, 1], f = this[1, 2];
        Complex g = this[2, 0], h = this[2, 1], i = this[2, 2];

        return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    }

    /// <summary>
    /// Apply matrix to 3-component color vector (quark triplet).
    /// </summary>
    public Complex[] Apply(Complex[] vec)
    {
        if (vec.Length != 3)
            throw new ArgumentException("Color vector must have 3 components", nameof(vec));

        var result = new Complex[3];
        for (int r = 0; r < 3; r++)
        {
            result[r] = this[r, 0] * vec[0] + this[r, 1] * vec[1] + this[r, 2] * vec[2];
        }
        return result;
    }

    /// <summary>
    /// Project matrix onto SU(3) manifold.
    /// Uses polar decomposition followed by det=1 normalization.
    /// </summary>
    public SU3Matrix ProjectToSU3()
    {
        _elements ??= Identity._elements;

        // Polar decomposition via Newton-Schulz iteration
        var U = (Complex[])_elements.Clone();

        for (int iter = 0; iter < 10; iter++)
        {
            // U_new = (3*U - U*U†*U) / 2
            var Udag = Dagger(U);
            var UdagU = MultiplyMatrices(Udag, U);
            var UUdagU = MultiplyMatrices(U, UdagU);

            double maxChange = 0;
            for (int i = 0; i < 9; i++)
            {
                var Unew = (3.0 * U[i] - UUdagU[i]) * 0.5;
                maxChange = Math.Max(maxChange, (Unew - U[i]).Magnitude);
                U[i] = Unew;
            }

            if (maxChange < 1e-12) break;
        }

        // Enforce det = +1
        Complex det = ComputeDet(U);
        if (det.Magnitude > 1e-12)
        {
            // Multiply last row by phase to make det real positive
            Complex phase = Complex.Pow(det / det.Magnitude, 1.0 / 3.0);
            for (int c = 0; c < 3; c++)
            {
                U[2 * 3 + c] /= phase;
            }
        }

        return new SU3Matrix(U);
    }

    private static Complex[] Dagger(Complex[] m)
    {
        var result = new Complex[9];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                result[r * 3 + c] = Complex.Conjugate(m[c * 3 + r]);
            }
        }
        return result;
    }

    private static Complex[] MultiplyMatrices(Complex[] A, Complex[] B)
    {
        var C = new Complex[9];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < 3; k++)
                {
                    sum += A[r * 3 + k] * B[k * 3 + c];
                }
                C[r * 3 + c] = sum;
            }
        }
        return C;
    }

    private static Complex ComputeDet(Complex[] m)
    {
        Complex a = m[0], b = m[1], c = m[2];
        Complex d = m[3], e = m[4], f = m[5];
        Complex g = m[6], h = m[7], i = m[8];
        return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    }

    /// <summary>
    /// Create random SU(3) element near identity for Metropolis updates.
    /// </summary>
    public static SU3Matrix RandomNearIdentity(Random rng, double scale = 0.1)
    {
        var theta = new double[8];
        for (int a = 0; a < 8; a++)
        {
            theta[a] = (rng.NextDouble() - 0.5) * 2.0 * scale;
        }
        return Exp(theta);
    }

    /// <summary>
    /// Distance from identity: ||U - I||_F (Frobenius norm).
    /// </summary>
    public double DistanceFromIdentity()
    {
        _elements ??= Identity._elements;

        double sum = 0;
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                Complex diff = this[r, c] - ((r == c) ? Complex.One : Complex.Zero);
                sum += diff.Magnitude * diff.Magnitude;
            }
        }
        return Math.Sqrt(sum);
    }

    public override string ToString()
    {
        _elements ??= Identity._elements;
        return $"SU3[Tr={Trace():F3}, det={Determinant():F3}]";
    }

    // ================================================================
    // OPERATORS
    // ================================================================

    public static SU3Matrix operator *(SU3Matrix a, SU3Matrix b) => a.Multiply(b);
}
