using System;
using System.Numerics;
using ComputeSharp;

namespace RQSimulation.GPUOptimized.CayleyEvolution
{
    /// <summary>
    /// Double-precision GPU-accelerated Cayley-form unitary evolution engine.
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 1: Unitary Quantum Evolution
    /// ==========================================================
    /// This is the DOUBLE PRECISION version required for:
    /// - Exact unitarity preservation (||?|| = const to machine precision)
    /// - BiCGStab convergence (avoids float32 rounding errors)
    /// - Long-term phase coherence (critical for interference)
    /// 
    /// PHYSICS: Cayley transform U = (1 - iH·dt/2)(1 + iH·dt/2)^-1
    /// preserves unitarity exactly because:
    /// - U†U = [(1 + iH†dt/2)(1 - iH†dt/2)^-1][(1 - iHdt/2)(1 + iHdt/2)^-1]
    /// - For Hermitian H (H† = H): U†U = I
    /// 
    /// GPU REQUIREMENT: Shader Model 6.0+ with double precision support.
    /// Falls back to CPU if double precision not available.
    /// </summary>
    public class GpuCayleyEvolutionEngineDouble : IDisposable
    {
        private readonly GraphicsDevice _device;
        private readonly bool _doubleSupported;
        
        // Wavefunction buffers (complex = Double2)
        private ReadWriteBuffer<Double2>? _psiBuffer;
        private ReadWriteBuffer<Double2>? _psiNewBuffer;
        private ReadWriteBuffer<Double2>? _rhsBuffer;
        
        // BiCGStab work buffers
        private ReadWriteBuffer<Double2>? _rBuffer;
        private ReadWriteBuffer<Double2>? _rHatBuffer;
        private ReadWriteBuffer<Double2>? _pBuffer;
        private ReadWriteBuffer<Double2>? _vBuffer;
        private ReadWriteBuffer<Double2>? _sBuffer;
        private ReadWriteBuffer<Double2>? _tBuffer;
        
        // Hamiltonian in CSR format (double precision)
        private ReadOnlyBuffer<int>? _csrOffsetsBuffer;
        private ReadOnlyBuffer<int>? _csrColumnsBuffer;
        private ReadOnlyBuffer<double>? _csrValuesBuffer;
        private ReadOnlyBuffer<double>? _potentialBuffer;
        
        // Reduction buffers
        private ReadWriteBuffer<Double2>? _partialSumsBuffer;
        private ReadWriteBuffer<double>? _normBuffer;
        
        private int _dim;
        private int _nodeCount;
        private int _gaugeDim;
        private int _nnz;
        private bool _initialized;
        
        // Solver parameters
        private const int MaxIterations = 100;
        private const double Tolerance = 1e-12; // Tighter tolerance for double
        
        /// <summary>
        /// Check if GPU supports double precision.
        /// </summary>
        public bool IsDoublePrecisionSupported => _doubleSupported;
        
        public GpuCayleyEvolutionEngineDouble()
        {
            _device = GraphicsDevice.GetDefault();
            _doubleSupported = _device.IsDoublePrecisionSupportAvailable();
            
            if (!_doubleSupported)
            {
                Console.WriteLine("WARNING: GPU does not support double precision.");
                Console.WriteLine("Cayley evolution will fall back to CPU or use reduced precision.");
            }
        }
        
        /// <summary>
        /// Initialize buffers for quantum evolution.
        /// </summary>
        public void Initialize(int nodeCount, int gaugeDim, int nnz)
        {
            if (!_doubleSupported)
            {
                throw new NotSupportedException(
                    "GPU does not support double precision. " +
                    "Use GpuCayleyEvolutionEngine (float) or CPU fallback.");
            }
            
            _nodeCount = nodeCount;
            _gaugeDim = gaugeDim;
            _dim = nodeCount * gaugeDim;
            _nnz = nnz;
            
            DisposeBuffers();
            
            // Wavefunction buffers
            _psiBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
            _psiNewBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
            _rhsBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
            
            // BiCGStab work buffers
            _rBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
            _rHatBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
            _pBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
            _vBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
            _sBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
            _tBuffer = _device.AllocateReadWriteBuffer<Double2>(_dim);
            
            // Hamiltonian buffers
            _csrOffsetsBuffer = _device.AllocateReadOnlyBuffer<int>(nodeCount + 1);
            _csrColumnsBuffer = _device.AllocateReadOnlyBuffer<int>(nnz);
            _csrValuesBuffer = _device.AllocateReadOnlyBuffer<double>(nnz);
            _potentialBuffer = _device.AllocateReadOnlyBuffer<double>(nodeCount);
            
            // Reduction buffers
            int numBlocks = (_dim + 255) / 256;
            _partialSumsBuffer = _device.AllocateReadWriteBuffer<Double2>(numBlocks);
            _normBuffer = _device.AllocateReadWriteBuffer<double>(numBlocks);
            
            _initialized = true;
        }
        
