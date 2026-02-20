namespace RqSimTelemetryForm;

partial class TelemetryForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uiUpdateTimer?.Stop();
            _uiUpdateTimer?.Dispose();
            _sysConsoleLiveTimer?.Stop();
            _sysConsoleLiveTimer?.Dispose();
            _simConsoleLiveTimer?.Stop();
            _simConsoleLiveTimer?.Dispose();
            _sysConsoleBuffer?.Dispose();
            _simConsoleBuffer?.Dispose();
            StopMetricsListener();

            if (components != null)
            {
                components.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        _tabControl = new TabControl();
        _tabDashboard = new TabPage();
        _tabCharts = new TabPage();
        _tabConsole = new TabPage();
        _tabExperiments = new TabPage();
        _tabOTelViewer = new TabPage();
        _tabPhysicsConstants = new TabPage();
        button_CopyConstantsToClipboard = new Button();
        _statusStrip = new StatusStrip();
        _statusLabelConnection = new ToolStripStatusLabel();
        _statusLabelSteps = new ToolStripStatusLabel();
        _statusLabelExcited = new ToolStripStatusLabel();
        _statusLabelTopology = new ToolStripStatusLabel();
        _tabControl.SuspendLayout();
        _tabPhysicsConstants.SuspendLayout();
        _statusStrip.SuspendLayout();
        SuspendLayout();
        // 
        // _tabControl
        // 
        _tabControl.Controls.Add(_tabDashboard);
        _tabControl.Controls.Add(_tabCharts);
        _tabControl.Controls.Add(_tabConsole);
        _tabControl.Controls.Add(_tabExperiments);
        _tabControl.Controls.Add(_tabOTelViewer);
        _tabControl.Controls.Add(_tabPhysicsConstants);
        _tabControl.Dock = DockStyle.Fill;
        _tabControl.Location = new Point(0, 0);
        _tabControl.Margin = new Padding(2);
        _tabControl.Name = "_tabControl";
        _tabControl.SelectedIndex = 0;
        _tabControl.Size = new Size(840, 416);
        _tabControl.TabIndex = 0;
        // 
        // _tabDashboard
        // 
        _tabDashboard.Location = new Point(4, 24);
        _tabDashboard.Margin = new Padding(2);
        _tabDashboard.Name = "_tabDashboard";
        _tabDashboard.Padding = new Padding(2);
        _tabDashboard.Size = new Size(832, 388);
        _tabDashboard.TabIndex = 0;
        _tabDashboard.Text = "📊 Dashboard";
        _tabDashboard.UseVisualStyleBackColor = true;
        // 
        // _tabCharts
        // 
        _tabCharts.Location = new Point(4, 24);
        _tabCharts.Margin = new Padding(2);
        _tabCharts.Name = "_tabCharts";
        _tabCharts.Padding = new Padding(2);
        _tabCharts.Size = new Size(832, 388);
        _tabCharts.TabIndex = 1;
        _tabCharts.Text = "📈 Charts";
        _tabCharts.UseVisualStyleBackColor = true;
        // 
        // _tabConsole
        // 
        _tabConsole.Location = new Point(4, 24);
        _tabConsole.Margin = new Padding(2);
        _tabConsole.Name = "_tabConsole";
        _tabConsole.Padding = new Padding(2);
        _tabConsole.Size = new Size(832, 388);
        _tabConsole.TabIndex = 2;
        _tabConsole.Text = "📋 Console";
        _tabConsole.UseVisualStyleBackColor = true;
        // 
        // _tabExperiments
        // 
        _tabExperiments.Location = new Point(4, 24);
        _tabExperiments.Margin = new Padding(2);
        _tabExperiments.Name = "_tabExperiments";
        _tabExperiments.Padding = new Padding(2);
        _tabExperiments.Size = new Size(832, 388);
        _tabExperiments.TabIndex = 3;
        _tabExperiments.Text = "🔬 Experiments";
        _tabExperiments.UseVisualStyleBackColor = true;
        // 
        // _tabOTelViewer
        // 
        _tabOTelViewer.Location = new Point(4, 24);
        _tabOTelViewer.Margin = new Padding(2);
        _tabOTelViewer.Name = "_tabOTelViewer";
        _tabOTelViewer.Padding = new Padding(2);
        _tabOTelViewer.Size = new Size(832, 388);
        _tabOTelViewer.TabIndex = 4;
        _tabOTelViewer.Text = "⚡ OpenTelemetry";
        _tabOTelViewer.UseVisualStyleBackColor = true;
        // 
        // _tabPhysicsConstants
        // 
        _tabPhysicsConstants.Controls.Add(button_CopyConstantsToClipboard);
        _tabPhysicsConstants.Location = new Point(4, 24);
        _tabPhysicsConstants.Margin = new Padding(2);
        _tabPhysicsConstants.Name = "_tabPhysicsConstants";
        _tabPhysicsConstants.Padding = new Padding(2);
        _tabPhysicsConstants.Size = new Size(832, 388);
        _tabPhysicsConstants.TabIndex = 5;
        _tabPhysicsConstants.Text = "🔧 Physics Constants";
        _tabPhysicsConstants.UseVisualStyleBackColor = true;
        // 
        // button_CopyConstantsToClipboard
        // 
        button_CopyConstantsToClipboard.Location = new Point(269, 0);
        button_CopyConstantsToClipboard.Name = "button_CopyConstantsToClipboard";
        button_CopyConstantsToClipboard.Size = new Size(75, 23);
        button_CopyConstantsToClipboard.TabIndex = 0;
        button_CopyConstantsToClipboard.Text = "Copy";
        button_CopyConstantsToClipboard.UseVisualStyleBackColor = true;
        button_CopyConstantsToClipboard.Click += button_CopyConstantsToClipboard_Click;
        // 
        // _statusStrip
        // 
        _statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabelConnection, _statusLabelSteps, _statusLabelExcited, _statusLabelTopology });
        _statusStrip.Location = new Point(0, 416);
        _statusStrip.Name = "_statusStrip";
        _statusStrip.Padding = new Padding(1, 0, 10, 0);
        _statusStrip.Size = new Size(840, 22);
        _statusStrip.TabIndex = 1;
        // 
        // _statusLabelConnection
        // 
        _statusLabelConnection.Name = "_statusLabelConnection";
        _statusLabelConnection.Size = new Size(79, 17);
        _statusLabelConnection.Text = "Disconnected";
        // 
        // _statusLabelSteps
        // 
        _statusLabelSteps.Name = "_statusLabelSteps";
        _statusLabelSteps.Size = new Size(53, 17);
        _statusLabelSteps.Text = "Step: 0/0";
        // 
        // _statusLabelExcited
        // 
        _statusLabelExcited.Name = "_statusLabelExcited";
        _statusLabelExcited.Size = new Size(56, 17);
        _statusLabelExcited.Text = "Excited: 0";
        // 
        // _statusLabelTopology
        // 
        _statusLabelTopology.Name = "_statusLabelTopology";
        _statusLabelTopology.Size = new Size(641, 17);
        _statusLabelTopology.Spring = true;
        // 
        // TelemetryForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(840, 438);
        Controls.Add(_tabControl);
        Controls.Add(_statusStrip);
        Margin = new Padding(2);
        Name = "TelemetryForm";
        Text = "RqSim Telemetry";
        _tabControl.ResumeLayout(false);
        _tabPhysicsConstants.ResumeLayout(false);
        _statusStrip.ResumeLayout(false);
        _statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private TabControl _tabControl;
    private TabPage _tabDashboard;
    private TabPage _tabCharts;
    private TabPage _tabConsole;
    private TabPage _tabExperiments;
    private TabPage _tabOTelViewer;
    private TabPage _tabPhysicsConstants;
    private StatusStrip _statusStrip;
    private ToolStripStatusLabel _statusLabelSteps;
    private ToolStripStatusLabel _statusLabelExcited;
    private ToolStripStatusLabel _statusLabelTopology;
    private ToolStripStatusLabel _statusLabelConnection;
    private Button button_CopyConstantsToClipboard;
}
