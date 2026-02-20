using RQSimulation.Core.Plugins;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RQSimulation.GPUCompressedSparseRow.Hybrid;

/// <summary>
/// Interface for GPU physics modules that can generate topology recommendations.
/// 
/// HYBRID PATTERN:
/// GPU modules read from immutable CSR topology and produce recommendations.
/// HybridPipelineCoordinator collects recommendations and applies them on CPU.
/// After topology changes, CSR is rebuilt and reuploaded to GPU.
/// </summary>
public interface IHybridPhysicsModule : IPhysicsModule
{
    /// <summary>
    /// Generate topology change recommendations based on current state.
    /// Called after ExecuteStep to collect GPU-computed suggestions.
    /// </summary>
    /// <param name="graph">The graph (for context only, not for modification)</param>
    /// <returns>List of recommended topology changes</returns>
    IEnumerable<TopologyRecommendation> GetRecommendations(RQGraph graph);

    /// <summary>
    /// Whether this module has pending recommendations.
    /// </summary>
    bool HasRecommendations { get; }

    /// <summary>
    /// Clear any cached recommendations after they've been processed.
    /// </summary>
    void ClearRecommendations();
}

/// <summary>
/// Hybrid pipeline coordinator for GPU-CPU topology changes.
/// 
/// ARCHITECTURE:
/// =============
/// 1. GPU Phase (Read-Only):
///    - All GPU modules compute on current CSR topology
///    - Modules detect conditions requiring topology changes
///    - Generate TopologyRecommendations (e.g., "remove edge X", "create node Y")
/// 
/// 2. Collection Phase:
///    - Coordinator collects recommendations from all IHybridPhysicsModule
///    - Filters, prioritizes, and validates recommendations
///    - Checks for conflicts (e.g., create + remove same edge)
/// 
/// 3. CPU Phase (Write):
///    - Applies approved recommendations to RQGraph topology
///    - Uses standard AddEdge/RemoveEdge/Rewire methods
///    - Increments TopologySignature
/// 
/// 4. Sync Phase:
///    - Rebuilds CSR from modified RQGraph
///    - Uploads new CSR to GPU
///    - GPU modules detect signature change and reinitialize
/// 
/// This ensures GPU computations are deterministic while allowing
/// dynamic topology evolution.
/// </summary>
public sealed class HybridPipelineCoordinator : IDisposable
{
    private readonly List<IHybridPhysicsModule> _hybridModules = [];
    private readonly RecommendationBuffer _recommendationBuffer = new();
    private CsrTopology? _csrTopology;
    private int _lastTopologySignature;
    private bool _disposed;

    /// <summary>Maximum recommendations to process per step.</summary>
    public int MaxRecommendationsPerStep { get; set; } = 100;

    /// <summary>Minimum priority threshold for recommendations.</summary>
    public double MinPriorityThreshold { get; set; } = 0.0;

    /// <summary>Whether to rebuild CSR automatically after changes.</summary>
    public bool AutoRebuildCsr { get; set; } = true;

    /// <summary>Number of recommendations processed in last step.</summary>
    public int LastRecommendationsProcessed { get; private set; }

    /// <summary>Number of topology changes applied in last step.</summary>
    public int LastChangesApplied { get; private set; }

    /// <summary>
    /// Register a hybrid module for recommendation collection.
    /// </summary>
    public void RegisterModule(IHybridPhysicsModule module)
    {
        if (!_hybridModules.Contains(module))
        {
            _hybridModules.Add(module);
        }
    }

    /// <summary>
    /// Unregister a hybrid module.
    /// </summary>
    public void UnregisterModule(IHybridPhysicsModule module)
    {
        _hybridModules.Remove(module);
    }

    /// <summary>
    /// Initialize coordinator with graph's CSR topology.
    /// </summary>
    public void Initialize(RQGraph graph)
    {
        _csrTopology = graph.CsrTopology;
        _lastTopologySignature = graph.TopologySignature;
    }