        /// <summary>
        /// Upload Hamiltonian matrix in CSR format (double precision).
        /// </summary>
        public void UploadHamiltonian(
            int[] csrOffsets,
            int[] csrColumns,
            double[] csrValues,
            double[] potential)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            _csrOffsetsBuffer!.CopyFrom(csrOffsets);
            _csrColumnsBuffer!.CopyFrom(csrColumns);
            _csrValuesBuffer!.CopyFrom(csrValues);
            _potentialBuffer!.CopyFrom(potential);
        }
        
        /// <summary>
        /// Perform one Cayley-form unitary evolution step.
        /// Returns number of BiCGStab iterations.
        /// </summary>
        public int EvolveUnitary(double[] psiReal, double[] psiImag, double dt)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            double alpha = dt * 0.5;
            
            // Pack complex wavefunction
            var psiPacked = new Double2[_dim];
            for (int i = 0; i < _dim; i++)
                psiPacked[i] = new Double2(psiReal[i], psiImag[i]);
            _psiBuffer!.CopyFrom(psiPacked);
            
            // Step 1: Compute RHS = (I - i·?·H)·?
            ComputeRightHandSide(alpha);
            
            // Step 2: Solve (I + i·?·H)·?_new = RHS using BiCGStab
            int iterations = SolveBiCGStab(alpha);
            
            // Step 3: Copy result back
            var result = new Double2[_dim];
            _psiNewBuffer!.CopyTo(result);
            
            for (int i = 0; i < _dim; i++)
            {
                psiReal[i] = result[i].X;
                psiImag[i] = result[i].Y;
            }
            
