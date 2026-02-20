using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Observer;

/// <summary>
/// CSR-optimized compute shaders for internal observer operations on large sparse graphs.
/// 
/// RQ-HYPOTHESIS STAGE 5: GPU INTERNAL OBSERVER (CSR VERSION)
/// ==========================================================
/// Same physics as GPUOptimized version but optimized for CSR sparse format.
/// For N &gt; 10? nodes, CSR format saves significant memory and bandwidth.
/// 
/// Memory comparison:
/// - Dense: O(N?) for adjacency matrix
/// - CSR: O(E) for edge list + O(N) for row pointers
/// 
/// PARALLELIZATION (same as dense version):
/// - Phase shifts: parallel over observer nodes
/// - Correlations: parallel over observer-target pairs
/// - Uses CSR row pointers for efficient neighbor access
/// 
/// All operations use double precision (64-bit).
/// </summary>

/// <summary>
/// CSR Phase shift kernel: applies exp(i * shift) using CSR neighbor access.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrPhaseShiftKernelDouble : IComputeShader
{
    /// <summary>Wavefunction (real, imag) per node * gauge dimension</summary>
    public readonly ReadWriteBuffer<Double2> wavefunction;
    
    /// <summary>Observer node indices</summary>
    public readonly ReadOnlyBuffer<int> observerNodes;
    
    /// <summary>Phase shifts per observer node</summary>
    public readonly ReadOnlyBuffer<double> phaseShifts;
    
    /// <summary>Gauge dimension</summary>
    public readonly int gaugeDim;
    
    /// <summary>Number of observer nodes</summary>
    public readonly int observerCount;
    
    public CsrPhaseShiftKernelDouble(
        ReadWriteBuffer<Double2> wavefunction,
        ReadOnlyBuffer<int> observerNodes,
        ReadOnlyBuffer<double> phaseShifts,
        int gaugeDim,
        int observerCount)
    {
        this.wavefunction = wavefunction;
        this.observerNodes = observerNodes;
        this.phaseShifts = phaseShifts;
        this.gaugeDim = gaugeDim;
        this.observerCount = observerCount;
    }
    
    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= observerCount) return;
        
        int node = observerNodes[idx];
        double shift = phaseShifts[idx];
        
        double cosS = Hlsl.Cos((float)shift);
        double sinS = Hlsl.Sin((float)shift);
        
        int baseIdx = node * gaugeDim;
        for (int a = 0; a < gaugeDim; a++)
        {
            Double2 psi = wavefunction[baseIdx + a];
            double newReal = psi.X * cosS - psi.Y * sinS;
            double newImag = psi.X * sinS + psi.Y * cosS;
            wavefunction[baseIdx + a] = new Double2(newReal, newImag);
        }
    }
}

/// <summary>
/// CSR Correlation kernel using CSR format for neighbor lookup.
/// Computes correlation between each observer node and its connected neighbors.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrCorrelationKernelDouble : IComputeShader
{
    /// <summary>Wavefunction (real, imag)</summary>
    public readonly ReadWriteBuffer<Double2> wavefunction;
    
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices (neighbors)</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>CSR edge weights</summary>
    public readonly ReadOnlyBuffer<double> edgeWeights;
    
    /// <summary>Observer node indices</summary>
    public readonly ReadOnlyBuffer<int> observerNodes;
    
    /// <summary>Output total correlation per observer node</summary>
    public readonly ReadWriteBuffer<double> correlations;
    
    /// <summary>Gauge dimension</summary>
    public readonly int gaugeDim;
    
    /// <summary>Number of observer nodes</summary>
    public readonly int observerCount;
    
    /// <summary>Minimum weight threshold</summary>
    public readonly double minWeight;
    
    public CsrCorrelationKernelDouble(
        ReadWriteBuffer<Double2> wavefunction,
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<int> observerNodes,
        ReadWriteBuffer<double> correlations,
        int gaugeDim,
        int observerCount,
        double minWeight)
    {
        this.wavefunction = wavefunction;
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.edgeWeights = edgeWeights;
        this.observerNodes = observerNodes;
        this.correlations = correlations;
        this.gaugeDim = gaugeDim;
        this.observerCount = observerCount;
        this.minWeight = minWeight;
    }
    
    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= observerCount) return;
        
        int obsNode = observerNodes[idx];
        int start = rowPtr[obsNode];
        int end = rowPtr[obsNode + 1];
        
        int obsBase = obsNode * gaugeDim;
        double totalCorr = 0.0;
        
        // Iterate over CSR neighbors
        for (int k = start; k < end; k++)
        {
            int tgtNode = colIdx[k];
            double w = edgeWeights[k];
            
            if (w < minWeight) continue;
            
            int tgtBase = tgtNode * gaugeDim;
            
            // Compute inner product
            double corrReal = 0.0;
            double corrImag = 0.0;
            
            for (int a = 0; a < gaugeDim; a++)
            {
                Double2 psiObs = wavefunction[obsBase + a];
                Double2 psiTgt = wavefunction[tgtBase + a];
                
                corrReal += psiObs.X * psiTgt.X + psiObs.Y * psiTgt.Y;
                corrImag += psiObs.X * psiTgt.Y - psiObs.Y * psiTgt.X;
            }
            
            double corrMag = Hlsl.Sqrt((float)(corrReal * corrReal + corrImag * corrImag));
            totalCorr += w * corrMag;
        }
        
        correlations[idx] = totalCorr;
    }
}

