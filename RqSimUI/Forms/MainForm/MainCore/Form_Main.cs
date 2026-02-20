using System.Diagnostics;
using RqSimForms.Forms.Interfaces;

namespace RqSimForms;

partial class Form_Main_RqSim : Form
{
    private void InitializeComponent()
    {
        ListViewGroup listViewGroup1 = new ListViewGroup("GPU", HorizontalAlignment.Left);
        tabControl_Main = new TabControl();
        tabPage_Settings = new TabPage();
        settingsMainLayout = new TableLayoutPanel();
        grpPhysicsModules = new GroupBox();
        flpPhysics = new FlowLayoutPanel();
        chkQuantumDriven = new CheckBox();
        chkSpacetimePhysics = new CheckBox();
        chkSpinorField = new CheckBox();
        chkVacuumFluctuations = new CheckBox();
        chkBlackHolePhysics = new CheckBox();
        chkYangMillsGauge = new CheckBox();
        chkEnhancedKleinGordon = new CheckBox();
        chkInternalTime = new CheckBox();
        chkSpectralGeometry = new CheckBox();
        chkQuantumGraphity = new CheckBox();
        chkRelationalTime = new CheckBox();
        chkRelationalYangMills = new CheckBox();
        chkNetworkGravity = new CheckBox();
        chkUnifiedPhysicsStep = new CheckBox();
        chkEnforceGaugeConstraints = new CheckBox();
        chkCausalRewiring = new CheckBox();
        chkTopologicalProtection = new CheckBox();
        chkValidateEnergyConservation = new CheckBox();
        chkMexicanHatPotential = new CheckBox();
        chkGeometryMomenta = new CheckBox();
        chkTopologicalCensorship = new CheckBox();
        grpPhysicsConstants = new GroupBox();
        tlpPhysicsConstants = new TableLayoutPanel();
        grpSimParams = new GroupBox();
        tlpSimParams = new TableLayoutPanel();
        lblNodeCount = new Label();
        numNodeCount = new NumericUpDown();
        lblTargetDegree = new Label();
        numTargetDegree = new NumericUpDown();
        lblInitialExcitedProb = new Label();
        numInitialExcitedProb = new NumericUpDown();
        lblLambdaState = new Label();
        numLambdaState = new NumericUpDown();
        lblTemperature = new Label();
        numTemperature = new NumericUpDown();
        lblEdgeTrialProb = new Label();
        numEdgeTrialProb = new NumericUpDown();
        lblMeasurementThreshold = new Label();
        numMeasurementThreshold = new NumericUpDown();
        lblTotalStepsSettings = new Label();
        numTotalSteps = new NumericUpDown();
        lblFractalLevels = new Label();
        numFractalLevels = new NumericUpDown();
        numFractalBranchFactor = new NumericUpDown();
        lblFractalBranchFactor = new Label();
        lblInitialEdgeProb = new Label();
        numInitialEdgeProb = new NumericUpDown();
        lblGravitationalCoupling = new Label();
        numGravitationalCoupling = new NumericUpDown();
        lblVacuumEnergyScale = new Label();
        numVacuumEnergyScale = new NumericUpDown();
        lblGravityTransitionDuration = new Label();
        numGravityTransitionDuration = new NumericUpDown();
        lblDecoherenceRate = new Label();
        numDecoherenceRate = new NumericUpDown();
        lblHotStartTemperature = new Label();
        numHotStartTemperature = new NumericUpDown();
        lblAdaptiveThresholdSigma = new Label();
        numAdaptiveThresholdSigma = new NumericUpDown();
        lblWarmupDuration = new Label();
        numWarmupDuration = new NumericUpDown();
        valAnnealingTimeConstant = new Label();
        tabPage_UniPipelineState = new TabPage();
        groupBox_MultiGpu_Settings = new GroupBox();
        button_RemoveGpuBackgroundPluginToPipeline = new Button();
        button_AddGpuBackgroundPluginToPipeline = new Button();
        label_BackgroundPipelineGPU = new Label();
        comboBox_BackgroundPipelineGPU = new ComboBox();
        label_BackgroundPipelineGPU_Kernels = new Label();
        numericUpDown_BackgroundPluginGPUKernels = new NumericUpDown();
        listView_AnaliticsGPU = new ListView();
        columnHeader_GPU = new ColumnHeader();
        columnHeader_Algorithm = new ColumnHeader();
        columnHeader_GPUKernels = new ColumnHeader();
        label_RenderingGPU = new Label();
        checkBox_UseMultiGPU = new CheckBox();
        label_MultiGPU_ActivePhysxGPU = new Label();
        label_GPUMode = new Label();
        checkBox_EnableGPU = new CheckBox();
        comboBox_GPUComputeEngine = new ComboBox();
        comboBox_3DRenderingGPU = new ComboBox();
        comboBox_MultiGpu_PhysicsGPU = new ComboBox();
        _tlp_UniPipeline_Main = new TableLayoutPanel();
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
        _flpGpuTopologySettings = new FlowLayoutPanel();
        label_GpuEngineUniPipeline = new Label();
        comboBox_GpuEngineUniPipeline = new ComboBox();
        label_TopologyMode = new Label();
        comboBox_TopologyMode = new ComboBox();
        _flpDialogButtons = new FlowLayoutPanel();
        tabPage_Visualization = new TabPage();
        tabPage_Telemetry = new TabPage();
        checkBox_ScienceSimMode = new CheckBox();
        comboBox_GPUIndex = new ComboBox();
        _btnCancel = new Button();
        _btnApply = new Button();
        _btnOK = new Button();
        button_ApplyPipelineConfSet = new Button();
        label_CPUThreads = new Label();
        numericUpDown1 = new NumericUpDown();
        button_Plugins = new Button();
        button_TerminateSimSession = new Button();
        checkBox_StanaloneDX12Form = new CheckBox();
        button_BindConsoleSession = new Button();
        checkBox_AutoTuning = new CheckBox();
        button_RunModernSim = new Button();
        tabControl_Main.SuspendLayout();
        tabPage_Settings.SuspendLayout();
        settingsMainLayout.SuspendLayout();
        grpPhysicsModules.SuspendLayout();
        flpPhysics.SuspendLayout();
        grpPhysicsConstants.SuspendLayout();
        grpSimParams.SuspendLayout();
        tlpSimParams.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)numNodeCount).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numTargetDegree).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numInitialExcitedProb).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numLambdaState).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numTemperature).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numEdgeTrialProb).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numMeasurementThreshold).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numTotalSteps).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numFractalLevels).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numFractalBranchFactor).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numInitialEdgeProb).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numGravitationalCoupling).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numVacuumEnergyScale).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numGravityTransitionDuration).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numDecoherenceRate).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numHotStartTemperature).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numAdaptiveThresholdSigma).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numWarmupDuration).BeginInit();
        tabPage_UniPipelineState.SuspendLayout();
        groupBox_MultiGpu_Settings.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)numericUpDown_BackgroundPluginGPUKernels).BeginInit();
        _tlp_UniPipeline_Main.SuspendLayout();
        _tlpLeft.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_dgvModules).BeginInit();
        _flpButtons.SuspendLayout();
        _grpProperties.SuspendLayout();
        _tlpProperties.SuspendLayout();
        _flpGpuTopologySettings.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)numericUpDown1).BeginInit();
        SuspendLayout();
        // 
        // tabControl_Main
        // 
        tabControl_Main.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        tabControl_Main.Controls.Add(tabPage_Settings);
        tabControl_Main.Controls.Add(tabPage_UniPipelineState);
        tabControl_Main.Controls.Add(tabPage_Visualization);
        tabControl_Main.Controls.Add(tabPage_Telemetry);
        tabControl_Main.Location = new Point(-3, 41);
        tabControl_Main.Name = "tabControl_Main";
        tabControl_Main.SelectedIndex = 0;
        tabControl_Main.Size = new Size(1350, 818);
        tabControl_Main.TabIndex = 0;
        // 
        // tabPage_Settings
        // 
        tabPage_Settings.AutoScroll = true;
        tabPage_Settings.Controls.Add(settingsMainLayout);
        tabPage_Settings.Location = new Point(4, 24);
        tabPage_Settings.Name = "tabPage_Settings";
        tabPage_Settings.Size = new Size(1342, 790);
        tabPage_Settings.TabIndex = 14;
        tabPage_Settings.Text = "Settings";
        tabPage_Settings.UseVisualStyleBackColor = true;
        // 
        // settingsMainLayout
        // 
        settingsMainLayout.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        settingsMainLayout.AutoSize = true;
        settingsMainLayout.ColumnCount = 3;
        settingsMainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        settingsMainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        settingsMainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        settingsMainLayout.Controls.Add(grpPhysicsModules, 0, 0);
        settingsMainLayout.Controls.Add(grpPhysicsConstants, 2, 0);
        settingsMainLayout.Controls.Add(grpSimParams, 1, 0);
        settingsMainLayout.Location = new Point(3, 3);
        settingsMainLayout.Name = "settingsMainLayout";
        settingsMainLayout.RowCount = 1;
        settingsMainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        settingsMainLayout.Size = new Size(1336, 742);
        settingsMainLayout.TabIndex = 0;
        // 
        // grpPhysicsModules
        // 
        grpPhysicsModules.Controls.Add(flpPhysics);
        grpPhysicsModules.Location = new Point(5, 5);
        grpPhysicsModules.Margin = new Padding(5);
        grpPhysicsModules.Name = "grpPhysicsModules";
        grpPhysicsModules.Size = new Size(457, 559);
        grpPhysicsModules.TabIndex = 1;
        grpPhysicsModules.TabStop = false;
        grpPhysicsModules.Text = "Physics Modules";
        // 
        // flpPhysics
        // 
        flpPhysics.AutoScroll = true;
        flpPhysics.Controls.Add(chkQuantumDriven);
        flpPhysics.Controls.Add(chkSpacetimePhysics);
        flpPhysics.Controls.Add(chkSpinorField);
        flpPhysics.Controls.Add(chkVacuumFluctuations);
        flpPhysics.Controls.Add(chkBlackHolePhysics);
        flpPhysics.Controls.Add(chkYangMillsGauge);
        flpPhysics.Controls.Add(chkEnhancedKleinGordon);
        flpPhysics.Controls.Add(chkInternalTime);
        flpPhysics.Controls.Add(chkSpectralGeometry);
        flpPhysics.Controls.Add(chkQuantumGraphity);
        flpPhysics.Controls.Add(chkRelationalTime);
        flpPhysics.Controls.Add(chkRelationalYangMills);
        flpPhysics.Controls.Add(chkNetworkGravity);
        flpPhysics.Controls.Add(chkUnifiedPhysicsStep);
        flpPhysics.Controls.Add(chkEnforceGaugeConstraints);
        flpPhysics.Controls.Add(chkCausalRewiring);
        flpPhysics.Controls.Add(chkTopologicalProtection);
        flpPhysics.Controls.Add(chkValidateEnergyConservation);
        flpPhysics.Controls.Add(chkMexicanHatPotential);
        flpPhysics.Controls.Add(chkGeometryMomenta);
        flpPhysics.Controls.Add(chkTopologicalCensorship);
        flpPhysics.Dock = DockStyle.Fill;
        flpPhysics.FlowDirection = FlowDirection.TopDown;
        flpPhysics.Location = new Point(3, 19);
        flpPhysics.Name = "flpPhysics";
        flpPhysics.Size = new Size(451, 537);
        flpPhysics.TabIndex = 0;
        // 
        // chkQuantumDriven
        // 
        chkQuantumDriven.AutoSize = true;
        chkQuantumDriven.Checked = true;
        chkQuantumDriven.CheckState = CheckState.Checked;
        chkQuantumDriven.Location = new Point(3, 3);
        chkQuantumDriven.Name = "chkQuantumDriven";
        chkQuantumDriven.Size = new Size(148, 19);
        chkQuantumDriven.TabIndex = 0;
        chkQuantumDriven.Text = "Quantum Driven States";
        // 
        // chkSpacetimePhysics
        // 
        chkSpacetimePhysics.AutoSize = true;
        chkSpacetimePhysics.Checked = true;
        chkSpacetimePhysics.CheckState = CheckState.Checked;
        chkSpacetimePhysics.Location = new Point(3, 28);
        chkSpacetimePhysics.Name = "chkSpacetimePhysics";
        chkSpacetimePhysics.Size = new Size(123, 19);
        chkSpacetimePhysics.TabIndex = 1;
        chkSpacetimePhysics.Text = "Spacetime Physics";
        // 
        // chkSpinorField
        // 
        chkSpinorField.AutoSize = true;
        chkSpinorField.Checked = true;
        chkSpinorField.CheckState = CheckState.Checked;
        chkSpinorField.Location = new Point(3, 53);
        chkSpinorField.Name = "chkSpinorField";
        chkSpinorField.Size = new Size(88, 19);
        chkSpinorField.TabIndex = 2;
        chkSpinorField.Text = "Spinor Field";
        // 
        // chkVacuumFluctuations
        // 
        chkVacuumFluctuations.AutoSize = true;
        chkVacuumFluctuations.Checked = true;
        chkVacuumFluctuations.CheckState = CheckState.Checked;
        chkVacuumFluctuations.Location = new Point(3, 78);
        chkVacuumFluctuations.Name = "chkVacuumFluctuations";
        chkVacuumFluctuations.Size = new Size(137, 19);
        chkVacuumFluctuations.TabIndex = 3;
        chkVacuumFluctuations.Text = "Vacuum Fluctuations";
        // 
        // chkBlackHolePhysics
        // 
        chkBlackHolePhysics.AutoSize = true;
        chkBlackHolePhysics.Checked = true;
        chkBlackHolePhysics.CheckState = CheckState.Checked;
        chkBlackHolePhysics.Location = new Point(3, 103);
        chkBlackHolePhysics.Name = "chkBlackHolePhysics";
        chkBlackHolePhysics.Size = new Size(124, 19);
        chkBlackHolePhysics.TabIndex = 4;
        chkBlackHolePhysics.Text = "Black Hole Physics";
        // 
        // chkYangMillsGauge
        // 
        chkYangMillsGauge.AutoSize = true;
        chkYangMillsGauge.Checked = true;
        chkYangMillsGauge.CheckState = CheckState.Checked;
        chkYangMillsGauge.Location = new Point(3, 128);
        chkYangMillsGauge.Name = "chkYangMillsGauge";
        chkYangMillsGauge.Size = new Size(119, 19);
        chkYangMillsGauge.TabIndex = 5;
        chkYangMillsGauge.Text = "Yang-Mills Gauge";
        // 
        // chkEnhancedKleinGordon
        // 
        chkEnhancedKleinGordon.AutoSize = true;
        chkEnhancedKleinGordon.Checked = true;
        chkEnhancedKleinGordon.CheckState = CheckState.Checked;
        chkEnhancedKleinGordon.Location = new Point(3, 153);
        chkEnhancedKleinGordon.Name = "chkEnhancedKleinGordon";
        chkEnhancedKleinGordon.Size = new Size(152, 19);
        chkEnhancedKleinGordon.TabIndex = 6;
        chkEnhancedKleinGordon.Text = "Enhanced Klein-Gordon";
        // 
        // chkInternalTime
        // 
        chkInternalTime.AutoSize = true;
        chkInternalTime.Checked = true;
        chkInternalTime.CheckState = CheckState.Checked;
        chkInternalTime.Location = new Point(3, 178);
        chkInternalTime.Name = "chkInternalTime";
        chkInternalTime.Size = new Size(186, 19);
        chkInternalTime.TabIndex = 7;
        chkInternalTime.Text = "Internal Time (Page-Wootters)";
        // 
        // chkSpectralGeometry
        // 
        chkSpectralGeometry.AutoSize = true;
        chkSpectralGeometry.Checked = true;
        chkSpectralGeometry.CheckState = CheckState.Checked;
        chkSpectralGeometry.Location = new Point(3, 203);
        chkSpectralGeometry.Name = "chkSpectralGeometry";
        chkSpectralGeometry.Size = new Size(123, 19);
        chkSpectralGeometry.TabIndex = 8;
        chkSpectralGeometry.Text = "Spectral Geometry";
        // 
        // chkQuantumGraphity
        // 
        chkQuantumGraphity.AutoSize = true;
        chkQuantumGraphity.Checked = true;
        chkQuantumGraphity.CheckState = CheckState.Checked;
        chkQuantumGraphity.Location = new Point(3, 228);
        chkQuantumGraphity.Name = "chkQuantumGraphity";
        chkQuantumGraphity.Size = new Size(125, 19);
        chkQuantumGraphity.TabIndex = 9;
        chkQuantumGraphity.Text = "Quantum Graphity";
        // 
        // chkRelationalTime
        // 
        chkRelationalTime.AutoSize = true;
        chkRelationalTime.Checked = true;
        chkRelationalTime.CheckState = CheckState.Checked;
        chkRelationalTime.Location = new Point(3, 253);
        chkRelationalTime.Name = "chkRelationalTime";
        chkRelationalTime.Size = new Size(108, 19);
        chkRelationalTime.TabIndex = 10;
        chkRelationalTime.Text = "Relational Time";
        // 
        // chkRelationalYangMills
        // 
        chkRelationalYangMills.AutoSize = true;
        chkRelationalYangMills.Checked = true;
        chkRelationalYangMills.CheckState = CheckState.Checked;
        chkRelationalYangMills.Location = new Point(3, 278);
        chkRelationalYangMills.Name = "chkRelationalYangMills";
        chkRelationalYangMills.Size = new Size(137, 19);
        chkRelationalYangMills.TabIndex = 11;
        chkRelationalYangMills.Text = "Relational Yang-Mills";
        // 
        // chkNetworkGravity
        // 
        chkNetworkGravity.AutoSize = true;
        chkNetworkGravity.Checked = true;
        chkNetworkGravity.CheckState = CheckState.Checked;
        chkNetworkGravity.Location = new Point(3, 303);
        chkNetworkGravity.Name = "chkNetworkGravity";
        chkNetworkGravity.Size = new Size(111, 19);
        chkNetworkGravity.TabIndex = 12;
        chkNetworkGravity.Text = "Network Gravity";
        // 
        // chkUnifiedPhysicsStep
        // 
        chkUnifiedPhysicsStep.AutoSize = true;
        chkUnifiedPhysicsStep.Checked = true;
        chkUnifiedPhysicsStep.CheckState = CheckState.Checked;
        chkUnifiedPhysicsStep.Location = new Point(3, 328);
        chkUnifiedPhysicsStep.Name = "chkUnifiedPhysicsStep";
        chkUnifiedPhysicsStep.Size = new Size(132, 19);
        chkUnifiedPhysicsStep.TabIndex = 13;
        chkUnifiedPhysicsStep.Text = "Unified Physics Step";
        // 
        // chkEnforceGaugeConstraints
        // 
        chkEnforceGaugeConstraints.AutoSize = true;
        chkEnforceGaugeConstraints.Checked = true;
        chkEnforceGaugeConstraints.CheckState = CheckState.Checked;
        chkEnforceGaugeConstraints.Location = new Point(3, 353);
        chkEnforceGaugeConstraints.Name = "chkEnforceGaugeConstraints";
        chkEnforceGaugeConstraints.Size = new Size(166, 19);
        chkEnforceGaugeConstraints.TabIndex = 14;
        chkEnforceGaugeConstraints.Text = "Enforce Gauge Constraints";
        // 
        // chkCausalRewiring
        // 
        chkCausalRewiring.AutoSize = true;
        chkCausalRewiring.Checked = true;
        chkCausalRewiring.CheckState = CheckState.Checked;
        chkCausalRewiring.Location = new Point(3, 378);
        chkCausalRewiring.Name = "chkCausalRewiring";
        chkCausalRewiring.Size = new Size(110, 19);
        chkCausalRewiring.TabIndex = 15;
        chkCausalRewiring.Text = "Causal Rewiring";
        // 
        // chkTopologicalProtection
        // 
        chkTopologicalProtection.AutoSize = true;
        chkTopologicalProtection.Checked = true;
        chkTopologicalProtection.CheckState = CheckState.Checked;
        chkTopologicalProtection.Location = new Point(3, 403);
        chkTopologicalProtection.Name = "chkTopologicalProtection";
        chkTopologicalProtection.Size = new Size(146, 19);
        chkTopologicalProtection.TabIndex = 16;
        chkTopologicalProtection.Text = "Topological Protection";
        // 
        // chkValidateEnergyConservation
        // 
        chkValidateEnergyConservation.AutoSize = true;
        chkValidateEnergyConservation.Checked = true;
        chkValidateEnergyConservation.CheckState = CheckState.Checked;
        chkValidateEnergyConservation.Location = new Point(3, 428);
        chkValidateEnergyConservation.Name = "chkValidateEnergyConservation";
        chkValidateEnergyConservation.Size = new Size(179, 19);
        chkValidateEnergyConservation.TabIndex = 17;
        chkValidateEnergyConservation.Text = "Validate Energy Conservation";
        // 
        // chkMexicanHatPotential
        // 
        chkMexicanHatPotential.AutoSize = true;
        chkMexicanHatPotential.Checked = true;
        chkMexicanHatPotential.CheckState = CheckState.Checked;
        chkMexicanHatPotential.Location = new Point(3, 453);
        chkMexicanHatPotential.Name = "chkMexicanHatPotential";
        chkMexicanHatPotential.Size = new Size(142, 19);
        chkMexicanHatPotential.TabIndex = 18;
        chkMexicanHatPotential.Text = "Mexican Hat Potential";
        // 
        // chkGeometryMomenta
        // 
        chkGeometryMomenta.AutoSize = true;
        chkGeometryMomenta.Checked = true;
        chkGeometryMomenta.CheckState = CheckState.Checked;
        chkGeometryMomenta.Location = new Point(3, 478);
        chkGeometryMomenta.Name = "chkGeometryMomenta";
        chkGeometryMomenta.Size = new Size(133, 19);
        chkGeometryMomenta.TabIndex = 20;
        chkGeometryMomenta.Text = "Geometry Momenta";
        // 
        // chkTopologicalCensorship
        // 
        chkTopologicalCensorship.AutoSize = true;
        chkTopologicalCensorship.Checked = true;
        chkTopologicalCensorship.CheckState = CheckState.Checked;
        chkTopologicalCensorship.Location = new Point(3, 503);
        chkTopologicalCensorship.Name = "chkTopologicalCensorship";
        chkTopologicalCensorship.Size = new Size(150, 19);
        chkTopologicalCensorship.TabIndex = 21;
        chkTopologicalCensorship.Text = "Topological Censorship";
        // 
        // grpPhysicsConstants
        // 
        grpPhysicsConstants.Controls.Add(tlpPhysicsConstants);
        grpPhysicsConstants.Dock = DockStyle.Top;
        grpPhysicsConstants.Location = new Point(739, 5);
        grpPhysicsConstants.Margin = new Padding(5);
        grpPhysicsConstants.Name = "grpPhysicsConstants";
        grpPhysicsConstants.Size = new Size(592, 559);
        grpPhysicsConstants.TabIndex = 2;
        grpPhysicsConstants.TabStop = false;
        grpPhysicsConstants.Text = "RQ Physics";
        // 
        // tlpPhysicsConstants
        // 
        tlpPhysicsConstants.AutoSize = true;
        tlpPhysicsConstants.ColumnCount = 2;
        tlpPhysicsConstants.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 73.3333359F));
        tlpPhysicsConstants.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26.666666F));
        tlpPhysicsConstants.Dock = DockStyle.Fill;
        tlpPhysicsConstants.Location = new Point(3, 19);
        tlpPhysicsConstants.Name = "tlpPhysicsConstants";
        tlpPhysicsConstants.Size = new Size(586, 537);
        tlpPhysicsConstants.TabIndex = 24;
        // 
        // grpSimParams
        // 
        grpSimParams.Controls.Add(tlpSimParams);
        grpSimParams.Location = new Point(472, 5);
        grpSimParams.Margin = new Padding(5);
        grpSimParams.Name = "grpSimParams";
        grpSimParams.Size = new Size(257, 559);
        grpSimParams.TabIndex = 0;
        grpSimParams.TabStop = false;
        grpSimParams.Text = "Simulation Parameters";
        // 
        // tlpSimParams
        // 
        tlpSimParams.AutoSize = true;
        tlpSimParams.ColumnCount = 2;
        tlpSimParams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        tlpSimParams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        tlpSimParams.Controls.Add(lblNodeCount, 0, 0);
        tlpSimParams.Controls.Add(numNodeCount, 1, 0);
        tlpSimParams.Controls.Add(lblTargetDegree, 0, 1);
        tlpSimParams.Controls.Add(numTargetDegree, 1, 1);
        tlpSimParams.Controls.Add(lblInitialExcitedProb, 0, 2);
        tlpSimParams.Controls.Add(numInitialExcitedProb, 1, 2);
        tlpSimParams.Controls.Add(lblLambdaState, 0, 3);
        tlpSimParams.Controls.Add(numLambdaState, 1, 3);
        tlpSimParams.Controls.Add(lblTemperature, 0, 4);
        tlpSimParams.Controls.Add(numTemperature, 1, 4);
        tlpSimParams.Controls.Add(lblEdgeTrialProb, 0, 5);
        tlpSimParams.Controls.Add(numEdgeTrialProb, 1, 5);
        tlpSimParams.Controls.Add(lblMeasurementThreshold, 0, 6);
        tlpSimParams.Controls.Add(numMeasurementThreshold, 1, 6);
        tlpSimParams.Controls.Add(lblTotalStepsSettings, 0, 7);
        tlpSimParams.Controls.Add(numTotalSteps, 1, 7);
        tlpSimParams.Controls.Add(lblFractalLevels, 0, 8);
        tlpSimParams.Controls.Add(numFractalLevels, 1, 8);
        tlpSimParams.Controls.Add(numFractalBranchFactor, 1, 9);
        tlpSimParams.Controls.Add(lblFractalBranchFactor, 0, 9);
        tlpSimParams.Controls.Add(lblInitialEdgeProb, 0, 10);
        tlpSimParams.Controls.Add(numInitialEdgeProb, 1, 10);
        tlpSimParams.Controls.Add(lblGravitationalCoupling, 0, 11);
        tlpSimParams.Controls.Add(numGravitationalCoupling, 1, 11);
        tlpSimParams.Controls.Add(lblVacuumEnergyScale, 0, 12);
        tlpSimParams.Controls.Add(numVacuumEnergyScale, 1, 12);
        tlpSimParams.Controls.Add(lblGravityTransitionDuration, 0, 13);
        tlpSimParams.Controls.Add(numGravityTransitionDuration, 1, 13);
        tlpSimParams.Controls.Add(lblDecoherenceRate, 0, 14);
        tlpSimParams.Controls.Add(numDecoherenceRate, 1, 14);
        tlpSimParams.Controls.Add(lblHotStartTemperature, 0, 15);
        tlpSimParams.Controls.Add(numHotStartTemperature, 1, 15);
        tlpSimParams.Controls.Add(lblAdaptiveThresholdSigma, 0, 16);
        tlpSimParams.Controls.Add(numAdaptiveThresholdSigma, 1, 16);
        tlpSimParams.Controls.Add(lblWarmupDuration, 0, 17);
        tlpSimParams.Controls.Add(numWarmupDuration, 1, 17);
        tlpSimParams.Controls.Add(valAnnealingTimeConstant, 0, 18);
        tlpSimParams.Dock = DockStyle.Fill;
        tlpSimParams.Location = new Point(3, 19);
        tlpSimParams.Margin = new Padding(4, 3, 3, 3);
        tlpSimParams.Name = "tlpSimParams";
        tlpSimParams.RowCount = 20;
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        tlpSimParams.Size = new Size(251, 537);
        tlpSimParams.TabIndex = 0;
        // 
        // lblNodeCount
        // 
        lblNodeCount.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblNodeCount.AutoSize = true;
        lblNodeCount.Location = new Point(3, 2);
        lblNodeCount.Name = "lblNodeCount";
        lblNodeCount.Size = new Size(144, 15);
        lblNodeCount.TabIndex = 0;
        lblNodeCount.Text = "Node Count:";
        // 
        // numNodeCount
        // 
        numNodeCount.Dock = DockStyle.Fill;
        numNodeCount.Location = new Point(153, 3);
        numNodeCount.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
        numNodeCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        numNodeCount.Name = "numNodeCount";
        numNodeCount.Size = new Size(95, 23);
        numNodeCount.TabIndex = 1;
        numNodeCount.Value = new decimal(new int[] { 250, 0, 0, 0 });
        // 
        // lblTargetDegree
        // 
        lblTargetDegree.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblTargetDegree.AutoSize = true;
        lblTargetDegree.Location = new Point(3, 22);
        lblTargetDegree.Name = "lblTargetDegree";
        lblTargetDegree.Size = new Size(144, 15);
        lblTargetDegree.TabIndex = 2;
        lblTargetDegree.Text = "Target Degree:";
        // 
        // numTargetDegree
        // 
        numTargetDegree.Dock = DockStyle.Fill;
        numTargetDegree.Location = new Point(153, 23);
        numTargetDegree.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
        numTargetDegree.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
        numTargetDegree.Name = "numTargetDegree";
        numTargetDegree.Size = new Size(95, 23);
        numTargetDegree.TabIndex = 3;
        numTargetDegree.Value = new decimal(new int[] { 8, 0, 0, 0 });
        // 
        // lblInitialExcitedProb
        // 
        lblInitialExcitedProb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblInitialExcitedProb.AutoSize = true;
        lblInitialExcitedProb.Location = new Point(3, 42);
        lblInitialExcitedProb.Name = "lblInitialExcitedProb";
        lblInitialExcitedProb.Size = new Size(144, 15);
        lblInitialExcitedProb.TabIndex = 4;
        lblInitialExcitedProb.Text = "Initial Excited Prob:";
        // 
        // numInitialExcitedProb
        // 
        numInitialExcitedProb.DecimalPlaces = 2;
        numInitialExcitedProb.Dock = DockStyle.Fill;
        numInitialExcitedProb.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
        numInitialExcitedProb.Location = new Point(153, 43);
        numInitialExcitedProb.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
        numInitialExcitedProb.Name = "numInitialExcitedProb";
        numInitialExcitedProb.Size = new Size(95, 23);
        numInitialExcitedProb.TabIndex = 5;
        numInitialExcitedProb.Value = new decimal(new int[] { 10, 0, 0, 131072 });
        // 
        // lblLambdaState
        // 
        lblLambdaState.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblLambdaState.AutoSize = true;
        lblLambdaState.Location = new Point(3, 62);
        lblLambdaState.Name = "lblLambdaState";
        lblLambdaState.Size = new Size(144, 15);
        lblLambdaState.TabIndex = 6;
        lblLambdaState.Text = "Lambda State:";
        // 
        // numLambdaState
        // 
        numLambdaState.DecimalPlaces = 2;
        numLambdaState.Dock = DockStyle.Fill;
        numLambdaState.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
        numLambdaState.Location = new Point(153, 63);
        numLambdaState.Name = "numLambdaState";
        numLambdaState.Size = new Size(95, 23);
        numLambdaState.TabIndex = 7;
        numLambdaState.Value = new decimal(new int[] { 5, 0, 0, 65536 });
        // 
        // lblTemperature
        // 
        lblTemperature.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblTemperature.AutoSize = true;
        lblTemperature.Location = new Point(3, 82);
        lblTemperature.Name = "lblTemperature";
        lblTemperature.Size = new Size(144, 15);
        lblTemperature.TabIndex = 8;
        lblTemperature.Text = "Temperature:";
        // 
        // numTemperature
        // 
        numTemperature.DecimalPlaces = 2;
        numTemperature.Dock = DockStyle.Fill;
        numTemperature.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
        numTemperature.Location = new Point(153, 83);
        numTemperature.Name = "numTemperature";
        numTemperature.Size = new Size(95, 23);
        numTemperature.TabIndex = 9;
        numTemperature.Value = new decimal(new int[] { 100, 0, 0, 65536 });
        // 
        // lblEdgeTrialProb
        // 
        lblEdgeTrialProb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblEdgeTrialProb.AutoSize = true;
        lblEdgeTrialProb.Location = new Point(3, 102);
        lblEdgeTrialProb.Name = "lblEdgeTrialProb";
        lblEdgeTrialProb.Size = new Size(144, 15);
        lblEdgeTrialProb.TabIndex = 10;
        lblEdgeTrialProb.Text = "Edge Trial Prob:";
        // 
        // numEdgeTrialProb
        // 
        numEdgeTrialProb.DecimalPlaces = 3;
        numEdgeTrialProb.Dock = DockStyle.Fill;
        numEdgeTrialProb.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
        numEdgeTrialProb.Location = new Point(153, 103);
        numEdgeTrialProb.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
        numEdgeTrialProb.Name = "numEdgeTrialProb";
        numEdgeTrialProb.Size = new Size(95, 23);
        numEdgeTrialProb.TabIndex = 11;
        numEdgeTrialProb.Value = new decimal(new int[] { 2, 0, 0, 131072 });
        // 
        // lblMeasurementThreshold
        // 
        lblMeasurementThreshold.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblMeasurementThreshold.AutoSize = true;
        lblMeasurementThreshold.Location = new Point(3, 122);
        lblMeasurementThreshold.Name = "lblMeasurementThreshold";
        lblMeasurementThreshold.Size = new Size(144, 15);
        lblMeasurementThreshold.TabIndex = 12;
        lblMeasurementThreshold.Text = "Measurement Threshold:";
        // 
        // numMeasurementThreshold
        // 
        numMeasurementThreshold.DecimalPlaces = 3;
        numMeasurementThreshold.Dock = DockStyle.Fill;
        numMeasurementThreshold.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
        numMeasurementThreshold.Location = new Point(153, 123);
        numMeasurementThreshold.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
        numMeasurementThreshold.Name = "numMeasurementThreshold";
        numMeasurementThreshold.Size = new Size(95, 23);
        numMeasurementThreshold.TabIndex = 13;
        numMeasurementThreshold.Value = new decimal(new int[] { 30, 0, 0, 131072 });
        // 
        // lblTotalStepsSettings
        // 
        lblTotalStepsSettings.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblTotalStepsSettings.AutoSize = true;
        lblTotalStepsSettings.Location = new Point(3, 142);
        lblTotalStepsSettings.Name = "lblTotalStepsSettings";
        lblTotalStepsSettings.Size = new Size(144, 15);
        lblTotalStepsSettings.TabIndex = 14;
        lblTotalStepsSettings.Text = "Total Steps:";
        // 
        // numTotalSteps
        // 
        numTotalSteps.Dock = DockStyle.Fill;
        numTotalSteps.Increment = new decimal(new int[] { 100, 0, 0, 0 });
        numTotalSteps.Location = new Point(153, 143);
        numTotalSteps.Maximum = new decimal(new int[] { 10000000, 0, 0, 0 });
        numTotalSteps.Minimum = new decimal(new int[] { 100, 0, 0, 0 });
        numTotalSteps.Name = "numTotalSteps";
        numTotalSteps.Size = new Size(95, 23);
        numTotalSteps.TabIndex = 15;
        numTotalSteps.Value = new decimal(new int[] { 500000, 0, 0, 0 });
        // 
        // lblFractalLevels
        // 
        lblFractalLevels.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblFractalLevels.AutoSize = true;
        lblFractalLevels.Location = new Point(4, 162);
        lblFractalLevels.Margin = new Padding(4, 0, 4, 0);
        lblFractalLevels.Name = "lblFractalLevels";
        lblFractalLevels.Size = new Size(142, 15);
        lblFractalLevels.TabIndex = 16;
        lblFractalLevels.Text = "Fractal Levels:";
        // 
        // numFractalLevels
        // 
        numFractalLevels.Dock = DockStyle.Fill;
        numFractalLevels.Location = new Point(153, 163);
        numFractalLevels.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
        numFractalLevels.Name = "numFractalLevels";
        numFractalLevels.Size = new Size(95, 23);
        numFractalLevels.TabIndex = 17;
        // 
        // numFractalBranchFactor
        // 
        numFractalBranchFactor.Dock = DockStyle.Fill;
        numFractalBranchFactor.Location = new Point(153, 183);
        numFractalBranchFactor.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
        numFractalBranchFactor.Name = "numFractalBranchFactor";
        numFractalBranchFactor.Size = new Size(95, 23);
        numFractalBranchFactor.TabIndex = 19;
        // 
        // lblFractalBranchFactor
        // 
        lblFractalBranchFactor.AutoSize = true;
        lblFractalBranchFactor.Location = new Point(3, 180);
        lblFractalBranchFactor.Name = "lblFractalBranchFactor";
        lblFractalBranchFactor.Size = new Size(121, 15);
        lblFractalBranchFactor.TabIndex = 18;
        lblFractalBranchFactor.Text = "Fractal Branch Factor:";
        // 
        // lblInitialEdgeProb
        // 
        lblInitialEdgeProb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblInitialEdgeProb.AutoSize = true;
        lblInitialEdgeProb.Location = new Point(3, 202);
        lblInitialEdgeProb.Name = "lblInitialEdgeProb";
        lblInitialEdgeProb.Size = new Size(144, 15);
        lblInitialEdgeProb.TabIndex = 0;
        lblInitialEdgeProb.Text = "Initial Edge Prob:";
        // 
        // numInitialEdgeProb
        // 
        numInitialEdgeProb.DecimalPlaces = 4;
        numInitialEdgeProb.Dock = DockStyle.Fill;
        numInitialEdgeProb.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
        numInitialEdgeProb.Location = new Point(153, 203);
        numInitialEdgeProb.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
        numInitialEdgeProb.Name = "numInitialEdgeProb";
        numInitialEdgeProb.Size = new Size(95, 23);
        numInitialEdgeProb.TabIndex = 1;
        numInitialEdgeProb.Value = new decimal(new int[] { 35, 0, 0, 196608 });
        // 
        // lblGravitationalCoupling
        // 
        lblGravitationalCoupling.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblGravitationalCoupling.AutoSize = true;
        lblGravitationalCoupling.Location = new Point(3, 220);
        lblGravitationalCoupling.Name = "lblGravitationalCoupling";
        lblGravitationalCoupling.Size = new Size(144, 20);
        lblGravitationalCoupling.TabIndex = 2;
        lblGravitationalCoupling.Text = "Gravitational Coupling (G):";
        // 
        // numGravitationalCoupling
        // 
        numGravitationalCoupling.DecimalPlaces = 4;
        numGravitationalCoupling.Dock = DockStyle.Fill;
        numGravitationalCoupling.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
        numGravitationalCoupling.Location = new Point(153, 223);
        numGravitationalCoupling.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
        numGravitationalCoupling.Name = "numGravitationalCoupling";
        numGravitationalCoupling.Size = new Size(95, 23);
        numGravitationalCoupling.TabIndex = 3;
        numGravitationalCoupling.Value = new decimal(new int[] { 10, 0, 0, 196608 });
        // 
        // lblVacuumEnergyScale
        // 
        lblVacuumEnergyScale.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblVacuumEnergyScale.AutoSize = true;
        lblVacuumEnergyScale.Location = new Point(3, 242);
        lblVacuumEnergyScale.Name = "lblVacuumEnergyScale";
        lblVacuumEnergyScale.Size = new Size(144, 15);
        lblVacuumEnergyScale.TabIndex = 4;
        lblVacuumEnergyScale.Text = "Vacuum Energy Scale:";
        // 
        // numVacuumEnergyScale
        // 
        numVacuumEnergyScale.DecimalPlaces = 4;
        numVacuumEnergyScale.Dock = DockStyle.Fill;
        numVacuumEnergyScale.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
        numVacuumEnergyScale.Location = new Point(153, 243);
        numVacuumEnergyScale.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
        numVacuumEnergyScale.Name = "numVacuumEnergyScale";
        numVacuumEnergyScale.Size = new Size(95, 23);
        numVacuumEnergyScale.TabIndex = 5;
        numVacuumEnergyScale.Value = new decimal(new int[] { 5, 0, 0, 327680 });
        // 
        // lblGravityTransitionDuration
        // 
        lblGravityTransitionDuration.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblGravityTransitionDuration.AutoSize = true;
        lblGravityTransitionDuration.Location = new Point(3, 262);
        lblGravityTransitionDuration.Name = "lblGravityTransitionDuration";
        lblGravityTransitionDuration.Size = new Size(144, 15);
        lblGravityTransitionDuration.TabIndex = 16;
        lblGravityTransitionDuration.Text = "Gravity Transition (1/?):";
        // 
        // numGravityTransitionDuration
        // 
        numGravityTransitionDuration.DecimalPlaces = 1;
        numGravityTransitionDuration.Dock = DockStyle.Fill;
        numGravityTransitionDuration.Increment = new decimal(new int[] { 10, 0, 0, 0 });
        numGravityTransitionDuration.Location = new Point(153, 263);
        numGravityTransitionDuration.Maximum = new decimal(new int[] { 500, 0, 0, 0 });
        numGravityTransitionDuration.Name = "numGravityTransitionDuration";
        numGravityTransitionDuration.Size = new Size(95, 23);
        numGravityTransitionDuration.TabIndex = 18;
        numGravityTransitionDuration.Value = new decimal(new int[] { 137, 0, 0, 0 });
        // 
        // lblDecoherenceRate
        // 
        lblDecoherenceRate.Anchor = AnchorStyles.Left;
        lblDecoherenceRate.AutoSize = true;
        lblDecoherenceRate.Location = new Point(3, 282);
        lblDecoherenceRate.Name = "lblDecoherenceRate";
        lblDecoherenceRate.Size = new Size(105, 15);
        lblDecoherenceRate.TabIndex = 8;
        lblDecoherenceRate.Text = "Decoherence Rate:";
        // 
        // numDecoherenceRate
        // 
        numDecoherenceRate.DecimalPlaces = 4;
        numDecoherenceRate.Dock = DockStyle.Fill;
        numDecoherenceRate.Increment = new decimal(new int[] { 1, 0, 0, 262144 });
        numDecoherenceRate.Location = new Point(153, 283);
        numDecoherenceRate.Maximum = new decimal(new int[] { 1, 0, 0, 65536 });
        numDecoherenceRate.Name = "numDecoherenceRate";
        numDecoherenceRate.Size = new Size(95, 23);
        numDecoherenceRate.TabIndex = 9;
        numDecoherenceRate.Value = new decimal(new int[] { 5, 0, 0, 196608 });
        // 
        // lblHotStartTemperature
        // 
        lblHotStartTemperature.Anchor = AnchorStyles.Left;
        lblHotStartTemperature.AutoSize = true;
        lblHotStartTemperature.Location = new Point(3, 302);
        lblHotStartTemperature.Name = "lblHotStartTemperature";
        lblHotStartTemperature.Size = new Size(127, 15);
        lblHotStartTemperature.TabIndex = 10;
        lblHotStartTemperature.Text = "Hot Start Temperature:";
        // 
        // numHotStartTemperature
        // 
        numHotStartTemperature.DecimalPlaces = 1;
        numHotStartTemperature.Dock = DockStyle.Fill;
        numHotStartTemperature.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
        numHotStartTemperature.Location = new Point(153, 303);
        numHotStartTemperature.Name = "numHotStartTemperature";
        numHotStartTemperature.Size = new Size(95, 23);
        numHotStartTemperature.TabIndex = 11;
        numHotStartTemperature.Value = new decimal(new int[] { 6, 0, 0, 0 });
        // 
        // lblAdaptiveThresholdSigma
        // 
        lblAdaptiveThresholdSigma.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblAdaptiveThresholdSigma.AutoSize = true;
        lblAdaptiveThresholdSigma.Location = new Point(3, 322);
        lblAdaptiveThresholdSigma.Name = "lblAdaptiveThresholdSigma";
        lblAdaptiveThresholdSigma.Size = new Size(144, 15);
        lblAdaptiveThresholdSigma.TabIndex = 12;
        lblAdaptiveThresholdSigma.Text = "Adaptive Threshold ?:";
        // 
        // numAdaptiveThresholdSigma
        // 
        numAdaptiveThresholdSigma.DecimalPlaces = 2;
        numAdaptiveThresholdSigma.Dock = DockStyle.Fill;
        numAdaptiveThresholdSigma.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
        numAdaptiveThresholdSigma.Location = new Point(153, 323);
        numAdaptiveThresholdSigma.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
        numAdaptiveThresholdSigma.Name = "numAdaptiveThresholdSigma";
        numAdaptiveThresholdSigma.Size = new Size(95, 23);
        numAdaptiveThresholdSigma.TabIndex = 13;
        numAdaptiveThresholdSigma.Value = new decimal(new int[] { 15, 0, 0, 65536 });
        // 
        // lblWarmupDuration
        // 
        lblWarmupDuration.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblWarmupDuration.AutoSize = true;
        lblWarmupDuration.Location = new Point(3, 342);
        lblWarmupDuration.Name = "lblWarmupDuration";
        lblWarmupDuration.Size = new Size(144, 15);
        lblWarmupDuration.TabIndex = 14;
        lblWarmupDuration.Text = "Warmup Duration:";
        // 
        // numWarmupDuration
        // 
        numWarmupDuration.Dock = DockStyle.Fill;
        numWarmupDuration.Increment = new decimal(new int[] { 10, 0, 0, 0 });
        numWarmupDuration.Location = new Point(153, 343);
        numWarmupDuration.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
        numWarmupDuration.Name = "numWarmupDuration";
        numWarmupDuration.Size = new Size(95, 23);
        numWarmupDuration.TabIndex = 15;
        numWarmupDuration.Value = new decimal(new int[] { 200, 0, 0, 0 });
        // 
        // valAnnealingTimeConstant
        // 
        valAnnealingTimeConstant.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        valAnnealingTimeConstant.AutoSize = true;
        tlpSimParams.SetColumnSpan(valAnnealingTimeConstant, 2);
        valAnnealingTimeConstant.Location = new Point(3, 360);
        valAnnealingTimeConstant.Name = "valAnnealingTimeConstant";
        valAnnealingTimeConstant.Size = new Size(245, 15);
        valAnnealingTimeConstant.TabIndex = 22;
        valAnnealingTimeConstant.Text = "?_anneal = (computed)";
        // 
        // tabPage_UniPipelineState
        // 
        tabPage_UniPipelineState.Controls.Add(groupBox_MultiGpu_Settings);
        tabPage_UniPipelineState.Controls.Add(_tlp_UniPipeline_Main);
        tabPage_UniPipelineState.Location = new Point(4, 24);
        tabPage_UniPipelineState.Name = "tabPage_UniPipelineState";
        tabPage_UniPipelineState.Size = new Size(1342, 790);
        tabPage_UniPipelineState.TabIndex = 19;
        tabPage_UniPipelineState.Text = "Uni-Pipeline";
        tabPage_UniPipelineState.UseVisualStyleBackColor = true;
        // 
        // groupBox_MultiGpu_Settings
        // 
        groupBox_MultiGpu_Settings.Controls.Add(button_RemoveGpuBackgroundPluginToPipeline);
        groupBox_MultiGpu_Settings.Controls.Add(button_AddGpuBackgroundPluginToPipeline);
        groupBox_MultiGpu_Settings.Controls.Add(label_BackgroundPipelineGPU);
        groupBox_MultiGpu_Settings.Controls.Add(comboBox_BackgroundPipelineGPU);
        groupBox_MultiGpu_Settings.Controls.Add(label_BackgroundPipelineGPU_Kernels);
        groupBox_MultiGpu_Settings.Controls.Add(numericUpDown_BackgroundPluginGPUKernels);
        groupBox_MultiGpu_Settings.Controls.Add(listView_AnaliticsGPU);
        groupBox_MultiGpu_Settings.Controls.Add(label_RenderingGPU);
        groupBox_MultiGpu_Settings.Controls.Add(checkBox_UseMultiGPU);
        groupBox_MultiGpu_Settings.Controls.Add(label_MultiGPU_ActivePhysxGPU);
        groupBox_MultiGpu_Settings.Controls.Add(label_GPUMode);
        groupBox_MultiGpu_Settings.Controls.Add(checkBox_EnableGPU);
        groupBox_MultiGpu_Settings.Controls.Add(comboBox_GPUComputeEngine);
        groupBox_MultiGpu_Settings.Controls.Add(comboBox_3DRenderingGPU);
        groupBox_MultiGpu_Settings.Controls.Add(comboBox_MultiGpu_PhysicsGPU);
        groupBox_MultiGpu_Settings.Location = new Point(952, 8);
        groupBox_MultiGpu_Settings.Name = "groupBox_MultiGpu_Settings";
        groupBox_MultiGpu_Settings.Size = new Size(387, 644);
        groupBox_MultiGpu_Settings.TabIndex = 34;
        groupBox_MultiGpu_Settings.TabStop = false;
        groupBox_MultiGpu_Settings.Text = "GPU \\ Multi-GPU Settings";
        // 
        // button_RemoveGpuBackgroundPluginToPipeline
        // 
        button_RemoveGpuBackgroundPluginToPipeline.Location = new Point(126, 604);
        button_RemoveGpuBackgroundPluginToPipeline.Name = "button_RemoveGpuBackgroundPluginToPipeline";
        button_RemoveGpuBackgroundPluginToPipeline.Size = new Size(114, 22);
        button_RemoveGpuBackgroundPluginToPipeline.TabIndex = 45;
        button_RemoveGpuBackgroundPluginToPipeline.Text = "Remove Plugin";
        button_RemoveGpuBackgroundPluginToPipeline.UseVisualStyleBackColor = true;
        button_RemoveGpuBackgroundPluginToPipeline.Click += button_RemoveGpuBackgroundPluginToPipeline_Click;
        // 
        // button_AddGpuBackgroundPluginToPipeline
        // 
        button_AddGpuBackgroundPluginToPipeline.Location = new Point(6, 604);
        button_AddGpuBackgroundPluginToPipeline.Name = "button_AddGpuBackgroundPluginToPipeline";
        button_AddGpuBackgroundPluginToPipeline.Size = new Size(114, 22);
        button_AddGpuBackgroundPluginToPipeline.TabIndex = 44;
        button_AddGpuBackgroundPluginToPipeline.Text = "Add to Pipeline";
        button_AddGpuBackgroundPluginToPipeline.UseVisualStyleBackColor = true;
        button_AddGpuBackgroundPluginToPipeline.Click += button_AddGpuBackgroundPluginToPipeline_Click;
        // 
        // label_BackgroundPipelineGPU
        // 
        label_BackgroundPipelineGPU.AutoSize = true;
        label_BackgroundPipelineGPU.Location = new Point(5, 161);
        label_BackgroundPipelineGPU.Name = "label_BackgroundPipelineGPU";
        label_BackgroundPipelineGPU.Size = new Size(145, 15);
        label_BackgroundPipelineGPU.TabIndex = 43;
        label_BackgroundPipelineGPU.Text = "Background Pipeline GPU:";
        // 
        // comboBox_BackgroundPipelineGPU
        // 
        comboBox_BackgroundPipelineGPU.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox_BackgroundPipelineGPU.Location = new Point(7, 179);
        comboBox_BackgroundPipelineGPU.Name = "comboBox_BackgroundPipelineGPU";
        comboBox_BackgroundPipelineGPU.Size = new Size(232, 23);
        comboBox_BackgroundPipelineGPU.TabIndex = 42;
        // 
        // label_BackgroundPipelineGPU_Kernels
        // 
        label_BackgroundPipelineGPU_Kernels.AutoSize = true;
        label_BackgroundPipelineGPU_Kernels.Location = new Point(242, 161);
        label_BackgroundPipelineGPU_Kernels.Name = "label_BackgroundPipelineGPU_Kernels";
        label_BackgroundPipelineGPU_Kernels.Size = new Size(74, 15);
        label_BackgroundPipelineGPU_Kernels.TabIndex = 41;
        label_BackgroundPipelineGPU_Kernels.Text = "GPU Kernels:";
        // 
        // numericUpDown_BackgroundPluginGPUKernels
        // 
        numericUpDown_BackgroundPluginGPUKernels.Location = new Point(245, 179);
        numericUpDown_BackgroundPluginGPUKernels.Maximum = new decimal(new int[] { 10000000, 0, 0, 0 });
        numericUpDown_BackgroundPluginGPUKernels.Minimum = new decimal(new int[] { 10000, 0, 0, 0 });
        numericUpDown_BackgroundPluginGPUKernels.Name = "numericUpDown_BackgroundPluginGPUKernels";
        numericUpDown_BackgroundPluginGPUKernels.Size = new Size(131, 23);
        numericUpDown_BackgroundPluginGPUKernels.TabIndex = 40;
        numericUpDown_BackgroundPluginGPUKernels.Value = new decimal(new int[] { 10000, 0, 0, 0 });
        // 
        // listView_AnaliticsGPU
        // 
        listView_AnaliticsGPU.CheckBoxes = true;
        listView_AnaliticsGPU.Columns.AddRange(new ColumnHeader[] { columnHeader_GPU, columnHeader_Algorithm, columnHeader_GPUKernels });
        listView_AnaliticsGPU.FullRowSelect = true;
        listView_AnaliticsGPU.GridLines = true;
        listViewGroup1.Header = "GPU";
        listViewGroup1.Name = "listViewGroup_GPU";
        listView_AnaliticsGPU.Groups.AddRange(new ListViewGroup[] { listViewGroup1 });
        listView_AnaliticsGPU.Location = new Point(7, 208);
        listView_AnaliticsGPU.Name = "listView_AnaliticsGPU";
        listView_AnaliticsGPU.Size = new Size(369, 381);
        listView_AnaliticsGPU.TabIndex = 39;
        listView_AnaliticsGPU.UseCompatibleStateImageBehavior = false;
        listView_AnaliticsGPU.View = View.Details;
        // 
        // columnHeader_GPU
        // 
        columnHeader_GPU.Text = "GPU";
        // 
        // columnHeader_Algorithm
        // 
        columnHeader_Algorithm.Text = "Algorithm\\Plugin";
        columnHeader_Algorithm.Width = 200;
        // 
        // columnHeader_GPUKernels
        // 
        columnHeader_GPUKernels.Text = "Kernels\\Threads";
        // 
        // label_RenderingGPU
        // 
        label_RenderingGPU.AutoSize = true;
        label_RenderingGPU.Location = new Point(7, 25);
        label_RenderingGPU.Name = "label_RenderingGPU";
        label_RenderingGPU.Size = new Size(107, 15);
        label_RenderingGPU.TabIndex = 38;
        label_RenderingGPU.Text = "3D Rendering GPU:";
        // 
        // checkBox_UseMultiGPU
        // 
        checkBox_UseMultiGPU.AutoSize = true;
        checkBox_UseMultiGPU.Location = new Point(8, 129);
        checkBox_UseMultiGPU.Name = "checkBox_UseMultiGPU";
        checkBox_UseMultiGPU.Size = new Size(120, 19);
        checkBox_UseMultiGPU.TabIndex = 31;
        checkBox_UseMultiGPU.Text = "Multi GPU Cluster";
        checkBox_UseMultiGPU.CheckedChanged += checkBox_UseMultiGPU_CheckedChanged;
        // 
        // label_MultiGPU_ActivePhysxGPU
        // 
        label_MultiGPU_ActivePhysxGPU.AutoSize = true;
        label_MultiGPU_ActivePhysxGPU.Location = new Point(7, 76);
        label_MultiGPU_ActivePhysxGPU.Name = "label_MultiGPU_ActivePhysxGPU";
        label_MultiGPU_ActivePhysxGPU.Size = new Size(141, 15);
        label_MultiGPU_ActivePhysxGPU.TabIndex = 37;
        label_MultiGPU_ActivePhysxGPU.Text = "Graph atomic physx GPU:";
        // 
        // label_GPUMode
        // 
        label_GPUMode.AutoSize = true;
        label_GPUMode.Location = new Point(243, 76);
        label_GPUMode.Name = "label_GPUMode";
        label_GPUMode.Size = new Size(120, 15);
        label_GPUMode.TabIndex = 30;
        label_GPUMode.Text = "GPU Compute Mode:";
        // 
        // checkBox_EnableGPU
        // 
        checkBox_EnableGPU.AutoSize = true;
        checkBox_EnableGPU.Checked = true;
        checkBox_EnableGPU.CheckState = CheckState.Checked;
        checkBox_EnableGPU.Location = new Point(246, 41);
        checkBox_EnableGPU.Name = "checkBox_EnableGPU";
        checkBox_EnableGPU.Size = new Size(87, 19);
        checkBox_EnableGPU.TabIndex = 28;
        checkBox_EnableGPU.Text = "Enable GPU";
        checkBox_EnableGPU.CheckedChanged += checkBox_EnableGPU_CheckedChanged;
        // 
        // comboBox_GPUComputeEngine
        // 
        comboBox_GPUComputeEngine.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox_GPUComputeEngine.Items.AddRange(new object[] { "Auto", "Original (Dense GPU)", "CSR (Sparse GPU)", "CPU Only" });
        comboBox_GPUComputeEngine.Location = new Point(245, 93);
        comboBox_GPUComputeEngine.Name = "comboBox_GPUComputeEngine";
        comboBox_GPUComputeEngine.Size = new Size(131, 23);
        comboBox_GPUComputeEngine.TabIndex = 29;
        // 
        // comboBox_3DRenderingGPU
        // 
        comboBox_3DRenderingGPU.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox_3DRenderingGPU.Items.AddRange(new object[] { "All (w>0.0)", "w>0.15", "w>0.3", "w>0.5", "w>0.7", "w>0.9" });
        comboBox_3DRenderingGPU.Location = new Point(8, 42);
        comboBox_3DRenderingGPU.Name = "comboBox_3DRenderingGPU";
        comboBox_3DRenderingGPU.Size = new Size(232, 23);
        comboBox_3DRenderingGPU.TabIndex = 15;
        // 
        // comboBox_MultiGpu_PhysicsGPU
        // 
        comboBox_MultiGpu_PhysicsGPU.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox_MultiGpu_PhysicsGPU.Items.AddRange(new object[] { "All (w>0.0)", "w>0.15", "w>0.3", "w>0.5", "w>0.7", "w>0.9" });
        comboBox_MultiGpu_PhysicsGPU.Location = new Point(7, 93);
        comboBox_MultiGpu_PhysicsGPU.Name = "comboBox_MultiGpu_PhysicsGPU";
        comboBox_MultiGpu_PhysicsGPU.Size = new Size(232, 23);
        comboBox_MultiGpu_PhysicsGPU.TabIndex = 32;
        // 
        // _tlp_UniPipeline_Main
        // 
        _tlp_UniPipeline_Main.ColumnCount = 2;
        _tlp_UniPipeline_Main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
        _tlp_UniPipeline_Main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        _tlp_UniPipeline_Main.Controls.Add(_tlpLeft, 0, 0);
        _tlp_UniPipeline_Main.Controls.Add(_grpProperties, 1, 0);
        _tlp_UniPipeline_Main.Controls.Add(_flpGpuTopologySettings, 0, 1);
        _tlp_UniPipeline_Main.Controls.Add(_flpDialogButtons, 1, 1);
        _tlp_UniPipeline_Main.Location = new Point(8, 8);
        _tlp_UniPipeline_Main.Margin = new Padding(3, 2, 3, 2);
        _tlp_UniPipeline_Main.Name = "_tlp_UniPipeline_Main";
        _tlp_UniPipeline_Main.RowCount = 2;
        _tlp_UniPipeline_Main.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _tlp_UniPipeline_Main.RowStyles.Add(new RowStyle());
        _tlp_UniPipeline_Main.Size = new Size(938, 703);
        _tlp_UniPipeline_Main.TabIndex = 1;
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
        _tlpLeft.Size = new Size(603, 649);
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
        _dgvModules.Size = new Size(597, 611);
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
        _flpButtons.Location = new Point(3, 619);
        _flpButtons.Margin = new Padding(3, 4, 3, 2);
        _flpButtons.Name = "_flpButtons";
        _flpButtons.Size = new Size(597, 28);
        _flpButtons.TabIndex = 1;
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
        _btnAddBuiltIn.Click += _btnAddBuiltIn_Click;
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
        _grpProperties.Location = new Point(614, 2);
        _grpProperties.Margin = new Padding(5, 2, 3, 2);
        _grpProperties.Name = "_grpProperties";
        _grpProperties.Padding = new Padding(7, 6, 7, 6);
        _grpProperties.Size = new Size(321, 649);
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
        _tlpProperties.Size = new Size(307, 621);
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
        _txtModuleName.Size = new Size(225, 23);
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
        _txtDescription.Size = new Size(225, 61);
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
        _cmbExecutionType.Size = new Size(225, 23);
        _cmbExecutionType.TabIndex = 5;
        // 
        // _flpGpuTopologySettings
        // 
        _flpGpuTopologySettings.AutoSize = true;
        _flpGpuTopologySettings.Controls.Add(label_GpuEngineUniPipeline);
        _flpGpuTopologySettings.Controls.Add(comboBox_GpuEngineUniPipeline);
        _flpGpuTopologySettings.Controls.Add(label_TopologyMode);
        _flpGpuTopologySettings.Controls.Add(comboBox_TopologyMode);
        _flpGpuTopologySettings.Dock = DockStyle.Fill;
        _flpGpuTopologySettings.Location = new Point(3, 657);
        _flpGpuTopologySettings.Margin = new Padding(3, 4, 3, 2);
        _flpGpuTopologySettings.Name = "_flpGpuTopologySettings";
        _flpGpuTopologySettings.Size = new Size(603, 44);
        _flpGpuTopologySettings.TabIndex = 3;
        // 
        // label_GpuEngineUniPipeline
        // 
        label_GpuEngineUniPipeline.AutoSize = true;
        label_GpuEngineUniPipeline.Location = new Point(3, 0);
        label_GpuEngineUniPipeline.Name = "label_GpuEngineUniPipeline";
        label_GpuEngineUniPipeline.Size = new Size(72, 15);
        label_GpuEngineUniPipeline.TabIndex = 0;
        label_GpuEngineUniPipeline.Text = "GPU Engine:";
        // 
        // comboBox_GpuEngineUniPipeline
        // 
        comboBox_GpuEngineUniPipeline.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox_GpuEngineUniPipeline.Items.AddRange(new object[] { "Auto (Recommend)", "Original (Dense)", "CSR (Sparse)", "CPU Only" });
        comboBox_GpuEngineUniPipeline.Location = new Point(81, 3);
        comboBox_GpuEngineUniPipeline.Name = "comboBox_GpuEngineUniPipeline";
        comboBox_GpuEngineUniPipeline.Size = new Size(140, 23);
        comboBox_GpuEngineUniPipeline.TabIndex = 1;
        // 
        // label_TopologyMode
        // 
        label_TopologyMode.AutoSize = true;
        label_TopologyMode.Location = new Point(227, 0);
        label_TopologyMode.Name = "label_TopologyMode";
        label_TopologyMode.Size = new Size(94, 15);
        label_TopologyMode.TabIndex = 2;
        label_TopologyMode.Text = "Topology Mode:";
        // 
        // comboBox_TopologyMode
        // 
        comboBox_TopologyMode.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox_TopologyMode.Items.AddRange(new object[] { "CSR (Static)", "StreamCompaction (Hybrid)", "StreamCompaction (Full GPU)", "Dynamic Hard Rewiring" });
        comboBox_TopologyMode.Location = new Point(327, 3);
        comboBox_TopologyMode.Name = "comboBox_TopologyMode";
        comboBox_TopologyMode.Size = new Size(180, 23);
        comboBox_TopologyMode.TabIndex = 3;
        // 
        // _flpDialogButtons
        // 
        _flpDialogButtons.Location = new Point(612, 656);
        _flpDialogButtons.Name = "_flpDialogButtons";
        _flpDialogButtons.Size = new Size(200, 44);
        _flpDialogButtons.TabIndex = 4;
        // 
        // tabPage_Visualization
        // 
        tabPage_Visualization.Location = new Point(4, 24);
        tabPage_Visualization.Name = "tabPage_Visualization";
        tabPage_Visualization.Size = new Size(1342, 790);
        tabPage_Visualization.TabIndex = 20;
        tabPage_Visualization.Text = "Visualization";
        tabPage_Visualization.UseVisualStyleBackColor = true;
        // 
        // tabPage_Telemetry
        // 
        tabPage_Telemetry.Location = new Point(4, 24);
        tabPage_Telemetry.Name = "tabPage_Telemetry";
        tabPage_Telemetry.Size = new Size(1342, 790);
        tabPage_Telemetry.TabIndex = 21;
        tabPage_Telemetry.Text = "Telemetry";
        tabPage_Telemetry.UseVisualStyleBackColor = true;
        // 
        // checkBox_ScienceSimMode
        // 
        checkBox_ScienceSimMode.AutoSize = true;
        checkBox_ScienceSimMode.Location = new Point(852, 19);
        checkBox_ScienceSimMode.Name = "checkBox_ScienceSimMode";
        checkBox_ScienceSimMode.Size = new Size(100, 19);
        checkBox_ScienceSimMode.TabIndex = 31;
        checkBox_ScienceSimMode.Text = "Science Mode";
        checkBox_ScienceSimMode.CheckedChanged += checkBox_ScienceSimMode_CheckedChanged;
        // 
        // comboBox_GPUIndex
        // 
        comboBox_GPUIndex.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox_GPUIndex.Items.AddRange(new object[] { "All (w>0.0)", "w>0.15", "w>0.3", "w>0.5", "w>0.7", "w>0.9" });
        comboBox_GPUIndex.Location = new Point(8, 42);
        comboBox_GPUIndex.Name = "comboBox_GPUIndex";
        comboBox_GPUIndex.Size = new Size(232, 23);
        comboBox_GPUIndex.TabIndex = 15;
        // 
        // _btnCancel
        // 
        _btnCancel.Location = new Point(0, 0);
        _btnCancel.Name = "_btnCancel";
        _btnCancel.Size = new Size(75, 23);
        _btnCancel.TabIndex = 0;
        // 
        // _btnApply
        // 
        _btnApply.Location = new Point(0, 0);
        _btnApply.Name = "_btnApply";
        _btnApply.Size = new Size(75, 23);
        _btnApply.TabIndex = 0;
        // 
        // _btnOK
        // 
        _btnOK.Location = new Point(0, 0);
        _btnOK.Name = "_btnOK";
        _btnOK.Size = new Size(75, 23);
        _btnOK.TabIndex = 0;
        // 
        // button_ApplyPipelineConfSet
        // 
        button_ApplyPipelineConfSet.AutoSize = true;
        button_ApplyPipelineConfSet.Location = new Point(414, 12);
        button_ApplyPipelineConfSet.Name = "button_ApplyPipelineConfSet";
        button_ApplyPipelineConfSet.Size = new Size(132, 25);
        button_ApplyPipelineConfSet.TabIndex = 28;
        button_ApplyPipelineConfSet.Text = "Apply Plugins\\Set";
        button_ApplyPipelineConfSet.Click += button_ApplyPipelineConfSet_Click;
        // 
        // label_CPUThreads
        // 
        label_CPUThreads.AutoSize = true;
        label_CPUThreads.Location = new Point(1211, 21);
        label_CPUThreads.Name = "label_CPUThreads";
        label_CPUThreads.Size = new Size(75, 15);
        label_CPUThreads.TabIndex = 25;
        label_CPUThreads.Text = "CPU threads:";
        // 
        // numericUpDown1
        // 
        numericUpDown1.Location = new Point(1291, 12);
        numericUpDown1.Maximum = new decimal(new int[] { 1024, 0, 0, 0 });
        numericUpDown1.Minimum = new decimal(new int[] { 8, 0, 0, 0 });
        numericUpDown1.Name = "numericUpDown1";
        numericUpDown1.Size = new Size(56, 23);
        numericUpDown1.TabIndex = 24;
        numericUpDown1.Value = new decimal(new int[] { 8, 0, 0, 0 });
        // 
        // button_Plugins
        // 
        button_Plugins.AutoSize = true;
        button_Plugins.Location = new Point(276, 12);
        button_Plugins.Name = "button_Plugins";
        button_Plugins.Size = new Size(132, 25);
        button_Plugins.TabIndex = 26;
        button_Plugins.Text = "Plugins";
        button_Plugins.Click += button_Plugins_Click;
        // 
        // button_TerminateSimSession
        // 
        button_TerminateSimSession.AutoSize = true;
        button_TerminateSimSession.Location = new Point(138, 12);
        button_TerminateSimSession.Name = "button_TerminateSimSession";
        button_TerminateSimSession.Size = new Size(132, 25);
        button_TerminateSimSession.TabIndex = 35;
        button_TerminateSimSession.Text = "Terminate Sim";
        button_TerminateSimSession.Click += button_TerminateSimSession_Click;
        // 
        // checkBox_StanaloneDX12Form
        // 
        checkBox_StanaloneDX12Form.AutoSize = true;
        checkBox_StanaloneDX12Form.Location = new Point(1049, 19);
        checkBox_StanaloneDX12Form.Name = "checkBox_StanaloneDX12Form";
        checkBox_StanaloneDX12Form.Size = new Size(143, 19);
        checkBox_StanaloneDX12Form.TabIndex = 34;
        checkBox_StanaloneDX12Form.Text = "standalone Dx12 Form";
        checkBox_StanaloneDX12Form.CheckedChanged += checkBox_StanaloneDX12Form_CheckedChanged;
        // 
        // button_BindConsoleSession
        // 
        button_BindConsoleSession.AutoSize = true;
        button_BindConsoleSession.Location = new Point(552, 12);
        button_BindConsoleSession.Name = "button_BindConsoleSession";
        button_BindConsoleSession.Size = new Size(146, 25);
        button_BindConsoleSession.TabIndex = 33;
        button_BindConsoleSession.Text = "Bind Console Session";
        button_BindConsoleSession.Click += button_BindConsoleSession_Click;
        // 
        // checkBox_AutoTuning
        // 
        checkBox_AutoTuning.AutoSize = true;
        checkBox_AutoTuning.Location = new Point(716, 19);
        checkBox_AutoTuning.Name = "checkBox_AutoTuning";
        checkBox_AutoTuning.Size = new Size(134, 19);
        checkBox_AutoTuning.TabIndex = 32;
        checkBox_AutoTuning.Text = "Auto-tuning params";
        checkBox_AutoTuning.CheckedChanged += checkBox_AutoTuning_CheckedChanged;
        // 
        // button_RunModernSim
        // 
        button_RunModernSim.AutoSize = true;
        button_RunModernSim.Location = new Point(1, 12);
        button_RunModernSim.Name = "button_RunModernSim";
        button_RunModernSim.Size = new Size(132, 25);
        button_RunModernSim.TabIndex = 31;
        button_RunModernSim.Text = "Run simulation";
        button_RunModernSim.Click += button_RunModernSim_Click;
        // 
        // Form_Main_RqSim
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        ClientSize = new Size(1359, 861);
        Controls.Add(button_TerminateSimSession);
        Controls.Add(checkBox_StanaloneDX12Form);
        Controls.Add(button_BindConsoleSession);
        Controls.Add(checkBox_AutoTuning);
        Controls.Add(checkBox_ScienceSimMode);
        Controls.Add(button_RunModernSim);
        Controls.Add(button_Plugins);
        Controls.Add(label_CPUThreads);
        Controls.Add(numericUpDown1);
        Controls.Add(tabControl_Main);
        Controls.Add(button_ApplyPipelineConfSet);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximumSize = new Size(1375, 900);
        Name = "Form_Main_RqSim";
        Text = "RQ-Sim";
        Load += Form_Main_Load;
        tabControl_Main.ResumeLayout(false);
        tabPage_Settings.ResumeLayout(false);
        tabPage_Settings.PerformLayout();
        settingsMainLayout.ResumeLayout(false);
        grpPhysicsModules.ResumeLayout(false);
        flpPhysics.ResumeLayout(false);
        flpPhysics.PerformLayout();
        grpPhysicsConstants.ResumeLayout(false);
        grpPhysicsConstants.PerformLayout();
        grpSimParams.ResumeLayout(false);
        grpSimParams.PerformLayout();
        tlpSimParams.ResumeLayout(false);
        tlpSimParams.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)numNodeCount).EndInit();
        ((System.ComponentModel.ISupportInitialize)numTargetDegree).EndInit();
        ((System.ComponentModel.ISupportInitialize)numInitialExcitedProb).EndInit();
        ((System.ComponentModel.ISupportInitialize)numLambdaState).EndInit();
        ((System.ComponentModel.ISupportInitialize)numTemperature).EndInit();
        ((System.ComponentModel.ISupportInitialize)numEdgeTrialProb).EndInit();
        ((System.ComponentModel.ISupportInitialize)numMeasurementThreshold).EndInit();
        ((System.ComponentModel.ISupportInitialize)numTotalSteps).EndInit();
        ((System.ComponentModel.ISupportInitialize)numFractalLevels).EndInit();
        ((System.ComponentModel.ISupportInitialize)numFractalBranchFactor).EndInit();
        ((System.ComponentModel.ISupportInitialize)numInitialEdgeProb).EndInit();
        ((System.ComponentModel.ISupportInitialize)numGravitationalCoupling).EndInit();
        ((System.ComponentModel.ISupportInitialize)numVacuumEnergyScale).EndInit();
        ((System.ComponentModel.ISupportInitialize)numGravityTransitionDuration).EndInit();
        ((System.ComponentModel.ISupportInitialize)numDecoherenceRate).EndInit();
        ((System.ComponentModel.ISupportInitialize)numHotStartTemperature).EndInit();
        ((System.ComponentModel.ISupportInitialize)numAdaptiveThresholdSigma).EndInit();
        ((System.ComponentModel.ISupportInitialize)numWarmupDuration).EndInit();
        tabPage_UniPipelineState.ResumeLayout(false);
        groupBox_MultiGpu_Settings.ResumeLayout(false);
        groupBox_MultiGpu_Settings.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)numericUpDown_BackgroundPluginGPUKernels).EndInit();
        _tlp_UniPipeline_Main.ResumeLayout(false);
        _tlp_UniPipeline_Main.PerformLayout();
        _tlpLeft.ResumeLayout(false);
        _tlpLeft.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)_dgvModules).EndInit();
        _flpButtons.ResumeLayout(false);
        _grpProperties.ResumeLayout(false);
        _tlpProperties.ResumeLayout(false);
        _tlpProperties.PerformLayout();
        _flpGpuTopologySettings.ResumeLayout(false);
        _flpGpuTopologySettings.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)numericUpDown1).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }
}

