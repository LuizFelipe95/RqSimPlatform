using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using RQSimulation.Core.Plugins;
using RqSimPlatform.PluginManager.UI.Configuration;
using RqSimPlatform.PluginManager.UI.IncludedPlugins;

namespace RqSimPlatform.PluginManager.UI
{
    /// <summary>
    /// Form for managing physics pipeline modules.
    /// Allows enabling/disabling, reordering, removing, and loading external DLL modules.
    /// Supports saving/loading configurations to JSON files.
    /// </summary>
    public partial class PhysxPluginsForm : Form
    {
        private PhysicsPipeline? _pipeline;
        private IPhysicsModule? _selectedModule;
        private bool _isDirty;

        /// <summary>
        /// Creates the form without a pipeline. Call SetPipeline before showing.
        /// </summary>
        public PhysxPluginsForm()
        {
            InitializeComponent();
            SetupEventHandlers();
            SetupExecutionTypeCombo();
        }

        /// <summary>
        /// Creates the form with an existing pipeline.
        /// </summary>
        public PhysxPluginsForm(PhysicsPipeline pipeline) : this()
        {
            SetPipeline(pipeline);
        }

        /// <summary>
        /// Sets the pipeline to manage. Call this before showing the form.
        /// </summary>
        public void SetPipeline(PhysicsPipeline pipeline)
        {
            ArgumentNullException.ThrowIfNull(pipeline);
            _pipeline = pipeline;
            RefreshModuleList();
        }

        private void SetupEventHandlers()
        {
            _btnMoveUp.Click += BtnMoveUp_Click;
            _btnMoveDown.Click += BtnMoveDown_Click;
            _btnRemove.Click += BtnRemove_Click;
            _btnLoadDll.Click += BtnLoadDll_Click;
            _btnAddBuiltIn.Click += BtnAddBuiltIn_Click;
            _btnApply.Click += BtnApply_Click;
            _btnSaveConfig.Click += BtnSaveConfig_Click;
            _btnLoadConfig.Click += BtnLoadConfig_Click;

            _dgvModules.SelectionChanged += DgvModules_SelectionChanged;
            _dgvModules.CellValueChanged += DgvModules_CellValueChanged;
            _dgvModules.CurrentCellDirtyStateChanged += DgvModules_CurrentCellDirtyStateChanged;

            FormClosing += PhysxPluginsForm_FormClosing;
        }

        private void SetupExecutionTypeCombo()
        {
            _cmbExecutionType.Items.Clear();
            _cmbExecutionType.Items.AddRange(Enum.GetNames<ExecutionType>());
        }

        /// <summary>
        /// Refreshes the DataGridView with current pipeline modules.
        /// </summary>
        public void RefreshModuleList()
        {
            if (_pipeline is null) return;

            _dgvModules.Rows.Clear();

            foreach (var module in _pipeline.Modules)
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

            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            bool hasSelection = _dgvModules.SelectedRows.Count > 0;
            int selectedIndex = hasSelection ? _dgvModules.SelectedRows[0].Index : -1;

            _btnMoveUp.Enabled = hasSelection && selectedIndex > 0;
            _btnMoveDown.Enabled = hasSelection && selectedIndex < _dgvModules.Rows.Count - 1;
            _btnRemove.Enabled = hasSelection;
        }

        private void UpdatePropertiesPanel()
        {
            if (_selectedModule is null)
            {
                _txtModuleName.Text = string.Empty;
                _txtDescription.Text = string.Empty;
                _cmbExecutionType.SelectedIndex = -1;
                return;
            }

            _txtModuleName.Text = _selectedModule.Name;
            _txtDescription.Text = _selectedModule.Description;
            _cmbExecutionType.SelectedItem = _selectedModule.ExecutionType.ToString();
        }

        private void DgvModules_SelectionChanged(object? sender, EventArgs e)
        {
            if (_dgvModules.SelectedRows.Count == 0)
            {
                _selectedModule = null;
            }
            else
            {
                _selectedModule = _dgvModules.SelectedRows[0].Tag as IPhysicsModule;
            }

            UpdatePropertiesPanel();
            UpdateButtonStates();
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
                _isDirty = true;
            }
        }

        private void BtnMoveUp_Click(object? sender, EventArgs e)
        {
            if (_pipeline is null || _selectedModule is null) return;

            if (_pipeline.MoveUp(_selectedModule))
            {
                int currentIndex = _dgvModules.SelectedRows[0].Index;
                RefreshModuleList();
                if (currentIndex > 0)
                {
                    _dgvModules.ClearSelection();
                    _dgvModules.Rows[currentIndex - 1].Selected = true;
                }
                _isDirty = true;
            }
        }

        private void BtnMoveDown_Click(object? sender, EventArgs e)
        {
            if (_pipeline is null || _selectedModule is null) return;

            if (_pipeline.MoveDown(_selectedModule))
            {
                int currentIndex = _dgvModules.SelectedRows[0].Index;
                RefreshModuleList();
                if (currentIndex < _dgvModules.Rows.Count - 1)
                {
                    _dgvModules.ClearSelection();
                    _dgvModules.Rows[currentIndex + 1].Selected = true;
                }
                _isDirty = true;
            }
        }

