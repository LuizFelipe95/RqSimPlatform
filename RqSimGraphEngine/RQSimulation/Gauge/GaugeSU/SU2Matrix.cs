using System;
using System.Numerics;

namespace RQSimulation.Gauge;

/// <summary>
/// Quaternion-based representation of SU(2) group elements.
/// 
/// CHECKLIST ITEM 16: Encapsulation of SU(2) group operations
/// 
/// SU(2) matrices can be represented efficiently via unit quaternions:
///   q = a + bi + cj + dk where ||q|| = 1
///   
/// The quaternion (a, b, c, d) maps to the 2?2 unitary matrix:
///   U = [ a - id    -c - ib ]
///       [ c - ib     a + id ]
///       
/// This representation avoids numerical issues with direct matrix operations
/// and provides natural composition via quaternion multiplication.
/// 
/// Physics: SU(2) is the gauge group for weak interactions.
/// The Pauli matrices ?_i generate the su(2) Lie algebra.
/// </summary>
public struct SU2Matrix
{
    /// <summary>Scalar part (real component)</summary>
    public double A;
    
    /// <summary>?_x component (imaginary i)</summary>
    public double B;
    
    /// <summary>?_y component (imaginary j)</summary>
    public double C;
    
    /// <summary>?_z component (imaginary k)</summary>
    public double D;

