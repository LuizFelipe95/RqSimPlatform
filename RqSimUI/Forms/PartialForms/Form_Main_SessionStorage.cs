using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RqSimForms.ProcessesDispatcher;
using RqSimPlatform.Contracts;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main — per-session folder management, metrics JSONL
/// streaming, and session info persistence.
/// </summary>
partial class Form_Main_RqSim
{
    /// <summary>
    /// Currently active session ID (<c>null</c> when no session is in progress).
    /// Format: <c>yyyy-MM-dd_HH-mm-ss_{mode}</c>.
    /// </summary>
    private string? _currentSessionId;

    /// <summary>
    /// Buffered writer for streaming metrics JSONL.
    /// </summary>
    private StreamWriter? _metricsWriter;

    /// <summary>
    /// UTC timestamp when the current session was started.
    /// </summary>
    private DateTime _sessionStartTimeUtc;

    /// <summary>
    /// Monotonically increasing counter of metrics records written in the current session.
    /// Used for throttling — only every Nth call actually writes to disk.
    /// </summary>
    private long _metricsRecordCount;

    /// <summary>
    /// Write a metrics record at most every N calls to avoid disk saturation.
    /// </summary>
    private const int MetricsWriteInterval = 20;

    private static readonly JsonSerializerOptions SessionInfoJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ================================================================
    //  SESSION LIFECYCLE
    // ================================================================

    /// <summary>
    /// Starts a new session: creates the per-session directory tree,
    /// writes <c>session_info.json</c> with a settings snapshot,
    /// and opens the metrics JSONL writer.
    /// </summary>
    /// <param name="mode"><c>"local"</c> or <c>"console"</c>.</param>
    private void StartNewSession(string mode)
    {
        // Finalize any previous session that was not properly closed
        FinalizeCurrentSession();

        string sessionId = SessionStoragePaths.GenerateSessionId(mode);
        _currentSessionId = sessionId;
        _sessionStartTimeUtc = DateTime.UtcNow;
        _metricsRecordCount = 0;

        try
        {
            SessionStoragePaths.EnsureSessionDirectoryStructure(sessionId);

            // Capture settings snapshot
            ServerModeSettingsDto settingsSnapshot;
            if (InvokeRequired)
            {
                settingsSnapshot = (ServerModeSettingsDto)Invoke(BuildServerModeSettingsFromUI);
            }
            else
            {
                settingsSnapshot = BuildServerModeSettingsFromUI();
            }

            // Write session_info.json
            var sessionInfo = new SessionInfo
            {
                SessionId = sessionId,
                StartTimeUtc = _sessionStartTimeUtc,
                Mode = mode,
                NodeCount = settingsSnapshot.NodeCount,
                TargetDegree = settingsSnapshot.TargetDegree,
                TotalSteps = settingsSnapshot.TotalSteps,
                SettingsSnapshot = settingsSnapshot
            };

            string infoJson = JsonSerializer.Serialize(sessionInfo, SessionInfoJsonOptions);
            File.WriteAllText(SessionStoragePaths.GetSessionInfoPath(sessionId), infoJson);

            // Open metrics JSONL writer with buffering
            string metricsPath = SessionStoragePaths.GetMetricsLogPath(sessionId);
            _metricsWriter = new StreamWriter(
                new FileStream(metricsPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 8192),
                leaveOpen: false)
            {
                AutoFlush = false
            };

            Debug.WriteLine($"[SessionStorage] Started session {sessionId}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SessionStorage] Failed to start session: {ex.Message}");
            _currentSessionId = null;
            DisposeSessionWriter();
        }
    }

