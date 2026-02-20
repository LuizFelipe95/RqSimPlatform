using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RQSimulation.Core.Infrastructure;

/// <summary>
/// Complete snapshot of simulation state including graph topology and all module states.
///
/// PURPOSE:
/// ========
/// Enables full simulation checkpointing for:
/// - Save/resume across sessions (long-running experiments)
/// - Exact reproducibility (research validation)
/// - Debugging and forensic analysis
/// - Distributed computing (work migration)
/// - A/B testing (branch from same initial state)
///
/// COMPONENTS:
/// ===========
/// 1. Graph Topology - Full CSR representation via GraphSnapshot
/// 2. Module States - Per-module serialized state via ISerializableModule
/// 3. Pipeline Metadata - Execution count, timing, configuration
/// 4. Physics State - Energy ledger, conservation violations, etc.
///
/// SERIALIZATION:
/// ==============
/// Uses System.Text.Json for human-readable JSON format.
/// - Graph arrays encoded as base64 for efficiency
/// - Module states as nested JSON objects
/// - Metadata as structured records
///
/// FILE SIZE ESTIMATES:
/// ====================
/// For N=10,000 nodes, E=50,000 edges:
/// - Topology: ~2 MB (CSR arrays)
/// - Module states: ~1 MB (depends on module count)
/// - Total: ~3-5 MB uncompressed
/// - With compression: ~500 KB - 1 MB (LZ4 or gzip)
///
/// USAGE:
/// ======
/// <code>
/// // Save
/// var snapshot = SimulationSnapshot.Capture(graph, pipeline, tickId);
/// var json = JsonSerializer.Serialize(snapshot, options);
/// File.WriteAllText("checkpoint.json", json);
///
/// // Load
/// var json = File.ReadAllText("checkpoint.json");
/// var snapshot = JsonSerializer.Deserialize&lt;SimulationSnapshot&gt;(json);
/// snapshot.Restore(graph, pipeline);
/// </code>
/// </summary>
public sealed class SimulationSnapshot
{
    /// <summary>
    /// Version of the snapshot format for future compatibility.
    /// Increment when making breaking changes to the schema.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Snapshot format version.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Graph topology and physics state snapshot.
    /// </summary>
    [JsonPropertyName("graph")]
    public GraphSnapshot? Graph { get; set; }

    /// <summary>
    /// Per-module serialized states.
    /// Key: Module name, Value: Module-specific state object.
    /// </summary>
    [JsonPropertyName("moduleStates")]
    public Dictionary<string, object?> ModuleStates { get; set; } = new();

    /// <summary>
    /// Simulation execution count (total frames executed).
    /// </summary>
    [JsonPropertyName("executionCount")]
    public long ExecutionCount { get; set; }

    /// <summary>
    /// Simulation time (accumulated dt).
    /// </summary>
    [JsonPropertyName("simulationTime")]
    public double SimulationTime { get; set; }

    /// <summary>
    /// Timestamp when snapshot was created.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional description or experiment name.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Configuration settings active at snapshot time.
    /// Stored as dictionary for flexibility across versions.
    /// </summary>
    [JsonPropertyName("settings")]
    public Dictionary<string, object> Settings { get; set; } = new();

    /// <summary>
    /// Physics metadata (energy, violations, etc.).
    /// </summary>
    [JsonPropertyName("physicsMetadata")]
    public PhysicsSnapshotMetadata PhysicsMetadata { get; set; } = new();

    /// <summary>
    /// Create empty snapshot.
    /// </summary>
    public SimulationSnapshot()
    {
    }

    /// <summary>
    /// Validate snapshot integrity.
    /// </summary>
    /// <returns>True if snapshot is valid and can be restored</returns>
    public bool Validate()
    {
        // Check version compatibility
        if (Version < 1 || Version > CurrentVersion)
            return false;

        // Validate graph snapshot
        if (Graph == null || !Graph.Validate())
            return false;

        // Check for reasonable values
        if (ExecutionCount < 0)
            return false;

        if (SimulationTime < 0)
            return false;

        return true;
    }

    /// <summary>
    /// Get approximate memory footprint in bytes.
    /// </summary>
    public long ApproximateSizeBytes
    {
        get
        {
            long size = 0;

            // Graph snapshot
            if (Graph != null)
                size += Graph.ApproximateSizeBytes;

            // Module states (rough estimate: 1 KB per module)
            size += ModuleStates.Count * 1024;

            // Metadata overhead
            size += 1024;

            return size;
        }
    }

    /// <summary>
    /// Serialize to JSON string.
    /// </summary>
    public string ToJson(bool indented = false)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(this, options);
    }

    /// <summary>
    /// Deserialize from JSON string.
    /// </summary>
    public static SimulationSnapshot? FromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        return JsonSerializer.Deserialize<SimulationSnapshot>(json, options);
    }
}

/// <summary>
/// Physics-specific metadata captured in snapshot.
/// </summary>
public sealed class PhysicsSnapshotMetadata
{
    /// <summary>
    /// Total vacuum energy pool at snapshot time.
    /// </summary>
    [JsonPropertyName("vacuumEnergy")]
    public double VacuumEnergy { get; set; }

    /// <summary>
    /// Total action at snapshot time.
    /// </summary>
    [JsonPropertyName("totalAction")]
    public double TotalAction { get; set; }

    /// <summary>
    /// Number of constraint violations detected.
    /// </summary>
    [JsonPropertyName("violationCount")]
    public int ViolationCount { get; set; }

    /// <summary>
    /// Recent constraint violations (last N).
    /// </summary>
    [JsonPropertyName("recentViolations")]
    public List<string> RecentViolations { get; set; } = new();

    /// <summary>
    /// Spectral dimension at snapshot time (if computed).
    /// </summary>
    [JsonPropertyName("spectralDimension")]
    public double? SpectralDimension { get; set; }

    /// <summary>
    /// Average node curvature.
    /// </summary>
    [JsonPropertyName("averageCurvature")]
    public double? AverageCurvature { get; set; }

    /// <summary>
    /// Lieb-Robinson speed (emergent light speed).
    /// </summary>
    [JsonPropertyName("liebRobinsonSpeed")]
    public double? LiebRobinsonSpeed { get; set; }

    /// <summary>
    /// Additional custom physics metrics.
    /// </summary>
    [JsonPropertyName("customMetrics")]
    public Dictionary<string, double> CustomMetrics { get; set; } = new();
}
