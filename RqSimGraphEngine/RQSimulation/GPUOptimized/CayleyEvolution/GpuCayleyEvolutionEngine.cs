using System;
using System.Numerics;
using ComputeSharp;

namespace RQSimulation.GPUOptimized.CayleyEvolution
{
    /// <summary>
    /// GPU-accelerated Cayley-form unitary evolution engine.
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 1: Unitary Quantum Evolution
    /// ==========================================================
    /// Implements the Cayley transform: U = (1 - iH·dt/2)(1 + iH·dt/2)^-1
    /// 
    /// This requires solving: (I + iH·dt/2) · ?_new = (I - iH·dt/2) · ?_old
    /// 
    /// GPU OPTIMIZATION:
    /// - BiCGStab iterations are dominated by SpMV (sparse matrix-vector multiply)
    /// - Each SpMV is O(E) where E = number of edges = O(N·k) for k-regular graphs
    /// - GPU parallelization gives O(N) with massive parallel speedup
    /// - Inner products and vector operations also parallelized
    /// 
    /// PHYSICS:
    /// - Cayley form preserves unitarity exactly (||?|| = const)
    /// - No forced normalization needed
    /// - Critical for preserving relational phase information
    /// </summary>
    public class GpuCayleyEvolutionEngine : IDisposable
    {
        private readonly GraphicsDevice _device;
        
        // Wavefunction buffers (complex = 2 floats per component)
        private ReadWriteBuffer<Float2>? _psiBuffer;      // Current ?
        private ReadWriteBuffer<Float2>? _psiNewBuffer;   // Solution ?_new
        private ReadWriteBuffer<Float2>? _rhsBuffer;      // Right-hand side b = (I - iHdt/2)?
        
        // BiCGStab work buffers
        private ReadWriteBuffer<Float2>? _rBuffer;        // Residual r
        private ReadWriteBuffer<Float2>? _rHatBuffer;     // Shadow residual r?
        private ReadWriteBuffer<Float2>? _pBuffer;        // Search direction p
        private ReadWriteBuffer<Float2>? _vBuffer;        // v = A·p
        private ReadWriteBuffer<Float2>? _sBuffer;        // s = r - ?·v
        private ReadWriteBuffer<Float2>? _tBuffer;        // t = A·s
        
        // Hamiltonian (graph Laplacian + potential) in CSR format
        private ReadOnlyBuffer<int>? _csrOffsetsBuffer;    // Row offsets [N+1]
        private ReadOnlyBuffer<int>? _csrColumnsBuffer;    // Column indices [nnz]
        private ReadOnlyBuffer<float>? _csrValuesBuffer;   // Matrix values [nnz]
        private ReadOnlyBuffer<float>? _potentialBuffer;   // Diagonal potential V_i
        
        // Reduction buffers for inner products
        private ReadWriteBuffer<Float2>? _partialSumsBuffer;
        private ReadWriteBuffer<float>? _normBuffer;
        
        private int _dim;              // Total dimension = N ? GaugeDimension
        private int _nodeCount;
        private int _gaugeDim;
        private int _nnz;              // Number of non-zeros in Hamiltonian
        private bool _initialized;
        
        // Solver parameters
        private const int MaxIterations = 100;
        private const float Tolerance = 1e-8f;
        
        public GpuCayleyEvolutionEngine()
        {
            _device = GraphicsDevice.GetDefault();
        }
        
        /// <summary>
        /// Initialize buffers for quantum evolution.
        /// </summary>
        /// <param name="nodeCount">Number of graph nodes</param>
        /// <param name="gaugeDim">Gauge dimension (color components)</param>
        /// <param name="nnz">Number of non-zeros in sparse Hamiltonian</param>
        public void Initialize(int nodeCount, int gaugeDim, int nnz)
        {
            _nodeCount = nodeCount;
            _gaugeDim = gaugeDim;
            _dim = nodeCount * gaugeDim;
            _nnz = nnz;
            
            // Dispose old buffers
            DisposeBuffers();
            
            // Allocate wavefunction buffers
            _psiBuffer = _device.AllocateReadWriteBuffer<Float2>(_dim);
            _psiNewBuffer = _device.AllocateReadWriteBuffer<Float2>(_dim);
            _rhsBuffer = _device.AllocateReadWriteBuffer<Float2>(_dim);
            
            // BiCGStab work buffers
            _rBuffer = _device.AllocateReadWriteBuffer<Float2>(_dim);
            _rHatBuffer = _device.AllocateReadWriteBuffer<Float2>(_dim);
            _pBuffer = _device.AllocateReadWriteBuffer<Float2>(_dim);
            _vBuffer = _device.AllocateReadWriteBuffer<Float2>(_dim);
            _sBuffer = _device.AllocateReadWriteBuffer<Float2>(_dim);
            _tBuffer = _device.AllocateReadWriteBuffer<Float2>(_dim);
            
            // Hamiltonian buffers
            _csrOffsetsBuffer = _device.AllocateReadOnlyBuffer<int>(nodeCount + 1);
            _csrColumnsBuffer = _device.AllocateReadOnlyBuffer<int>(nnz);
            _csrValuesBuffer = _device.AllocateReadOnlyBuffer<float>(nnz);
            _potentialBuffer = _device.AllocateReadOnlyBuffer<float>(nodeCount);
            
            // Reduction buffers (one per warp, assuming 256 threads per block)
            int numBlocks = (_dim + 255) / 256;
            _partialSumsBuffer = _device.AllocateReadWriteBuffer<Float2>(numBlocks);
            _normBuffer = _device.AllocateReadWriteBuffer<float>(numBlocks);
            
            _initialized = true;
        }
        
