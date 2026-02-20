using System;
using ComputeSharp;

namespace RQSimulation.GPUOptimized.RelationalTime;

/// <summary>
/// Double-precision GPU-accelerated relational time computation engine.
/// 
/// RQ-HYPOTHESIS CHECKLIST ITEM 1: Pure Relational Time
/// =====================================================
/// Implements the lapse function N_i from Ricci curvature:
///   N_i = 1 / (1 + |R_i|)
/// 
/// Each node evolves with its own proper time step: d?_i = N_i * dt
/// </summary>
public class GpuRelationalTimeEngineDouble : IDisposable
{
    private readonly GraphicsDevice _device;

    private ReadWriteBuffer<double>? _lapseBuffer;
    private ReadWriteBuffer<double>? _localDtBuffer;
    private ReadWriteBuffer<double>? _ricciScalarBuffer;
    private ReadWriteBuffer<double>? _entropyBuffer;
    
    /// <summary>
    /// RQ-HYPOTHESIS CHECKLIST ITEM 1: Node Proper Time (Page-Wootters Mechanism).
    /// Each node accumulates its own proper time: ?_i += localDt_i
    /// </summary>
    private ReadWriteBuffer<double>? _nodeClocksBuffer;
    
    private ReadOnlyBuffer<double>? _edgeCurvaturesBuffer;
    private ReadOnlyBuffer<double>? _csrWeightsBuffer;
    private ReadOnlyBuffer<int>? _csrOffsetsBuffer;
    private ReadOnlyBuffer<int>? _csrNeighborsBuffer;

    private int _nodeCount;
    private int _edgeCount;
    private bool _initialized;

    private double _timeDilationAlpha = 0.5;
    private double _lapseFunctionAlpha = 1.0;  // RQ-HYPOTHESIS: Curvature coupling constant
    private double _minLapse = 0.01;
    private double _maxLapse = 1.0;
    private double _minDt = 1e-6;
    private double _maxDt = 0.1;

    public GpuRelationalTimeEngineDouble()
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

        _lapseBuffer = _device.AllocateReadWriteBuffer<double>(nodeCount);
        _localDtBuffer = _device.AllocateReadWriteBuffer<double>(nodeCount);
        _ricciScalarBuffer = _device.AllocateReadWriteBuffer<double>(nodeCount);
        _entropyBuffer = _device.AllocateReadWriteBuffer<double>(nodeCount);
        _nodeClocksBuffer = _device.AllocateReadWriteBuffer<double>(nodeCount);
        _edgeCurvaturesBuffer = _device.AllocateReadOnlyBuffer<double>(edgeCount);
        _csrWeightsBuffer = _device.AllocateReadOnlyBuffer<double>(edgeCount);
        _csrOffsetsBuffer = _device.AllocateReadOnlyBuffer<int>(nodeCount + 1);
        _csrNeighborsBuffer = _device.AllocateReadOnlyBuffer<int>(edgeCount);

        _initialized = true;

        // Initialize lapse to 1.0
        var ones = new double[nodeCount];
        for (int i = 0; i < nodeCount; i++) ones[i] = 1.0;
        _lapseBuffer.CopyFrom(ones);
        
