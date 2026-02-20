using System;
using ComputeSharp;
using RQSimulation.GPUCompressedSparseRow.Data;
using RQSimulation.GPUCompressedSparseRow.Shaders;

namespace RQSimulation.GPUCompressedSparseRow.Solvers;

/// <summary>
/// GPU-accelerated BiCGStab solver for Cayley evolution using CSR format.
/// 
/// ALGORITHM: BiCGStab (Biconjugate Gradient Stabilized)
/// Solves A*x = b where A = (1 + i*H*dt/2) for Cayley transform.
/// 
/// PHYSICS: Cayley evolution preserves unitarity exactly because
/// U = (1 - i*H*dt/2) * (1 + i*H*dt/2)^{-1} satisfies U†U = I for Hermitian H.
/// 
/// PRECISION: Double precision required for:
/// - Unitarity preservation to machine epsilon
/// - Convergence of BiCGStab iterations
/// - Long-term phase coherence
/// </summary>
public sealed class GpuBiCGStabSolverCsr : IDisposable
{
    private readonly GraphicsDevice _device;
    private CsrTopology? _topology;
    
    // BiCGStab work buffers
    private ReadWriteBuffer<Double2>? _rBuffer;
    private ReadWriteBuffer<Double2>? _rHatBuffer;
    private ReadWriteBuffer<Double2>? _pBuffer;
    private ReadWriteBuffer<Double2>? _vBuffer;
    private ReadWriteBuffer<Double2>? _sBuffer;
    private ReadWriteBuffer<Double2>? _tBuffer;
    private ReadWriteBuffer<Double2>? _rhsBuffer;
    
    // Reduction buffers
    private ReadWriteBuffer<Double2>? _complexPartialSums;
    private ReadWriteBuffer<double>? _realPartialSums;
    
    // Problem dimensions
    private int _dim;
    private int _nodeCount;
    private int _gaugeDim;
    private int _numBlocks;
    
    // Solver parameters
    private const int BlockSize = 256;
    private const int DefaultMaxIterations = 100;
    private const double DefaultTolerance = 1e-12;
    
    private bool _initialized;
    private bool _disposed;
    
    /// <summary>
    /// Maximum iterations before solver gives up.
    /// </summary>
    public int MaxIterations { get; set; } = DefaultMaxIterations;
    
    /// <summary>
    /// Convergence tolerance for residual norm.
    /// </summary>
    public double Tolerance { get; set; } = DefaultTolerance;
    
    /// <summary>
    /// Number of iterations in last solve.
    /// </summary>
    public int LastIterations { get; private set; }
    
    /// <summary>
    /// Final residual norm from last solve.
    /// </summary>
    public double LastResidual { get; private set; }
    
    /// <summary>
    /// Whether the solver has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    public GpuBiCGStabSolverCsr()
    {
        _device = GraphicsDevice.GetDefault();
        
        if (!_device.IsDoublePrecisionSupportAvailable())
        {
            throw new NotSupportedException(
                "GPU does not support double precision. " +
                "BiCGStab solver requires SM 6.0+ for accurate physics.");
        }
    }

    public GpuBiCGStabSolverCsr(GraphicsDevice device)
    {
        _device = device;
        
        if (!_device.IsDoublePrecisionSupportAvailable())
        {
            throw new NotSupportedException("GPU does not support double precision.");
        }
    }

    /// <summary>
    /// Initialize solver with CSR topology.
    /// </summary>
    /// <param name="topology">CSR graph topology (must be uploaded to GPU)</param>
    /// <param name="gaugeDim">Gauge dimension (components per node)</param>
    public void Initialize(CsrTopology topology, int gaugeDim = 1)
    {
        ArgumentNullException.ThrowIfNull(topology);
        
        if (!topology.IsGpuReady)
        {
            throw new InvalidOperationException("Topology must be uploaded to GPU first. Call topology.UploadToGpu().");
        }
        
        _topology = topology;
        _nodeCount = topology.NodeCount;
        _gaugeDim = gaugeDim;
        _dim = _nodeCount * _gaugeDim;
        
        DisposeBuffers();
        
        // Allocate work buffers
        _rBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
        _rHatBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
        _pBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
        _vBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
        _sBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
        _tBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
        _rhsBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
        
        // Reduction buffers
        _numBlocks = (_dim + BlockSize - 1) / BlockSize;
        _complexPartialSums = _device.AllocateReadWriteBuffer<Double2>(_numBlocks);
        _realPartialSums = _device.AllocateReadWriteBuffer<double>(_numBlocks);
        
        _initialized = true;
    }

