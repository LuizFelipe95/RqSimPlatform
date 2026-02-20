namespace Dx12WinForm
{
    partial class Dx12WinForm
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
            if (disposing && (components != null))
            {
                components.Dispose();
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
            _btnStartDx12Test = new Button();
            _btnStopDx12Test = new Button();
            _pnlRenderArea = new RqSimUI.Controls.NativeRenderPanel();
            _lblStatus = new Label();
            _lblFrameInfo = new Label();
            _chkEnableDebugLayer = new CheckBox();
            _lblShaderMode = new Label();
            _cmbShaderMode = new ComboBox();
            SuspendLayout();
            // 
            // _btnStartDx12Test
            // 
            _btnStartDx12Test.Location = new Point(12, 12);
            _btnStartDx12Test.Name = "_btnStartDx12Test";
            _btnStartDx12Test.Size = new Size(120, 28);
            _btnStartDx12Test.TabIndex = 0;
            _btnStartDx12Test.Text = "Start DX12 Test";
            _btnStartDx12Test.UseVisualStyleBackColor = true;
            _btnStartDx12Test.Click += BtnStartDx12Test_Click;
            // 
            // _btnStopDx12Test
            // 
            _btnStopDx12Test.Enabled = false;
            _btnStopDx12Test.Location = new Point(138, 12);
            _btnStopDx12Test.Name = "_btnStopDx12Test";
            _btnStopDx12Test.Size = new Size(120, 28);
            _btnStopDx12Test.TabIndex = 1;
            _btnStopDx12Test.Text = "Stop Test";
            _btnStopDx12Test.UseVisualStyleBackColor = true;
            _btnStopDx12Test.Click += BtnStopDx12Test_Click;
            // 
            // _pnlRenderArea
            // 
            _pnlRenderArea.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _pnlRenderArea.BackColor = Color.Black;
            _pnlRenderArea.Location = new Point(12, 75);
            _pnlRenderArea.Name = "_pnlRenderArea";
            _pnlRenderArea.Size = new Size(776, 363);
            _pnlRenderArea.TabIndex = 2;
            // 
            // _lblStatus
            // 
            _lblStatus.AutoSize = true;
            _lblStatus.Location = new Point(264, 17);
            _lblStatus.Name = "_lblStatus";
            _lblStatus.Size = new Size(75, 15);
            _lblStatus.TabIndex = 3;
            _lblStatus.Text = "Status: Idle";
            // 
            // _lblFrameInfo
            // 
            _lblFrameInfo.AutoSize = true;
            _lblFrameInfo.Location = new Point(12, 52);
            _lblFrameInfo.Name = "_lblFrameInfo";
            _lblFrameInfo.Size = new Size(180, 15);
            _lblFrameInfo.TabIndex = 4;
            _lblFrameInfo.Text = "Frame: 0 | FPS: 0 | Render: None";
            // 
            // _chkEnableDebugLayer
            // 
            _chkEnableDebugLayer.AutoSize = true;
            _chkEnableDebugLayer.Location = new Point(350, 16);
            _chkEnableDebugLayer.Name = "_chkEnableDebugLayer";
            _chkEnableDebugLayer.Size = new Size(93, 19);
            _chkEnableDebugLayer.TabIndex = 5;
            _chkEnableDebugLayer.Text = "Debug Layer";
            _chkEnableDebugLayer.UseVisualStyleBackColor = true;
            // 
            // _lblShaderMode
            // 
            _lblShaderMode.AutoSize = true;
            _lblShaderMode.Location = new Point(460, 17);
            _lblShaderMode.Name = "_lblShaderMode";
            _lblShaderMode.Size = new Size(46, 15);
            _lblShaderMode.TabIndex = 6;
            _lblShaderMode.Text = "Shader:";
            // 
            // _cmbShaderMode
            // 
            _cmbShaderMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbShaderMode.FormattingEnabled = true;
            _cmbShaderMode.Location = new Point(512, 13);
            _cmbShaderMode.Name = "_cmbShaderMode";
            _cmbShaderMode.Size = new Size(140, 23);
            _cmbShaderMode.TabIndex = 7;
            _cmbShaderMode.SelectedIndexChanged += CmbShaderMode_SelectedIndexChanged;
            // 
            // Dx12WinForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(_cmbShaderMode);
            Controls.Add(_lblShaderMode);
            Controls.Add(_chkEnableDebugLayer);
            Controls.Add(_lblFrameInfo);
            Controls.Add(_lblStatus);
            Controls.Add(_pnlRenderArea);
            Controls.Add(_btnStopDx12Test);
            Controls.Add(_btnStartDx12Test);
            Name = "Dx12WinForm";
            Text = "DX12 Render Test";
            FormClosing += Dx12WinForm_FormClosing;
            ResumeLayout(false);
            PerformLayout();
        }



        #endregion

        private Button _btnStartDx12Test;
        private Button _btnStopDx12Test;
        private RqSimUI.Controls.NativeRenderPanel _pnlRenderArea;
        private Label _lblStatus;
        private Label _lblFrameInfo;
        private CheckBox _chkEnableDebugLayer;
        private Label _lblShaderMode;
        private ComboBox _cmbShaderMode;
    }
}
