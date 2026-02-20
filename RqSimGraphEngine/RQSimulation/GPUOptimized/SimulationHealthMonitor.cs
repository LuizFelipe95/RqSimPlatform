// ============================================================
// SimulationHealthMonitor.cs
// NaN/Inf detection and graceful simulation halt for Science mode
// Part of Hard Science Mode implementation (Audit Item 5)
// ============================================================

using ComputeSharp;

namespace RQSimulation.GPUOptimized;

/// <summary>
/// Monitors simulation health and detects singularities.
/// <para><strong>SCIENTIFIC INTEGRITY:</strong></para>
/// <para>
/// In Science mode, we allow divergence (NaN/Inf) as valid experimental results.
/// This monitor detects divergence and halts simulation gracefully.
/// </para>
/// </summary>
public sealed class SimulationHealthMonitor : IDisposable
{
    private readonly GraphicsDevice _device;
    
    // Diagnostic buffers
    private ReadWriteBuffer<int>? _nanCountBuffer;
    private ReadWriteBuffer<int>? _infCountBuffer;
    private ReadWriteBuffer<int>? _firstNanIndexBuffer;
    private ReadWriteBuffer<int>? _negativeCountBuffer;
    
    // Results cache
    private readonly int[] _resultCache = new int[4];
    
    /// <summary>
    /// Threshold for considering simulation "unhealthy".
    /// </summary>
    public int NaNThreshold { get; set; } = 0;
    
    /// <summary>
    /// Threshold for Inf values (some Inf is acceptable during horizon formation).
    /// </summary>
    public int InfThreshold { get; set; } = 10;
    
    /// <summary>
    /// Whether to check for negative weights (should be 0 in physical simulation).
    /// </summary>
    public bool CheckNegativeWeights { get; set; } = true;

    public SimulationHealthMonitor(GraphicsDevice device)
    {
        _device = device;
        InitializeBuffers();
    }

    private void InitializeBuffers()
    {
        _nanCountBuffer = _device.AllocateReadWriteBuffer<int>(1);
        _infCountBuffer = _device.AllocateReadWriteBuffer<int>(1);
        _firstNanIndexBuffer = _device.AllocateReadWriteBuffer<int>(1);
        _negativeCountBuffer = _device.AllocateReadWriteBuffer<int>(1);
    }

