namespace RQSimulation.GPUCompressedSparseRow.Hybrid;

/// <summary>
/// Recommendation for topology change from GPU computation.
/// 
/// GPU modules compute on read-only CSR topology and generate
/// recommendations for topology changes that CPU then executes.
/// </summary>
public readonly struct TopologyRecommendation
{
    /// <summary>Type of topology change.</summary>
    public RecommendationType Type { get; init; }

    /// <summary>First node involved (source for edges).</summary>
    public int NodeA { get; init; }

    /// <summary>Second node involved (target for edges).</summary>
    public int NodeB { get; init; }

    /// <summary>Weight for new/modified edge.</summary>
    public double Weight { get; init; }

    /// <summary>Priority of this recommendation (higher = more important).</summary>
    public double Priority { get; init; }

    /// <summary>Source module that generated this recommendation.</summary>
    public string Source { get; init; }
}

/// <summary>
/// Types of topology modifications.
/// </summary>
public enum RecommendationType
{
    /// <summary>No change.</summary>
    None = 0,

    /// <summary>Create new edge between NodeA and NodeB.</summary>
    CreateEdge = 1,

    /// <summary>Remove edge between NodeA and NodeB.</summary>
    RemoveEdge = 2,

    /// <summary>Strengthen existing edge (increase weight).</summary>
    StrengthenEdge = 3,

    /// <summary>Weaken existing edge (decrease weight).</summary>
    WeakenEdge = 4,

    /// <summary>Create new node (NodeA = proposed parent).</summary>
    CreateNode = 5,

    /// <summary>Remove node (NodeA = node to remove).</summary>
    RemoveNode = 6,

    /// <summary>Rewire: remove edge (NodeA, NodeB) and create (NodeA, Weight as NodeC).</summary>
    Rewire = 7
}

/// <summary>
/// Buffer for collecting topology recommendations from GPU modules.
/// Thread-safe for multi-GPU scenarios.
/// </summary>
public sealed class RecommendationBuffer
{
    private readonly List<TopologyRecommendation> _recommendations = [];
    private readonly object _lock = new();

    /// <summary>Maximum recommendations to keep.</summary>
    public int MaxRecommendations { get; set; } = 1000;

    /// <summary>Current number of recommendations.</summary>
    public int Count
    {
        get { lock (_lock) return _recommendations.Count; }
    }

    /// <summary>
    /// Add a recommendation to the buffer.
    /// </summary>
    public void Add(TopologyRecommendation rec)
    {
        lock (_lock)
        {
            if (_recommendations.Count < MaxRecommendations)
            {
                _recommendations.Add(rec);
            }
            else
            {
                // Replace lowest priority if new one is higher
                int minIdx = 0;
                double minPriority = _recommendations[0].Priority;
                for (int i = 1; i < _recommendations.Count; i++)
                {
                    if (_recommendations[i].Priority < minPriority)
                    {
                        minPriority = _recommendations[i].Priority;
                        minIdx = i;
                    }
                }
                if (rec.Priority > minPriority)
                {
                    _recommendations[minIdx] = rec;
                }
            }
        }
    }

    /// <summary>
    /// Add multiple recommendations.
    /// </summary>
    public void AddRange(IEnumerable<TopologyRecommendation> recs)
    {
        foreach (var rec in recs)
        {
            Add(rec);
        }
    }

    /// <summary>
    /// Get all recommendations sorted by priority (descending).
    /// </summary>
    public List<TopologyRecommendation> GetSortedRecommendations()
    {
        lock (_lock)
        {
            return _recommendations
                .OrderByDescending(r => r.Priority)
                .ToList();
        }
    }

    /// <summary>
    /// Get top N recommendations by priority.
    /// </summary>
    public List<TopologyRecommendation> GetTopRecommendations(int count)
    {
        lock (_lock)
        {
            return _recommendations
                .OrderByDescending(r => r.Priority)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Clear all recommendations.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _recommendations.Clear();
        }
    }

    /// <summary>
    /// Get recommendations by type.
    /// </summary>
    public List<TopologyRecommendation> GetByType(RecommendationType type)
    {
        lock (_lock)
        {
            return _recommendations
                .Where(r => r.Type == type)
                .OrderByDescending(r => r.Priority)
                .ToList();
        }
    }
}
