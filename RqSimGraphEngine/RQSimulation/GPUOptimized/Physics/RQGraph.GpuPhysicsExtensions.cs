using System;
using System.Numerics;
using RQSimulation.GPUOptimized;

namespace RQSimulation
{
    /// <summary>
    /// GPU-accelerated extensions for RQGraph.
    /// Provides GPU-optimized versions of RQ-Hypothesis computations:
    /// - Stress-Energy Tensor
    /// - Node Mass Models
    /// - Lapse Function
    /// - Volume Metrics
    /// </summary>
    public partial class RQGraph
    {
        // GPU Physics Engine instance
        private GpuRQPhysicsEngine? _gpuPhysicsEngine;

        // Cached GPU buffers
        private float[]? _gpuStressEnergyHost;
        private float[]? _gpuNodeMassesHost;
        private float[]? _gpuLapseHost;

        /// <summary>
        /// Initialize GPU physics engine for RQ-Hypothesis computations.
        /// Call this once after graph is created and before simulation loop.
        /// </summary>
        /// <returns>True if GPU initialization succeeded</returns>
        public bool InitGpuPhysicsEngine()
        {
            try
            {
                int edgeCount = FlatEdgesFrom?.Length ?? 0;
                if (edgeCount == 0)
                {
                    BuildSoAViews();
                    edgeCount = FlatEdgesFrom.Length;
                }

                _gpuPhysicsEngine = new GpuRQPhysicsEngine();
                _gpuPhysicsEngine.Initialize(N, edgeCount);

                // Upload topology
                _gpuPhysicsEngine.UpdateTopology(CsrOffsets, CsrIndices);

                // Allocate host buffers
                _gpuStressEnergyHost = new float[edgeCount];
                _gpuNodeMassesHost = new float[N];
                _gpuLapseHost = new float[N];

                //Console.WriteLine($"[GPU-RQ] Physics engine initialized: N={N}, E={edgeCount}");
                return true;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[GPU-RQ] Init failed: {ex.Message}");
                _gpuPhysicsEngine = null;
                return false;
            }
        }

        /// <summary>
        /// Check if GPU physics engine is available.
        /// </summary>
        public bool IsGpuPhysicsActive => _gpuPhysicsEngine != null;

        /// <summary>
        /// Dispose GPU physics engine.
        /// </summary>
        public void DisposeGpuPhysicsEngine()
        {
            _gpuPhysicsEngine?.Dispose();
            _gpuPhysicsEngine = null;
            _gpuStressEnergyHost = null;
            _gpuNodeMassesHost = null;
            _gpuLapseHost = null;
        }

        /// <summary>
        /// Compute stress-energy tensor for all edges using GPU.
        /// Much faster than per-edge CPU computation for large graphs.
        /// 
        /// Results are stored internally and can be accessed via GetStressEnergyTensorGpu(i, j).
        /// </summary>
        public void ComputeAllStressEnergyGpu()
        {
            if (_gpuPhysicsEngine == null || _gpuStressEnergyHost == null)
            {
                throw new InvalidOperationException("GPU physics engine not initialized");
            }

            // Prepare field data
            int edgeCount = FlatEdgesFrom.Length;
            float[] scalarFieldF = new float[N];
            float[] spinorNormsF = new float[N];
            float[] correlationMassF = new float[N];
            float[] edgePhasesF = new float[edgeCount];
            float[] weightsF = new float[edgeCount];

            // Copy scalar field
            if (ScalarField != null)
            {
                for (int i = 0; i < N; i++)
                    scalarFieldF[i] = (float)ScalarField[i];
            }

            // Copy spinor norms
            if (_spinorA != null)
            {
                for (int i = 0; i < N; i++)
                    spinorNormsF[i] = (float)_spinorA[i].Magnitude;
            }

            // Copy correlation mass
            var corrMass = ComputePerNodeCorrelationMass();
            for (int i = 0; i < N; i++)
                correlationMassF[i] = (float)corrMass[i];

            // Copy edge phases and weights
            for (int e = 0; e < edgeCount; e++)
            {
                int i = FlatEdgesFrom[e];
                int j = FlatEdgesTo[e];
                edgePhasesF[e] = _edgePhaseU1 != null ? (float)_edgePhaseU1[i, j] : 0f;
                weightsF[e] = (float)Weights[i, j];
            }

            // Upload data
            _gpuPhysicsEngine.UploadFieldData(
                scalarFieldF, spinorNormsF, correlationMassF,
                edgePhasesF, weightsF, FlatEdgesFrom, FlatEdgesTo);

            // Compute on GPU
            _gpuPhysicsEngine.ComputeStressEnergyGpu(
                (float)PhysicsConstants.ScalarFieldEnergyWeight,
                (float)PhysicsConstants.FermionFieldEnergyWeight,
                (float)PhysicsConstants.GaugeFieldEnergyWeight);

            // Sync results
            _gpuPhysicsEngine.SyncStressEnergyToHost(_gpuStressEnergyHost);
        }

        /// <summary>
        /// Get GPU-computed stress-energy tensor for edge (i, j).
        /// Call ComputeAllStressEnergyGpu() first.
        /// </summary>
        public double GetStressEnergyTensorGpu(int i, int j)
        {
            if (_gpuStressEnergyHost == null)
                return GetStressEnergyTensor(i, j); // Fallback to CPU

            int edgeIdx = GetEdgeIndex(i, j);
            if (edgeIdx < 0 || edgeIdx >= _gpuStressEnergyHost.Length)
                return 0.0;

            return _gpuStressEnergyHost[edgeIdx];
        }

