using System.Text.Json;
using RqSimGraphEngine.Experiments;

namespace RqSimTelemetryForm;

/// <summary>
/// Experiments tab: experiment selection, expected results configuration,
/// validation, load/save JSON, and apply-to-simulation flow.
/// </summary>
public partial class TelemetryForm
{
    // ============================================================
    // EXPERIMENTS TAB CONTROLS
    // ============================================================

    private TableLayoutPanel _expMainLayout = null!;

    // GroupBox 1: Experiment Selection & Info
    private GroupBox _grpExpSelection = null!;
    private ComboBox _cmbExpSelect = null!;
    private TextBox _txtExpDescription = null!;
    private TextBox _txtExpNotes = null!;
    private Button _btnExpNew = null!;
    private Button _btnExpLoad = null!;
    private Button _btnExpSave = null!;

    // GroupBox 2: Expected Results
    private GroupBox _grpExpExpected = null!;
    private TableLayoutPanel _tlpExpExpected = null!;
    private NumericUpDown _numExpSpectralDimTarget = null!;
    private NumericUpDown _numExpSpectralDimMin = null!;
    private NumericUpDown _numExpSpectralDimMax = null!;
    private NumericUpDown _numExpHeavyClusterMin = null!;
    private NumericUpDown _numExpHeavyClusterMax = null!;
    private NumericUpDown _numExpHeavyMassMin = null!;
    private NumericUpDown _numExpHeavyMassMax = null!;
    private NumericUpDown _numExpLargestClusterMax = null!;
    private NumericUpDown _numExpFinalTempMax = null!;
    private CheckBox _chkExpPhaseTransition = null!;
    private CheckBox _chkExpStabilization = null!;

    // GroupBox 3: Validation Summary
    private GroupBox _grpExpValidation = null!;
    private Label _lblExpValidationStatus = null!;
    private ListView _lvExpCriteria = null!;
    private TextBox _txtExpReport = null!;
    private Button _btnExpValidate = null!;
    private Button _btnExpApplyToSim = null!;

    // Current experiment state
    private ExperimentDefinition? _currentExperiment;

    // ============================================================
    // INITIALIZATION
    // ============================================================

    private void InitializeExperimentsTab()
    {
        _expMainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(5)
        };
        _expMainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        _expMainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        _expMainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        _expMainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        InitializeExpSelectionGroup();
        InitializeExpExpectedGroup();
        InitializeExpValidationGroup();

        _expMainLayout.Controls.Add(_grpExpSelection, 0, 0);
        _expMainLayout.Controls.Add(_grpExpExpected, 1, 0);
        _expMainLayout.Controls.Add(_grpExpValidation, 2, 0);

        _tabExperiments.Controls.Add(_expMainLayout);

        // Initialize scaling visualization below
        InitializeScalingVisualization(_tabExperiments);

