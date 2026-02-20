using RQSimulation.Analysis;

namespace RqSimTelemetryForm;

/// <summary>
/// Console tab: System, Simulation, and GPU log display with filtering,
/// live update, and auto-scroll support.
/// </summary>
public partial class TelemetryForm
{
    // ============================================================
    // CONSOLE CONTROLS
    // ============================================================

    private TextBox _txtSysConsole = null!;
    private TextBox _txtSimConsole = null!;
    private TextBox _txtGpuConsole = null!;
    private ComboBox _cmbSysFilter = null!;
    private ComboBox _cmbSimFilter = null!;
    private CheckBox _chkAutoScrollSys = null!;
    private CheckBox _chkAutoScrollSim = null!;
    private CheckBox _chkLiveUpdateSys = null!;
    private CheckBox _chkLiveUpdateSim = null!;
    private System.Windows.Forms.Timer? _sysConsoleLiveTimer;
    private System.Windows.Forms.Timer? _simConsoleLiveTimer;

    // ============================================================
    // INITIALIZATION
    // ============================================================

    private void InitializeConsoleTab()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(3)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // Toolbar row
        var toolbar = CreateConsoleToolbar();
        mainLayout.Controls.Add(toolbar, 0, 0);

        // Console panels via SplitContainer
        var splitOuter = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300
        };

        var splitInner = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 200
        };

        // System Console
        var grpSys = new GroupBox { Text = "System Console", Dock = DockStyle.Fill };
        _txtSysConsole = CreateConsoleTextBox();
        grpSys.Controls.Add(_txtSysConsole);
        splitOuter.Panel1.Controls.Add(grpSys);

        // Simulation Console
        var grpSim = new GroupBox { Text = "Simulation Console", Dock = DockStyle.Fill };
        _txtSimConsole = CreateConsoleTextBox();
        grpSim.Controls.Add(_txtSimConsole);
        splitInner.Panel1.Controls.Add(grpSim);

        // GPU Console
        var grpGpu = new GroupBox { Text = "GPU Console", Dock = DockStyle.Fill };
        _txtGpuConsole = CreateConsoleTextBox();
        grpGpu.Controls.Add(_txtGpuConsole);
        splitInner.Panel2.Controls.Add(grpGpu);

        splitOuter.Panel2.Controls.Add(splitInner);
        mainLayout.Controls.Add(splitOuter, 0, 1);

        _tabConsole.Controls.Add(mainLayout);

        // Initialize console buffers
        _sysConsoleBuffer = new RqSimUI.FormSimAPI.Interfaces.ConsoleBuffer(_txtSysConsole, _chkAutoScrollSys);
        _simConsoleBuffer = new RqSimUI.FormSimAPI.Interfaces.ConsoleBuffer(_txtSimConsole, _chkAutoScrollSim);
    }

    private FlowLayoutPanel CreateConsoleToolbar()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            Padding = new Padding(3)
        };

        // System filter
        var lblSysFilter = new Label { Text = "Sys Filter:", AutoSize = true, Anchor = AnchorStyles.Left };
        _cmbSysFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        _cmbSysFilter.Items.AddRange(new object[] { "All", "Info", "Warning", "Error", "Dispatcher", "GPU", "IO" });
        _cmbSysFilter.SelectedIndex = 0;
        _cmbSysFilter.SelectedIndexChanged += CmbSysFilter_SelectedIndexChanged;

        // Sim filter
        var lblSimFilter = new Label { Text = "Sim Filter:", AutoSize = true, Anchor = AnchorStyles.Left };
        _cmbSimFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        _cmbSimFilter.Items.AddRange(new object[] { "All", "Info", "Warning", "Error", "Pipeline", "Physics", "Metrics" });
        _cmbSimFilter.SelectedIndex = 0;
        _cmbSimFilter.SelectedIndexChanged += CmbSimFilter_SelectedIndexChanged;

        // Checkboxes
        _chkLiveUpdateSys = new CheckBox { Text = "Live Sys", Checked = true, AutoSize = true };
        _chkLiveUpdateSys.CheckedChanged += ChkLiveUpdateSys_CheckedChanged;
        _chkLiveUpdateSim = new CheckBox { Text = "Live Sim", Checked = true, AutoSize = true };
        _chkLiveUpdateSim.CheckedChanged += ChkLiveUpdateSim_CheckedChanged;
        _chkAutoScrollSys = new CheckBox { Text = "Auto-scroll Sys", Checked = true, AutoSize = true };
        _chkAutoScrollSim = new CheckBox { Text = "Auto-scroll Sim", Checked = true, AutoSize = true };

        // Buttons
        var btnCopySys = new Button { Text = "Copy Sys", Width = 80 };
        btnCopySys.Click += BtnCopySys_Click;
        var btnClearSys = new Button { Text = "Clear Sys", Width = 80 };
        btnClearSys.Click += BtnClearSys_Click;
        var btnCopySim = new Button { Text = "Copy Sim", Width = 80 };
        btnCopySim.Click += BtnCopySim_Click;
        var btnClearSim = new Button { Text = "Clear Sim", Width = 80 };
        btnClearSim.Click += BtnClearSim_Click;
        var btnRefresh = new Button { Text = "Refresh", Width = 80 };
        btnRefresh.Click += BtnRefreshConsoles_Click;

        toolbar.Controls.AddRange(new Control[]
        {
            lblSysFilter, _cmbSysFilter, lblSimFilter, _cmbSimFilter,
            _chkLiveUpdateSys, _chkLiveUpdateSim, _chkAutoScrollSys, _chkAutoScrollSim,
            btnCopySys, btnClearSys, btnCopySim, btnClearSim, btnRefresh
        });

        return toolbar;
    }

    private static TextBox CreateConsoleTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 9F),
            WordWrap = false
        };
    }

    // ============================================================
    // EVENT HANDLERS
    // ============================================================

    private void CmbSysFilter_SelectedIndexChanged(object? sender, EventArgs e)
    {
        _sysConsoleOutType = _cmbSysFilter.SelectedIndex switch
        {
            1 => SysConsoleOutType.Info,
            2 => SysConsoleOutType.Warning,
            3 => SysConsoleOutType.Error,
            4 => SysConsoleOutType.Dispatcher,
            5 => SysConsoleOutType.GPU,
            6 => SysConsoleOutType.IO,
            _ => SysConsoleOutType.All,
        };
        RefreshSysConsole();
    }

    private void CmbSimFilter_SelectedIndexChanged(object? sender, EventArgs e)
    {
        _simConsoleOutType = _cmbSimFilter.SelectedIndex switch
        {
            1 => SimConsoleOutType.Info,
            2 => SimConsoleOutType.Warning,
            3 => SimConsoleOutType.Error,
            4 => SimConsoleOutType.Pipeline,
            5 => SimConsoleOutType.Physics,
            6 => SimConsoleOutType.Metrics,
            _ => SimConsoleOutType.All,
        };
        RefreshSimConsole();
    }

    private void ChkLiveUpdateSys_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not CheckBox cb) return;
        ToggleSysConsoleLiveUpdate(cb.Checked);
    }

    private void ChkLiveUpdateSim_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not CheckBox cb) return;
        ToggleSimConsoleLiveUpdate(cb.Checked);
    }

    private void BtnCopySys_Click(object? sender, EventArgs e)
    {
        if (_txtSysConsole is null || _txtSysConsole.IsDisposed) return;
        try { Clipboard.SetText(_txtSysConsole.Text); } catch { }
    }

    private void BtnClearSys_Click(object? sender, EventArgs e)
    {
        _sysConsoleLines.Clear();
        if (_txtSysConsole is not null && !_txtSysConsole.IsDisposed)
            _txtSysConsole.Clear();
    }

    private void BtnCopySim_Click(object? sender, EventArgs e)
    {
        if (_txtSimConsole is null || _txtSimConsole.IsDisposed) return;
        try { Clipboard.SetText(_txtSimConsole.Text); } catch { }
    }

    private void BtnClearSim_Click(object? sender, EventArgs e)
    {
        _simConsoleLines.Clear();
        if (_txtSimConsole is not null && !_txtSimConsole.IsDisposed)
            _txtSimConsole.Clear();
    }

    private void BtnRefreshConsoles_Click(object? sender, EventArgs e)
    {
        RefreshSysConsole();
        RefreshSimConsole();
    }

    // ============================================================
    // LIVE UPDATE TIMERS
    // ============================================================

    private void ToggleSysConsoleLiveUpdate(bool enabled)
    {
        if (!enabled)
        {
            _sysConsoleLiveTimer?.Stop();
            return;
        }

        _sysConsoleLiveTimer ??= new System.Windows.Forms.Timer { Interval = 150 };
        _sysConsoleLiveTimer.Tick -= SysConsoleLiveTimer_Tick;
        _sysConsoleLiveTimer.Tick += SysConsoleLiveTimer_Tick;
        _sysConsoleLiveTimer.Start();
    }

    private void ToggleSimConsoleLiveUpdate(bool enabled)
    {
        if (!enabled)
        {
            _simConsoleLiveTimer?.Stop();
            return;
        }

        _simConsoleLiveTimer ??= new System.Windows.Forms.Timer { Interval = 150 };
        _simConsoleLiveTimer.Tick -= SimConsoleLiveTimer_Tick;
        _simConsoleLiveTimer.Tick += SimConsoleLiveTimer_Tick;
        _simConsoleLiveTimer.Start();
    }

    private void SysConsoleLiveTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_chkAutoScrollSys is not null && _chkAutoScrollSys.Checked)
                ScrollToBottom(_txtSysConsole);
        }
        catch
        {
            _sysConsoleLiveTimer?.Stop();
        }
    }

    private void SimConsoleLiveTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            DrainSimConsoleSources();

            if (_chkAutoScrollSim is not null && _chkAutoScrollSim.Checked)
                ScrollToBottom(_txtSimConsole);
        }
        catch
        {
            _simConsoleLiveTimer?.Stop();
        }
    }

    private void DrainSimConsoleSources()
    {
        foreach (string line in LogStatistics.FetchCpuLogs())
            AppendSimLog(line);

        foreach (string line in LogStatistics.FetchGpuLogs())
            AppendSimLog(line);
    }

    // ============================================================
    // APPEND METHODS
    // ============================================================

    private void AppendSysConsole(string text)
    {
        if (IsDisposed || _txtSysConsole is null || _txtSysConsole.IsDisposed)
            return;

        if (_sysConsoleBuffer is not null)
            _sysConsoleBuffer.Append(text);
        else
            _txtSysConsole.AppendText(text);

        if (_chkAutoScrollSys is not null && _chkAutoScrollSys.Checked)
            ScrollToBottom(_txtSysConsole);
    }

    private void AppendSimConsole(string text)
    {
        if (IsDisposed || _txtSimConsole is null || _txtSimConsole.IsDisposed)
            return;

        if (_simConsoleBuffer is not null)
            _simConsoleBuffer.Append(text);
        else
            _txtSimConsole.AppendText(text);

        if (_chkAutoScrollSim is not null && _chkAutoScrollSim.Checked)
            ScrollToBottom(_txtSimConsole);
    }

    private void AppendGPUConsole(string text)
    {
        if (_txtGpuConsole is null || _txtGpuConsole.IsDisposed) return;

        if (_txtGpuConsole.InvokeRequired)
        {
            _txtGpuConsole.BeginInvoke(new Action(() => AppendGPUConsole(text)));
            return;
        }

        if (_txtGpuConsole.TextLength > 50000)
        {
            _txtGpuConsole.Clear();
            _txtGpuConsole.AppendText("[Console cleared due to size limit]\n");
        }
        _txtGpuConsole.AppendText(text);
    }

    private void AppendSysLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var (category, normalized) = ClassifySysMessage(message);

        lock (_sysConsoleLines)
        {
            _sysConsoleLines.Add(new ConsoleLine(DateTime.UtcNow, category, normalized));
            if (_sysConsoleLines.Count > MaxConsoleLines)
                _sysConsoleLines.RemoveRange(0, _sysConsoleLines.Count - MaxConsoleLines);
        }

        if (!PassSysFilter(category)) return;
        AppendSysConsole(normalized);
    }

    private void AppendSimLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var (category, normalized) = ClassifySimMessage(message);

        lock (_simConsoleLines)
        {
            _simConsoleLines.Add(new ConsoleLine(DateTime.UtcNow, category, normalized));
            if (_simConsoleLines.Count > MaxConsoleLines)
                _simConsoleLines.RemoveRange(0, _simConsoleLines.Count - MaxConsoleLines);
        }

        if (!PassSimFilter(category)) return;
        AppendSimConsole(normalized);
    }

    // ============================================================
    // REFRESH / FILTER
    // ============================================================

    private void RefreshSysConsole()
    {
        if (IsDisposed || _txtSysConsole is null || _txtSysConsole.IsDisposed) return;

        _txtSysConsole.Clear();

        List<ConsoleLine> snapshot;
        lock (_sysConsoleLines)
        {
            snapshot = new List<ConsoleLine>(_sysConsoleLines);
        }

        foreach (ConsoleLine line in snapshot)
        {
            if (PassSysFilter(line.Category))
                _txtSysConsole.AppendText(line.Message);
        }

        if (_chkAutoScrollSys is not null && _chkAutoScrollSys.Checked)
            ScrollToBottom(_txtSysConsole);
    }

    private void RefreshSimConsole()
    {
        if (IsDisposed || _txtSimConsole is null || _txtSimConsole.IsDisposed) return;

        _txtSimConsole.Clear();

        List<ConsoleLine> snapshot;
        lock (_simConsoleLines)
        {
            snapshot = new List<ConsoleLine>(_simConsoleLines);
        }

        foreach (ConsoleLine line in snapshot)
        {
            if (PassSimFilter(line.Category))
                _txtSimConsole.AppendText(line.Message);
        }

        if (_chkAutoScrollSim is not null && _chkAutoScrollSim.Checked)
            ScrollToBottom(_txtSimConsole);
    }

    // ============================================================
    // CLASSIFICATION / FILTERING
    // ============================================================

    private static (string Category, string Normalized) ClassifySysMessage(string message)
    {
        string m = message.EndsWith('\n') ? message : message + "\n";

        if (m.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) || m.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            return (nameof(SysConsoleOutType.Error), PrefixIfMissing(nameof(SysConsoleOutType.Error), m));

        if (m.Contains("[WARN]", StringComparison.OrdinalIgnoreCase) || m.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
            return (nameof(SysConsoleOutType.Warning), PrefixIfMissing(nameof(SysConsoleOutType.Warning), m));

        if (m.StartsWith("[Dispatcher]", StringComparison.OrdinalIgnoreCase) || m.Contains("Dispatcher", StringComparison.OrdinalIgnoreCase))
            return (nameof(SysConsoleOutType.Dispatcher), PrefixIfMissing(nameof(SysConsoleOutType.Dispatcher), m));

        if (m.Contains("GPU", StringComparison.OrdinalIgnoreCase))
            return (nameof(SysConsoleOutType.GPU), PrefixIfMissing(nameof(SysConsoleOutType.GPU), m));

        if (m.Contains("[IO]", StringComparison.OrdinalIgnoreCase) || m.Contains("File", StringComparison.OrdinalIgnoreCase) || m.Contains("Path", StringComparison.OrdinalIgnoreCase))
            return (nameof(SysConsoleOutType.IO), PrefixIfMissing(nameof(SysConsoleOutType.IO), m));

        return (nameof(SysConsoleOutType.Info), PrefixIfMissing(nameof(SysConsoleOutType.Info), m));
    }

    private static (string Category, string Normalized) ClassifySimMessage(string message)
    {
        string m = message.EndsWith('\n') ? message : message + "\n";

        if (m.Contains("[Pipeline ERROR]", StringComparison.OrdinalIgnoreCase) || m.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase))
            return (nameof(SimConsoleOutType.Error), PrefixIfMissing(nameof(SimConsoleOutType.Error), m));

        if (m.Contains("[Pipeline]", StringComparison.OrdinalIgnoreCase))
            return (nameof(SimConsoleOutType.Pipeline), PrefixIfMissing(nameof(SimConsoleOutType.Pipeline), m));

        if (m.Contains("Physics", StringComparison.OrdinalIgnoreCase) || m.Contains("Module", StringComparison.OrdinalIgnoreCase))
            return (nameof(SimConsoleOutType.Physics), PrefixIfMissing(nameof(SimConsoleOutType.Physics), m));

        if (m.Contains("Metric", StringComparison.OrdinalIgnoreCase) || m.Contains("Spectral", StringComparison.OrdinalIgnoreCase))
            return (nameof(SimConsoleOutType.Metrics), PrefixIfMissing(nameof(SimConsoleOutType.Metrics), m));

        if (m.Contains("WARNING", StringComparison.OrdinalIgnoreCase) || m.Contains("[WARN]", StringComparison.OrdinalIgnoreCase))
            return (nameof(SimConsoleOutType.Warning), PrefixIfMissing(nameof(SimConsoleOutType.Warning), m));

        return (nameof(SimConsoleOutType.Info), PrefixIfMissing(nameof(SimConsoleOutType.Info), m));
    }

    private bool PassSysFilter(string category)
    {
        if (_sysConsoleOutType == SysConsoleOutType.All) return true;
        return category == _sysConsoleOutType.ToString();
    }

    private bool PassSimFilter(string category)
    {
        if (_simConsoleOutType == SimConsoleOutType.All) return true;
        return category == _simConsoleOutType.ToString();
    }

    private static string PrefixIfMissing(string category, string message)
    {
        string prefix = $"[{category}] ";
        string withPrefix = message.StartsWith('[') ? message : prefix + message;

        withPrefix = withPrefix.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!withPrefix.EndsWith('\n'))
            withPrefix += "\n";
        if (!withPrefix.EndsWith("\n\n", StringComparison.Ordinal))
            withPrefix += "\n";

        return withPrefix;
    }

    private static void ScrollToBottom(TextBox? textBox)
    {
        if (textBox is null || textBox.IsDisposed) return;

        if (textBox.InvokeRequired)
        {
            if (!textBox.IsHandleCreated) return;
            textBox.BeginInvoke(new Action(() => ScrollToBottom(textBox)));
            return;
        }

        textBox.SelectionStart = textBox.Text.Length;
        textBox.ScrollToCaret();
    }
}