    /// <summary>
    /// Create SU(2) element from quaternion components.
    /// Automatically normalizes to ensure ||q|| = 1.
    /// </summary>
    public SU2Matrix(double a, double b, double c, double d)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        Normalize();
    }

    /// <summary>
    /// Normalize quaternion to unit norm (ensure det(U) = +1).
    /// </summary>
    public void Normalize()
    {
        double norm = Math.Sqrt(A * A + B * B + C * C + D * D);
        
        if (norm < 1e-12)
        {
            // Degenerate: reset to identity
            A = 1;
            B = C = D = 0;
            return;
        }
        
        double invNorm = 1.0 / norm;
        A *= invNorm;
        B *= invNorm;
        C *= invNorm;
        D *= invNorm;
        
        // Fix sign convention: ensure a >= 0 for unique representation
        // (Quaternion q and -q represent the same rotation)
        if (A < 0)
        {
            A = -A;
            B = -B;
            C = -C;
            D = -D;
        }
    }

    /// <summary>
    /// Identity element of SU(2).
    /// </summary>
    public static SU2Matrix Identity => new(1, 0, 0, 0);

    /// <summary>
    /// Create SU(2) rotation from axis-angle representation.
    /// 
    /// U = exp(i ?/2 (x ?_x + y ?_y + z ?_z))
    /// 
    /// where (x, y, z) is a unit vector and ? is the rotation angle.
    /// </summary>
    /// <param name="x">X component of rotation axis (should be normalized)</param>
    /// <param name="y">Y component of rotation axis</param>
    /// <param name="z">Z component of rotation axis</param>
    /// <param name="angle">Rotation angle in radians</param>
    public static SU2Matrix FromAxisAngle(double x, double y, double z, double angle)
    {
        // Ensure axis is normalized
        double axisNorm = Math.Sqrt(x * x + y * y + z * z);
        if (axisNorm > 1e-12)
        {
            x /= axisNorm;
            y /= axisNorm;
            z /= axisNorm;
        }
        else
        {
            return Identity;
        }

        double halfAngle = angle * 0.5;
        double sinHalf = Math.Sin(halfAngle);
        double cosHalf = Math.Cos(halfAngle);

        return new SU2Matrix(cosHalf, sinHalf * x, sinHalf * y, sinHalf * z);
    }

    /// <summary>
    /// Create random SU(2) element near identity for Metropolis updates.
    /// </summary>
    /// <param name="random">Random number generator</param>
    /// <param name="scale">Scale of perturbation (0 = identity, 1 = random)</param>
    public static SU2Matrix RandomNearIdentity(Random random, double scale = 0.1)
    {
        // Generate small random rotation
        double x = (random.NextDouble() - 0.5) * 2.0;
        double y = (random.NextDouble() - 0.5) * 2.0;
        double z = (random.NextDouble() - 0.5) * 2.0;
        double angle = scale * (random.NextDouble() - 0.5) * Math.PI;

        return FromAxisAngle(x, y, z, angle);
    }

    /// <summary>
    /// Quaternion multiplication (group composition).
    /// 
    /// For SU(2): U1 * U2 corresponds to q1 ? q2.
    /// </summary>
    public SU2Matrix Multiply(SU2Matrix other)
    {
        // Hamilton product for quaternions
        return new SU2Matrix(
            A * other.A - B * other.B - C * other.C - D * other.D,
            A * other.B + B * other.A + C * other.D - D * other.C,
            A * other.C - B * other.D + C * other.A + D * other.B,
            A * other.D + B * other.C - C * other.B + D * other.A
        );
    }

    /// <summary>
    /// Quaternion conjugate (group inverse for unit quaternions).
    /// </summary>
    public SU2Matrix Conjugate()
    {
        return new SU2Matrix(A, -B, -C, -D);
    }

    /// <summary>
    /// Convert quaternion to 2?2 complex unitary matrix.
    /// 
    /// U = [ a - id    -c - ib ]
    ///     [ c - ib     a + id ]
    /// </summary>
    public Complex[,] ToComplexMatrix()
    {
        var M = new Complex[2, 2];
        
        M[0, 0] = new Complex(A, -D);   // a - id
        M[0, 1] = new Complex(-C, -B);  // -c - ib
        M[1, 0] = new Complex(C, -B);   // c - ib
        M[1, 1] = new Complex(A, D);    // a + id
        
        return M;
    }

    /// <summary>
    /// Convert quaternion to flat complex array (row-major, 4 elements).
    /// Compatible with existing gauge matrix storage.
    /// </summary>
    public Complex[] ToFlatArray()
    {
        var flat = new Complex[4];
        
        flat[0] = new Complex(A, -D);   // M[0,0] = a - id
        flat[1] = new Complex(-C, -B);  // M[0,1] = -c - ib
        flat[2] = new Complex(C, -B);   // M[1,0] = c - ib
        flat[3] = new Complex(A, D);    // M[1,1] = a + id
        
        return flat;
    }

    /// <summary>
    /// Create SU2Matrix from 2?2 complex matrix.
    /// Projects arbitrary 2?2 matrix onto SU(2) via quaternion extraction.
    /// </summary>
    public static SU2Matrix FromComplexMatrix(Complex[,] M)
    {
        // Extract quaternion components from matrix
        // U = [ a - id    -c - ib ]
        //     [ c - ib     a + id ]
        
        // a = Re(M[0,0] + M[1,1]) / 2
        // d = -Im(M[0,0] - M[1,1]) / 2
        // b = -Im(M[0,1] + M[1,0]) / 2
        // c = Re(M[1,0] - M[0,1]) / 2
        
        double a = (M[0, 0].Real + M[1, 1].Real) * 0.5;
        double d = -(M[0, 0].Imaginary - M[1, 1].Imaginary) * 0.5;
        double b = -(M[0, 1].Imaginary + M[1, 0].Imaginary) * 0.5;
        double c = (M[1, 0].Real - M[0, 1].Real) * 0.5;
        
        return new SU2Matrix(a, b, c, d);
    }

    /// <summary>
    /// Create SU2Matrix from flat complex array (4 elements, row-major).
    /// </summary>
    public static SU2Matrix FromFlatArray(Complex[] flat)
    {
        if (flat.Length < 4)
            return Identity;
            
        // Same extraction as FromComplexMatrix
        double a = (flat[0].Real + flat[3].Real) * 0.5;
        double d = -(flat[0].Imaginary - flat[3].Imaginary) * 0.5;
        double b = -(flat[1].Imaginary + flat[2].Imaginary) * 0.5;
        double c = (flat[2].Real - flat[1].Real) * 0.5;
        
        return new SU2Matrix(a, b, c, d);
    }

    /// <summary>
    /// Apply SU(2) transformation to a 2-component spinor (doublet).
    /// Returns transformed spinor: |?'? = U |??
    /// </summary>
    public (Complex up, Complex down) TransformDoublet(Complex up, Complex down)
    {
        var M = ToComplexMatrix();
        
        Complex newUp = M[0, 0] * up + M[0, 1] * down;
        Complex newDown = M[1, 0] * up + M[1, 1] * down;
        
        return (newUp, newDown);
    }

    /// <summary>
    /// Compute trace of the 2?2 matrix.
    /// Tr(U) = 2a for unit quaternion.
    /// </summary>
    public double Trace()
    {
        return 2.0 * A;
    }

    /// <summary>
    /// Distance from identity in SU(2).
    /// Uses geodesic distance: d(U, I) = arccos(a) where a = Re(Tr(U))/2.
    /// </summary>
    public double DistanceFromIdentity()
    {
        // Clamp to handle numerical errors
        double cosTheta = Math.Clamp(A, -1.0, 1.0);
        return Math.Acos(cosTheta);
    }

    /// <summary>
    /// Interpolate between this element and another (SLERP).
    /// Used for smooth gauge field updates.
    /// </summary>
    /// <param name="target">Target SU(2) element</param>
    /// <param name="t">Interpolation parameter [0, 1]</param>
    public SU2Matrix Slerp(SU2Matrix target, double t)
    {
        // Compute angle between quaternions
        double dot = A * target.A + B * target.B + C * target.C + D * target.D;
        
        // If dot < 0, negate one quaternion to take shorter path
        if (dot < 0)
        {
            target = new SU2Matrix(-target.A, -target.B, -target.C, -target.D);
            dot = -dot;
        }
        
        // If quaternions are very close, use linear interpolation
        if (dot > 0.9995)
        {
            return new SU2Matrix(
                A + t * (target.A - A),
                B + t * (target.B - B),
                C + t * (target.C - C),
                D + t * (target.D - D)
            );
        }
        
        // Spherical linear interpolation
        double theta = Math.Acos(dot);
        double sinTheta = Math.Sin(theta);
        double w1 = Math.Sin((1.0 - t) * theta) / sinTheta;
        double w2 = Math.Sin(t * theta) / sinTheta;
        
        return new SU2Matrix(
            w1 * A + w2 * target.A,
            w1 * B + w2 * target.B,
            w1 * C + w2 * target.C,
            w1 * D + w2 * target.D
        );
    }

    /// <summary>
    /// RQ-HYPOTHESIS CHECKLIST ITEM 3: Exponential Map for SU(2)
    /// ==========================================================
    /// Create SU(2) group element from Lie algebra coefficients.
    /// 
    /// U = exp(i ?_a ?_a/2) where ?_a are 3 real parameters
    /// 
    /// For SU(2), this has a closed form using axis-angle representation:
    ///   U = cos(|?|/2) + i sin(|?|/2) (?? · ?)
    /// 
    /// where |?| = ?(?_1? + ?_2? + ?_3?) and ?? = ?/|?|
    /// 
    /// This ensures gauge-covariant updates: U_new = exp(-iE*dt) * U_old
    /// instead of non-gauge-covariant: W_new = W_old + ?W
    /// </summary>
    /// <param name="theta">3 Lie algebra coefficients (?_1, ?_2, ?_3)</param>
    /// <returns>SU(2) group element</returns>
    public static SU2Matrix Exponential(double[] theta)
    {
        if (theta.Length < 3)
            return Identity;

        double x = theta[0];
        double y = theta[1];
        double z = theta[2];
        
        double normSq = x * x + y * y + z * z;
        
        if (normSq < 1e-15)
            return Identity;
            
        double norm = Math.Sqrt(normSq);
        return FromAxisAngle(x / norm, y / norm, z / norm, norm);
    }

    /// <summary>
    /// Exponential map with single coefficient for each generator.
    /// U = exp(i ? ?_a/2) for generator a ? {1, 2, 3}
    /// </summary>
    public static SU2Matrix ExponentialGenerator(int generatorIndex, double angle)
    {
        return generatorIndex switch
        {
            1 => FromAxisAngle(1, 0, 0, angle), // ?_x rotation
            2 => FromAxisAngle(0, 1, 0, angle), // ?_y rotation  
            3 => FromAxisAngle(0, 0, 1, angle), // ?_z rotation
            _ => Identity
        };
    }

    public override string ToString()
    {
        return $"SU2({A:F4}, {B:F4}, {C:F4}, {D:F4})";
    }
}

