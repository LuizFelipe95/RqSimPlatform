using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace RQSimulation.Core.Numerics;

/// <summary>
/// SIMD vectorization utilities for physics module optimization (Item 19/4.3).
/// Provides high-performance vectorized operations for common physics calculations.
///
/// USAGE GUIDELINES:
/// ================
/// 1. Use these utilities in hot-path inner loops of physics modules
/// 2. Prefer Vector&lt;T&gt; for cross-platform SIMD (works on ARM, x64, etc.)
/// 3. Use AVX2/SSE intrinsics only when Vector&lt;T&gt; doesn't provide needed operations
/// 4. Always provide scalar fallback for edge cases (array length % Vector.Count != 0)
/// 5. Test performance gains before replacing scalar code - not all operations benefit from SIMD
///
/// PERFORMANCE NOTES:
/// ==================
/// - Best for arrays with length >= 256 elements (smaller arrays may be slower due to overhead)
/// - Ensure data is aligned when possible (use stackalloc or ArrayPool)
/// - Avoid branching inside SIMD loops - use masked operations instead
/// </summary>
public static class SimdUtilities
{
    /// <summary>
    /// SIMD-optimized addition: dst[i] = src1[i] + src2[i]
    /// </summary>
    /// <param name="src1">First source array</param>
    /// <param name="src2">Second source array</param>
    /// <param name="dst">Destination array (can be same as src1 or src2 for in-place)</param>
    /// <param name="length">Number of elements to process</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void VectorAdd(ReadOnlySpan<double> src1, ReadOnlySpan<double> src2, Span<double> dst, int length)
    {
        if (length > src1.Length || length > src2.Length || length > dst.Length)
            throw new ArgumentException("Length exceeds span bounds");

        int i = 0;
        int vectorSize = Vector<double>.Count;
        int vectorLimit = length - vectorSize + 1;

        // Vectorized loop - process multiple elements per iteration
        for (; i < vectorLimit; i += vectorSize)
        {
            var v1 = new Vector<double>(src1.Slice(i, vectorSize));
            var v2 = new Vector<double>(src2.Slice(i, vectorSize));
            var result = v1 + v2;
            result.CopyTo(dst.Slice(i, vectorSize));
        }

        // Scalar tail - handle remaining elements
        for (; i < length; i++)
        {
            dst[i] = src1[i] + src2[i];
        }
    }

    /// <summary>
    /// SIMD-optimized scalar multiplication: dst[i] = src[i] * scalar
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void VectorScale(ReadOnlySpan<double> src, double scalar, Span<double> dst, int length)
    {
        if (length > src.Length || length > dst.Length)
            throw new ArgumentException("Length exceeds span bounds");

        int i = 0;
        int vectorSize = Vector<double>.Count;
        int vectorLimit = length - vectorSize + 1;
        var scalarVec = new Vector<double>(scalar);

        // Vectorized loop
        for (; i < vectorLimit; i += vectorSize)
        {
            var v = new Vector<double>(src.Slice(i, vectorSize));
            var result = v * scalarVec;
            result.CopyTo(dst.Slice(i, vectorSize));
        }

        // Scalar tail
        for (; i < length; i++)
        {
            dst[i] = src[i] * scalar;
        }
    }

    /// <summary>
    /// SIMD-optimized multiply-add: dst[i] = src1[i] * src2[i] + src3[i]
    /// Useful for weight updates: newWeight = oldWeight * decay + delta
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void VectorMultiplyAdd(ReadOnlySpan<double> src1, ReadOnlySpan<double> src2, ReadOnlySpan<double> src3, Span<double> dst, int length)
    {
        if (length > src1.Length || length > src2.Length || length > src3.Length || length > dst.Length)
            throw new ArgumentException("Length exceeds span bounds");

        int i = 0;
        int vectorSize = Vector<double>.Count;
        int vectorLimit = length - vectorSize + 1;

        // Vectorized loop
        for (; i < vectorLimit; i += vectorSize)
        {
            var v1 = new Vector<double>(src1.Slice(i, vectorSize));
            var v2 = new Vector<double>(src2.Slice(i, vectorSize));
            var v3 = new Vector<double>(src3.Slice(i, vectorSize));
            var result = v1 * v2 + v3;
            result.CopyTo(dst.Slice(i, vectorSize));
        }

        // Scalar tail
        for (; i < length; i++)
        {
            dst[i] = src1[i] * src2[i] + src3[i];
        }
    }

    /// <summary>
    /// SIMD-optimized dot product: sum(src1[i] * src2[i])
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double VectorDotProduct(ReadOnlySpan<double> src1, ReadOnlySpan<double> src2, int length)
    {
        if (length > src1.Length || length > src2.Length)
            throw new ArgumentException("Length exceeds span bounds");

        int i = 0;
        int vectorSize = Vector<double>.Count;
        int vectorLimit = length - vectorSize + 1;
        var sumVec = Vector<double>.Zero;

        // Vectorized loop - accumulate partial sums
        for (; i < vectorLimit; i += vectorSize)
        {
            var v1 = new Vector<double>(src1.Slice(i, vectorSize));
            var v2 = new Vector<double>(src2.Slice(i, vectorSize));
            sumVec += v1 * v2;
        }

        // Horizontal sum of vector components
        double sum = 0.0;
        for (int j = 0; j < vectorSize; j++)
        {
            sum += sumVec[j];
        }

        // Scalar tail
        for (; i < length; i++)
        {
            sum += src1[i] * src2[i];
        }

        return sum;
    }

