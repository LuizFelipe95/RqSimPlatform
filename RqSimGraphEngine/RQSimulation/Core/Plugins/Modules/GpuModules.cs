using RQSimulation.GPUOptimized;
using RQSimulation.GPUOptimized.KleinGordon;
using RQSimulation.GPUOptimized.RelationalTime;
using RQSimulation.GPUOptimized.SpinorField;
using RQSimulation.GPUOptimized.YangMills;

namespace RQSimulation.Core.Plugins.Modules;

/// <summary>
/// GPU-accelerated gravity module wrapping GpuGravityEngine.
/// Provides network geometry evolution on GPU.
/// </summary>
public sealed class GpuGravityModule : PhysicsModuleBase, IDisposable
{
    private GpuGravityEngine? _engine;
    private readonly GpuConfig _config;
    private bool _disposed;

    public override string Name => "GPU Network Gravity";
    public override string Description => "GPU-accelerated network geometry evolution with Ollivier-Ricci curvature";
    public override string Category => "Gravity";
    public override ExecutionType ExecutionType => ExecutionType.GPU;
    
    /// <summary>
    /// Gravity runs in Forces stage - computes gravitational forces from curvature.
    /// </summary>
    public override ExecutionStage Stage => ExecutionStage.Forces;
    
    public override int Priority => 40;

    public GpuGravityModule(GpuConfig? config = null)
    {
        _config = config ?? new GpuConfig { GpuIndex = 0, MultiGpu = false, ThreadBlockSize = 256 };
    }

    public override void Initialize(RQGraph graph)
    {
        _engine?.Dispose();

        int edgeCount = graph.FlatEdgesFrom?.Length ?? 0;
        if (edgeCount == 0)
        {
            // No edges yet, skip initialization
            return;
        }

        _engine = new GpuGravityEngine(_config, edgeCount, graph.N);
        _engine.UpdateTopologyBuffers(graph);

        // Link to graph for automatic GPU usage
        graph.GpuGravity = _engine;
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (_engine is null) return;

        // GPU gravity is invoked via EvolveNetworkGeometry which checks GpuGravity
        // If direct control is needed:
        // _engine.EvolveCurvatureStep(weights, masses, dt);
    }

    public override void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _engine?.Dispose();
        _engine = null;
        _disposed = true;
    }
}

/// <summary>
/// GPU-accelerated Yang-Mills gauge field module.
/// </summary>
public sealed class GpuYangMillsModule : PhysicsModuleBase, IDisposable
{
    private GpuYangMillsEngine? _engine;
    private readonly int _gaugeDimension;
    private bool _disposed;

    public override string Name => "GPU Yang-Mills";
    public override string Description => "GPU-accelerated SU(N) gauge field evolution with Wilson action";
    public override string Category => "Gauge";
    public override ExecutionType ExecutionType => ExecutionType.GPU;
    
    /// <summary>
    /// Yang-Mills runs in Forces stage - computes gauge forces.
    /// </summary>
    public override ExecutionStage Stage => ExecutionStage.Forces;
    
    public override int Priority => 50;

    /// <param name="gaugeDimension">2 for SU(2), 3 for SU(3)</param>
    public GpuYangMillsModule(int gaugeDimension = 2)
    {
        _gaugeDimension = gaugeDimension;
    }

    public override void Initialize(RQGraph graph)
    {
        _engine?.Dispose();
        _engine = new GpuYangMillsEngine();

        int edgeCount = graph.FlatEdgesFrom?.Length ?? 0;
        if (edgeCount == 0) return;

        // Count plaquettes (triangles and squares)
        // This is a simplified estimate - actual implementation would count from graph
        int triangleCount = graph.N; // Approximate
        int squareCount = graph.N / 2;

        if (_gaugeDimension == 2)
        {
            _engine.InitializeSU2(graph.N, edgeCount, triangleCount, squareCount);
        }
        // SU(3) initialization would be similar
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        // Yang-Mills evolution via gauge-covariant update
        // _engine?.EvolveGaugeField(dt);
    }

    public override void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _engine?.Dispose();
        _engine = null;
        _disposed = true;
    }
}

/// <summary>
/// GPU-accelerated Klein-Gordon scalar field module (double precision).
/// </summary>
public sealed class GpuKleinGordonModule : PhysicsModuleBase, IDisposable
{
    private GpuKleinGordonEngineDouble? _engine;
    private bool _disposed;

