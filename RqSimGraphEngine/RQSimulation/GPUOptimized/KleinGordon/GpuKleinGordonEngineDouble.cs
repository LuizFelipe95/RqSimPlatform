using System;
using ComputeSharp;

namespace RQSimulation.GPUOptimized.KleinGordon;

/// <summary>
/// Double-precision GPU-accelerated Klein-Gordon field evolution engine.
/// 
/// RQ-HYPOTHESIS CHECKLIST ITEM 2: Relativistic Scalar Field
/// ==========================================================
/// Discrete Klein-Gordon equation: (d?/dt? - Laplacian + m?) ? = 0
/// Ensures finite light cone and causality using Verlet integration.
/// </summary>
public class GpuKleinGordonEngineDouble : IDisposable
{
    private readonly GraphicsDevice _device;

    private ReadWriteBuffer<double>? _phiCurrentBuffer;
    private ReadWriteBuffer<double>? _phiPrevBuffer;
    private ReadWriteBuffer<double>? _phiNextBuffer;
    private ReadWriteBuffer<double>? _laplacianBuffer;
    private ReadWriteBuffer<double>? _energyBuffer;
    private ReadOnlyBuffer<double>? _massBuffer;
    private ReadOnlyBuffer<double>? _localDtBuffer;
    private ReadOnlyBuffer<double>? _csrWeightsBuffer;
    private ReadOnlyBuffer<int>? _csrOffsetsBuffer;
    private ReadOnlyBuffer<int>? _csrNeighborsBuffer;

    private int _nodeCount;
    private int _edgeCount;
    private bool _initialized;

    public GpuKleinGordonEngineDouble()
    {
        _device = GraphicsDevice.GetDefault();
    }

    public bool IsDoublePrecisionSupported => _device.IsDoublePrecisionSupportAvailable();

    public void Initialize(int nodeCount, int edgeCount)
    {
        if (!IsDoublePrecisionSupported)
            throw new NotSupportedException("GPU does not support double precision.");

        _nodeCount = nodeCount;
        _edgeCount = edgeCount;

        DisposeBuffers();

        _phiCurrentBuffer = _device.AllocateReadWriteBuffer<double>(nodeCount);
        _phiPrevBuffer = _device.AllocateReadWriteBuffer<double>(nodeCount);
        _phiNextBuffer = _device.AllocateReadWriteBuffer<double>(nodeCount);
        _laplacianBuffer = _device.AllocateReadWriteBuffer<double>(nodeCount);
        _energyBuffer = _device.AllocateReadWriteBuffer<double>(nodeCount);
        _massBuffer = _device.AllocateReadOnlyBuffer<double>(nodeCount);
        _localDtBuffer = _device.AllocateReadOnlyBuffer<double>(nodeCount);
        _csrWeightsBuffer = _device.AllocateReadOnlyBuffer<double>(edgeCount);
        _csrOffsetsBuffer = _device.AllocateReadOnlyBuffer<int>(nodeCount + 1);
        _csrNeighborsBuffer = _device.AllocateReadOnlyBuffer<int>(edgeCount);

        _initialized = true;

        // Initialize fields to zero
        var zeros = new double[nodeCount];
        _phiCurrentBuffer.CopyFrom(zeros);
        _phiPrevBuffer.CopyFrom(zeros);
        _phiNextBuffer.CopyFrom(zeros);
    }