    /// <summary>
    /// Appends a single metrics record to the JSONL file.
    /// Throttled: only writes every <see cref="MetricsWriteInterval"/> calls.
    /// Thread-safe — can be called from the UI timer or console polling timer.
    /// </summary>
    private void AppendMetricsRecord(
        long iteration,
        int nodeCount,
        int edgeCount,
        double spectralDim,
        double energy,
        int excitedCount,
        int largestCluster,
        double temperature,
        double effectiveG)
    {
        if (_metricsWriter is null || _currentSessionId is null)
            return;

        long count = Interlocked.Increment(ref _metricsRecordCount);
        if (count % MetricsWriteInterval != 0)
            return;

        try
        {
            // Hand-craft JSONL line to avoid allocation from JsonSerializer
            string line = string.Create(CultureInfo.InvariantCulture,
                $"{{\"i\":{iteration},\"n\":{nodeCount},\"e\":{edgeCount}," +
                $"\"ds\":{spectralDim:G6},\"E\":{energy:G6}," +
                $"\"ex\":{excitedCount},\"lc\":{largestCluster}," +
                $"\"T\":{temperature:G6},\"G\":{effectiveG:G6}," +
                $"\"ts\":\"{DateTime.UtcNow:O}\"}}");

            lock (_metricsWriter)
            {
                _metricsWriter.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SessionStorage] Metrics write failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Finalizes the current session: writes end-time and final metrics into
    /// <c>session_info.json</c>, flushes and closes the metrics writer.
    /// </summary>
    private void FinalizeCurrentSession()
    {
        if (_currentSessionId is null)
            return;

        string sessionId = _currentSessionId;

        try
        {
            // Flush and close the metrics writer
            FlushAndDisposeMetricsWriter();

            // Update session_info.json with end time and final metrics
            string infoPath = SessionStoragePaths.GetSessionInfoPath(sessionId);
            if (File.Exists(infoPath))
            {
                string json = File.ReadAllText(infoPath);
                var sessionInfo = JsonSerializer.Deserialize<SessionInfo>(json, SessionInfoJsonOptions);
                if (sessionInfo is not null)
                {
                    sessionInfo.EndTimeUtc = DateTime.UtcNow;
                    sessionInfo.CompletedSteps = _simApi.Dispatcher.LiveStep;
                    sessionInfo.FinalSpectralDimension = _simApi.Dispatcher.LiveSpectralDim;

                    string updatedJson = JsonSerializer.Serialize(sessionInfo, SessionInfoJsonOptions);
                    File.WriteAllText(infoPath, updatedJson);
                }
            }

            Debug.WriteLine($"[SessionStorage] Finalized session {sessionId} " +
                            $"(steps={_simApi.Dispatcher.LiveStep}, records={_metricsRecordCount})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SessionStorage] Failed to finalize session: {ex.Message}");
        }
        finally
        {
            _currentSessionId = null;
            _metricsRecordCount = 0;
        }
    }

    /// <summary>
    /// Flushes the buffered metrics writer and releases resources.
    /// </summary>
    private void FlushAndDisposeMetricsWriter()
    {
        if (_metricsWriter is null)
            return;

        try
        {
            lock (_metricsWriter)
            {
                _metricsWriter.Flush();
            }

            _metricsWriter.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SessionStorage] Metrics writer dispose failed: {ex.Message}");
        }
        finally
        {
            _metricsWriter = null;
        }
    }

    /// <summary>
    /// Disposes the metrics writer without flushing (used on error paths).
    /// </summary>
    private void DisposeSessionWriter()
    {
        try
        {
            _metricsWriter?.Dispose();
        }
        catch
        {
            // Best-effort cleanup
        }
        finally
        {
            _metricsWriter = null;
        }
    }

    // ================================================================
    //  METRICS SAMPLING — HELPERS
    // ================================================================

    /// <summary>
    /// Samples current metrics from <see cref="MetricsDispatcher"/> and appends
    /// a record to the session JSONL. Called from UI timer or console polling.
    /// </summary>
    private void SampleAndAppendMetrics()
    {
        if (_currentSessionId is null)
            return;

        var d = _simApi.Dispatcher;
        AppendMetricsRecord(
            iteration: d.LiveStep,
            nodeCount: d.LiveNodeCount,
            edgeCount: d.LiveEdgeCount,
            spectralDim: d.LiveSpectralDim,
            energy: d.LiveSystemEnergy,
            excitedCount: d.LiveExcited,
            largestCluster: d.LiveLargestCluster,
            temperature: d.LiveTemp,
            effectiveG: d.LiveEffectiveG);
    }

    // ================================================================
    //  SESSION INFO DTO
    // ================================================================

    /// <summary>
    /// Metadata written to <c>session_info.json</c> at the start and updated
    /// at finalization of each simulation session.
    /// </summary>
    private sealed class SessionInfo
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("startTimeUtc")]
        public DateTime StartTimeUtc { get; set; }

        [JsonPropertyName("endTimeUtc")]
        public DateTime? EndTimeUtc { get; set; }

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "";

        [JsonPropertyName("nodeCount")]
        public int NodeCount { get; set; }

        [JsonPropertyName("targetDegree")]
        public int TargetDegree { get; set; }

        [JsonPropertyName("totalSteps")]
        public int TotalSteps { get; set; }

        [JsonPropertyName("completedSteps")]
        public int CompletedSteps { get; set; }

        [JsonPropertyName("finalSpectralDimension")]
        public double? FinalSpectralDimension { get; set; }

        [JsonPropertyName("settingsSnapshot")]
        public ServerModeSettingsDto? SettingsSnapshot { get; set; }
    }
}
