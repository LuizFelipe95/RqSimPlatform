namespace RqSimVisualization
{
    partial class RqSimVisualizationForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _uiUpdateTimer?.Stop();
                _uiUpdateTimer?.Dispose();
                _lifeCycleManager?.Dispose();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            tabControl_MainVisualizationFormTab = new TabControl();
            tabPage_GDI = new TabPage();
            tabPage_DX12 = new TabPage();
            _lblConnectionStatus = new Label();
            tabControl_MainVisualizationFormTab.SuspendLayout();
            SuspendLayout();
            // 
            // tabControl_MainVisualizationFormTab
            // 
            tabControl_MainVisualizationFormTab.Controls.Add(tabPage_GDI);
            tabControl_MainVisualizationFormTab.Controls.Add(tabPage_DX12);
            tabControl_MainVisualizationFormTab.Dock = DockStyle.Fill;
            tabControl_MainVisualizationFormTab.Location = new Point(0, 38);
            tabControl_MainVisualizationFormTab.Name = "tabControl_MainVisualizationFormTab";
            tabControl_MainVisualizationFormTab.SelectedIndex = 0;
            tabControl_MainVisualizationFormTab.Size = new Size(1222, 713);
            tabControl_MainVisualizationFormTab.TabIndex = 0;
            // 
            // tabPage_GDI
            // 
            tabPage_GDI.Location = new Point(4, 24);
            tabPage_GDI.Name = "tabPage_GDI";
            tabPage_GDI.Padding = new Padding(3);
            tabPage_GDI.Size = new Size(1214, 685);
            tabPage_GDI.TabIndex = 0;
            tabPage_GDI.Text = "GDI+";
            tabPage_GDI.UseVisualStyleBackColor = true;
            // 
            // tabPage_DX12
            // 
            tabPage_DX12.Location = new Point(4, 24);
            tabPage_DX12.Name = "tabPage_DX12";
            tabPage_DX12.Padding = new Padding(3);
            tabPage_DX12.Size = new Size(1214, 685);
            tabPage_DX12.TabIndex = 1;
            tabPage_DX12.Text = "DX12";
            tabPage_DX12.UseVisualStyleBackColor = true;
            // 
            // _lblConnectionStatus
            // 
            _lblConnectionStatus.BackColor = Color.FromArgb(40, 40, 50);
            _lblConnectionStatus.Dock = DockStyle.Top;
            _lblConnectionStatus.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            _lblConnectionStatus.ForeColor = Color.Orange;
            _lblConnectionStatus.Location = new Point(0, 0);
            _lblConnectionStatus.Name = "_lblConnectionStatus";
            _lblConnectionStatus.Padding = new Padding(12, 8, 12, 8);
            _lblConnectionStatus.Size = new Size(1222, 38);
            _lblConnectionStatus.TabIndex = 2;
            _lblConnectionStatus.Text = "⚠ Initializing visualization...";
            _lblConnectionStatus.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // RqSimVisualizationForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1222, 751);
            Controls.Add(tabControl_MainVisualizationFormTab);
            Controls.Add(_lblConnectionStatus);
            Name = "RqSimVisualizationForm";
            Text = "RqSim Visualization";
            tabControl_MainVisualizationFormTab.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private TabControl tabControl_MainVisualizationFormTab;
        private TabPage tabPage_GDI;
        private TabPage tabPage_DX12;
        private Label _lblConnectionStatus;
    }
}
