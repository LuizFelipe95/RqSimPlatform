using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using RqSimRenderingEngine.Rendering.Diagnostics;
using RqSimRenderingEngine.Abstractions;

namespace RqSimVisualization;

/// <summary>
/// Partial class for render benchmark UI integration.
/// Provides performance overlay, benchmark controls, and results logging.
/// </summary>
public partial class RqSimVisualizationForm
{
    private RenderBenchmark? _renderBenchmark;
    private BenchmarkLogger? _benchmarkLogger;
    private Panel? _benchmarkOverlay;
    private Label? _lblFps;
    private Label? _lblFrameTime;
    private Label? _lblMemory;
    private Label? _lblBenchmarkStatus;
    private Button? _btnStartBenchmark;
    private Button? _btnStopBenchmark;
    private CheckBox? _chkShowOverlay;
    private System.Windows.Forms.Timer? _benchmarkUpdateTimer;

    private bool _benchmarkOverlayVisible = true;
    private const string BenchmarkLogFileName = "benchmark_results.csv";

    /// <summary>
    /// Current render benchmark instance.
    /// </summary>
    public RenderBenchmark? RenderBenchmark => _renderBenchmark;

    /// <summary>
    /// Initialize benchmark UI components.
    /// Call from Form_Load or after render backend initialization.
    /// </summary>
    private void InitializeBenchmarkUI()
    {
        if (_benchmarkOverlay is not null)
            return;

        // Create benchmark instance
        _renderBenchmark = new RenderBenchmark();

        // Create overlay panel
        _benchmarkOverlay = new Panel
        {
            Size = new Size(220, 140),
            BackColor = Color.FromArgb(200, 20, 20, 20),
            BorderStyle = BorderStyle.None,
            Visible = _benchmarkOverlayVisible
        };

        // Position in top-right corner
        PositionBenchmarkOverlay();

        // FPS label
        _lblFps = new Label
        {
            Text = "FPS: --",
            ForeColor = Color.LimeGreen,
            BackColor = Color.Transparent,
            Font = new Font("Consolas", 14, FontStyle.Bold),
            Location = new Point(10, 10),
            AutoSize = true
        };

        // Frame time label
        _lblFrameTime = new Label
        {
            Text = "Frame: -- ms (P99: -- ms)",
            ForeColor = Color.LightGray,
            BackColor = Color.Transparent,
            Font = new Font("Consolas", 9),
            Location = new Point(10, 38),
            AutoSize = true
        };

        // Memory label
        _lblMemory = new Label
        {
            Text = "Mem: -- MB (Peak: -- MB)",
            ForeColor = Color.LightGray,
            BackColor = Color.Transparent,
            Font = new Font("Consolas", 9),
            Location = new Point(10, 56),
            AutoSize = true
        };

        // Benchmark status label
        _lblBenchmarkStatus = new Label
        {
            Text = "Benchmark: Idle",
            ForeColor = Color.Gray,
            BackColor = Color.Transparent,
            Font = new Font("Consolas", 9),
            Location = new Point(10, 74),
            AutoSize = true
        };

        // Start benchmark button
        _btnStartBenchmark = new Button
        {
            Text = "Start",
            Size = new Size(60, 25),
            Location = new Point(10, 100),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(40, 80, 40),
            ForeColor = Color.White
        };
        _btnStartBenchmark.FlatAppearance.BorderColor = Color.DarkGreen;
        _btnStartBenchmark.Click += OnStartBenchmarkClick;

        // Stop benchmark button
        _btnStopBenchmark = new Button
        {
            Text = "Stop",
            Size = new Size(60, 25),
            Location = new Point(75, 100),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(80, 40, 40),
            ForeColor = Color.White,
            Enabled = false
        };
        _btnStopBenchmark.FlatAppearance.BorderColor = Color.DarkRed;
        _btnStopBenchmark.Click += OnStopBenchmarkClick;

        // Show overlay checkbox
        _chkShowOverlay = new CheckBox
        {
            Text = "Show",
            ForeColor = Color.LightGray,
            BackColor = Color.Transparent,
            Font = new Font("Consolas", 8),
            Location = new Point(145, 104),
            AutoSize = true,
            Checked = true
        };
        _chkShowOverlay.CheckedChanged += OnShowOverlayChanged;

        // Add controls to overlay
        _benchmarkOverlay.Controls.Add(_lblFps);
        _benchmarkOverlay.Controls.Add(_lblFrameTime);
        _benchmarkOverlay.Controls.Add(_lblMemory);
        _benchmarkOverlay.Controls.Add(_lblBenchmarkStatus);
        _benchmarkOverlay.Controls.Add(_btnStartBenchmark);
        _benchmarkOverlay.Controls.Add(_btnStopBenchmark);
        _benchmarkOverlay.Controls.Add(_chkShowOverlay);

        // Add overlay to appropriate container
        AddBenchmarkOverlayToContainer();

        // Create update timer
        _benchmarkUpdateTimer = new System.Windows.Forms.Timer
        {
            Interval = 100 // Update 10 times per second
        };
        _benchmarkUpdateTimer.Tick += OnBenchmarkUpdateTick;
        _benchmarkUpdateTimer.Start();

        // Handle form resize to reposition overlay
        Resize += (_, _) => PositionBenchmarkOverlay();
    }

    /// <summary>
    /// Call this at the beginning of each render frame.
    /// </summary>
    public void BeginRenderFrame()
    {
        _renderBenchmark?.BeginFrame();
    }

    /// <summary>
    /// Call this at the end of each render frame.
    /// </summary>
    /// <param name="gpuTimeMs">Optional GPU time in milliseconds</param>
    public void EndRenderFrame(double gpuTimeMs = 0)
    {
        _renderBenchmark?.EndFrame(gpuTimeMs);
    }

