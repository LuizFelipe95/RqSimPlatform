using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RQSimulation.Core.Plugins;

/// <summary>
/// Defines the execution stage within the physics pipeline.
/// Modules are sorted by stage first, then by priority within each stage.
/// </summary>
public enum ExecutionStage
{
    /// <summary>
    /// Preparation phase: data validation, buffer setup, constraint checks.
    /// Runs before any physics calculations.
    /// </summary>
    Preparation = 0,

    /// <summary>
    /// Forces phase: compute all forces (gravity, gauge, quantum potentials).
    /// This is where most physics modules operate.
    /// </summary>
    Forces = 1,

    /// <summary>
    /// Integration phase: apply forces to update positions/momenta.
    /// Runs after all forces are computed.
    /// </summary>
    Integration = 2,

    /// <summary>
    /// Post-processing phase: statistics, normalization, visualization prep.
    /// Runs after physics integration is complete.
    /// </summary>
    PostProcess = 3
}

/// <summary>
/// Defines how modules within a group should execute.
/// </summary>
public enum GroupExecutionMode
{
    /// <summary>
    /// Modules execute sequentially in priority order within the group.
    /// </summary>
    Sequential = 0,
    
    /// <summary>
    /// Modules execute in parallel within the group.
    /// All modules must complete before the next group/stage starts.
    /// </summary>
    Parallel = 1
}

/// <summary>
/// Contract for any physics module in the RQ simulation pipeline.
/// 
/// Modules are initialized once at simulation start and executed each step.
/// The pipeline manages ordering, enabling/disabling, and dispatch type.
/// 
/// GROUPING:
/// =========
/// Modules can specify a ModuleGroup for atomic execution:
/// - All modules in a group complete before the next step/group
/// - Groups can execute internally as Sequential or Parallel
/// - Groups are ordered by Stage, then by group priority, then by module priority
/// 
/// Implementation guidelines:
/// - Keep Initialize() idempotent (safe to call multiple times)
/// - ExecuteStep() should be stateless where possible
/// - GPU modules should use ExecutionType.GPU for proper scheduling
/// - Throw descriptive exceptions on initialization failure
/// </summary>
public interface IPhysicsModule
{
    /// <summary>
    /// Display name of the module for UI and logging.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Optional description for UI tooltips.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Whether the module is currently enabled.
    /// Disabled modules are skipped during initialization and execution.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Determines how the module executes (sync, async, GPU).
    /// </summary>
    ExecutionType ExecutionType { get; }

    /// <summary>
    /// Execution stage within the pipeline.
    /// Modules are sorted by stage first, then by priority.
    /// Default is Forces (main physics computation stage).
    /// </summary>
    ExecutionStage Stage => ExecutionStage.Forces;

    /// <summary>
    /// Priority for ordering within same execution stage.
    /// Lower values execute first. Default is 100.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Category for UI grouping (e.g., "Fields", "Gauge", "Gravity", "Topology").
    /// </summary>
    string Category { get; }
    
    /// <summary>
    /// Optional module group for atomic execution.
    /// Modules in the same group execute together and all must complete
    /// before the next group/stage starts.
    /// Default is null (no group - execute individually by stage/priority).
    /// </summary>
    string? ModuleGroup => null;
    
    /// <summary>
    /// Execution mode within the module group.
    /// Only used if ModuleGroup is specified.
    /// Default is Sequential.
    /// </summary>
    GroupExecutionMode GroupMode => GroupExecutionMode.Sequential;

    /// <summary>
    /// Optional exclusive group identifier for mutually exclusive modules.
    /// Modules in the same exclusive group cannot be active simultaneously.
    /// For example, different integrators might use the same exclusive group "Integrator"
    /// to ensure only one integration method is active at a time.
    /// Default is null (no exclusivity - module can coexist with all others).
    /// </summary>
    string? ExclusiveGroup => null;

    /// <summary>
    /// Preferred GPU device ID for modules with ExecutionType.GPU.
    /// Used for multi-GPU distribution to assign modules to specific devices.
    /// Value of -1 (default) means auto-assign to any available device.
    /// Device IDs typically range from 0 to (device count - 1).
    /// Only used when multiple GPUs are available and multi-GPU orchestration is enabled.
    /// </summary>
    int PreferredDeviceId => -1;

    /// <summary>
    /// Called once when the simulation starts or resets.
    /// Initialize fields, allocate buffers, set up GPU resources.
    /// </summary>
    /// <param name="graph">The RQGraph instance to initialize</param>
    void Initialize(RQGraph graph);

    /// <summary>
    /// Called every simulation step to evolve physics.
    /// </summary>
    /// <param name="graph">The RQGraph instance to update</param>
    /// <param name="dt">Time step (proper time or relational time)</param>
    void ExecuteStep(RQGraph graph, double dt);

    /// <summary>
    /// Called when simulation stops. Release GPU resources, flush buffers.
    /// </summary>
    void Cleanup();
}

/// <summary>
/// Optional interface for CPU physics modules that support zero-copy Span-based execution.
/// 
/// Implement this interface in addition to IPhysicsModule to enable high-performance
/// data-oriented execution with direct memory access.
/// 
/// PERFORMANCE GUIDELINES:
/// =======================
/// 1. Use for loops, not foreach or LINQ inside ExecuteSpan()
/// 2. Avoid allocations - no new arrays, lists, or objects in hot path
/// 3. Access data sequentially when possible for cache efficiency
/// 4. Consider SIMD via Vector&lt;T&gt; for numerical operations
/// 
/// The PhysicsPipeline will automatically detect and use this interface when available,
/// falling back to the standard ExecuteStep method otherwise.
/// </summary>
public interface ISpanPhysicsModule : IPhysicsModule
{
    /// <summary>
    /// Execute physics for one time step using direct Span access.
    /// 
    /// IMPORTANT: 
    /// - Do NOT allocate memory in this method
    /// - Use for loops, not foreach/LINQ
    /// - Modify data in-place via ref access
    /// </summary>
    /// <param name="weights">Direct span access to edge weights (N*N flattened, read/write)</param>
    /// <param name="edgePhases">Direct span access to edge phases (N*N flattened, read/write)</param>
    /// <param name="edges">Direct span access to edge existence flags (N*N flattened, read-only)</param>
    /// <param name="nodeCount">Number of nodes in the graph</param>
    /// <param name="dt">Time step (proper time or relational time)</param>
    void ExecuteSpan(Span<double> weights, Span<double> edgePhases, ReadOnlySpan<bool> edges, int nodeCount, double dt);
}

/// <summary>
/// Helper methods for converting 2D arrays to Span for zero-copy module execution.
/// </summary>
public static class PhysicsModuleSpanHelper
{
    /// <summary>
    /// Gets a Span view of a 2D array's underlying storage.
    /// CRITICAL: Only works with row-major contiguous arrays.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<T> AsSpan<T>(this T[,] array)
    {
        return MemoryMarshal.CreateSpan(ref array[0, 0], array.Length);
    }

    /// <summary>
    /// Gets a ReadOnlySpan view of a 2D array's underlying storage.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<T> AsReadOnlySpan<T>(this T[,] array)
    {
        return MemoryMarshal.CreateReadOnlySpan(ref array[0, 0], array.Length);
    }
}