    /// <summary>
    /// SIMD-optimized phase normalization: phase = (phase + Math.PI) % (2 * Math.PI) - Math.PI
    /// Wraps phases to [-π, π] range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void VectorNormalizePhases(Span<double> phases, int length)
    {
        if (length > phases.Length)
            throw new ArgumentException("Length exceeds span bounds");

        const double TwoPi = 2.0 * Math.PI;
        const double Pi = Math.PI;

        int i = 0;
        int vectorSize = Vector<double>.Count;
        int vectorLimit = length - vectorSize + 1;
        var twoPiVec = new Vector<double>(TwoPi);
        var piVec = new Vector<double>(Pi);

        // Vectorized loop
        for (; i < vectorLimit; i += vectorSize)
        {
            var v = new Vector<double>(phases.Slice(i, vectorSize));
            // Shift to [0, 2π]
            v += piVec;
            // Modulo operation (approximation for SIMD)
            // For exact modulo, use scalar loop
            v -= Vector.ConditionalSelect(
                Vector.GreaterThan(v, twoPiVec),
                twoPiVec,
                Vector<double>.Zero);
            // Shift back to [-π, π]
            v -= piVec;
            v.CopyTo(phases.Slice(i, vectorSize));
        }

        // Scalar tail with exact modulo
        for (; i < length; i++)
        {
            double p = phases[i];
            p = (p + Pi) % TwoPi;
            if (p < 0) p += TwoPi;
            phases[i] = p - Pi;
        }
    }

    /// <summary>
    /// SIMD-optimized sum: sum(src[i])
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double VectorSum(ReadOnlySpan<double> src, int length)
    {
        if (length > src.Length)
            throw new ArgumentException("Length exceeds span bounds");

        int i = 0;
        int vectorSize = Vector<double>.Count;
        int vectorLimit = length - vectorSize + 1;
        var sumVec = Vector<double>.Zero;

        // Vectorized loop
        for (; i < vectorLimit; i += vectorSize)
        {
            var v = new Vector<double>(src.Slice(i, vectorSize));
            sumVec += v;
        }

        // Horizontal sum
        double sum = 0.0;
        for (int j = 0; j < vectorSize; j++)
        {
            sum += sumVec[j];
        }

        // Scalar tail
        for (; i < length; i++)
        {
            sum += src[i];
        }

        return sum;
    }

    /// <summary>
    /// SIMD-optimized absolute value: dst[i] = |src[i]|
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void VectorAbs(ReadOnlySpan<double> src, Span<double> dst, int length)
    {
        if (length > src.Length || length > dst.Length)
            throw new ArgumentException("Length exceeds span bounds");

        int i = 0;
        int vectorSize = Vector<double>.Count;
        int vectorLimit = length - vectorSize + 1;

        // Vectorized loop
        for (; i < vectorLimit; i += vectorSize)
        {
            var v = new Vector<double>(src.Slice(i, vectorSize));
            var result = Vector.Abs(v);
            result.CopyTo(dst.Slice(i, vectorSize));
        }

        // Scalar tail
        for (; i < length; i++)
        {
            dst[i] = Math.Abs(src[i]);
        }
    }

    /// <summary>
    /// SIMD-optimized clamping: dst[i] = clamp(src[i], min, max)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void VectorClamp(ReadOnlySpan<double> src, double min, double max, Span<double> dst, int length)
    {
        if (length > src.Length || length > dst.Length)
            throw new ArgumentException("Length exceeds span bounds");

        int i = 0;
        int vectorSize = Vector<double>.Count;
        int vectorLimit = length - vectorSize + 1;
        var minVec = new Vector<double>(min);
        var maxVec = new Vector<double>(max);

        // Vectorized loop
        for (; i < vectorLimit; i += vectorSize)
        {
            var v = new Vector<double>(src.Slice(i, vectorSize));
            var result = Vector.Min(Vector.Max(v, minVec), maxVec);
            result.CopyTo(dst.Slice(i, vectorSize));
        }

        // Scalar tail
        for (; i < length; i++)
        {
            dst[i] = Math.Clamp(src[i], min, max);
        }
    }

    /// <summary>
    /// Check if AVX2 is supported on this CPU.
    /// Use this to conditionally enable AVX2-specific optimizations.
    /// </summary>
    public static bool IsAvx2Supported => Avx2.IsSupported;

    /// <summary>
    /// Check if SSE4.1 is supported on this CPU.
    /// </summary>
    public static bool IsSse41Supported => Sse41.IsSupported;

    /// <summary>
    /// Get the number of double values that fit in a SIMD vector on this CPU.
    /// Typically 2 for SSE, 4 for AVX2, 8 for AVX-512.
    /// </summary>
    public static int VectorDoubleCount => Vector<double>.Count;

    /// <summary>
    /// Get the number of float values that fit in a SIMD vector on this CPU.
    /// Typically 4 for SSE, 8 for AVX2, 16 for AVX-512.
    /// </summary>
    public static int VectorFloatCount => Vector<float>.Count;
}