    public override string Name => "GPU Klein-Gordon";
    public override string Description => "GPU-accelerated scalar field evolution with Verlet integration";
    public override string Category => "Fields";
    public override ExecutionType ExecutionType => ExecutionType.GPU;
    
    /// <summary>
    /// Klein-Gordon runs in Integration stage - integrates field equations.
    /// </summary>
    public override ExecutionStage Stage => ExecutionStage.Integration;
    
    public override int Priority => 60;

    public override void Initialize(RQGraph graph)
    {
        _engine?.Dispose();
        _engine = new GpuKleinGordonEngineDouble();

        if (!_engine.IsDoublePrecisionSupported)
        {
            // Fall back to CPU module
            _engine.Dispose();
            _engine = null;
            return;
        }

        int edgeCount = graph.FlatEdgesFrom?.Length ?? 0;
        _engine.Initialize(graph.N, Math.Max(1, edgeCount));
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        // Klein-Gordon evolution step
        // _engine?.EvolveStep(dt);
    }

    public override void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _engine?.Dispose();
        _engine = null;
        _disposed = true;
    }
}

/// <summary>
/// GPU-accelerated Dirac spinor field module.
/// </summary>
public sealed class GpuSpinorFieldModule : PhysicsModuleBase, IDisposable
{
    private GpuSpinorFieldEngine? _engine;
    private bool _disposed;

    public override string Name => "GPU Spinor Field";
    public override string Description => "GPU-accelerated Dirac spinor evolution with Wilson term";
    public override string Category => "Fields";
    public override ExecutionType ExecutionType => ExecutionType.GPU;
    
    /// <summary>
    /// Spinor field runs in Forces stage - couples to gauge fields.
    /// </summary>
    public override ExecutionStage Stage => ExecutionStage.Forces;
    
    public override int Priority => 20;

    public override void Initialize(RQGraph graph)
    {
        _engine?.Dispose();
        _engine = new GpuSpinorFieldEngine();

        int edgeCount = graph.FlatEdgesFrom?.Length ?? 0;
        if (edgeCount == 0) return;

        // Initialize would upload spinor data to GPU
        // _engine.Initialize(graph.N, edgeCount);
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        // Spinor evolution via Dirac equation
        // _engine?.EvolveStep(dt);
    }

    public override void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _engine?.Dispose();
        _engine = null;
        _disposed = true;
    }
}

/// <summary>
/// GPU-accelerated relational time module.
/// </summary>
public sealed class GpuRelationalTimeModule : PhysicsModuleBase, IDisposable
{
    private GpuRelationalTimeEngine? _engine;
    private bool _disposed;

    public override string Name => "GPU Relational Time";
    public override string Description => "GPU-accelerated Page-Wootters relational time computation";
    public override string Category => "Time";
    public override ExecutionType ExecutionType => ExecutionType.GPU;
    public override int Priority => 75;

    public override void Initialize(RQGraph graph)
    {
        _engine?.Dispose();
        _engine = new GpuRelationalTimeEngine();

        int clockSize = Math.Max(2, graph.N / 20);
        // _engine.Initialize(graph.N, clockSize);
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        // Relational time computed on GPU
        // _engine?.ComputeRelationalDt();
    }

    public override void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _engine?.Dispose();
        _engine = null;
        _disposed = true;
    }
}

/// <summary>
/// GPU-accelerated spectral dimension analysis module.
/// </summary>
public sealed class GpuSpectralModule : PhysicsModuleBase, IDisposable
{
    private GPUOptimized.GpuSpectralEngine? _engine;
    private bool _disposed;

    public override string Name => "GPU Spectral Analysis";
    public override string Description => "GPU-accelerated spectral dimension and eigenvalue computation";
    public override string Category => "Analysis";
    public override ExecutionType ExecutionType => ExecutionType.GPU;
    public override int Priority => 150;

    public override void Initialize(RQGraph graph)
    {
        _engine?.Dispose();
        _engine = new GPUOptimized.GpuSpectralEngine();

        // _engine.Initialize(graph.N);
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        // Spectral analysis typically not every step
        // Could be run periodically
    }

    public override void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _engine?.Dispose();
        _engine = null;
        _disposed = true;
    }
}