        /// <summary>
        /// Update all node mass models using GPU.
        /// Much faster than sequential CPU computation.
        /// </summary>
        public void UpdateNodeMassModelsGpu()
        {
            if (_gpuPhysicsEngine == null || _gpuNodeMassesHost == null)
            {
                UpdateNodeMassModels(); // Fallback to CPU
                return;
            }

            // Upload current field data (if not already done)
            // Note: For efficiency, call ComputeAllStressEnergyGpu() first which uploads data

            // Compute on GPU
            _gpuPhysicsEngine.ComputeNodeMassesGpu(
                (float)ScalarMass,
                (float)HiggsMuSquared,
                (float)HiggsLambda,
                UseMexicanHatPotential,
                (float)PhysicsConstants.CosmologicalConstant);

            // Sync results
            _gpuPhysicsEngine.SyncNodeMassesToHost(_gpuNodeMassesHost);

            // Update NodeMasses from GPU results
            var masses = NodeMasses;
            for (int i = 0; i < N; i++)
            {
                masses[i].Reset();
                // GPU computes unified total mass
                // We store it as correlation mass for simplicity
                masses[i].CorrelationMass = _gpuNodeMassesHost[i];
            }
        }

        /// <summary>
        /// Compute lapse function for all nodes using GPU.
        /// Results are cached and can be accessed via GetLocalLapseGpu(node).
        /// </summary>
        public void ComputeLapseFunctionsGpu()
        {
            if (_gpuPhysicsEngine == null || _gpuLapseHost == null)
            {
                UpdateLapseFunctions(); // Fallback to CPU
                return;
            }

            // Compute on GPU
            _gpuPhysicsEngine.ComputeLapseFunctionGpu(
                (float)_avgCurvature,
                (float)_avgCorrelationMass);

            // Sync results
            _gpuPhysicsEngine.SyncLapseToHost(_gpuLapseHost);

            // Update CPU cache
            if (_lapseFunction == null || _lapseFunction.Length != N)
                _lapseFunction = new double[N];

            for (int i = 0; i < N; i++)
                _lapseFunction[i] = _gpuLapseHost[i];
        }

        /// <summary>
        /// Get GPU-computed lapse function for a node.
        /// Call ComputeLapseFunctionsGpu() first, or use GetLocalLapse() for CPU fallback.
        /// </summary>
        public double GetLocalLapseGpu(int node)
        {
            if (_gpuLapseHost != null && node >= 0 && node < _gpuLapseHost.Length)
                return _gpuLapseHost[node];

            return GetLocalLapse(node); // Fallback to CPU
        }

        /// <summary>
        /// Compute volume metrics (edge count, total weight) using GPU parallel reduction.
        /// Much faster than sequential iteration for large graphs.
        /// </summary>
        public (int edgeCount, double totalWeight) ComputeVolumeMetricsGpu()
        {
            if (_gpuPhysicsEngine == null)
            {
                // Fallback to CPU
                int count = CountEdges();
                double weight = TotalEdgeWeight();
                return (count, weight);
            }

            var (edgeCount, totalWeight) = _gpuPhysicsEngine.ComputeVolumeMetricsGpu();

            // Decode fixed-point weight (was multiplied by 10000 in shader)
            double decodedWeight = totalWeight / 10000.0;

            return (edgeCount, decodedWeight);
        }

        /// <summary>
        /// Compute volume penalty using GPU-accelerated metrics.
        /// </summary>
        public double ComputeVolumePenaltyGpu()
        {
            if (!_volumeConstraintInitialized || _volumeLambda <= 0)
                return 0.0;

            var (currentEdges, currentWeight) = ComputeVolumeMetricsGpu();

            double edgeDeviation = currentEdges - _targetEdgeCount;
            double weightDeviation = currentWeight - _targetTotalWeight;

            double normalizedEdgeDev = _targetEdgeCount > 0 ? edgeDeviation / _targetEdgeCount : edgeDeviation;
            double normalizedWeightDev = _targetTotalWeight > 0 ? weightDeviation / _targetTotalWeight : weightDeviation;

            return _volumeLambda * (normalizedEdgeDev * normalizedEdgeDev
                                  + normalizedWeightDev * normalizedWeightDev);
        }

        /// <summary>
        /// Perform a complete GPU-accelerated physics update step.
        /// Combines stress-energy, gravity, and field updates.
        /// 
        /// This is the recommended method for maximum performance.
        /// </summary>
        /// <param name="dt">Time step</param>
        /// <param name="effectiveG">Gravitational coupling</param>
        public void StepPhysicsGpuUnified(double dt, double effectiveG)
        {
            // 1. Update node masses on GPU
            UpdateNodeMassModelsGpu();

            // 2. Compute stress-energy tensor on GPU
            ComputeAllStressEnergyGpu();

            // 3. Update lapse functions on GPU (for event-driven time)
            ComputeLapseFunctionsGpu();

            // 4. Evolve gravity using existing GPU engine
            // Note: GpuGravity engine is separate from GpuPhysicsEngine
            if (GpuGravity != null && GpuGravity.IsTopologyInitialized)
            {
                // Use full GPU gravity step
                int edgeCount = FlatEdgesFrom.Length;
                float[] weights = GetAllWeightsFlat();
                float[] masses = GetNodeMasses();

                GpuGravity.EvolveFullGpuStep(
                    weights, masses,
                    FlatEdgesFrom, FlatEdgesTo,
                    (float)dt,
                    (float)effectiveG,
                    (float)PhysicsConstants.CosmologicalConstant);

                UpdateWeightsFromFlat(weights);
            }
            else
            {
                // CPU fallback
                ImprovedNetworkGravity.EvolveNetworkGeometryOllivierDynamic(this, dt, effectiveG);
            }

            // 5. Update target distances
            UpdateTargetDistancesFromWeights();
        }
    }
}
