using System;
using ComputeSharp;
using RQSimulation.Analysis.VacuumEnergy;

namespace RQSimulation.GPUOptimized.Analysis;

/// <summary>
/// GPU-accelerated engine for computing vacuum energy statistics.
///
/// Pipeline:
/// 1. Upload vacuum mask (int[]) and energy (double[]) to GPU
/// 2. Run MaskedEnergyCopyKernel → masked energy + masked energy²
/// 3. Run VacuumCountMinMaxKernel → vacuum count, scaled min/max
/// 4. Run BlockSumDoubleKernel on masked arrays → partial sums
/// 5. Final CPU reduction of partial sums → mean, variance
/// 6. Return VacuumStats
///
/// Falls back to CPU VacuumEnergyAnalyzer when GPU is unavailable.
/// </summary>
public sealed class GpuVacuumStatEngine : IDisposable
{
    private readonly GraphicsDevice _device;

    // Reusable GPU buffers
    private ReadOnlyBuffer<double>? _energyBuffer;
    private ReadOnlyBuffer<int>? _maskBuffer;
    private ReadWriteBuffer<double>? _maskedEnergyBuffer;
    private ReadWriteBuffer<double>? _maskedEnergySqBuffer;
    private ReadWriteBuffer<double>? _partialSumsBuffer;
    private ReadWriteBuffer<double>? _partialSumsSqBuffer;
    private ReadWriteBuffer<int>? _resultsBuffer;

    private int _allocatedSize;
    private int _blockCount;
    private bool _disposed;

    private const int BlockSize = 64;

    /// <summary>Scale factor for int atomics on energy values.</summary>
    private const double EnergyScale = 100_000.0;

    public GpuVacuumStatEngine(GraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    /// <summary>
    /// Computes vacuum energy statistics using GPU acceleration.
    /// </summary>
    /// <param name="vacuumEnergy">Per-node vacuum energy array</param>
    /// <param name="vacuumMask">Per-node mask: 1 = vacuum, 0 = non-vacuum</param>
    /// <returns>Aggregated vacuum energy statistics</returns>
    public VacuumStats ComputeVacuumStats(double[] vacuumEnergy, int[] vacuumMask)
    {
        ArgumentNullException.ThrowIfNull(vacuumEnergy);
        ArgumentNullException.ThrowIfNull(vacuumMask);

        int n = vacuumEnergy.Length;
        if (n == 0 || n != vacuumMask.Length)
        {
            return default;
        }

        EnsureBuffers(n);
        UploadData(vacuumEnergy, vacuumMask, n);

        // Phase 1: Masked copy — produces zeroed-out non-vacuum entries
        _device.For(n, new MaskedEnergyCopyKernel(
            _energyBuffer!, _maskBuffer!,
            _maskedEnergyBuffer!, _maskedEnergySqBuffer!, n));

        // Phase 2: Count + min/max via atomics
        int[] initResults = [0, int.MaxValue, int.MinValue];
        _resultsBuffer!.CopyFrom(initResults);

        _device.For(n, new VacuumCountMinMaxKernel(
            _energyBuffer!, _maskBuffer!, _resultsBuffer!, n, EnergyScale));

        // Phase 3: Block-level reduction for sum and sumSq
        _device.For(n, new BlockSumDoubleKernel(
            _maskedEnergyBuffer!, _partialSumsBuffer!, n, BlockSize));

        _device.For(n, new BlockSumDoubleKernel(
            _maskedEnergySqBuffer!, _partialSumsSqBuffer!, n, BlockSize));

        // Download and finalize on CPU
        int[] results = new int[3];
        _resultsBuffer!.CopyTo(results);

        int vacuumCount = results[0];
        if (vacuumCount == 0)
        {
            return default;
        }

        double min = results[1] / EnergyScale;
        double max = results[2] / EnergyScale;

        // Sum partial sums on CPU
        double[] partials = new double[_blockCount];
        double[] partialsSq = new double[_blockCount];
        _partialSumsBuffer!.CopyTo(partials);
        _partialSumsSqBuffer!.CopyTo(partialsSq);

        double totalEnergy = 0;
        double totalEnergySq = 0;
        for (int i = 0; i < _blockCount; i++)
        {
            totalEnergy += partials[i];
            totalEnergySq += partialsSq[i];
        }

        double mean = totalEnergy / vacuumCount;
        double variance = Math.Max(0, (totalEnergySq / vacuumCount) - (mean * mean));

        return new VacuumStats(
            TotalVacuumEnergy: totalEnergy,
            VacuumNodeCount: vacuumCount,
            EnergyVariance: variance,
            MinNodeEnergy: min,
            MaxNodeEnergy: max);
    }

    /// <summary>
    /// Builds a vacuum mask array from an RQGraph.
    /// Each element is 1 if the node is vacuum, 0 otherwise.
    /// </summary>
    public static int[] BuildVacuumMask(RQGraph graph, double massThreshold = 1e-6)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var mask = new int[graph.N];
        for (int i = 0; i < graph.N; i++)
        {
            mask[i] = graph.IsVacuumNode(i, massThreshold) ? 1 : 0;
        }
        return mask;
    }

    /// <summary>
    /// Extracts vacuum energy array from an RQGraph.
    /// Returns zero-filled array if vacuum field is not initialized.
    /// </summary>
    public static double[] ExtractVacuumEnergy(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var field = graph.VacuumEnergyField;
        if (field.IsEmpty)
        {
            return new double[graph.N];
        }

        return field.ToArray();
    }

    // ============================================================
    // Buffer management
    // ============================================================

    private void EnsureBuffers(int n)
    {
        if (n <= _allocatedSize)
        {
            return;
        }

        DisposeBuffers();

        _blockCount = (n + BlockSize - 1) / BlockSize;

        _energyBuffer = _device.AllocateReadOnlyBuffer<double>(n);
        _maskBuffer = _device.AllocateReadOnlyBuffer<int>(n);
        _maskedEnergyBuffer = _device.AllocateReadWriteBuffer<double>(n);
        _maskedEnergySqBuffer = _device.AllocateReadWriteBuffer<double>(n);
        _partialSumsBuffer = _device.AllocateReadWriteBuffer<double>(_blockCount);
        _partialSumsSqBuffer = _device.AllocateReadWriteBuffer<double>(_blockCount);
        _resultsBuffer = _device.AllocateReadWriteBuffer<int>(3);

        _allocatedSize = n;
    }

    private void UploadData(double[] energy, int[] mask, int n)
    {
        // ReadOnlyBuffer requires re-creation to upload new data
        _energyBuffer?.Dispose();
        _maskBuffer?.Dispose();
        _energyBuffer = _device.AllocateReadOnlyBuffer(energy.AsSpan(0, n));
        _maskBuffer = _device.AllocateReadOnlyBuffer(mask.AsSpan(0, n));
    }

    private void DisposeBuffers()
    {
        _energyBuffer?.Dispose();
        _maskBuffer?.Dispose();
        _maskedEnergyBuffer?.Dispose();
        _maskedEnergySqBuffer?.Dispose();
        _partialSumsBuffer?.Dispose();
        _partialSumsSqBuffer?.Dispose();
        _resultsBuffer?.Dispose();
        _energyBuffer = null;
        _maskBuffer = null;
        _maskedEnergyBuffer = null;
        _maskedEnergySqBuffer = null;
        _partialSumsBuffer = null;
        _partialSumsSqBuffer = null;
        _resultsBuffer = null;
        _allocatedSize = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeBuffers();
    }
}
