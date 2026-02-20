using RQSimulation;

namespace RqSimVisualization;

/// <summary>
/// Manifold Embedding functionality for 3D visualization.
/// Force-directed graph layout based on RQ-hypothesis principles.
/// </summary>
public partial class RqSimVisualizationForm
{
    /// <summary>
    /// Resets all manifold embedding state (positions, velocities, initialization flag).
    /// Call this when disabling manifold embedding to prevent "replay" effect on re-enable.
    /// 
    /// RQ-FIX: Without this reset, velocities accumulated during manifold mode
    /// would persist and cause "fast-forward" animation when re-enabled.
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
        
        // Also reset cached external edges so they get rebuilt on next enable
        _cachedExternalEdges = null;
        _cachedExternalEdgeCount = 0;
    }

    /// <summary>
    /// Initializes manifold embedding positions from current spectral coordinates.
    /// Call when entering manifold mode or when node count changes.
    /// </summary>
    private void InitializeManifoldPositions(RQGraph graph)
    {
        if (graph == null) return;

        int n = graph.N;
        _embeddingPositionX = new float[n];
        _embeddingPositionY = new float[n];
        _embeddingPositionZ = new float[n];
        _embeddingVelocityX = new float[n];
        _embeddingVelocityY = new float[n];
        _embeddingVelocityZ = new float[n];

        // Initialize positions from spectral coordinates
        for (int i = 0; i < n; i++)
        {
            _embeddingPositionX[i] = (float)graph.SpectralX[i];
            _embeddingPositionY[i] = (float)graph.SpectralY[i];
            _embeddingPositionZ[i] = (float)graph.SpectralZ[i];
            // Velocities initialized to 0 by default
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
    private void UpdateManifoldEmbedding(int n, float[] x, float[] y, float[] z, List<(int u, int v, float w)> edges)
    {
        // Ensure buffers exist
        if (_embeddingVelocityX == null || _embeddingPositionX == null) return;
        if (_embeddingVelocityX.Length != n || _embeddingPositionX.Length != n) return;

        float[] forceX = new float[n];
        float[] forceY = new float[n];
        float[] forceZ = new float[n];

        // Calculate center of mass for repulsion reference
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
            forceX[i] += dx / dist * repulsion;
            forceY[i] += dy / dist * repulsion;
            forceZ[i] += dz / dist * repulsion;
        }

        // 2. Spring attraction along edges (Hooke's law with weight as spring constant)
        foreach (var (u, v, w) in edges)
        {
            if (u >= n || v >= n) continue;

            float dx = x[v] - x[u];
            float dy = y[v] - y[u];
            float dz = z[v] - z[u];
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz) + 0.01f;

            // Spring force: F = k * x, where k = weight (stronger connection = stiffer spring)
            // Target distance inversely proportional to weight
            float targetDist = 1.0f / (w + 0.1f);
            float springForce = (float)(ManifoldSpringFactor * w * (dist - targetDist));

            float fx = dx / dist * springForce;
            float fy = dy / dist * springForce;
            float fz = dz / dist * springForce;

            forceX[u] += fx;
            forceY[u] += fy;
            forceZ[u] += fz;
            forceX[v] -= fx;
            forceY[v] -= fy;
            forceZ[v] -= fz;
        }

        // 3. Integration (Verlet-like with damping)
        float dt = (float)ManifoldDeltaTime;
        float damping = (float)ManifoldDamping;

        for (int i = 0; i < n; i++)
        {
            _embeddingVelocityX[i] = (_embeddingVelocityX[i] + forceX[i] * dt) * damping;
            _embeddingVelocityY![i] = (_embeddingVelocityY[i] + forceY[i] * dt) * damping;
            _embeddingVelocityZ![i] = (_embeddingVelocityZ[i] + forceZ[i] * dt) * damping;

            // Update persistent positions
            _embeddingPositionX[i] += _embeddingVelocityX[i] * dt;
            _embeddingPositionY![i] += _embeddingVelocityY[i] * dt;
            _embeddingPositionZ![i] += _embeddingVelocityZ[i] * dt;

            // Copy to output arrays
            x[i] = _embeddingPositionX[i];
            y[i] = _embeddingPositionY[i];
            z[i] = _embeddingPositionZ[i];
        }
    }
}
