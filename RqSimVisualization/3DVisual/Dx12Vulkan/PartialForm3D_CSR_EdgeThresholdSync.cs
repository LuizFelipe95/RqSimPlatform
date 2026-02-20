using System.Numerics;
using RqSimRenderingEngine.Abstractions;

namespace RqSimVisualization;

/// <summary>
/// Partial class for embedded CSR 3D visualization edge threshold synchronization.
/// Part of Phase 4 of uni-pipeline implementation.
/// </summary>
public partial class RqSimVisualizationForm
{
    // Reference to the CSR Edge Threshold trackbar for Science mode gating
    private TrackBar? _csrTrackEdgeThreshold;

    // Waiting for data overlay
    private Label? _csrWaitingLabel;
    private bool _csrIsWaitingForData = true;

    /// <summary>
    /// Stores the CSR edge threshold trackbar reference for Science mode gating.
    /// Call this after creating the trackbar in InitializeCsrVisualizationControls.
    /// </summary>
    private void StoreCsrEdgeThresholdReference(TrackBar trackbar)
    {
        _csrTrackEdgeThreshold = trackbar;

        // Apply current Science mode state
        bool scienceModeEnabled = checkBox_ScienceSimMode?.Checked ?? false;
        _csrTrackEdgeThreshold.Enabled = !scienceModeEnabled;
    }

    /// <summary>
    /// Synchronizes the edge threshold value from the main form to the CSR visualization.
    /// </summary>
    public void SyncEdgeThresholdToCsrWindow()
    {
        _csrEdgeWeightThreshold = _edgeThresholdValue;

        if (_csrTrackEdgeThreshold is not null)
        {
            int clampedValue = Math.Clamp((int)(_edgeThresholdValue * 100), 0, 100);
            if (_csrTrackEdgeThreshold.Value != clampedValue)
            {
                _csrTrackEdgeThreshold.Value = clampedValue;
            }
        }
    }

    /// <summary>
    /// Synchronizes the edge threshold value from the CSR visualization to the main form.
    /// </summary>
    public void SyncEdgeThresholdFromCsrWindow(double csrValue)
    {
        _edgeThresholdValue = csrValue;
        _csrEdgeWeightThreshold = csrValue;

        // Update the main form trackbar if present
        if (_trkEdgeThreshold is not null)
        {
            int clampedValue = Math.Clamp((int)(csrValue * 100), 0, 100);
            if (_trkEdgeThreshold.Value != clampedValue)
            {
                _trkEdgeThreshold.Value = clampedValue;
            }
        }

        if (_lblEdgeThresholdValue is not null)
        {
            _lblEdgeThresholdValue.Text = csrValue.ToString("F2");
        }
    }

    /// <summary>
    /// Updates the "waiting for data" overlay visibility.
    /// </summary>
    private void UpdateCsrWaitingOverlay()
    {
        bool hasData = _csrNodeCount > 0 || _csrNodeX is not null;
        bool isSimRunning = _isModernRunning || _isExternalSimulation;

        _csrIsWaitingForData = !hasData && !isSimRunning;

        if (_csrWaitingLabel is not null)
        {
            _csrWaitingLabel.Visible = _csrIsWaitingForData;
        }
    }

    /// <summary>
    /// Creates the "waiting for simulation data" overlay label.
    /// Call this during CSR UI initialization.
    /// </summary>
    private Label CreateCsrWaitingOverlay()
    {
        _csrWaitingLabel = new Label
        {
            Text = "Waiting for simulation data...",
            ForeColor = Color.FromArgb(150, 150, 150),
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 12f, FontStyle.Italic),
            AutoSize = true,
            Visible = true,
            Anchor = AnchorStyles.None
        };

        return _csrWaitingLabel;
    }
}