    /// <summary>
    /// Solve (1 + i*?*H)*x = b for Cayley evolution.
    /// </summary>
    /// <param name="psiBuffer">Input/output wavefunction buffer</param>
    /// <param name="alpha">Cayley parameter (dt/2)</param>
    /// <returns>Number of iterations used</returns>
    public int Solve(ReadWriteBuffer<Double2> psiBuffer, double alpha)
    {
        if (!_initialized || _topology is null)
            throw new InvalidOperationException("Solver not initialized");
        
        // Compute RHS: b = (1 - i*?*H)*?
        ComputeRhs(psiBuffer, alpha);
        
        // Initial guess: x = ? (warm start)
        // Already in psiBuffer
        
        // Compute initial residual: r = b - A*x
        ComputeResidual(psiBuffer, alpha);
        
        // r? = r (shadow residual, stays constant)
        CopyVector(_rBuffer!, _rHatBuffer!);
        
        // p = r
        CopyVector(_rBuffer!, _pBuffer!);
        
        // Initial ? = ?r?, r?
        Double2 rhoOld = ComputeComplexDotProduct(_rHatBuffer!, _rBuffer!);
        double rhoMag = System.Math.Sqrt(rhoOld.X * rhoOld.X + rhoOld.Y * rhoOld.Y);
        
        // Check for early convergence
        double residualNorm = System.Math.Sqrt(ComputeSquaredNorm(_rBuffer!));
        if (residualNorm < Tolerance)
        {
            LastIterations = 0;
            LastResidual = residualNorm;
            return 0;
        }
        
        Double2 omega = new Double2(1, 0);
        
        for (int iter = 0; iter < MaxIterations; iter++)
        {
            // v = A*p
            ApplyCayleyOperator(_pBuffer!, _vBuffer!, alpha);
            
            // ?_iter = ? / ?r?, v?
            Double2 rHatV = ComputeComplexDotProduct(_rHatBuffer!, _vBuffer!);
            Double2 alphaIter = ComplexDiv(rhoOld, rHatV);
            
            // s = r - ?*v
            ComputeS(alphaIter);
            
            // Check for convergence on s
            double sNorm = System.Math.Sqrt(ComputeSquaredNorm(_sBuffer!));
            if (sNorm < Tolerance)
            {
                // x = x + ?*p (final update)
                UpdateXSimple(psiBuffer, _pBuffer!, alphaIter);
                LastIterations = iter + 1;
                LastResidual = sNorm;
                return iter + 1;
            }
            
            // t = A*s
            ApplyCayleyOperator(_sBuffer!, _tBuffer!, alpha);
            
            // ? = ?t, s? / ?t, t?
            Double2 tS = ComputeComplexDotProduct(_tBuffer!, _sBuffer!);
            Double2 tT = ComputeComplexDotProduct(_tBuffer!, _tBuffer!);
            omega = ComplexDiv(tS, tT);
            
            // x = x + ?*p + ?*s
            UpdateX(psiBuffer, alphaIter, omega);
            
            // r = s - ?*t
            UpdateR(omega);
            
            // Check convergence
            residualNorm = System.Math.Sqrt(ComputeSquaredNorm(_rBuffer!));
            if (residualNorm < Tolerance)
            {
                LastIterations = iter + 1;
                LastResidual = residualNorm;
                return iter + 1;
            }
            
            // ?_new = ?r?, r?
            Double2 rhoNew = ComputeComplexDotProduct(_rHatBuffer!, _rBuffer!);
            
            // ? = (?_new / ?_old) * (? / ?)
            Double2 beta = ComplexMul(ComplexDiv(rhoNew, rhoOld), ComplexDiv(alphaIter, omega));
            
            // p = r + ?*(p - ?*v)
            UpdateP(beta, omega);
            
            rhoOld = rhoNew;
        }
        
        // Did not converge
        LastIterations = MaxIterations;
        LastResidual = residualNorm;
        return MaxIterations;
    }

    private void ComputeRhs(ReadWriteBuffer<Double2> psiBuffer, double alpha)
    {
        var kernel = new CsrComputeRhsKernel(
            psiBuffer, _rhsBuffer!,
            _topology!.RowOffsetsBuffer, _topology.ColIndicesBuffer,
            _topology.EdgeWeightsBuffer, _topology.NodePotentialBuffer,
            alpha, _nodeCount, _gaugeDim);
        
        _device.For(_dim, kernel);
    }

    private void ComputeResidual(ReadWriteBuffer<Double2> xBuffer, double alpha)
    {
        var kernel = new CsrComputeResidualKernel(
            xBuffer, _rhsBuffer!, _rBuffer!,
            _topology!.RowOffsetsBuffer, _topology.ColIndicesBuffer,
            _topology.EdgeWeightsBuffer, _topology.NodePotentialBuffer,
            alpha, _nodeCount, _gaugeDim);
        
        _device.For(_dim, kernel);
    }

