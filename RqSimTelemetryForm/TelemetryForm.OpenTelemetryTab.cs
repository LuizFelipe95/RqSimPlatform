using RqSimForms.ProcessesDispatcher.Contracts;

namespace RqSimTelemetryForm;

/// <summary>
/// OpenTelemetry Viewer tab: live-updating tables showing metrics from MeterListener
/// and per-module performance statistics.
/// </summary>
public partial class TelemetryForm
{
    // ============================================================
    // OTEL VIEWER CONTROLS
    // ============================================================

    private ListView _lvOTelMetrics = null!;
    private ListView _lvOTelModules = null!;
    private Label _lblOTelStatus = null!;

    // ============================================================
    // INITIALIZATION
    // ============================================================

    private void InitializeOpenTelemetryTab()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(5)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));

        // Row 0: Status
        _lblOTelStatus = new Label
        {
            Text = "OpenTelemetry Metrics",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            Margin = new Padding(3, 3, 3, 6)
        };
        mainLayout.Controls.Add(_lblOTelStatus, 0, 0);

        // Row 1: Metrics ListView
        var grpMetrics = new GroupBox
        {
            Text = "Metrics",
            Dock = DockStyle.Fill,
            Padding = new Padding(3)
        };

        _lvOTelMetrics = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font("Consolas", 9F),
            AccessibleName = "OpenTelemetry Metrics Table",
            AccessibleDescription = "Live table of RqSimPlatform metrics"
        };
        _lvOTelMetrics.Columns.Add("Metric", 250);
        _lvOTelMetrics.Columns.Add("Value", 120);
        _lvOTelMetrics.Columns.Add("Unit", 100);
        _lvOTelMetrics.Columns.Add("Updated", 120);
        AttachCopySupport(_lvOTelMetrics);

        grpMetrics.Controls.Add(_lvOTelMetrics);
        mainLayout.Controls.Add(grpMetrics, 0, 1);

        // Row 2: Module header
        var lblModules = new Label
        {
            Text = "Module Performance",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            Margin = new Padding(3, 6, 3, 3)
        };
        mainLayout.Controls.Add(lblModules, 0, 2);

        // Row 3: Module performance ListView
        var grpModules = new GroupBox
        {
            Text = "Module Execution Statistics",
            Dock = DockStyle.Fill,
            Padding = new Padding(3)
        };

        _lvOTelModules = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font("Consolas", 9F),
            AccessibleName = "Module Performance Table",
            AccessibleDescription = "Per-module execution time and error counts"
        };
        _lvOTelModules.Columns.Add("Module", 200);
        _lvOTelModules.Columns.Add("Avg (ms)", 100);
        _lvOTelModules.Columns.Add("Count", 100);
        _lvOTelModules.Columns.Add("Errors", 80);
        AttachCopySupport(_lvOTelModules);

        grpModules.Controls.Add(_lvOTelModules);
        mainLayout.Controls.Add(grpModules, 0, 3);

        _tabOTelViewer.Controls.Add(mainLayout);
    }

    // ============================================================
    // UI REFRESH (called from timer tick on UI thread)
    // ============================================================

    /// <summary>
    /// Refreshes the OpenTelemetry viewer tab from the MeterListener's snapshot data.
    /// </summary>
    private void RefreshOpenTelemetryTab()
    {
        if (_tabControl.SelectedTab != _tabOTelViewer)
            return;

        // Update status text based on mode
        if (_isExternalSimulation)
        {
            int count = _metricSnapshots.Count;
            _lblOTelStatus.Text = count > 0
                ? $"OpenTelemetry Metrics \u2014 Console mode (IPC): {count} metrics from SharedMemory"
                : "OpenTelemetry Metrics \u2014 Console mode (IPC): waiting for data...";
        }
        else if (_metricSnapshots.IsEmpty)
        {
            _lblOTelStatus.Text = "OpenTelemetry Metrics \u2014 Listening (no data yet)";
        }
        else
        {
            _lblOTelStatus.Text = $"OpenTelemetry Metrics \u2014 {_metricSnapshots.Count} instruments active";
        }

        RefreshOTelMetricsListView();
        RefreshOTelModulesListView();
    }

    /// <summary>
    /// Populates <see cref="_metricSnapshots"/> from IPC SimState data so the
    /// existing OTel ListView renders metrics even in console mode.
    /// Called from <see cref="HandleExternalSimulationTick"/> on each timer tick.
    /// </summary>
    private void PopulateIpcMetrics(SimState s)
    {
        DateTime now = DateTime.UtcNow;
        KeyValuePair<string, object?>[] empty = [];

        RecordIpcMetric("rqsim.graph.nodes", s.NodeCount, "nodes", now, empty);
        RecordIpcMetric("rqsim.graph.edges", s.EdgeCount, "edges", now, empty);
        RecordIpcMetric("rqsim.graph.excited", s.ExcitedCount, "nodes", now, empty);
        RecordIpcMetric("rqsim.physics.energy", s.SystemEnergy, "", now, empty);
        RecordIpcMetric("rqsim.physics.heavy_mass", s.HeavyMass, "", now, empty);
        RecordIpcMetric("rqsim.physics.largest_cluster", s.LargestCluster, "nodes", now, empty);
        RecordIpcMetric("rqsim.physics.strong_edges", s.StrongEdgeCount, "edges", now, empty);
        RecordIpcMetric("rqsim.physics.spectral_dimension", s.SpectralDimension, "", now, empty);
        RecordIpcMetric("rqsim.physics.network_temp", s.NetworkTemperature, "", now, empty);
        RecordIpcMetric("rqsim.physics.effective_g", s.EffectiveG, "", now, empty);
        RecordIpcMetric("rqsim.physics.q_norm", s.QNorm, "", now, empty);
        RecordIpcMetric("rqsim.physics.entanglement", s.Entanglement, "", now, empty);
        RecordIpcMetric("rqsim.physics.correlation", s.Correlation, "", now, empty);
        RecordIpcMetric("rqsim.simulation.iteration", s.Iteration, "steps", now, empty);
    }

    // ============================================================
    // MODULE STATS FROM SHARED MEMORY (Console Pipeline)
    // ============================================================

    /// <summary>
    /// Known pipeline module names indexed by FNV-1a hash.
    /// Must match the module Name properties in BuiltInModules.cs
    /// and the Fnv1aHash algorithm in ServerModeHost.PublishLoop.cs.
    /// </summary>
    private static readonly Dictionary<int, string> s_moduleNamesByHash = BuildModuleNameHashMap();

    private static Dictionary<int, string> BuildModuleNameHashMap()
    {
        string[] moduleNames =
        [
            "Spacetime Physics",
            "Spinor Field",
            "Vacuum Fluctuations",
            "Black Hole Physics",
            "Yang-Mills Gauge",
            "Klein-Gordon Field",
            "Internal Time",
            "Relational Time",
            "Spectral Geometry",
            "Quantum Graphity",
            "Asynchronous Time",
            "Mexican Hat Potential",
            "Geometry Momenta",
            "Unified Physics Step"
        ];

        Dictionary<int, string> map = new(moduleNames.Length);
        foreach (string name in moduleNames)
        {
            map[Fnv1aHash(name)] = name;
        }
        return map;
    }

    /// <summary>
    /// FNV-1a hash matching the console-side algorithm in ServerModeHost.PublishLoop.cs.
    /// </summary>
    private static int Fnv1aHash(string text)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return (int)hash;
        }
    }

    /// <summary>
    /// Reads pipeline module stats from SharedMemory and populates
    /// <see cref="_modulePerformance"/> so the Module Performance ListView
    /// shows real per-module timing data in console mode.
    /// </summary>
    private void PopulateModuleStatsFromIpc()
    {
        ModuleStatsEntry[] entries = _lifeCycleManager.GetExternalModuleStats();
        if (entries.Length == 0)
            return;

        foreach (ModuleStatsEntry entry in entries)
        {
            string moduleName = s_moduleNamesByHash.TryGetValue(entry.NameHash, out string? name)
                ? name
                : $"Module#{entry.NameHash:X8}";

            _modulePerformance[moduleName] = (entry.AvgMs * entry.Count, entry.Count, entry.Errors);
        }
    }

    private void RecordIpcMetric(string name, double value, string unit,
        DateTime timestampUtc, IReadOnlyList<KeyValuePair<string, object?>> tags)
    {
        _metricSnapshots[name] = new MetricSnapshot(name, value, unit, timestampUtc, tags);
    }

    private void RefreshOTelMetricsListView()
    {
        if (_lvOTelMetrics is null || _lvOTelMetrics.IsDisposed)
            return;

        DateTime now = DateTime.UtcNow;

        _lvOTelMetrics.BeginUpdate();
        try
        {
            // Build lookup of existing items by metric key
            Dictionary<string, ListViewItem> existing = new(_lvOTelMetrics.Items.Count);
            foreach (ListViewItem item in _lvOTelMetrics.Items)
            {
                if (item.Tag is string key)
                    existing[key] = item;
            }

            foreach (var kvp in _metricSnapshots)
            {
                MetricSnapshot snap = kvp.Value;
                string elapsed = FormatElapsed(now - snap.TimestampUtc);
                string valueText = FormatMetricValue(snap.Value);

                if (existing.TryGetValue(kvp.Key, out ListViewItem? item))
                {
                    item.SubItems[1].Text = valueText;
                    item.SubItems[2].Text = snap.Unit;
                    item.SubItems[3].Text = elapsed;
                }
                else
                {
                    var newItem = new ListViewItem(snap.Name);
                    newItem.SubItems.Add(valueText);
                    newItem.SubItems.Add(snap.Unit);
                    newItem.SubItems.Add(elapsed);
                    newItem.Tag = kvp.Key;
                    _lvOTelMetrics.Items.Add(newItem);
                }
            }
        }
        finally
        {
            _lvOTelMetrics.EndUpdate();
        }
    }

    private void RefreshOTelModulesListView()
    {
        if (_lvOTelModules is null || _lvOTelModules.IsDisposed)
            return;

        // Console mode runs GPU physics directly — no pipeline modules
        if (_isExternalSimulation && _modulePerformance.IsEmpty)
        {
            if (_lvOTelModules.Items.Count == 0)
            {
                var note = new ListViewItem("(Console mode — GPU physics, no pipeline modules)");
                note.ForeColor = Color.Gray;
                note.SubItems.Add("—");
                note.SubItems.Add("—");
                note.SubItems.Add("—");
                _lvOTelModules.Items.Add(note);
            }
            return;
        }

        _lvOTelModules.BeginUpdate();
        try
        {
            Dictionary<string, ListViewItem> existing = new(_lvOTelModules.Items.Count);
            foreach (ListViewItem item in _lvOTelModules.Items)
            {
                if (item.Tag is string key)
                    existing[key] = item;
            }

            foreach (var kvp in _modulePerformance)
            {
                string moduleName = kvp.Key;
                (double totalMs, long count, long errors) = kvp.Value;
                double avgMs = count > 0 ? totalMs / count : 0;

                if (existing.TryGetValue(moduleName, out ListViewItem? item))
                {
                    item.SubItems[1].Text = avgMs.ToString("F2");
                    item.SubItems[2].Text = count.ToString();
                    item.SubItems[3].Text = errors.ToString();
                    item.BackColor = errors > 0 ? Color.MistyRose : SystemColors.Window;
                }
                else
                {
                    var newItem = new ListViewItem(moduleName);
                    newItem.SubItems.Add(avgMs.ToString("F2"));
                    newItem.SubItems.Add(count.ToString());
                    newItem.SubItems.Add(errors.ToString());
                    newItem.Tag = moduleName;
                    newItem.BackColor = errors > 0 ? Color.MistyRose : SystemColors.Window;
                    _lvOTelModules.Items.Add(newItem);
                }
            }
        }
        finally
        {
            _lvOTelModules.EndUpdate();
        }
    }

    // ============================================================
    // FORMAT HELPERS
    // ============================================================

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
            return $"{elapsed.TotalMilliseconds:F0}ms ago";
        if (elapsed.TotalSeconds < 60)
            return $"{elapsed.TotalSeconds:F1}s ago";
        if (elapsed.TotalMinutes < 60)
            return $"{elapsed.TotalMinutes:F0}m ago";

        return $"{elapsed.TotalHours:F0}h ago";
    }

    private static string FormatMetricValue(double value)
    {
        if (Math.Abs(value) < 0.01 && value != 0)
            return value.ToString("E3");
        if (Math.Abs(value) > 9999)
            return value.ToString("N0");

        return value.ToString("F4");
    }

    // ============================================================
    // COPY SUPPORT
    // ============================================================

    /// <summary>
    /// Attaches Ctrl+C keyboard shortcut and a context menu with Copy/Copy All
    /// to a ListView so the user can select and copy rows to clipboard.
    /// </summary>
    private static void AttachCopySupport(ListView listView)
    {
        listView.KeyDown += (sender, e) =>
        {
            if (e is { Control: true, KeyCode: Keys.C })
            {
                CopyListViewToClipboard((ListView)sender!, selectedOnly: true);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e is { Control: true, KeyCode: Keys.A })
            {
                // Select all items
                foreach (ListViewItem item in ((ListView)sender!).Items)
                    item.Selected = true;

                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        var ctxMenu = new ContextMenuStrip();

        var miCopySelected = new ToolStripMenuItem("Copy Selected\tCtrl+C");
        miCopySelected.Click += (_, _) => CopyListViewToClipboard(listView, selectedOnly: true);
        ctxMenu.Items.Add(miCopySelected);

        var miCopyAll = new ToolStripMenuItem("Copy All");
        miCopyAll.Click += (_, _) => CopyListViewToClipboard(listView, selectedOnly: false);
        ctxMenu.Items.Add(miCopyAll);

        listView.ContextMenuStrip = ctxMenu;
    }

    /// <summary>
    /// Copies ListView rows (selected or all) to clipboard as tab-separated text.
    /// </summary>
    private static void CopyListViewToClipboard(ListView listView, bool selectedOnly)
    {
        var items = selectedOnly
            ? listView.SelectedItems.Cast<ListViewItem>()
            : listView.Items.Cast<ListViewItem>();

        var sb = new System.Text.StringBuilder();

        // Header row
        foreach (ColumnHeader col in listView.Columns)
        {
            if (sb.Length > 0) sb.Append('\t');
            sb.Append(col.Text);
        }
        sb.AppendLine();

        // Data rows
        foreach (ListViewItem item in items)
        {
            for (int i = 0; i < item.SubItems.Count; i++)
            {
                if (i > 0) sb.Append('\t');
                sb.Append(item.SubItems[i].Text);
            }
            sb.AppendLine();
        }

        if (sb.Length > 0)
        {
            Clipboard.SetText(sb.ToString());
        }
    }
}
