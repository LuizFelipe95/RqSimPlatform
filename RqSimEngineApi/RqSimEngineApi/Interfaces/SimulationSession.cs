using RQSimulation;
using System.Text.Json.Serialization;

namespace RqSimUI.FormSimAPI.Interfaces;

/// <summary>
/// Stores complete snapshot of a simulation session for archiving and export
/// </summary>
public sealed record SimulationSession
{
    /// <summary>Unique session identifier</summary>
    public Guid SessionId { get; init; } = Guid.NewGuid();

    /// <summary>When session started</summary>
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>When session ended (null if still running)</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>How simulation ended</summary>
    public SessionEndReason EndReason { get; set; } = SessionEndReason.Unknown;

    /// <summary>Configuration used for this session</summary>
    public SimulationConfig? Config { get; init; }

    /// <summary>GPU enabled flag</summary>
    public bool GpuEnabled { get; init; }

    /// <summary>GPU device name</summary>
    public string? GpuDeviceName { get; init; }

    /// <summary>Final result metrics</summary>
    public SimulationResult? Result { get; set; }

    /// <summary>Modern simulation result if applicable</summary>
    public RQSimulation.ExampleModernSimulation.ScenarioResult? ModernResult { get; set; }

    /// <summary>Time series: steps</summary>
    public List<int> SeriesSteps { get; init; } = [];

    /// <summary>Time series: excited count</summary>
    public List<int> SeriesExcited { get; init; } = [];

    /// <summary>Time series: heavy mass</summary>
    public List<double> SeriesHeavyMass { get; init; } = [];

    /// <summary>Time series: heavy count</summary>
    public List<int> SeriesHeavyCount { get; init; } = [];

    /// <summary>Time series: largest cluster</summary>
    public List<int> SeriesLargestCluster { get; init; } = [];

    /// <summary>Time series: avg link distance</summary>
    public List<double> SeriesAvgDist { get; init; } = [];

    /// <summary>Time series: density</summary>
    public List<double> SeriesDensity { get; init; } = [];

    /// <summary>Time series: energy</summary>
    public List<double> SeriesEnergy { get; init; } = [];

    /// <summary>Time series: correlation</summary>
    public List<double> SeriesCorr { get; init; } = [];

    /// <summary>Time series: strong edges</summary>
    public List<int> SeriesStrongEdges { get; init; } = [];

    /// <summary>Time series: quantum norm</summary>
    public List<double> SeriesQNorm { get; init; } = [];

    /// <summary>Time series: quantum energy</summary>
    public List<double> SeriesQEnergy { get; init; } = [];

    /// <summary>Time series: entanglement</summary>
    public List<double> SeriesEntanglement { get; init; } = [];

    /// <summary>Time series: spectral dimension</summary>
    public List<double> SeriesSpectralDimension { get; init; } = [];

    /// <summary>Time series: network temperature</summary>
    public List<double> SeriesNetworkTemperature { get; init; } = [];

    /// <summary>Time series: effective gravitational coupling</summary>
    public List<double> SeriesEffectiveG { get; init; } = [];

    /// <summary>Time series: adaptive heavy threshold</summary>
    public List<double> SeriesAdaptiveThreshold { get; init; } = [];

    /// <summary>Synthesis analysis data</summary>
    public List<(int volume, double deltaMass)>? SynthesisData { get; init; }

    /// <summary>Synthesis events count</summary>
    public int SynthesisCount { get; init; }

    /// <summary>Fission events count</summary>
    public int FissionCount { get; init; }

    /// <summary>Display filters applied</summary>
    public DisplayFilters Filters { get; init; } = new();

    /// <summary>Console log captured during session</summary>
    public string ConsoleLog { get; set; } = string.Empty;

    /// <summary>Summary text</summary>
    public string SummaryText { get; set; } = string.Empty;

    /// <summary>Important events captured</summary>
    public List<ImportantEvent> ImportantEvents { get; init; } = [];

    /// <summary>Last step completed</summary>
    public int LastStep { get; set; }

    /// <summary>Total steps planned</summary>
    public int TotalStepsPlanned { get; set; }

    /// <summary>Final spectral dimension value</summary>
    public double FinalSpectralDimension { get; set; }

    /// <summary>Final network temperature</summary>
    public double FinalNetworkTemperature { get; set; }

    /// <summary>Simulation duration in seconds</summary>
    public double WallClockDurationSeconds { get; set; }
}

/// <summary>
/// Reason why simulation session ended
/// </summary>
public enum SessionEndReason
{
    Unknown = 0,
    Completed,
    CancelledByUser,
    Error,
    EnergyTerminated,
    FragmentationTerminated
}

/// <summary>
/// Display filter settings used during visualization
/// </summary>
public sealed record DisplayFilters
{
    public double WeightThreshold { get; init; }
    public bool HeavyOnly { get; init; }
    public bool DynamicLayout { get; init; }
}

/// <summary>
/// Important event captured during simulation
/// </summary>
public sealed record ImportantEvent
{
    public int Step { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