/// <summary>
/// Pauli matrices for SU(2) Lie algebra.
/// ?_i are the generators of su(2): [?_i, ?_j] = 2i ?_{ijk} ?_k
/// </summary>
public static class PauliMatrices
{
    /// <summary>
    /// Pauli ?_x matrix.
    /// ?_x = [0 1; 1 0]
    /// </summary>
    public static readonly Complex[,] SigmaX = 
    {
        { Complex.Zero, Complex.One },
        { Complex.One, Complex.Zero }
    };

    /// <summary>
    /// Pauli ?_y matrix.
    /// ?_y = [0 -i; i 0]
    /// </summary>
    public static readonly Complex[,] SigmaY = 
    {
        { Complex.Zero, -Complex.ImaginaryOne },
        { Complex.ImaginaryOne, Complex.Zero }
    };

    /// <summary>
    /// Pauli ?_z matrix.
    /// ?_z = [1 0; 0 -1]
    /// </summary>
    public static readonly Complex[,] SigmaZ = 
    {
        { Complex.One, Complex.Zero },
        { Complex.Zero, -Complex.One }
    };

    /// <summary>
    /// Get Pauli matrix by index (1, 2, or 3).
    /// </summary>
    public static Complex[,] Get(int index)
    {
        return index switch
        {
            1 => SigmaX,
            2 => SigmaY,
            3 => SigmaZ,
            _ => throw new ArgumentOutOfRangeException(nameof(index), "Pauli index must be 1, 2, or 3")
        };
    }