        LoadBuiltInExperiments();
    }

    private void InitializeExpSelectionGroup()
    {
        _grpExpSelection = new GroupBox
        {
            Text = "Experiment Selection",
            Dock = DockStyle.Fill,
            Padding = new Padding(5)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Row 0: Selector
        var lblSelect = new Label { Text = "Experiment:", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _cmbExpSelect = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbExpSelect.SelectedIndexChanged += CmbExpSelect_SelectedIndexChanged;
        layout.Controls.Add(lblSelect, 0, 0);
        layout.Controls.Add(_cmbExpSelect, 1, 0);

        // Row 1: Buttons
        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        _btnExpNew = new Button { Text = "New", Width = 60 };
        _btnExpNew.Click += BtnExpNew_Click;
        _btnExpLoad = new Button { Text = "Load JSON", Width = 80 };
        _btnExpLoad.Click += BtnExpLoad_Click;
        _btnExpSave = new Button { Text = "Save JSON", Width = 80 };
        _btnExpSave.Click += BtnExpSave_Click;
        btnPanel.Controls.AddRange(new Control[] { _btnExpNew, _btnExpLoad, _btnExpSave });
        layout.Controls.Add(btnPanel, 0, 1);
        layout.SetColumnSpan(btnPanel, 2);

        // Row 2-3: Description
        var lblDesc = new Label { Text = "Description:", AutoSize = true };
        layout.Controls.Add(lblDesc, 0, 2);
        layout.SetColumnSpan(lblDesc, 2);

        _txtExpDescription = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Info
        };
        layout.Controls.Add(_txtExpDescription, 0, 3);
        layout.SetColumnSpan(_txtExpDescription, 2);

        // Row 4-5: Notes
        var lblNotes = new Label { Text = "Notes (editable):", AutoSize = true };
        layout.Controls.Add(lblNotes, 0, 4);
        layout.SetColumnSpan(lblNotes, 2);

        _txtExpNotes = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };
        layout.Controls.Add(_txtExpNotes, 0, 5);
        layout.SetColumnSpan(_txtExpNotes, 2);

        // Row 6: Apply to Simulation button
        _btnExpApplyToSim = new Button
        {
            Text = "Apply to Simulation \u2192",
            Dock = DockStyle.Fill,
            Height = 30,
            Font = new Font(Font, FontStyle.Bold),
            Enabled = _hasApiConnection
        };
        if (!_hasApiConnection)
        {
            _btnExpApplyToSim.Text = "Apply to Simulation (connect via RqSimUI)";
        }
        _btnExpApplyToSim.Click += BtnExpApplyToSim_Click;
        layout.Controls.Add(_btnExpApplyToSim, 0, 6);
        layout.SetColumnSpan(_btnExpApplyToSim, 2);

        _grpExpSelection.Controls.Add(layout);
    }

    private void InitializeExpExpectedGroup()
    {
        _grpExpExpected = new GroupBox
        {
            Text = "Expected Results (Predictions)",
            Dock = DockStyle.Fill,
            Padding = new Padding(5)
        };

        _tlpExpExpected = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 12,
            AutoScroll = true
        };
        _tlpExpExpected.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        _tlpExpExpected.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));

        for (int i = 0; i < 12; i++)
            _tlpExpExpected.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        int row = 0;
        AddExpectedRow("d_S Target:", ref _numExpSpectralDimTarget, 4.0, 0.0, 10.0, 1, row++);
        AddExpectedRow("d_S Min:", ref _numExpSpectralDimMin, 2.0, 0.0, 10.0, 1, row++);
        AddExpectedRow("d_S Max:", ref _numExpSpectralDimMax, 6.0, 0.0, 10.0, 1, row++);
        AddExpectedRowInt("Heavy Cluster Min:", ref _numExpHeavyClusterMin, 1, 0, 1000, row++);
        AddExpectedRowInt("Heavy Cluster Max:", ref _numExpHeavyClusterMax, 50, 0, 1000, row++);
        AddExpectedRow("Heavy Mass Min:", ref _numExpHeavyMassMin, 0.0, 0.0, 1000.0, 1, row++);
        AddExpectedRow("Heavy Mass Max:", ref _numExpHeavyMassMax, 100.0, 0.0, 1000.0, 1, row++);
        AddExpectedRow("Largest Cluster Max (%):", ref _numExpLargestClusterMax, 30.0, 0.0, 100.0, 0, row++);
        AddExpectedRow("Final Temp Max:", ref _numExpFinalTempMax, 1.0, 0.0, 100.0, 2, row++);

        _chkExpPhaseTransition = new CheckBox { Text = "Expect Phase Transition", Checked = true, AutoSize = true };
        _tlpExpExpected.Controls.Add(_chkExpPhaseTransition, 0, row);
        _tlpExpExpected.SetColumnSpan(_chkExpPhaseTransition, 2);
        row++;

        _chkExpStabilization = new CheckBox { Text = "Expect Stabilization", Checked = true, AutoSize = true };
        _tlpExpExpected.Controls.Add(_chkExpStabilization, 0, row);
        _tlpExpExpected.SetColumnSpan(_chkExpStabilization, 2);

        _grpExpExpected.Controls.Add(_tlpExpExpected);
    }

    private void AddExpectedRow(string label, ref NumericUpDown num, double value, double min, double max, int decimals, int row)
    {
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        num = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            DecimalPlaces = decimals,
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Value = (decimal)value,
            Increment = (decimal)Math.Pow(10, -decimals)
        };
        _tlpExpExpected.Controls.Add(lbl, 0, row);
        _tlpExpExpected.Controls.Add(num, 1, row);
    }

    private void AddExpectedRowInt(string label, ref NumericUpDown num, int value, int min, int max, int row)
    {
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        num = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            DecimalPlaces = 0,
            Minimum = min,
            Maximum = max,
            Value = value
        };
        _tlpExpExpected.Controls.Add(lbl, 0, row);
        _tlpExpExpected.Controls.Add(num, 1, row);
    }

    private void InitializeExpValidationGroup()
    {
        _grpExpValidation = new GroupBox
        {
            Text = "Validation Summary",
            Dock = DockStyle.Fill,
            Padding = new Padding(5)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        _lblExpValidationStatus = new Label
        {
            Text = "Not validated yet",
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Height = 40,
            BackColor = SystemColors.ControlLight
        };
        layout.Controls.Add(_lblExpValidationStatus, 0, 0);

        _btnExpValidate = new Button
        {
            Text = "Validate Results",
            Dock = DockStyle.Fill,
            Height = 30
        };
        _btnExpValidate.Click += BtnExpValidate_Click;
        layout.Controls.Add(_btnExpValidate, 0, 1);

        _lvExpCriteria = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        _lvExpCriteria.Columns.Add("Criterion", 150);
        _lvExpCriteria.Columns.Add("Status", 60);
        _lvExpCriteria.Columns.Add("Expected", 100);
        _lvExpCriteria.Columns.Add("Actual", 100);
        layout.Controls.Add(_lvExpCriteria, 0, 2);

        _txtExpReport = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 9F),
            WordWrap = false
        };
        layout.Controls.Add(_txtExpReport, 0, 3);

        _grpExpValidation.Controls.Add(layout);
    }

    private void LoadBuiltInExperiments()
    {
        _cmbExpSelect.Items.Clear();
        _cmbExpSelect.Items.Add("(New Custom Experiment)");

        foreach (IExperiment exp in ExperimentFactory.AvailableExperiments)
        {
            _cmbExpSelect.Items.Add(exp.Name);
        }

        _cmbExpSelect.SelectedIndex = 0;
    }

    // ============================================================
    // EVENT HANDLERS
    // ============================================================

    private void CmbExpSelect_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_cmbExpSelect.SelectedIndex <= 0)
        {
            _currentExperiment = new ExperimentDefinition
            {
                Name = "Custom Experiment",
                Description = "Create a new custom experiment with your own parameters and expected results."
            };
            LoadExperimentToUI(_currentExperiment);
            return;
        }

        string expName = _cmbExpSelect.SelectedItem?.ToString() ?? "";
        IExperiment? builtIn = ExperimentFactory.GetByName(expName);

        if (builtIn is not null)
        {
            _currentExperiment = CreateDefinitionFromBuiltIn(builtIn);
            LoadExperimentToUI(_currentExperiment);
        }
    }

    private void BtnExpNew_Click(object? sender, EventArgs e)
    {
        _currentExperiment = new ExperimentDefinition
        {
            Name = $"Experiment_{DateTime.Now:yyyyMMdd_HHmmss}",
            Description = "New custom experiment"
        };

        _cmbExpSelect.SelectedIndex = 0;
        LoadExperimentToUI(_currentExperiment);
        AppendSysConsole("[Experiment] New experiment created\n");
    }

    private void BtnExpLoad_Click(object? sender, EventArgs e)
    {
        using OpenFileDialog dlg = new()
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Load Experiment Definition"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            string json = File.ReadAllText(dlg.FileName);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _currentExperiment = JsonSerializer.Deserialize<ExperimentDefinition>(json, options);

            if (_currentExperiment is not null)
            {
                LoadExperimentToUI(_currentExperiment);
                _cmbExpSelect.SelectedIndex = 0;
                AppendSysConsole($"[Experiment] Loaded: {_currentExperiment.Name}\n");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading experiment: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnExpSave_Click(object? sender, EventArgs e)
    {
        if (_currentExperiment is null)
        {
            MessageBox.Show("No experiment to save.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        UpdateExperimentFromUI();

        using SaveFileDialog dlg = new()
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = $"experiment-{_currentExperiment.Name.Replace(" ", "-").ToLowerInvariant()}-{DateTime.Now:yyyyMMdd}.json",
            Title = "Save Experiment Definition"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_currentExperiment, options);
            File.WriteAllText(dlg.FileName, json, System.Text.Encoding.UTF8);
            AppendSysConsole($"[Experiment] Saved: {dlg.FileName}\n");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving experiment: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnExpValidate_Click(object? sender, EventArgs e)
    {
        if (_currentExperiment is null)
        {
            MessageBox.Show("No experiment selected.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        UpdateExperimentFromUI();

        ActualResults actual = CollectActualResultsFromApi();
        _currentExperiment.Actual = actual;
        _currentExperiment.LastRunAt = DateTime.UtcNow;

        ValidationSummary validation = ExperimentValidator.Validate(_currentExperiment.Expected, actual);
        _currentExperiment.Validation = validation;

        UpdateValidationUI(validation);
        _txtExpReport.Text = ExperimentValidator.GenerateReport(_currentExperiment);

        AppendSysConsole($"[Experiment] Validation: {validation.Summary}\n");
    }

    // ============================================================
    // HELPER METHODS
    // ============================================================

    private static ExperimentDefinition CreateDefinitionFromBuiltIn(IExperiment builtIn)
    {
        var config = builtIn.GetConfig();

        return new ExperimentDefinition
        {
            Name = builtIn.Name,
            Description = builtIn.Description,
            Config = ExperimentConfig.FromStartupConfig(config),
            Expected = GetDefaultExpectedForExperiment(builtIn.Name),
            Notes = $"Built-in experiment: {builtIn.Name}"
        };
    }

    private static ExpectedResults GetDefaultExpectedForExperiment(string name)
    {
        return name switch
        {
            "Vacuum Genesis" => new ExpectedResults
            {
                SpectralDimensionTarget = 4.0,
                SpectralDimensionMin = 3.0,
                SpectralDimensionMax = 5.0,
                HeavyClusterCountMin = 0,
                HeavyClusterCountMax = 10,
                LargestClusterFractionMax = 0.20,
                ExpectPhaseTransition = true,
                ExpectStabilization = true
            },
            "Mass Nucleation" => new ExpectedResults
            {
                SpectralDimensionTarget = 3.5,
                SpectralDimensionMin = 2.5,
                SpectralDimensionMax = 4.5,
                HeavyClusterCountMin = 5,
                HeavyClusterCountMax = 50,
                HeavyMassMin = 10.0,
                HeavyMassMax = 200.0,
                LargestClusterFractionMax = 0.25,
                ExpectPhaseTransition = true,
                ExpectStabilization = true
            },
            "Bio-Folding (DNA Hairpin)" => new ExpectedResults
            {
                SpectralDimensionTarget = 2.5,
                SpectralDimensionMin = 1.5,
                SpectralDimensionMax = 3.5,
                HeavyClusterCountMin = 1,
                HeavyClusterCountMax = 3,
                LargestClusterFractionMax = 0.50,
                ExpectPhaseTransition = true,
                ExpectStabilization = true
            },
            _ => new ExpectedResults()
        };
    }

    private void LoadExperimentToUI(ExperimentDefinition exp)
    {
        _txtExpDescription.Text = exp.Description;
        _txtExpNotes.Text = exp.Notes;

        SetNumericValueSafe(_numExpSpectralDimTarget, (decimal)exp.Expected.SpectralDimensionTarget);
        SetNumericValueSafe(_numExpSpectralDimMin, (decimal)exp.Expected.SpectralDimensionMin);
        SetNumericValueSafe(_numExpSpectralDimMax, (decimal)exp.Expected.SpectralDimensionMax);
        SetNumericValueSafe(_numExpHeavyClusterMin, exp.Expected.HeavyClusterCountMin);
        SetNumericValueSafe(_numExpHeavyClusterMax, exp.Expected.HeavyClusterCountMax);
        SetNumericValueSafe(_numExpHeavyMassMin, (decimal)exp.Expected.HeavyMassMin);
        SetNumericValueSafe(_numExpHeavyMassMax, (decimal)exp.Expected.HeavyMassMax);
        SetNumericValueSafe(_numExpLargestClusterMax, (decimal)(exp.Expected.LargestClusterFractionMax * 100));
        SetNumericValueSafe(_numExpFinalTempMax, (decimal)exp.Expected.FinalTemperatureMax);
        _chkExpPhaseTransition.Checked = exp.Expected.ExpectPhaseTransition;
        _chkExpStabilization.Checked = exp.Expected.ExpectStabilization;

        _lblExpValidationStatus.Text = exp.Validation is not null
            ? exp.Validation.Summary
            : "Not validated yet";
        _lblExpValidationStatus.BackColor = exp.Validation?.Passed == true
            ? Color.LightGreen
            : exp.Validation?.Passed == false
                ? Color.LightCoral
                : SystemColors.ControlLight;

        if (exp.Validation is not null)
        {
            UpdateValidationUI(exp.Validation);
        }
        else
        {
            _lvExpCriteria.Items.Clear();
            _txtExpReport.Clear();
        }
    }

    private void UpdateExperimentFromUI()
    {
        if (_currentExperiment is null) return;

        _currentExperiment.Notes = _txtExpNotes.Text;
        _currentExperiment.Expected.SpectralDimensionTarget = (double)_numExpSpectralDimTarget.Value;
        _currentExperiment.Expected.SpectralDimensionMin = (double)_numExpSpectralDimMin.Value;
        _currentExperiment.Expected.SpectralDimensionMax = (double)_numExpSpectralDimMax.Value;
        _currentExperiment.Expected.HeavyClusterCountMin = (int)_numExpHeavyClusterMin.Value;
        _currentExperiment.Expected.HeavyClusterCountMax = (int)_numExpHeavyClusterMax.Value;
        _currentExperiment.Expected.HeavyMassMin = (double)_numExpHeavyMassMin.Value;
        _currentExperiment.Expected.HeavyMassMax = (double)_numExpHeavyMassMax.Value;
        _currentExperiment.Expected.LargestClusterFractionMax = (double)_numExpLargestClusterMax.Value / 100.0;
        _currentExperiment.Expected.FinalTemperatureMax = (double)_numExpFinalTempMax.Value;
        _currentExperiment.Expected.ExpectPhaseTransition = _chkExpPhaseTransition.Checked;
        _currentExperiment.Expected.ExpectStabilization = _chkExpStabilization.Checked;
    }

    /// <summary>
    /// Collects actual results from the API (read-only, no direct simulation access).
    /// </summary>
    private ActualResults CollectActualResultsFromApi()
    {
        int finalStep = _simApi.LiveStep;
        int totalSteps = _simApi.LastConfig?.TotalSteps ?? 0;
        int nodeCount = _simApi.LastConfig?.NodeCount ?? 0;

        double finalSpectralDim = _simApi.LiveSpectralDim;
        double heavyMass = _simApi.LiveHeavyMass;
        int largestCluster = _simApi.LiveLargestCluster;
        double finalTemp = _simApi.LiveTemp;
        double effectiveG = _simApi.LiveEffectiveG;
        int excitedCount = _simApi.LiveExcited;
        int strongEdges = _simApi.LiveStrongEdges;
        double correlation = _simApi.LiveCorrelation;
        double qNorm = _simApi.LiveQNorm;
        double entanglement = _simApi.LiveEntanglement;

        int heavyClusterCount = _simApi.LiveHeavyClusterCount;
        double wallClock = (DateTime.UtcNow - _simApi.SimulationWallClockStart).TotalSeconds;

        return ExperimentValidator.CollectResults(
            finalStep, totalSteps, _simApi.SimulationComplete, null,
            finalSpectralDim, finalSpectralDim, // initial = final when we don't have series
            heavyClusterCount, heavyMass, largestCluster, nodeCount,
            finalTemp, effectiveG, excitedCount, strongEdges,
            correlation, qNorm, entanglement,
            0.0, 0.0, wallClock);
    }

    private void UpdateValidationUI(ValidationSummary validation)
    {
        _lblExpValidationStatus.Text = validation.Summary;
        _lblExpValidationStatus.BackColor = validation.Passed ? Color.LightGreen : Color.LightCoral;

        _lvExpCriteria.Items.Clear();

        foreach (ValidationCriterion criterion in validation.Criteria)
        {
            var item = new ListViewItem(criterion.Name);
            item.SubItems.Add(criterion.Passed ? "\u2705 PASS" : "\u274C FAIL");
            item.SubItems.Add(criterion.Expected);
            item.SubItems.Add(criterion.Actual);

            item.BackColor = criterion.Passed
                ? Color.LightGreen
                : criterion.Level == ValidationLevel.Critical
                    ? Color.LightCoral
                    : Color.LightYellow;

            _lvExpCriteria.Items.Add(item);
        }

        foreach (ColumnHeader col in _lvExpCriteria.Columns)
        {
            col.Width = -2;
        }
    }

    private void BtnExpApplyToSim_Click(object? sender, EventArgs e)
    {
        if (_currentExperiment is null)
        {
            MessageBox.Show("No experiment selected.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        UpdateExperimentFromUI();

        ExperimentConfigApplyRequested?.Invoke(this,
            new ExperimentConfigEventArgs(_currentExperiment.Config, _currentExperiment.Name));

        AppendSysConsole($"[Experiment] Apply requested: '{_currentExperiment.Name}'\n");

        if (!_hasApiConnection)
        {
            MessageBox.Show(
                "Experiment config ready to apply.\nConnect via RqSimUI to apply to a running simulation.",
                "Apply to Simulation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
