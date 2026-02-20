using RQSimulation;

namespace RqSim3DForm;

/// <summary>
/// Manifold Embedding functionality for standalone 3D visualization.
/// Force-directed graph layout based on RQ-hypothesis principles.
/// </summary>
public partial class Form_Rsim3DForm
{
    // === Manifold Embedding State ===
    private bool _enableManifoldEmbedding = true; // Enabled by default
    private bool _embeddingInitialized;
    private float[]? _embeddingPositionX;
    private float[]? _embeddingPositionY;
    private float[]? _embeddingPositionZ;
    private float[]? _embeddingVelocityX;
    private float[]? _embeddingVelocityY;
    private float[]? _embeddingVelocityZ;

    // === Cached force arrays (avoid per-frame allocations) ===
    private float[]? _forceX;
    private float[]? _forceY;
    private float[]? _forceZ;

    // === Cached filtered edges (avoid per-frame List creation) ===
    private List<(int u, int v, float w)>? _filteredEdgesCache;
    private double _lastFilteredThreshold = double.NaN;

    // === Throttling: skip some frames for manifold physics ===
    private int _manifoldUpdateCounter;
    private const int ManifoldUpdateInterval = 2; // Update every N frames

    // === Manifold Physics Constants (matched to original GDI+ version) ===
    private const double ManifoldRepulsionFactor = 0.5;  // Was 50.0 - way too high!
    private const double ManifoldSpringFactor = 0.8;     // Was 0.5
    private const double ManifoldDamping = 0.85;
    private const double ManifoldDeltaTime = 0.05;       // Was 0.016 - increased for faster convergence

    /// <summary>
    /// Resets all manifold embedding state.
    /// </summary>
    private void ResetManifoldEmbedding()
    {
        _embeddingInitialized = false;
        _embeddingPositionX = null;
        _embeddingPositionY = null;
        _embeddingPositionZ = null;
        _embeddingVelocityX = null;
        _embeddingVelocityY = null;
        _embeddingVelocityZ = null;
        _forceX = null;
        _forceY = null;
        _forceZ = null;
        _filteredEdgesCache = null;
        _lastFilteredThreshold = double.NaN;
    }

    /// <summary>
    /// Initializes manifold embedding positions from current coordinates.
    /// </summary>
    private void InitializeManifoldPositions(int n, float[] x, float[] y, float[] z)
    {
        _embeddingPositionX = new float[n];
        _embeddingPositionY = new float[n];
        _embeddingPositionZ = new float[n];
        _embeddingVelocityX = new float[n];
        _embeddingVelocityY = new float[n];
        _embeddingVelocityZ = new float[n];
        _forceX = new float[n];
        _forceY = new float[n];
        _forceZ = new float[n];

        for (int i = 0; i < n; i++)
        {
            _embeddingPositionX[i] = x[i];
            _embeddingPositionY[i] = y[i];
            _embeddingPositionZ[i] = z[i];
        }

        _embeddingInitialized = true;
    }

    /// <summary>
    /// Checks if manifold embedding needs (re)initialization.
    /// </summary>
    private bool NeedsManifoldInitialization(int nodeCount)
    {
        if (!_embeddingInitialized) return true;
        if (_embeddingPositionX == null || _embeddingPositionX.Length != nodeCount) return true;
        return false;
    }

    /// <summary>
    /// Updates manifold embedding positions based on force-directed layout.
    /// </summary>
    private void UpdateManifoldEmbedding(int n, float[] x, float[] y, float[] z, List<(int u, int v, float w)>? edges)
    {
        if (_embeddingVelocityX == null || _embeddingPositionX == null) return;
        if (_embeddingVelocityX.Length != n || _embeddingPositionX.Length != n) return;
        if (edges == null) return;

        // Reuse cached force arrays (avoid allocation every frame)
        if (_forceX == null || _forceX.Length != n)
        {
            _forceX = new float[n];
            _forceY = new float[n];
            _forceZ = new float[n];
        }
        else
        {
            // Clear arrays
            Array.Clear(_forceX, 0, n);
            Array.Clear(_forceY!, 0, n);
            Array.Clear(_forceZ!, 0, n);
        }

        // Calculate center of mass
        float comX = 0, comY = 0, comZ = 0;
        for (int i = 0; i < n; i++)
        {
            comX += x[i];
            comY += y[i];
            comZ += z[i];
        }
        comX /= n;
        comY /= n;
        comZ /= n;

        // 1. Global repulsion from center (prevents collapse)
        for (int i = 0; i < n; i++)
        {
            float dx = x[i] - comX;
            float dy = y[i] - comY;
            float dz = z[i] - comZ;
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz) + 0.1f;

            float repulsion = (float)(ManifoldRepulsionFactor / (dist * dist));
            _forceX[i] += dx / dist * repulsion;
            _forceY![i] += dy / dist * repulsion;
            _forceZ![i] += dz / dist * repulsion;
        }

