namespace RqSimPlatform.PluginManager.UI
{
    partial class PhysxPluginsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }



        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            _tlpMain = new TableLayoutPanel();
            _tlpLeft = new TableLayoutPanel();
            _dgvModules = new DataGridView();
            _colEnabled = new DataGridViewCheckBoxColumn();
            _colName = new DataGridViewTextBoxColumn();
            _colCategory = new DataGridViewTextBoxColumn();
            _colStage = new DataGridViewTextBoxColumn();
            _colType = new DataGridViewTextBoxColumn();
            _colPriority = new DataGridViewTextBoxColumn();
            _colModuleGroup = new DataGridViewTextBoxColumn();
            _flpButtons = new FlowLayoutPanel();
            _btnMoveUp = new Button();
            _btnMoveDown = new Button();
            _btnRemove = new Button();
            _btnLoadDll = new Button();
            _btnAddBuiltIn = new Button();
            _btnSaveConfig = new Button();
            _btnLoadConfig = new Button();
            _grpProperties = new GroupBox();
            _tlpProperties = new TableLayoutPanel();
            _lblModuleName = new Label();
            _txtModuleName = new TextBox();
            _lblDescription = new Label();
            _txtDescription = new TextBox();
            _lblExecutionType = new Label();
            _cmbExecutionType = new ComboBox();
            _flpDialogButtons = new FlowLayoutPanel();
            _btnCancel = new Button();
            _btnApply = new Button();
            _btnOK = new Button();
            _tlpMain.SuspendLayout();
            _tlpLeft.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_dgvModules).BeginInit();
            _flpButtons.SuspendLayout();
            _grpProperties.SuspendLayout();
            _tlpProperties.SuspendLayout();
            _flpDialogButtons.SuspendLayout();
            SuspendLayout();
            // 
            // _tlpMain
            // 
            _tlpMain.ColumnCount = 2;
            _tlpMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            _tlpMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            _tlpMain.Controls.Add(_tlpLeft, 0, 0);
            _tlpMain.Controls.Add(_grpProperties, 1, 0);
            _tlpMain.Controls.Add(_flpDialogButtons, 1, 1);
            _tlpMain.Dock = DockStyle.Fill;
            _tlpMain.Location = new Point(7, 6);
            _tlpMain.Margin = new Padding(3, 2, 3, 2);
            _tlpMain.Name = "_tlpMain";
            _tlpMain.RowCount = 2;
            _tlpMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tlpMain.RowStyles.Add(new RowStyle());
            _tlpMain.Size = new Size(952, 400);
            _tlpMain.TabIndex = 0;
            // 
            // _tlpLeft
            // 
            _tlpLeft.ColumnCount = 1;
            _tlpLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tlpLeft.Controls.Add(_dgvModules, 0, 0);
            _tlpLeft.Controls.Add(_flpButtons, 0, 1);
            _tlpLeft.Dock = DockStyle.Fill;
            _tlpLeft.Location = new Point(3, 2);
            _tlpLeft.Margin = new Padding(3, 2, 3, 2);
            _tlpLeft.Name = "_tlpLeft";
            _tlpLeft.RowCount = 2;
            _tlpLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tlpLeft.RowStyles.Add(new RowStyle());
            _tlpLeft.Size = new Size(612, 362);
            _tlpLeft.TabIndex = 0;
            // 
            // _dgvModules
            // 
            _dgvModules.AllowUserToAddRows = false;
            _dgvModules.AllowUserToDeleteRows = false;
            _dgvModules.AllowUserToResizeRows = false;
            _dgvModules.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _dgvModules.BackgroundColor = SystemColors.Window;
            _dgvModules.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _dgvModules.Columns.AddRange(new DataGridViewColumn[] { _colEnabled, _colName, _colCategory, _colStage, _colType, _colPriority, _colModuleGroup });
            _dgvModules.Dock = DockStyle.Fill;
            _dgvModules.Location = new Point(3, 2);
            _dgvModules.Margin = new Padding(3, 2, 3, 2);
            _dgvModules.Name = "_dgvModules";
            _dgvModules.RowHeadersVisible = false;
            _dgvModules.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _dgvModules.Size = new Size(606, 324);
            _dgvModules.TabIndex = 0;
            // 
            // _colEnabled
            // 
            _colEnabled.FillWeight = 10F;
            _colEnabled.HeaderText = "On";
            _colEnabled.Name = "_colEnabled";
            // 
            // _colName
            // 
            _colName.FillWeight = 30F;
            _colName.HeaderText = "Module Name";
            _colName.Name = "_colName";
            _colName.ReadOnly = true;
            // 
            // _colCategory
            // 
            _colCategory.FillWeight = 15F;
            _colCategory.HeaderText = "Category";
            _colCategory.Name = "_colCategory";
            _colCategory.ReadOnly = true;
            // 
            // _colStage
            // 
            _colStage.FillWeight = 15F;
            _colStage.HeaderText = "Stage";
            _colStage.Name = "_colStage";
            _colStage.ReadOnly = true;
            // 
            // _colType
            // 
            _colType.FillWeight = 12F;
            _colType.HeaderText = "Exec";
            _colType.Name = "_colType";
            _colType.ReadOnly = true;
            // 
            // _colPriority
            // 
            _colPriority.FillWeight = 8F;
            _colPriority.HeaderText = "Priority";
            _colPriority.Name = "_colPriority";
            _colPriority.ReadOnly = true;
            // 
            // _colModuleGroup
            // 
            _colModuleGroup.FillWeight = 15F;
            _colModuleGroup.HeaderText = "Group";
            _colModuleGroup.Name = "_colModuleGroup";
            _colModuleGroup.ReadOnly = true;
            // 
            // _flpButtons
            // 
            _flpButtons.AutoSize = true;
            _flpButtons.Controls.Add(_btnMoveUp);
            _flpButtons.Controls.Add(_btnMoveDown);
            _flpButtons.Controls.Add(_btnRemove);
            _flpButtons.Controls.Add(_btnLoadDll);
            _flpButtons.Controls.Add(_btnAddBuiltIn);
            _flpButtons.Controls.Add(_btnSaveConfig);
            _flpButtons.Controls.Add(_btnLoadConfig);
            _flpButtons.Dock = DockStyle.Fill;
            _flpButtons.Location = new Point(3, 332);
            _flpButtons.Margin = new Padding(3, 4, 3, 2);
            _flpButtons.Name = "_flpButtons";
            _flpButtons.Size = new Size(606, 28);
            _flpButtons.TabIndex = 1;
            _flpButtons.Paint += _flpButtons_Paint;
            // 
            // _btnMoveUp
            // 
            _btnMoveUp.Location = new Point(3, 2);
            _btnMoveUp.Margin = new Padding(3, 2, 3, 2);
            _btnMoveUp.Name = "_btnMoveUp";
            _btnMoveUp.Size = new Size(66, 24);
            _btnMoveUp.TabIndex = 0;
            _btnMoveUp.Text = "▲ Up";
            _btnMoveUp.UseVisualStyleBackColor = true;
            _btnMoveUp.Click += _btnMoveUp_Click;
            // 
            // _btnMoveDown
            // 
            _btnMoveDown.Location = new Point(75, 2);
            _btnMoveDown.Margin = new Padding(3, 2, 3, 2);
            _btnMoveDown.Name = "_btnMoveDown";
            _btnMoveDown.Size = new Size(66, 24);
            _btnMoveDown.TabIndex = 1;
            _btnMoveDown.Text = "▼ Down";
            _btnMoveDown.UseVisualStyleBackColor = true;
            _btnMoveDown.Click += _btnMoveDown_Click;
            // 
            // _btnRemove
            // 
            _btnRemove.Location = new Point(147, 2);
            _btnRemove.Margin = new Padding(3, 2, 3, 2);
            _btnRemove.Name = "_btnRemove";
            _btnRemove.Size = new Size(66, 24);
            _btnRemove.TabIndex = 2;
            _btnRemove.Text = "Remove";
            _btnRemove.UseVisualStyleBackColor = true;
            _btnRemove.Click += _btnRemove_Click;
            // 
            // _btnLoadDll
            // 
            _btnLoadDll.Location = new Point(219, 2);
            _btnLoadDll.Margin = new Padding(3, 2, 3, 2);
            _btnLoadDll.Name = "_btnLoadDll";
            _btnLoadDll.Size = new Size(88, 24);
            _btnLoadDll.TabIndex = 3;
            _btnLoadDll.Text = "Load DLL...";
            _btnLoadDll.UseVisualStyleBackColor = true;
            _btnLoadDll.Click += _btnLoadDll_Click;
            // 
            // _btnAddBuiltIn
            // 
            _btnAddBuiltIn.Location = new Point(313, 2);
            _btnAddBuiltIn.Margin = new Padding(3, 2, 3, 2);
            _btnAddBuiltIn.Name = "_btnAddBuiltIn";
            _btnAddBuiltIn.Size = new Size(88, 24);
            _btnAddBuiltIn.TabIndex = 4;
            _btnAddBuiltIn.Text = "Add Built-in...";
            _btnAddBuiltIn.UseVisualStyleBackColor = true;
            // 
            // _btnSaveConfig
            // 
            _btnSaveConfig.Location = new Point(407, 2);
            _btnSaveConfig.Margin = new Padding(3, 2, 3, 2);
            _btnSaveConfig.Name = "_btnSaveConfig";
            _btnSaveConfig.Size = new Size(88, 24);
            _btnSaveConfig.TabIndex = 5;
            _btnSaveConfig.Text = "Save Config...";
            _btnSaveConfig.UseVisualStyleBackColor = true;
            _btnSaveConfig.Click += _btnSaveConfig_Click;
            // 
            // _btnLoadConfig
            // 
            _btnLoadConfig.Location = new Point(501, 2);
            _btnLoadConfig.Margin = new Padding(3, 2, 3, 2);
            _btnLoadConfig.Name = "_btnLoadConfig";
            _btnLoadConfig.Size = new Size(88, 24);
            _btnLoadConfig.TabIndex = 6;
            _btnLoadConfig.Text = "Load Config...";
            _btnLoadConfig.UseVisualStyleBackColor = true;
            _btnLoadConfig.Click += _btnLoadConfig_Click;
            // 
            // _grpProperties
            // 
            _grpProperties.Controls.Add(_tlpProperties);
            _grpProperties.Dock = DockStyle.Fill;
            _grpProperties.Location = new Point(623, 2);
            _grpProperties.Margin = new Padding(5, 2, 3, 2);
            _grpProperties.Name = "_grpProperties";
            _grpProperties.Padding = new Padding(7, 6, 7, 6);
            _grpProperties.Size = new Size(326, 362);
            _grpProperties.TabIndex = 1;
            _grpProperties.TabStop = false;
            _grpProperties.Text = "Module Properties";
            // 
            // _tlpProperties
            // 
            _tlpProperties.ColumnCount = 2;
            _tlpProperties.ColumnStyles.Add(new ColumnStyle());
            _tlpProperties.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tlpProperties.Controls.Add(_lblModuleName, 0, 0);
            _tlpProperties.Controls.Add(_txtModuleName, 1, 0);
            _tlpProperties.Controls.Add(_lblDescription, 0, 1);
            _tlpProperties.Controls.Add(_txtDescription, 1, 1);
            _tlpProperties.Controls.Add(_lblExecutionType, 0, 2);
            _tlpProperties.Controls.Add(_cmbExecutionType, 1, 2);
            _tlpProperties.Dock = DockStyle.Fill;
            _tlpProperties.Location = new Point(7, 22);
            _tlpProperties.Margin = new Padding(3, 2, 3, 2);
            _tlpProperties.Name = "_tlpProperties";
            _tlpProperties.RowCount = 4;
            _tlpProperties.RowStyles.Add(new RowStyle());
            _tlpProperties.RowStyles.Add(new RowStyle());
            _tlpProperties.RowStyles.Add(new RowStyle());
            _tlpProperties.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tlpProperties.Size = new Size(312, 334);
            _tlpProperties.TabIndex = 0;
            // 
            // _lblModuleName
            // 
            _lblModuleName.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _lblModuleName.AutoSize = true;
            _lblModuleName.Location = new Point(3, 8);
            _lblModuleName.Margin = new Padding(3, 4, 3, 4);
            _lblModuleName.Name = "_lblModuleName";
            _lblModuleName.Size = new Size(70, 15);
            _lblModuleName.TabIndex = 0;
            _lblModuleName.Text = "Name:";
            // 
            // _txtModuleName
            // 
            _txtModuleName.Dock = DockStyle.Fill;
            _txtModuleName.Location = new Point(79, 4);
            _txtModuleName.Margin = new Padding(3, 4, 3, 4);
            _txtModuleName.Name = "_txtModuleName";
            _txtModuleName.ReadOnly = true;
            _txtModuleName.Size = new Size(230, 23);
            _txtModuleName.TabIndex = 1;
            // 
            // _lblDescription
            // 
            _lblDescription.AutoSize = true;
            _lblDescription.Location = new Point(3, 35);
            _lblDescription.Margin = new Padding(3, 4, 3, 4);
            _lblDescription.Name = "_lblDescription";
            _lblDescription.Size = new Size(70, 15);
            _lblDescription.TabIndex = 2;
            _lblDescription.Text = "Description:";
            // 
            // _txtDescription
            // 
            _txtDescription.Dock = DockStyle.Fill;
            _txtDescription.Location = new Point(79, 35);
            _txtDescription.Margin = new Padding(3, 4, 3, 4);
            _txtDescription.Multiline = true;
            _txtDescription.Name = "_txtDescription";
            _txtDescription.ReadOnly = true;
            _txtDescription.ScrollBars = ScrollBars.Vertical;
            _txtDescription.Size = new Size(230, 61);
            _txtDescription.TabIndex = 3;
            // 
            // _lblExecutionType
            // 
            _lblExecutionType.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _lblExecutionType.AutoSize = true;
            _lblExecutionType.Location = new Point(3, 108);
            _lblExecutionType.Margin = new Padding(3, 4, 3, 4);
            _lblExecutionType.Name = "_lblExecutionType";
            _lblExecutionType.Size = new Size(70, 15);
            _lblExecutionType.TabIndex = 4;
            _lblExecutionType.Text = "Execution:";
            // 
            // _cmbExecutionType
            // 
            _cmbExecutionType.Dock = DockStyle.Fill;
            _cmbExecutionType.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbExecutionType.Enabled = false;
            _cmbExecutionType.Location = new Point(79, 104);
            _cmbExecutionType.Margin = new Padding(3, 4, 3, 4);
            _cmbExecutionType.Name = "_cmbExecutionType";
            _cmbExecutionType.Size = new Size(230, 23);
            _cmbExecutionType.TabIndex = 5;
            // 
            // _flpDialogButtons
            // 
            _flpDialogButtons.AutoSize = true;
            _flpDialogButtons.Controls.Add(_btnCancel);
            _flpDialogButtons.Controls.Add(_btnApply);
            _flpDialogButtons.Controls.Add(_btnOK);
            _flpDialogButtons.Dock = DockStyle.Fill;
            _flpDialogButtons.FlowDirection = FlowDirection.RightToLeft;
            _flpDialogButtons.Location = new Point(623, 370);
            _flpDialogButtons.Margin = new Padding(5, 4, 3, 2);
            _flpDialogButtons.Name = "_flpDialogButtons";
            _flpDialogButtons.Size = new Size(326, 28);
            _flpDialogButtons.TabIndex = 2;
            // 
            // _btnCancel
            // 
            _btnCancel.DialogResult = DialogResult.Cancel;
            _btnCancel.Location = new Point(244, 2);
            _btnCancel.Margin = new Padding(3, 2, 3, 2);
            _btnCancel.Name = "_btnCancel";
            _btnCancel.Size = new Size(79, 24);
            _btnCancel.TabIndex = 0;
            _btnCancel.Text = "Cancel";
            _btnCancel.UseVisualStyleBackColor = true;
            // 
            // _btnApply
            // 
            _btnApply.Location = new Point(159, 2);
            _btnApply.Margin = new Padding(3, 2, 3, 2);
            _btnApply.Name = "_btnApply";
            _btnApply.Size = new Size(79, 24);
            _btnApply.TabIndex = 1;
            _btnApply.Text = "Apply";
            _btnApply.UseVisualStyleBackColor = true;
            _btnApply.Click += _btnApply_Click;
            // 
            // _btnOK
            // 
            _btnOK.DialogResult = DialogResult.OK;
            _btnOK.Location = new Point(74, 2);
            _btnOK.Margin = new Padding(3, 2, 3, 2);
            _btnOK.Name = "_btnOK";
            _btnOK.Size = new Size(79, 24);
            _btnOK.TabIndex = 2;
            _btnOK.Text = "OK";
            _btnOK.UseVisualStyleBackColor = true;
            _btnOK.Click += _btnOK_Click;
            // 
            // PhysxPluginsForm
            // 
            AcceptButton = _btnOK;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = _btnCancel;
            ClientSize = new Size(966, 412);
            Controls.Add(_tlpMain);
            Margin = new Padding(3, 2, 3, 2);
            MinimumSize = new Size(614, 347);
            Name = "PhysxPluginsForm";
            Padding = new Padding(7, 6, 7, 6);
            StartPosition = FormStartPosition.CenterParent;
            Text = "Physics Pipeline Manager";
            _tlpMain.ResumeLayout(false);
            _tlpMain.PerformLayout();
            _tlpLeft.ResumeLayout(false);
            _tlpLeft.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_dgvModules).EndInit();
            _flpButtons.ResumeLayout(false);
            _grpProperties.ResumeLayout(false);
            _tlpProperties.ResumeLayout(false);
            _tlpProperties.PerformLayout();
            _flpDialogButtons.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel _tlpMain;
        private System.Windows.Forms.TableLayoutPanel _tlpLeft;
        private System.Windows.Forms.DataGridView _dgvModules;
        private System.Windows.Forms.DataGridViewCheckBoxColumn _colEnabled;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colName;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colCategory;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colStage;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colType;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colPriority;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colModuleGroup;
        private System.Windows.Forms.FlowLayoutPanel _flpButtons;
        private System.Windows.Forms.Button _btnMoveUp;
        private System.Windows.Forms.Button _btnMoveDown;
        private System.Windows.Forms.Button _btnRemove;
        private System.Windows.Forms.Button _btnLoadDll;
        private System.Windows.Forms.Button _btnAddBuiltIn;
        private System.Windows.Forms.Button _btnSaveConfig;
        private System.Windows.Forms.Button _btnLoadConfig;
        private System.Windows.Forms.GroupBox _grpProperties;
        private System.Windows.Forms.TableLayoutPanel _tlpProperties;
        private System.Windows.Forms.Label _lblModuleName;
        private System.Windows.Forms.TextBox _txtModuleName;
        private System.Windows.Forms.Label _lblDescription;
        private System.Windows.Forms.TextBox _txtDescription;
        private System.Windows.Forms.Label _lblExecutionType;
        private System.Windows.Forms.ComboBox _cmbExecutionType;
        private System.Windows.Forms.FlowLayoutPanel _flpDialogButtons;
        private System.Windows.Forms.Button _btnOK;
        private System.Windows.Forms.Button _btnCancel;
        private System.Windows.Forms.Button _btnApply;
    }
}