using System;
using ComputeSharp;

namespace RQSimulation.GPUOptimized
{
    /// <summary>
    /// GPU-accelerated scalar field engine with Higgs potential.
    /// Implements Klein-Gordon equation with Mexican hat potential:
    /// 
    ///   d??/dt? = D??? - V'(?)
    ///   V'(?) = ??? - ???  (Higgs potential gradient)
    /// 
    /// Uses symplectic leapfrog integration for energy conservation:
    ///   1. p += dt * F  (momentum update)
    ///   2. ? += dt * p  (position update)
    /// 
    /// This matches the CPU UpdateScalarFieldParallel implementation exactly.
    /// </summary>
    public class ScalarFieldEngine : IDisposable
    {
        private readonly GraphicsDevice _device;
        private ReadWriteBuffer<float>? _fieldBuffer;
        private ReadWriteBuffer<float>? _momentumBuffer;  // For symplectic integration

        // Topology in CSR format (for fast neighbor access)
        private ReadOnlyBuffer<int>? _adjOffsets;
        private ReadOnlyBuffer<int>? _adjNeighbors;
        private ReadOnlyBuffer<float>? _adjWeights;

        private int _nodeCount;
        private bool _topologyInitialized;

        public ScalarFieldEngine()
        {
            _device = GraphicsDevice.GetDefault();
        }

        /// <summary>
        /// Initialize buffers for a graph of given size.
        /// Call this before UpdateTopology and UpdateField.
        /// </summary>
        /// <param name="nodeCount">Number of nodes in the graph</param>
        /// <param name="totalEdges">Total number of directed edges (2 * undirected edges)</param>
        public void Initialize(int nodeCount, int totalEdges)
        {
            _nodeCount = nodeCount;

            // Dispose old buffers if reinitializing
            _fieldBuffer?.Dispose();
            _momentumBuffer?.Dispose();
            _adjOffsets?.Dispose();
            _adjNeighbors?.Dispose();
            _adjWeights?.Dispose();

            _fieldBuffer = _device.AllocateReadWriteBuffer<float>(nodeCount);
            _momentumBuffer = _device.AllocateReadWriteBuffer<float>(nodeCount);
            _adjOffsets = _device.AllocateReadOnlyBuffer<int>(nodeCount + 1);
            _adjNeighbors = _device.AllocateReadOnlyBuffer<int>(totalEdges);
            _adjWeights = _device.AllocateReadOnlyBuffer<float>(totalEdges);

            // Initialize momentum to zero
            float[] zeros = new float[nodeCount];
            _momentumBuffer.CopyFrom(zeros);
        }

        /// <summary>
        /// Update topology buffers (CSR format).
        /// Call this when graph structure changes.
        /// </summary>
        public void UpdateTopology(int[] offsets, int[] neighbors, float[] weights)
        {
            if (_adjOffsets == null || _adjNeighbors == null || _adjWeights == null)
            {
                throw new InvalidOperationException(
                    "Buffers not initialized. Call Initialize() first.");
            }

            _adjOffsets.CopyFrom(offsets);
            _adjNeighbors.CopyFrom(neighbors);
            _adjWeights.CopyFrom(weights);
            _topologyInitialized = true;
        }

        /// <summary>
        /// Evolve scalar field by one timestep using GPU with Higgs potential.
        /// Uses symplectic leapfrog integration matching CPU UpdateScalarFieldParallel.
        /// </summary>
        /// <param name="hostField">Field values at each node (updated in-place)</param>
        /// <param name="dt">Time step</param>
        /// <param name="diffusionRate">Diffusion coefficient D (default from PhysicsConstants)</param>
        /// <param name="higgsLambda">Higgs ? parameter (default from PhysicsConstants)</param>
        /// <param name="higgsMuSquared">Higgs ?? parameter (default from PhysicsConstants)</param>
        public void UpdateField(float[] hostField, float dt, 
            float diffusionRate = 0.1f,
            float higgsLambda = 0.1f,
            float higgsMuSquared = 0.01f)
        {
            if (!_topologyInitialized || _fieldBuffer == null || _momentumBuffer == null ||
                _adjOffsets == null || _adjNeighbors == null || _adjWeights == null)
            {
                throw new InvalidOperationException(
                    "Engine not properly initialized. Call Initialize() and UpdateTopology() first.");
            }

            if (hostField.Length != _nodeCount)
            {
                throw new ArgumentException(
                    $"Field array length ({hostField.Length}) doesn't match node count ({_nodeCount})");
            }

            // Upload field to GPU
            _fieldBuffer.CopyFrom(hostField);

            // Step 1: Update momentum (p += dt * F) where F = D??? - V'(?)
            var momentumShader = new HiggsMomentumShader(
                _fieldBuffer,
                _momentumBuffer,
                _adjOffsets,
                _adjNeighbors,
                _adjWeights,
                dt,
                diffusionRate,
                higgsLambda,
                higgsMuSquared,
                _nodeCount);

            _device.For(_nodeCount, momentumShader);

            // Step 2: Update field (? += dt * p)
            var fieldShader = new SymplecticFieldUpdateShader(_fieldBuffer, _momentumBuffer, dt, _nodeCount);
            _device.For(_nodeCount, fieldShader);

            // Download results
            _fieldBuffer.CopyTo(hostField);
        }