        private void BtnRemove_Click(object? sender, EventArgs e)
        {
            if (_pipeline is null || _selectedModule is null) return;

            var result = MessageBox.Show(
                $"Remove module '{_selectedModule.Name}'?",
                "Confirm Removal",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _pipeline.RemoveModule(_selectedModule);
                _selectedModule = null;
                RefreshModuleList();
                _isDirty = true;
            }
        }

        private void BtnLoadDll_Click(object? sender, EventArgs e)
        {
            if (_pipeline is null) return;

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
                    _pipeline.RegisterModule(modules[0]);
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
                                _pipeline.RegisterModule(modules[i]);
                            }
                        }
                    }
                }

                RefreshModuleList();
                _isDirty = true;
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

        private void BtnAddBuiltIn_Click(object? sender, EventArgs e)
        {
            if (_pipeline is null) return;

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
            if (_pipeline is null) return;

            // Get available built-in modules not already in pipeline
            var builtInTypes = GetBuiltInModuleTypes();
            var existingNames = _pipeline.Modules.Select(m => m.Name).ToHashSet();
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
                            _pipeline.RegisterModule(module);
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

                RefreshModuleList();
                _isDirty = true;
            }
        }

        /// <summary>
        /// Shows dialog to add included plugins from the IncludedPlugins folder.
        /// </summary>
        private void AddIncludedPlugins()
        {
            if (_pipeline is null) return;

            var pluginInfoList = IncludedPluginsRegistry.GetPluginInfoList();
            var existingNames = _pipeline.Modules.Select(m => m.Name).ToHashSet();
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
                                _pipeline.RegisterModule(module);
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
                    RefreshModuleList();
                    _isDirty = true;
                }
            }
        }

        /// <summary>
        /// Replaces all current modules with default included plugins.
        /// </summary>
        public void ReplaceWithDefaultPlugins(bool cpuOnly = false)
        {
            if (_pipeline is null) return;

            var result = MessageBox.Show(
                cpuOnly
                    ? "Replace current modules with default CPU plugins?"
                    : "Replace current modules with all default plugins (CPU + GPU)?",
                "Replace Modules",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            IncludedPluginsRegistry.ReplaceWithDefaultPlugins(_pipeline, cpuOnly);
            RefreshModuleList();
            _isDirty = true;
        }

        private void BtnApply_Click(object? sender, EventArgs e)
        {
            _isDirty = false;
        }

        private void PhysxPluginsForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (_isDirty && DialogResult != DialogResult.OK)
            {
                var result = MessageBox.Show(
                    "Changes have been made. Apply changes before closing?",
                    "Unsaved Changes",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                }
                else if (result == DialogResult.Yes)
                {
                    DialogResult = DialogResult.OK;
                }
            }
        }

        private void _btnOK_Click(object sender, EventArgs e)
        {
            // Apply changes and sort pipeline before closing
            ApplyChanges();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void _btnApply_Click(object sender, EventArgs e)
        {
            // Apply changes without closing
            ApplyChanges();
        }

        /// <summary>
        /// Applies current changes to the pipeline.
        /// </summary>
        private void ApplyChanges()
        {
            if (_pipeline is null) return;

            // Sort pipeline by stage and priority
            _pipeline.SortByPriority();

            // Refresh the view to show new order
            RefreshModuleList();

            // Clear dirty flag
            _isDirty = false;
        }

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

        private class PluginInfoItem
        {
            public IncludedPlugins.PluginInfo Info { get; }

            public PluginInfoItem(IncludedPlugins.PluginInfo info)
            {
                Info = info;
            }

            public override string ToString() =>
                $"{Info.Name} [{Info.Category}] ({(Info.IsGpu ? "GPU" : "CPU")}) - Priority: {Info.Priority}";
        }

        private class ModuleTypeItem
        {
            public Type Type { get; }

            public ModuleTypeItem(Type type)
            {
                Type = type;
            }

            public override string ToString() => GetModuleName(Type);
        }

        // === Configuration Save/Load ===

        private void BtnSaveConfig_Click(object? sender, EventArgs e)
        {
            if (_pipeline is null) return;

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
                var config = PluginConfigSerializer.CaptureFromPipeline(_pipeline,
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

        private void BtnLoadConfig_Click(object? sender, EventArgs e)
        {
            if (_pipeline is null) return;

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
                    $"This will replace {_pipeline.Count} current modules with {config.Modules.Count} saved modules.\n\n" +
                    $"Last modified: {config.LastModified:g}",
                    "Confirm Load",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result != DialogResult.Yes) return;

                int restored = PluginConfigSerializer.RestoreToPipeline(_pipeline, config, clearExisting: true);
                RefreshModuleList();
                _isDirty = true;

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

        private void _btnSaveConfig_Click(object sender, EventArgs e)
        {

        }

        private void _btnLoadConfig_Click(object sender, EventArgs e)
        {

        }

        private void _flpButtons_Paint(object sender, PaintEventArgs e)
        {

        }

        private void _btnLoadDll_Click(object sender, EventArgs e)
        {

        }

        private void _btnRemove_Click(object sender, EventArgs e)
        {

        }

        private void _btnMoveDown_Click(object sender, EventArgs e)
        {

        }

        private void _btnMoveUp_Click(object sender, EventArgs e)
        {

        }
    }
}
