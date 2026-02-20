using System.Runtime.InteropServices;
using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Math;

/// <summary>
/// Complex number structure for double-precision GPU compute shaders.
/// Uses Double2 as underlying storage for HLSL compatibility.
/// 
/// Layout: (Real, Imaginary) stored as Double2(X=Real, Y=Imag)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Complex64
{
    public readonly double Real;
    public readonly double Imaginary;

    public Complex64(double real, double imaginary)
    {
        Real = real;
        Imaginary = imaginary;
    }

    /// <summary>
    /// Create from ComputeSharp Double2.
    /// </summary>
    public Complex64(Double2 d2)
    {
        Real = d2.X;
        Imaginary = d2.Y;
    }

    /// <summary>
    /// Convert to ComputeSharp Double2 for GPU buffers.
    /// </summary>
    public Double2 ToDouble2() => new(Real, Imaginary);

    public static Complex64 Zero => new(0, 0);
    public static Complex64 One => new(1, 0);
    public static Complex64 I => new(0, 1);

    public override string ToString() => $"({Real:G4}, {Imaginary:G4}i)";
}

/// <summary>
/// Static methods for complex arithmetic to be used inline in HLSL compute shaders.
/// 
/// USAGE PATTERN: These methods are called directly within shader Execute() methods.
/// ComputeSharp will inline these operations during HLSL translation.
/// 
/// Convention: Double2.X = Real, Double2.Y = Imaginary
/// </summary>
public static class HlslComplexMath
{
    /// <summary>
    /// Complex addition: a + b
    /// </summary>
    public static Double2 Add(Double2 a, Double2 b)
    {
        return new Double2(a.X + b.X, a.Y + b.Y);
    }

    /// <summary>
    /// Complex subtraction: a - b
    /// </summary>
    public static Double2 Sub(Double2 a, Double2 b)
    {
        return new Double2(a.X - b.X, a.Y - b.Y);
    }

    /// <summary>
    /// Complex multiplication: a * b
    /// (a.r + a.i*i)(b.r + b.i*i) = (a.r*b.r - a.i*b.i) + (a.r*b.i + a.i*b.r)*i
    /// </summary>
    public static Double2 Mul(Double2 a, Double2 b)
    {
        return new Double2(
            a.X * b.X - a.Y * b.Y,
            a.X * b.Y + a.Y * b.X
        );
    }

    /// <summary>
    /// Complex conjugate: conj(a) = a.r - a.i*i
    /// </summary>
    public static Double2 Conj(Double2 a)
    {
        return new Double2(a.X, -a.Y);
    }

    /// <summary>
    /// Complex magnitude squared: |a|? = a.r? + a.i?
    /// </summary>
    public static double MagSq(Double2 a)
    {
        return a.X * a.X + a.Y * a.Y;
    }

    /// <summary>
    /// Complex magnitude: |a| = sqrt(a.r? + a.i?)
    /// </summary>
    public static double Mag(Double2 a)
    {
        return System.Math.Sqrt(a.X * a.X + a.Y * a.Y);
    }

    /// <summary>
    /// Scale complex by real: alpha * a
    /// </summary>
    public static Double2 Scale(Double2 a, double alpha)
    {
        return new Double2(alpha * a.X, alpha * a.Y);
    }

    /// <summary>
    /// Scale complex by imaginary: i * alpha * a = (-alpha * a.i, alpha * a.r)
    /// </summary>
    public static Double2 ScaleByI(Double2 a, double alpha)
    {
        return new Double2(-alpha * a.Y, alpha * a.X);
    }

    /// <summary>
    /// Complex division: a / b
    /// </summary>
    public static Double2 Div(Double2 a, Double2 b)
    {
        double denom = b.X * b.X + b.Y * b.Y;
        return new Double2(
            (a.X * b.X + a.Y * b.Y) / denom,
            (a.Y * b.X - a.X * b.Y) / denom
        );
    }

    /// <summary>
    /// Complex fused multiply-add: a * b + c
    /// </summary>
    public static Double2 Fma(Double2 a, Double2 b, Double2 c)
    {
        return new Double2(
            a.X * b.X - a.Y * b.Y + c.X,
            a.X * b.Y + a.Y * b.X + c.Y
        );
    }

    /// <summary>
    /// Linear combination: alpha * a + beta * b
    /// </summary>
    public static Double2 LinearCombination(double alpha, Double2 a, double beta, Double2 b)
    {
        return new Double2(
            alpha * a.X + beta * b.X,
            alpha * a.Y + beta * b.Y
        );
    }

    /// <summary>
    /// Complex linear combination: alpha * a + b where alpha is complex
    /// </summary>
    public static Double2 ComplexAxpy(Double2 alpha, Double2 a, Double2 b)
    {
        // alpha * a
        double realPart = alpha.X * a.X - alpha.Y * a.Y;
        double imagPart = alpha.X * a.Y + alpha.Y * a.X;
        
        return new Double2(realPart + b.X, imagPart + b.Y);
    }

    /// <summary>
    /// Hermitian inner product: conj(a) * b
    /// Used for complex dot products.
    /// </summary>
    public static Double2 HermitianProduct(Double2 a, Double2 b)
    {
        return new Double2(
            a.X * b.X + a.Y * b.Y,  // Real: a.r*b.r + a.i*b.i (conj flips sign)
            a.X * b.Y - a.Y * b.X   // Imag: a.r*b.i - a.i*b.r
        );
    }

