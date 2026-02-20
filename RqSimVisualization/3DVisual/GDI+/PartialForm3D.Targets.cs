using RqSimForms.Forms.Interfaces.AutoTuning;
using RQSimulation;
using RQSimulation.GPUOptimized;
using RqSimForms.ProcessesDispatcher.Contracts;

namespace RqSimVisualization;

/// <summary>
/// Target state visualization for physics verification metrics.
/// Displays and highlights nodes/edges related to:
/// - Mass Gap (Yang-Mills spectral gap ??)
/// - Speed of Light Isotropy (Lieb-Robinson bounds)
/// - Ricci Flatness (vacuum curvature)
/// - Holographic Area Law (entropy scaling)
/// </summary>
public partial class RqSimVisualizationForm
{
    // ============================================================
    // TARGET STATE ENUMS
    // ============================================================

    /// <summary>
    /// Types of physical target states for visualization.
    /// </summary>
    private enum TargetStateType
    {
        None,
        MassGap,
        SpeedOfLight,
        RicciFlatness,
        HolographicAreaLaw,
        Combined
    }

    /// <summary>
    /// Status of a target state achievement.
    /// </summary>
    private enum TargetStatus
    {
        Unknown,
        Searching,
        Approaching,
        Achieved,
        Unstable,
        Failed
    }

    // ============================================================
    // TARGET STATE FIELDS
    // ============================================================

    // Current metrics values
    private double _massGapValue = double.NaN;
    private double _speedOfLightMean = double.NaN;
    private double _speedOfLightVariance = double.NaN;
    private double _ricciCurvatureAvg = double.NaN;
    private double _holographicEntropyRatio = double.NaN;
    private double _hausdorffDimension = double.NaN;

    // Target thresholds (physics targets)
    private const double MassGapTargetMin = 0.01;      // ?? > 0 indicates mass gap
    private const double SpeedOfLightVarianceMax = 0.1; // Low variance = isotropic
    private const double RicciFlatnessTarget = 0.0;     // <R> > 0 for flat vacuum
    private const double RicciFlatnessTolerance = 0.05;
    private const double HolographicRatioTarget = 1.0;  // S ? Area

    // Status tracking
    private TargetStatus _massGapStatus = TargetStatus.Unknown;
    private TargetStatus _speedOfLightStatus = TargetStatus.Unknown;
    private TargetStatus _ricciFlatnessStatus = TargetStatus.Unknown;
    private TargetStatus _holographicStatus = TargetStatus.Unknown;

    // Active visualization mode for targets
    private TargetStateType _activeTargetVis = TargetStateType.None;
    private bool _showTargetOverlay = true;

    // Update throttling (expensive computations)
    private DateTime _lastTargetMetricUpdate = DateTime.MinValue;
    private readonly TimeSpan _targetMetricUpdateInterval = TimeSpan.FromSeconds(2.0);

    // Engine references for metric computation
    private GpuSpectralEngine? _targetSpectralEngine;
    private SpectralWalkEngine? _targetWalkEngine;

    // Signal propagation visualization data
    private int _signalSourceNode = -1;
    private int _signalTargetNode = -1;
    private List<int>? _signalPath;

    // UI Controls for target panel
    private Panel? _pnlTargetStatus;
    private Label? _lblTargetTitle;
    private CheckBox? _chkShowTargetOverlay;
    private ComboBox? _cmbTargetMode;

    // ============================================================
    // INITIALIZATION
    // ============================================================

    /// <summary>
    /// Initializes target state visualization.
    /// Call after Initialize3DVisual().
    /// </summary>
    private void InitializeTargetVisualization()
    {
        // Add "Target" mode to visualization ComboBox
        if (_cmbVisMode != null && !_cmbVisMode.Items.Contains("Target"))
        {
            _cmbVisMode.Items.Add("Target");
        }

        // Find the main controls panel first
        Control? mainControlsPanel = null;
        if (_panel3D != null)
        {
            foreach (Control c in _panel3D.Controls)
            {
                if (c is FlowLayoutPanel)
                {
                    mainControlsPanel = c;
                    break;
                }
            }
        }

        // Create target status panel (positioned at bottom-left of 3D panel)
        // Use DoubleBufferedPanel to prevent flickering
        _pnlTargetStatus = new DoubleBufferedPanel
        {
            Size = new Size(280, 160), // Reduced height since controls are moved out
            BackColor = Color.FromArgb(180, 15, 15, 25),
            Visible = true
        };
        _pnlTargetStatus.Paint += PnlTargetStatus_Paint;

        // Target overlay toggle
        _chkShowTargetOverlay = new CheckBox
        {
            Text = "Show Target Metrics",
            ForeColor = Color.White,
            Checked = true,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 5)
        };
        _chkShowTargetOverlay.CheckedChanged += (s, e) =>
        {
            _showTargetOverlay = _chkShowTargetOverlay.Checked;
            if (_pnlTargetStatus != null) _pnlTargetStatus.Visible = _showTargetOverlay;
            if (_cmbTargetMode != null) _cmbTargetMode.Enabled = _showTargetOverlay;
            _panel3D?.Invalidate();
        };

