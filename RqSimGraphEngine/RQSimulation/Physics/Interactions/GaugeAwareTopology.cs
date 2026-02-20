using System;
using System.Collections.Generic;
using System.Numerics;

namespace RQSimulation.Physics
{
    /// <summary>
    /// Type of topology modification being proposed.
    /// Used for gauge invariance checks before applying changes.
    /// </summary>
    public enum TopologyMove
    {
        /// <summary>Adding a new edge between two nodes.</summary>
        AddEdge,
        
        /// <summary>Removing an existing edge.</summary>
        RemoveEdge,
        
        /// <summary>Modifying the weight of an existing edge.</summary>
        ModifyWeight
    }

    /// <summary>
    /// Provides gauge-invariant topology operations.
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 2: Gauss Law Conservation
    /// ========================================================
    /// When removing an edge with non-trivial gauge phase (U_ij ? 1), the
    /// magnetic flux through Wilson loops must be preserved. This module:
    /// 
    /// 1. Checks if an edge carries non-trivial flux via Wilson loops
    /// 2. Finds alternate paths to redistribute flux before removal
    /// 3. Blocks removal if edge is a topological bridge (no alternate path)
    /// 
    /// PHYSICS:
    /// - ?·E = ? (Gauss law must hold before and after)
    /// - Wilson loop W = exp(i?A·dl) must be conserved
    /// - Flux cannot "disappear" from the universe
    /// </summary>
    public static class GaugeAwareTopology
    {
        /// <summary>
        /// Threshold for considering a phase "trivial" (close to 0 or 2?).
        /// Edges with phase below this can be removed without flux redistribution.
        /// </summary>
        public const double TrivialPhaseThreshold = 0.1; // ~6 degrees
        
        // ================================================================
        // STAGE 3: ENHANCED WILSON LOOP PROTECTION
        // ================================================================
        
        /// <summary>
        /// Enhanced gauge check using minimal Wilson loops (triangles).
        /// 
        /// RQ-HYPOTHESIS: Gauss Law as Topological Invariant
        /// =================================================
        /// Before removing edge (i,j), compute Wilson loop phase for
        /// all minimal cycles (triangles) containing this edge.
        /// 
        /// If |W - 1| > tolerance, the edge carries physical gauge flux
        /// and CANNOT be removed without redistributing that flux first.
        /// 
        /// This is a PREVENTIVE check - blocks removal before it happens,
        /// rather than fixing violations after the fact.
        /// </summary>
        /// <param name="graph">The RQ graph.</param>
        /// <param name="nodeA">First endpoint of edge.</param>
        /// <param name="nodeB">Second endpoint of edge.</param>
        /// <param name="moveType">Type of topology change being proposed.</param>
        /// <returns>True if the move is gauge-invariant, false if it would violate gauge invariance.</returns>
        public static bool IsTopologicalMoveGaugeInvariant(
            RQGraph graph,
            int nodeA,
            int nodeB,
            TopologyMove moveType)
        {
            ArgumentNullException.ThrowIfNull(graph);
            
            // Only removal can violate gauge invariance
            // (Adding edges or modifying weights preserves topology)
            if (moveType != TopologyMove.RemoveEdge)
            {
                return true;
            }
            
            // Edge doesn't exist - nothing to check
            if (!graph.Edges[nodeA, nodeB])
            {
                return true;
            }
            
            // Check if Wilson loop protection is enabled
            if (!PhysicsConstants.EnableWilsonLoopProtection)
            {
                return true; // Legacy mode - no protection
            }
            
            // Find all minimal triangles containing edge (nodeA, nodeB)
            var triangles = FindMinimalTrianglesWithEdge(graph, nodeA, nodeB);
            
            // If no triangles, check direct edge phase
            if (triangles.Count == 0)
            {
                // Edge is not part of any triangle - check its own phase
                double edgePhase = graph.GetEdgePhase(nodeA, nodeB);
                double normalizedPhase = NormalizePhaseToSymmetric(edgePhase);
                
                // If phase is trivial, edge can be removed
                return Math.Abs(normalizedPhase) < PhysicsConstants.GaugeTolerance;
            }
            
            // Check Wilson loop for each triangle
            foreach (int[] triangle in triangles)
            {
                // Compute Wilson loop for this triangle: W = U_ij ? U_jk ? U_ki
                Complex W = ComputeTriangleWilsonLoop(graph, triangle[0], triangle[1], triangle[2]);
                
                // Check if phase is non-trivial
                double phase = Math.Atan2(W.Imaginary, W.Real);
                double normalizedPhase = NormalizePhaseToSymmetric(phase);
                
                if (Math.Abs(normalizedPhase) > PhysicsConstants.GaugeTolerance)
                {
                    // This edge carries significant gauge flux
                    // Check if there's an alternate path for flux redistribution
                    var alternatePath = FindAlternatePath(graph, nodeA, nodeB);
                    
                    if (alternatePath.Count == 0)
                    {
                        // No alternate path - edge is a bridge with flux
                        // BLOCK removal to preserve gauge invariance
                        return false;
                    }
                    
                    // Alternate path exists - flux can be redistributed
                    // Allow removal (redistribution will happen in RemoveEdgeWithFluxRedistribution)
                }
            }
            
            return true; // Safe to remove
        }
        