    /// <summary>
    /// Create complex from polar: r * exp(i * theta)
    /// Note: Use System.Math for CPU-side, this is for pre-computation.
    /// </summary>
    public static Double2 FromPolar(double r, double theta)
    {
        return new Double2(r * System.Math.Cos(theta), r * System.Math.Sin(theta));
    }

    /// <summary>
    /// Complex exponential: exp(i * theta)
    /// </summary>
    public static Double2 ExpI(double theta)
    {
        return new Double2(System.Math.Cos(theta), System.Math.Sin(theta));
    }

    /// <summary>
    /// Check if complex is approximately zero.
    /// </summary>
    public static bool IsNearZero(Double2 a, double epsilon)
    {
        return MagSq(a) < epsilon * epsilon;
    }
    
    /// <summary>
    /// Check if complex number contains NaN or Inf.
    /// Returns true if either component is NaN or Inf.
    /// </summary>
    public static bool IsNaNOrInf(Double2 a)
    {
        return double.IsNaN(a.X) || double.IsInfinity(a.X) ||
               double.IsNaN(a.Y) || double.IsInfinity(a.Y);
    }
    
    /// <summary>
    /// Safe complex multiplication with overflow protection.
    /// If result would overflow, returns zero.
    /// </summary>
    public static Double2 SafeMul(Double2 a, Double2 b)
    {
        double realPart = a.X * b.X - a.Y * b.Y;
        double imagPart = a.X * b.Y + a.Y * b.X;
        
        if (double.IsNaN(realPart) || double.IsInfinity(realPart) ||
            double.IsNaN(imagPart) || double.IsInfinity(imagPart))
        {
            return new Double2(0.0, 0.0);
        }
        
        return new Double2(realPart, imagPart);
    }
    
    /// <summary>
    /// Safe complex division with zero and overflow protection.
    /// If denominator is near zero or result would overflow, returns zero.
    /// </summary>
    public static Double2 SafeDiv(Double2 a, Double2 b, double epsilon = 1e-30)
    {
        double denom = b.X * b.X + b.Y * b.Y;
        
        if (denom < epsilon)
        {
            return new Double2(0.0, 0.0);
        }
        
        double realPart = (a.X * b.X + a.Y * b.Y) / denom;
        double imagPart = (a.Y * b.X - a.X * b.Y) / denom;
        
        if (double.IsNaN(realPart) || double.IsInfinity(realPart) ||
            double.IsNaN(imagPart) || double.IsInfinity(imagPart))
        {
            return new Double2(0.0, 0.0);
        }
        
        return new Double2(realPart, imagPart);
    }
    
    /// <summary>
    /// Safe complex exponential: exp(i * theta) with phase normalization.
    /// Normalizes theta to [-?, ?] range to prevent overflow.
    /// </summary>
    public static Double2 SafeExpI(double theta)
    {
        // Normalize theta to [-?, ?] range
        const double twoPi = 2.0 * System.Math.PI;
        theta = theta - twoPi * System.Math.Floor(theta / twoPi + 0.5);
        
        double cosT = System.Math.Cos(theta);
        double sinT = System.Math.Sin(theta);
        
        if (double.IsNaN(cosT) || double.IsNaN(sinT))
        {
            return new Double2(1.0, 0.0); // Default to real 1
        }
        
        return new Double2(cosT, sinT);
    }
    
    /// <summary>
    /// Normalize complex number to unit magnitude.
    /// Returns zero if magnitude is below epsilon.
    /// </summary>
    public static Double2 Normalize(Double2 a, double epsilon = 1e-15)
    {
        double magSq = a.X * a.X + a.Y * a.Y;
        
        if (magSq < epsilon * epsilon)
        {
            return new Double2(0.0, 0.0);
        }
        
        double invMag = 1.0 / System.Math.Sqrt(magSq);
        
        if (double.IsNaN(invMag) || double.IsInfinity(invMag))
        {
            return new Double2(0.0, 0.0);
        }
        
        return new Double2(a.X * invMag, a.Y * invMag);
    }
    
    /// <summary>
    /// Sanitize complex number: replace NaN/Inf with zero.
    /// </summary>
    public static Double2 Sanitize(Double2 a)
    {
        double realPart = a.X;
        double imagPart = a.Y;
        
        if (double.IsNaN(realPart) || double.IsInfinity(realPart))
        {
            realPart = 0.0;
        }
        
        if (double.IsNaN(imagPart) || double.IsInfinity(imagPart))
        {
            imagPart = 0.0;
        }
        
        return new Double2(realPart, imagPart);
    }
}

/// <summary>
/// Float precision complex math for GPUs without double support.
/// Fallback when IsDoublePrecisionSupportAvailable() returns false.
/// </summary>
public static class HlslComplexMathFloat
{
    public static Float2 Add(Float2 a, Float2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Float2 Sub(Float2 a, Float2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Float2 Mul(Float2 a, Float2 b) => new(a.X * b.X - a.Y * b.Y, a.X * b.Y + a.Y * b.X);

    public static Float2 Conj(Float2 a) => new(a.X, -a.Y);

    public static float MagSq(Float2 a) => a.X * a.X + a.Y * a.Y;

    public static Float2 Scale(Float2 a, float alpha) => new(alpha * a.X, alpha * a.Y);

    public static Float2 ScaleByI(Float2 a, float alpha) => new(-alpha * a.Y, alpha * a.X);

    public static Float2 HermitianProduct(Float2 a, Float2 b) => new(a.X * b.X + a.Y * b.Y, a.X * b.Y - a.Y * b.X);
}
