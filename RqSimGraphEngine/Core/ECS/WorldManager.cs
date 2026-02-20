using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;

namespace RQSimulation.Core.ECS;

/// <summary>
/// Manages the Arch ECS World for high-performance graph simulation.
/// 
/// RESPONSIBILITIES:
/// - Initialize and maintain the ECS World
/// - Convert RQGraph data to ECS entities
/// - Synchronize ECS state with simulation updates
/// - Provide efficient queries for rendering systems
/// 
/// MEMORY OPTIMIZATION:
/// - Entities are created in batches to minimize allocation overhead
/// - Components are stored in contiguous memory (Archetype pattern)
/// </summary>
public sealed class WorldManager : IDisposable
{
    private World? _world;
    private bool _disposed;
    
    // Entity tracking
    private int _nodeEntityCount;
    private int _edgeEntityCount;
    
    /// <summary>
    /// The ECS World instance.
    /// </summary>
    public World World => _world ?? throw new InvalidOperationException("WorldManager not initialized");
    
    /// <summary>
    /// Number of node entities in the world.
    /// </summary>
    public int NodeCount => _nodeEntityCount;
    
    /// <summary>
    /// Number of edge entities in the world.
    /// </summary>
    public int EdgeCount => _edgeEntityCount;
    
    /// <summary>
    /// Whether the world is initialized.
    /// </summary>
    public bool IsInitialized => _world is not null;

    /// <summary>
    /// Create and initialize the ECS World.
    /// </summary>
    public void Initialize()
    {
        Dispose();
        _world = World.Create();
        _nodeEntityCount = 0;
        _edgeEntityCount = 0;
        _disposed = false;
    }

    /// <summary>
    /// Initialize world with reserved capacity for expected entity count.
    /// </summary>
    /// <param name="expectedNodeCount">Expected number of nodes</param>
    /// <param name="expectedEdgeCount">Expected number of edges (optional)</param>
    public void Initialize(int expectedNodeCount, int expectedEdgeCount = 0)
    {
        Initialize();
        // Arch 2.x handles memory management internally
    }

    /// <summary>
    /// Populate ECS world from RQGraph data.
    /// Creates one entity per node with render and physics components.
    /// </summary>
    /// <param name="graph">Source graph</param>
    /// <param name="useCircleLayout">If true, arrange nodes in circle; otherwise use graph coordinates</param>
    public void InitializeFromGraph(RQGraph graph, bool useCircleLayout = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        
        if (_world is null)
            Initialize();
        
        int n = graph.N;
        _nodeEntityCount = n;
        
        // Create node entities
        for (int i = 0; i < n; i++)
        {
            // Calculate position
            Vector3 position;
            if (useCircleLayout)
            {
                double angle = 2.0 * System.Math.PI * i / n;
                position = new Vector3(
                    (float)System.Math.Cos(angle),
                    (float)System.Math.Sin(angle),
                    0f
                );
            }
            else
            {
                // Use graph coordinates if available
                var coords = graph.Coordinates;
                if (coords is not null && i < coords.Length)
                {
                    position = new Vector3((float)coords[i].X, (float)coords[i].Y, 0f);
                }
                else
                {
                    position = Vector3.Zero;
                }
            }
            
            // Determine color based on state
            Vector4 color = graph.State[i] switch
            {
                NodeState.Excited => new Vector4(1, 0, 0, 1),     // Red
                NodeState.Refractory => new Vector4(0, 0, 1, 1), // Blue
                _ => new Vector4(0.7f, 0.7f, 0.7f, 1)            // Gray
            };
            
            // Get physics data
            double mass = graph.PhysicsProperties[i].Mass;
            double energy = graph.NodeEnergy?[i] ?? 0;
            double potential = graph.LocalPotential?[i] ?? 0;
            
            // Calculate degree
            int degree = 0;
            for (int j = 0; j < n; j++)
            {
                if (graph.Edges[i, j]) degree++;
            }
            
            // Create entity with components - Arch 2.x API
            _world!.Create(
                new NodeId(i),
                new RenderPosition(position),
                new RenderColor(color),
                new RenderScale(1f),
                new NodeMass(mass),
                new NodeEnergy(energy),
                new NodeStateComponent((int)graph.State[i])
            );
        }
    }