        /// <summary>
        /// Upload Hamiltonian matrix in CSR format.
        /// H = Graph Laplacian + diagonal potential
        /// </summary>
        public void UploadHamiltonian(
            int[] csrOffsets, 
            int[] csrColumns, 
            float[] csrValues,
            float[] potential)
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
        /// Solves: (I + i·alpha·H) · ?_new = (I - i·alpha·H) · ?_old
        /// where alpha = dt/2
        /// </summary>
        /// <param name="psiReal">Real parts of wavefunction (in/out)</param>
        /// <param name="psiImag">Imaginary parts of wavefunction (in/out)</param>
        /// <param name="dt">Time step</param>
        /// <returns>Number of BiCGStab iterations used</returns>
        public int EvolveUnitary(float[] psiReal, float[] psiImag, float dt)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
                
            float alpha = dt * 0.5f;
            
            // Pack complex wavefunction into Float2 buffer
            var psiPacked = new Float2[_dim];
            for (int i = 0; i < _dim; i++)
                psiPacked[i] = new Float2(psiReal[i], psiImag[i]);
            _psiBuffer!.CopyFrom(psiPacked);
            
            // Step 1: Compute RHS = (I - i·alpha·H) · ?
            ComputeRightHandSide(alpha);
            
            // Step 2: Solve (I + i·alpha·H) · ?_new = RHS using BiCGStab
            int iterations = SolveBiCGStab(alpha);
            
            // Step 3: Copy result back to host
            var result = new Float2[_dim];
            _psiNewBuffer!.CopyTo(result);
            
            for (int i = 0; i < _dim; i++)
            {
                psiReal[i] = result[i].X;
                psiImag[i] = result[i].Y;
            }
            
            return iterations;
        }
        
