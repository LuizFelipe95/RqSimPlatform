using System;
using System.Numerics;

namespace RQSimulation.Gauge;

/// <summary>
/// SU(3) gauge group implementation for quantum chromodynamics (QCD).
///
/// CHECKLIST ITEM 44 (11.3): Gauge Group Support Extension
/// ==========================================================
/// Concrete implementation of IGaugeGroup for SU(3) strong interaction gauge group.
///
/// SU(3) is represented as 3×3 complex unitary matrices with determinant +1.
/// The group has 8 generators (Gell-Mann matrices λ_1 through λ_8).
///
/// PHYSICS CONTEXT:
/// ================
/// SU(3) is the gauge group for:
/// - Quantum Chromodynamics (QCD) - strong nuclear force
/// - Color charge symmetry (red, green, blue quarks)
/// - Gluon fields (8 types corresponding to 8 generators)
///
/// LATTICE QCD:
/// ============
/// Link variables U_μ(x) ∈ SU(3) connect adjacent lattice sites.
/// Gluon field strength computed from plaquettes (minimal loops).
/// Confinement emerges from strong-coupling Wilson action.
/// </summary>
public sealed class SU3Group : IGaugeGroup<SU3Matrix>
{
    /// <inheritdoc/>
    public SU3Matrix Identity => SU3Matrix.Identity;

    /// <inheritdoc/>
    public int Dimension => 3;

    /// <inheritdoc/>
    public int GeneratorCount => 8;

    /// <inheritdoc/>
    public SU3Matrix Multiply(SU3Matrix a, SU3Matrix b)
    {
        return a.Multiply(b);
    }

    /// <inheritdoc/>
    public SU3Matrix Conjugate(SU3Matrix element)
    {
        return element.Dagger();
    }

    /// <inheritdoc/>
    public Complex Trace(SU3Matrix element)
    {
        return element.Trace();
    }

    /// <inheritdoc/>
    public SU3Matrix Exponential(double[] algebraCoefficients)
    {
        if (algebraCoefficients.Length != 8)
            throw new ArgumentException(
                $"SU(3) requires 8 Lie algebra coefficients, got {algebraCoefficients.Length}",
                nameof(algebraCoefficients));

        return SU3Matrix.Exp(algebraCoefficients);
    }

    /// <inheritdoc/>
    public SU3Matrix ComputePlaquette(SU3Matrix u1, SU3Matrix u2, SU3Matrix u3, SU3Matrix u4)
    {
        // P = U1 * U2 * U3† * U4†
        var u3Dag = u3.Dagger();
        var u4Dag = u4.Dagger();

        var temp1 = u1.Multiply(u2);
        var temp2 = u3Dag.Multiply(u4Dag);
        return temp1.Multiply(temp2);
    }

    /// <inheritdoc/>
    public double TraceDistance(SU3Matrix a, SU3Matrix b)
    {
        // d(U, V) = |1 - Re(Tr(U† * V)) / 3|
        var aDag = a.Dagger();
        var product = aDag.Multiply(b);
        double traceReal = product.TraceReal();

        double normalized = traceReal / 3.0; // N=3 for SU(3)
        return Math.Abs(1.0 - normalized);
    }

    /// <inheritdoc/>
    public SU3Matrix ProjectToGroup(SU3Matrix element)
    {
        return element.ProjectToSU3();
    }

    /// <inheritdoc/>
    public SU3Matrix RandomNearIdentity(Random random, double scale)
    {
        return SU3Matrix.RandomNearIdentity(random, scale);
    }

    /// <inheritdoc/>
    public SU3Matrix GetGenerator(int index)
    {
        if (index < 0 || index >= 8)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"SU(3) has 8 generators (0-7), got {index}");

        // Return small rotation along generator direction
        // For actual generator matrices, use SU3Matrix.GetGellMann(index + 1)
        return SU3Matrix.FromGenerator(index + 1, 0.01);
    }

    /// <inheritdoc/>
    public double GetStructureConstant(int a, int b, int c)
    {
        if (a < 0 || a >= 8 || b < 0 || b >= 8 || c < 0 || c >= 8)
            return 0.0;

        return SU3Matrix.GetStructureConstant(a, b, c);
    }

    /// <summary>
    /// Compute Wilson action for a single plaquette.
    ///
    /// S_W = β [1 - Re(Tr(P))/(2N)]
    ///
    /// where β = 2N/g² is the inverse coupling constant and N=3 for SU(3).
    /// </summary>
    /// <param name="plaquette">Plaquette P</param>
    /// <param name="beta">Inverse coupling β</param>
    /// <returns>Action S_W</returns>
    public double WilsonAction(SU3Matrix plaquette, double beta)
    {
        double traceReal = plaquette.TraceReal();
        return beta * (1.0 - traceReal / 6.0); // 2N = 6 for SU(3)
    }

    /// <summary>
    /// Compute average plaquette value across an ensemble.
    ///
    /// <![CDATA[⟨P⟩ = (1/V) Σ Re(Tr(P))/(2N)]]>
    ///
    /// where V is the number of plaquettes.
    ///
    /// PHYSICS INTERPRETATION:
    /// - ⟨P⟩ ≈ 1: weak coupling (perturbative regime)
    /// - ⟨P⟩ << 1: strong coupling (confinement regime)
    /// - Transition occurs around β_c ≈ 5.7 for SU(3) pure gauge
    /// </summary>
    /// <param name="plaquettes">Collection of plaquettes</param>
    /// <returns>Average plaquette value</returns>
    public double AveragePlaquette(ReadOnlySpan<SU3Matrix> plaquettes)
    {
        if (plaquettes.Length == 0)
            return 0.0;

        double sum = 0.0;
        for (int i = 0; i < plaquettes.Length; i++)
        {
            sum += plaquettes[i].TraceReal() / 6.0;
        }

        return sum / plaquettes.Length;
    }

    /// <summary>
    /// Compute Polyakov loop for finite temperature QCD.
    ///
    /// L(x) = Tr[Π_t U_0(x, t)]
    ///
    /// PHYSICS SIGNIFICANCE:
    /// - Order parameter for confinement-deconfinement phase transition
    /// - ⟨|L|⟩ = 0: confined phase (low T)
    /// - ⟨|L|⟩ > 0: deconfined phase (high T)
    /// - Related to free energy of static quark: F = -T ln⟨|L|⟩
    /// </summary>
    /// <param name="timeSliceLinks">Temporal links U_0(x,t) along time direction</param>
    /// <returns>Polyakov loop value</returns>
    public Complex PolyakovLoop(ReadOnlySpan<SU3Matrix> timeSliceLinks)
    {
        if (timeSliceLinks.Length == 0)
            return Complex.Zero;

        // Compute product: Π_t U_0(x, t)
        SU3Matrix product = Identity;
        for (int t = 0; t < timeSliceLinks.Length; t++)
        {
            product = product.Multiply(timeSliceLinks[t]);
        }

        // Return normalized trace: Tr(product) / N
        return product.Trace() / 3.0;
    }
}