    /// <summary>
    /// Update render positions from graph coordinates.
    /// Called after physics step to sync visualization.
    /// </summary>
    /// <param name="graph">Source graph with updated coordinates</param>
    public void UpdatePositionsFromGraph(RQGraph graph)
    {
        if (_world is null || graph.Coordinates is null)
            return;
        
        var coords = graph.Coordinates;
        
        // Arch 2.x Query API
        var query = new QueryDescription().WithAll<NodeId, RenderPosition>();
        _world.Query(in query, (ref NodeId nodeId, ref RenderPosition pos) =>
        {
            int idx = nodeId.Value;
            if (idx >= 0 && idx < coords.Length)
            {
                pos.Value = new Vector3((float)coords[idx].X, (float)coords[idx].Y, 0f);
            }
        });
    }

    /// <summary>
    /// Update node colors based on current state.
    /// </summary>
    /// <param name="graph">Source graph with updated states</param>
    public void UpdateStatesFromGraph(RQGraph graph)
    {
        if (_world is null)
            return;
        
        var query = new QueryDescription().WithAll<NodeId, RenderColor, NodeStateComponent>();
        _world.Query(in query, (ref NodeId nodeId, ref RenderColor color, ref NodeStateComponent state) =>
        {
            int idx = nodeId.Value;
            if (idx >= 0 && idx < graph.N)
            {
                state.State = (int)graph.State[idx];
                
                color.Value = graph.State[idx] switch
                {
                    NodeState.Excited => new Vector4(1, 0, 0, 1),
                    NodeState.Refractory => new Vector4(0, 0, 1, 1),
                    _ => new Vector4(0.7f, 0.7f, 0.7f, 1)
                };
            }
        });
    }

    /// <summary>
    /// Update heavy cluster tags from graph analysis.
    /// </summary>
    /// <param name="graph">Source graph</param>
    public void UpdateHeavyClusters(RQGraph graph)
    {
        if (_world is null)
            return;
        
        // Get heavy clusters
        var clusters = graph.GetStrongCorrelationClusters(graph.AdaptiveHeavyThreshold);
        
        // Build lookup: nodeId -> clusterId
        var clusterMap = new Dictionary<int, int>();
        for (int c = 0; c < clusters.Count; c++)
        {
            foreach (int nodeIdx in clusters[c])
            {
                clusterMap[nodeIdx] = c;
            }
        }
        
        // Update colors for heavy nodes
        var query = new QueryDescription().WithAll<NodeId, RenderColor>();
        _world.Query(in query, (ref NodeId nodeId, ref RenderColor color) =>
        {
            int idx = nodeId.Value;
            
            if (clusterMap.ContainsKey(idx))
            {
                // Heavy cluster node - use yellow
                color.Value = new Vector4(1, 1, 0, 1);
            }
        });
    }

    /// <summary>
    /// Get a query description for nodes with render components.
    /// </summary>
    public QueryDescription GetRenderNodesQuery() => 
        new QueryDescription().WithAll<NodeId, RenderPosition, RenderColor>();
    
    /// <summary>
    /// Get a query description for all nodes.
    /// </summary>
    public QueryDescription GetAllNodesQuery() => 
        new QueryDescription().WithAll<NodeId>();

    /// <summary>
    /// Clear all entities from the world.
    /// </summary>
    public void Clear()
    {
        _world?.Dispose();
        _world = World.Create();
        _nodeEntityCount = 0;
        _edgeEntityCount = 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        _world?.Dispose();
        _world = null;
        
        _nodeEntityCount = 0;
        _edgeEntityCount = 0;
        _disposed = true;
    }
}
