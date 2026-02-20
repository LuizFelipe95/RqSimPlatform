using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // === GPU Acceleration Helper Methods ===
        
        /// <summary>
        /// Cached flat arrays for GPU edge indices.
        /// These are built once and reused as long as topology doesn't change.
        /// </summary>
        private int[]? _flatEdgesFrom;
        private int[]? _flatEdgesTo;
        private int _flatEdgeCacheVersion = -1; // Track topology changes
        
        /// <summary>
        /// Public accessors for flat edge arrays (required by GPU engine).
        /// Arrays are lazily computed and cached based on _topologyVersion.
        /// When topology changes (via InvalidateTopologyCache), arrays are automatically rebuilt on next access.
        /// </summary>
        public int[] FlatEdgesFrom
        {
            get
            {
                if (_flatEdgesFrom == null || _flatEdgeCacheVersion != _topologyVersion)
                    RebuildFlatEdgeArrays();
                return _flatEdgesFrom!;
            }
        }
        
        public int[] FlatEdgesTo
        {
            get
            {
                if (_flatEdgesTo == null || _flatEdgeCacheVersion != _topologyVersion)
                    RebuildFlatEdgeArrays();
                return _flatEdgesTo!;
            }
        }
        
        // Topology version counter (incremented when edges change)
        private int _topologyVersion = 0;
        
        /// <summary>
        /// Call this method when edges are added or removed from the graph.
        /// Invalidates cached flat edge arrays by incrementing the topology version.
        /// The arrays will be automatically rebuilt on next access to FlatEdgesFrom/FlatEdgesTo.
        /// 
        /// Should be called from:
        /// - AddEdge() methods
        /// - RemoveEdge() methods
        /// - Any topology modification operations
        /// </summary>
        public void InvalidateTopologyCache()
        {
            _topologyVersion++;
        }
        
        /// <summary>
        /// Build flat arrays of edge indices for GPU processing.
        /// Each edge (i,j) where i less than j is stored as one entry.
        /// </summary>
        private void RebuildFlatEdgeArrays()
        {
            // Count edges
            int edgeCount = 0;
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j > i) edgeCount++;
                }
            }
            
            _flatEdgesFrom = new int[edgeCount];
            _flatEdgesTo = new int[edgeCount];
            
            int idx = 0;
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j > i)
                    {
                        _flatEdgesFrom[idx] = i;
                        _flatEdgesTo[idx] = j;
                        idx++;
                    }
                }
            }
            
            _flatEdgeCacheVersion = _topologyVersion;
        }
        
        /// <summary>
        /// Get all edge weights as a flat array for GPU processing.
        /// Order matches FlatEdgesFrom/FlatEdgesTo arrays.
        /// </summary>
        public float[] GetAllWeightsFlat()
        {
            var edgesFrom = FlatEdgesFrom; // Ensure arrays are built
            var edgesTo = FlatEdgesTo;
            int edgeCount = edgesFrom.Length;
            
            float[] weights = new float[edgeCount];
            for (int e = 0; e < edgeCount; e++)
            {
                int i = edgesFrom[e];
                int j = edgesTo[e];
                weights[e] = (float)Weights[i, j];
            }
            
            return weights;
        }
        
        /// <summary>
        /// Get all edge curvatures as a flat array for GPU processing.
        /// Uses Forman-Ricci curvature (computed on CPU for now).
        /// Order matches FlatEdgesFrom/FlatEdgesTo arrays.
        /// </summary>
        public float[] GetAllCurvaturesFlat()
        {
            var edgesFrom = FlatEdgesFrom;
            var edgesTo = FlatEdgesTo;
            int edgeCount = edgesFrom.Length;
            
            float[] curvatures = new float[edgeCount];
            for (int e = 0; e < edgeCount; e++)
            {
                int i = edgesFrom[e];
                int j = edgesTo[e];
                // Use ComputeFormanRicciCurvature from RQGraph.UnifiedEnergy.cs
                curvatures[e] = (float)ComputeFormanRicciCurvature(i, j);
            }
            
            return curvatures;
        }
        
        /// <summary>
        /// Get node masses as a flat array for GPU processing.
        /// 
        /// RQ-HYPOTHESIS FIX: Now returns NodeMasses[i].TotalMass which includes
        /// ALL field contributions (fermion, scalar, gauge, correlation, vacuum),
        /// not just topological correlation mass.
        /// 
        /// This fixes the critical gravity-matter decoupling issue where gravity
        /// was ignoring scalar field (Higgs), fermion (Dirac), and gauge field
        /// energy contributions. The Einstein equations G_?? = 8?G T_??
        /// require the FULL stress-energy tensor as source.
        /// </summary>
        public float[] GetNodeMasses()
        {
            // RQ-FIX: Update unified node mass models from all fields BEFORE gravity step
            // This ensures gravity sees matter from scalar, fermion, gauge fields
            // UpdateNodeMasses is defined in RQGraph.UnifiedMass.cs
            UpdateNodeMasses();
            
            float[] masses = new float[N];
            
            // Use GetNodeTotalMass from UnifiedMass.cs which includes ALL field contributions
            for (int i = 0; i < N; i++)
            {
                masses[i] = (float)GetNodeTotalMass(i);
            }
            
            return masses;
        }
        
        /// <summary>
        /// Update graph weights from flat array (after GPU processing).
        /// Order must match FlatEdgesFrom/FlatEdgesTo arrays.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when weights array length doesn't match edge count</exception>
        public void UpdateWeightsFromFlat(float[] weights)
        {
            var edgesFrom = FlatEdgesFrom;
            var edgesTo = FlatEdgesTo;
            
            // Validate array lengths match
            if (weights.Length != edgesFrom.Length || weights.Length != edgesTo.Length)
            {
                throw new ArgumentException(
                    $"Array length mismatch: weights.Length={weights.Length}, " +
                    $"edgesFrom.Length={edgesFrom.Length}, edgesTo.Length={edgesTo.Length}. " +
                    $"All arrays must have the same length.");
            }
            
            int edgeCount = weights.Length;
            
            for (int e = 0; e < edgeCount; e++)
            {
                int i = edgesFrom[e];
                int j = edgesTo[e];
                double w = weights[e];
                Weights[i, j] = w;
                Weights[j, i] = w; // Symmetric
            }
        }
        
        // === Edge Index Lookup for GPU ===
        
        /// <summary>
        /// Cached dictionary for fast edge index lookup by node pair.
        /// Key: (min(i,j), max(i,j)), Value: edge index in flat arrays.
        /// </summary>
        private Dictionary<(int, int), int>? _edgeIndexCache;
        private int _edgeIndexCacheVersion = -1;
        
        /// <summary>
        /// Get the flat array index for an edge (i,j).
        /// Returns -1 if the edge doesn't exist.
        /// 
        /// This is required by the GPU curvature shader for CSR structure building.
        /// </summary>
        /// <param name="i">First node of edge</param>
        /// <param name="j">Second node of edge</param>
        /// <returns>Edge index in FlatEdgesFrom/FlatEdgesTo arrays, or -1 if not found</returns>
        public int GetEdgeIndex(int i, int j)
        {
            // Ensure cache is up to date
            if (_edgeIndexCache == null || _edgeIndexCacheVersion != _topologyVersion)
            {
                RebuildEdgeIndexCache();
            }
            
            // Normalize to (min, max) for undirected edge lookup
            var key = i < j ? (i, j) : (j, i);
            
            if (_edgeIndexCache!.TryGetValue(key, out int edgeIndex))
            {
                return edgeIndex;
            }
            
            return -1; // Edge not found
        }
        
        /// <summary>
        /// Rebuild the edge index cache from flat edge arrays.
        /// Called automatically when topology version changes.
        /// </summary>
        private void RebuildEdgeIndexCache()
        {
            // Ensure flat edge arrays are built
            var edgesFrom = FlatEdgesFrom;
            var edgesTo = FlatEdgesTo;
            
            _edgeIndexCache = new Dictionary<(int, int), int>(edgesFrom.Length);
            
            for (int e = 0; e < edgesFrom.Length; e++)
            {
                int i = edgesFrom[e];
                int j = edgesTo[e];
                
                // Store with normalized key (min, max)
                var key = i < j ? (i, j) : (j, i);
                _edgeIndexCache[key] = e;
            }
            
            _edgeIndexCacheVersion = _topologyVersion;
        }
    }
}