        // Initialize nodeClocks to 0.0 (Page-Wootters: each node starts with zero proper time)
        var zeros = new double[nodeCount];
        _nodeClocksBuffer.CopyFrom(zeros);
    }

    public void SetParameters(double alpha, double minLapse, double maxLapse, double minDt, double maxDt)
    {
        _timeDilationAlpha = alpha;
        _minLapse = minLapse;
        _maxLapse = maxLapse;
        _minDt = minDt;
        _maxDt = maxDt;
    }
    
    /// <summary>
    /// Set the lapse function curvature coupling constant.
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 1: N = 1 / (1 + Alpha * |R|)
    /// Higher Alpha = stronger gravitational time dilation effect.
    /// </summary>
    public void SetLapseFunctionAlpha(double alpha)
    {
        _lapseFunctionAlpha = alpha;
    }

    public void UploadTopology(int[] csrOffsets, int[] csrNeighbors, double[] csrWeights)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _csrOffsetsBuffer!.CopyFrom(csrOffsets);
        _csrNeighborsBuffer!.CopyFrom(csrNeighbors);
        _csrWeightsBuffer!.CopyFrom(csrWeights);
    }

    public void UploadEdgeCurvatures(double[] edgeCurvatures)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _edgeCurvaturesBuffer!.CopyFrom(edgeCurvatures);
    }

    /// <summary>
    /// Compute Ricci scalar at each node from edge curvatures.
    /// R_i = ?_j R_ij (sum over all edges incident to node i)
    /// </summary>
    public void ComputeRicciScalar()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _device.For(_nodeCount, new ComputeRicciScalarKernelDouble(
            _csrOffsetsBuffer!,
            _csrNeighborsBuffer!,
            _edgeCurvaturesBuffer!,
            _ricciScalarBuffer!,
            _nodeCount
        ));
    }

    /// <summary>
    /// Compute lapse function from Ricci scalar: N = 1 / (1 + Alpha * |R|)
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 1: Relativistic Lapse Function
    /// Alpha = _lapseFunctionAlpha controls gravitational time dilation strength.
    /// </summary>
    public void ComputeLapseFromRicci()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        // Copy ricciScalar to a temp ReadOnly view
        var ricciData = new double[_nodeCount];
        _ricciScalarBuffer!.CopyTo(ricciData);
        using var ricciReadOnly = _device.AllocateReadOnlyBuffer(ricciData);

        _device.For(_nodeCount, new LapseFromRicciKernelDouble(
            ricciReadOnly,
            _lapseBuffer!,
            _lapseFunctionAlpha,  // RQ-HYPOTHESIS: Use Alpha parameter
            _minLapse,
            _maxLapse
        ));
    }

    /// <summary>
    /// Compute local time step: localDt = baseDt * lapse[i]
    /// </summary>
    public void ComputeLocalTimeStep(double baseDt)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        // Copy lapse to a temp ReadOnly view
        var lapseData = new double[_nodeCount];
        _lapseBuffer!.CopyTo(lapseData);
        using var lapseReadOnly = _device.AllocateReadOnlyBuffer(lapseData);

        _device.For(_nodeCount, new LocalTimeStepKernelDouble(
            lapseReadOnly,
            _localDtBuffer!,
            baseDt,
            _minDt,
            _maxDt
        ));
    }

    /// <summary>
    /// Compute entanglement entropy from edge weights.
    /// </summary>
    public void ComputeEntropy()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _device.For(_nodeCount, new ComputeEntropyKernelDouble(
            _csrOffsetsBuffer!,
            _csrNeighborsBuffer!,
            _csrWeightsBuffer!,
            _entropyBuffer!,
            _nodeCount
        ));
    }

    /// <summary>
    /// Compute entropic lapse function: N = exp(-? * S)
    /// </summary>
    public void ComputeLapseFromEntropy()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        // Copy entropy to a temp ReadOnly view
        var entropyData = new double[_nodeCount];
        _entropyBuffer!.CopyTo(entropyData);
        using var entropyReadOnly = _device.AllocateReadOnlyBuffer(entropyData);

        _device.For(_nodeCount, new EntropicLapseKernelDouble(
            entropyReadOnly,
            _lapseBuffer!,
            _timeDilationAlpha,
            _minLapse,
            _maxLapse
        ));
    }

    /// <summary>
    /// Full relational time step: compute Ricci ? lapse ? local dt ? accumulate clocks
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 1: Complete Page-Wootters implementation.
    /// </summary>
    public void ComputeRelationalTimeStep(double baseDt)
    {
        ComputeRicciScalar();
        ComputeLapseFromRicci();
        ComputeLocalTimeStep(baseDt);
        AccumulateNodeClocks();  // RQ-HYPOTHESIS: Page-Wootters mechanism
    }
    
    /// <summary>
    /// RQ-HYPOTHESIS CHECKLIST ITEM 1: Page-Wootters Proper Time Accumulation.
    /// 
    /// Accumulates proper time for each node: ?_i += localDt_i
    /// This implements relational time: no global clock exists.
    /// </summary>
    public void AccumulateNodeClocks()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        // Copy localDt to a temp ReadOnly view
        var localDtData = new double[_nodeCount];
        _localDtBuffer!.CopyTo(localDtData);
        using var localDtReadOnly = _device.AllocateReadOnlyBuffer(localDtData);

        _device.For(_nodeCount, new AccumulateNodeClocksKernelDouble(
            localDtReadOnly,
            _nodeClocksBuffer!
        ));
    }

    public void DownloadLapse(double[] lapse)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _lapseBuffer!.CopyTo(lapse);
    }

    public void DownloadLocalDt(double[] localDt)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _localDtBuffer!.CopyTo(localDt);
    }

    public void DownloadRicciScalar(double[] ricciScalar)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _ricciScalarBuffer!.CopyTo(ricciScalar);
    }

    public void DownloadEntropy(double[] entropy)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _entropyBuffer!.CopyTo(entropy);
    }
    
    /// <summary>
    /// Download node proper times (Page-Wootters clocks).
    /// </summary>
    public void DownloadNodeClocks(double[] nodeClocks)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        _nodeClocksBuffer!.CopyTo(nodeClocks);
    }
    
    /// <summary>
    /// Reset node clocks to zero.
    /// </summary>
    public void ResetNodeClocks()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");

        var zeros = new double[_nodeCount];
        _nodeClocksBuffer!.CopyFrom(zeros);
    }

    private void DisposeBuffers()
    {
        _lapseBuffer?.Dispose();
        _localDtBuffer?.Dispose();
        _ricciScalarBuffer?.Dispose();
        _entropyBuffer?.Dispose();
        _nodeClocksBuffer?.Dispose();
        _edgeCurvaturesBuffer?.Dispose();
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
