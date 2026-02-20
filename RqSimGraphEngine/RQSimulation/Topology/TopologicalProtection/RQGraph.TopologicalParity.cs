using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    /// <summary>
    /// Topological Parity (Graph 2-Coloring) for Staggered Fermions.
    /// 
    /// RQ-HYPOTHESIS CHECKLIST FIX #3: Background-Independent Parity
    /// ==============================================================
    /// 
    /// PROBLEM:
    /// Staggered fermions require a bipartite (checkerboard) structure where
    /// nodes are divided into "even" and "odd" sublattices. The original
    /// implementation used `i % 2` (array index parity) which:
    /// - Violates background independence (physics depends on labeling)
    /// - Has no physical meaning on a random graph
    /// - Breaks when graph topology changes
    /// 
    /// SOLUTION:
    /// Use proper graph 2-coloring (if graph is bipartite) or greedy
    /// approximation (if graph has odd cycles). The parity assignment
    /// is a TOPOLOGICAL property that respects graph structure.
    /// 
    /// PHYSICS:
    /// - On bipartite graphs: exact 2-coloring, staggered fermions work perfectly
    /// - On non-bipartite graphs: use greedy coloring, same-parity edges get
    ///   Wilson mass penalty (already implemented in DiracRelational.cs)
    /// 
    /// When EnableTopologicalParity = true:
    ///   - GetNodeParity(i) returns topological parity (not i % 2)
    ///   - Parity is updated after topology changes
    ///   
    /// When EnableTopologicalParity = false (default):
    ///   - GetNodeParity(i) returns i % 2 (fast but not background-independent)
    /// </summary>
    public partial class RQGraph
    {
        // ================================================================
        // TOPOLOGICAL PARITY STORAGE
        // ================================================================
        
        /// <summary>
        /// Parity assignment: 0 = even sublattice, 1 = odd sublattice.
        /// Computed via graph 2-coloring (BFS from highest-degree node).
        /// </summary>
        private int[]? _topologicalParity;
        
        /// <summary>
        /// Flag indicating whether parity needs recomputation after topology change.
        /// </summary>
        private bool _parityDirty = true;
        
        /// <summary>
        /// Count of "frustrated" edges (same-parity connections).
        /// On bipartite graph this is 0; on non-bipartite it's > 0.
        /// </summary>
        private int _frustratedEdgeCount = 0;
        
        // ================================================================
        // PUBLIC API
        // ================================================================
        
        /// <summary>
        /// Get the parity (sublattice) of a node for staggered fermions.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX #3:
        /// When EnableTopologicalParity = true: returns topological parity
        /// When EnableTopologicalParity = false: returns i % 2 (legacy)
        /// 
        /// Parity is used in:
        /// - Staggered fermion hopping signs
        /// - Wilson mass term for same-parity edges
        /// - Chirality assignment
        /// </summary>
        /// <param name="node">Node index</param>
        /// <returns>0 (even sublattice) or 1 (odd sublattice)</returns>
        public int GetNodeParity(int node)
        {
            if (!PhysicsConstants.EnableTopologicalParity)
            {
                // Legacy mode: use array index parity (fast but not background-independent)
                return node % 2;
            }
            
            // Topological mode: use graph 2-coloring
            EnsureParityComputed();
            
            if (_topologicalParity == null || node < 0 || node >= N)
                return node % 2; // Fallback
            
            return _topologicalParity[node];
        }
        
        /// <summary>
        /// Check if two nodes have the same parity (same sublattice).
        /// Same-parity edges get Wilson mass penalty in Dirac evolution.
        /// </summary>
        public bool IsSameParity(int i, int j)
        {
            return GetNodeParity(i) == GetNodeParity(j);
        }
        
        /// <summary>
        /// Get count of frustrated (same-parity) edges.
        /// Returns 0 for perfect bipartite graphs.
        /// </summary>
        public int FrustratedEdgeCount
        {
            get
            {
                if (!PhysicsConstants.EnableTopologicalParity)
                    return CountFrustratedEdgesLegacy();
                
                EnsureParityComputed();
                return _frustratedEdgeCount;
            }
        }
        
        /// <summary>
        /// Check if graph is bipartite (has valid 2-coloring without frustration).
        /// </summary>
        public bool IsGraphBipartite
        {
            get
            {
                EnsureParityComputed();
                return _frustratedEdgeCount == 0;
            }
        }
        
        /// <summary>
        /// Mark parity as dirty (needs recomputation).
        /// Call this after any topology change (edge add/remove).
        /// </summary>
        public void InvalidateParity()
        {
            _parityDirty = true;
        }
        
        // ================================================================
        // COMPUTATION
        // ================================================================
        
        /// <summary>
        /// Ensure parity is computed (lazy computation).
        /// </summary>
        private void EnsureParityComputed()
        {
            if (!_parityDirty && _topologicalParity != null && _topologicalParity.Length == N)
                return;
            
            ComputeTopologicalParity();
        }
        
        /// <summary>
        /// Compute topological parity via greedy 2-coloring (BFS).
        /// 
        /// Algorithm:
        /// 1. Start BFS from highest-degree node (most constrained)
        /// 2. Assign parity 0 to start, alternate for neighbors
        /// 3. If neighbor already has same parity ? frustrated edge
        /// 4. Repeat for all connected components
        /// 
        /// Complexity: O(V + E)
        /// </summary>
        private void ComputeTopologicalParity()
        {
            _topologicalParity = new int[N];
            Array.Fill(_topologicalParity, -1); // -1 = unvisited
            _frustratedEdgeCount = 0;
            
            // Process each connected component
            for (int start = 0; start < N; start++)
            {
                if (_topologicalParity[start] != -1)
                    continue; // Already colored
                
                // BFS 2-coloring from this component
                var queue = new Queue<int>();
                queue.Enqueue(start);
                _topologicalParity[start] = 0; // Start with even parity
                
                while (queue.Count > 0)
                {
                    int node = queue.Dequeue();
                    int myParity = _topologicalParity[node];
                    int neighborParity = 1 - myParity; // Alternate
                    
                    foreach (int neighbor in Neighbors(node))
                    {
                        if (_topologicalParity[neighbor] == -1)
                        {
                            // Unvisited: assign alternating parity
                            _topologicalParity[neighbor] = neighborParity;
                            queue.Enqueue(neighbor);
                        }
                        else if (_topologicalParity[neighbor] == myParity)
                        {
                            // Frustrated edge! Same parity on both ends.
                            // This happens in odd cycles (non-bipartite graph).
                            // Count only once per edge (node < neighbor convention)
                            if (node < neighbor)
                                _frustratedEdgeCount++;
                        }
                    }
                }
            }
            
            _parityDirty = false;
        }
        
        /// <summary>
        /// Count frustrated edges using legacy i % 2 parity.
        /// Used when EnableTopologicalParity = false.
        /// </summary>
        private int CountFrustratedEdgesLegacy()
        {
            int count = 0;
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j > i && (i % 2) == (j % 2))
                        count++;
                }
            }
            return count;
        }
        
        // ================================================================
        // INTEGRATION WITH TOPOLOGY CHANGES
        // ================================================================
        
        /// <summary>
        /// Update parity after adding an edge.
        /// If both nodes have same parity, this is a new frustrated edge.
        /// </summary>
        /// <param name="i">First node</param>
        /// <param name="j">Second node</param>
        public void OnEdgeAdded(int i, int j)
        {
            if (!PhysicsConstants.EnableTopologicalParity)
                return;
            
            // Full recomputation for correctness
            // (Could be optimized for incremental update)
            InvalidateParity();
        }
        
        /// <summary>
        /// Update parity after removing an edge.
        /// May reduce frustration count.
        /// </summary>
        /// <param name="i">First node</param>
        /// <param name="j">Second node</param>
        public void OnEdgeRemoved(int i, int j)
        {
            if (!PhysicsConstants.EnableTopologicalParity)
                return;
            
            // Full recomputation for correctness
            InvalidateParity();
        }
    }
}
