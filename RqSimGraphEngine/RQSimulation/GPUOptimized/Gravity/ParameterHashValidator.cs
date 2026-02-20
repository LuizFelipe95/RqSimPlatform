// ============================================================
// ParameterHashValidator.cs
// Hash-based change detection for shader parameter injection
// Part of Hard Science Mode implementation (Audit Item 2)
// ============================================================
// 
// AUDIT REQUIREMENT: "Parameter Injection Validation"
// Шейдеры ComputeSharp - это readonly struct. Если создать один раз
// и переиспользовать, параметры "замораживаются" на первом кадре.
// 
// РЕШЕНИЕ: Вычислять хэш параметров и пересоздавать kernel при изменении.
// ============================================================

using System.Runtime.InteropServices;
using RQSimulation.Core.Plugins;

namespace RQSimulation.GPUOptimized;

/// <summary>
/// Validates and tracks physics parameter changes for shader recreation.
/// <para><strong>AUDIT REQUIREMENT:</strong></para>
/// <para>
/// ComputeSharp shaders are readonly structs. Parameters are captured at construction.
/// This validator detects when parameters change, forcing shader recreation.
/// </para>
/// <para><strong>USAGE:</strong></para>
/// <code>
/// if (validator.HasParametersChanged(currentParams))
/// {
///     // Recreate shader with new parameters
///     shader = new MyKernel(..., currentParams.G, currentParams.Alpha);
///     validator.UpdateHash(currentParams);
/// }
/// </code>
/// </summary>
public sealed class ParameterHashValidator
{
    private int _lastGravityHash;
    private int _lastCurvatureHash;
    private int _lastFullHash;
    
    /// <summary>
    /// Checks if gravity-related parameters have changed.
    /// Gravity params: G, Lambda, dt
    /// </summary>
    public bool HasGravityParamsChanged(in DynamicPhysicsParams p)
    {
        int hash = ComputeGravityHash(p);
        return hash != _lastGravityHash;
    }
    
    /// <summary>
    /// Checks if curvature-related parameters have changed.
    /// Curvature params: RicciFlowAlpha, LazyWalkAlpha, SinkhornIterations, SinkhornEpsilon
    /// </summary>
    public bool HasCurvatureParamsChanged(in DynamicPhysicsParams p)
    {
        int hash = ComputeCurvatureHash(p);
        return hash != _lastCurvatureHash;
    }
    
    /// <summary>
    /// Checks if any physics parameters have changed.
    /// </summary>
    public bool HasAnyParamsChanged(in DynamicPhysicsParams p)
    {
        int hash = ComputeFullHash(p);
        return hash != _lastFullHash;
    }
    
    /// <summary>
    /// Updates stored gravity hash after shader recreation.
    /// </summary>
    public void UpdateGravityHash(in DynamicPhysicsParams p)
    {
        _lastGravityHash = ComputeGravityHash(p);
    }
    
    /// <summary>
    /// Updates stored curvature hash after shader recreation.
    /// </summary>
    public void UpdateCurvatureHash(in DynamicPhysicsParams p)
    {
        _lastCurvatureHash = ComputeCurvatureHash(p);
    }
    
    /// <summary>
    /// Updates all stored hashes after shader recreation.
    /// </summary>
    public void UpdateAllHashes(in DynamicPhysicsParams p)
    {
        _lastGravityHash = ComputeGravityHash(p);
        _lastCurvatureHash = ComputeCurvatureHash(p);
        _lastFullHash = ComputeFullHash(p);
    }
    
    /// <summary>
    /// Forces hash reset, ensuring next call to HasXChanged returns true.
    /// Use when topology changes require shader recreation.
    /// </summary>
    public void InvalidateAllHashes()
    {
        _lastGravityHash = 0;
        _lastCurvatureHash = 0;
        _lastFullHash = 0;
    }
    
    // ============================================================
    // HASH COMPUTATION
    // ============================================================
    
    /// <summary>
    /// Computes hash for gravity-related parameters.
    /// Uses bit manipulation to detect any change in double values.
    /// </summary>
    private static int ComputeGravityHash(in DynamicPhysicsParams p)
    {
        // Convert doubles to bits for exact comparison
        long gBits = BitConverter.DoubleToInt64Bits(p.GravitationalCoupling);
        long lambdaBits = BitConverter.DoubleToInt64Bits(p.CosmologicalConstant);
        long dtBits = BitConverter.DoubleToInt64Bits(p.DeltaTime);
        
        // Combine using XOR and bit rotation
        int hash = 17;
        hash = hash * 31 + (int)(gBits ^ (gBits >> 32));
        hash = hash * 31 + (int)(lambdaBits ^ (lambdaBits >> 32));
        hash = hash * 31 + (int)(dtBits ^ (dtBits >> 32));
        hash = hash * 31 + (p.ScientificMode ? 1 : 0);
        
        return hash;
    }
    
