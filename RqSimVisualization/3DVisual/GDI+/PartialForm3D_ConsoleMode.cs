using RqSimForms.ProcessesDispatcher.Contracts;
using RQSimulation;

namespace RqSimVisualization;

/// <summary>
/// Console mode specific visualization logic for GDI+ 3D rendering.
/// Separated from local mode for easier debugging and maintenance.
/// 
/// KEY DIFFERENCE FROM LOCAL MODE:
/// - Console mode receives positions AND EDGES via shared memory (no RQGraph object)
/// - FIX 29: Now uses REAL edges from shared memory instead of reconstructing from proximity!
/// - Uses SOFT TRACKING to server positions (no aggressive manifold physics)
/// 
/// DESIGN DECISION (Fix 21):
/// Instead of running full manifold embedding with repulsion forces,
/// console mode now uses "soft tracking" where nodes gently follow
/// server coordinates with mild smoothing. This preserves the 3D
/// structure from the server's spectral embedding.
/// </summary>
public partial class RqSimVisualizationForm
{
    // === CONSOLE MODE STATE ===
    // Separate from local mode to prevent interference

    /// <summary>
    /// Smoothed positions for console mode (blend of server + local smoothing).
    /// </summary>
    private float[]? _consoleSmoothedX;
    private float[]? _consoleSmoothedY;
    private float[]? _consoleSmoothedZ;

    /// <summary>
    /// Edges reconstructed from server coordinates (used only if no real edges available).
    /// </summary>
    private List<(int u, int v, float w)>? _consoleEdges;

    /// <summary>
    /// Flag indicating console mode state is initialized.
    /// </summary>
    private bool _consoleModeInitialized = false;

    /// <summary>
    /// Node count when console mode was initialized.
    /// </summary>
    private int _consoleNodeCount = 0;

    /// <summary>
    /// Frame counter for periodic edge rebuilding.
    /// </summary>
    private int _consoleFrameCounter = 0;

    /// <summary>
    /// Tracks the last node count to detect actual graph changes vs. simple tab switches.
    /// FIX: Tab Switch Reset - prevents reinit when switching tabs but graph hasn't changed.
    /// </summary>
    private int _lastConsoleModeNodeCount = 0;

    /// <summary>
    /// Resets all console mode state. Called when disconnecting from console.
    /// </summary>
    private void ResetConsoleModeState()
    {
        _consoleModeInitialized = false;
        _consoleNodeCount = 0;
        _lastConsoleModeNodeCount = 0;
        _consoleSmoothedX = null;
        _consoleSmoothedY = null;
        _consoleSmoothedZ = null;
        _consoleEdges = null;
        _consoleFrameCounter = 0;
    }

