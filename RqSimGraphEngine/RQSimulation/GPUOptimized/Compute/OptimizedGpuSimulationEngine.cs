using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using ComputeSharp;
using RQSimulation.Core.Infrastructure;

namespace RQSimulation.GPUOptimized
{
    /// <summary>
    /// High-performance GPU-accelerated simulation engine.
    /// Key optimizations:
    /// - Pre-allocated buffers (zero GC pressure)
    /// - Persistent GPU state (minimal CPU-GPU transfers)
    /// - Fused kernels (reduced kernel launch overhead)
    /// - Batched operations (reduced sync points)
    /// </summary>
    public partial class OptimizedGpuSimulationEngine : IDisposable
    {
        private readonly GraphicsDevice _device;
        private readonly RQGraph _graph;

        // Pre-allocated host buffers (reused every step)
        private float[] _hostWeights;
        private float[] _hostMasses;
        private float[] _hostScalarField;
        private int[] _csrNodeMapping;  // CSR index ? source node (precomputed)

        // GPU buffers (persistent)
        private ReadWriteBuffer<float> _weightsBuffer;
        private ReadWriteBuffer<float> _curvaturesBuffer;
        private ReadWriteBuffer<float> _scalarFieldBuffer;
        private ReadWriteBuffer<float> _scalarMomentumBuffer;  // For symplectic Higgs evolution
        private ReadOnlyBuffer<float> _massesBuffer;
        private ReadOnlyBuffer<Int2> _edgesBuffer;

        // Topology buffers (CSR format)
        private ReadOnlyBuffer<int> _adjOffsetsBuffer;
        private ReadOnlyBuffer<Int2> _adjDataBuffer;
        private ReadOnlyBuffer<int> _csrOffsetsBuffer;
        private ReadOnlyBuffer<int> _csrNeighborsBuffer;
        private ReadOnlyBuffer<float> _csrWeightsBuffer;

        // Statistics buffers (for GPU-side aggregation)
        private ReadWriteBuffer<int> _excitedCountBuffer;
        private ReadWriteBuffer<float> _energySumBuffer;

        private int _nodeCount;
        private int _edgeCount;
        private int _totalDirectedEdges;
        private bool _initialized;
        private int _topologyVersion;

        // Performance counters
        private long _gpuKernelTime;
        private long _dataCopyTime;
        private int _kernelLaunches;
        private readonly Stopwatch _perfTimer = new();

        public static string GetGpuName(int index)
        {
            try
            {
                var device = GraphicsDevice.EnumerateDevices().ElementAtOrDefault(index);
                return device?.Name ?? $"Unknown GPU (Index {index} out of range)";
            }
            catch (Exception ex)
            {
                return $"Error retrieving GPU info: {ex.Message}";
            }
        }

        public OptimizedGpuSimulationEngine(RQGraph graph)
        {
            _graph = graph;
            _device = GraphicsDevice.GetDefault();
            _nodeCount = graph.N;
        }

        /// <summary>
        /// Initialize all GPU resources. Call once before simulation loop.
        /// </summary>
        public void Initialize()
        {
            _edgeCount = _graph.FlatEdgesFrom.Length;
            _graph.BuildSoAViews();
            _totalDirectedEdges = _graph.CsrOffsets[_nodeCount];

            // Pre-allocate host buffers (ZERO allocations in main loop!)
            _hostWeights = new float[_edgeCount];
            _hostMasses = new float[_nodeCount];
            _hostScalarField = new float[_nodeCount];

            // Precompute CSR node mapping (O(E) once instead of O(E?N) every time)
            _csrNodeMapping = new int[_totalDirectedEdges];
            for (int n = 0; n < _nodeCount; n++)
            {
                int start = _graph.CsrOffsets[n];
                int end = _graph.CsrOffsets[n + 1];
                for (int k = start; k < end; k++)
                {
                    _csrNodeMapping[k] = n;
                }
            }

            // Allocate GPU buffers
            _weightsBuffer = _device.AllocateReadWriteBuffer<float>(_edgeCount);
            _curvaturesBuffer = _device.AllocateReadWriteBuffer<float>(_edgeCount);
            _scalarFieldBuffer = _device.AllocateReadWriteBuffer<float>(_nodeCount);
            _scalarMomentumBuffer = _device.AllocateReadWriteBuffer<float>(_nodeCount);
            _massesBuffer = _device.AllocateReadOnlyBuffer<float>(_nodeCount);
            _excitedCountBuffer = _device.AllocateReadWriteBuffer<int>(1);
            _energySumBuffer = _device.AllocateReadWriteBuffer<float>(1);

            // Pack edge pairs
            Int2[] packedEdges = new Int2[_edgeCount];
            for (int i = 0; i < _edgeCount; i++)
            {
                packedEdges[i] = new Int2(_graph.FlatEdgesFrom[i], _graph.FlatEdgesTo[i]);
            }
            _edgesBuffer = _device.AllocateReadOnlyBuffer(packedEdges);

            // Topology buffers
            UpdateTopologyBuffers();

            _initialized = true;
            _topologyVersion = 0;
        }

