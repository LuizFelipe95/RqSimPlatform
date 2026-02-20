using System;
using System.Numerics;
using RQSimulation.Gauge;

namespace RQSimulation.Fields;

/// <summary>
/// Color triplet spinor for SU(3) gauge theory (QCD).
/// 
/// CHECKLIST ITEM 2.3: Extend spinors to color triplets for SU(3).
/// 
/// In QCD, quarks carry color charge (red, green, blue). Each Dirac spinor
/// component (A, B, C, D) becomes a 3-component color vector:
/// 
///   ?_A = (?_A_r, ?_A_g, ?_A_b)
///   ?_B = (?_B_r, ?_B_g, ?_B_b)
///   etc.
/// 
/// Total degrees of freedom: 4 (Dirac) ? 3 (color) = 12 complex components per node.
/// 
/// Under SU(3) gauge transformation:
///   ?'_c = U_cd ?_d  (sum over d = r, g, b)
/// 
/// The parallel transport from node j to node i transforms the color indices:
///   ?_transported = U†_ij ?_j
/// </summary>
public struct ColorTriplet
{
    /// <summary>Red color component</summary>
    public Complex R;
    
    /// <summary>Green color component</summary>
    public Complex G;
    
    /// <summary>Blue color component</summary>
    public Complex B;
    
    /// <summary>Create zero color triplet</summary>
    public static ColorTriplet Zero => new() { R = Complex.Zero, G = Complex.Zero, B = Complex.Zero };
    
    /// <summary>Create color triplet with specified components</summary>
    public ColorTriplet(Complex r, Complex g, Complex b)
    {
        R = r;
        G = g;
        B = b;
    }
    
    /// <summary>Create from flat array [r, g, b]</summary>
    public ColorTriplet(Complex[] components)
    {
        if (components.Length != 3)
            throw new ArgumentException("Color triplet requires 3 components", nameof(components));
        R = components[0];
        G = components[1];
        B = components[2];
    }
    
    /// <summary>Convert to flat array [r, g, b]</summary>
    public Complex[] ToArray() => [R, G, B];
    
    /// <summary>Access by color index (0=R, 1=G, 2=B)</summary>
    public Complex this[int colorIndex]
    {
        get => colorIndex switch
        {
            0 => R,
            1 => G,
            2 => B,
            _ => throw new IndexOutOfRangeException("Color index must be 0, 1, or 2")
        };
        set
        {
            switch (colorIndex)
            {
                case 0: R = value; break;
                case 1: G = value; break;
                case 2: B = value; break;
                default: throw new IndexOutOfRangeException("Color index must be 0, 1, or 2");
            }
        }
    }
    
    /// <summary>Compute squared norm |?|? = |r|? + |g|? + |b|?</summary>
    public double NormSquared => R.Magnitude * R.Magnitude + G.Magnitude * G.Magnitude + B.Magnitude * B.Magnitude;
    
    /// <summary>Compute norm |?|</summary>
    public double Norm => Math.Sqrt(NormSquared);
    
    /// <summary>Hermitian conjugate (complex conjugate of each component)</summary>
    public ColorTriplet Dagger => new(Complex.Conjugate(R), Complex.Conjugate(G), Complex.Conjugate(B));
    
    /// <summary>
    /// Apply SU(3) matrix transformation: ?' = U * ?
    /// </summary>
    public ColorTriplet Transform(SU3Matrix U)
    {
        return new ColorTriplet(U.Apply(ToArray()));
    }
    
    /// <summary>
    /// Apply SU(3) adjoint transformation: ?' = U† * ? (parallel transport)
    /// </summary>
    public ColorTriplet ParallelTransport(SU3Matrix U)
    {
        return Transform(U.Dagger());
    }
    
    /// <summary>Inner product ??|?? = ?†·?</summary>
    public static Complex InnerProduct(ColorTriplet phi, ColorTriplet psi)
    {
        return Complex.Conjugate(phi.R) * psi.R +
               Complex.Conjugate(phi.G) * psi.G +
               Complex.Conjugate(phi.B) * psi.B;
    }
    
    // Arithmetic operators
    public static ColorTriplet operator +(ColorTriplet a, ColorTriplet b)
        => new(a.R + b.R, a.G + b.G, a.B + b.B);
    
