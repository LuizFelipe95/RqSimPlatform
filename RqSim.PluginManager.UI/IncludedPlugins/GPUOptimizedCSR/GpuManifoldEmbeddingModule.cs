using ComputeSharp;
using RqSimEngineApi.Contracts;
using RQSimulation;
using RQSimulation.Core.Plugins;
using RQSimulation.GPUOptimized.Rendering;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.GPUOptimizedCSR;

/// <summary>
/// GPU-accelerated Manifold Embedding engine for emergent spacetime visualization.
/// 
/// RQ-HYPOTHESIS: EMERGENT GEOMETRY
/// =================================
/// In RQ-theory, "distance" is not predefined but emerges from interaction strength.
/// Nodes with strong connections (high edge weight) are conceptually "close".
/// 
/// This module implements force-directed graph embedding where:
/// - Edge springs pull connected nodes together (force ? weight)
/// - Global repulsion prevents collapse
/// - The resulting embedding reveals emergent spatial structure
/// 
/// VISUALIZATION EFFECTS:
/// =====================
/// - 1D chains ? linear filaments
/// - 2D lattices ? planar membranes  
/// - 3D bulk ? spherical distributions
/// - Quantum foam ? pulsating complex structures
/// 
/// EXECUTION STAGE: PostIntegration
/// ================================
/// Runs after physics to update visualization coordinates.
/// </summary>
public sealed class GpuManifoldEmbeddingModule : GpuPluginBase
{
    private RQGraph? _graph;
    private bool _isEnabled = false;

    // CPU buffers (used when GPU context unavailable or for small graphs)
    private double[]? _positionsX;
    private double[]? _positionsY;
    private double[]? _positionsZ;
    private double[]? _velocitiesX;
    private double[]? _velocitiesY;
    private double[]? _velocitiesZ;

    public override string Name => "Manifold Embedding";
    public override string Description => "Force-directed graph embedding based on edge weights for emergent spacetime visualization";
    public override string Category => "Visualization";
    public override ExecutionStage Stage => ExecutionStage.PostProcess;
    public override int Priority => 200;

    /// <summary>
    /// Repulsion factor: controls how strongly nodes repel from center.
    /// Higher values prevent collapse but slow convergence.
    /// </summary>
    public double RepulsionFactor { get; set; } = 0.5;

    /// <summary>
    /// Spring factor: base stiffness for edge springs.
    /// Higher values make edges pull stronger.
    /// </summary>
    public double SpringFactor { get; set; } = 0.8;

    /// <summary>
    /// Integration time step.
    /// </summary>
    public double DeltaTime { get; set; } = 0.016;

    /// <summary>
    /// Damping factor (0-1): velocity decay per step.
    /// Lower values = more oscillation, higher = faster settling.
    /// </summary>
    public double Damping { get; set; } = 0.85;

    /// <summary>
    /// Target dimension for embedding (1, 2, or 3).
    /// Lower dimensions "flatten" the visualization.
    /// </summary>
    public int TargetDimension { get; set; } = 3;