    /// <summary>
    /// Creates a VisualSnapshot from external (console) render nodes and edges.
    /// FIX 29: Now uses REAL edges from shared memory instead of k-NN proximity reconstruction!
    /// This produces the same topology as local mode, fixing the data gap issue.
    /// </summary>
    private VisualSnapshot? CreateSnapshotFromConsole(RenderNode[] nodes, RenderEdge[]? edges)
    {
        if (nodes == null || nodes.Length == 0)
            return null;

        int count = nodes.Length;

        // Get spectral dimension from external state if available
        var externalState = _lifeCycleManager?.TryGetExternalSimulationState();
        double spectralDim = externalState?.SpectralDimension ?? 0;

        var snapshot = new VisualSnapshot
        {
            NodeCount = count,
            SpectralDim = spectralDim,
            X = new float[count],
            Y = new float[count],
            Z = new float[count],
            States = new NodeState[count],
            ClusterIds = new int[count],
            Edges = new List<(int, int, float)>()
        };

        // Initialize states from node colors
        for (int i = 0; i < count; i++)
        {
            snapshot.States[i] = nodes[i].R > 0.5f ? NodeState.Excited : NodeState.Rest;
            snapshot.ClusterIds[i] = -1; // No cluster info in console mode
        }

        // FIX 29: Use REAL edges from shared memory if available!
        bool hasRealEdges = edges != null && edges.Length > 0;

        if (hasRealEdges)
        {
            // Convert RenderEdge[] to (int, int, float) tuples
            for (int e = 0; e < edges!.Length; e++)
            {
                int from = edges[e].FromNode;
                int to = edges[e].ToNode;
                float weight = edges[e].Weight;

                // Validate edge indices
                if (from >= 0 && from < count && to >= 0 && to < count && from != to)
                {
                    snapshot.Edges.Add((from, to, weight));
                }
            }
        }

        // FIX 23: Check simulation status - freeze positions when Stopped
        bool isSimulationStopped = externalState?.Status == SimulationStatus.Stopped;
        
        if (_enableManifoldEmbedding)
        {
            // FIX 29: With real edges, we can use server spectral coords as initial positions!
            // FIX: Tab Switch Reset - only reinit if node count ACTUALLY changed, not on tab switch.
            bool nodeCountChanged = _lastConsoleModeNodeCount != count;
            bool needsReinit = !_embeddingInitialized 
                            || _embeddingPositionX == null 
                            || _embeddingPositionX.Length != count
                            || nodeCountChanged;
            
            if (needsReinit)
            {
                _embeddingPositionX = new float[count];
                _embeddingPositionY = new float[count];
                _embeddingPositionZ = new float[count];
                _embeddingVelocityX = new float[count];
                _embeddingVelocityY = new float[count];
                _embeddingVelocityZ = new float[count];

                // Initialize from server spectral coordinates
                // Console mode differs from local: server provides pre-computed spectral embedding
                // Manifold physics will animate from these positions
                for (int i = 0; i < count; i++)
                {
                    _embeddingPositionX[i] = nodes[i].X;
                    _embeddingPositionY[i] = nodes[i].Y;
                    _embeddingPositionZ[i] = nodes[i].Z;
                }
                
                if (!hasRealEdges)
                {
                    // Fallback: Build edges from positions if no real edges available
                    snapshot.X = (float[])_embeddingPositionX.Clone();
                    snapshot.Y = (float[])_embeddingPositionY.Clone();
                    snapshot.Z = (float[])_embeddingPositionZ.Clone();
                    ReconstructEdgesFromProximity(snapshot);
                    
                    _cachedExternalEdges = new List<(int, int, float)>(snapshot.Edges);
                    _cachedExternalEdgeCount = count;
                }
                else
                {
                    // Use real edges - no need to cache k-NN edges
                    _cachedExternalEdges = null;
                    _cachedExternalEdgeCount = 0;
                }
                
                _embeddingInitialized = true;
                _lastConsoleModeNodeCount = count;
            }

            // Use manifold-controlled positions for display
            for (int i = 0; i < count; i++)
            {
                snapshot.X[i] = _embeddingPositionX![i];
                snapshot.Y[i] = _embeddingPositionY![i];
                snapshot.Z[i] = _embeddingPositionZ![i];
            }

            // Use REAL edges if available, otherwise use cached k-NN edges
            if (!hasRealEdges && _cachedExternalEdges != null && _cachedExternalEdgeCount == count)
            {
                snapshot.Edges.Clear();
                snapshot.Edges.AddRange(_cachedExternalEdges);
            }

            // FIX 23: Only apply physics when simulation is Running or Paused
            // When Stopped, freeze positions to prevent animation after Terminate
            if (!isSimulationStopped)
            {
                // Apply force-directed manifold embedding (same physics as local mode!)
                ApplyExternalManifoldEmbedding(snapshot);
            }
        }
        else
        {
            // Direct passthrough - use server coordinates as-is
            for (int i = 0; i < count; i++)
            {
                snapshot.X[i] = nodes[i].X;
                snapshot.Y[i] = nodes[i].Y;
                snapshot.Z[i] = nodes[i].Z;
            }

            // Build edges from current server positions for display only (if no real edges)
            if (!hasRealEdges)
            {
                _consoleFrameCounter++;
                if (_consoleFrameCounter % 30 == 0 || _consoleEdges == null || _consoleEdges.Count == 0)
                {
                    RebuildConsoleEdges(nodes);
                }

                if (_consoleEdges != null)
                {
                    snapshot.Edges.AddRange(_consoleEdges);
                }
            }
        }

        _consoleFrameCounter++;
        return snapshot;
    }