    public static ColorTriplet operator -(ColorTriplet a, ColorTriplet b)
        => new(a.R - b.R, a.G - b.G, a.B - b.B);
    
    public static ColorTriplet operator *(Complex scalar, ColorTriplet v)
        => new(scalar * v.R, scalar * v.G, scalar * v.B);
    
    public static ColorTriplet operator *(ColorTriplet v, Complex scalar)
        => scalar * v;
    
    public static ColorTriplet operator *(double scalar, ColorTriplet v)
        => new(scalar * v.R, scalar * v.G, scalar * v.B);
    
    public static ColorTriplet operator *(ColorTriplet v, double scalar)
        => scalar * v;
    
    public override string ToString() => $"({R:F3}, {G:F3}, {B:F3})";
}

/// <summary>
/// Full color spinor: 4 Dirac components ? 3 colors = 12 complex DOF per node.
/// 
/// This represents a quark field in QCD:
/// - A, B: left-handed Weyl spinor components (SU(2)_L doublet)
/// - C, D: right-handed Weyl spinor components (SU(2)_L singlet)
/// - Each component is a color triplet (r, g, b)
/// </summary>
public struct ColorSpinor
{
    /// <summary>Left-handed upper component (color triplet)</summary>
    public ColorTriplet A;
    
    /// <summary>Left-handed lower component (color triplet)</summary>
    public ColorTriplet B;
    
    /// <summary>Right-handed upper component (color triplet)</summary>
    public ColorTriplet C;
    
    /// <summary>Right-handed lower component (color triplet)</summary>
    public ColorTriplet D;
    
    /// <summary>Create zero color spinor</summary>
    public static ColorSpinor Zero => new()
    {
        A = ColorTriplet.Zero,
        B = ColorTriplet.Zero,
        C = ColorTriplet.Zero,
        D = ColorTriplet.Zero
    };
    
    /// <summary>
    /// Total squared norm: ?_c (|A_c|? + |B_c|? + |C_c|? + |D_c|?)
    /// </summary>
    public double NormSquared => A.NormSquared + B.NormSquared + C.NormSquared + D.NormSquared;
    
    /// <summary>Total norm</summary>
    public double Norm => Math.Sqrt(NormSquared);
    
    /// <summary>
    /// Apply SU(3) gauge transformation to all Dirac components.
    /// ?'_?,c = U_cd ?_?,d  for each ? ? {A, B, C, D}
    /// </summary>
    public ColorSpinor Transform(SU3Matrix U)
    {
        return new ColorSpinor
        {
            A = A.Transform(U),
            B = B.Transform(U),
            C = C.Transform(U),
            D = D.Transform(U)
        };
    }
    
    /// <summary>
    /// Apply SU(3) parallel transport (adjoint transformation).
    /// Used for gauge-covariant hopping term in Dirac equation.
    /// </summary>
    public ColorSpinor ParallelTransport(SU3Matrix U)
    {
        return Transform(U.Dagger());
    }
    
    /// <summary>
    /// Normalize to unit norm (preserving relative phases).
    /// </summary>
    public ColorSpinor Normalized()
    {
        double norm = Norm;
        if (norm < 1e-15)
            return Zero;
        
        double invNorm = 1.0 / norm;
        return new ColorSpinor
        {
            A = invNorm * A,
            B = invNorm * B,
            C = invNorm * C,
            D = invNorm * D
        };
    }
    
    /// <summary>
    /// Compute color charge density for color a ? {0,1,2} (r,g,b).
    /// ?_a = ?†_a ?_a (sum over Dirac indices)
    /// </summary>
    public double ColorDensity(int colorIndex)
    {
        double density = 0.0;
        density += A[colorIndex].Magnitude * A[colorIndex].Magnitude;
        density += B[colorIndex].Magnitude * B[colorIndex].Magnitude;
        density += C[colorIndex].Magnitude * C[colorIndex].Magnitude;
        density += D[colorIndex].Magnitude * D[colorIndex].Magnitude;
        return density;
    }
    
    /// <summary>
    /// Total fermion density (color singlet): ?_c ?†_c ?_c
    /// </summary>
    public double FermionDensity => NormSquared;
    
