using RQSimulation;
using RqSimForms.ProcessesDispatcher.Contracts;

namespace RqSimVisualization;

/*
 Spectral Dimension Interpretation:
•	$d_S < 1.5$: "0D/1D Dust" (Gray)
•	$d_S \approx 2$: "1D Filament" (Cyan)
•	$d_S \approx 3$: "2D Membrane" (Yellow)
•	$d_S \approx 4$: "3D Bulk" (Lime) - This is the target emergent spacetime.
•	$d_S > 4.5$: "High-D / Complex" (Magenta)
*/
public partial class RqSimVisualizationForm
{
    private DoubleBufferedPanel _panel3D;
    private System.Windows.Forms.Timer _timer3D;
    private float _yaw3D = 0, _pitch3D = 0;
    private Point _lastMouse3D;
    private float _zoom3D = 50.0f;
    private bool _is3DInitialized = false;

    // Visualization State
    private VisualSnapshot? _lastSnapshot;
    private string _visualMode = "Depth";
    private bool _showEdges = true;
    private bool _showGrid = true;
    private bool _enableManifoldEmbedding = true;

    // Manifold Embedding Parameters (Force-Directed Layout)
    private const double ManifoldRepulsionFactor = 0.5;
    private const double ManifoldSpringFactor = 0.8;
    private const double ManifoldDamping = 0.85;
    private const double ManifoldDeltaTime = 0.05; // Increased for faster movement
    private float[]? _embeddingVelocityX;
    private float[]? _embeddingVelocityY;
    private float[]? _embeddingVelocityZ;

    // Persistent positions for manifold embedding (survive between frames)
    private float[]? _embeddingPositionX;
    private float[]? _embeddingPositionY;
    private float[]? _embeddingPositionZ;
    private bool _embeddingInitialized = false;

    // Cached edges for external simulation (reconstructed ONCE from initial server coordinates)
    // This is critical: edges must be reconstructed from ORIGINAL spectral coordinates,
    // not from manifold-modified positions which would create isolated clusters.
    private List<(int u, int v, float w)>? _cachedExternalEdges;
    private int _cachedExternalEdgeCount = 0;

    // UI Controls
    private ComboBox _cmbVisMode;
    private CheckBox _chkEdges;
    private CheckBox _chkGrid;
    private CheckBox _chkManifoldEmbedding;
    private Label _lblStats;
    private Panel? _pnlLegend; // Replaced Label with Panel for custom drawing

    private class VisualSnapshot
    {
        public float[] X;
        public float[] Y;
        public float[] Z;
        public NodeState[] States;
        public int[] ClusterIds;
        public List<(int u, int v, float w)> Edges;
        public int NodeCount;
        public double SpectralDim;
    }

    private void Initialize3DVisual()
    {
        if (_is3DInitialized) return;

        // GDI+ visualization for non-CSR modes
        // CSR visualization is on separate tabPage_3DVisualCSR tab
        _panel3D = new DoubleBufferedPanel();
        _panel3D.Dock = DockStyle.Fill;
        _panel3D.BackColor = Color.Black;
        _panel3D.Paint += Panel3D_Paint;
        _panel3D.MouseDown += Panel3D_MouseDown;
        _panel3D.MouseMove += Panel3D_MouseMove;
        _panel3D.MouseWheel += Panel3D_MouseWheel;

        // Controls Panel
        var controlsPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            BackColor = Color.FromArgb(150, 20, 20, 20),
            Location = new Point(10, 10),
            Padding = new Padding(5),
            Width = 180
        };

