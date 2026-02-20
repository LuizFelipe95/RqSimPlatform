using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RqSimRenderingEngine.Rendering.Diagnostics;

/// <summary>
/// Performance benchmarking utilities for render backends.
/// Provides FPS measurement, frame timing, and resource monitoring.
/// </summary>
public sealed class RenderBenchmark : IDisposable
{
    private readonly Stopwatch _sessionWatch = new();
    private readonly Stopwatch _frameWatch = new();
    private readonly Queue<double> _frameTimes = new();
    private readonly Queue<double> _cpuTimes = new();
    private readonly Queue<double> _gpuTimes = new();

    private const int SampleWindow = 120; // ~2 seconds at 60 FPS

    private long _frameCount;
    private double _lastFrameTimeMs;
    private double _lastCpuTimeMs;
    private double _lastGpuTimeMs;

    private long _peakMemoryBytes;
    private long _currentMemoryBytes;

    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// Total frames rendered during this benchmark session.
    /// </summary>
    public long FrameCount => _frameCount;

    /// <summary>
    /// Current frames per second (averaged over sample window).
    /// </summary>
    public double CurrentFps { get; private set; }

    /// <summary>
    /// Average frame time in milliseconds (over sample window).
    /// </summary>
    public double AverageFrameTimeMs { get; private set; }

    /// <summary>
    /// Minimum frame time in milliseconds (over sample window).
    /// </summary>
    public double MinFrameTimeMs { get; private set; }

    /// <summary>
    /// Maximum frame time in milliseconds (over sample window).
    /// </summary>
    public double MaxFrameTimeMs { get; private set; }

    /// <summary>
    /// 99th percentile frame time in milliseconds.
    /// </summary>
    public double P99FrameTimeMs { get; private set; }

    /// <summary>
    /// Average CPU time per frame in milliseconds.
    /// </summary>
    public double AverageCpuTimeMs { get; private set; }

    /// <summary>
    /// Average GPU time per frame in milliseconds (if available).
    /// </summary>
    public double AverageGpuTimeMs { get; private set; }

    /// <summary>
    /// Total session duration in seconds.
    /// </summary>
    public double SessionDurationSeconds => _sessionWatch.Elapsed.TotalSeconds;

    /// <summary>
    /// Peak memory usage in megabytes.
    /// </summary>
    public double PeakMemoryMB => _peakMemoryBytes / (1024.0 * 1024.0);

    /// <summary>
    /// Current memory usage in megabytes.
    /// </summary>
    public double CurrentMemoryMB => _currentMemoryBytes / (1024.0 * 1024.0);

