using RqSimEngineApi.Contracts;
using RQSimulation;
using RQSimulation.Core.Plugins;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.GPUOptimizedCSR;

/// <summary>
/// Base class for GPU-based included plugins.
/// Provides common infrastructure for GPU-accelerated default pipeline modules.
/// 
/// GPU Context Injection:
/// =====================
/// Plugins should NOT create their own GraphicsDevice. Instead, they receive
/// a shared IGpuDeviceContext via SetDeviceContext(). This enables:
/// - Shared GPU resources between all plugins
/// - Proper barrier synchronization via the pipeline
/// - Zero-copy buffer sharing between compute and render
/// 
/// Usage:
/// 1. Create plugin instance
/// 2. Call SetDeviceContext() with the unified context
/// 3. Register with PhysicsPipeline
/// 4. Pipeline calls Initialize() when simulation starts
/// </summary>
public abstract class GpuPluginBase : IPhysicsModule, IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Shared device context for GPU operations.
    /// Set via SetDeviceContext() before Initialize().
    /// </summary>
    protected IGpuDeviceContext? DeviceContext { get; private set; }

    /// <summary>
    /// Shared node buffer on GPU.
    /// Set via SetSharedBuffer() before Initialize().
    /// </summary>
    protected IGpuBuffer<ApiNodeState>? SharedNodeBuffer { get; private set; }

    /// <summary>
    /// Whether the device context has been set.
    /// </summary>
    public bool HasDeviceContext => DeviceContext?.IsInitialized == true;

    public abstract string Name { get; }
    public virtual string Description => Name;
    public bool IsEnabled { get; set; } = true;
    public virtual ExecutionType ExecutionType => ExecutionType.GPU;
    
    /// <summary>
    /// Execution stage within the pipeline.
    /// Default is Forces (main physics computation stage).
    /// Override in derived classes to specify different stages.
    /// </summary>
    public virtual ExecutionStage Stage => ExecutionStage.Forces;
    
    public virtual int Priority => 100;
    public virtual string Category => "GPU";

    /// <summary>
    /// Indicates this is a default/included plugin.
    /// </summary>
    public bool IsIncludedPlugin => true;

    /// <summary>
    /// Sets the unified device context for GPU operations.
    /// Must be called before Initialize().
    /// 
    /// IMPORTANT: Do NOT create your own GraphicsDevice in derived classes.
    /// Use DeviceContext for all GPU operations.
    /// </summary>
    /// <param name="deviceContext">Shared device context (must not be null)</param>
    public virtual void SetDeviceContext(IGpuDeviceContext deviceContext)
    {
        ArgumentNullException.ThrowIfNull(deviceContext);
        
        if (!deviceContext.IsInitialized)
            throw new InvalidOperationException("DeviceContext must be initialized before setting");

        DeviceContext = deviceContext;
    }

    /// <summary>
    /// Sets the shared node buffer for GPU operations.
    /// Must be called before Initialize().
    /// 
    /// IMPORTANT: Do NOT dispose this buffer - it's owned by the pipeline.
    /// </summary>
    /// <param name="buffer">Shared node buffer</param>
    public virtual void SetSharedBuffer(IGpuBuffer<ApiNodeState> buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        SharedNodeBuffer = buffer;
    }

    public abstract void Initialize(RQGraph graph);
    public abstract void ExecuteStep(RQGraph graph, double dt);
    
    public virtual void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeCore();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Override to dispose GPU resources.
    /// Called during Cleanup() and Dispose().
    /// Do NOT dispose DeviceContext or SharedNodeBuffer - they're shared across all plugins.
    /// </summary>
    protected virtual void DisposeCore() { }
}