        /// <summary>
        /// Update topology buffers. Call only when edges are added/removed.
        /// </summary>
        public void UpdateTopologyBuffers()
        {
            _graph.BuildSoAViews();
            _totalDirectedEdges = _graph.CsrOffsets[_nodeCount];

            // Rebuild CSR node mapping
            if (_csrNodeMapping.Length != _totalDirectedEdges)
            {
                _csrNodeMapping = new int[_totalDirectedEdges];
            }

            for (int n = 0; n < _nodeCount; n++)
            {
                int start = _graph.CsrOffsets[n];
                int end = _graph.CsrOffsets[n + 1];
                for (int k = start; k < end; k++)
                {
                    _csrNodeMapping[k] = n;
                }
            }

            // Build adjacency data for Forman curvature
            var adjDataList = new List<Int2>();
            for (int i = 0; i < _nodeCount; i++)
            {
                foreach (int neighbor in _graph.Neighbors(i))
                {
                    int edgeIndex = _graph.GetEdgeIndex(i, neighbor);
                    if (edgeIndex >= 0)
                    {
                        adjDataList.Add(new Int2(neighbor, edgeIndex));
                    }
                }
            }

            // Upload to GPU
            _adjOffsetsBuffer?.Dispose();
            _adjDataBuffer?.Dispose();
            _csrOffsetsBuffer?.Dispose();
            _csrNeighborsBuffer?.Dispose();
            _csrWeightsBuffer?.Dispose();

            _adjOffsetsBuffer = _device.AllocateReadOnlyBuffer(_graph.CsrOffsets);
            _adjDataBuffer = _device.AllocateReadOnlyBuffer(adjDataList.ToArray());
            _csrOffsetsBuffer = _device.AllocateReadOnlyBuffer(_graph.CsrOffsets);
            _csrNeighborsBuffer = _device.AllocateReadOnlyBuffer(_graph.CsrIndices);

            // CSR weights
            float[] csrWeights = new float[_totalDirectedEdges];
            for (int k = 0; k < _totalDirectedEdges; k++)
            {
                int from = _csrNodeMapping[k];
                int to = _graph.CsrIndices[k];
                csrWeights[k] = (float)_graph.Weights[from, to];
            }
            _csrWeightsBuffer = _device.AllocateReadOnlyBuffer(csrWeights);

            _topologyVersion++;
        }

        /// <summary>
        /// Upload current state to GPU. Call at start or after CPU-side changes.
        /// </summary>
        public void UploadState()
        {
            _perfTimer.Restart();

            // Copy weights using precomputed indices
            for (int e = 0; e < _edgeCount; e++)
            {
                int i = _graph.FlatEdgesFrom[e];
                int j = _graph.FlatEdgesTo[e];
                _hostWeights[e] = (float)_graph.Weights[i, j];
            }

            // Copy masses
            var correlationMass = _graph.ComputePerNodeCorrelationMass();
            for (int n = 0; n < _nodeCount; n++)
            {
                _hostMasses[n] = (float)correlationMass[n];
            }

            // Copy scalar field
            for (int n = 0; n < _nodeCount; n++)
            {
                _hostScalarField[n] = _graph.ScalarField is not null && n < _graph.ScalarField.Length
                    ? (float)_graph.ScalarField[n]
                    : 0f;
            }

            // Upload to GPU
            _weightsBuffer.CopyFrom(_hostWeights);
            _massesBuffer.Dispose();
            _massesBuffer = _device.AllocateReadOnlyBuffer(_hostMasses);
            _scalarFieldBuffer.CopyFrom(_hostScalarField);

            _dataCopyTime += _perfTimer.ElapsedTicks;
        }

        /// <summary>
        /// Run one physics step entirely on GPU (no CPU-GPU sync).
        /// Uses Higgs potential with symplectic integration for scalar field.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StepGpu(float dt, float G, float lambda,
                           float diffusionRate, float higgsLambda = 0.1f, float higgsMuSquared = 0.01f)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized. Call Initialize() first.");

            _perfTimer.Restart();