    private void ApplyCayleyOperator(ReadWriteBuffer<Double2> x, ReadWriteBuffer<Double2> y, double alpha)
    {
        var kernel = new CsrCayleySpMVKernel(
            x, y,
            _topology!.RowOffsetsBuffer, _topology.ColIndicesBuffer,
            _topology.EdgeWeightsBuffer, _topology.NodePotentialBuffer,
            alpha, _nodeCount, _gaugeDim);
        
        _device.For(_dim, kernel);
    }

    private void CopyVector(ReadWriteBuffer<Double2> src, ReadWriteBuffer<Double2> dst)
    {
        var kernel = new VectorCopyKernel(src, dst, _dim);
        _device.For(_dim, kernel);
    }

    private void ComputeS(Double2 alpha)
    {
        var kernel = new ComputeSKernel(_rBuffer!, _vBuffer!, _sBuffer!, alpha, _dim);
        _device.For(_dim, kernel);
    }

    private void UpdateX(ReadWriteBuffer<Double2> x, Double2 alpha, Double2 omega)
    {
        var kernel = new UpdateXKernel(x, _pBuffer!, _sBuffer!, alpha, omega, _dim);
        _device.For(_dim, kernel);
    }

    private void UpdateXSimple(ReadWriteBuffer<Double2> x, ReadWriteBuffer<Double2> p, Double2 alpha)
    {
        var kernel = new VectorAxpyInPlaceKernel(x, p, alpha, _dim);
        _device.For(_dim, kernel);
    }

    private void UpdateR(Double2 omega)
    {
        var kernel = new UpdateRKernel(_sBuffer!, _tBuffer!, _rBuffer!, omega, _dim);
        _device.For(_dim, kernel);
    }

    private void UpdateP(Double2 beta, Double2 omega)
    {
        var kernel = new UpdatePKernel(_rBuffer!, _pBuffer!, _vBuffer!, beta, omega, _dim);
        _device.For(_dim, kernel);
    }

    private Double2 ComputeComplexDotProduct(ReadWriteBuffer<Double2> a, ReadWriteBuffer<Double2> b)
    {
        var kernel = new ComplexDotProductKernel(a, b, _complexPartialSums!, _dim, BlockSize);
        _device.For(_numBlocks * BlockSize, kernel);
        
        // Download and sum partial results on CPU
        Span<Double2> partials = stackalloc Double2[_numBlocks];
        _complexPartialSums!.CopyTo(partials);
        
        Double2 sum = new Double2(0, 0);
        for (int i = 0; i < _numBlocks; i++)
        {
            sum.X += partials[i].X;
            sum.Y += partials[i].Y;
        }
        
        return sum;
    }

    private double ComputeSquaredNorm(ReadWriteBuffer<Double2> v)
    {
        var kernel = new SquaredNormKernel(v, _realPartialSums!, _dim, BlockSize);
        _device.For(_numBlocks * BlockSize, kernel);
        
        // Download and sum partial results on CPU
        Span<double> partials = stackalloc double[_numBlocks];
        _realPartialSums!.CopyTo(partials);
        
        double sum = 0;
        for (int i = 0; i < _numBlocks; i++)
        {
            sum += partials[i];
        }
        
        return sum;
    }

    // Complex arithmetic helpers
    private static Double2 ComplexMul(Double2 a, Double2 b)
    {
        return new Double2(
            a.X * b.X - a.Y * b.Y,
            a.X * b.Y + a.Y * b.X
        );
    }

    private static Double2 ComplexDiv(Double2 a, Double2 b)
    {
        double denom = b.X * b.X + b.Y * b.Y;
        if (denom < 1e-30)
        {
            return new Double2(0, 0); // Avoid division by zero
        }
        
        return new Double2(
            (a.X * b.X + a.Y * b.Y) / denom,
            (a.Y * b.X - a.X * b.Y) / denom
        );
    }

    private void DisposeBuffers()
    {
        _rBuffer?.Dispose();
        _rHatBuffer?.Dispose();
        _pBuffer?.Dispose();
        _vBuffer?.Dispose();
        _sBuffer?.Dispose();
        _tBuffer?.Dispose();
        _rhsBuffer?.Dispose();
        _complexPartialSums?.Dispose();
        _realPartialSums?.Dispose();
        
        _rBuffer = null;
        _rHatBuffer = null;
        _pBuffer = null;
        _vBuffer = null;
        _sBuffer = null;
        _tBuffer = null;
        _rhsBuffer = null;
        _complexPartialSums = null;
        _realPartialSums = null;
        
        _initialized = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        DisposeBuffers();
        _disposed = true;
    }
}
