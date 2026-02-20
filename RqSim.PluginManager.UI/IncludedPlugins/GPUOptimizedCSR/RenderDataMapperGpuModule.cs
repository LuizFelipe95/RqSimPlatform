using ComputeSharp;
using RQSimulation;
using RQSimulation.Core.Plugins;
using RQSimulation.GPUOptimized.Rendering;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.GPUOptimizedCSR;

/// <summary>
/// GPU module for physics-to-render data mapping.
/// 
/// Converts double-precision physics state to float-precision vertex data
/// entirely on GPU, avoiding PCIe bandwidth bottleneck.
/// 
/// COLOR MODES:
/// 0 = Phase: HSV rainbow based on quantum phase angle
/// 1 = Energy: Grayscale brightness from potential
/// 2 = Mass: Red gradient from node mass
/// 
/// Uses: GPUOptimized/Rendering/RenderMapperShader.cs
/// </summary>
public sealed class RenderDataMapperGpuModule : GpuPluginBase
{
    private RenderDataMapper? _mapper;
    private RQGraph? _graph;
    
    // Staging arrays
    private PhysicsNodeState[]? _physicsStates;
    private RenderNodeVertex[]? _renderVertices;
    
    public override string Name => "Render Mapper (GPU)";
    public override string Description => "GPU-accelerated physics?vertex conversion for visualization";
    public override string Category => "Rendering";
    public override int Priority => 250; // Run very late
    public override ExecutionStage Stage => ExecutionStage.PostProcess;
    
    /// <summary>
    /// Module group for atomic execution with other rendering modules.
    /// </summary>
    public string ModuleGroup => "Visualization";
    
    /// <summary>
    /// Color mode for vertex coloring.
    /// 0 = Phase, 1 = Energy, 2 = Mass
    /// </summary>
    public int ColorMode { get; set; } = 0;
    
    /// <summary>
    /// Base vertex size.
    /// </summary>
    public float BaseSize { get; set; } = 0.5f;
    
    /// <summary>
    /// Size scaling factor for probability density.
    /// </summary>
    public float SizeScale { get; set; } = 2.0f;
    
    /// <summary>
    /// Whether GPU double precision is supported.
    /// </summary>
    public bool IsDoublePrecisionSupported => _mapper?.IsDoublePrecisionSupported ?? false;
    
    /// <summary>
    /// Output render vertices (for binding to render pipeline).
    /// </summary>
    public RenderNodeVertex[]? RenderVertices => _renderVertices;

    public override void Initialize(RQGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        
        // Initialize mapper
        _mapper = new RenderDataMapper();
        _mapper.Initialize(graph.N);
        
        // Allocate staging arrays
        _physicsStates = new PhysicsNodeState[graph.N];
        _renderVertices = new RenderNodeVertex[graph.N];
        
        // Initial mapping
        UpdateMapping();
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (_mapper is null || _physicsStates is null || _renderVertices is null) return;
        
        UpdateMapping();
    }

    private void UpdateMapping()
    {
        if (_graph is null || _physicsStates is null || _renderVertices is null || _mapper is null) return;
        
        #pragma warning disable CS0618 // Coordinates is obsolete but needed for rendering
        var coords = _graph.Coordinates;
        #pragma warning restore CS0618
        
        // Fill physics states from graph
        for (int i = 0; i < _graph.N; i++)
        {
            // Get quantum state from graph - use excited state as phase proxy
            double psiReal = _graph.State[i] == NodeState.Excited ? 1.0 : 0.0;
            double psiImag = _graph.State[i] == NodeState.Refractory ? 0.5 : 0.0;
            
            // Get 2D coordinates
            double x = coords[i].X;
            double y = coords[i].Y;
            double z = 0.0; // 2D graph, z = 0
            
            _physicsStates[i] = new PhysicsNodeState
            {
                X = x,
                Y = y,
                Z = z,
                PsiReal = psiReal,
                PsiImag = psiImag,
                Potential = 0.0, // Not tracked separately
                Mass = _graph.GetNodeMass(i)
            };
        }
        
        // Map to render vertices
        _mapper.Map(_physicsStates, _renderVertices, ColorMode, BaseSize, SizeScale);
    }

    protected override void DisposeCore()
    {
        _mapper?.Dispose();
        _mapper = null;
        _physicsStates = null;
        _renderVertices = null;
    }
}