    /// <summary>
    /// Whether manifold embedding is actively computing.
    /// </summary>
    public bool ManifoldEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public override void Initialize(RQGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));

        int n = graph.N;

        // Initialize CPU buffers from spectral coordinates if available
        _positionsX = new double[n];
        _positionsY = new double[n];
        _positionsZ = new double[n];
        _velocitiesX = new double[n];
        _velocitiesY = new double[n];
        _velocitiesZ = new double[n];

        // Initialize from spectral coordinates if available
        if (graph.SpectralX != null && graph.SpectralX.Length == n)
        {
            Array.Copy(graph.SpectralX, _positionsX, n);
            Array.Copy(graph.SpectralY, _positionsY, n);
            Array.Copy(graph.SpectralZ, _positionsZ, n);
        }
        else
        {
            // Random initialization
            var rng = new Random(42);
            for (int i = 0; i < n; i++)
            {
                _positionsX[i] = (rng.NextDouble() - 0.5) * 2;
                _positionsY[i] = (rng.NextDouble() - 0.5) * 2;
                _positionsZ[i] = (rng.NextDouble() - 0.5) * 2;
            }
        }

        Array.Clear(_velocitiesX, 0, n);
        Array.Clear(_velocitiesY, 0, n);
        Array.Clear(_velocitiesZ, 0, n);

        if (HasDeviceContext)
        {
            System.Diagnostics.Trace.WriteLine($"[{Name}] GPU context available for manifold embedding");
        }
        else
        {
            System.Diagnostics.Trace.WriteLine($"[{Name}] Using CPU fallback for manifold embedding");
        }
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (!_isEnabled || _graph is null) return;
        if (_positionsX is null || _velocitiesX is null) return;

        // CPU implementation (GPU version would use ManifoldEmbeddingShader)
        ExecuteCpuStep(graph, dt);
    }

    private void ExecuteCpuStep(RQGraph graph, double dt)
    {
        int n = graph.N;
        if (_positionsX is null || _positionsX.Length != n) return;

        double[] forceX = new double[n];
        double[] forceY = new double[n];
        double[] forceZ = new double[n];

        // Calculate center of mass
        double comX = 0, comY = 0, comZ = 0;
        for (int i = 0; i < n; i++)
        {
            comX += _positionsX[i];
            comY += _positionsY[i];
            comZ += _positionsZ[i];
        }
        comX /= n;
        comY /= n;
        comZ /= n;

        // 1. Global repulsion from center (prevents collapse)
        for (int i = 0; i < n; i++)
        {
            double dx = _positionsX[i] - comX;
            double dy = _positionsY[i] - comY;
            double dz = _positionsZ[i] - comZ;
            double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz) + 0.1;

            double repulsion = RepulsionFactor / (dist * dist);
            forceX[i] += dx / dist * repulsion;
            forceY[i] += dy / dist * repulsion;
            forceZ[i] += dz / dist * repulsion;
        }

        // 2. Spring attraction along edges
        for (int i = 0; i < n; i++)
        {
            foreach (int j in graph.Neighbors(i))
            {
                if (j <= i) continue; // Avoid double counting

                double w = graph.Weights[i, j];
                if (w < 1e-6) continue;

                double dx = _positionsX[j] - _positionsX[i];
                double dy = _positionsY[j] - _positionsY[i];
                double dz = _positionsZ[j] - _positionsZ[i];
                double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz) + 0.01;

                // Target distance: inversely proportional to weight
                double targetDist = 1.0 / (w + 0.1);
                double springForce = SpringFactor * w * (dist - targetDist);

                double fx = dx / dist * springForce;
                double fy = dy / dist * springForce;
                double fz = dz / dist * springForce;

                forceX[i] += fx;
                forceY[i] += fy;
                forceZ[i] += fz;
                forceX[j] -= fx;
                forceY[j] -= fy;
                forceZ[j] -= fz;
            }
        }

        // 3. Integration with damping
        double stepDt = DeltaTime;
        double damp = Damping;

        for (int i = 0; i < n; i++)
        {
            _velocitiesX![i] = (_velocitiesX[i] + forceX[i] * stepDt) * damp;
            _velocitiesY![i] = (_velocitiesY[i] + forceY[i] * stepDt) * damp;
            _velocitiesZ![i] = (_velocitiesZ[i] + forceZ[i] * stepDt) * damp;

            _positionsX[i] += _velocitiesX[i] * stepDt;
            _positionsY[i] += _velocitiesY[i] * stepDt;
            _positionsZ[i] += _velocitiesZ[i] * stepDt;

            // Dimension reduction if needed
            if (TargetDimension < 3)
            {
                _positionsZ[i] *= 0.01;
            }
            if (TargetDimension < 2)
            {
                _positionsY[i] *= 0.01;
            }
        }

        // Write back to graph's spectral coordinates for visualization
        for (int i = 0; i < n; i++)
        {
            if (graph.SpectralX != null)
            {
                graph.SpectralX[i] = _positionsX[i];
                graph.SpectralY[i] = _positionsY[i];
                graph.SpectralZ[i] = _positionsZ[i];
            }
        }
    }

    /// <summary>
    /// Gets the current embedded positions for external visualization.
    /// </summary>
    public (double[] X, double[] Y, double[] Z)? GetEmbeddedPositions()
    {
        if (_positionsX is null || _positionsY is null || _positionsZ is null)
            return null;

        return (_positionsX, _positionsY, _positionsZ);
    }

    /// <summary>
    /// Resets the embedding to initial (spectral) coordinates.
    /// </summary>
    public void ResetToSpectral()
    {
        if (_graph is null || _positionsX is null) return;

        int n = _graph.N;
        if (_graph.SpectralX != null && _graph.SpectralX.Length == n)
        {
            Array.Copy(_graph.SpectralX, _positionsX, n);
            Array.Copy(_graph.SpectralY, _positionsY!, n);
            Array.Copy(_graph.SpectralZ, _positionsZ!, n);
        }

        Array.Clear(_velocitiesX!, 0, n);
        Array.Clear(_velocitiesY!, 0, n);
        Array.Clear(_velocitiesZ!, 0, n);
    }

    protected override void DisposeCore()
    {
        _positionsX = null;
        _positionsY = null;
        _positionsZ = null;
        _velocitiesX = null;
        _velocitiesY = null;
        _velocitiesZ = null;
    }
}