    /// <summary>
    /// Main coordination step: collect recommendations and apply changes.
    /// Call this after all GPU modules have executed.
    /// </summary>
    public void ProcessRecommendations(RQGraph graph)
    {
        LastRecommendationsProcessed = 0;
        LastChangesApplied = 0;

        // Phase 1: Collect recommendations from all hybrid modules
        _recommendationBuffer.Clear();
        foreach (var module in _hybridModules)
        {
            if (!module.IsEnabled || !module.HasRecommendations)
                continue;

            var recs = module.GetRecommendations(graph);
            _recommendationBuffer.AddRange(recs);
            module.ClearRecommendations();
        }

        if (_recommendationBuffer.Count == 0)
            return;

        // Phase 2: Filter and prioritize
        var recommendations = _recommendationBuffer
            .GetSortedRecommendations()
            .Where(r => r.Priority >= MinPriorityThreshold)
            .Take(MaxRecommendationsPerStep)
            .ToList();

        LastRecommendationsProcessed = recommendations.Count;

        // Phase 3: Validate and resolve conflicts
        var validRecommendations = ValidateAndResolveConflicts(recommendations);

        // Phase 4: Apply to graph (CPU phase)
        foreach (var rec in validRecommendations)
        {
            if (ApplyRecommendation(graph, rec))
            {
                LastChangesApplied++;
            }
        }

        // Phase 5: Rebuild CSR if changes were made
        if (LastChangesApplied > 0 && AutoRebuildCsr)
        {
            RebuildCsr(graph);
        }
    }

    private List<TopologyRecommendation> ValidateAndResolveConflicts(
        List<TopologyRecommendation> recommendations)
    {
        var result = new List<TopologyRecommendation>();
        var processedEdges = new HashSet<(int, int)>();

        foreach (var rec in recommendations)
        {
            // Normalize edge key (smaller node first)
            var edgeKey = rec.NodeA < rec.NodeB 
                ? (rec.NodeA, rec.NodeB) 
                : (rec.NodeB, rec.NodeA);

            // Skip if already processed this edge
            if (processedEdges.Contains(edgeKey))
                continue;

            processedEdges.Add(edgeKey);
            result.Add(rec);
        }

        return result;
    }

    private bool ApplyRecommendation(RQGraph graph, TopologyRecommendation rec)
    {
        try
        {
            switch (rec.Type)
            {
                case RecommendationType.CreateEdge:
                    if (!graph.Edges[rec.NodeA, rec.NodeB])
                    {
                        graph.Edges[rec.NodeA, rec.NodeB] = true;
                        graph.Edges[rec.NodeB, rec.NodeA] = true;
                        graph.Weights[rec.NodeA, rec.NodeB] = rec.Weight;
                        graph.Weights[rec.NodeB, rec.NodeA] = rec.Weight;
                        graph.IncrementTopologySignature();
                        return true;
                    }
                    break;

                case RecommendationType.RemoveEdge:
                    if (graph.Edges[rec.NodeA, rec.NodeB])
                    {
                        graph.Edges[rec.NodeA, rec.NodeB] = false;
                        graph.Edges[rec.NodeB, rec.NodeA] = false;
                        graph.Weights[rec.NodeA, rec.NodeB] = 0;
                        graph.Weights[rec.NodeB, rec.NodeA] = 0;
                        graph.IncrementTopologySignature();
                        return true;
                    }
                    break;

                case RecommendationType.StrengthenEdge:
                    if (graph.Edges[rec.NodeA, rec.NodeB])
                    {
                        graph.Weights[rec.NodeA, rec.NodeB] += rec.Weight;
                        graph.Weights[rec.NodeB, rec.NodeA] += rec.Weight;
                        return true;
                    }
                    break;

                case RecommendationType.WeakenEdge:
                    if (graph.Edges[rec.NodeA, rec.NodeB])
                    {
                        double newWeight = graph.Weights[rec.NodeA, rec.NodeB] - rec.Weight;
                        if (newWeight > 0)
                        {
                            graph.Weights[rec.NodeA, rec.NodeB] = newWeight;
                            graph.Weights[rec.NodeB, rec.NodeA] = newWeight;
                        }
                        else
                        {
                            // Weight dropped to zero, remove edge
                            graph.Edges[rec.NodeA, rec.NodeB] = false;
                            graph.Edges[rec.NodeB, rec.NodeA] = false;
                            graph.Weights[rec.NodeA, rec.NodeB] = 0;
                            graph.Weights[rec.NodeB, rec.NodeA] = 0;
                            graph.IncrementTopologySignature();
                        }
                        return true;
                    }
                    break;

                case RecommendationType.None:
                default:
                    break;
            }
        }
        catch
        {
            // Failed to apply recommendation, skip it
        }

        return false;
    }

    private void RebuildCsr(RQGraph graph)
    {
        if (_csrTopology == null)
        {
            _csrTopology = new CsrTopology();
            graph.CsrTopology = _csrTopology;
        }

        // Rebuild CSR from current graph topology
        _csrTopology.BuildFromDenseMatrix(graph.Edges, graph.Weights);
        _csrTopology.UploadToGpu();

        _lastTopologySignature = graph.TopologySignature;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _hybridModules.Clear();
        _recommendationBuffer.Clear();
        _disposed = true;
    }
}