        // 2. Spring attraction along edges
        foreach (var (u, v, w) in edges)
        {
            if (u >= n || v >= n) continue;

            float dx = x[v] - x[u];
            float dy = y[v] - y[u];
            float dz = z[v] - z[u];
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz) + 0.01f;

            float targetDist = 1.0f / (w + 0.1f);
            float springForce = (float)(ManifoldSpringFactor * w * (dist - targetDist));

            float fx = dx / dist * springForce;
            float fy = dy / dist * springForce;
            float fz = dz / dist * springForce;

            _forceX[u] += fx;
            _forceY![u] += fy;
            _forceZ![u] += fz;
            _forceX[v] -= fx;
            _forceY[v] -= fy;
            _forceZ[v] -= fz;
        }

        // 3. Integration with damping
        float dt = (float)ManifoldDeltaTime;
        float damping = (float)ManifoldDamping;

        for (int i = 0; i < n; i++)
        {
            _embeddingVelocityX[i] = (_embeddingVelocityX[i] + _forceX[i] * dt) * damping;
            _embeddingVelocityY![i] = (_embeddingVelocityY[i] + _forceY![i] * dt) * damping;
            _embeddingVelocityZ![i] = (_embeddingVelocityZ[i] + _forceZ![i] * dt) * damping;

            _embeddingPositionX[i] += _embeddingVelocityX[i] * dt;
            _embeddingPositionY![i] += _embeddingVelocityY[i] * dt;
            _embeddingPositionZ![i] += _embeddingVelocityZ[i] * dt;

            // Copy to output
            x[i] = _embeddingPositionX[i];
            y[i] = _embeddingPositionY[i];
            z[i] = _embeddingPositionZ[i];
        }
    }

    /// <summary>
    /// Applies manifold embedding to graph data if enabled.
    /// Throttled to run every N frames to reduce CPU load.
    /// </summary>
    private void ApplyManifoldEmbedding()
    {
        if (!_enableManifoldEmbedding) return;
        if (_nodeX == null || _nodeY == null || _nodeZ == null) return;
        if (_nodeCount == 0) return;

        // Throttle: only update manifold physics every N frames
        _manifoldUpdateCounter++;
        if (_manifoldUpdateCounter < ManifoldUpdateInterval)
        {
            // Still apply cached positions even on skipped frames
            if (_embeddingInitialized && _embeddingPositionX != null && _embeddingPositionX.Length == _nodeCount)
            {
                for (int i = 0; i < _nodeCount; i++)
                {
                    _nodeX[i] = _embeddingPositionX[i];
                    _nodeY[i] = _embeddingPositionY![i];
                    _nodeZ[i] = _embeddingPositionZ![i];
                }
            }
            return;
        }
        _manifoldUpdateCounter = 0;

        if (NeedsManifoldInitialization(_nodeCount))
        {
            InitializeManifoldPositions(_nodeCount, _nodeX, _nodeY, _nodeZ);
        }
        // NOTE: Removed the blend-with-fresh-data logic that was here!
        // The blend was pulling nodes back to their source positions every frame,
        // which prevented manifold embedding from creating a cohesive structure
        // in console mode where source coordinates come from spectral embedding.
        // Now manifold embedding fully controls positions after initialization.

        // Build filtered edges list for spring forces
        // Cache this to avoid re-filtering every frame
        List<(int u, int v, float w)>? edges = null;
        if (_edges != null && _edges.Count > 0)
        {
            double threshold = _edgeWeightThreshold;
            if (_filteredEdgesCache == null || Math.Abs(_lastFilteredThreshold - threshold) > 0.001)
            {
                _filteredEdgesCache ??= new List<(int u, int v, float w)>(_edges.Count);
                _filteredEdgesCache.Clear();
                
                foreach (var (u, v, w) in _edges)
                {
                    if (w >= threshold)
                    {
                        _filteredEdgesCache.Add((u, v, w));
                    }
                }
                _lastFilteredThreshold = threshold;
            }
            edges = _filteredEdgesCache;
        }

        // Run physics update on EMBEDDING positions (not source _nodeX/Y/Z)
        UpdateManifoldEmbedding(_nodeCount, _embeddingPositionX!, _embeddingPositionY!, _embeddingPositionZ!, edges);

        // Copy results back to display arrays
        for (int i = 0; i < _nodeCount && i < _embeddingPositionX!.Length; i++)
        {
            _nodeX[i] = _embeddingPositionX[i];
            _nodeY[i] = _embeddingPositionY![i];
            _nodeZ[i] = _embeddingPositionZ![i];
        }
    }
}