        // Target mode selector
        _lblTargetTitle = new Label
        {
            Text = "Target Mode:",
            ForeColor = Color.LightGray,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 0)
        };

        _cmbTargetMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 160,
            BackColor = Color.FromArgb(40, 40, 50),
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 0, 5)
        };
        _cmbTargetMode.Items.AddRange(new object[]
        {
            "Combined",
            "Mass Gap",
            "Speed of Light",
            "Ricci Flatness",
            "Holographic"
        });
        _cmbTargetMode.SelectedIndex = 0;
        _cmbTargetMode.SelectedIndexChanged += (s, e) =>
        {
            _activeTargetVis = _cmbTargetMode.SelectedIndex switch
            {
                0 => TargetStateType.Combined,
                1 => TargetStateType.MassGap,
                2 => TargetStateType.SpeedOfLight,
                3 => TargetStateType.RicciFlatness,
                4 => TargetStateType.HolographicAreaLaw,
                _ => TargetStateType.None
            };
            _panel3D?.Invalidate();
        };

        // Add controls to main panel if found, otherwise fallback to target panel
        if (mainControlsPanel != null)
        {
            mainControlsPanel.Controls.Add(_chkShowTargetOverlay);
            mainControlsPanel.Controls.Add(_lblTargetTitle);
            mainControlsPanel.Controls.Add(_cmbTargetMode);
        }
        else
        {
            _pnlTargetStatus.Controls.Add(_chkShowTargetOverlay);
            _pnlTargetStatus.Controls.Add(_lblTargetTitle);
            _pnlTargetStatus.Controls.Add(_cmbTargetMode);
            _chkShowTargetOverlay.Location = new Point(5, 5);
            _lblTargetTitle.Location = new Point(5, 28);
            _cmbTargetMode.Location = new Point(90, 25);
        }

        // Add to 3D panel
        if (_panel3D != null)
        {
            _panel3D.Controls.Add(_pnlTargetStatus);

            if (mainControlsPanel != null)
            {
                // Position immediately below the main controls panel
                _pnlTargetStatus.Location = new Point(10, mainControlsPanel.Bottom + 10);

                // Keep attached to the bottom of the main panel if it resizes
                mainControlsPanel.SizeChanged += (s, e) =>
                {
                    if (_pnlTargetStatus != null && mainControlsPanel != null)
                        _pnlTargetStatus.Location = new Point(10, mainControlsPanel.Bottom + 10);
                };
            }
            else
            {
                // Fallback
                _pnlTargetStatus.Location = new Point(10, 450);
            }
        }
    }

    // ============================================================
    // METRIC COMPUTATION
    // ============================================================

    /// <summary>
    /// Updates all target metrics from the current graph state.
    /// Called periodically from Timer3D_Tick.
    /// </summary>
    private void UpdateTargetMetrics(RQGraph? graph)
    {
        if (graph == null || graph.N < 10) return;

        // Throttle expensive computations
        if (DateTime.UtcNow - _lastTargetMetricUpdate < _targetMetricUpdateInterval)
        {
            return;
        }
        _lastTargetMetricUpdate = DateTime.UtcNow;

        try
        {
            // Initialize engines if needed
            _targetSpectralEngine ??= new GpuSpectralEngine();
            _targetWalkEngine ??= new SpectralWalkEngine();

            // Update topology
            if (_targetSpectralEngine.TopologyVersion != graph.TopologyVersion)
            {
                _targetSpectralEngine.UpdateTopology(graph);
            }
            if (_targetWalkEngine.TopologyVersion != graph.TopologyVersion)
            {
                _targetWalkEngine.UpdateTopologyFromGraph(graph);
            }

            // 1. Mass Gap (??)
            UpdateMassGapMetric(graph);

            // 2. Speed of Light (Lieb-Robinson)
            UpdateSpeedOfLightMetric(graph);

            // 3. Ricci Curvature (if available)
            UpdateRicciCurvatureMetric(graph);

            // 4. Hausdorff vs Spectral Dimension
            UpdateDimensionComparisonMetric(graph);

            // Update panel display
            _pnlTargetStatus?.Invalidate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GDI+ Targets] Metric computation failed: {ex.GetType().Name}: {ex.Message}");
            _massGapStatus = TargetStatus.Failed;
            _speedOfLightStatus = TargetStatus.Failed;
            _ricciFlatnessStatus = TargetStatus.Failed;
            _holographicStatus = TargetStatus.Failed;
            _massGapValue = double.NaN;
            _speedOfLightVariance = double.NaN;
            _speedOfLightMean = double.NaN;
            _ricciCurvatureAvg = double.NaN;
            _hausdorffDimension = double.NaN;
            _pnlTargetStatus?.Invalidate();
        }
    }

    private void UpdateMassGapMetric(RQGraph graph)
    {
        try
        {
            _massGapValue = _targetSpectralEngine?.EstimateMassGap(graph, iterations: 50) ?? double.NaN;

            if (double.IsNaN(_massGapValue))
            {
                _massGapStatus = TargetStatus.Unknown;
            }
            else if (_massGapValue < 0.001)
            {
                _massGapStatus = TargetStatus.Searching;
            }
            else if (_massGapValue < MassGapTargetMin)
            {
                _massGapStatus = TargetStatus.Approaching;
            }
            else
            {
                _massGapStatus = TargetStatus.Achieved;
            }
        }
        catch
        {
            _massGapValue = double.NaN;
            _massGapStatus = TargetStatus.Failed;
        }
    }

    private void UpdateSpeedOfLightMetric(RQGraph graph)
    {
        try
        {
            if (_targetWalkEngine == null) return;

            var (variance, mean) = _targetWalkEngine.CalculateIsotropyVariance(graph, numSamples: 5, minDistance: 2);
            _speedOfLightVariance = variance;
            _speedOfLightMean = mean;

            if (double.IsNaN(variance))
            {
                _speedOfLightStatus = TargetStatus.Unknown;
            }
            else if (variance > 0.5)
            {
                _speedOfLightStatus = TargetStatus.Searching;
            }
            else if (variance > SpeedOfLightVarianceMax)
            {
                _speedOfLightStatus = TargetStatus.Approaching;
            }
            else
            {
                _speedOfLightStatus = TargetStatus.Achieved;
            }
        }
        catch
        {
            _speedOfLightVariance = double.NaN;
            _speedOfLightMean = double.NaN;
            _speedOfLightStatus = TargetStatus.Failed;
        }
    }

    private void UpdateRicciCurvatureMetric(RQGraph graph)
    {
        // Ricci curvature requires OllivierRicciCurvature engine
        // For now, estimate from local connectivity variance
        try
        {
            double sumCurvature = 0;
            int count = 0;

            for (int i = 0; i < Math.Min(graph.N, 100); i++)
            {
                var neighbors = graph.Neighbors(i).ToList();
                if (neighbors.Count < 2) continue;

                // Simple proxy: curvature ? (actual edges - expected edges) / degree?
                int actualEdges = 0;
                foreach (int n1 in neighbors)
                {
                    foreach (int n2 in neighbors)
                    {
                        if (n1 < n2 && graph.Weights[n1, n2] > 0.3)
                        {
                            actualEdges++;
                        }
                    }
                }
                int maxEdges = neighbors.Count * (neighbors.Count - 1) / 2;
                if (maxEdges > 0)
                {
                    double localCurvature = 1.0 - (double)actualEdges / maxEdges;
                    sumCurvature += localCurvature;
                    count++;
                }
            }

            _ricciCurvatureAvg = count > 0 ? sumCurvature / count : double.NaN;

            double deviation = Math.Abs(_ricciCurvatureAvg - RicciFlatnessTarget);
            _ricciFlatnessStatus = deviation switch
            {
                > 0.3 => TargetStatus.Searching,
                > RicciFlatnessTolerance => TargetStatus.Approaching,
                <= RicciFlatnessTolerance => TargetStatus.Achieved,
                _ => TargetStatus.Unknown
            };
        }
        catch
        {
            _ricciCurvatureAvg = double.NaN;
            _ricciFlatnessStatus = TargetStatus.Failed;
        }
    }

    private void UpdateDimensionComparisonMetric(RQGraph graph)
    {
        try
        {
            // Compare spectral dimension with simple BFS-based growth rate
            double ds = graph.SmoothedSpectralDimension;

            // Hausdorff dimension estimate (simplified)
            // Count nodes within radius r, fit N(r) ~ r^dH
            if (graph.N > 50)
            {
                _hausdorffDimension = EstimateHausdorffDimensionSimple(graph);

                // For good emergent geometry, dS ? dH ? 4
                double dimDiff = Math.Abs(ds - _hausdorffDimension);

                // Holographic status based on dimension agreement
                _holographicStatus = dimDiff switch
                {
                    > 1.5 => TargetStatus.Searching,
                    > 0.5 => TargetStatus.Approaching,
                    <= 0.5 when ds > 3.5 && ds < 4.5 => TargetStatus.Achieved,
                    _ => TargetStatus.Approaching
                };
            }
        }
        catch
        {
            _hausdorffDimension = double.NaN;
            _holographicStatus = TargetStatus.Failed;
        }
    }

    private double EstimateHausdorffDimensionSimple(RQGraph graph)
    {
        // BFS from a random node, count nodes by layer
        int startNode = graph.N / 2;
        int maxRadius = Math.Min(10, graph.N / 5);

        int[] distance = new int[graph.N];
        Array.Fill(distance, -1);
        distance[startNode] = 0;

        Queue<int> queue = new();
        queue.Enqueue(startNode);

        int[] countByRadius = new int[maxRadius + 1];
        countByRadius[0] = 1;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int currentDist = distance[current];
            if (currentDist >= maxRadius) continue;

            foreach (int neighbor in graph.Neighbors(current))
            {
                if (distance[neighbor] < 0)
                {
                    distance[neighbor] = currentDist + 1;
                    if (distance[neighbor] <= maxRadius)
                    {
                        countByRadius[distance[neighbor]]++;
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        // Linear regression on log-log plot: log(cumulative count) vs log(r)
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        int cumulative = 0;
        int n = 0;

        for (int r = 1; r <= maxRadius; r++)
        {
            cumulative += countByRadius[r];
            if (cumulative <= 1) continue;

            double logR = Math.Log(r);
            double logN = Math.Log(cumulative);

            sumX += logR;
            sumY += logN;
            sumXY += logR * logN;
            sumX2 += logR * logR;
            n++;
        }

        if (n < 2) return double.NaN;

        double denom = n * sumX2 - sumX * sumX;
        if (Math.Abs(denom) < 1e-10) return double.NaN;

        return (n * sumXY - sumX * sumY) / denom;
    }

    // ============================================================
    // VISUALIZATION RENDERING
    // ============================================================

    /// <summary>
    /// Paints the target status panel with current metrics.
    /// </summary>
    private void PnlTargetStatus_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel) return;

        Graphics g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var fontTitle = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold);
        using var fontMetric = new Font(SystemFonts.DefaultFont.FontFamily, 9f);
        using var fontSmall = new Font(SystemFonts.DefaultFont.FontFamily, 8f);

        // Start drawing from top since controls are moved out (unless fallback used)
        // Check if controls are in this panel (fallback mode)
        bool hasControls = panel.Controls.Count > 0;
        int y = hasControls ? 55 : 10; 
        int labelX = 8;
        int valueX = 150;

        // Title
        using (var titleBrush = new SolidBrush(Color.FromArgb(100, 180, 255)))
        {
            g.DrawString("? PHYSICS TARGETS", fontTitle, titleBrush, labelX, y);
        }
        y += 22;

        // Draw separator line
        using (var linePen = new Pen(Color.FromArgb(80, 100, 180, 255), 1))
        {
            g.DrawLine(linePen, labelX, y, panel.Width - labelX, y);
        }
        y += 8;

        // Mass Gap
        DrawTargetMetricLine(g, fontMetric, fontSmall, labelX, valueX, ref y,
            "Mass Gap (??):",
            double.IsNaN(_massGapValue) ? "---" : $"{_massGapValue:F4}",
            _massGapStatus,
            "Yang-Mills");

        // Speed of Light
        DrawTargetMetricLine(g, fontMetric, fontSmall, labelX, valueX, ref y,
            "c Isotropy (??):",
            double.IsNaN(_speedOfLightVariance) ? "---" : $"{_speedOfLightVariance:F4}",
            _speedOfLightStatus,
            "Lieb-Robinson");

        // Speed of Light Mean
        if (!double.IsNaN(_speedOfLightMean))
        {
            using var dimBrush = new SolidBrush(Color.Gray);
            g.DrawString($"  c_eff = {_speedOfLightMean:F3}", fontSmall, dimBrush, labelX + 10, y);
            y += 14;
        }

        // Ricci Curvature
        DrawTargetMetricLine(g, fontMetric, fontSmall, labelX, valueX, ref y,
            "Ricci <R>:",
            double.IsNaN(_ricciCurvatureAvg) ? "---" : $"{_ricciCurvatureAvg:F4}",
            _ricciFlatnessStatus,
            "Vacuum");

        // Hausdorff Dimension
        DrawTargetMetricLine(g, fontMetric, fontSmall, labelX, valueX, ref y,
            "Hausdorff d_H:",
            double.IsNaN(_hausdorffDimension) ? "---" : $"{_hausdorffDimension:F2}",
            _holographicStatus,
            "Holographic");

        // Overall status summary
        y += 6;
        using (var linePen = new Pen(Color.FromArgb(60, 100, 180, 255), 1))
        {
            g.DrawLine(linePen, labelX, y, panel.Width - labelX, y);
        }
        y += 6;

        int achieved = CountAchievedTargets();
        int total = 4;
        Color summaryColor = achieved switch
        {
            4 => Color.Lime,
            3 => Color.GreenYellow,
            2 => Color.Yellow,
            1 => Color.Orange,
            _ => Color.Red
        };

        using (var summaryBrush = new SolidBrush(summaryColor))
        {
            string statusText = achieved switch
            {
                4 => "? ALL TARGETS ACHIEVED",
                3 => "? 3/4 Targets Met",
                2 => "? 2/4 Targets Met",
                1 => "? 1/4 Targets Met",
                _ => "0 Searching..."
            };
            g.DrawString(statusText, fontTitle, summaryBrush, labelX, y);
        }
    }

    private void DrawTargetMetricLine(Graphics g, Font fontMetric, Font fontSmall,
        int labelX, int valueX, ref int y,
        string label, string value, TargetStatus status, string category)
    {
        Color statusColor = status switch
        {
            TargetStatus.Achieved => Color.Lime,
            TargetStatus.Approaching => Color.Yellow,
            TargetStatus.Searching => Color.Orange,
            TargetStatus.Unstable => Color.OrangeRed,
            TargetStatus.Failed => Color.Red,
            _ => Color.Gray
        };

        string statusSymbol = status switch
        {
            TargetStatus.Achieved => "?",
            TargetStatus.Approaching => "?",
            TargetStatus.Searching => "0",
            TargetStatus.Unstable => "?",
            TargetStatus.Failed => "?",
            _ => "?"
        };

        using var labelBrush = new SolidBrush(Color.LightGray);
        using var valueBrush = new SolidBrush(statusColor);
        using var catBrush = new SolidBrush(Color.FromArgb(120, statusColor));

        g.DrawString(label, fontMetric, labelBrush, labelX, y);
        g.DrawString($"{statusSymbol} {value}", fontMetric, valueBrush, valueX, y);
        y += 16;

        // Category subtitle
        g.DrawString($"  [{category}]", fontSmall, catBrush, labelX, y);
        y += 14;
    }

    private int CountAchievedTargets()
    {
        int count = 0;
        if (_massGapStatus == TargetStatus.Achieved) count++;
        if (_speedOfLightStatus == TargetStatus.Achieved) count++;
        if (_ricciFlatnessStatus == TargetStatus.Achieved) count++;
        if (_holographicStatus == TargetStatus.Achieved) count++;
        return count;
    }

    // ============================================================
    // NODE COLORING FOR TARGET MODE
    // ============================================================

    /// <summary>
    /// Gets color for a node in Target visualization mode.
    /// Colors based on the node's contribution to target states.
    /// </summary>
    private Color GetTargetStateColor(int nodeIndex, VisualSnapshot snapshot, int alpha = 255)
    {
        if (snapshot == null || nodeIndex < 0 || nodeIndex >= snapshot.NodeCount)
        {
            return Color.FromArgb(alpha, 50, 50, 50);
        }

        return _activeTargetVis switch
        {
            TargetStateType.MassGap => GetMassGapNodeColor(nodeIndex, snapshot, alpha),
            TargetStateType.SpeedOfLight => GetSpeedOfLightNodeColor(nodeIndex, snapshot, alpha),
            TargetStateType.RicciFlatness => GetRicciNodeColor(nodeIndex, snapshot, alpha),
            TargetStateType.HolographicAreaLaw => GetHolographicNodeColor(nodeIndex, snapshot, alpha),
            TargetStateType.Combined => GetCombinedTargetColor(nodeIndex, snapshot, alpha),
            _ => Color.FromArgb(alpha, 100, 100, 100)
        };
    }

    private Color GetMassGapNodeColor(int nodeIndex, VisualSnapshot snapshot, int alpha)
    {
        // Color by edge weight sum (connectivity) - high connectivity nodes
        // contribute more to mass gap (spectral gap = graph connectivity)
        float totalWeight = 0;
        int edgeCount = 0;

        foreach (var (u, v, w) in snapshot.Edges)
        {
            if (u == nodeIndex || v == nodeIndex)
            {
                totalWeight += w;
                edgeCount++;
            }
        }

        float avgWeight = edgeCount > 0 ? totalWeight / edgeCount : 0;
        float normalized = Math.Clamp(avgWeight, 0, 1);

        // Blue (low contribution) -> Purple -> Red (high contribution)
        int r = (int)(255 * normalized);
        int b = (int)(255 * (1 - normalized));
        int g = (int)(80 * (1 - Math.Abs(normalized - 0.5f) * 2));

        return Color.FromArgb(alpha, r, g, b);
    }

    private Color GetSpeedOfLightNodeColor(int nodeIndex, VisualSnapshot snapshot, int alpha)
    {
        // Color by position in spectral coordinates (distance from center)
        // Nodes at similar distances should show similar colors for isotropy
        float x = snapshot.X[nodeIndex];
        float y = snapshot.Y[nodeIndex];
        float z = snapshot.Z[nodeIndex];
        float dist = MathF.Sqrt(x * x + y * y + z * z);

        // Normalize distance
        float maxDist = 2.0f;
        float normalized = Math.Clamp(dist / maxDist, 0, 1);

        // Cyan (center) -> Green -> Yellow (edge) - representing wavefront
        int r = (int)(255 * normalized);
        int g = (int)(255 * (1 - normalized * 0.3f));
        int b = (int)(255 * (1 - normalized));

        return Color.FromArgb(alpha, r, g, b);
    }

    private Color GetRicciNodeColor(int nodeIndex, VisualSnapshot snapshot, int alpha)
    {
        // Color by local clustering (proxy for curvature)
        // Positive curvature (high clustering) = red, flat = green, negative = blue
        int neighborEdges = 0;
        int maxPossibleEdges = 0;

        // Count edges between neighbors of this node
        var myNeighbors = new HashSet<int>();
        foreach (var (u, v, _) in snapshot.Edges)
        {
            if (u == nodeIndex) myNeighbors.Add(v);
            if (v == nodeIndex) myNeighbors.Add(u);
        }

        if (myNeighbors.Count >= 2)
        {
            maxPossibleEdges = myNeighbors.Count * (myNeighbors.Count - 1) / 2;
            foreach (var (u, v, _) in snapshot.Edges)
            {
                if (myNeighbors.Contains(u) && myNeighbors.Contains(v))
                {
                    neighborEdges++;
                }
            }
        }

        float clusterCoeff = maxPossibleEdges > 0 ? (float)neighborEdges / maxPossibleEdges : 0;
        float curvature = clusterCoeff - 0.3f; // Center around expected value

        // Red (positive curvature) -> Green (flat) -> Blue (negative)
        int r = curvature > 0 ? (int)(255 * Math.Min(1, curvature * 3)) : 0;
        int g = (int)(200 * (1 - Math.Abs(curvature) * 2));
        int b = curvature < 0 ? (int)(255 * Math.Min(1, -curvature * 3)) : 0;

        return Color.FromArgb(alpha, r, Math.Max(50, g), b);
    }

    private Color GetHolographicNodeColor(int nodeIndex, VisualSnapshot snapshot, int alpha)
    {
        // Color by radial layer (shell membership for area law)
        float x = snapshot.X[nodeIndex];
        float y = snapshot.Y[nodeIndex];
        float z = snapshot.Z[nodeIndex];
        float dist = MathF.Sqrt(x * x + y * y + z * z);

        // Quantize into shells
        int shell = (int)(dist * 5);
        float hue = (shell % 6) / 6.0f;

        // HSV to RGB conversion (simplified)
        float h = hue * 6;
        int hi = (int)h % 6;
        float f = h - hi;
        int v = 255;
        int p = 50;
        int q = (int)(255 * (1 - f * 0.8f));
        int t = (int)(50 + 205 * f);

        return hi switch
        {
            0 => Color.FromArgb(alpha, v, t, p),
            1 => Color.FromArgb(alpha, q, v, p),
            2 => Color.FromArgb(alpha, p, v, t),
            3 => Color.FromArgb(alpha, p, q, v),
            4 => Color.FromArgb(alpha, t, p, v),
            _ => Color.FromArgb(alpha, v, p, q)
        };
    }

    private Color GetCombinedTargetColor(int nodeIndex, VisualSnapshot snapshot, int alpha)
    {
        // Blend all target colors
        var massGap = GetMassGapNodeColor(nodeIndex, snapshot, 255);
        var speed = GetSpeedOfLightNodeColor(nodeIndex, snapshot, 255);
        var ricci = GetRicciNodeColor(nodeIndex, snapshot, 255);

        // Simple average blending
        int r = (massGap.R + speed.R + ricci.R) / 3;
        int g = (massGap.G + speed.G + ricci.G) / 3;
        int b = (massGap.B + speed.B + ricci.B) / 3;

        return Color.FromArgb(alpha, r, g, b);
    }

    // ============================================================
    // EDGE VISUALIZATION FOR TARGETS
    // ============================================================

    /// <summary>
    /// Gets edge color and width for Target visualization mode.
    /// </summary>
    private (Color color, float width) GetTargetEdgeStyle(int u, int v, float weight, VisualSnapshot snapshot)
    {
        return _activeTargetVis switch
        {
            TargetStateType.MassGap => GetMassGapEdgeStyle(weight),
            TargetStateType.SpeedOfLight => GetSpeedOfLightEdgeStyle(u, v, snapshot),
            TargetStateType.RicciFlatness => GetRicciEdgeStyle(weight),
            _ => (Color.FromArgb(60, 100, 200, 100), 1f)
        };
    }

    private (Color, float) GetMassGapEdgeStyle(float weight)
    {
        // Heavy edges are critical for mass gap - highlight them
        if (weight > 0.8f)
        {
            return (Color.FromArgb(200, 255, 100, 100), 2.5f);
        }
        if (weight > 0.5f)
        {
            return (Color.FromArgb(150, 255, 200, 100), 1.5f);
        }
        return (Color.FromArgb(40, 100, 100, 100), 0.5f);
    }

    private (Color, float) GetSpeedOfLightEdgeStyle(int u, int v, VisualSnapshot snapshot)
    {
        // Highlight edges along signal path
        if (_signalPath != null && _signalPath.Count > 1)
        {
            for (int i = 0; i < _signalPath.Count - 1; i++)
            {
                if ((_signalPath[i] == u && _signalPath[i + 1] == v) ||
                    (_signalPath[i] == v && _signalPath[i + 1] == u))
                {
                    return (Color.FromArgb(255, 0, 255, 255), 3f);
                }
            }
        }
        return (Color.FromArgb(30, 100, 200, 200), 1f);
    }

    private (Color, float) GetRicciEdgeStyle(float weight)
    {
        // Edge color based on weight (curvature contribution)
        float normalized = Math.Clamp(weight, 0, 1);
        int intensity = (int)(100 + 155 * normalized);
        return (Color.FromArgb(80, intensity, 200, intensity), 1f + normalized);
    }

    // ============================================================
    // INTEGRATION HOOKS
    // ============================================================

    /// <summary>
    /// Called from Timer3D_Tick to update target metrics.
    /// </summary>
    private void UpdateTargetsFromTimer(RQGraph? graph)
    {
        if (_showTargetOverlay && (_visualMode == "Target" || _activeTargetVis != TargetStateType.None))
        {
            UpdateTargetMetrics(graph);
        }
    }

    /// <summary>
    /// Disposes target visualization resources.
    /// </summary>
    private void DisposeTargetVisualization()
    {
        _targetSpectralEngine?.Dispose();
        _targetSpectralEngine = null;
        _targetWalkEngine?.Dispose();
        _targetWalkEngine = null;
    }
}