/// <summary>
/// CSR Controlled phase kernel: applies measurement-induced entanglement via CSR topology.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrControlledPhaseKernelDouble : IComputeShader
{
    /// <summary>Wavefunction (real, imag)</summary>
    public readonly ReadWriteBuffer<Double2> wavefunction;
    
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>CSR edge weights</summary>
    public readonly ReadOnlyBuffer<double> edgeWeights;
    
    /// <summary>Observer node indices</summary>
    public readonly ReadOnlyBuffer<int> observerNodes;
    
    /// <summary>Measurement coupling strength</summary>
    public readonly double coupling;
    
    /// <summary>Gauge dimension</summary>
    public readonly int gaugeDim;
    
    /// <summary>Number of observer nodes</summary>
    public readonly int observerCount;
    
    /// <summary>Minimum weight for interaction</summary>
    public readonly double minWeight;
    
    public CsrControlledPhaseKernelDouble(
        ReadWriteBuffer<Double2> wavefunction,
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<int> observerNodes,
        double coupling,
        int gaugeDim,
        int observerCount,
        double minWeight)
    {
        this.wavefunction = wavefunction;
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.edgeWeights = edgeWeights;
        this.observerNodes = observerNodes;
        this.coupling = coupling;
        this.gaugeDim = gaugeDim;
        this.observerCount = observerCount;
        this.minWeight = minWeight;
    }
    
    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= observerCount) return;
        
        int obsNode = observerNodes[idx];
        int start = rowPtr[obsNode];
        int end = rowPtr[obsNode + 1];
        
        int obsBase = obsNode * gaugeDim;
        
        // For each connected target, apply controlled phase to observer
        for (int k = start; k < end; k++)
        {
            int tgtNode = colIdx[k];
            double w = edgeWeights[k];
            
            if (w < minWeight) continue;
            
            // Get target phase
            int tgtBase = tgtNode * gaugeDim;
            double tgtPhase = 0.0;
            for (int a = 0; a < gaugeDim; a++)
            {
                Double2 psiTgt = wavefunction[tgtBase + a];
                tgtPhase += Hlsl.Atan2((float)psiTgt.Y, (float)psiTgt.X);
            }
            tgtPhase /= gaugeDim;
            
            // Apply controlled phase shift to observer
            double shift = coupling * w * tgtPhase;
            double cosS = Hlsl.Cos((float)shift);
            double sinS = Hlsl.Sin((float)shift);
            
            for (int a = 0; a < gaugeDim; a++)
            {
                Double2 psi = wavefunction[obsBase + a];
                double newReal = psi.X * cosS - psi.Y * sinS;
                double newImag = psi.X * sinS + psi.Y * cosS;
                wavefunction[obsBase + a] = new Double2(newReal, newImag);
            }
        }
    }
}

