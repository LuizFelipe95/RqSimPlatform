using System;
using System.Collections.Generic;
using ComputeSharp;

namespace RQSimulation.GPUOptimized
{
    // GPU configuration structure
    public struct GpuConfig
    {
        public int GpuIndex;
        public bool MultiGpu;
        public int ThreadBlockSize;
    }

    public partial class GpuGravityEngine : IDisposable
    {
        private readonly GraphicsDevice _device;

        // GPU buffers
        private ReadWriteBuffer<float> _weightsBuffer;
        private ReadWriteBuffer<float> _curvaturesBuffer; // Changed to ReadWrite for curvature shader output
        private ReadOnlyBuffer<float> _nodeMassesBuffer;
        private ReadOnlyBuffer<Int2> _edgeIndicesBuffer; // Edge pairs (NodeA, NodeB)

        // CSR adjacency buffers for curvature computation
        private ReadOnlyBuffer<int>? _adjOffsetsBuffer;
        private ReadOnlyBuffer<Int2>? _adjDataBuffer;
        private bool _topologyInitialized = false;
        private int _nodeCount;

        public GpuGravityEngine(GpuConfig config, int edgeCount, int nodeCount)
        {
            // 1. Select device (default for now, multi-GPU support prepared)
            _device = GraphicsDevice.GetDefault();
            if (config.MultiGpu && config.GpuIndex >= 0)
            {
                // Select specific GPU if needed
                // _device = GraphicsDevice.QueryDevices().ElementAt(config.GpuIndex);
            }

            _nodeCount = nodeCount;

            // 2. Allocate GPU buffers
            _weightsBuffer = _device.AllocateReadWriteBuffer<float>(edgeCount);
            _curvaturesBuffer = _device.AllocateReadWriteBuffer<float>(edgeCount); // ReadWrite for curvature shader
            _nodeMassesBuffer = _device.AllocateReadOnlyBuffer<float>(nodeCount);
            _edgeIndicesBuffer = _device.AllocateReadOnlyBuffer<Int2>(edgeCount);
        }

        /// <summary>
        /// Update topology buffers (CSR format) for GPU curvature computation.
        /// Call this when graph topology changes (edge add/remove).
        /// 
        /// IMPORTANT: This builds a directed adjacency list where each undirected edge
        /// appears twice (once for each direction). The edgeIndex stored is always
        /// the canonical index from FlatEdgesFrom (where i less than j).
        /// </summary>
        /// <param name="graph">The graph to build CSR from</param>
        public void UpdateTopologyBuffers(RQGraph graph)
        {
            int N = graph.N;
            int[] offsets = new int[N + 1];
            List<Int2> flatAdj = new List<Int2>();

            // Build CSR structure on CPU (done rarely, only on rewiring)
            // Each undirected edge (i,j) appears twice in adjacency lists:
            // - In node i's list: neighbor=j, edgeIndex=GetEdgeIndex(i,j)
            // - In node j's list: neighbor=i, edgeIndex=GetEdgeIndex(j,i) = same index
            int currentOffset = 0;
            for (int i = 0; i < N; i++)
            {
                offsets[i] = currentOffset;
                foreach (int neighbor in graph.Neighbors(i))
                {
                    // GetEdgeIndex normalizes to (min, max) internally, so it returns
                    // the same index regardless of argument order
                    int edgeIndex = graph.GetEdgeIndex(i, neighbor);
                    if (edgeIndex >= 0) // Valid edge
                    {
                        flatAdj.Add(new Int2(neighbor, edgeIndex));
                        currentOffset++;
                    }
                }
            }
            offsets[N] = currentOffset;

            // Validate: flatAdj.Count should equal 2 * edgeCount (each edge appears twice)
            int expectedAdjCount = graph.FlatEdgesFrom.Length * 2;
            if (flatAdj.Count != expectedAdjCount)
            {
                // This can happen if graph has isolated nodes or self-loops
                // Log warning but continue
                //Console.WriteLine($"[GPU] Warning: CSR adjacency count {flatAdj.Count} != expected {expectedAdjCount}");
            }

            // Upload to GPU
            _adjOffsetsBuffer?.Dispose();
            _adjDataBuffer?.Dispose();

            _adjOffsetsBuffer = _device.AllocateReadOnlyBuffer(offsets);
            _adjDataBuffer = _device.AllocateReadOnlyBuffer(flatAdj.ToArray());
            _topologyInitialized = true;
            _nodeCount = N;
        }

        /// <summary>
        /// Check if topology buffers are initialized.
        /// </summary>
        public bool IsTopologyInitialized => _topologyInitialized;