    /// <summary>
    /// Chiral density (left - right handed): ?_c (|A_c|? + |B_c|? - |C_c|? - |D_c|?)
    /// </summary>
    public double ChiralDensity => A.NormSquared + B.NormSquared - C.NormSquared - D.NormSquared;
    
    // Arithmetic operators
    public static ColorSpinor operator +(ColorSpinor a, ColorSpinor b)
        => new() { A = a.A + b.A, B = a.B + b.B, C = a.C + b.C, D = a.D + b.D };
    
    public static ColorSpinor operator -(ColorSpinor a, ColorSpinor b)
        => new() { A = a.A - b.A, B = a.B - b.B, C = a.C - b.C, D = a.D - b.D };
    
    public static ColorSpinor operator *(Complex scalar, ColorSpinor v)
        => new() { A = scalar * v.A, B = scalar * v.B, C = scalar * v.C, D = scalar * v.D };
    
    public static ColorSpinor operator *(ColorSpinor v, Complex scalar)
        => scalar * v;
    
    public static ColorSpinor operator *(double scalar, ColorSpinor v)
        => new() { A = scalar * v.A, B = scalar * v.B, C = scalar * v.C, D = scalar * v.D };
    
    public static ColorSpinor operator *(ColorSpinor v, double scalar)
        => scalar * v;
}

/// <summary>
/// Helper methods for color spinor field operations.
/// </summary>
public static class ColorSpinorField
{
    /// <summary>
    /// Initialize random color spinor field with given seed.
    /// Each component is initialized with random phase and small amplitude.
    /// </summary>
    public static ColorSpinor[] InitializeRandom(int N, Random rng, double amplitude = 0.1)
    {
        var field = new ColorSpinor[N];
        
        for (int i = 0; i < N; i++)
        {
            field[i] = new ColorSpinor
            {
                A = RandomColorTriplet(rng, amplitude),
                B = RandomColorTriplet(rng, amplitude),
                C = RandomColorTriplet(rng, amplitude),
                D = RandomColorTriplet(rng, amplitude)
            };
        }
        
        // Normalize total field
        double totalNorm = 0.0;
        for (int i = 0; i < N; i++)
            totalNorm += field[i].NormSquared;
        
        if (totalNorm > 1e-10)
        {
            double scale = Math.Sqrt(N / totalNorm);
            for (int i = 0; i < N; i++)
                field[i] = scale * field[i];
        }
        
        return field;
    }
    
    private static ColorTriplet RandomColorTriplet(Random rng, double amplitude)
    {
        return new ColorTriplet(
            Complex.FromPolarCoordinates(amplitude * rng.NextDouble(), 2.0 * Math.PI * rng.NextDouble()),
            Complex.FromPolarCoordinates(amplitude * rng.NextDouble(), 2.0 * Math.PI * rng.NextDouble()),
            Complex.FromPolarCoordinates(amplitude * rng.NextDouble(), 2.0 * Math.PI * rng.NextDouble())
        );
    }
    
    /// <summary>
    /// Compute total color charge (Gell-Mann generator expectation values).
    /// Returns 8-component array for T_a = ?_a/2.
    /// </summary>
    public static double[] ComputeColorCharges(ColorSpinor[] field)
    {
        var charges = new double[8];
        
        foreach (var spinor in field)
        {
            // Color density matrix: ?_cd = ?_? ?*_?c ?_?d
            // T_a = Tr(?_a ?) / 2
            var components = new Complex[3];
            for (int c = 0; c < 3; c++)
            {
                components[c] = spinor.A[c] + spinor.B[c] + spinor.C[c] + spinor.D[c];
            }
            
            // Simplified: just track diagonal charges (T_3 and T_8)
            // T_3 ? |r|? - |g|?
            charges[2] += components[0].Magnitude * components[0].Magnitude 
                        - components[1].Magnitude * components[1].Magnitude;
            
            // T_8 ? |r|? + |g|? - 2|b|?
            double sqrt3 = Math.Sqrt(3.0);
            charges[7] += (components[0].Magnitude * components[0].Magnitude 
                         + components[1].Magnitude * components[1].Magnitude 
                         - 2.0 * components[2].Magnitude * components[2].Magnitude) / sqrt3;
        }
        
        return charges;
    }
}