        /// <summary>
        /// Compute b = (I - i·alpha·H) · ? on GPU.
        /// </summary>
        private void ComputeRightHandSide(float alpha)
        {
            // Launch kernel: b_i = ?_i - i·alpha·(H·?)_i
            _device.For(_dim, new ComputeRhsKernel(
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
        
        /// <summary>
        /// Solve (I + i·alpha·H) · x = b using BiCGStab.
        /// </summary>
        private int SolveBiCGStab(float alpha)
        {
            // Initialize: x = b (initial guess)
            _device.For(_dim, new CopyKernel(_rhsBuffer!, _psiNewBuffer!));
            
            // r = b - A·x (initially r = b - A·b)
            _device.For(_dim, new ComputeResidualKernel(
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
            
            // Check initial residual
            float rNorm = ComputeNorm(_rBuffer!);
            if (rNorm < Tolerance)
                return 0;
            
            // r? = r
            _device.For(_dim, new CopyKernel(_rBuffer!, _rHatBuffer!));
            
            // p = r
            _device.For(_dim, new CopyKernel(_rBuffer!, _pBuffer!));
            
            Float2 rho = new Float2(1, 0);
            Float2 alpha_cg = new Float2(1, 0);
            Float2 omega = new Float2(1, 0);
            
            for (int iter = 0; iter < MaxIterations; iter++)
            {
                // rho_new = <r?, r>
                Float2 rhoNew = ComputeInnerProduct(_rHatBuffer!, _rBuffer!);
                
                if (rhoNew.X * rhoNew.X + rhoNew.Y * rhoNew.Y < 1e-30f)
                    break; // Breakdown
                
                if (iter > 0)
                {
                    // beta = (rho_new / rho) * (alpha_cg / omega)
                    Float2 beta = ComplexDiv(ComplexMul(rhoNew, alpha_cg), ComplexMul(rho, omega));
                    
                    // p = r + beta * (p - omega * v)
                    _device.For(_dim, new UpdatePKernel(_rBuffer!, _pBuffer!, _vBuffer!, beta, omega));
                }
                
                rho = rhoNew;
                
                // v = A·p
                _device.For(_dim, new SpMVKernel(
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
                
                // alpha_cg = rho / <r?, v>
                Float2 rHatV = ComputeInnerProduct(_rHatBuffer!, _vBuffer!);
                if (rHatV.X * rHatV.X + rHatV.Y * rHatV.Y < 1e-30f)
                    break;
                alpha_cg = ComplexDiv(rho, rHatV);
                
                // s = r - alpha_cg * v
                _device.For(_dim, new AxpyKernel(_rBuffer!, _vBuffer!, _sBuffer!, 
                    new Float2(-alpha_cg.X, -alpha_cg.Y)));
                
                // Check for early convergence
                float sNorm = ComputeNorm(_sBuffer!);
                if (sNorm < Tolerance)
                {
                    // x = x + alpha_cg * p
                    _device.For(_dim, new AxpyInPlaceKernel(_psiNewBuffer!, _pBuffer!, alpha_cg));
                    return iter + 1;
                }
                
                // t = A·s
                _device.For(_dim, new SpMVKernel(
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
                
                // omega = <t, s> / <t, t>
                Float2 tDotS = ComputeInnerProduct(_tBuffer!, _sBuffer!);
                Float2 tDotT = ComputeInnerProduct(_tBuffer!, _tBuffer!);
                if (tDotT.X * tDotT.X + tDotT.Y * tDotT.Y < 1e-30f)
                    break;
                omega = ComplexDiv(tDotS, tDotT);
                
                // x = x + alpha_cg * p + omega * s
                _device.For(_dim, new UpdateXKernel(_psiNewBuffer!, _pBuffer!, _sBuffer!, alpha_cg, omega));
                
                // r = s - omega * t
                _device.For(_dim, new AxpyKernel(_sBuffer!, _tBuffer!, _rBuffer!, 
                    new Float2(-omega.X, -omega.Y)));
                
                // Check convergence
                rNorm = ComputeNorm(_rBuffer!);
                if (rNorm < Tolerance)
                    return iter + 1;
                
                if (omega.X * omega.X + omega.Y * omega.Y < 1e-30f)
                    break;
            }
            
            return MaxIterations;
        }
        
        /// <summary>
        /// Compute ||v||_2 on GPU.
        /// </summary>
        private float ComputeNorm(ReadWriteBuffer<Float2> v)
        {
            // Parallel reduction for squared sum
            int numBlocks = (_dim + 255) / 256;
            _device.For(_dim, new SquaredNormKernel(v, _normBuffer!, _dim));
            
            // Final reduction on CPU (small)
            var partialNorms = new float[numBlocks];
            _normBuffer!.CopyTo(partialNorms);
            
            float sum = 0;
            for (int i = 0; i < numBlocks; i++)
                sum += partialNorms[i];
            
            return MathF.Sqrt(sum);
        }
        
        /// <summary>
        /// Compute <a, b> on GPU (complex inner product).
        /// </summary>
        private Float2 ComputeInnerProduct(ReadWriteBuffer<Float2> a, ReadWriteBuffer<Float2> b)
        {
            int numBlocks = (_dim + 255) / 256;
            _device.For(_dim, new InnerProductKernel(a, b, _partialSumsBuffer!, _dim));
            
            var partialSums = new Float2[numBlocks];
            _partialSumsBuffer!.CopyTo(partialSums);
            
            Float2 sum = new Float2(0, 0);
            for (int i = 0; i < numBlocks; i++)
            {
                sum.X += partialSums[i].X;
                sum.Y += partialSums[i].Y;
            }
            
            return sum;
        }
        
        // Complex multiplication: (a + bi)(c + di) = (ac - bd) + (ad + bc)i
        private static Float2 ComplexMul(Float2 a, Float2 b)
            => new Float2(a.X * b.X - a.Y * b.Y, a.X * b.Y + a.Y * b.X);
        
        // Complex division: (a + bi) / (c + di)
        private static Float2 ComplexDiv(Float2 a, Float2 b)
        {
            float denom = b.X * b.X + b.Y * b.Y;
            return new Float2(
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