        /// <summary>
        /// Hybrid GPU method: Accepts curvature computed on CPU, evolves gravity on GPU.
        /// Use EvolveFullGpuStep for full GPU computation when topology buffers are ready.
        /// </summary>
        public void EvolveGravityGpu(
            float[] hostWeights,
            float[] hostCurvatures,
            float[] hostMasses,
            int[] hostEdgesFrom,
            int[] hostEdgesTo,
            float dt,
            float G,
            float lambda)
        {
            int edgeCount = hostWeights.Length;

            // Copy data CPU -> GPU
            _weightsBuffer.CopyFrom(hostWeights);
            _curvaturesBuffer.CopyFrom(hostCurvatures);
            _nodeMassesBuffer.CopyFrom(hostMasses);

            // Pack edge pairs into Int2 (only needed if topology changed)
            Int2[] packedEdges = new Int2[edgeCount];
            for (int i = 0; i < edgeCount; i++)
                packedEdges[i] = new Int2(hostEdgesFrom[i], hostEdgesTo[i]);
            _edgeIndicesBuffer.CopyFrom(packedEdges);

            // Run gravity shader (curvatureTermScale not used when curvature is precomputed)
            var shader = new GravityShader(
                _weightsBuffer,
                _curvaturesBuffer,
                _nodeMassesBuffer,
                _edgeIndicesBuffer,
                dt, G, lambda,
                (float)PhysicsConstants.CurvatureTermScale);

            _device.For(edgeCount, shader);

            // Copy results GPU -> CPU
            _weightsBuffer.CopyTo(hostWeights);
        }

        /// <summary>
        /// Full GPU step: Compute curvature AND evolve gravity entirely on GPU.
        /// Eliminates CPU bottleneck by computing Forman-Ricci curvature on GPU.
        /// 
        /// IMPORTANT: Call UpdateTopologyBuffers() first when graph topology changes.
        /// 
        /// RQ-MODERNIZATION: Now passes isScientificMode from PhysicsConstants.ScientificMode.
        /// </summary>
        /// <param name="hostWeights">Edge weights (will be updated in-place)</param>
        /// <param name="hostMasses">Node masses</param>
        /// <param name="hostEdgesFrom">Edge source nodes</param>
        /// <param name="hostEdgesTo">Edge target nodes</param>
        /// <param name="dt">Time step</param>
        /// <param name="G">Gravitational coupling constant</param>
        /// <param name="lambda">Cosmological constant</param>
        public void EvolveFullGpuStep(
            float[] hostWeights,
            float[] hostMasses,
            int[] hostEdgesFrom,
            int[] hostEdgesTo,
            float dt,
            float G,
            float lambda)
        {
            if (!_topologyInitialized || _adjOffsetsBuffer == null || _adjDataBuffer == null)
            {
                throw new InvalidOperationException(
                    "Topology buffers not initialized. Call UpdateTopologyBuffers() first.");
            }

            int edgeCount = hostWeights.Length;

            // Copy data CPU -> GPU
            _weightsBuffer.CopyFrom(hostWeights);
            _nodeMassesBuffer.CopyFrom(hostMasses);

            // Pack edge pairs into Int2
            Int2[] packedEdges = new Int2[edgeCount];
            for (int i = 0; i < edgeCount; i++)
                packedEdges[i] = new Int2(hostEdgesFrom[i], hostEdgesTo[i]);
            _edgeIndicesBuffer.CopyFrom(packedEdges);

            // 1. Compute curvature on GPU (Jost Forman-Ricci formula)
            var curvatureShader = new FormanCurvatureShader(
                _weightsBuffer,
                _edgeIndicesBuffer,
                _adjOffsetsBuffer,
                _adjDataBuffer,
                _curvaturesBuffer,
                _nodeCount);

            _device.For(edgeCount, curvatureShader);

            // 2. Evolve gravity (using fresh GPU-computed curvature)
            // RQ-MODERNIZATION: Pass isScientificMode from PhysicsConstants
            int isScientificMode = PhysicsConstants.ScientificMode ? 1 : 0;
            
            var gravityShader = new GravityShader(
                _weightsBuffer,
                _curvaturesBuffer,
                _nodeMassesBuffer,
                _edgeIndicesBuffer,
                dt, G, lambda,
                (float)PhysicsConstants.CurvatureTermScale,
                isScientificMode);

            _device.For(edgeCount, gravityShader);

            // Copy results GPU -> CPU
            _weightsBuffer.CopyTo(hostWeights);
        }