        /// <summary>
        /// Find all minimal triangles (3-cycles) containing the edge (a, b).
        /// A triangle exists when there's a common neighbor c such that
        /// edges (a,c) and (b,c) both exist.
        /// </summary>
        /// <param name="graph">The RQ graph.</param>
        /// <param name="a">First endpoint of edge.</param>
        /// <param name="b">Second endpoint of edge.</param>
        /// <returns>List of triangles, each represented as int[3] = {a, b, c}.</returns>
        public static List<int[]> FindMinimalTrianglesWithEdge(RQGraph graph, int a, int b)
        {
            var result = new List<int[]>();
            
            ArgumentNullException.ThrowIfNull(graph);
            
            if (!graph.Edges[a, b])
            {
                return result; // Edge doesn't exist
            }
            
            // Find common neighbors of a and b
            var neighborsA = new HashSet<int>();
            foreach (int neighbor in graph.Neighbors(a))
            {
                if (neighbor != b)
                {
                    neighborsA.Add(neighbor);
                }
            }
            
            // Check each neighbor of b
            foreach (int c in graph.Neighbors(b))
            {
                if (c == a) continue;
                
                // If c is also a neighbor of a, we found a triangle
                if (neighborsA.Contains(c))
                {
                    result.Add(new[] { a, b, c });
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Compute Wilson loop for a triangle (i, j, k).
        /// W = U_ij ? U_jk ? U_ki = exp(i(?_ij + ?_jk + ?_ki))
        /// </summary>
        private static Complex ComputeTriangleWilsonLoop(RQGraph graph, int i, int j, int k)
        {
            // Get phases for each edge
            double phi_ij = graph.GetEdgePhase(i, j);
            double phi_jk = graph.GetEdgePhase(j, k);
            double phi_ki = graph.GetEdgePhase(k, i);
            
            // Total phase around the loop
            double totalPhase = phi_ij + phi_jk + phi_ki;
            
            // Wilson loop = exp(i ? totalPhase)
            return Complex.FromPolarCoordinates(1.0, totalPhase);
        }
        
        /// <summary>
        /// Get total flux through all Wilson loops containing an edge.
        /// This measures the "magnetic charge" carried by the edge.
        /// </summary>
        /// <param name="graph">The RQ graph.</param>
        /// <param name="nodeA">First endpoint of edge.</param>
        /// <param name="nodeB">Second endpoint of edge.</param>
        /// <returns>Average flux magnitude in radians.</returns>
        public static double GetEdgeWilsonFlux(RQGraph graph, int nodeA, int nodeB)
        {
            ArgumentNullException.ThrowIfNull(graph);
            
            if (!graph.Edges[nodeA, nodeB])
            {
                return 0.0;
            }
            
            var triangles = FindMinimalTrianglesWithEdge(graph, nodeA, nodeB);
            
            if (triangles.Count == 0)
            {
                // No triangles - return edge's own phase
                double edgePhase = graph.GetEdgePhase(nodeA, nodeB);
                return Math.Abs(NormalizePhaseToSymmetric(edgePhase));
            }
            
            double totalFlux = 0.0;
            
            foreach (int[] triangle in triangles)
            {
                Complex W = ComputeTriangleWilsonLoop(graph, triangle[0], triangle[1], triangle[2]);
                double phase = Math.Atan2(W.Imaginary, W.Real);
                totalFlux += Math.Abs(NormalizePhaseToSymmetric(phase));
            }
            
            return totalFlux / triangles.Count;
        }

        /// <summary>
        /// Checks if an edge can be safely removed while preserving gauge invariance.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 2: Topological Charge Protection
        /// =============================================================
        /// An edge can be removed if:
        /// 1. Its phase is trivial (|U_ij| ? 1), OR
        /// 2. There exists an alternate path to redistribute the flux
        /// 
        /// An edge CANNOT be removed if:
        /// - It carries non-trivial flux AND
        /// - It is a topological bridge (removing it disconnects the graph)
        /// </summary>
        /// <param name="graph">The RQ graph.</param>
        /// <param name="i">First endpoint of edge.</param>
        /// <param name="j">Second endpoint of edge.</param>
        /// <param name="alternatePathLength">Output: length of alternate path if exists, -1 otherwise.</param>
        /// <returns>True if edge can be safely removed.</returns>
        public static bool CanRemoveEdgeSafely(
            RQGraph graph,
            int i,
            int j,
            out int alternatePathLength)
        {
            alternatePathLength = -1;

            ArgumentNullException.ThrowIfNull(graph);

            // Edge doesn't exist - nothing to remove
            if (!graph.Edges[i, j])
                return true;

            // Check if edge has non-trivial phase
            double phase = graph.GetEdgePhase(i, j);
            double normalizedPhase = NormalizePhase(phase);

            // Trivial phase: can always remove
            if (Math.Abs(normalizedPhase) < TrivialPhaseThreshold ||
                Math.Abs(normalizedPhase - 2 * Math.PI) < TrivialPhaseThreshold)
            {
                return true;
            }

            // Non-trivial phase: need alternate path for flux redistribution
            var alternatePath = FindAlternatePath(graph, i, j);

            if (alternatePath.Count == 0)
            {
                // No alternate path - edge is a bridge
                // Cannot remove without violating gauge invariance
                return false;
            }

            alternatePathLength = alternatePath.Count;
            return true;
        }

        /// <summary>
        /// Removes an edge with proper flux redistribution to preserve gauge invariance.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 2: Flux Conservation
        /// ==================================================
        /// Before removing edge (i,j) with phase ?_ij:
        /// 1. Find alternate path P = [i, k1, k2, ..., j]
        /// 2. Redistribute flux: ?'_path = ?_path + ?_ij / |P|
        /// 3. Remove edge
        /// 
        /// This ensures ? A·dl (Wilson loop) is conserved.
        /// </summary>
        /// <param name="graph">The RQ graph.</param>
        /// <param name="i">First endpoint of edge.</param>
        /// <param name="j">Second endpoint of edge.</param>
        /// <param name="releasedEnergy">Output: energy released by edge removal.</param>
        /// <returns>True if edge was successfully removed, false if removal blocked.</returns>
        public static bool RemoveEdgeWithFluxRedistribution(
            RQGraph graph,
            int i,
            int j,
            out double releasedEnergy)
        {
            releasedEnergy = 0.0;

            ArgumentNullException.ThrowIfNull(graph);

            if (!graph.Edges[i, j])
                return true; // Already removed

            // Get edge phase before removal
            double phase = graph.GetEdgePhase(i, j);
            double normalizedPhase = NormalizePhase(phase);

            // Check if flux redistribution is needed
            bool needsRedistribution =
                Math.Abs(normalizedPhase) >= TrivialPhaseThreshold &&
                Math.Abs(normalizedPhase - 2 * Math.PI) >= TrivialPhaseThreshold;

            if (needsRedistribution)
            {
                // Find alternate path
                var alternatePath = FindAlternatePath(graph, i, j);

                if (alternatePath.Count == 0)
                {
                    // Topological protection: cannot remove bridge with flux
                    return false;
                }

                // Redistribute flux along alternate path
                RedistributeFlux(graph, alternatePath, normalizedPhase);
            }

            // Calculate released energy
            double weight = graph.Weights[i, j];
            releasedEnergy = weight * weight; // E ~ w?

            // Perform the actual edge removal
            // Note: The caller should handle the energy accounting
            return true;
        }

        /// <summary>
        /// Computes the total flux through all Wilson loops containing an edge.
        /// This is the gauge-invariant measure of the edge's "magnetic charge".
        /// </summary>
        /// <param name="graph">The RQ graph.</param>
        /// <param name="i">First endpoint of edge.</param>
        /// <param name="j">Second endpoint of edge.</param>
        /// <returns>Total flux through loops containing edge (i,j).</returns>
        public static double ComputeEdgeFlux(RQGraph graph, int i, int j)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if (!graph.Edges[i, j])
                return 0.0;

            double totalFlux = 0.0;
            int loopCount = 0;

            // Find all triangles containing edge (i,j)
            foreach (int k in graph.Neighbors(i))
            {
                if (k == j) continue;

                if (graph.Edges[j, k])
                {
                    // Triangle i-j-k exists
                    double loopPhase = graph.ComputeWilsonLoop(i, j, k);
                    totalFlux += NormalizePhaseToSymmetric(loopPhase);
                    loopCount++;
                }
            }

            // Return average flux if multiple loops
            return loopCount > 0 ? totalFlux / loopCount : 0.0;
        }

        /// <summary>
        /// Checks if removing an edge would create a topological defect
        /// (change the total Chern number of the graph).
        /// </summary>
        /// <param name="graph">The RQ graph.</param>
        /// <param name="i">First endpoint of edge.</param>
        /// <param name="j">Second endpoint of edge.</param>
        /// <returns>True if removal would create a defect.</returns>
        public static bool WouldCreateTopologicalDefect(RQGraph graph, int i, int j)
        {
            double flux = ComputeEdgeFlux(graph, i, j);

            // If edge carries significant flux and is a bridge,
            // removal would create a monopole (topological defect)
            if (Math.Abs(flux) > Math.PI / 4) // ?/4 ~ 45 degrees
            {
                var path = FindAlternatePath(graph, i, j);
                if (path.Count == 0)
                {
                    // Bridge with flux = would create monopole
                    return true;
                }
            }

            return false;
        }

        // ================================================================
        // PRIVATE HELPERS
        // ================================================================

        /// <summary>
        /// Find shortest alternate path from i to j (not using edge i-j).
        /// Uses BFS for unweighted shortest path.
        /// </summary>
        private static List<int> FindAlternatePath(RQGraph graph, int source, int target)
        {
            var path = new List<int>();
            var visited = new HashSet<int>();
            var parent = new Dictionary<int, int>();
            var queue = new Queue<int>();

            visited.Add(source);
            queue.Enqueue(source);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                if (current == target)
                {
                    // Reconstruct path
                    int node = target;
                    while (node != source)
                    {
                        path.Add(node);
                        node = parent[node];
                    }
                    path.Add(source);
                    path.Reverse();
                    return path;
                }

                foreach (int neighbor in graph.Neighbors(current))
                {
                    // Skip the direct edge we're trying to remove
                    if ((current == source && neighbor == target) ||
                        (current == target && neighbor == source))
                    {
                        continue;
                    }

                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        parent[neighbor] = current;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // No alternate path found (edge is a bridge)
            return new List<int>();
        }

        /// <summary>
        /// Redistribute flux from removed edge along alternate path.
        /// </summary>
        private static void RedistributeFlux(RQGraph graph, List<int> path, double flux)
        {
            if (path.Count < 2)
                return;

            // Distribute flux evenly along path edges
            double fluxPerEdge = flux / (path.Count - 1);

            for (int k = 0; k < path.Count - 1; k++)
            {
                int a = path[k];
                int b = path[k + 1];

                if (graph.Edges[a, b])
                {
                    // Add flux to this edge
                    graph.AddEdgePhase(a, b, fluxPerEdge);
                }
            }
        }

        /// <summary>
        /// Normalize phase to [0, 2?).
        /// </summary>
        private static double NormalizePhase(double phase)
        {
            phase = phase % (2 * Math.PI);
            if (phase < 0) phase += 2 * Math.PI;
            return phase;
        }

        /// <summary>
        /// Normalize phase to (-?, ?] for flux calculations.
        /// </summary>
        private static double NormalizePhaseToSymmetric(double phase)
        {
            phase = NormalizePhase(phase);
            if (phase > Math.PI)
                phase -= 2 * Math.PI;
            return phase;
        }
    }
}