    /// <summary>
    /// Processes console mode with SOFT TRACKING (Fix 21).
    /// Instead of running aggressive manifold physics, we gently smooth
    /// the server coordinates to reduce jitter while preserving structure.
    /// </summary>
    private void ProcessConsoleSoftTracking(RenderNode[] nodes, VisualSnapshot snapshot)
    {
        int n = nodes.Length;

        // Initialize or resize arrays if needed
        if (!_consoleModeInitialized || _consoleNodeCount != n)
        {
            InitializeConsoleSoftTracking(nodes);
        }

        // Soft tracking parameters
        const float trackingSpeed = 0.3f;  // How fast to follow server (0=ignore, 1=instant)
        const float smoothing = 0.7f;      // Temporal smoothing (higher = smoother but laggier)

        for (int i = 0; i < n; i++)
        {
            // Server target position
            float serverX = nodes[i].X;
            float serverY = nodes[i].Y;
            float serverZ = nodes[i].Z;

            // Blend current smoothed position towards server
            _consoleSmoothedX![i] = _consoleSmoothedX[i] * smoothing +
                                   (serverX * trackingSpeed + _consoleSmoothedX[i] * (1 - trackingSpeed)) * (1 - smoothing);
            _consoleSmoothedY![i] = _consoleSmoothedY[i] * smoothing +
                                   (serverY * trackingSpeed + _consoleSmoothedY[i] * (1 - trackingSpeed)) * (1 - smoothing);
            _consoleSmoothedZ![i] = _consoleSmoothedZ[i] * smoothing +
                                   (serverZ * trackingSpeed + _consoleSmoothedZ[i] * (1 - trackingSpeed)) * (1 - smoothing);

            // Copy to snapshot
            snapshot.X[i] = _consoleSmoothedX[i];
            snapshot.Y[i] = _consoleSmoothedY[i];
            snapshot.Z[i] = _consoleSmoothedZ[i];
        }
    }

    /// <summary>
    /// Initializes soft tracking state from initial server data.
    /// </summary>
    private void InitializeConsoleSoftTracking(RenderNode[] nodes)
    {
        int n = nodes.Length;

        // Allocate smoothed position arrays
        _consoleSmoothedX = new float[n];
        _consoleSmoothedY = new float[n];
        _consoleSmoothedZ = new float[n];

        // Initialize with server coordinates
        for (int i = 0; i < n; i++)
        {
            _consoleSmoothedX[i] = nodes[i].X;
            _consoleSmoothedY[i] = nodes[i].Y;
            _consoleSmoothedZ[i] = nodes[i].Z;
        }

        // Build initial edges
        RebuildConsoleEdges(nodes);

        _consoleNodeCount = n;
        _consoleModeInitialized = true;
    }

    /// <summary>
    /// Rebuilds edges from CURRENT server coordinates using k-NN.
    /// Called periodically to keep edges in sync with server data.
    /// </summary>
    private void RebuildConsoleEdges(RenderNode[] nodes)
    {
        int n = nodes.Length;
        if (n < 2) return;

        // Extract positions from nodes
        float[] x = new float[n];
        float[] y = new float[n];
        float[] z = new float[n];

        for (int i = 0; i < n; i++)
        {
            x[i] = nodes[i].X;
            y[i] = nodes[i].Y;
            z[i] = nodes[i].Z;
        }

        _consoleEdges = ReconstructEdges_KNN(n, x, y, z, k: 6);
    }

    /// <summary>
    /// IMPROVED edge reconstruction using k-nearest neighbors WITHOUT distance threshold.
    /// This ensures the graph is always connected regardless of coordinate distribution.
    /// </summary>
    private List<(int u, int v, float w)> ReconstructEdges_KNN(int n, float[] x, float[] y, float[] z, int k = 6)
    {
        var edges = new List<(int u, int v, float w)>();
        if (n < 2) return edges;

        // Calculate reference distance for weight normalization
        float maxRange = Math.Max(
            x.Max() - x.Min(),
            Math.Max(y.Max() - y.Min(), z.Max() - z.Min()));
        if (maxRange < 0.01f) maxRange = 1f;
        float refDist = maxRange / MathF.Pow(n, 0.33f);

        // Use HashSet to avoid duplicate edges
        var edgeSet = new HashSet<(int, int)>();

        // For each node, connect to k nearest neighbors
        for (int i = 0; i < n; i++)
        {
            // Calculate distances to all other nodes
            var distances = new List<(int j, float dist)>(n - 1);
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                float dx = x[i] - x[j];
                float dy = y[i] - y[j];
                float dz = z[i] - z[j];
                float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                distances.Add((j, dist));
            }

            // Sort by distance
            distances.Sort((a, b) => a.dist.CompareTo(b.dist));

            // Connect to k nearest (NO distance threshold!)
            for (int e = 0; e < Math.Min(k, distances.Count); e++)
            {
                var (j, dist) = distances[e];

                // Canonical edge representation to avoid duplicates
                int u = Math.Min(i, j);
                int v = Math.Max(i, j);

                if (!edgeSet.Contains((u, v)))
                {
                    edgeSet.Add((u, v));

                    // Weight inversely proportional to distance
                    // Stronger weight for closer nodes
                    float weight = 1.0f / (1.0f + dist / refDist);
                    edges.Add((u, v, weight));
                }
            }
        }

        return edges;
    }
}