    /// <summary>
    /// Get current benchmark report (for external use).
    /// </summary>
    public BenchmarkReport? GetBenchmarkReport()
    {
        return _renderBenchmark?.GetReport();
    }

    private void AddBenchmarkOverlayToContainer()
    {
        if (_benchmarkOverlay is null)
            return;

        // Try to add to DX12 panel first, otherwise add to form
        if (_dx12Panel is not null)
        {
            _dx12Panel.Controls.Add(_benchmarkOverlay);
            _benchmarkOverlay.BringToFront();
        }
        else
        {
            Controls.Add(_benchmarkOverlay);
            _benchmarkOverlay.BringToFront();
        }
    }

    private void PositionBenchmarkOverlay()
    {
        if (_benchmarkOverlay is null)
            return;

        // Position in top-right corner of container
        var container = _benchmarkOverlay.Parent;
        if (container is null)
            return;

        int x = container.ClientSize.Width - _benchmarkOverlay.Width - 10;
        int y = 10;

        _benchmarkOverlay.Location = new Point(Math.Max(x, 10), y);
    }

    private void OnStartBenchmarkClick(object? sender, EventArgs e)
    {
        if (_renderBenchmark is null)
            return;

        _renderBenchmark.Start();

        // Initialize logger
        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RqSimulator",
            "Benchmarks",
            BenchmarkLogFileName);

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        _benchmarkLogger?.Dispose();
        _benchmarkLogger = new BenchmarkLogger(logPath);

        UpdateBenchmarkButtons(isRunning: true);

        Debug.WriteLine($"[Benchmark] Started. Logging to: {logPath}");
    }

    private void OnStopBenchmarkClick(object? sender, EventArgs e)
    {
        if (_renderBenchmark is null)
            return;

        _renderBenchmark.Stop();

        // Log final report
        if (_benchmarkLogger is not null)
        {
            var report = _renderBenchmark.GetReport();
            _benchmarkLogger.Log(report);
            _benchmarkLogger.Dispose();
            _benchmarkLogger = null;

            Debug.WriteLine($"[Benchmark] Stopped. Final report logged.");
            Debug.WriteLine(_renderBenchmark.GetSummary());
        }

        UpdateBenchmarkButtons(isRunning: false);
    }

    private void OnShowOverlayChanged(object? sender, EventArgs e)
    {
        _benchmarkOverlayVisible = _chkShowOverlay?.Checked ?? true;

        // Only hide the metrics labels, not the controls
        if (_lblFps is not null) _lblFps.Visible = _benchmarkOverlayVisible;
        if (_lblFrameTime is not null) _lblFrameTime.Visible = _benchmarkOverlayVisible;
        if (_lblMemory is not null) _lblMemory.Visible = _benchmarkOverlayVisible;
    }

    private void OnBenchmarkUpdateTick(object? sender, EventArgs e)
    {
        if (_renderBenchmark is null || !_benchmarkOverlayVisible)
            return;

        UpdateBenchmarkLabels();

        // Log periodic reports during benchmark
        if (_renderBenchmark.IsRunning && _renderBenchmark.FrameCount % 600 == 0) // Every ~10 seconds at 60 FPS
        {
            _benchmarkLogger?.Log(_renderBenchmark.GetReport());
        }
    }

    private void UpdateBenchmarkLabels()
    {
        if (_renderBenchmark is null)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(UpdateBenchmarkLabels);
            return;
        }

        double fps = _renderBenchmark.CurrentFps;
        double frameTime = _renderBenchmark.AverageFrameTimeMs;
        double p99 = _renderBenchmark.P99FrameTimeMs;
        double mem = _renderBenchmark.CurrentMemoryMB;
        double peakMem = _renderBenchmark.PeakMemoryMB;

        // Update FPS with color coding
        if (_lblFps is not null)
        {
            _lblFps.Text = $"FPS: {fps:F1}";
            _lblFps.ForeColor = fps switch
            {
                >= 60 => Color.LimeGreen,
                >= 30 => Color.Yellow,
                _ => Color.Red
            };
        }

        if (_lblFrameTime is not null)
        {
            _lblFrameTime.Text = $"Frame: {frameTime:F2} ms (P99: {p99:F2} ms)";
        }

        if (_lblMemory is not null)
        {
            _lblMemory.Text = $"Mem: {mem:F0} MB (Peak: {peakMem:F0} MB)";
        }

        if (_lblBenchmarkStatus is not null)
        {
            if (_renderBenchmark.IsRunning)
            {
                _lblBenchmarkStatus.Text = $"Benchmark: {_renderBenchmark.SessionDurationSeconds:F1}s";
                _lblBenchmarkStatus.ForeColor = Color.Cyan;
            }
            else
            {
                _lblBenchmarkStatus.Text = "Benchmark: Idle";
                _lblBenchmarkStatus.ForeColor = Color.Gray;
            }
        }
    }

    private void UpdateBenchmarkButtons(bool isRunning)
    {
        if (_btnStartBenchmark is not null)
            _btnStartBenchmark.Enabled = !isRunning;

        if (_btnStopBenchmark is not null)
            _btnStopBenchmark.Enabled = isRunning;
    }

    /// <summary>
    /// Clean up benchmark resources.
    /// Call from Form_FormClosing or Dispose.
    /// </summary>
    private void DisposeBenchmarkUI()
    {
        _benchmarkUpdateTimer?.Stop();
        _benchmarkUpdateTimer?.Dispose();
        _benchmarkLogger?.Dispose();
        _renderBenchmark?.Dispose();
    }
}