            return iterations;
        }
        
        private void ComputeRightHandSide(double alpha)
        {
            _device.For(_dim, new ComputeRhsKernelDouble(
                _psiBuffer!,
                _rhsBuffer!,
                _csrOffsetsBuffer!,
                _csrColumnsBuffer!,
                _csrValuesBuffer!,
                _potentialBuffer!,
                alpha,
                _nodeCount,
                _gaugeDim
            ));
        }
        
        private int SolveBiCGStab(double alpha)
        {
            // Initialize: x = b
            _device.For(_dim, new CopyKernelDouble(_rhsBuffer!, _psiNewBuffer!));
            
            // r = b - A·x
            _device.For(_dim, new ComputeResidualKernelDouble(
                _psiNewBuffer!,
                _rhsBuffer!,
                _rBuffer!,
                _csrOffsetsBuffer!,
                _csrColumnsBuffer!,
                _csrValuesBuffer!,
                _potentialBuffer!,
                alpha,
                _nodeCount,
                _gaugeDim
            ));
            
            double rNorm = ComputeNorm(_rBuffer!);
            if (rNorm < Tolerance)
                return 0;
            
            // r? = r
            _device.For(_dim, new CopyKernelDouble(_rBuffer!, _rHatBuffer!));
            
            // p = r
            _device.For(_dim, new CopyKernelDouble(_rBuffer!, _pBuffer!));
            
            Double2 rho = new Double2(1, 0);
            Double2 alpha_cg = new Double2(1, 0);
            Double2 omega = new Double2(1, 0);
            
            for (int iter = 0; iter < MaxIterations; iter++)
            {
                Double2 rhoNew = ComputeInnerProduct(_rHatBuffer!, _rBuffer!);
                
                if (rhoNew.X * rhoNew.X + rhoNew.Y * rhoNew.Y < 1e-30)
                    break;
                
                if (iter > 0)
                {
                    Double2 beta = ComplexDiv(ComplexMul(rhoNew, alpha_cg), ComplexMul(rho, omega));
                    _device.For(_dim, new UpdatePKernelDouble(_rBuffer!, _pBuffer!, _vBuffer!, beta, omega));
                }
                
                rho = rhoNew;
                
                // v = A·p
                _device.For(_dim, new SpMVKernelDouble(
                    _pBuffer!,
                    _vBuffer!,
                    _csrOffsetsBuffer!,
                    _csrColumnsBuffer!,
                    _csrValuesBuffer!,
                    _potentialBuffer!,
                    alpha,
                    _nodeCount,
                    _gaugeDim
                ));
                
                Double2 rHatV = ComputeInnerProduct(_rHatBuffer!, _vBuffer!);
                if (rHatV.X * rHatV.X + rHatV.Y * rHatV.Y < 1e-30)
                    break;
                alpha_cg = ComplexDiv(rho, rHatV);
                
                // s = r - alpha_cg · v
                _device.For(_dim, new AxpyKernelDouble(_rBuffer!, _vBuffer!, _sBuffer!,
                    new Double2(-alpha_cg.X, -alpha_cg.Y)));
                
                double sNorm = ComputeNorm(_sBuffer!);
                if (sNorm < Tolerance)
                {
                    _device.For(_dim, new AxpyInPlaceKernelDouble(_psiNewBuffer!, _pBuffer!, alpha_cg));
                    return iter + 1;
                }
                
                // t = A·s
                _device.For(_dim, new SpMVKernelDouble(
                    _sBuffer!,
                    _tBuffer!,
                    _csrOffsetsBuffer!,
                    _csrColumnsBuffer!,
                    _csrValuesBuffer!,
                    _potentialBuffer!,
                    alpha,
                    _nodeCount,
                    _gaugeDim
                ));
                
                Double2 tDotS = ComputeInnerProduct(_tBuffer!, _sBuffer!);
                Double2 tDotT = ComputeInnerProduct(_tBuffer!, _tBuffer!);
                if (tDotT.X * tDotT.X + tDotT.Y * tDotT.Y < 1e-30)
                    break;
                omega = ComplexDiv(tDotS, tDotT);
                
                // x = x + alpha_cg · p + omega · s
                _device.For(_dim, new UpdateXKernelDouble(_psiNewBuffer!, _pBuffer!, _sBuffer!, alpha_cg, omega));
                
                // r = s - omega · t
                _device.For(_dim, new AxpyKernelDouble(_sBuffer!, _tBuffer!, _rBuffer!,
                    new Double2(-omega.X, -omega.Y)));
                
                rNorm = ComputeNorm(_rBuffer!);
                if (rNorm < Tolerance)
                    return iter + 1;
                
                if (omega.X * omega.X + omega.Y * omega.Y < 1e-30)
                    break;
            }
            
            return MaxIterations;
        }
        
        private double ComputeNorm(ReadWriteBuffer<Double2> v)
        {
            int numBlocks = (_dim + 255) / 256;
            _device.For(_dim, new SquaredNormKernelDouble(v, _normBuffer!, _dim));
            
            var partialNorms = new double[numBlocks];
            _normBuffer!.CopyTo(partialNorms);
            
            double sum = 0;
            for (int i = 0; i < numBlocks; i++)
                sum += partialNorms[i];
            
            return Math.Sqrt(sum);
        }
        
        private Double2 ComputeInnerProduct(ReadWriteBuffer<Double2> a, ReadWriteBuffer<Double2> b)
        {
            int numBlocks = (_dim + 255) / 256;
            _device.For(_dim, new InnerProductKernelDouble(a, b, _partialSumsBuffer!, _dim));
            
            var partialSums = new Double2[numBlocks];
            _partialSumsBuffer!.CopyTo(partialSums);
            
            Double2 sum = new Double2(0, 0);
            for (int i = 0; i < numBlocks; i++)
            {
                sum.X += partialSums[i].X;
                sum.Y += partialSums[i].Y;
            }
            
            return sum;
        }
        
        private static Double2 ComplexMul(Double2 a, Double2 b)
            => new Double2(a.X * b.X - a.Y * b.Y, a.X * b.Y + a.Y * b.X);
        
        private static Double2 ComplexDiv(Double2 a, Double2 b)
        {
            double denom = b.X * b.X + b.Y * b.Y;
            return new Double2(
                (a.X * b.X + a.Y * b.Y) / denom,
                (a.Y * b.X - a.X * b.Y) / denom
            );
        }
        
        private void DisposeBuffers()
        {
            _psiBuffer?.Dispose();
            _psiNewBuffer?.Dispose();
            _rhsBuffer?.Dispose();
            _rBuffer?.Dispose();
            _rHatBuffer?.Dispose();
            _pBuffer?.Dispose();
            _vBuffer?.Dispose();
            _sBuffer?.Dispose();
            _tBuffer?.Dispose();
            _csrOffsetsBuffer?.Dispose();
            _csrColumnsBuffer?.Dispose();
            _csrValuesBuffer?.Dispose();
            _potentialBuffer?.Dispose();
            _partialSumsBuffer?.Dispose();
            _normBuffer?.Dispose();
        }
        
        public void Dispose()
        {
            DisposeBuffers();
            GC.SuppressFinalize(this);
        }
    }
}
