using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RQSimulation.Core.Observability;

/// <summary>
/// Telemetry and observability infrastructure for RqSimPlatform.
///
/// CHECKLIST ITEM 39 (11.2): Observability Integration
/// ======================================================
/// Provides OpenTelemetry-compatible instrumentation for monitoring simulation
/// state, performance, and physics metrics during execution.
///
/// COMPONENTS:
/// ===========
/// 1. **ActivitySource** - Distributed tracing for operation timing
/// 2. **Meter** - Metrics collection (counters, gauges, histograms)
/// 3. **Event Counters** - High-performance event publishing
///
/// USAGE WITH OPENTELEMETRY:
/// ==========================
/// Add to your application startup:
/// <code>
/// services.AddOpenTelemetry()
///     .WithTracing(builder => builder
///         .AddSource(RqSimPlatformTelemetry.ActivitySourceName)
///         .AddOtlpExporter())
///     .WithMetrics(builder => builder
///         .AddMeter(RqSimPlatformTelemetry.MeterName)
///         .AddOtlpExporter());
/// </code>
///
/// EXPORTED METRICS:
/// =================
/// - rqsim.frame.duration_ms - Frame execution time
/// - rqsim.frame.count - Total frames executed
/// - rqsim.graph.nodes - Current node count
/// - rqsim.graph.edges - Current edge count
/// - rqsim.physics.energy - Total vacuum energy
/// - rqsim.physics.curvature_avg - Average node curvature
/// - rqsim.physics.spectral_dimension - Spectral dimension D_s
/// - rqsim.module.duration_ms - Per-module execution time
///
/// VISUALIZATION:
/// ==============
/// - Grafana: Real-time dashboards for metrics
/// - Jaeger/Zipkin: Distributed tracing visualization
/// - Prometheus: Time-series metrics storage and queries
/// - Application Insights: Cloud-native monitoring (Azure)
/// </summary>
public static class RqSimPlatformTelemetry
{
    /// <summary>
    /// Activity source name for distributed tracing.
    /// </summary>
    public const string ActivitySourceName = "RqSimPlatform";

    /// <summary>
    /// Meter name for metrics collection.
    /// </summary>
    public const string MeterName = "RqSimPlatform";

