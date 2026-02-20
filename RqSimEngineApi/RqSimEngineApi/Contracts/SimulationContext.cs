namespace RqSimEngineApi.Contracts;

/// <summary>
/// Context passed to physics modules during execution.
/// Contains global simulation parameters and state.
/// 
/// This is a ref struct to avoid heap allocations in the hot path.
/// Passed by reference to ExecuteStep methods.
/// </summary>
public ref struct SimulationContext
{
    /// <summary>
    /// Current simulation time.
    /// </summary>
    public double Time;

    /// <summary>
    /// Time step (dt) for this frame.
    /// </summary>
    public double DeltaTime;

    /// <summary>
    /// Frame/tick counter.
    /// </summary>
    public long TickId;

    /// <summary>
    /// Total number of nodes in the simulation.
    /// </summary>
    public int NodeCount;

    /// <summary>
    /// Total number of edges (non-zero entries).
    /// </summary>
    public int EdgeCount;

    /// <summary>
    /// Inverse temperature beta = 1/(kT) for MCMC/thermodynamic calculations.
    /// </summary>
    public double Beta;

    /// <summary>
    /// Gravitational coupling constant.
    /// </summary>
    public double GravityStrength;

    /// <summary>
    /// Gauge coupling constant g.
    /// </summary>
    public double GaugeCoupling;

    /// <summary>
    /// Whether to use double precision (fp64) for calculations.
    /// </summary>
    public bool UseDoublePrecision;

    /// <summary>
    /// Full physics parameters from UI configuration.
    /// GPU-compatible blittable struct for shader constant buffers.
    /// 
    /// USAGE IN MODULES:
    /// - Read context.Params.RicciFlowAlpha instead of PhysicsConstants
    /// - Pass context.Params fields to shader constructors
    /// - Values are updated every frame from UI
    /// </summary>
    public SimulationParameters Params;

    /// <summary>
    /// Creates a simulation context with default values.
    /// </summary>
    public static SimulationContext Default => new()
    {
        Time = 0,
        DeltaTime = 0.01,
        TickId = 0,
        NodeCount = 0,
        EdgeCount = 0,
        Beta = 1.0,
        GravityStrength = 1.0,
        GaugeCoupling = 1.0,
        UseDoublePrecision = false,
        Params = SimulationParameters.Default
    };

    /// <summary>
    /// Synchronizes legacy fields from Params for backward compatibility.
    /// Call this after setting Params to update legacy fields.
    /// </summary>
    public void SyncFromParams()
    {
        DeltaTime = Params.DeltaTime;
        Beta = Params.InverseBeta;
        GravityStrength = Params.GravitationalCoupling;
        GaugeCoupling = Params.GaugeCoupling;
        UseDoublePrecision = Params.UseDoublePrecision;
    }

    /// <summary>
    /// Synchronizes Params from legacy fields for backward compatibility.
    /// Call this before passing context to new-style modules.
    /// </summary>
    public void SyncToParams()
    {
        Params.DeltaTime = DeltaTime;
        Params.CurrentTime = Time;
        Params.TickId = TickId;
        Params.InverseBeta = Beta;
        Params.GravitationalCoupling = GravityStrength;
        Params.GaugeCoupling = GaugeCoupling;
        Params.UseDoublePrecision = UseDoublePrecision;
    }
}

/// <summary>
/// Non-ref version of SimulationContext for storage/serialization.
/// Use when you need to persist context across method boundaries.
/// </summary>
public struct SimulationContextSnapshot
{
    public double Time;
    public double DeltaTime;
    public long TickId;
    public int NodeCount;
    public int EdgeCount;
    public double Beta;
    public double GravityStrength;
    public double GaugeCoupling;
    public bool UseDoublePrecision;

    /// <summary>
    /// Full physics parameters snapshot.
    /// </summary>
    public SimulationParameters Params;

    /// <summary>
    /// Creates a ref struct context from this snapshot.
    /// </summary>
    public SimulationContext ToContext() => new()
    {
        Time = Time,
        DeltaTime = DeltaTime,
        TickId = TickId,
        NodeCount = NodeCount,
        EdgeCount = EdgeCount,
        Beta = Beta,
        GravityStrength = GravityStrength,
        GaugeCoupling = GaugeCoupling,
        UseDoublePrecision = UseDoublePrecision,
        Params = Params
    };

    /// <summary>
    /// Creates a snapshot from a ref struct context.
    /// </summary>
    public static SimulationContextSnapshot FromContext(in SimulationContext ctx) => new()
    {
        Time = ctx.Time,
        DeltaTime = ctx.DeltaTime,
        TickId = ctx.TickId,
        NodeCount = ctx.NodeCount,
        EdgeCount = ctx.EdgeCount,
        Beta = ctx.Beta,
        GravityStrength = ctx.GravityStrength,
        GaugeCoupling = ctx.GaugeCoupling,
        UseDoublePrecision = ctx.UseDoublePrecision,
        Params = ctx.Params
    };
}