        // Mode Selection
        var lblMode = new Label { Text = "Color Mode:", ForeColor = Color.White, AutoSize = true, Margin = new Padding(0, 0, 0, 5) };
        _cmbVisMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, BackColor = Color.FromArgb(40, 40, 40), ForeColor = Color.White };
        _cmbVisMode.Items.AddRange(new object[] { "Depth", "State", "Clusters", "Quantum", "Manifold", "Target" });
        _cmbVisMode.SelectedIndex = 0;
        _cmbVisMode.SelectedIndexChanged += (s, e) => { _visualMode = _cmbVisMode.SelectedItem?.ToString() ?? "Depth"; _panel3D.Invalidate(); };

        // Edges Toggle
        _chkEdges = new CheckBox { Text = "Show Strong Edges", ForeColor = Color.White, Checked = true, AutoSize = true, Margin = new Padding(0, 10, 0, 5) };
        _chkEdges.CheckedChanged += (s, e) => { _showEdges = _chkEdges.Checked; _panel3D.Invalidate(); };

        // Grid Toggle
        _chkGrid = new CheckBox { Text = "Show Reference Grid", ForeColor = Color.White, Checked = true, AutoSize = true, Margin = new Padding(0, 0, 0, 5) };
        _chkGrid.CheckedChanged += (s, e) => { _showGrid = _chkGrid.Checked; _panel3D.Invalidate(); };

        // Manifold Embedding Toggle
        _chkManifoldEmbedding = new CheckBox { Text = "Enable Manifold Embedding", ForeColor = Color.White, Checked = true, AutoSize = true, Margin = new Padding(0, 0, 0, 5) };
        _chkManifoldEmbedding.CheckedChanged += (s, e) =>
        {
            _enableManifoldEmbedding = _chkManifoldEmbedding.Checked;
            if (!_enableManifoldEmbedding)
            {
                // Reset embedding state completely when disabled
                ResetManifoldEmbedding();
            }
            _panel3D.Invalidate();
        };

        // Refresh Button
        var btnRefresh = new Button
        {
            Text = "Update Spectral Coords",
            AutoSize = false,
            Width = 160,
            Height = 30,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 10, 0, 5)
        };
        btnRefresh.Click += BtnRefresh_Click;

        // Stats Label
        _lblStats = new Label { Text = "Waiting for Sim...", ForeColor = Color.LightGray, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };

        // Spectral Interpretation Legend
        _pnlLegend = new DoubleBufferedPanel
        {
            Size = new Size(170, 120),
            BackColor = Color.FromArgb(120, 20, 20, 20),
            Margin = new Padding(0, 10, 0, 0)
        };
        _pnlLegend.Paint += PnlLegend_Paint;

        controlsPanel.Controls.Add(lblMode);
        controlsPanel.Controls.Add(_cmbVisMode);
        controlsPanel.Controls.Add(_chkEdges);
        controlsPanel.Controls.Add(_chkGrid);
        controlsPanel.Controls.Add(_chkManifoldEmbedding);
        controlsPanel.Controls.Add(btnRefresh);
        controlsPanel.Controls.Add(_lblStats);
        controlsPanel.Controls.Add(_pnlLegend);

        _panel3D.Controls.Add(controlsPanel);

        if (tabPage_3DVisual != null)
        {
            tabPage_3DVisual.Controls.Add(_panel3D);
        }

        _timer3D = new System.Windows.Forms.Timer { Interval = 50 };
        _timer3D.Tick += Timer3D_Tick;
        _timer3D.Start();

        _is3DInitialized = true;

        // Initialize target visualization (adds Target mode and status panel)
        InitializeTargetVisualization();
    }

    private void BtnRefresh_Click(object? sender, EventArgs e)
    {
        var graph = _simulationEngine?.Graph;
        if (graph != null)
        {
            _lblStats.Text = "Calculating...";
            Task.Run(() =>
            {
                try
                {
                    graph.UpdateSpectralCoordinates();
                }
                catch { }
            });
        }
    }
    private void Timer3D_Tick(object? sender, EventArgs e)
    {
        // Skip GDI+ visualization when CSR mode is active
        // CSR has its own visualization on tabPage_3DVisualCSR
        /*   if (_simApi?.ActiveEngineType == RqSimForms.Forms.Interfaces.GpuEngineType.Csr)
           {
               return;
           }*/

        // Skip rendering when simulation is not active (local mode stopped)
        if (_hasApiConnection && !_isExternalSimulation && !_simApi.IsModernRunning)
        {
            return;
        }

        // GDI+ visualization: Console mode vs Local mode
        if (_isExternalSimulation)
        {
            // CONSOLE MODE: Use separate logic in PartialForm3D_ConsoleMode.cs
            var externalNodes = _lifeCycleManager?.GetExternalRenderNodes();
            if (externalNodes != null && externalNodes.Length > 0)
            {
                // FIX 29: Get REAL edges from shared memory instead of reconstructing from proximity!
                var externalEdges = _lifeCycleManager?.GetExternalRenderEdges();

                // Use new isolated console mode processing with real edges
                var snapshot = CreateSnapshotFromConsole(externalNodes, externalEdges);
                if (snapshot != null)
                {
                    _lastSnapshot = snapshot;
                    UpdateStatsLabel(snapshot);
                }
            }
        }
        else if (_simulationEngine?.Graph != null)
        {
            // LOCAL MODE: Use original logic with RQGraph
            UpdateSnapshot(_simulationEngine.Graph);

            // Update target metrics (throttled internally)
            UpdateTargetsFromTimer(_simulationEngine.Graph);
        }

        if (tabControl_Main.SelectedTab == tabPage_3DVisual)
        {
            _panel3D.Invalidate();
        }
    }

    private void PnlLegend_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel) return;
        Graphics g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        string title = "Spectral Interpretation:";
        var items = new (string Label, Color Color)[]
        {
            ("d_S < 1.5: Dust", Color.Gray),
            ("d_S ? 2: Filament", Color.Cyan),
            ("d_S ? 3: Membrane", Color.Yellow),
            ("d_S ? 4: Bulk (Target)", Color.Lime),
            ("d_S > 4.5: Complex", Color.Magenta)
        };

        int y = 2;
        using var brushText = new SolidBrush(Color.LightGray);
        using var font = new Font(SystemFonts.DefaultFont.FontFamily, 8f);

        // Draw Title
        g.DrawString(title, font, brushText, 0, y);
        y += 18;

        int boxSize = 10;
        using var borderPen = new Pen(Color.White, 1);

        foreach (var item in items)
        {
            // Draw Box
            using (var brush = new SolidBrush(item.Color))
            {
                g.FillRectangle(brush, 1, y + 2, boxSize, boxSize);
            }
            g.DrawRectangle(borderPen, 1, y + 2, boxSize, boxSize);

            // Draw Text
            g.DrawString(item.Label, font, brushText, boxSize + 5, y);
            y += 18;
        }
    }

    private void UpdateSnapshot(RQGraph? graph, RenderNode[]? externalNodes = null)
    {
        try
        {
            VisualSnapshot? snapshot = null;

            if (graph != null)
            {
                if (graph.SpectralX == null || graph.SpectralX.Length == 0) return;

                snapshot = new VisualSnapshot
                {
                    NodeCount = graph.N,
                    SpectralDim = graph.SmoothedSpectralDimension,
                    X = new float[graph.N],
                    Y = new float[graph.N],
                    Z = new float[graph.N],
                    States = new NodeState[graph.N],
                    ClusterIds = new int[graph.N],
                    Edges = new List<(int, int, float)>()
                };

                // Initialize or reset persistent positions when manifold embedding is enabled
                if (_enableManifoldEmbedding)
                {
                    // Initialize persistent positions from spectral coords if not done or size changed
                    if (!_embeddingInitialized || _embeddingPositionX == null || _embeddingPositionX.Length != graph.N)
                    {
                        _embeddingPositionX = new float[graph.N];
                        _embeddingPositionY = new float[graph.N];
                        _embeddingPositionZ = new float[graph.N];
                        _embeddingVelocityX = new float[graph.N];
                        _embeddingVelocityY = new float[graph.N];
                        _embeddingVelocityZ = new float[graph.N];

                        for (int i = 0; i < graph.N; i++)
                        {
                            _embeddingPositionX[i] = (float)graph.SpectralX[i];
                            _embeddingPositionY[i] = (float)graph.SpectralY[i];
                            _embeddingPositionZ[i] = (float)graph.SpectralZ[i];
                        }
                        _embeddingInitialized = true;
                    }

                    // Use persistent positions
                    for (int i = 0; i < graph.N; i++)
                    {
                        snapshot.X[i] = _embeddingPositionX[i];
                        snapshot.Y[i] = _embeddingPositionY[i];
                        snapshot.Z[i] = _embeddingPositionZ[i];
                        snapshot.States[i] = graph.State[i];
                    }
                }
                else
                {
                    // Manifold disabled - use original spectral coords directly
                    // Note: ResetManifoldEmbedding() is called when checkbox is unchecked
                    for (int i = 0; i < graph.N; i++)
                    {
                        snapshot.X[i] = (float)graph.SpectralX[i];
                        snapshot.Y[i] = (float)graph.SpectralY[i];
                        snapshot.Z[i] = (float)graph.SpectralZ[i];
                        snapshot.States[i] = graph.State[i];
                    }
                }

                // Clusters
                Array.Fill(snapshot.ClusterIds, -1);
                try
                {
                    var clusters = graph.GetStrongCorrelationClusters(graph.GetAdaptiveHeavyThreshold());
                    for (int c = 0; c < clusters.Count; c++)
                        foreach (int node in clusters[c]) snapshot.ClusterIds[node] = c;
                }
                catch { }

                // Edges - always collect for manifold embedding even if not displaying
                double threshold = 0.5;
                int step = Math.Max(1, graph.N / 300);

                for (int i = 0; i < graph.N; i += step)
                {
                    foreach (int j in graph.Neighbors(i))
                    {
                        if (j > i)
                        {
                            float w = (float)graph.Weights[i, j];
                            if (w > threshold)
                            {
                                snapshot.Edges.Add((i, j, w));
                            }
                        }
                    }
                }

                // Apply manifold embedding if enabled - this updates persistent positions
                if (_enableManifoldEmbedding)
                {
                    ApplyManifoldEmbedding(snapshot, graph);
                }

                // Update quantum visualization data if available
                UpdateQuantumVisualization(graph);
            }
            else if (externalNodes != null && externalNodes.Length > 0)
            {
                int count = externalNodes.Length;

                // Get spectral dimension from external state if available
                var externalState = _lifeCycleManager?.TryGetExternalSimulationState();
                double spectralDim = externalState?.SpectralDimension ?? 0;

                snapshot = new VisualSnapshot
                {
                    NodeCount = count,
                    SpectralDim = spectralDim,
                    X = new float[count],
                    Y = new float[count],
                    Z = new float[count],
                    States = new NodeState[count],
                    ClusterIds = new int[count],
                    Edges = new List<(int, int, float)>()
                };

                // Initialize or update persistent positions for external simulation
                // This enables manifold embedding animation even without edge data
                if (_enableManifoldEmbedding)
                {
                    if (!_embeddingInitialized || _embeddingPositionX == null || _embeddingPositionX.Length != count)
                    {
                        _embeddingPositionX = new float[count];
                        _embeddingPositionY = new float[count];
                        _embeddingPositionZ = new float[count];
                        _embeddingVelocityX = new float[count];
                        _embeddingVelocityY = new float[count];
                        _embeddingVelocityZ = new float[count];

                        for (int i = 0; i < count; i++)
                        {
                            _embeddingPositionX[i] = externalNodes[i].X;
                            _embeddingPositionY[i] = externalNodes[i].Y;
                            _embeddingPositionZ[i] = externalNodes[i].Z;
                        }

                        // CRITICAL: Reconstruct edges ONCE from ORIGINAL server coordinates!
                        // These edges define the graph topology for manifold spring forces.
                        // We must NOT reconstruct edges from manifold-modified positions,
                        // as that would create isolated clusters with no inter-cluster connections.
                        snapshot.X = (float[])_embeddingPositionX.Clone();
                        snapshot.Y = (float[])_embeddingPositionY.Clone();
                        snapshot.Z = (float[])_embeddingPositionZ.Clone();
                        ReconstructEdgesFromProximity(snapshot);

                        // Cache the edges for all subsequent frames
                        _cachedExternalEdges = new List<(int, int, float)>(snapshot.Edges);
                        _cachedExternalEdgeCount = count;

                        _embeddingInitialized = true;
                    }

                    // Use manifold-controlled positions for display
                    for (int i = 0; i < count; i++)
                    {
                        snapshot.X[i] = _embeddingPositionX![i];
                        snapshot.Y[i] = _embeddingPositionY![i];
                        snapshot.Z[i] = _embeddingPositionZ![i];
                        // Map color R channel to state (Console sends R>0.5 for excited)
                        snapshot.States[i] = externalNodes[i].R > 0.5f ? NodeState.Excited : NodeState.Rest;
                    }

                    // Use CACHED edges from initial server coordinates, NOT reconstructed from current positions!
                    if (_cachedExternalEdges != null && _cachedExternalEdgeCount == count)
                    {
                        snapshot.Edges.Clear();
                        snapshot.Edges.AddRange(_cachedExternalEdges);
                    }

                    // Apply force-directed manifold embedding with spring forces
                    ApplyExternalManifoldEmbedding(snapshot);
                }
                else
                {
                    // No manifold embedding - use server coordinates directly
                    for (int i = 0; i < count; i++)
                    {
                        snapshot.X[i] = externalNodes[i].X;
                        snapshot.Y[i] = externalNodes[i].Y;
                        snapshot.Z[i] = externalNodes[i].Z;
                        snapshot.States[i] = externalNodes[i].R > 0.5f ? NodeState.Excited : NodeState.Rest;
                    }
                }
            }

            if (snapshot == null) return;

            _lastSnapshot = snapshot;

            _panel3D.Invoke((MethodInvoker)delegate
            {
                string embeddingStatus = _enableManifoldEmbedding ? "\n[Manifold: ON]" : "";
                _lblStats.Text = $"Nodes: {snapshot.NodeCount}\nd_S: {snapshot.SpectralDim:F2}\nMode: {_visualMode}{embeddingStatus}";
            });

            // Removed the block that was overwriting the legend label
        }
        catch { }
    }

    /// <summary>
    /// Updates the stats label with snapshot information.
    /// </summary>
    private void UpdateStatsLabel(VisualSnapshot snapshot)
    {
        try
        {
            _panel3D.Invoke((MethodInvoker)delegate
            {
                string embeddingStatus = _enableManifoldEmbedding ? "\n[Manifold: ON]" : "";
                string modeInfo = _isExternalSimulation ? "\n[Console Mode]" : "";
                _lblStats.Text = $"Nodes: {snapshot.NodeCount}\nd_S: {snapshot.SpectralDim:F2}\nMode: {_visualMode}{embeddingStatus}{modeInfo}";
            });
        }
        catch { }
    }

    /// <summary>
    /// Reconstructs edge list from node positions using proximity-based heuristic.
    /// This enables manifold embedding spring forces when edge data isn't available from server.
    /// 
    /// CRITICAL: For console mode, we need edges to enable manifold embedding which creates
    /// the cohesive sphere structure. Without edges, manifold has no spring forces.
    /// </summary>
    private void ReconstructEdgesFromProximity(VisualSnapshot snapshot)
    {
        int n = snapshot.NodeCount;
        snapshot.Edges.Clear();

        if (n < 2) return;

        // For console mode spectral coordinates (range ~[-15,15]):
        // Calculate coordinate ranges to determine adaptive threshold
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        for (int i = 0; i < n; i++)
        {
            if (snapshot.X[i] < minX) minX = snapshot.X[i];
            if (snapshot.X[i] > maxX) maxX = snapshot.X[i];
            if (snapshot.Y[i] < minY) minY = snapshot.Y[i];
            if (snapshot.Y[i] > maxY) maxY = snapshot.Y[i];
            if (snapshot.Z[i] < minZ) minZ = snapshot.Z[i];
            if (snapshot.Z[i] > maxZ) maxZ = snapshot.Z[i];
        }

        float rangeX = maxX - minX;
        float rangeY = maxY - minY;
        float rangeZ = maxZ - minZ;
        float maxRange = Math.Max(rangeX, Math.Max(rangeY, rangeZ));

        if (maxRange < 0.01f) maxRange = 1f;

        // Use larger k for denser connections (creates cohesive manifold)
        const int k = 6;  // Connect to 6 nearest neighbors

        // Adaptive threshold based on node count and coordinate range
        // For a uniform sphere, expected avg distance scales with range / cbrt(n)
        float expectedAvgDist = maxRange / MathF.Pow(n, 0.33f);
        float distanceThreshold = expectedAvgDist * 3.0f;  // 3x expected distance
        float distanceThresholdSq = distanceThreshold * distanceThreshold;

        // Build edge list - connect each node to k nearest neighbors within threshold
        for (int i = 0; i < n; i++)
        {
            var neighbors = new List<(int j, float distSq)>(n);
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;

                float dx = snapshot.X[i] - snapshot.X[j];
                float dy = snapshot.Y[i] - snapshot.Y[j];
                float dz = snapshot.Z[i] - snapshot.Z[j];
                float distSq = dx * dx + dy * dy + dz * dz;

                if (distSq < distanceThresholdSq)
                {
                    neighbors.Add((j, distSq));
                }
            }

            // Sort by distance and take k nearest
            neighbors.Sort((a, b) => a.distSq.CompareTo(b.distSq));
            int edgesToAdd = Math.Min(k, neighbors.Count);

            for (int e = 0; e < edgesToAdd; e++)
            {
                var (j, distSq) = neighbors[e];

                // Avoid duplicates (only add if i < j)
                if (i < j)
                {
                    float dist = MathF.Sqrt(distSq);
                    // Weight inversely proportional to distance, normalized by expected distance
                    float weight = 1.0f / (1.0f + dist / expectedAvgDist);
                    snapshot.Edges.Add((i, j, weight));
                }
            }
        }
    }

    /// <summary>
    /// Applies force-directed manifold embedding for external simulation nodes.
    /// Uses reconstructed edges for spring forces to create folding dynamics.
    /// FIX 25: Now uses SAME parameters and formulas as local mode!
    /// </summary>
    private void ApplyExternalManifoldEmbedding(VisualSnapshot snapshot)
    {
        int n = snapshot.NodeCount;
        if (_embeddingVelocityX == null || _embeddingPositionX == null) return;
        if (_embeddingVelocityX.Length != n || _embeddingPositionX.Length != n) return;

        // FIX 25: Use SAME physics constants as local mode (from PartialForm3D.cs fields)
        float repulsionStrength = (float)ManifoldRepulsionFactor;  // Was 0.3, now 0.5
        float springStrength = (float)ManifoldSpringFactor;        // Was 0.5, now 0.8
        float damping = (float)ManifoldDamping;                    // 0.85
        float dt = (float)ManifoldDeltaTime;                       // 0.05

        // Calculate center of mass
        float comX = 0, comY = 0, comZ = 0;
        for (int i = 0; i < n; i++)
        {
            comX += snapshot.X[i];
            comY += snapshot.Y[i];
            comZ += snapshot.Z[i];
        }
        comX /= n;
        comY /= n;
        comZ /= n;

        // Allocate force arrays
        float[] forceX = new float[n];
        float[] forceY = new float[n];
        float[] forceZ = new float[n];

        // 1. Global repulsion from center (same formula as local mode)
        for (int i = 0; i < n; i++)
        {
            float dx = snapshot.X[i] - comX;
            float dy = snapshot.Y[i] - comY;
            float dz = snapshot.Z[i] - comZ;
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz) + 0.1f;

            // FIX 25: Same formula as local mode: repulsion / (dist * dist)
            float force = repulsionStrength / (dist * dist);
            forceX[i] += dx / dist * force;
            forceY[i] += dy / dist * force;
            forceZ[i] += dz / dist * force;
        }

        // 2. Spring attraction along edges (same formula as local mode)
        foreach (var (u, v, w) in snapshot.Edges)
        {
            if (u >= n || v >= n) continue;

            float dx = snapshot.X[v] - snapshot.X[u];
            float dy = snapshot.Y[v] - snapshot.Y[u];
            float dz = snapshot.Z[v] - snapshot.Z[u];
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz) + 0.01f;

            // FIX 25: Same formula as local mode
            float targetDist = 1.0f / (w + 0.1f);
            float springForce = springStrength * w * (dist - targetDist);

            float fx = dx / dist * springForce;
            float fy = dy / dist * springForce;
            float fz = dz / dist * springForce;

            forceX[u] += fx;
            forceY[u] += fy;
            forceZ[u] += fz;
            forceX[v] -= fx;
            forceY[v] -= fy;
            forceZ[v] -= fz;
        }

        // 3. Integration with damping (FIX 25: same formula as local mode with dt multiplication)
        for (int i = 0; i < n; i++)
        {
            _embeddingVelocityX![i] = (_embeddingVelocityX[i] + forceX[i] * dt) * damping;
            _embeddingVelocityY![i] = (_embeddingVelocityY[i] + forceY[i] * dt) * damping;
            _embeddingVelocityZ![i] = (_embeddingVelocityZ[i] + forceZ[i] * dt) * damping;

            _embeddingPositionX![i] += _embeddingVelocityX[i] * dt;
            _embeddingPositionY![i] += _embeddingVelocityY[i] * dt;
            _embeddingPositionZ![i] += _embeddingVelocityZ[i] * dt;

            snapshot.X[i] = _embeddingPositionX[i];
            snapshot.Y[i] = _embeddingPositionY[i];
            snapshot.Z[i] = _embeddingPositionZ[i];
        }
    }

    /// <summary>
    /// Applies force-directed graph embedding (manifold embedding) based on RQ-hypothesis.
    /// 
    /// RQ-HYPOTHESIS: Distance is not predefined but derived from interaction strength (edge weight).
    /// Nodes with strong connections (high weight) are pulled closer together.
    /// 
    /// Energy function: E = ?(w_ij * |r_i - r_j|?) + k * ?(1/|r_i - r_j|)
    /// - First term: spring attraction proportional to edge weight
    /// - Second term: global repulsion to prevent collapse
    /// </summary>
    private void ApplyManifoldEmbedding(VisualSnapshot snapshot, RQGraph graph)
    {
        int n = snapshot.NodeCount;

        // Ensure buffers exist (should already be initialized in UpdateSnapshot)
        if (_embeddingVelocityX == null || _embeddingPositionX == null) return;
        if (_embeddingVelocityX.Length != n || _embeddingPositionX.Length != n) return;

        float[] forceX = new float[n];
        float[] forceY = new float[n];
        float[] forceZ = new float[n];

        // Calculate center of mass for repulsion reference
        float comX = 0, comY = 0, comZ = 0;
        for (int i = 0; i < n; i++)
        {
            comX += snapshot.X[i];
            comY += snapshot.Y[i];
            comZ += snapshot.Z[i];
        }
        comX /= n;
        comY /= n;
        comZ /= n;

        // 1. Global repulsion from center (prevents collapse)
        for (int i = 0; i < n; i++)
        {
            float dx = snapshot.X[i] - comX;
            float dy = snapshot.Y[i] - comY;
            float dz = snapshot.Z[i] - comZ;
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz) + 0.1f;

            float repulsion = (float)(ManifoldRepulsionFactor / (dist * dist));
            forceX[i] += dx / dist * repulsion;
            forceY[i] += dy / dist * repulsion;
            forceZ[i] += dz / dist * repulsion;
        }

        // 2. Spring attraction along edges (Hooke's law with weight as spring constant)
        foreach (var (u, v, w) in snapshot.Edges)
        {
            if (u >= n || v >= n) continue;

            float dx = snapshot.X[v] - snapshot.X[u];
            float dy = snapshot.Y[v] - snapshot.Y[u];
            float dz = snapshot.Z[v] - snapshot.Z[u];
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz) + 0.01f;

            // Spring force: F = k * x, where k = weight (stronger connection = stiffer spring)
            // Target distance inversely proportional to weight
            float targetDist = 1.0f / (w + 0.1f);
            float springForce = (float)(ManifoldSpringFactor * w * (dist - targetDist));

            float fx = dx / dist * springForce;
            float fy = dy / dist * springForce;
            float fz = dz / dist * springForce;

            forceX[u] += fx;
            forceY[u] += fy;
            forceZ[u] += fz;
            forceX[v] -= fx;
            forceY[v] -= fy;
            forceZ[v] -= fz;
        }

        // 3. Integration (Verlet-like with damping)
        float dt = (float)ManifoldDeltaTime;
        float damping = (float)ManifoldDamping;

        for (int i = 0; i < n; i++)
        {
            _embeddingVelocityX[i] = (_embeddingVelocityX[i] + forceX[i] * dt) * damping;
            _embeddingVelocityY![i] = (_embeddingVelocityY[i] + forceY[i] * dt) * damping;
            _embeddingVelocityZ![i] = (_embeddingVelocityZ[i] + forceZ[i] * dt) * damping;

            // Update persistent positions
            _embeddingPositionX[i] += _embeddingVelocityX[i] * dt;
            _embeddingPositionY![i] += _embeddingVelocityY[i] * dt;
            _embeddingPositionZ![i] += _embeddingVelocityZ[i] * dt;

            // Copy to snapshot for rendering
            snapshot.X[i] = _embeddingPositionX[i];
            snapshot.Y[i] = _embeddingPositionY[i];
            snapshot.Z[i] = _embeddingPositionZ[i];
        }
    }

    private void Panel3D_MouseDown(object? sender, MouseEventArgs e)
    {
        _lastMouse3D = e.Location;
    }

    private void Panel3D_MouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _yaw3D += (e.X - _lastMouse3D.X) * 0.01f;
            _pitch3D += (e.Y - _lastMouse3D.Y) * 0.01f;
            _lastMouse3D = e.Location;
            _panel3D.Invalidate();
        }
    }

    private void Panel3D_MouseWheel(object? sender, MouseEventArgs e)
    {
        _zoom3D += e.Delta * 0.1f;
        if (_zoom3D < 1.0f) _zoom3D = 1.0f;
        _panel3D.Invalidate();
    }

    private void Panel3D_Paint(object? sender, PaintEventArgs e)
    {
        var snapshot = _lastSnapshot;
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        float cx = _panel3D.Width / 2.0f;
        float cy = _panel3D.Height / 2.0f;
        float cosY = (float)Math.Cos(_yaw3D), sinY = (float)Math.Sin(_yaw3D);
        float cosP = (float)Math.Cos(_pitch3D), sinP = (float)Math.Sin(_pitch3D);

        // Draw Grid (Background)
        if (_showGrid)
        {
            DrawGrid(g, cx, cy, cosY, sinY, cosP, sinP);
        }

        if (snapshot == null)
        {
            e.Graphics.DrawString("No Data. Run Simulation & Update Spectral Coords.", SystemFonts.DefaultFont, Brushes.White, 200, 200);
            return;
        }

        int n = snapshot.NodeCount;
        var points2D = new PointF[n];
        var depths = new float[n];

        // Project Points
        for (int i = 0; i < n; i++)
        {
            var p = ProjectPoint(snapshot.X[i], snapshot.Y[i], snapshot.Z[i], cx, cy, cosY, sinY, cosP, sinP, out float z2);
            points2D[i] = p;
            depths[i] = z2;
        }

        Color[] palette = new Color[] { Color.Lime, Color.Yellow, Color.Magenta, Color.Orange, Color.Cyan, Color.Pink };

        // Draw Edges
        if (_showEdges && snapshot.Edges != null)
        {
            foreach (var edge in snapshot.Edges)
            {
                if (edge.u < n && edge.v < n)
                {
                    Color edgeColor = Color.FromArgb(40, 0, 255, 0);
                    float width = 1;

                    if (_visualMode == "Clusters")
                    {
                        int c1 = snapshot.ClusterIds[edge.u];
                        int c2 = snapshot.ClusterIds[edge.v];
                        if (c1 == c2 && c1 != -1)
                        {
                            edgeColor = palette[c1 % palette.Length];
                            width = 2; // Thicker for cluster internal edges
                        }
                    }
                    else if (_visualMode == "Target")
                    {
                        // Use target-specific edge styling
                        var (targetEdgeColor, targetWidth) = GetTargetEdgeStyle(edge.u, edge.v, edge.w, snapshot);
                        edgeColor = targetEdgeColor;
                        width = targetWidth;
                    }

                    int alpha = _visualMode == "Clusters" ? 150 : (_visualMode == "Target" ? edgeColor.A : 40);
                    using (var pen = new Pen(Color.FromArgb(Math.Min(255, alpha), edgeColor), width))
                    {
                        g.DrawLine(pen, points2D[edge.u], points2D[edge.v]);
                    }
                }
            }
        }

        // Draw Nodes
        for (int i = 0; i < n; i++)
        {
            float size = 4.0f;
            int alpha = (int)Math.Clamp(255 - (depths[i] + 100), 50, 255);

            Color baseColor = Color.White;
            switch (_visualMode)
            {
                case "State":
                    baseColor = snapshot.States[i] == NodeState.Excited ? Color.Red : Color.Cyan;
                    break;
                case "Clusters":
                    baseColor = snapshot.ClusterIds[i] >= 0 ? palette[snapshot.ClusterIds[i] % palette.Length] : Color.FromArgb(50, 50, 50);
                    break;
                case "Quantum":
                    baseColor = GetQuantumColor(i, 255);
                    break;
                case "Target":
                    baseColor = GetTargetStateColor(i, snapshot, 255);
                    size = 5.0f; // Slightly larger nodes for target mode
                    break;
                case "Manifold":
                    // Stability-based coloring: red (high connectivity/singularity) to blue (low connectivity/flat)
                    // Use edge count as proxy for local curvature/stability
                    int edgeCount = 0;
                    float totalWeight = 0f;
                    foreach (var edge in snapshot.Edges)
                    {
                        if (edge.u == i || edge.v == i)
                        {
                            edgeCount++;
                            totalWeight += edge.w;
                        }
                    }
                    float stability = Math.Clamp(totalWeight / (edgeCount + 1), 0f, 1f);
                    int r = (int)(255 * (1 - stability));
                    int b = (int)(255 * stability);
                    int gr = (int)(128 * Math.Abs(stability - 0.5f) * 2);
                    baseColor = Color.FromArgb(r, gr, b);
                    break;
                default: // Depth
                    baseColor = Color.FromArgb(100, 200, 255);
                    break;
            }

            using (var brush = new SolidBrush(Color.FromArgb(alpha, baseColor)))
            {
                g.FillEllipse(brush, points2D[i].X - size / 2, points2D[i].Y - size / 2, size, size);
            }
        }

        // Topology Status
        DrawTopologyStatus(g, snapshot.SpectralDim);
    }

    private void DrawGrid(Graphics g, float cx, float cy, float cosY, float sinY, float cosP, float sinP)
    {
        int lines = 8;
        float size = 1.5f;
        float step = size * 2 / lines;
        float yLevel = 1.2f; // Below the graph

        using var pen = new Pen(Color.FromArgb(40, 100, 255, 255), 1);

        for (int i = 0; i <= lines; i++)
        {
            float val = -size + i * step;

            // Z-lines
            var p1 = ProjectPoint(val, yLevel, -size, cx, cy, cosY, sinY, cosP, sinP, out _);
            var p2 = ProjectPoint(val, yLevel, size, cx, cy, cosY, sinY, cosP, sinP, out _);
            g.DrawLine(pen, p1, p2);

            // X-lines
            var p3 = ProjectPoint(-size, yLevel, val, cx, cy, cosY, sinY, cosP, sinP, out _);
            var p4 = ProjectPoint(size, yLevel, val, cx, cy, cosY, sinY, cosP, sinP, out _);
            g.DrawLine(pen, p3, p4);
        }

        // Draw axes hint
        using var penX = new Pen(Color.Red, 2);
        using var penZ = new Pen(Color.Blue, 2);
        var origin = ProjectPoint(0, yLevel, 0, cx, cy, cosY, sinY, cosP, sinP, out _);
        var axisX = ProjectPoint(0.5f, yLevel, 0, cx, cy, cosY, sinY, cosP, sinP, out _);
        var axisZ = ProjectPoint(0, yLevel, 0.5f, cx, cy, cosY, sinY, cosP, sinP, out _);
        g.DrawLine(penX, origin, axisX);
        g.DrawLine(penZ, origin, axisZ);
    }

    private PointF ProjectPoint(float x, float y, float z, float cx, float cy, float cosY, float sinY, float cosP, float sinP, out float depth)
    {
        x *= _zoom3D; y *= _zoom3D; z *= _zoom3D;

        float x1 = x * cosY - z * sinY;
        float z1 = x * sinY + z * cosY;
        float y1 = y * cosP - z1 * sinP;
        float z2 = y * sinP + z1 * cosP;

        float cameraDist = 1000.0f;
        float scale = cameraDist / (cameraDist + z2);

        depth = z2;
        return new PointF(cx + x1 * scale, cy + y1 * scale);
    }

    private void DrawTopologyStatus(Graphics g, double ds)
    {
        string status = "";
        Color color = Color.Gray;

        if (ds < 1.5) { status = "0D/1D Dust (Disconnected)"; color = Color.Gray; }
        else if (ds < 2.5) { status = "1D Filament (String-like)"; color = Color.Cyan; }
        else if (ds < 3.5) { status = "2D Membrane (Sheet-like)"; color = Color.Yellow; }
        else if (ds < 4.5) { status = "3D Bulk (Emergent Space)"; color = Color.Lime; }
        else { status = "High-D / Complex"; color = Color.Magenta; }

        string msg = $"Topology: {status} (d_S ~ {ds:F2})";
        var font = new Font(SystemFonts.DefaultFont.FontFamily, 12, FontStyle.Bold);
        var size = g.MeasureString(msg, font);

        // Draw background for text
        using (var bgBrush = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        {
            g.FillRectangle(bgBrush, _panel3D.Width - size.Width - 15, _panel3D.Height - 45, size.Width + 10, size.Height + 5);
        }

        using (var brush = new SolidBrush(color))
        {
            g.DrawString(msg, font, brush, _panel3D.Width - size.Width - 10, _panel3D.Height - 40);
        }

        // Draw Emergent Space Indicator if applicable
        if (ds >= 3.5 && ds <= 4.5)
        {
            using (var pen = new Pen(Color.FromArgb(100, 255, 0, 0), 5))
            {
                g.DrawRectangle(pen, 0, 0, _panel3D.Width - 1, _panel3D.Height - 1);
            }
        }

        // Draw Manifold Embedding indicator when active
        if (_enableManifoldEmbedding)
        {
            string embMsg = "MANIFOLD EMBEDDING ACTIVE";
            using var embFont = new Font(SystemFonts.DefaultFont.FontFamily, 10, FontStyle.Bold);
            var embSize = g.MeasureString(embMsg, embFont);

            // Draw pulsing orange border
            using (var pen = new Pen(Color.FromArgb(150, 255, 165, 0), 3))
            {
                g.DrawRectangle(pen, 2, 2, _panel3D.Width - 5, _panel3D.Height - 5);
            }

            // Draw label at top-right
            using (var bgBrush = new SolidBrush(Color.FromArgb(180, 40, 40, 40)))
            {
                g.FillRectangle(bgBrush, _panel3D.Width - embSize.Width - 15, 10, embSize.Width + 10, embSize.Height + 4);
            }
            using (var brush = new SolidBrush(Color.Orange))
            {
                g.DrawString(embMsg, embFont, brush, _panel3D.Width - embSize.Width - 10, 12);
            }
        }
    }
}