            // 1. Compute Forman-Ricci curvature on GPU (Jost formula)
            var curvatureShader = new FormanCurvatureShader(
                _weightsBuffer,
                _edgesBuffer,
                _adjOffsetsBuffer,
                _adjDataBuffer,
                _curvaturesBuffer,
                _nodeCount);
            _device.For(_edgeCount, curvatureShader);
            _kernelLaunches++;

            // 2. Evolve gravity (weight update)
            var gravityShader = new GravityShader(
                _weightsBuffer,
                _curvaturesBuffer,
                _massesBuffer,
                _edgesBuffer,
                dt, G, lambda, 
                (float)PhysicsConstants.CurvatureTermScale);
            _device.For(_edgeCount, gravityShader);
            _kernelLaunches++;

            // 3. Evolve scalar field with Higgs potential (symplectic leapfrog)
            // Step 3a: Update momentum: p += dt * F, where F = D??? - V'(?)
            var momentumShader = new HiggsMomentumShader(
                _scalarFieldBuffer,
                _scalarMomentumBuffer,
                _csrOffsetsBuffer,
                _csrNeighborsBuffer,
                _csrWeightsBuffer,
                dt,
                diffusionRate,
                higgsLambda,
                higgsMuSquared,
                _nodeCount);
            _device.For(_nodeCount, momentumShader);
            _kernelLaunches++;

            // Step 3b: Update field: ? += dt * p
            var fieldShader = new SymplecticFieldUpdateShader(
                _scalarFieldBuffer, _scalarMomentumBuffer, dt, _nodeCount);
            _device.For(_nodeCount, fieldShader);
            _kernelLaunches++;

