using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using ComputeSharp;

namespace RQSimulation.GPUOptimized
{
    public partial class OptimizedGpuSimulationEngine
    {
        // === EXTENDED GPU PHYSICS METHODS ===
        
        // Additional buffers for extended physics
        private ReadWriteBuffer<float>? _gaugePhasesBuffer;
        private ReadWriteBuffer<float>? _timeDilationBuffer;
        private ReadWriteBuffer<int>? _nodeStatesBuffer;
        private ReadWriteBuffer<int>? _refractoryCountsBuffer;
        private ReadOnlyBuffer<int>? _colorMaskBuffer;
        private ReadOnlyBuffer<int>? _csrEdgeIndicesBuffer;
        private bool _extendedBuffersInitialized;

        /// <summary>
        /// Initialize extended physics buffers (gauge, time dilation, node states).
        /// Call after Initialize() if using extended physics.
        /// </summary>
        public void InitializeExtendedPhysics()
        {
            if (!_initialized)
                throw new InvalidOperationException("Call Initialize() before InitializeExtendedPhysics()");

            _gaugePhasesBuffer = _device.AllocateReadWriteBuffer<float>(_edgeCount);
            _timeDilationBuffer = _device.AllocateReadWriteBuffer<float>(_nodeCount);
            _nodeStatesBuffer = _device.AllocateReadWriteBuffer<int>(_nodeCount);
            _refractoryCountsBuffer = _device.AllocateReadWriteBuffer<int>(_nodeCount);

            // Build CSR edge index mapping
            int[] csrEdgeIndices = new int[_totalDirectedEdges];
            for (int n = 0; n < _nodeCount; n++)
            {
                int start = _graph.CsrOffsets[n];
                int end = _graph.CsrOffsets[n + 1];
                for (int k = start; k < end; k++)
                {
                    int neighbor = _graph.CsrIndices[k];
                    int edgeIdx = _graph.GetEdgeIndex(n, neighbor);
                    csrEdgeIndices[k] = Math.Max(0, edgeIdx);
                }
            }
            _csrEdgeIndicesBuffer = _device.AllocateReadOnlyBuffer(csrEdgeIndices);

            _extendedBuffersInitialized = true;
        }

        /// <summary>
        /// Upload extended physics state to GPU.
        /// </summary>
        public void UploadExtendedState()
        {
            if (!_extendedBuffersInitialized)
                throw new InvalidOperationException("Call InitializeExtendedPhysics() first");

            _perfTimer.Restart();

            // Upload gauge phases
            float[] phases = new float[_edgeCount];
            for (int e = 0; e < _edgeCount; e++)
            {
                int i = _graph.FlatEdgesFrom[e];
                int j = _graph.FlatEdgesTo[e];
                phases[e] = (float)(_graph.EdgePhaseU1?[i, j] ?? 0.0);
            }
            _gaugePhasesBuffer!.CopyFrom(phases);

            // Upload node states
            int[] states = new int[_nodeCount];
            int[] refractory = new int[_nodeCount];
            for (int n = 0; n < _nodeCount; n++)
            {
                states[n] = _graph.State[n] switch
                {
                    NodeState.Rest => 0,
                    NodeState.Excited => 1,
                    NodeState.Refractory => 2,
                    _ => 0
                };
                refractory[n] = _graph.GetRefractoryCounter(n);
            }
            _nodeStatesBuffer!.CopyFrom(states);
            _refractoryCountsBuffer!.CopyFrom(refractory);

            // Initialize time dilation to 1.0
            float[] dilation = new float[_nodeCount];
            Array.Fill(dilation, 1.0f);
            _timeDilationBuffer!.CopyFrom(dilation);

            _dataCopyTime += _perfTimer.ElapsedTicks;
        }

        /// <summary>
        /// Run gauge phase evolution on GPU.
        /// </summary>
        public void StepGaugeEvolutionGpu(float dt, float gaugeCoupling = 0.1f, float plaquetteWeight = 0.05f)
        {
            if (!_extendedBuffersInitialized)
                throw new InvalidOperationException("Extended physics not initialized");

            _perfTimer.Restart();

            // Create temporary ReadOnly buffers from ReadWrite buffers
            float[] hostWeights = new float[_edgeCount];
            float[] hostScalar = new float[_nodeCount];
            int[] hostStates = new int[_nodeCount];

            _weightsBuffer!.CopyTo(hostWeights);
            _scalarFieldBuffer!.CopyTo(hostScalar);
            _nodeStatesBuffer!.CopyTo(hostStates);

            using var weightsRO = _device.AllocateReadOnlyBuffer(hostWeights);
            using var scalarRO = _device.AllocateReadOnlyBuffer(hostScalar);
            using var statesRO = _device.AllocateReadOnlyBuffer(hostStates);

            var shader = new GaugePhaseEvolutionShader(
                _gaugePhasesBuffer!,
                _edgesBuffer!,
                weightsRO,
                scalarRO,
                statesRO,
                _adjOffsetsBuffer!,
                _adjDataBuffer!,
                dt, gaugeCoupling, plaquetteWeight, _nodeCount);

            _device.For(_edgeCount, shader);
            _kernelLaunches++;

            _gpuKernelTime += _perfTimer.ElapsedTicks;
        }