/// <summary>
/// CSR Probability density kernel.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrProbabilityDensityKernelDouble : IComputeShader
{
    /// <summary>Wavefunction</summary>
    public readonly ReadWriteBuffer<Double2> wavefunction;
    
    /// <summary>Output probability density</summary>
    public readonly ReadWriteBuffer<double> probDensity;
    
    /// <summary>Gauge dimension</summary>
    public readonly int gaugeDim;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrProbabilityDensityKernelDouble(
        ReadWriteBuffer<Double2> wavefunction,
        ReadWriteBuffer<double> probDensity,
        int gaugeDim,
        int nodeCount)
    {
        this.wavefunction = wavefunction;
        this.probDensity = probDensity;
        this.gaugeDim = gaugeDim;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int baseIdx = i * gaugeDim;
        double prob = 0.0;
        
        for (int a = 0; a < gaugeDim; a++)
        {
            Double2 psi = wavefunction[baseIdx + a];
            prob += psi.X * psi.X + psi.Y * psi.Y;
        }
        
        probDensity[i] = prob;
    }
}

/// <summary>
/// CSR Shannon entropy contribution kernel.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrEntropyContributionKernelDouble : IComputeShader
{
    /// <summary>Normalized probabilities</summary>
    public readonly ReadOnlyBuffer<double> probabilities;
    
    /// <summary>Output entropy contributions</summary>
    public readonly ReadWriteBuffer<double> entropyContribs;
    
    /// <summary>Count</summary>
    public readonly int count;
    
    /// <summary>Epsilon to avoid log(0)</summary>
    public readonly double epsilon;
    
    public CsrEntropyContributionKernelDouble(
        ReadOnlyBuffer<double> probabilities,
        ReadWriteBuffer<double> entropyContribs,
        int count,
        double epsilon)
    {
        this.probabilities = probabilities;
        this.entropyContribs = entropyContribs;
        this.count = count;
        this.epsilon = epsilon;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= count) return;
        
        double p = probabilities[i];
        
        if (p > epsilon)
        {
            entropyContribs[i] = -p * Hlsl.Log2((float)p);
        }
        else
        {
            entropyContribs[i] = 0.0;
        }
    }
}

/// <summary>
/// CSR Observer expectation value kernel.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrObserverExpectationKernelDouble : IComputeShader
{
    /// <summary>Probability density per node</summary>
    public readonly ReadWriteBuffer<double> probDensity;
    
    /// <summary>Observable values per node</summary>
    public readonly ReadOnlyBuffer<double> observable;
    
    /// <summary>Observer node indices</summary>
    public readonly ReadOnlyBuffer<int> observerNodes;
    
    /// <summary>Output contributions</summary>
    public readonly ReadWriteBuffer<double> contributions;
    
    /// <summary>Number of observer nodes</summary>
    public readonly int observerCount;
    
    public CsrObserverExpectationKernelDouble(
        ReadWriteBuffer<double> probDensity,
        ReadOnlyBuffer<double> observable,
        ReadOnlyBuffer<int> observerNodes,
        ReadWriteBuffer<double> contributions,
        int observerCount)
    {
        this.probDensity = probDensity;
        this.observable = observable;
        this.observerNodes = observerNodes;
        this.contributions = contributions;
        this.observerCount = observerCount;
    }
    
    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= observerCount) return;
        
        int node = observerNodes[idx];
        contributions[idx] = probDensity[node] * observable[node];
    }
}

/// <summary>
/// CSR Sum reduction kernel.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrSumReductionKernelDouble : IComputeShader
{
    /// <summary>Data buffer (modified in place)</summary>
    public readonly ReadWriteBuffer<double> data;
    
    /// <summary>Stride for this pass</summary>
    public readonly int stride;
    
    /// <summary>Total count</summary>
    public readonly int count;
    
    public CsrSumReductionKernelDouble(
        ReadWriteBuffer<double> data,
        int stride,
        int count)
    {
        this.data = data;
        this.stride = stride;
        this.count = count;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        int target = i * stride * 2;
        int source = target + stride;
        
        if (source < count)
        {
            data[target] = data[target] + data[source];
        }
    }
}