            _gpuKernelTime += _perfTimer.ElapsedTicks;
        }

        /// <summary>
        /// Run multiple steps on GPU without any sync (maximum performance).
        /// </summary>
        public void StepGpuBatch(int batchSize, float dt, float G, float lambda,
                                 float diffusionRate, 
                                 float higgsLambda = 0.1f, float higgsMuSquared = 0.01f)
        {
            for (int i = 0; i < batchSize; i++)
            {
                StepGpu(dt, G, lambda, diffusionRate, higgsLambda, higgsMuSquared);
            }
        }

        /// <summary>
        /// Sync weights from GPU to CPU graph. Call periodically for visualization/metrics.
        /// Also updates the adaptive heavy threshold to ensure cluster detection uses current data.
        /// </summary>
        public void SyncWeightsToGraph()
        {
            _perfTimer.Restart();

            _weightsBuffer.CopyTo(_hostWeights);

            for (int e = 0; e < _edgeCount; e++)
            {
                int i = _graph.FlatEdgesFrom[e];
                int j = _graph.FlatEdgesTo[e];
                double w = Math.Clamp(_hostWeights[e], 0.0, 1.0);
                _graph.Weights[i, j] = w;
                _graph.Weights[j, i] = w;
            }

            // Update derived metrics that depend on weights
            _graph.UpdateAdaptiveHeavyThreshold();

            _dataCopyTime += _perfTimer.ElapsedTicks;
        }

        /// <summary>
        /// Comprehensive sync of all GPU state to CPU graph.
        /// Call this before visualization or metrics collection to ensure consistency.
        /// Syncs: weights, scalar field, adaptive threshold, correlation mass.
        /// </summary>
        public void SyncAllStatesToGraph()
        {
            SyncWeightsToGraph();
            SyncScalarFieldToGraph();
            
            // Recompute correlation mass from updated weights
            _graph.RecomputeCorrelationMass();
        }

        /// <summary>
        /// Sync scalar field from GPU to CPU graph.
        /// </summary>
        public void SyncScalarFieldToGraph()
        {
            _perfTimer.Restart();

            _scalarFieldBuffer.CopyTo(_hostScalarField);

            for (int n = 0; n < _nodeCount; n++)
            {
                _graph.ScalarField[n] = _hostScalarField[n];
            }

            _dataCopyTime += _perfTimer.ElapsedTicks;
        }

        /// <summary>
        /// Run unified physics step with ALL GPU algorithms.
        /// Combines gravity, scalar field (Higgs), gauge phases, time dilation, and node states.
        /// 
        /// This is the RQ-compliant GPU implementation that matches the CPU UnifiedPhysicsStep.
        /// Uses Higgs potential with symplectic integration for scalar field.
        /// </summary>
        public void UnifiedPhysicsStepGpu(
            float dt, 
            float G, 
            float lambda,
            int[][] colorMasks)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");

            // 1. Compute curvature and update gravity (Jost Forman-Ricci)
            var curvatureShader = new FormanCurvatureShader(
                _weightsBuffer,
                _edgesBuffer,
                _adjOffsetsBuffer,
                _adjDataBuffer,
                _curvaturesBuffer,
                _nodeCount);
            _device.For(_edgeCount, curvatureShader);
            _kernelLaunches++;

            var gravityShader = new GravityShader(
                _weightsBuffer,
                _curvaturesBuffer,
                _massesBuffer,
                _edgesBuffer,
                dt, G, lambda,
                (float)PhysicsConstants.CurvatureTermScale);
            _device.For(_edgeCount, gravityShader);
            _kernelLaunches++;

            // 2. Evolve scalar field with Higgs potential (symplectic leapfrog)
            // Step 2a: Update momentum: p += dt * F
            var momentumShader = new HiggsMomentumShader(
                _scalarFieldBuffer,
                _scalarMomentumBuffer,
                _csrOffsetsBuffer,
                _csrNeighborsBuffer,
                _csrWeightsBuffer,
                dt,
                (float)PhysicsConstants.FieldDiffusionRate,
                (float)PhysicsConstants.HiggsLambda,
                (float)PhysicsConstants.HiggsMuSquared,
                _nodeCount);
            _device.For(_nodeCount, momentumShader);
            _kernelLaunches++;

            // Step 2b: Update field: ? += dt * p
            var fieldShader = new SymplecticFieldUpdateShader(
                _scalarFieldBuffer, _scalarMomentumBuffer, dt, _nodeCount);
            _device.For(_nodeCount, fieldShader);
            _kernelLaunches++;

            // 3. Extended physics (if initialized)
            if (_extendedBuffersInitialized)
            {
                // 3a. Gauge phase evolution
                StepGaugeEvolutionGpu(dt, 0.1f, 0.05f);

                // 3b. Time dilation
                ComputeTimeDilationGpu(1.0f, 1.0f);

                // 3c. Node state updates (with graph coloring for parallel safety)
                if (colorMasks != null)
                {
                    foreach (var colorMask in colorMasks)
                    {
                        StepNodeStatesGpu(colorMask, 0.5f, 3);
                    }
                }
            }
        }

        /// <summary>
        /// Simple unified step without node state updates (for compatibility).
        /// </summary>
        public void UnifiedPhysicsStepGpuSimple(float dt, float G, float lambda)
        {
            UnifiedPhysicsStepGpu(dt, G, lambda, null!);
        }

        /// <summary>
        /// Get performance statistics.
        /// </summary>
        public (double gpuTimeMs, double copyTimeMs, int kernelLaunches) GetPerformanceStats()
        {
            double gpuMs = _gpuKernelTime * 1000.0 / Stopwatch.Frequency;
            double copyMs = _dataCopyTime * 1000.0 / Stopwatch.Frequency;
            return (gpuMs, copyMs, _kernelLaunches);
        }

        /// <summary>
        /// Reset performance counters.
        /// </summary>
        public void ResetPerformanceCounters()
        {
            _gpuKernelTime = 0;
            _dataCopyTime = 0;
            _kernelLaunches = 0;
        }

        /// <summary>
        /// Download current GPU state to a GraphSnapshot DTO for distribution to worker GPUs.
        /// 
        /// MULTI-GPU SUPPORT:
        /// ==================
        /// This method extracts the current GPU state into CPU RAM (GraphSnapshot),
        /// which can then be uploaded to worker GPUs for parallel analysis.
        /// 
        /// PERFORMANCE NOTE:
        /// =================
        /// This involves VRAM -> CPU copy (~2-5ms for 100k nodes).
        /// Call periodically (e.g., every 100 steps), not every frame.
        /// </summary>
        /// <param name="tickId">Current simulation tick for temporal tracking</param>
        /// <returns>GraphSnapshot containing current topology and physics state</returns>
        public GraphSnapshot DownloadSnapshot(long tickId)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized. Call Initialize() first.");

            _perfTimer.Restart();

            // 1. Download weights from GPU
            _weightsBuffer.CopyTo(_hostWeights);

            // 2. Download scalar field from GPU
            _scalarFieldBuffer.CopyTo(_hostScalarField);

            // 3. Build CSR topology from graph (already maintains CSR data)
            _graph.BuildSoAViews();
            int nnz = _graph.CsrIndices.Length;

            // 4. Copy CSR arrays (defensive copies)
            int[] offsets = new int[_graph.CsrOffsets.Length];
            Array.Copy(_graph.CsrOffsets, offsets, offsets.Length);

            int[] indices = new int[nnz];
            Array.Copy(_graph.CsrIndices, indices, indices.Length);

            // 5. Build double-precision weights array in CSR order
            double[] csrWeights = new double[nnz];
            for (int i = 0; i < _nodeCount; i++)
            {
                int start = offsets[i];
                int end = offsets[i + 1];
                for (int k = start; k < end; k++)
                {
                    int j = indices[k];
                    csrWeights[k] = _graph.Weights[i, j];
                }
            }

            // 6. Get node masses (correlation mass)
            double[] masses = new double[_nodeCount];
            var correlationMass = _graph.CorrelationMass;
            if (correlationMass != null && correlationMass.Length >= _nodeCount)
            {
                Array.Copy(correlationMass, masses, _nodeCount);
            }
            else
            {
                // Fallback: compute from weights
                for (int i = 0; i < _nodeCount; i++)
                {
                    masses[i] = _graph.GetNodeMass(i);
                }
            }

            // 7. Convert scalar field to double
            double[] scalarField = new double[_nodeCount];
            for (int n = 0; n < _nodeCount; n++)
            {
                scalarField[n] = _hostScalarField[n];
            }

            // 8. Compute total weight
            double totalWeight = 0.0;
            for (int i = 0; i < nnz; i++)
            {
                totalWeight += csrWeights[i];
            }
            totalWeight /= 2.0; // Each edge counted twice in CSR

            _dataCopyTime += _perfTimer.ElapsedTicks;

            return new GraphSnapshot
            {
                RowOffsets = offsets,
                ColIndices = indices,
                EdgeWeights = csrWeights,
                NodeMasses = masses,
                ScalarField = scalarField,
                Curvatures = new double[_nodeCount], // Curvatures not currently stored per-node
                TickId = tickId,
                Timestamp = DateTime.UtcNow,
                TopologyVersion = _graph.TopologyVersion,
                TotalWeight = totalWeight
            };
        }

        /// <summary>
        /// Download lightweight snapshot with topology only (no physics state).
        /// Faster than full DownloadSnapshot when only topology is needed.
        /// </summary>
        /// <param name="tickId">Current simulation tick</param>
        /// <returns>GraphSnapshot with topology but empty physics arrays</returns>
        public GraphSnapshot DownloadTopologyOnly(long tickId)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized. Call Initialize() first.");

            _perfTimer.Restart();

            // Build CSR topology
            _graph.BuildSoAViews();
            int nnz = _graph.CsrIndices.Length;

            // Copy CSR arrays
            int[] offsets = new int[_graph.CsrOffsets.Length];
            Array.Copy(_graph.CsrOffsets, offsets, offsets.Length);

            int[] indices = new int[nnz];
            Array.Copy(_graph.CsrIndices, indices, indices.Length);

            // Build weights in CSR order from current GPU state
            _weightsBuffer.CopyTo(_hostWeights);

            double[] csrWeights = new double[nnz];
            for (int i = 0; i < _nodeCount; i++)
            {
                int start = offsets[i];
                int end = offsets[i + 1];
                for (int k = start; k < end; k++)
                {
                    int j = indices[k];
                    // Use flat edge lookup
                    int edgeIdx = _graph.GetEdgeIndex(i, j);
                    if (edgeIdx >= 0 && edgeIdx < _hostWeights.Length)
                    {
                        csrWeights[k] = _hostWeights[edgeIdx];
                    }
                    else
                    {
                        csrWeights[k] = _graph.Weights[i, j];
                    }
                }
            }

            _dataCopyTime += _perfTimer.ElapsedTicks;

            return new GraphSnapshot
            {
                RowOffsets = offsets,
                ColIndices = indices,
                EdgeWeights = csrWeights,
                NodeMasses = [], // Empty for topology-only
                ScalarField = [],
                Curvatures = [],
                TickId = tickId,
                Timestamp = DateTime.UtcNow,
                TopologyVersion = _graph.TopologyVersion,
                TotalWeight = csrWeights.Sum() / 2.0
            };
        }

        public void Dispose()
        {
            _weightsBuffer?.Dispose();
            _curvaturesBuffer?.Dispose();
            _scalarFieldBuffer?.Dispose();
            _scalarMomentumBuffer?.Dispose();
            _massesBuffer?.Dispose();
            _edgesBuffer?.Dispose();
            _adjOffsetsBuffer?.Dispose();
            _adjDataBuffer?.Dispose();
            _csrOffsetsBuffer?.Dispose();
            _csrNeighborsBuffer?.Dispose();
            _csrWeightsBuffer?.Dispose();
            _excitedCountBuffer?.Dispose();
            _energySumBuffer?.Dispose();
        }
    }
}