        /// <summary>
        /// Evolve field without copying back to CPU.
        /// Use SyncToHost() to retrieve results when needed.
        /// </summary>
        public void UpdateFieldNoCopy(float dt,
            float diffusionRate = 0.1f,
            float higgsLambda = 0.1f,
            float higgsMuSquared = 0.01f)
        {
            if (!_topologyInitialized || _fieldBuffer == null || _momentumBuffer == null ||
                _adjOffsets == null || _adjNeighbors == null || _adjWeights == null)
            {
                throw new InvalidOperationException(
                    "Engine not properly initialized. Call Initialize() and UpdateTopology() first.");
            }

            var momentumShader = new HiggsMomentumShader(
                _fieldBuffer,
                _momentumBuffer,
                _adjOffsets,
                _adjNeighbors,
                _adjWeights,
                dt,
                diffusionRate,
                higgsLambda,
                higgsMuSquared,
                _nodeCount);

            _device.For(_nodeCount, momentumShader);

            var fieldShader = new SymplecticFieldUpdateShader(_fieldBuffer, _momentumBuffer, dt, _nodeCount);
            _device.For(_nodeCount, fieldShader);
        }

        /// <summary>
        /// Upload initial field data to GPU.
        /// </summary>
        public void UploadField(float[] hostField)
        {
            if (_fieldBuffer == null)
            {
                throw new InvalidOperationException("Buffers not initialized.");
            }

            _fieldBuffer.CopyFrom(hostField);
        }

        /// <summary>
        /// Upload initial momentum data to GPU.
        /// </summary>
        public void UploadMomentum(float[] hostMomentum)
        {
            if (_momentumBuffer == null)
            {
                throw new InvalidOperationException("Buffers not initialized.");
            }

            _momentumBuffer.CopyFrom(hostMomentum);
        }

        /// <summary>
        /// Download field data from GPU.
        /// </summary>
        public void SyncToHost(float[] hostField)
        {
            if (_fieldBuffer == null)
            {
                throw new InvalidOperationException("Buffers not initialized.");
            }

            _fieldBuffer.CopyTo(hostField);
        }

        /// <summary>
        /// Download momentum data from GPU.
        /// </summary>
        public void SyncMomentumToHost(float[] hostMomentum)
        {
            if (_momentumBuffer == null)
            {
                throw new InvalidOperationException("Buffers not initialized.");
            }

            _momentumBuffer.CopyTo(hostMomentum);
        }

        public void Dispose()
        {
            _fieldBuffer?.Dispose();
            _momentumBuffer?.Dispose();
            _adjOffsets?.Dispose();
            _adjNeighbors?.Dispose();
            _adjWeights?.Dispose();
        }
    }

    /// <summary>
    /// GPU shader for computing Higgs force and updating momentum.
    /// Implements symplectic leapfrog step 1: p += dt * F
    /// 
    /// Force: F = D??? - V'(?)
    /// Discrete Laplacian: ???_i = ?_j w_ij * (?_j - ?_i)
    /// Higgs potential gradient: V'(?) = ??? - ???
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct HiggsMomentumShader : IComputeShader
    {
        public readonly ReadWriteBuffer<float> field;
        public readonly ReadWriteBuffer<float> momentum;
        public readonly ReadOnlyBuffer<int> offsets;
        public readonly ReadOnlyBuffer<int> neighbors;
        public readonly ReadOnlyBuffer<float> weights;
        public readonly float dt;
        public readonly float diffusion;
        public readonly float higgsLambda;
        public readonly float higgsMuSquared;
        public readonly int nodeCount;

        public HiggsMomentumShader(
            ReadWriteBuffer<float> field,
            ReadWriteBuffer<float> momentum,
            ReadOnlyBuffer<int> offsets,
            ReadOnlyBuffer<int> neighbors,
            ReadOnlyBuffer<float> weights,
            float dt,
            float diffusion,
            float higgsLambda,
            float higgsMuSquared,
            int nodeCount)
        {
            this.field = field;
            this.momentum = momentum;
            this.offsets = offsets;
            this.neighbors = neighbors;
            this.weights = weights;
            this.dt = dt;
            this.diffusion = diffusion;
            this.higgsLambda = higgsLambda;
            this.higgsMuSquared = higgsMuSquared;
            this.nodeCount = nodeCount;
        }

        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= nodeCount) return;

            float phi_i = field[i];

            // Compute discrete Laplacian: ?_j w_ij * (?_j - ?_i)
            float laplacian = 0.0f;
            int start = offsets[i];
            int end = offsets[i + 1];

            for (int k = start; k < end; k++)
            {
                int neighborIdx = neighbors[k];
                float w = weights[k];
                float phi_j = field[neighborIdx];
                laplacian += w * (phi_j - phi_i);
            }

            // Higgs potential gradient: V'(?) = ??? - ???
            float potentialGrad = higgsLambda * phi_i * phi_i * phi_i - higgsMuSquared * phi_i;

            // Total force: F = D??? - V'(?)
            float force = diffusion * laplacian - potentialGrad;

            // Symplectic update: p += dt * F
            momentum[i] += dt * force;
        }
    }

    /// <summary>
    /// GPU shader for symplectic field update: ? += dt * p
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct SymplecticFieldUpdateShader : IComputeShader
    {
        public readonly ReadWriteBuffer<float> field;
        public readonly ReadWriteBuffer<float> momentum;
        public readonly float dt;
        public readonly int nodeCount;

        public SymplecticFieldUpdateShader(
            ReadWriteBuffer<float> field,
            ReadWriteBuffer<float> momentum,
            float dt,
            int nodeCount)
        {
            this.field = field;
            this.momentum = momentum;
            this.dt = dt;
            this.nodeCount = nodeCount;
        }

        public void Execute()
        {
            int i = ThreadIds.X;
            if (i < nodeCount)
            {
                // Symplectic update: ? += dt * p
                field[i] += dt * momentum[i];
            }
        }
    }
}