    /// <summary>
    /// Checks health of double-precision weight buffer.
    /// </summary>
    public HealthCheckResult CheckWeightsDouble(ReadOnlyBuffer<double> weights)
    {
        int count = (int)weights.Length;
        
        // Zero diagnostic buffers
        ZeroBuffers();
        
        // Initialize first NaN index to max (will be atomically minimized)
        _firstNanIndexBuffer!.CopyFrom([int.MaxValue]);
        
        // Run NaN detection kernel
        var kernel = new WeightHealthKernelDouble(
            weights,
            _nanCountBuffer!,
            _infCountBuffer!,
            _firstNanIndexBuffer!,
            _negativeCountBuffer!,
            count);
        
        _device.For(count, kernel);
        
        // Read results
        _nanCountBuffer!.CopyTo(_resultCache.AsSpan(0, 1));
        _infCountBuffer!.CopyTo(_resultCache.AsSpan(1, 1));
        _firstNanIndexBuffer!.CopyTo(_resultCache.AsSpan(2, 1));
        _negativeCountBuffer!.CopyTo(_resultCache.AsSpan(3, 1));
        
        int nanCount = _resultCache[0];
        int infCount = _resultCache[1];
        int firstNaN = _resultCache[2];
        int negCount = _resultCache[3];
        
        return new HealthCheckResult
        {
            NaNCount = nanCount,
            InfCount = infCount,
            FirstNaNIndex = firstNaN < int.MaxValue ? firstNaN : -1,
            NegativeCount = negCount,
            IsHealthy = nanCount <= NaNThreshold 
                     && infCount <= InfThreshold 
                     && (!CheckNegativeWeights || negCount == 0),
            SingularityDetected = nanCount > 0 || infCount > InfThreshold,
            Timestamp = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Checks health of single-precision weight buffer.
    /// </summary>
    public HealthCheckResult CheckWeightsFloat(ReadOnlyBuffer<float> weights)
    {
        int count = (int)weights.Length;
        
        ZeroBuffers();
        _firstNanIndexBuffer!.CopyFrom([int.MaxValue]);
        
        var kernel = new WeightHealthKernelFloat(
            weights,
            _nanCountBuffer!,
            _infCountBuffer!,
            _firstNanIndexBuffer!,
            _negativeCountBuffer!,
            count);
        
        _device.For(count, kernel);
        
        _nanCountBuffer!.CopyTo(_resultCache.AsSpan(0, 1));
        _infCountBuffer!.CopyTo(_resultCache.AsSpan(1, 1));
        _firstNanIndexBuffer!.CopyTo(_resultCache.AsSpan(2, 1));
        _negativeCountBuffer!.CopyTo(_resultCache.AsSpan(3, 1));
        
        return new HealthCheckResult
        {
            NaNCount = _resultCache[0],
            InfCount = _resultCache[1],
            FirstNaNIndex = _resultCache[2] < int.MaxValue ? _resultCache[2] : -1,
            NegativeCount = _resultCache[3],
            IsHealthy = _resultCache[0] <= NaNThreshold 
                     && _resultCache[1] <= InfThreshold,
            SingularityDetected = _resultCache[0] > 0,
            Timestamp = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Quick check without full diagnostics - just returns healthy/unhealthy.
    /// Use for frequent polling in hot path.
    /// </summary>
    public bool QuickHealthCheck(ReadOnlyBuffer<float> weights, int sampleCount = 100)
    {
        int count = (int)weights.Length;
        int stride = Math.Max(1, count / sampleCount);
        
        ZeroBuffers();
        
        var kernel = new QuickHealthKernel(
            weights,
            _nanCountBuffer!,
            count,
            stride);
        
        _device.For(sampleCount, kernel);
        
        _nanCountBuffer!.CopyTo(_resultCache.AsSpan(0, 1));
        return _resultCache[0] == 0;
    }
    
    private void ZeroBuffers()
    {
        _nanCountBuffer!.CopyFrom([0]);
        _infCountBuffer!.CopyFrom([0]);
        _negativeCountBuffer!.CopyFrom([0]);
    }
    
    public void Dispose()
    {
        _nanCountBuffer?.Dispose();
        _infCountBuffer?.Dispose();
        _firstNanIndexBuffer?.Dispose();
        _negativeCountBuffer?.Dispose();
    }
}

/// <summary>
/// Result of health check.
/// </summary>
public struct HealthCheckResult
{
    public int NaNCount;
    public int InfCount;
    public int FirstNaNIndex;
    public int NegativeCount;
    public bool IsHealthy;
    public bool SingularityDetected;
    public DateTime Timestamp;
    
    public override readonly string ToString()
    {
        if (IsHealthy)
            return "Healthy";
        
        var issues = new List<string>();
        if (NaNCount > 0) issues.Add($"NaN?{NaNCount} @{FirstNaNIndex}");
        if (InfCount > 0) issues.Add($"Inf?{InfCount}");
        if (NegativeCount > 0) issues.Add($"Neg?{NegativeCount}");
        
        return $"UNHEALTHY: {string.Join(", ", issues)}";
    }
}

// ============================================================
// GPU KERNELS
// ============================================================

[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
public readonly partial struct WeightHealthKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<double> Weights;
    public readonly ReadWriteBuffer<int> NaNCount;
    public readonly ReadWriteBuffer<int> InfCount;
    public readonly ReadWriteBuffer<int> FirstNaNIndex;
    public readonly ReadWriteBuffer<int> NegativeCount;
    public readonly int Count;

    public WeightHealthKernelDouble(
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<int> nanCount,
        ReadWriteBuffer<int> infCount,
        ReadWriteBuffer<int> firstNaNIndex,
        ReadWriteBuffer<int> negativeCount,
        int count)
    {
        Weights = weights;
        NaNCount = nanCount;
        InfCount = infCount;
        FirstNaNIndex = firstNaNIndex;
        NegativeCount = negativeCount;
        Count = count;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= Count) return;
        
        double val = Weights[i];
        
        // NaN check (x != x is true for NaN)
        if (val != val)
        {
            Hlsl.InterlockedAdd(ref NaNCount[0], 1);
            Hlsl.InterlockedMin(ref FirstNaNIndex[0], i);
        }
        // Inf check using threshold
        else if (val > 1e300 || val < -1e300)
        {
            Hlsl.InterlockedAdd(ref InfCount[0], 1);
        }
        // Negative check
        else if (val < 0.0)
        {
            Hlsl.InterlockedAdd(ref NegativeCount[0], 1);
        }
    }
}

[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct WeightHealthKernelFloat : IComputeShader
{
    public readonly ReadOnlyBuffer<float> Weights;
    public readonly ReadWriteBuffer<int> NaNCount;
    public readonly ReadWriteBuffer<int> InfCount;
    public readonly ReadWriteBuffer<int> FirstNaNIndex;
    public readonly ReadWriteBuffer<int> NegativeCount;
    public readonly int Count;

    public WeightHealthKernelFloat(
        ReadOnlyBuffer<float> weights,
        ReadWriteBuffer<int> nanCount,
        ReadWriteBuffer<int> infCount,
        ReadWriteBuffer<int> firstNaNIndex,
        ReadWriteBuffer<int> negativeCount,
        int count)
    {
        Weights = weights;
        NaNCount = nanCount;
        InfCount = infCount;
        FirstNaNIndex = firstNaNIndex;
        NegativeCount = negativeCount;
        Count = count;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= Count) return;
        
        float val = Weights[i];
        
        // NaN check using comparison trick (NaN != NaN)
        if (val != val)
        {
            Hlsl.InterlockedAdd(ref NaNCount[0], 1);
            Hlsl.InterlockedMin(ref FirstNaNIndex[0], i);
        }
        // Inf check using threshold (large float values)
        else if (val > 1e37f || val < -1e37f)
        {
            Hlsl.InterlockedAdd(ref InfCount[0], 1);
        }
        else if (val < 0.0f)
        {
            Hlsl.InterlockedAdd(ref NegativeCount[0], 1);
        }
    }
}

/// <summary>
/// Health kernel for ReadWriteBuffer (used when checking live simulation buffers).
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct WeightHealthKernelFloatRW : IComputeShader
{
    public readonly ReadWriteBuffer<float> Weights;
    public readonly ReadWriteBuffer<int> NaNCount;
    public readonly ReadWriteBuffer<int> InfCount;
    public readonly ReadWriteBuffer<int> FirstNaNIndex;
    public readonly ReadWriteBuffer<int> NegativeCount;
    public readonly int Count;

    public WeightHealthKernelFloatRW(
        ReadWriteBuffer<float> weights,
        ReadWriteBuffer<int> nanCount,
        ReadWriteBuffer<int> infCount,
        ReadWriteBuffer<int> firstNaNIndex,
        ReadWriteBuffer<int> negativeCount,
        int count)
    {
        Weights = weights;
        NaNCount = nanCount;
        InfCount = infCount;
        FirstNaNIndex = firstNaNIndex;
        NegativeCount = negativeCount;
        Count = count;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= Count) return;
        
        float val = Weights[i];
        
        if (val != val)
        {
            Hlsl.InterlockedAdd(ref NaNCount[0], 1);
            Hlsl.InterlockedMin(ref FirstNaNIndex[0], i);
        }
        else if (val > 1e37f || val < -1e37f)
        {
            Hlsl.InterlockedAdd(ref InfCount[0], 1);
        }
        else if (val < 0.0f)
        {
            Hlsl.InterlockedAdd(ref NegativeCount[0], 1);
        }
    }
}

[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct QuickHealthKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<float> Weights;
    public readonly ReadWriteBuffer<int> NaNCount;
    public readonly int Count;
    public readonly int Stride;

    public QuickHealthKernel(
        ReadOnlyBuffer<float> weights,
        ReadWriteBuffer<int> nanCount,
        int count,
        int stride)
    {
        Weights = weights;
        NaNCount = nanCount;
        Count = count;
        Stride = stride;
    }

    public void Execute()
    {
        int sampleIdx = ThreadIds.X;
        int i = sampleIdx * Stride;
        if (i >= Count) return;
        
        float val = Weights[i];
        
        // NaN check: val != val, or Inf check using threshold
        if (val != val || val > 1e37f || val < -1e37f)
        {
            Hlsl.InterlockedAdd(ref NaNCount[0], 1);
        }
    }
}