    /// <summary>
    /// Computes hash for curvature-related parameters.
    /// </summary>
    private static int ComputeCurvatureHash(in DynamicPhysicsParams p)
    {
        long alphaBits = BitConverter.DoubleToInt64Bits(p.RicciFlowAlpha);
        long lazyBits = BitConverter.DoubleToInt64Bits(p.LazyWalkAlpha);
        long epsilonBits = BitConverter.DoubleToInt64Bits(p.SinkhornEpsilon);
        
        int hash = 17;
        hash = hash * 31 + (int)(alphaBits ^ (alphaBits >> 32));
        hash = hash * 31 + (int)(lazyBits ^ (lazyBits >> 32));
        hash = hash * 31 + (int)(epsilonBits ^ (epsilonBits >> 32));
        hash = hash * 31 + p.SinkhornIterations;
        hash = hash * 31 + (p.EnableOllivierRicci ? 1 : 0);
        
        return hash;
    }
    
    /// <summary>
    /// Computes hash for all physics parameters.
    /// Uses struct memory layout for comprehensive comparison.
    /// </summary>
    private static int ComputeFullHash(in DynamicPhysicsParams p)
    {
        // Use StructuralHash for full struct comparison
        int size = Marshal.SizeOf<DynamicPhysicsParams>();
        Span<byte> bytes = stackalloc byte[size];
        
        // Copy struct to byte span
        MemoryMarshal.Write(bytes, in p);
        
        // Compute hash over all bytes
        int hash = 17;
        for (int i = 0; i < bytes.Length; i++)
        {
            hash = hash * 31 + bytes[i];
        }
        
        return hash;
    }
}

/// <summary>
/// Extension methods for parameter validation in physics engines.
/// </summary>
public static class ParameterValidationExtensions
{
    /// <summary>
    /// Creates a parameter snapshot for later comparison.
    /// Useful for tracking which specific parameters changed.
    /// </summary>
    public static ParameterSnapshot CreateSnapshot(this DynamicPhysicsParams p)
    {
        return new ParameterSnapshot
        {
            G = p.GravitationalCoupling,
            Alpha = p.RicciFlowAlpha,
            Lambda = p.CosmologicalConstant,
            Dt = p.DeltaTime,
            LazyAlpha = p.LazyWalkAlpha,
            SinkhornIter = p.SinkhornIterations,
            IsScience = p.ScientificMode
        };
    }
    
    /// <summary>
    /// Compares two snapshots and returns change description.
    /// Useful for logging parameter changes.
    /// </summary>
    public static string DescribeChanges(this ParameterSnapshot before, ParameterSnapshot after)
    {
        var changes = new List<string>();
        
        if (Math.Abs(before.G - after.G) > 1e-10)
            changes.Add($"G: {before.G:F4} ? {after.G:F4}");
        if (Math.Abs(before.Alpha - after.Alpha) > 1e-10)
            changes.Add($"?: {before.Alpha:F4} ? {after.Alpha:F4}");
        if (Math.Abs(before.Lambda - after.Lambda) > 1e-10)
            changes.Add($"?: {before.Lambda:F4} ? {after.Lambda:F4}");
        if (Math.Abs(before.Dt - after.Dt) > 1e-10)
            changes.Add($"dt: {before.Dt:F4} ? {after.Dt:F4}");
        if (before.SinkhornIter != after.SinkhornIter)
            changes.Add($"Sinkhorn: {before.SinkhornIter} ? {after.SinkhornIter}");
        if (before.IsScience != after.IsScience)
            changes.Add($"Science: {before.IsScience} ? {after.IsScience}");
        
        return changes.Count > 0 
            ? string.Join(", ", changes) 
            : "No changes";
    }
}

/// <summary>
/// Lightweight parameter snapshot for change tracking.
/// </summary>
public struct ParameterSnapshot
{
    public double G;
    public double Alpha;
    public double Lambda;
    public double Dt;
    public double LazyAlpha;
    public int SinkhornIter;
    public bool IsScience;
}
