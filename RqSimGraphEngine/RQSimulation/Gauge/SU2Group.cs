using System;
using System.Numerics;

namespace RQSimulation.Gauge;

/// <summary>
/// SU(2) gauge group implementation using quaternion representation.
///
/// CHECKLIST ITEM 44 (11.3): Gauge Group Support Extension
/// ==========================================================
/// Concrete implementation of IGaugeGroup for SU(2) weak interaction gauge group.
///
/// SU(2) is efficiently represented via unit quaternions, avoiding
/// numerical issues with direct 2×2 complex matrix operations.
///
/// PHYSICS CONTEXT:
/// ================
/// SU(2) is the gauge group for:
/// - Weak nuclear force (electroweak theory)
/// - Isospin symmetry in nuclear physics
/// - Spin rotations in quantum mechanics
///
/// The group has 3 generators (Pauli matrices σ_x, σ_y, σ_z).
/// </summary>
public sealed class SU2Group : IGaugeGroup<SU2Matrix>
{
    /// <inheritdoc/>
    public SU2Matrix Identity => SU2Matrix.Identity;

    /// <inheritdoc/>
    public int Dimension => 2;

    /// <inheritdoc/>
    public int GeneratorCount => 3;

    /// <inheritdoc/>
    public SU2Matrix Multiply(SU2Matrix a, SU2Matrix b)
    {
        return a.Multiply(b);
    }

    /// <inheritdoc/>
    public SU2Matrix Conjugate(SU2Matrix element)
    {
        return element.Conjugate();
    }

    /// <inheritdoc/>
    public Complex Trace(SU2Matrix element)
    {
        // For SU(2): Tr(U) = 2a where a is the quaternion real part
        return new Complex(element.Trace(), 0);
    }

    /// <inheritdoc/>
    public SU2Matrix Exponential(double[] algebraCoefficients)
    {
        if (algebraCoefficients.Length != 3)
            throw new ArgumentException(
                $"SU(2) requires 3 Lie algebra coefficients, got {algebraCoefficients.Length}",
                nameof(algebraCoefficients));

        return SU2Matrix.Exponential(algebraCoefficients);
    }

    /// <inheritdoc/>
    public SU2Matrix ComputePlaquette(SU2Matrix u1, SU2Matrix u2, SU2Matrix u3, SU2Matrix u4)
    {
        // P = U1 * U2 * U3† * U4†
        var u3Dag = u3.Conjugate();
        var u4Dag = u4.Conjugate();

        var temp1 = u1.Multiply(u2);
        var temp2 = u3Dag.Multiply(u4Dag);
        return temp1.Multiply(temp2);
    }

    /// <inheritdoc/>
    public double TraceDistance(SU2Matrix a, SU2Matrix b)
    {
        // d(U, V) = |1 - Re(Tr(U† * V)) / 2|
        var aDag = a.Conjugate();
        var product = aDag.Multiply(b);
        double traceReal = product.Trace(); // Already real for SU(2)

        double normalized = traceReal / 2.0; // N=2 for SU(2)
        return Math.Abs(1.0 - normalized);
    }

    /// <inheritdoc/>
    public SU2Matrix ProjectToGroup(SU2Matrix element)
    {
        // Quaternion representation is automatically projected
        // The Normalize() method ensures unit norm
        var result = new SU2Matrix(element.A, element.B, element.C, element.D);
        result.Normalize();
        return result;
    }

    /// <inheritdoc/>
    public SU2Matrix RandomNearIdentity(Random random, double scale)
    {
        return SU2Matrix.RandomNearIdentity(random, scale);
    }

    /// <inheritdoc/>
    public SU2Matrix GetGenerator(int index)
    {
        if (index < 0 || index >= 3)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"SU(2) has 3 generators (0-2), got {index}");

        // Return T_a = σ_a/2 as SU2Matrix
        // For efficiency, we return the exponential with angle = 0
        // which gives the generator direction
        // In practice, generators are accessed via exponential map
        return SU2Matrix.ExponentialGenerator(index + 1, 0.01);
    }

    /// <inheritdoc/>
    public double GetStructureConstant(int a, int b, int c)
    {
        if (a < 0 || a >= 3 || b < 0 || b >= 3 || c < 0 || c >= 3)
            return 0.0;

        // SU(2) structure constants are the Levi-Civita symbol ε_abc
        return PauliMatrices.GetStructureConstant(a, b, c);
    }

    /// <summary>
    /// Compute Wilson action for a single plaquette.
    ///
    /// S_W = β [1 - Re(Tr(P))/(2N)]
    ///
    /// where β = 2N/g² is the inverse coupling constant.
    /// </summary>
    /// <param name="plaquette">Plaquette P</param>
    /// <param name="beta">Inverse coupling β</param>
    /// <returns>Action S_W</returns>
    public double WilsonAction(SU2Matrix plaquette, double beta)
    {
        double traceReal = plaquette.Trace();
        return beta * (1.0 - traceReal / 2.0);
    }
}