    /// <summary>
    /// SU(2) structure constants ?_{abc} (Levi-Civita tensor).
    /// [?_a/2, ?_b/2] = i ?_{abc} ?_c/2
    /// 
    /// CHECKLIST ITEM 2: Explicit SU(2) structure constants.
    /// This replaces the manual (a+1)%3, (a+2)%3 index cycling
    /// with proper antisymmetric tensor.
    /// </summary>
    public static readonly double[,,] StructureConstants = new double[3, 3, 3]
    {
        // ?_{0bc}
        { 
            { 0, 0, 0 },    // ?_{0,0,c}
            { 0, 0, 1 },    // ?_{0,1,c} = (0, 0, 1)
            { 0, -1, 0 }    // ?_{0,2,c} = (0, -1, 0)
        },
        // ?_{1bc}
        {
            { 0, 0, -1 },   // ?_{1,0,c} = (0, 0, -1)
            { 0, 0, 0 },    // ?_{1,1,c}
            { 1, 0, 0 }     // ?_{1,2,c} = (1, 0, 0)
        },
        // ?_{2bc}
        {
            { 0, 1, 0 },    // ?_{2,0,c} = (0, 1, 0)
            { -1, 0, 0 },   // ?_{2,1,c} = (-1, 0, 0)
            { 0, 0, 0 }     // ?_{2,2,c}
        }
    };

    /// <summary>
    /// Get structure constant f^{abc} for SU(2).
    /// Returns ?_{abc} (Levi-Civita symbol).
    /// </summary>
    public static double GetStructureConstant(int a, int b, int c)
    {
        if (a < 0 || a > 2 || b < 0 || b > 2 || c < 0 || c > 2)
            return 0.0;
        return StructureConstants[a, b, c];
    }
}
