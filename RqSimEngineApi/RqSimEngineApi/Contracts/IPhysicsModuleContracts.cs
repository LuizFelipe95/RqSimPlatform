namespace RqSimEngineApi.Contracts;

/// <summary>
/// Interface for CPU-based physics modules that work with raw Span data.
/// 
/// CPU modules receive direct memory access to simulation data via Span&lt;T&gt;.
/// This enables zero-copy data processing without boxing or LINQ overhead.
/// 
/// PERFORMANCE GUIDELINES:
/// =======================
/// 1. Use for loops, not foreach or LINQ inside Execute()
/// 2. Avoid allocations - no new arrays, lists, or objects in hot path
/// 3. Access data sequentially when possible for cache efficiency
/// 4. Consider SIMD via Vector&lt;T&gt; for numerical operations
/// 
/// Example:
/// <code>
/// public void Execute(Span&lt;ApiNodeState&gt; nodes, ref SimulationContext ctx)
/// {
///     for (int i = 0; i &lt; nodes.Length; i++)
///     {
///         ref var node = ref nodes[i];
///         node.Velocity += ComputeForce(node) * (float)ctx.DeltaTime;
///     }
/// }
/// </code>
/// </summary>
public interface ICpuPhysicsModule
{
    /// <summary>
    /// Module display name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Execution priority (lower executes first).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// One-time initialization when simulation starts.
    /// </summary>
    /// <param name="nodeCount">Number of nodes in simulation</param>
    /// <param name="context">Initial simulation context</param>
    void Initialize(int nodeCount, in SimulationContext context);

    /// <summary>
    /// Execute physics for one time step.
    /// 
    /// IMPORTANT: 
    /// - Do NOT allocate memory in this method
    /// - Use for loops, not foreach/LINQ
    /// - Modify nodes in-place via ref access
    /// </summary>
    /// <param name="nodes">Direct span access to node data (read/write)</param>
    /// <param name="context">Simulation context with global parameters</param>
    void Execute(Span<ApiNodeState> nodes, ref SimulationContext context);

    /// <summary>
    /// Cleanup when simulation stops.
    /// </summary>
    void Cleanup();
}

/// <summary>
/// Interface for GPU-based physics modules using injected device context.
/// 
/// GPU modules do NOT create their own device - they receive a shared context
/// via Configure(). This enables proper barrier synchronization and zero-copy
/// buffer sharing between all modules.
/// 
/// LIFECYCLE:
/// ==========
/// 1. Configure() - Receives device context and shared buffer
/// 2. Initialize() - Create module-specific GPU resources  
/// 3. Execute(dt) - Dispatch compute shader (NO data transfers)
/// 4. Cleanup() - Release module-specific resources
/// 
/// IMPORTANT:
/// - Do NOT create GraphicsDevice in your module
/// - Do NOT transfer data CPU?GPU in Execute() - pipeline handles this
/// - Just call Dispatch() on your compute shader
/// </summary>
public interface IGpuPhysicsModule
{
    /// <summary>
    /// Module display name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Execution priority (lower executes first).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Configure with shared GPU context and buffer.
    /// Called by pipeline before simulation starts.
    /// 
    /// IMPORTANT: Store the buffer reference but do NOT dispose it.
    /// The buffer is owned by the pipeline.
    /// </summary>
    /// <param name="deviceContext">Shared GPU device context</param>
    /// <param name="nodeBuffer">Shared node state buffer on GPU</param>
    void Configure(IGpuDeviceContext deviceContext, IGpuBuffer<ApiNodeState> nodeBuffer);

    /// <summary>
    /// Initialize module-specific GPU resources.
    /// </summary>
    /// <param name="nodeCount">Number of nodes</param>
    void Initialize(int nodeCount);

    /// <summary>
    /// Execute compute shader for one time step.
    /// 
    /// IMPORTANT:
    /// - Just dispatch the shader - do NOT wait for completion
    /// - Pipeline handles barriers and synchronization
    /// - No CPU?GPU data transfers here
    /// </summary>
    /// <param name="dt">Time step</param>
    void Execute(float dt);

    /// <summary>
    /// Cleanup module-specific GPU resources.
    /// Do NOT dispose the shared buffer or device context.
    /// </summary>
    void Cleanup();
}