    /// <summary>
    /// Activity source for creating spans/activities.
    /// Use this for tracing operation execution.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(
        ActivitySourceName,
        version: "1.0.0");

    /// <summary>
    /// Meter for recording metrics.
    /// Use this for counters, gauges, and histograms.
    /// </summary>
    public static readonly Meter Meter = new(
        MeterName,
        version: "1.0.0");

    // ================================================================
    // METRICS
    // ================================================================

    /// <summary>
    /// Frame execution duration histogram (milliseconds).
    /// Tracks distribution of frame execution times.
    /// </summary>
    public static readonly Histogram<double> FrameDuration = Meter.CreateHistogram<double>(
        name: "rqsim.frame.duration_ms",
        unit: "ms",
        description: "Frame execution duration in milliseconds");

    /// <summary>
    /// Total frame count counter.
    /// </summary>
    public static readonly Counter<long> FrameCount = Meter.CreateCounter<long>(
        name: "rqsim.frame.count",
        unit: "frames",
        description: "Total number of frames executed");

    /// <summary>
    /// Module execution duration histogram (milliseconds).
    /// Tracks per-module performance.
    /// </summary>
    public static readonly Histogram<double> ModuleDuration = Meter.CreateHistogram<double>(
        name: "rqsim.module.duration_ms",
        unit: "ms",
        description: "Module execution duration in milliseconds");

    /// <summary>
    /// Module execution count counter.
    /// </summary>
    public static readonly Counter<long> ModuleExecutionCount = Meter.CreateCounter<long>(
        name: "rqsim.module.execution.count",
        unit: "executions",
        description: "Number of times each module has been executed");

    /// <summary>
    /// Module error count counter.
    /// </summary>
    public static readonly Counter<long> ModuleErrorCount = Meter.CreateCounter<long>(
        name: "rqsim.module.error.count",
        unit: "errors",
        description: "Number of errors encountered by each module");

    // ================================================================
    // GRAPH METRICS
    // ================================================================

    /// <summary>
    /// Current graph node count (observable gauge).
    /// </summary>
    private static int _currentNodeCount;
    public static readonly ObservableGauge<int> NodeCount = Meter.CreateObservableGauge(
        name: "rqsim.graph.nodes",
        observeValue: () => _currentNodeCount,
        unit: "nodes",
        description: "Current number of nodes in the graph");

    /// <summary>
    /// Current graph edge count (observable gauge).
    /// </summary>
    private static int _currentEdgeCount;
    public static readonly ObservableGauge<int> EdgeCount = Meter.CreateObservableGauge(
        name: "rqsim.graph.edges",
        observeValue: () => _currentEdgeCount,
        unit: "edges",
        description: "Current number of edges in the graph");

    /// <summary>
    /// Average node degree (observable gauge).
    /// </summary>
    public static readonly ObservableGauge<double> AverageDegree = Meter.CreateObservableGauge(
        name: "rqsim.graph.average_degree",
        observeValue: () => _currentNodeCount > 0 ? (double)_currentEdgeCount * 2 / _currentNodeCount : 0.0,
        unit: "edges_per_node",
        description: "Average number of edges per node");

    // ================================================================
    // PHYSICS METRICS
    // ================================================================

    /// <summary>
    /// Total vacuum energy (observable gauge).
    /// </summary>
    private static double _vacuumEnergy;
    public static readonly ObservableGauge<double> VacuumEnergy = Meter.CreateObservableGauge(
        name: "rqsim.physics.vacuum_energy",
        observeValue: () => _vacuumEnergy,
        unit: "energy_units",
        description: "Current vacuum energy pool");

    /// <summary>
    /// Average node curvature (observable gauge).
    /// </summary>
    private static double _averageCurvature;
    public static readonly ObservableGauge<double> AverageCurvature = Meter.CreateObservableGauge(
        name: "rqsim.physics.curvature_avg",
        observeValue: () => _averageCurvature,
        unit: "curvature",
        description: "Average Ricci curvature across nodes");

    /// <summary>
    /// Spectral dimension D_s (observable gauge).
    /// </summary>
    private static double _spectralDimension = 4.0;
    public static readonly ObservableGauge<double> SpectralDimension = Meter.CreateObservableGauge(
        name: "rqsim.physics.spectral_dimension",
        observeValue: () => _spectralDimension,
        unit: "dimensions",
        description: "Spectral dimension D_s from diffusion analysis");

    /// <summary>
    /// Lieb-Robinson speed (emergent light speed, observable gauge).
    /// </summary>
    private static double _liebRobinsonSpeed;
    public static readonly ObservableGauge<double> LiebRobinsonSpeed = Meter.CreateObservableGauge(
        name: "rqsim.physics.lieb_robinson_speed",
        observeValue: () => _liebRobinsonSpeed,
        unit: "units_per_step",
        description: "Lieb-Robinson speed (emergent light-cone velocity)");

    /// <summary>
    /// Conservation violation count counter.
    /// </summary>
    public static readonly Counter<long> ConservationViolationCount = Meter.CreateCounter<long>(
        name: "rqsim.physics.conservation_violations",
        unit: "violations",
        description: "Number of energy conservation violations detected");

    // ================================================================
    // UPDATE METHODS
    // ================================================================

    /// <summary>
    /// Update graph topology metrics.
    /// Call this after topology changes.
    /// </summary>
    public static void UpdateGraphMetrics(int nodeCount, int edgeCount)
    {
        _currentNodeCount = nodeCount;
        _currentEdgeCount = edgeCount;
    }

    /// <summary>
    /// Update physics state metrics.
    /// Call this periodically during simulation.
    /// </summary>
    public static void UpdatePhysicsMetrics(
        double? vacuumEnergy = null,
        double? averageCurvature = null,
        double? spectralDimension = null,
        double? liebRobinsonSpeed = null)
    {
        if (vacuumEnergy.HasValue)
            _vacuumEnergy = vacuumEnergy.Value;

        if (averageCurvature.HasValue)
            _averageCurvature = averageCurvature.Value;

        if (spectralDimension.HasValue)
            _spectralDimension = spectralDimension.Value;

        if (liebRobinsonSpeed.HasValue)
            _liebRobinsonSpeed = liebRobinsonSpeed.Value;
    }

    /// <summary>
    /// Dispose resources (call on application shutdown).
    /// </summary>
    public static void Dispose()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}