        /// <summary>
        /// Compute time dilation factors on GPU.
        /// </summary>
        public void ComputeTimeDilationGpu(float massScale = 1.0f, float curvatureScale = 1.0f)
        {
            if (!_extendedBuffersInitialized)
                throw new InvalidOperationException("Extended physics not initialized");

            _perfTimer.Restart();

            // Create temporary ReadOnly buffer from curvatures
            float[] hostCurvatures = new float[_edgeCount];
            _curvaturesBuffer!.CopyTo(hostCurvatures);
            using var curvaturesRO = _device.AllocateReadOnlyBuffer(hostCurvatures);

            var shader = new TimeDilationShader(
                _timeDilationBuffer!,
                _massesBuffer!,
                curvaturesRO,
                _adjOffsetsBuffer!,
                _adjDataBuffer!,
                massScale, curvatureScale, _nodeCount);

            _device.For(_nodeCount, shader);
            _kernelLaunches++;

            _gpuKernelTime += _perfTimer.ElapsedTicks;
        }

        /// <summary>
        /// Update node states for a specific color (graph coloring for parallel safety).
        /// </summary>
        public void StepNodeStatesGpu(int[] colorMask, float excitationThreshold = 0.5f, int refractoryDuration = 3)
        {
            if (!_extendedBuffersInitialized)
                throw new InvalidOperationException("Extended physics not initialized");

            _perfTimer.Restart();

            // Create temporary ReadOnly buffers
            float[] hostWeights = new float[_edgeCount];
            float[] hostDilation = new float[_nodeCount];

            _weightsBuffer!.CopyTo(hostWeights);
            _timeDilationBuffer!.CopyTo(hostDilation);

            using var weightsRO = _device.AllocateReadOnlyBuffer(hostWeights);
            using var dilationRO = _device.AllocateReadOnlyBuffer(hostDilation);

            // Upload color mask
            _colorMaskBuffer?.Dispose();
            _colorMaskBuffer = _device.AllocateReadOnlyBuffer(colorMask);

            var shader = new NodeStateUpdateShader(
                _nodeStatesBuffer!,
                _refractoryCountsBuffer!,
                weightsRO,
                _csrOffsetsBuffer!,
                _csrNeighborsBuffer!,
                _csrEdgeIndicesBuffer!,
                dilationRO,
                _colorMaskBuffer,
                excitationThreshold, refractoryDuration, _nodeCount);

            _device.For(_nodeCount, shader);
            _kernelLaunches++;

            _gpuKernelTime += _perfTimer.ElapsedTicks;
        }

        /// <summary>
        /// Sync extended physics state back to CPU graph.
        /// </summary>
        public void SyncExtendedStateToGraph()
        {
            if (!_extendedBuffersInitialized) return;

            _perfTimer.Restart();

            // Sync gauge phases
            float[] phases = new float[_edgeCount];
            _gaugePhasesBuffer!.CopyTo(phases);
            if (_graph.EdgePhaseU1 != null)
            {
                for (int e = 0; e < _edgeCount; e++)
                {
                    int i = _graph.FlatEdgesFrom[e];
                    int j = _graph.FlatEdgesTo[e];
                    _graph.EdgePhaseU1[i, j] = phases[e];
                    _graph.EdgePhaseU1[j, i] = -phases[e]; // Antisymmetric
                }
            }

            // Sync node states
            int[] states = new int[_nodeCount];
            int[] refractory = new int[_nodeCount];
            _nodeStatesBuffer!.CopyTo(states);
            _refractoryCountsBuffer!.CopyTo(refractory);

            for (int n = 0; n < _nodeCount; n++)
            {
                _graph.State[n] = states[n] switch
                {
                    0 => NodeState.Rest,
                    1 => NodeState.Excited,
                    2 => NodeState.Refractory,
                    _ => NodeState.Rest
                };
                _graph.SetRefractoryCounter(n, refractory[n]);
            }

            _dataCopyTime += _perfTimer.ElapsedTicks;
        }
    }
}