    public void UploadTopology(int[] csrOffsets, int[] csrNeighbors, double[] csrWeights)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _csrOffsetsBuffer!.CopyFrom(csrOffsets);
        _csrNeighborsBuffer!.CopyFrom(csrNeighbors);
        _csrWeightsBuffer!.CopyFrom(csrWeights);
    }

    public void UploadMass(double[] mass)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _massBuffer!.CopyFrom(mass);
    }

    public void UploadLocalDt(double[] localDt)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _localDtBuffer!.CopyFrom(localDt);
    }

    public void UploadField(double[] phiCurrent, double[] phiPrev)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _phiCurrentBuffer!.CopyFrom(phiCurrent);
        _phiPrevBuffer!.CopyFrom(phiPrev);
    }

    /// <summary>
    /// Compute graph Laplacian of the current field.
    /// </summary>
    public void ComputeLaplacian()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        // Copy phiCurrent to temp ReadOnly buffer
        var phiData = new double[_nodeCount];
        _phiCurrentBuffer!.CopyTo(phiData);
        using var phiReadOnly = _device.AllocateReadOnlyBuffer(phiData);

        _device.For(_nodeCount, new GraphLaplacianKernelDouble(
            _csrOffsetsBuffer!,
            _csrNeighborsBuffer!,
            _csrWeightsBuffer!,
            phiReadOnly,
            _laplacianBuffer!,
            _nodeCount
        ));
    }

    /// <summary>
    /// Perform one Klein-Gordon Verlet integration step.
    /// </summary>
    public void Step()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        // 1. Compute Laplacian
        ComputeLaplacian();

        // 2. Copy buffers to ReadOnly for Verlet kernel
        var phiCurrentData = new double[_nodeCount];
        var phiPrevData = new double[_nodeCount];
        var laplacianData = new double[_nodeCount];
        _phiCurrentBuffer!.CopyTo(phiCurrentData);
        _phiPrevBuffer!.CopyTo(phiPrevData);
        _laplacianBuffer!.CopyTo(laplacianData);

        using var phiCurrentRO = _device.AllocateReadOnlyBuffer(phiCurrentData);
        using var phiPrevRO = _device.AllocateReadOnlyBuffer(phiPrevData);
        using var laplacianRO = _device.AllocateReadOnlyBuffer(laplacianData);

        // 2. Verlet step
        _device.For(_nodeCount, new KleinGordonVerletKernelDouble(
            phiCurrentRO,
            phiPrevRO,
            _phiNextBuffer!,
            laplacianRO,
            _massBuffer!,
            _localDtBuffer!,
            _nodeCount
        ));

        // 3. Copy phiNext to ReadOnly for swap kernel
        var phiNextData = new double[_nodeCount];
        _phiNextBuffer!.CopyTo(phiNextData);
        using var phiNextRO = _device.AllocateReadOnlyBuffer(phiNextData);

        // 3. Swap buffers: prev ? current, current ? next
        _device.For(_nodeCount, new SwapBuffersKernelDouble(
            _phiCurrentBuffer!,
            _phiPrevBuffer!,
            phiNextRO,
            _nodeCount
        ));
    }

    /// <summary>
    /// Compute total field energy.
    /// </summary>
    public double ComputeTotalEnergy()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        // First compute Laplacian for gradient term
        ComputeLaplacian();

        // Copy buffers to ReadOnly
        var phiCurrentData = new double[_nodeCount];
        var phiPrevData = new double[_nodeCount];
        var laplacianData = new double[_nodeCount];
        _phiCurrentBuffer!.CopyTo(phiCurrentData);
        _phiPrevBuffer!.CopyTo(phiPrevData);
        _laplacianBuffer!.CopyTo(laplacianData);

        using var phiCurrentRO = _device.AllocateReadOnlyBuffer(phiCurrentData);
        using var phiPrevRO = _device.AllocateReadOnlyBuffer(phiPrevData);
        using var laplacianRO = _device.AllocateReadOnlyBuffer(laplacianData);

        _device.For(_nodeCount, new FieldEnergyKernelDouble(
            phiCurrentRO,
            phiPrevRO,
            laplacianRO,
            _massBuffer!,
            _localDtBuffer!,
            _energyBuffer!,
            _nodeCount
        ));

        var energies = new double[_nodeCount];
        _energyBuffer!.CopyTo(energies);

        double total = 0;
        for (int i = 0; i < _nodeCount; i++)
            total += energies[i];

        return total;
    }

    public void DownloadField(double[] phiCurrent)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _phiCurrentBuffer!.CopyTo(phiCurrent);
    }

    public void DownloadLaplacian(double[] laplacian)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _laplacianBuffer!.CopyTo(laplacian);
    }

    private void DisposeBuffers()
    {
        _phiCurrentBuffer?.Dispose();
        _phiPrevBuffer?.Dispose();
        _phiNextBuffer?.Dispose();
        _laplacianBuffer?.Dispose();
        _energyBuffer?.Dispose();
        _massBuffer?.Dispose();
        _localDtBuffer?.Dispose();
        _csrWeightsBuffer?.Dispose();
        _csrOffsetsBuffer?.Dispose();
        _csrNeighborsBuffer?.Dispose();
    }

    public void Dispose()
    {
        DisposeBuffers();
        GC.SuppressFinalize(this);
    }
}