        /// <summary>
        /// Full GPU step without data copy back - data stays in GPU memory.
        /// Use SyncToHost() to retrieve results when needed.
        /// 
        /// This is the most efficient mode for iterative simulation.
        /// 
        /// RQ-MODERNIZATION: Now passes isScientificMode from PhysicsConstants.ScientificMode.
        /// </summary>
        public void EvolveFullGpuStep_NoCopy(float dt, float G, float lambda)
        {
            if (!_topologyInitialized || _adjOffsetsBuffer == null || _adjDataBuffer == null)
            {
                throw new InvalidOperationException(
                    "Topology buffers not initialized. Call UpdateTopologyBuffers() first.");
            }

            int edgeCount = (int)_weightsBuffer.Length;

            // 1. Compute curvature on GPU (Jost Forman-Ricci formula)
            var curvatureShader = new FormanCurvatureShader(
                _weightsBuffer,
                _edgeIndicesBuffer,
                _adjOffsetsBuffer,
                _adjDataBuffer,
                _curvaturesBuffer,
                _nodeCount);

            _device.For(edgeCount, curvatureShader);

            // 2. Evolve gravity
            // RQ-MODERNIZATION: Pass isScientificMode from PhysicsConstants
            int isScientificMode = PhysicsConstants.ScientificMode ? 1 : 0;
            
            var gravityShader = new GravityShader(
                _weightsBuffer,
                _curvaturesBuffer,
                _nodeMassesBuffer,
                _edgeIndicesBuffer,
                dt, G, lambda,
                (float)PhysicsConstants.CurvatureTermScale,
                isScientificMode);

            _device.For(edgeCount, gravityShader);
        }

        /// <summary>
        /// Upload initial data to GPU (weights, masses, edges).
        /// Call this once before using EvolveFullGpuStep_NoCopy.
        /// </summary>
        public void UploadInitialData(
            float[] hostWeights,
            float[] hostMasses,
            int[] hostEdgesFrom,
            int[] hostEdgesTo)
        {
            _weightsBuffer.CopyFrom(hostWeights);
            _nodeMassesBuffer.CopyFrom(hostMasses);

            int edgeCount = hostWeights.Length;
            Int2[] packedEdges = new Int2[edgeCount];
            for (int i = 0; i < edgeCount; i++)
                packedEdges[i] = new Int2(hostEdgesFrom[i], hostEdgesTo[i]);
            _edgeIndicesBuffer.CopyFrom(packedEdges);
        }

        /// <summary>
        /// Optimized method that evolves gravity without copying data back to CPU.
        /// Data stays in GPU memory for maximum performance.
        /// Use SyncToHost() to retrieve results when needed (e.g., for visualization).
        /// 
        /// NOTE: This is the HYBRID mode (curvature from CPU).
        /// For full GPU mode, use EvolveFullGpuStep_NoCopy instead.
        /// 
        /// RQ-MODERNIZATION: Now passes isScientificMode from PhysicsConstants.ScientificMode.
        /// </summary>
        public void EvolveGravityGpu_NoCopy(float dt, float G, float lambda)
        {
            // RQ-MODERNIZATION: Pass isScientificMode from PhysicsConstants
            int isScientificMode = PhysicsConstants.ScientificMode ? 1 : 0;
            
            var shader = new GravityShader(
                _weightsBuffer,
                _curvaturesBuffer,
                _nodeMassesBuffer,
                _edgeIndicesBuffer,
                dt, G, lambda,
                (float)PhysicsConstants.CurvatureTermScale,
                isScientificMode);

            int edgeCount = (int)_weightsBuffer.Length;
            _device.For(edgeCount, shader);
        }

        /// <summary>
        /// Synchronize GPU data back to host memory.
        /// Call this only when visualization or analysis is needed (e.g., every 50 steps).
        /// </summary>
        public void SyncToHost(float[] hostWeights)
        {
            _weightsBuffer.CopyTo(hostWeights);
        }

        /// <summary>
        /// Synchronize both weights and curvatures to host.
        /// Useful for diagnostics and visualization.
        /// </summary>
        public void SyncToHost(float[] hostWeights, float[] hostCurvatures)
        {
            _weightsBuffer.CopyTo(hostWeights);
            _curvaturesBuffer.CopyTo(hostCurvatures);
        }

        public void Dispose()
        {
            _weightsBuffer?.Dispose();
            _curvaturesBuffer?.Dispose();
            _nodeMassesBuffer?.Dispose();
            _edgeIndicesBuffer?.Dispose();
            _adjOffsetsBuffer?.Dispose();
            _adjDataBuffer?.Dispose();
        }
    }
}
