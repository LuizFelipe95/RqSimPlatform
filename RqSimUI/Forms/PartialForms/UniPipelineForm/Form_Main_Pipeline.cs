using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Text.Json;
using RQSimulation.Core.Plugins;
using RqSimPlatform.PluginManager.UI.Configuration;
using RqSimPlatform.PluginManager.UI.IncludedPlugins;
using RqSimForms.Forms.Interfaces;
using RQSimulation.GPUCompressedSparseRow;

namespace RqSimForms;

partial class Form_Main_RqSim
{
    // === UniPipeline State ===
    private IPhysicsModule? _uniPipelineSelectedModule;
    private bool _uniPipelineIsDirty;

    // New UI state fields
    private string? _gpuComputeEngineSelection;
    private string? _topologyModeSelection;



    // Settings file path
    private static string UiSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RqSimPlatform",
        "ui_settings.json");

    private sealed class UiSettings
    {
        public string? GpuEngine { get; set; }
        public string? TopologyMode { get; set; }
    }

    private static void SaveUiSettings(UiSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(UiSettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(UiSettingsPath, json);
        }
        catch
        {
            // Non-fatal - do not crash UI on save failure
        }
    }

    private static UiSettings? LoadUiSettings()
    {
        try
        {
            if (!File.Exists(UiSettingsPath)) return null;
            string json = File.ReadAllText(UiSettingsPath);
            return JsonSerializer.Deserialize<UiSettings>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the physics pipeline from SimAPI.
    /// </summary>
    private PhysicsPipeline? UniPipelineCurrent => _simApi?.Pipeline;

    /// <summary>
    /// Initializes the UniPipeline tab controls and event handlers.
    /// Call this from InitializeUiAfterDesigner.
    /// </summary>
    private void InitializeUniPipelineTab()
    {
        SetupUniPipelineEventHandlers();
        SetupExecutionTypeCombo();
        RefreshUniPipelineModuleList();

        // Load saved UI settings and apply to controls
        var saved = LoadUiSettings();
        if (saved is not null)
        {
            // Apply to UniPipeline tab controls
            if (!string.IsNullOrEmpty(saved.GpuEngine) && comboBox_GpuEngineUniPipeline != null)
            {
                for (int i = 0; i < comboBox_GpuEngineUniPipeline.Items.Count; i++)
                {
                    if (string.Equals(comboBox_GpuEngineUniPipeline.Items[i].ToString(), saved.GpuEngine, StringComparison.OrdinalIgnoreCase))
                    {
                        comboBox_GpuEngineUniPipeline.SelectedIndex = i;
                        _gpuComputeEngineSelection = saved.GpuEngine;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(saved.TopologyMode) && comboBox_TopologyMode != null)
            {
                for (int i = 0; i < comboBox_TopologyMode.Items.Count; i++)
                {
                    if (string.Equals(comboBox_TopologyMode.Items[i].ToString(), saved.TopologyMode, StringComparison.OrdinalIgnoreCase))
                    {
                        comboBox_TopologyMode.SelectedIndex = i;
                        _topologyModeSelection = saved.TopologyMode;
                        break;
                    }
                }
            }

            // Apply loaded settings to SimAPI if already initialized
            ApplyGpuEngineSelectionToSimApi();
            ApplyTopologyModeToSimApi();
        }

        // Wire GPU and topology UI controls
        SetupGpuAndTopologyControls();


        // Initialize Dynamic Physics Parameters UI (Phase 4)
        InitializeDynamicPhysicsControls();
    }


    private void SetupUniPipelineEventHandlers()
    {
        _dgvModules.SelectionChanged += DgvModules_SelectionChanged;
        _dgvModules.CellValueChanged += DgvModules_CellValueChanged;
        _dgvModules.CurrentCellDirtyStateChanged += DgvModules_CurrentCellDirtyStateChanged;
    }

    // Provide local implementation in this partial to ensure compilation
    private void SetupExecutionTypeCombo()
    {
        if (_cmbExecutionType == null) return;
        _cmbExecutionType.Items.Clear();
        try
        {
            _cmbExecutionType.Items.AddRange(Enum.GetNames(typeof(ExecutionType)));
        }
        catch
        {
            // Ignore if ExecutionType not available in this context
        }
    }

    private void SetupGpuAndTopologyControls()
    {
        try
        {
            // Subscribe to GPU engine selection changes (UniPipeline tab control)
            if (comboBox_GpuEngineUniPipeline != null)
            {
                comboBox_GpuEngineUniPipeline.SelectedIndexChanged -= ComboBox_GpuEngineUniPipeline_SelectedIndexChanged;
                comboBox_GpuEngineUniPipeline.SelectedIndexChanged += ComboBox_GpuEngineUniPipeline_SelectedIndexChanged;

                // Initialize selection field
                if (comboBox_GpuEngineUniPipeline.SelectedItem is object sel)
                {
                    _gpuComputeEngineSelection = sel.ToString();
                }
            }

            // Subscribe to topology mode changes
            if (comboBox_TopologyMode != null)
            {
                comboBox_TopologyMode.SelectedIndexChanged -= ComboBox_TopologyMode_SelectedIndexChanged;
                comboBox_TopologyMode.SelectedIndexChanged += ComboBox_TopologyMode_SelectedIndexChanged;

                if (comboBox_TopologyMode.SelectedItem is object tsel)
                {
                    _topologyModeSelection = tsel.ToString();
                }
            }
        }
        catch
        {
            // Non-fatal; UI may not be fully initialized in some contexts
        }
    }

    private void ComboBox_GpuEngineUniPipeline_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (comboBox_GpuEngineUniPipeline?.SelectedItem is null) return;
        _gpuComputeEngineSelection = comboBox_GpuEngineUniPipeline.SelectedItem.ToString();

        AppendSysConsole($"[UI] GPU compute engine selected: {_gpuComputeEngineSelection}\n");

        // Save to persistent settings
        SaveUiSettings(new UiSettings
        {
            GpuEngine = _gpuComputeEngineSelection,
            TopologyMode = _topologyModeSelection
        });

        // Apply selection to RqSimEngineApi if available
        ApplyGpuEngineSelectionToSimApi();
    }

    private void ComboBox_TopologyMode_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (comboBox_TopologyMode?.SelectedItem is null) return;
        _topologyModeSelection = comboBox_TopologyMode.SelectedItem.ToString();

        AppendSysConsole($"[UI] Topology mode selected: {_topologyModeSelection}\n");

        // Save to persistent settings
        SaveUiSettings(new UiSettings
        {
            GpuEngine = _gpuComputeEngineSelection,
            TopologyMode = _topologyModeSelection
        });

        // Apply to running CSR engine if available
        ApplyTopologyModeToSimApi();
    }

    /// <summary>
    /// Applies GPU engine type selection to RqSimEngineApi.
    /// </summary>
    private void ApplyGpuEngineSelectionToSimApi()
    {
        if (_simApi is null) return;

        try
        {
            // Map combo selection to GpuEngineType enum
            var engineType = _gpuComputeEngineSelection switch
            {
                "Auto (Recommend)" => RqSimForms.Forms.Interfaces.GpuEngineType.Auto,
                "Original (Dense)" => RqSimForms.Forms.Interfaces.GpuEngineType.Original,
                "CSR (Sparse)" => RqSimForms.Forms.Interfaces.GpuEngineType.Csr,
                "CPU Only" => RqSimForms.Forms.Interfaces.GpuEngineType.CpuOnly,
                _ => RqSimForms.Forms.Interfaces.GpuEngineType.Auto
            };

            _simApi.SetGpuEngineType(engineType);
            AppendSysConsole($"[UI] Applied GPU engine type to SimAPI: {engineType}\n");
        }
        catch (Exception ex)
        {
            AppendSysConsole($"[UI] Failed to apply GPU engine type: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Applies topology mode selection to CSR engine if available.
    /// </summary>
    private void ApplyTopologyModeToSimApi()
    {
        if (_simApi is null) return;

        try
        {
            var csrEngine = _simApi.CsrCayleyEngine;
            if (csrEngine is null || !csrEngine.IsInitialized)
            {
                AppendSysConsole($"[UI] CSR engine not available - topology mode will be applied on next init\n");
                return;
            }

            var mode = _topologyModeSelection switch
            {
                "CSR (Static)" => RQSimulation.GPUCompressedSparseRow.GpuCayleyEvolutionEngineCsr.TopologyMode.CsrStatic,
                "StreamCompaction (Hybrid)" => RQSimulation.GPUCompressedSparseRow.GpuCayleyEvolutionEngineCsr.TopologyMode.StreamCompaction,
                "StreamCompaction (Full GPU)" => RQSimulation.GPUCompressedSparseRow.GpuCayleyEvolutionEngineCsr.TopologyMode.StreamCompactionFullGpu,
                _ => RQSimulation.GPUCompressedSparseRow.GpuCayleyEvolutionEngineCsr.TopologyMode.CsrStatic
            };

            csrEngine.CurrentTopologyMode = mode;
            AppendSysConsole($"[UI] Applied topology mode to CSR engine: {mode}\n");
        }
        catch (Exception ex)
        {
            AppendSysConsole($"[UI] Failed to apply topology mode: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Refreshes the DataGridView with current pipeline modules.
    /// </summary>
    public void RefreshUniPipelineModuleList()
    {
        var pipeline = UniPipelineCurrent;
        if (pipeline is null) return;

        _dgvModules.Rows.Clear();

        foreach (var module in pipeline.Modules)
        {
            int rowIndex = _dgvModules.Rows.Add();
            var row = _dgvModules.Rows[rowIndex];
            row.Tag = module;
            row.Cells[_colEnabled.Index].Value = module.IsEnabled;
            row.Cells[_colName.Index].Value = module.Name;
            row.Cells[_colCategory.Index].Value = module.Category;
            row.Cells[_colStage.Index].Value = module.Stage.ToString();
            row.Cells[_colType.Index].Value = module.ExecutionType.ToString();
            row.Cells[_colPriority.Index].Value = module.Priority;
            row.Cells[_colModuleGroup.Index].Value = module.ModuleGroup ?? "-";

            // Color-code by execution stage (primary) and type (secondary)
            row.DefaultCellStyle.BackColor = module.Stage switch
            {
                ExecutionStage.Preparation => Color.FromArgb(255, 255, 220), // Light yellow
                ExecutionStage.Forces => module.ExecutionType == ExecutionType.GPU
                    ? Color.FromArgb(220, 255, 220) // Light green for GPU
                    : SystemColors.Window,
                ExecutionStage.Integration => Color.FromArgb(220, 240, 255), // Light cyan
                ExecutionStage.PostProcess => Color.FromArgb(240, 240, 240), // Light gray
                _ => SystemColors.Window
            };
        }

        UpdateUniPipelineButtonStates();
    }

    private void UpdateUniPipelineButtonStates()
    {
        bool hasSelection = _dgvModules.SelectedRows.Count > 0;
        int selectedIndex = hasSelection ? _dgvModules.SelectedRows[0].Index : -1;

        _btnMoveUp.Enabled = hasSelection && selectedIndex > 0;
        _btnMoveDown.Enabled = hasSelection && selectedIndex < _dgvModules.Rows.Count - 1;
        _btnRemove.Enabled = hasSelection;
    }

    private void UpdateUniPipelinePropertiesPanel()
    {
        if (_uniPipelineSelectedModule is null)
        {
            _txtModuleName.Text = string.Empty;
            _txtDescription.Text = string.Empty;
            _cmbExecutionType.SelectedIndex = -1;
            return;
        }

        _txtModuleName.Text = _uniPipelineSelectedModule.Name;
        _txtDescription.Text = _uniPipelineSelectedModule.Description;
        _cmbExecutionType.SelectedItem = _uniPipelineSelectedModule.ExecutionType.ToString();
    }

    private void DgvModules_SelectionChanged(object? sender, EventArgs e)
    {
        if (_dgvModules.SelectedRows.Count == 0)
        {
            _uniPipelineSelectedModule = null;
        }
        else
        {
            _uniPipelineSelectedModule = _dgvModules.SelectedRows[0].Tag as IPhysicsModule;
        }

        UpdateUniPipelinePropertiesPanel();
        UpdateUniPipelineButtonStates();
    }

    private void DgvModules_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        // Commit checkbox changes immediately
        if (_dgvModules.IsCurrentCellDirty &&
            _dgvModules.CurrentCell.ColumnIndex == _colEnabled.Index)
        {
            _dgvModules.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void DgvModules_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != _colEnabled.Index) return;

        var row = _dgvModules.Rows[e.RowIndex];
        if (row.Tag is IPhysicsModule module)
        {
            bool isEnabled = (bool)(row.Cells[_colEnabled.Index].Value ?? false);
            module.IsEnabled = isEnabled;
            _uniPipelineIsDirty = true;
        }
    }

    private void _btnMoveUp_Click(object sender, EventArgs e)
    {
        var pipeline = UniPipelineCurrent;
        if (pipeline is null || _uniPipelineSelectedModule is null) return;

        if (pipeline.MoveUp(_uniPipelineSelectedModule))
        {
            int currentIndex = _dgvModules.SelectedRows[0].Index;
            RefreshUniPipelineModuleList();
            if (currentIndex > 0)
            {
                _dgvModules.ClearSelection();
                _dgvModules.Rows[currentIndex - 1].Selected = true;
            }
            _uniPipelineIsDirty = true;
        }
    }

    private void _btnMoveDown_Click(object sender, EventArgs e)
    {
        var pipeline = UniPipelineCurrent;
        if (pipeline is null || _uniPipelineSelectedModule is null) return;

        if (pipeline.MoveDown(_uniPipelineSelectedModule))
        {
            int currentIndex = _dgvModules.SelectedRows[0].Index;
            RefreshUniPipelineModuleList();
            if (currentIndex < _dgvModules.Rows.Count - 1)
            {
                _dgvModules.ClearSelection();
                _dgvModules.Rows[currentIndex + 1].Selected = true;
            }
            _uniPipelineIsDirty = true;
        }
    }

    private void _btnRemove_Click(object sender, EventArgs e)
    {
        var pipeline = UniPipelineCurrent;
        if (pipeline is null || _uniPipelineSelectedModule is null) return;

        var result = MessageBox.Show(
            $"Remove module '{_uniPipelineSelectedModule.Name}'?",
            "Confirm Removal",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            pipeline.RemoveModule(_uniPipelineSelectedModule);
            _uniPipelineSelectedModule = null;
            RefreshUniPipelineModuleList();
            _uniPipelineIsDirty = true;
        }
    }

    private void _btnLoadDll_Click(object sender, EventArgs e)
    {
        var pipeline = UniPipelineCurrent;
        if (pipeline is null) return;

        using var dialog = new OpenFileDialog
        {
            Title = "Load Physics Module DLL",
            Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            var modules = LoadModulesFromDll(dialog.FileName);
            if (modules.Count == 0)
            {
                MessageBox.Show(
                    "No IPhysicsModule implementations found in the selected DLL.",
                    "No Modules Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Show selection dialog if multiple modules
            if (modules.Count == 1)
            {
                pipeline.RegisterModule(modules[0]);
            }
            else
            {
                using var selectForm = new Form
                {
                    Text = "Select Modules to Add",
                    Size = new Size(400, 300),
                    StartPosition = FormStartPosition.CenterParent,
                    MinimizeBox = false,
                    MaximizeBox = false
                };

                var checkedList = new CheckedListBox
                {
                    Dock = DockStyle.Fill,
                    CheckOnClick = true
                };

                foreach (var mod in modules)
                {
                    checkedList.Items.Add(mod.Name, isChecked: true);
                }

                var btnAdd = new Button
                {
                    Text = "Add Selected",
                    DialogResult = DialogResult.OK,
                    Dock = DockStyle.Bottom,
                    Height = 35
                };

                selectForm.Controls.Add(checkedList);
                selectForm.Controls.Add(btnAdd);
                selectForm.AcceptButton = btnAdd;

                if (selectForm.ShowDialog(this) == DialogResult.OK)
                {
                    for (int i = 0; i < checkedList.Items.Count; i++)
                    {
                        if (checkedList.GetItemChecked(i))
                        {
                            pipeline.RegisterModule(modules[i]);
                        }
                    }
                }
            }

            RefreshUniPipelineModuleList();
            _uniPipelineIsDirty = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error loading DLL:\n{ex.Message}",
                "Load Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void _btnAddBuiltIn_Click(object sender, EventArgs e)
    {
        var pipeline = UniPipelineCurrent;
        if (pipeline is null) return;

        // Show selection between built-in modules and included plugins
        using var selectTypeForm = new Form
        {
            Text = "Select Module Source",
            Size = new Size(380, 160),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog
        };

        var btnBuiltIn = new Button
        {
            Text = "Built-in Modules",
            Location = new Point(20, 30),
            Size = new Size(160, 50),
            DialogResult = DialogResult.Yes
        };

        var btnIncluded = new Button
        {
            Text = "Included Plugins\n(CPU/GPU Alternate)",
            Location = new Point(200, 30),
            Size = new Size(160, 50),
            DialogResult = DialogResult.No
        };

        selectTypeForm.Controls.Add(btnBuiltIn);
        selectTypeForm.Controls.Add(btnIncluded);

        var result = selectTypeForm.ShowDialog(this);

        if (result == DialogResult.Yes)
        {
            AddBuiltInModules();
        }
        else if (result == DialogResult.No)
        {
            AddIncludedPlugins();
        }
    }

    private void AddBuiltInModules()
    {
        var pipeline = UniPipelineCurrent;
        if (pipeline is null) return;

        // Get available built-in modules not already in pipeline
        var builtInTypes = GetBuiltInModuleTypes();
        var existingNames = pipeline.Modules.Select(m => m.Name).ToHashSet();
        var availableTypes = builtInTypes
            .Where(t => !existingNames.Contains(GetModuleName(t)))
            .ToList();

        if (availableTypes.Count == 0)
        {
            MessageBox.Show(
                "All built-in modules are already in the pipeline.",
                "No Modules Available",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var selectForm = new Form
        {
            Text = "Add Built-in Module",
            Size = new Size(400, 350),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.MultiExtended
        };

        foreach (var type in availableTypes)
        {
            listBox.Items.Add(new ModuleTypeItem(type));
        }

        var btnAdd = new Button
        {
            Text = "Add Selected",
            DialogResult = DialogResult.OK,
            Dock = DockStyle.Bottom,
            Height = 35
        };

        selectForm.Controls.Add(listBox);
        selectForm.Controls.Add(btnAdd);
        selectForm.AcceptButton = btnAdd;

        if (selectForm.ShowDialog(this) == DialogResult.OK && listBox.SelectedItems.Count > 0)
        {
            foreach (ModuleTypeItem item in listBox.SelectedItems)
            {
                try
                {
                    var module = (IPhysicsModule?)Activator.CreateInstance(item.Type);
                    if (module is not null)
                    {
                        pipeline.RegisterModule(module);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Error creating {item.Type.Name}:\n{ex.Message}",
                        "Create Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            RefreshUniPipelineModuleList();
            _uniPipelineIsDirty = true;
        }
    }

    /// <summary>
    /// Shows dialog to add included plugins from the IncludedPlugins folder.
    /// </summary>
    private void AddIncludedPlugins()
    {
        var pipeline = UniPipelineCurrent;
        if (pipeline is null) return;

        var pluginInfoList = IncludedPluginsRegistry.GetPluginInfoList();
        var existingNames = pipeline.Modules.Select(m => m.Name).ToHashSet();
        var availablePlugins = pluginInfoList
            .Where(p => !existingNames.Contains(p.Name))
            .ToList();

        if (availablePlugins.Count == 0)
        {
            MessageBox.Show(
                "All included plugins are already in the pipeline.",
                "No Plugins Available",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var selectForm = new Form
        {
            Text = "Add Included Plugins",
            Size = new Size(550, 450),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var checkedList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true
        };

        foreach (var plugin in availablePlugins)
        {
            checkedList.Items.Add(new PluginInfoItem(plugin), isChecked: false);
        }

        var tlpButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            ColumnCount = 4,
            RowCount = 1
        };
        tlpButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        tlpButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        tlpButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        tlpButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

        var btnSelectAll = new Button { Text = "Select All", Dock = DockStyle.Fill, Margin = new Padding(3) };
        var btnSelectCpu = new Button { Text = "Select CPU", Dock = DockStyle.Fill, Margin = new Padding(3) };
        var btnSelectGpu = new Button { Text = "Select GPU", Dock = DockStyle.Fill, Margin = new Padding(3) };
        var btnAdd = new Button { Text = "Add Selected", Dock = DockStyle.Fill, Margin = new Padding(3), DialogResult = DialogResult.OK };

        btnSelectAll.Click += (s, args) =>
        {
            for (int i = 0; i < checkedList.Items.Count; i++)
                checkedList.SetItemChecked(i, true);
        };

        btnSelectCpu.Click += (s, args) =>
        {
            for (int i = 0; i < checkedList.Items.Count; i++)
            {
                if (checkedList.Items[i] is PluginInfoItem item)
                    checkedList.SetItemChecked(i, !item.Info.IsGpu);
            }
        };

        btnSelectGpu.Click += (s, args) =>
        {
            for (int i = 0; i < checkedList.Items.Count; i++)
            {
                if (checkedList.Items[i] is PluginInfoItem item)
                    checkedList.SetItemChecked(i, item.Info.IsGpu);
            }
        };

        tlpButtons.Controls.Add(btnSelectAll, 0, 0);
        tlpButtons.Controls.Add(btnSelectCpu, 1, 0);
        tlpButtons.Controls.Add(btnSelectGpu, 2, 0);
        tlpButtons.Controls.Add(btnAdd, 3, 0);

        selectForm.Controls.Add(checkedList);
        selectForm.Controls.Add(tlpButtons);
        selectForm.AcceptButton = btnAdd;

        if (selectForm.ShowDialog(this) == DialogResult.OK)
        {
            int addedCount = 0;
            for (int i = 0; i < checkedList.Items.Count; i++)
            {
                if (checkedList.GetItemChecked(i) && checkedList.Items[i] is PluginInfoItem item)
                {
                    try
                    {
                        var module = (IPhysicsModule?)Activator.CreateInstance(item.Info.Type);
                        if (module is not null)
                        {
                            pipeline.RegisterModule(module);
                            addedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Error creating {item.Info.Name}:\n{ex.Message}",
                            "Create Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
            }

            if (addedCount > 0)
            {
                RefreshUniPipelineModuleList();
                _uniPipelineIsDirty = true;
            }
        }
    }

    private void _btnSaveConfig_Click(object sender, EventArgs e)
    {
        var pipeline = UniPipelineCurrent;
        if (pipeline is null) return;

        using var dialog = new SaveFileDialog
        {
            Title = "Save Plugin Configuration",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
            FileName = "plugins.json",
            InitialDirectory = Path.GetDirectoryName(PluginConfigSerializer.DefaultConfigPath)
        };

        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            var config = PluginConfigSerializer.CaptureFromPipeline(pipeline,
                Path.GetFileNameWithoutExtension(dialog.FileName));
            PluginConfigSerializer.Save(config, dialog.FileName);

            MessageBox.Show(
                $"Configuration saved successfully.\n{config.Modules.Count} modules saved.",
                "Save Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error saving configuration:\n{ex.Message}",
                "Save Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void _btnLoadConfig_Click(object sender, EventArgs e)
    {
        var pipeline = UniPipelineCurrent;
        if (pipeline is null) return;

        using var dialog = new OpenFileDialog
        {
            Title = "Load Plugin Configuration",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(PluginConfigSerializer.DefaultConfigPath)
        };

        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            var config = PluginConfigSerializer.Load(dialog.FileName);
            if (config is null)
            {
                MessageBox.Show(
                    "Failed to load configuration file.",
                    "Load Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var result = MessageBox.Show(
                $"Load configuration '{config.PresetName}'?\n\n" +
                $"This will replace {pipeline.Count} current modules with {config.Modules.Count} saved modules.\n\n" +
                $"Last modified: {config.LastModified:g}",
                "Confirm Load",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            int restored = PluginConfigSerializer.RestoreToPipeline(pipeline, config, clearExisting: true);
            RefreshUniPipelineModuleList();
            _uniPipelineIsDirty = true;

            MessageBox.Show(
                $"Configuration loaded successfully.\n{restored} of {config.Modules.Count} modules restored.",
                "Load Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error loading configuration:\n{ex.Message}",
                "Load Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }



    private void _btnOK_Click(object sender, EventArgs e)
    {
        // Apply changes (no dialog to close since it's a tab)
        ApplyUniPipelineChanges();
    }

    private void _btnApply_Click(object sender, EventArgs e)
    {
        ApplyUniPipelineChanges();
    }

    private void _btnCancel_Click(object sender, EventArgs e)
    {
        // Reload pipeline from default (discard changes)
        RefreshUniPipelineModuleList();
        _uniPipelineIsDirty = false;
    }

    /// <summary>
    /// Applies current changes to the pipeline.
    /// </summary>
    private void ApplyUniPipelineChanges()
    {
        var pipeline = UniPipelineCurrent;
        if (pipeline is null) return;

        // Sort pipeline by stage and priority
        pipeline.SortByPriority();

        // Refresh the view to show new order
        RefreshUniPipelineModuleList();

        // Clear dirty flag
        _uniPipelineIsDirty = false;

        AppendSysConsole($"[Pipeline] Applied changes: {pipeline.Count} modules in pipeline\n");
    }

    // === Helper Methods ===

    private static List<IPhysicsModule> LoadModulesFromDll(string dllPath)
    {
        var modules = new List<IPhysicsModule>();

        var assembly = Assembly.LoadFrom(dllPath);
        var moduleTypes = assembly.GetTypes()
            .Where(t => typeof(IPhysicsModule).IsAssignableFrom(t)
                     && !t.IsInterface
                     && !t.IsAbstract
                     && t.GetConstructor(Type.EmptyTypes) is not null);

        foreach (var type in moduleTypes)
        {
            try
            {
                if (Activator.CreateInstance(type) is IPhysicsModule module)
                {
                    modules.Add(module);
                }
            }
            catch
            {
                // Skip modules that fail to instantiate
            }
        }

        return modules;
    }

    private static IReadOnlyList<Type> GetBuiltInModuleTypes()
    {
        var moduleTypes = new List<Type>();

        // Get all types from RQSimulation.Core.Plugins.Modules namespace
        var assembly = typeof(IPhysicsModule).Assembly;
        var types = assembly.GetTypes()
            .Where(t => typeof(IPhysicsModule).IsAssignableFrom(t)
                     && !t.IsInterface
                     && !t.IsAbstract
                     && t.Namespace?.Contains("Plugins.Modules") == true);

        moduleTypes.AddRange(types);
        return moduleTypes;
    }

    private static string GetModuleName(Type moduleType)
    {
        try
        {
            if (Activator.CreateInstance(moduleType) is IPhysicsModule module)
            {
                return module.Name;
            }
        }
        catch { }
        return moduleType.Name;
    }

    // === Helper Classes ===

    private sealed class ModuleTypeItem
    {
        public Type Type { get; }

        public ModuleTypeItem(Type type)
        {
            Type = type;
        }

        public override string ToString() => Type.Name;
    }

    private sealed class PluginInfoItem
    {
        public PluginInfo Info { get; }

        public PluginInfoItem(PluginInfo info)
        {
            Info = info;
        }

        public override string ToString() =>
            $"{Info.Name} [{Info.Category}] ({(Info.IsGpu ? "GPU" : "CPU")}) - Priority: {Info.Priority}";
    }
}