    /// <summary>
    /// Whether the benchmark is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Start a new benchmark session.
    /// </summary>
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RenderBenchmark));

        Reset();
        _sessionWatch.Start();
        _isRunning = true;
    }

    /// <summary>
    /// Stop the benchmark session.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _sessionWatch.Stop();
        _isRunning = false;
    }

    /// <summary>
    /// Reset all benchmark data.
    /// </summary>
    public void Reset()
    {
        _sessionWatch.Reset();
        _frameWatch.Reset();
        _frameTimes.Clear();
        _cpuTimes.Clear();
        _gpuTimes.Clear();
        _frameCount = 0;
        _lastFrameTimeMs = 0;
        _lastCpuTimeMs = 0;
        _lastGpuTimeMs = 0;
        _peakMemoryBytes = 0;
        _currentMemoryBytes = 0;
        CurrentFps = 0;
        AverageFrameTimeMs = 0;
        MinFrameTimeMs = 0;
        MaxFrameTimeMs = 0;
        P99FrameTimeMs = 0;
        AverageCpuTimeMs = 0;
        AverageGpuTimeMs = 0;
    }

    /// <summary>
    /// Mark the beginning of a frame. Call before rendering starts.
    /// </summary>
    public void BeginFrame()
    {
        if (!_isRunning)
            return;

        _frameWatch.Restart();
    }

    /// <summary>
    /// Mark the end of a frame. Call after rendering completes.
    /// </summary>
    /// <param name="gpuTimeMs">Optional GPU time in milliseconds (from GPU queries)</param>
    public void EndFrame(double gpuTimeMs = 0)
    {
        if (!_isRunning)
            return;

        _frameWatch.Stop();
        _frameCount++;

        _lastFrameTimeMs = _frameWatch.Elapsed.TotalMilliseconds;
        _lastGpuTimeMs = gpuTimeMs;
        _lastCpuTimeMs = _lastFrameTimeMs - gpuTimeMs;

        // Add to sample queues
        _frameTimes.Enqueue(_lastFrameTimeMs);
        _cpuTimes.Enqueue(_lastCpuTimeMs);
        _gpuTimes.Enqueue(_lastGpuTimeMs);

        // Maintain window size
        while (_frameTimes.Count > SampleWindow)
        {
            _frameTimes.Dequeue();
            _cpuTimes.Dequeue();
            _gpuTimes.Dequeue();
        }

        // Update metrics
        UpdateMetrics();

        // Update memory stats periodically (every 60 frames)
        if (_frameCount % 60 == 0)
        {
            UpdateMemoryStats();
        }
    }

    /// <summary>
    /// Record CPU-only frame timing (when GPU time is not available).
    /// </summary>
    /// <param name="cpuTimeMs">CPU time in milliseconds</param>
    public void RecordCpuTime(double cpuTimeMs)
    {
        if (!_isRunning)
            return;

        _lastCpuTimeMs = cpuTimeMs;
        _cpuTimes.Enqueue(cpuTimeMs);

        while (_cpuTimes.Count > SampleWindow)
            _cpuTimes.Dequeue();

        if (_cpuTimes.Count > 0)
            AverageCpuTimeMs = _cpuTimes.Average();
    }

    /// <summary>
    /// Get a formatted summary of current benchmark results.
    /// </summary>
    public string GetSummary()
    {
        return $"""
            === Render Benchmark Results ===
            Session Duration: {SessionDurationSeconds:F1}s
            Total Frames: {_frameCount:N0}
            
            FPS: {CurrentFps:F1}
            Avg Frame Time: {AverageFrameTimeMs:F2}ms
            Min Frame Time: {MinFrameTimeMs:F2}ms
            Max Frame Time: {MaxFrameTimeMs:F2}ms
            P99 Frame Time: {P99FrameTimeMs:F2}ms
            
            Avg CPU Time: {AverageCpuTimeMs:F2}ms
            Avg GPU Time: {AverageGpuTimeMs:F2}ms
            
            Current Memory: {CurrentMemoryMB:F1}MB
            Peak Memory: {PeakMemoryMB:F1}MB
            ================================
            """;
    }

    /// <summary>
    /// Get benchmark results as a structured report.
    /// </summary>
    public BenchmarkReport GetReport()
    {
        return new BenchmarkReport
        {
            SessionDurationSeconds = SessionDurationSeconds,
            FrameCount = _frameCount,
            CurrentFps = CurrentFps,
            AverageFrameTimeMs = AverageFrameTimeMs,
            MinFrameTimeMs = MinFrameTimeMs,
            MaxFrameTimeMs = MaxFrameTimeMs,
            P99FrameTimeMs = P99FrameTimeMs,
            AverageCpuTimeMs = AverageCpuTimeMs,
            AverageGpuTimeMs = AverageGpuTimeMs,
            CurrentMemoryMB = CurrentMemoryMB,
            PeakMemoryMB = PeakMemoryMB,
            Timestamp = DateTime.UtcNow
        };
    }

    private void UpdateMetrics()
    {
        if (_frameTimes.Count == 0)
            return;

        var times = _frameTimes.ToArray();
        Array.Sort(times);

        AverageFrameTimeMs = times.Average();
        MinFrameTimeMs = times[0];
        MaxFrameTimeMs = times[^1];

        // Calculate P99
        int p99Index = (int)(times.Length * 0.99);
        P99FrameTimeMs = times[Math.Min(p99Index, times.Length - 1)];

        // Calculate FPS from average frame time
        if (AverageFrameTimeMs > 0)
            CurrentFps = 1000.0 / AverageFrameTimeMs;

        // Update CPU/GPU averages
        if (_cpuTimes.Count > 0)
            AverageCpuTimeMs = _cpuTimes.Average();

        if (_gpuTimes.Count > 0)
            AverageGpuTimeMs = _gpuTimes.Average();
    }

    private void UpdateMemoryStats()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            _currentMemoryBytes = process.WorkingSet64;
            _peakMemoryBytes = Math.Max(_peakMemoryBytes, _currentMemoryBytes);
        }
        catch
        {
            // Ignore memory query failures
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
    }
}

/// <summary>
/// Structured benchmark report for logging and analysis.
/// </summary>
public sealed record BenchmarkReport
{
    public double SessionDurationSeconds { get; init; }
    public long FrameCount { get; init; }
    public double CurrentFps { get; init; }
    public double AverageFrameTimeMs { get; init; }
    public double MinFrameTimeMs { get; init; }
    public double MaxFrameTimeMs { get; init; }
    public double P99FrameTimeMs { get; init; }
    public double AverageCpuTimeMs { get; init; }
    public double AverageGpuTimeMs { get; init; }
    public double CurrentMemoryMB { get; init; }
    public double PeakMemoryMB { get; init; }
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Convert to CSV format for logging.
    /// </summary>
    public string ToCsvLine()
    {
        return $"{Timestamp:O},{SessionDurationSeconds:F2},{FrameCount},{CurrentFps:F2}," +
               $"{AverageFrameTimeMs:F3},{MinFrameTimeMs:F3},{MaxFrameTimeMs:F3},{P99FrameTimeMs:F3}," +
               $"{AverageCpuTimeMs:F3},{AverageGpuTimeMs:F3},{CurrentMemoryMB:F1},{PeakMemoryMB:F1}";
    }

    /// <summary>
    /// CSV header for log files.
    /// </summary>
    public static string CsvHeader =>
        "Timestamp,Duration_s,Frames,FPS,AvgFrame_ms,MinFrame_ms,MaxFrame_ms,P99Frame_ms," +
        "AvgCpu_ms,AvgGpu_ms,CurrentMem_MB,PeakMem_MB";
}

/// <summary>
/// Benchmark session logger for persistent results.
/// </summary>
public sealed class BenchmarkLogger : IDisposable
{
    private readonly string _logPath;
    private readonly StreamWriter _writer;
    private bool _disposed;

    public BenchmarkLogger(string logPath)
    {
        _logPath = logPath;
        bool isNewFile = !File.Exists(logPath);

        _writer = new StreamWriter(logPath, append: true);

        if (isNewFile)
        {
            _writer.WriteLine(BenchmarkReport.CsvHeader);
            _writer.Flush();
        }
    }

    /// <summary>
    /// Log a benchmark report to the file.
    /// </summary>
    public void Log(BenchmarkReport report)
    {
        if (_disposed)
            return;

        _writer.WriteLine(report.ToCsvLine());
        _writer.Flush();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _writer.Dispose();
        _disposed = true;
    }
}
