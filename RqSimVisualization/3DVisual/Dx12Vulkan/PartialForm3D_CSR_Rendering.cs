using System.Numerics;
using System.Linq;
using ImGuiNET;
using RqSimRenderingEngine.Abstractions;
using RqSimRenderingEngine.Rendering.Backend.DX12;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using RQSimulation;
using Vortice.Mathematics;
using RqSimForms.ProcessesDispatcher.Contracts;

namespace RqSimVisualization;

public partial class RqSimVisualizationForm
{
    private DateTime _csrLastFrameTime = DateTime.Now;
    private float _csrFps;
    private int _csrFrameCount;
    private DateTime _csrFpsUpdateTime = DateTime.Now;

    // Cached graph data for rendering
    private int _csrNodeCount;
    private int _csrEdgeCount;
    private float[]? _csrNodeX;
    private float[]? _csrNodeY;
    private float[]? _csrNodeZ;
    private NodeState[]? _csrNodeStates;
    private List<(int u, int v, float w)>? _csrEdgeList;
    private double _csrSpectralDim;
    private DateTime _csrLastDebugLog = DateTime.MinValue;

    private void TimerCsr3D_Tick(object? sender, EventArgs e)
    {
        if (!_csrVisualizationInitialized) return;
        if (_csrRenderHost is null) return;

        // Skip rendering when simulation is not active (local mode stopped)
        if (_hasApiConnection && !_isExternalSimulation && !_simApi.IsModernRunning)
            return;

        if (tabControl_Main.SelectedTab == tabPage_3DVisualCSR)
            RenderCsrVisualizationFrame();
    }

    /// <summary>
    /// Clears all cached graph data and resets the renderer buffers.
    /// Called when starting a new simulation to prevent "ghost" data.
    /// </summary>
    private void ClearCsrVisualizationData()
    {
        _csrNodeCount = 0;
        _csrEdgeCount = 0;
        _csrNodeX = null;
        _csrNodeY = null;
        _csrNodeZ = null;
        _csrNodeStates = null;
        _csrEdgeList?.Clear();

        // Clear the cached graph from previous session to prevent "ghost" overlay
        _simApi?.ClearCachedGraph();

        // Reset manifold embedding state
        ResetManifoldEmbedding();

        // Clear renderer buffers
        if (_csrRenderHost is Dx12RenderHost dx12Host)
        {
            dx12Host.SetNodeInstances(Array.Empty<Dx12NodeInstance>(), 0);
            dx12Host.SetEdgeVertices(Array.Empty<Dx12LineVertex>(), 0);
        }

        // Clear standalone form if open
        _standalone3DForm?.ClearData();

        // Force overlay update
        _csrIsWaitingForData = true;
        UpdateCsrWaitingLabelVisibility(true);
    }

    private void RenderCsrVisualizationFrame()
    {
        if (_csrRenderHost is null) return;

        // If device is lost, stop trying to render
        if (_csrDx12Host?.IsDeviceLost == true)
        {
            // Only log once per second to avoid spam
            if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 1)
            {
                System.Diagnostics.Debug.WriteLine("[CSR Render] Skipping frame - DX12 device lost");
                _csrLastDebugLog = DateTime.Now;
            }
            return;
        }

        bool beganFrame = false;

        try
        {
            // Throttled diagnostics: confirm which host is active and panel state
            if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 1)
            {
                var hostType = _csrRenderHost.GetType().FullName ?? _csrRenderHost.GetType().Name;
                var dx12 = _csrRenderHost is Dx12RenderHost;
                var hwnd = _csrRenderPanel?.Handle ?? IntPtr.Zero;
                System.Diagnostics.Debug.WriteLine($"[CSR Render] Host={hostType}, IsDx12={dx12}, HWND=0x{hwnd.ToInt64():X}, Size={_csrRenderPanel?.Width}x{_csrRenderPanel?.Height}, Visible={_csrRenderPanel?.Visible}");
            }

            // BeginFrame first so we always have a chance to present even if simulation throws
            _csrRenderHost.BeginFrame();
            beganFrame = true;

            if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 1)
            {
                System.Diagnostics.Debug.WriteLine("[CSR Render] BeginFrame called");
            }

            // Update cached graph data from simulation (best-effort)
            try
            {
                UpdateCsrGraphData();
            }
            catch (Exception ex)
            {
                if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 1)
                {
                    System.Diagnostics.Debug.WriteLine($"[CSR Render] UpdateCsrGraphData threw: {ex.GetType().Name}: {ex.Message}");
                }
                // Keep last valid cached data to avoid blank frames
            }

            // Clear with dark background
            if (_csrDx12Host is not null)
            {
                _csrDx12Host.Clear(new Color4(0.02f, 0.02f, 0.05f, 1f));
            }

            // Draw 3D content via ImGui draw lists
            DrawCsr3DContent();

            // Draw ImGui overlay with graph info
            DrawCsrImGuiOverlay();

            UpdateCsrStats();
        }
        catch (Exception ex)
        {
            // Avoid log spam; show full frame errors at most once per second
            if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 1)
            {
                System.Diagnostics.Debug.WriteLine($"[CSR Render] Frame error: {ex.GetType().Name}: {ex.Message}");
            }
        }
        finally
        {
            if (beganFrame)
            {
                try
                {
                    _csrRenderHost.EndFrame();
                }
                catch (Exception ex)
                {
                    if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CSR Render] EndFrame error: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 1)
                {
                    System.Diagnostics.Debug.WriteLine("[CSR Render] EndFrame called");
                    _csrLastDebugLog = DateTime.Now;
                }
            }
        }
    }

    private void UpdateCsrGraphData()
    {
        RQGraph? graph = null;

        // Debug: Log external simulation state on each update (throttled)
        if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 2)
        {
            var externalNodes = _lifeCycleManager?.GetExternalRenderNodes();
            System.Diagnostics.Debug.WriteLine($"[CSR UpdateData] _isExternalSimulation={_isExternalSimulation}, externalNodes={(externalNodes?.Length ?? -1)}, _csrNodeCount={_csrNodeCount}");
        }

        // First, check if we have an external simulation via shared memory
        // IMPORTANT: Only check external nodes when _isExternalSimulation flag is set
        // Otherwise this breaks local simulation mode
        if (_isExternalSimulation)
        {
            var externalNodes = _lifeCycleManager?.GetExternalRenderNodes();
            if (externalNodes is not null && externalNodes.Length > 0)
            {
                // Use external simulation data from shared memory
                UpdateCsrFromExternalNodes(externalNodes);
                return;
            }
        }

        // Use ActiveGraph which persists after simulation ends
        graph = _simApi?.ActiveGraph;

        // If we have a running simulation, also cache the graph
        if (_simApi?.SimulationEngine?.Graph is not null)
        {
            _simApi.CacheActiveGraph(_simApi.SimulationEngine.Graph);
            graph = _simApi.SimulationEngine.Graph;
        }

        if (graph is null)
        {
            // Throttled debug logging (once per second max)
            if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 1)
            {
                System.Diagnostics.Debug.WriteLine($"[CSR Render] Graph is null. ActiveGraph={_simApi?.ActiveGraph is not null}");
            }

            // Generate test data if no graph available (verifies render pipeline)
            if (_csrNodeCount == 0)
            {
                GenerateTestVisualizationData();
            }
            return;
        }

        try
        {
            int n = graph.N;
            if (n == 0)
            {
                // Keep existing cached data if simulation is unstable; don't force blank
                _csrNodeCount = 0;
                _csrEdgeCount = 0;
                return;
            }

            _csrNodeCount = n;
            _csrSpectralDim = graph.SmoothedSpectralDimension;

            // Resize arrays if needed
            if (_csrNodeX is null || _csrNodeX.Length != n)
            {
                _csrNodeX = new float[n];
                _csrNodeY = new float[n];
                _csrNodeZ = new float[n];
                _csrNodeStates = new NodeState[n];
            }

            // Use SpectralX/Y/Z if available, otherwise fall back to 2D Coordinates
            bool hasSpectral = graph.SpectralX is not null && graph.SpectralX.Length == n;

#pragma warning disable CS0618 // Coordinates is obsolete but needed for visualization
            bool hasCoords = graph.Coordinates is not null && graph.Coordinates.Length == n;
#pragma warning restore CS0618

            // 1. Position Initialization (Manifold or Standard)
            if (_enableManifoldEmbedding)
            {
                if (NeedsManifoldInitialization(n))
                {
                    InitializeManifoldPositions(graph);
                }

                for (int i = 0; i < n; i++)
                {
                    _csrNodeX[i] = _embeddingPositionX![i];
                    _csrNodeY[i] = _embeddingPositionY![i];
                    _csrNodeZ[i] = _embeddingPositionZ![i];
                    _csrNodeStates![i] = graph.State[i];
                }
            }
            else
            {
                if (hasSpectral)
                {
                    for (int i = 0; i < n; i++)
                    {
                        _csrNodeX[i] = (float)graph.SpectralX![i];
                        _csrNodeY[i] = (float)graph.SpectralY![i];
                        _csrNodeZ[i] = (float)graph.SpectralZ![i];
                        _csrNodeStates![i] = graph.State[i];
                    }
                }
                else if (hasCoords)
                {
#pragma warning disable CS0618
                    for (int i = 0; i < n; i++)
                    {
                        _csrNodeX[i] = (float)graph.Coordinates[i].X;
                        _csrNodeY[i] = (float)graph.Coordinates[i].Y;
                        _csrNodeZ[i] = 0f; // 2D coordinates, Z = 0
                        _csrNodeStates![i] = graph.State[i];
                    }
#pragma warning restore CS0618
                }
                else
                {
                    // No coordinates - generate simple grid layout
                    int gridSize = (int)Math.Ceiling(Math.Sqrt(n));
                    float spacing = 2.0f;
                    for (int i = 0; i < n; i++)
                    {
                        int gx = i % gridSize;
                        int gy = i / gridSize;
                        _csrNodeX[i] = (gx - gridSize / 2f) * spacing;
                        _csrNodeY[i] = (gy - gridSize / 2f) * spacing;
                        _csrNodeZ[i] = 0f;
                        _csrNodeStates![i] = graph.State[i];
                    }
                }
            }

            // Build edge list using Neighbors() method
            _csrEdgeList ??= new List<(int, int, float)>(n * 4);
            _csrEdgeList.Clear();

            // Log coordinate range for debugging (once per second)
            if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 1)
            {
                float minX = _csrNodeX.Take(n).Min();
                float maxX = _csrNodeX.Take(n).Max();
                float minY = _csrNodeY!.Take(n).Min();
                float maxY = _csrNodeY.Take(n).Max();
                float rangeX = maxX - minX;
                float rangeY = maxY - minY;
                System.Diagnostics.Debug.WriteLine($"[CSR Data] Coordinate range: X=[{minX:F3}, {maxX:F3}] (range={rangeX:F3}), Y=[{minY:F3}, {maxY:F3}] (range={rangeY:F3}), HasSpectral={hasSpectral}, HasCoords={hasCoords}");
            }

            // Collect edges if showing edges OR if manifold embedding is enabled
            if (_csrShowEdges || _enableManifoldEmbedding)
            {
                double threshold = _csrEdgeWeightThreshold;
                int step = Math.Max(1, n / 500); // Sample edges for large graphs

                for (int i = 0; i < n; i += step)
                {
                    foreach (int j in graph.Neighbors(i))
                    {
                        if (j > i)
                        {
                            float w = (float)graph.Weights[i, j];
                            if (w >= threshold)
                            {
                                _csrEdgeList.Add((i, j, w));
                            }
                        }
                    }
                }
            }
            _csrEdgeCount = _csrEdgeList.Count;

            // 2. Update Manifold Physics
            if (_enableManifoldEmbedding)
            {
                UpdateManifoldEmbedding(n, _csrNodeX, _csrNodeY, _csrNodeZ, _csrEdgeList);
            }

            // 3. Update Cluster IDs (for Clusters visualization mode)
            if (_csrVisMode == CsrVisualizationMode.Clusters)
            {
                UpdateCsrClusterIds(graph, n);
            }

            // 4. Update Target Metrics (only if overlay is shown)
            if (_showTargetOverlay)
            {
                UpdateTargetMetrics(graph);
            }
        }
        catch (Exception ex)
        {
            // Keep last valid cached data; don't force blank.
            if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 1)
            {
                System.Diagnostics.Debug.WriteLine($"[CSR Render] UpdateCsrGraphData error: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Updates CSR graph data from external simulation nodes received via shared memory.
    /// This is used when UI is connected to console server mode.
    /// </summary>
    private void UpdateCsrFromExternalNodes(RqSimForms.ProcessesDispatcher.Contracts.RenderNode[] nodes)
    {
        int n = nodes.Length;
        if (n == 0)
        {
            _csrNodeCount = 0;
            _csrEdgeCount = 0;
            return;
        }

        _csrNodeCount = n;

        // Resize arrays if needed
        if (_csrNodeX is null || _csrNodeX.Length != n)
        {
            _csrNodeX = new float[n];
            _csrNodeY = new float[n];
            _csrNodeZ = new float[n];
            _csrNodeStates = new NodeState[n];
        }

        // FIX 23: Check simulation status - freeze positions when Stopped
        var externalState = _lifeCycleManager?.TryGetExternalSimulationState();
        bool isSimulationStopped = externalState?.Status == SimulationStatus.Stopped;

        // FIX 33: Load edges BEFORE applying manifold embedding!
        // Otherwise first frames have no edges (only repulsion), scattering nodes
        var externalEdges = _lifeCycleManager?.GetExternalRenderEdges();
        if (externalEdges is not null && externalEdges.Length > 0)
        {
            // Use real edges from shared memory
            _csrEdgeList ??= new List<(int, int, float)>(externalEdges.Length);
            _csrEdgeList.Clear();

            for (int e = 0; e < externalEdges.Length; e++)
            {
                int from = externalEdges[e].FromNode;
                int to = externalEdges[e].ToNode;
                float weight = externalEdges[e].Weight;

                // Validate edge indices
                if (from >= 0 && from < n && to >= 0 && to < n && from != to)
                {
                    _csrEdgeList.Add((from, to, weight));
                }
            }
            _csrEdgeCount = _csrEdgeList.Count;
        }
        else
        {
            // Fallback: Reconstruct edges from node positions using proximity
            ReconstructEdgesFromProximity(n);
        }

        // Apply manifold embedding for external data if enabled
        if (_enableManifoldEmbedding)
        {
            // FIX 30/31: Track node count to detect actual graph changes vs. tab switches
            // Only reinit if node count ACTUALLY changed (same fix as GDI+ mode)
            // REMOVED blending - it causes oscillation/pulsation with manifold forces!
            bool nodeCountChanged = _lastConsoleModeNodeCount != n;
            bool needsReinit = _embeddingPositionX is null 
                            || _embeddingPositionX.Length != n
                            || nodeCountChanged;

            if (needsReinit)
            {
                _embeddingPositionX = new float[n];
                _embeddingPositionY = new float[n];
                _embeddingPositionZ = new float[n];
                _embeddingVelocityX = new float[n];
                _embeddingVelocityY = new float[n];
                _embeddingVelocityZ = new float[n];

                // Initialize from server spectral coordinates
                // Console mode differs from local: server provides pre-computed spectral embedding
                for (int i = 0; i < n; i++)
                {
                    _embeddingPositionX[i] = nodes[i].X;
                    _embeddingPositionY[i] = nodes[i].Y;
                    _embeddingPositionZ[i] = nodes[i].Z;
                }

                _lastConsoleModeNodeCount = n;
                _embeddingInitialized = true;
            }
            // NOTE: No blending here! Manifold embedding is autonomous after init (like GDI+)

            // FIX 23: Only apply physics when simulation is Running or Paused
            // When Stopped, freeze positions to prevent animation after Terminate
            if (!isSimulationStopped)
            {
                // Apply force-directed manifold embedding (same physics as GDI+ mode)
                ApplyCsrExternalManifoldEmbedding(n);
            }

            // Use manifold-controlled positions for display
            for (int i = 0; i < n; i++)
            {
                _csrNodeX[i] = _embeddingPositionX![i];
                _csrNodeY[i] = _embeddingPositionY![i];
                _csrNodeZ[i] = _embeddingPositionZ![i];
                _csrNodeStates![i] = nodes[i].R > 0.5f ? NodeState.Excited : NodeState.Rest;
            }
        }
        else
        {
            // Copy positions directly from external nodes (no manifold)
            for (int i = 0; i < n; i++)
            {
                _csrNodeX[i] = nodes[i].X;
                _csrNodeY[i] = nodes[i].Y;
                _csrNodeZ[i] = nodes[i].Z;
                _csrNodeStates![i] = nodes[i].R > 0.5f ? NodeState.Excited : NodeState.Rest;
            }
        }

        // Throttled debug logging
        if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 1)
        {
            float minX = _csrNodeX.Min();
            float maxX = _csrNodeX.Max();
            float minY = _csrNodeY.Min();
            float maxY = _csrNodeY.Max();
            System.Diagnostics.Debug.WriteLine($"[CSR External] Loaded {n} nodes from shared memory. X=[{minX:F2},{maxX:F2}], Y=[{minY:F2},{maxY:F2}], Edges={_csrEdgeList?.Count ?? 0}, Manifold={_enableManifoldEmbedding}");
        }
    }

    /// <summary>
    /// Reconstructs edge list from node positions using proximity-based heuristic.
    /// This enables manifold embedding spring forces when edge data isn't available from server.
    /// </summary>
    private void ReconstructEdgesFromProximity(int n)
    {
        _csrEdgeList ??= new List<(int, int, float)>(n * 4);
        _csrEdgeList.Clear();

        if (n < 2 || _csrNodeX is null || _csrNodeY is null || _csrNodeZ is null)
        {
            _csrEdgeCount = 0;
            return;
        }

        // Calculate average nearest-neighbor distance to set adaptive threshold
        // Use k=4 nearest neighbors (typical graph connectivity)
        const int k = 4;
        float totalMinDist = 0;
        int sampleCount = Math.Min(n, 50); // Sample subset for performance
        int sampleStep = Math.Max(1, n / sampleCount);

        for (int i = 0; i < n; i += sampleStep)
        {
            float minDist = float.MaxValue;
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                float dx = _csrNodeX[i] - _csrNodeX[j];
                float dy = _csrNodeY[i] - _csrNodeY[j];
                float dz = _csrNodeZ[i] - _csrNodeZ[j];
                float dist = dx * dx + dy * dy + dz * dz;
                if (dist < minDist) minDist = dist;
            }
            if (minDist < float.MaxValue)
                totalMinDist += MathF.Sqrt(minDist);
        }

        float avgMinDist = totalMinDist / (n / sampleStep);
        float distanceThreshold = avgMinDist * 2.5f; // Connect nodes within ~2.5x average nearest distance
        float distanceThresholdSq = distanceThreshold * distanceThreshold;

        // Build edge list - for performance, limit to k nearest neighbors per node
        for (int i = 0; i < n; i++)
        {
            // Find k nearest neighbors
            var neighbors = new List<(int j, float distSq)>();
            for (int j = i + 1; j < n; j++) // Only consider j > i to avoid duplicates
            {
                float dx = _csrNodeX[i] - _csrNodeX[j];
                float dy = _csrNodeY[i] - _csrNodeY[j];
                float dz = _csrNodeZ[i] - _csrNodeZ[j];
                float distSq = dx * dx + dy * dy + dz * dz;

                if (distSq < distanceThresholdSq)
                {
                    neighbors.Add((j, distSq));
                }
            }

            // Take up to k nearest from this node
            neighbors.Sort((a, b) => a.distSq.CompareTo(b.distSq));
            int edgesToAdd = Math.Min(k, neighbors.Count);

            for (int e = 0; e < edgesToAdd; e++)
            {
                var (j, distSq) = neighbors[e];
                // Weight inversely proportional to distance (closer = stronger)
                float dist = MathF.Sqrt(distSq);
                float weight = 1.0f / (1.0f + dist * 0.5f);
                _csrEdgeList.Add((i, j, weight));
            }
        }

        _csrEdgeCount = _csrEdgeList.Count;
    }

    /// <summary>
    /// Applies force-directed manifold embedding for external simulation nodes.
    /// Uses reconstructed edges for spring forces to create folding dynamics.
    /// FIX 31: Uses same physics constants as GDI+ mode for consistent behavior.
    /// </summary>
    private void ApplyCsrExternalManifoldEmbedding(int n)
    {
        if (_embeddingVelocityX == null || _embeddingPositionX == null) return;
        if (_embeddingVelocityX.Length != n || _embeddingPositionX.Length != n) return;

        // FIX 31: Use SAME physics constants as GDI+ mode (from PartialForm3D.cs fields)
        const float repulsionStrength = 0.5f;   // Was 0.3, now matches ManifoldRepulsionFactor
        const float springStrength = 0.8f;       // Was 0.5, now matches ManifoldSpringFactor
        const float damping = 0.85f;             // ManifoldDamping
        const float dt = 0.05f;                  // ManifoldDeltaTime

        // Calculate center of mass
        float comX = 0, comY = 0, comZ = 0;
        for (int i = 0; i < n; i++)
        {
            comX += _embeddingPositionX[i];
            comY += _embeddingPositionY![i];
            comZ += _embeddingPositionZ![i];
        }
        comX /= n;
        comY /= n;
        comZ /= n;

        // Allocate force arrays
        Span<float> forceX = stackalloc float[n];
        Span<float> forceY = stackalloc float[n];
        Span<float> forceZ = stackalloc float[n];

        // 1. Global repulsion from center (drives expansion)
        for (int i = 0; i < n; i++)
        {
            float dx = _embeddingPositionX[i] - comX;
            float dy = _embeddingPositionY![i] - comY;
            float dz = _embeddingPositionZ![i] - comZ;
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz) + 0.1f;

            float force = repulsionStrength / (dist * dist);
            forceX[i] = dx / dist * force;
            forceY[i] = dy / dist * force;
            forceZ[i] = dz / dist * force;
        }

        // 2. Spring attraction along edges (keeps connected nodes together during folding)
        if (_csrEdgeList is not null)
        {
            foreach (var (u, v, w) in _csrEdgeList)
            {
                if (u >= n || v >= n) continue;

                float dx = _embeddingPositionX[v] - _embeddingPositionX[u];
                float dy = _embeddingPositionY![v] - _embeddingPositionY[u];
                float dz = _embeddingPositionZ![v] - _embeddingPositionZ[u];
                float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz) + 0.01f;

                // Spring force: F = k * w * (dist - restLength)
                // Stronger weight = stiffer spring = closer rest length
                float restLength = 1.0f / (w + 0.1f);
                float springForce = springStrength * w * (dist - restLength);

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
        }

        // 3. Apply forces with damping (FIX 33: multiply force by dt like GDI+ mode!)
        // Without * dt, forces were 20x stronger causing violent shaking
        for (int i = 0; i < n; i++)
        {
            _embeddingVelocityX![i] = (_embeddingVelocityX[i] + forceX[i] * dt) * damping;
            _embeddingVelocityY![i] = (_embeddingVelocityY[i] + forceY[i] * dt) * damping;
            _embeddingVelocityZ![i] = (_embeddingVelocityZ[i] + forceZ[i] * dt) * damping;

            _embeddingPositionX[i] += _embeddingVelocityX[i] * dt;
            _embeddingPositionY![i] += _embeddingVelocityY[i] * dt;
            _embeddingPositionZ![i] += _embeddingVelocityZ[i] * dt;
        }
    }


    private void DrawCsr3DContent()
    {
        // Safety check: don't call ImGui if device is lost
        if (_csrDx12Host?.IsDeviceLost == true)
        {
            System.Diagnostics.Debug.WriteLine("[CSR Draw] Skipping - DX12 device lost");
            return;
        }

        // Debug: Log state on each draw call (throttled)
        if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 2)
        {
            System.Diagnostics.Debug.WriteLine($"[CSR Draw] _csrNodeCount={_csrNodeCount}, _csrNodeX null={_csrNodeX is null}, _csrCamera null={_csrCamera is null}");
            _csrLastDebugLog = DateTime.Now;
        }

        if (_csrNodeCount == 0 || _csrNodeX is null || _csrCamera is null)
        {
            // Draw waiting overlay when no data available (matching standalone behavior)
            DrawCsrWaitingOverlayIfNeeded();
            return;
        }

        // Switch between rendering modes (matching Form_Rsim3DForm.DrawGraph)
        if (_csrRenderMode3D == CsrRenderMode3D.Gpu3D)
        {
            RenderCsrSceneGpu3D();
            return;
        }

        // === ImGui 2D Mode (CPU-based legacy rendering) ===
        // Use foreground draw list instead of background - it draws ON TOP of everything
        var drawList = ImGui.GetForegroundDrawList();
        int panelW = _csrRenderPanel?.Width ?? 800;
        int panelH = _csrRenderPanel?.Height ?? 600;
        float cx = panelW / 2f;
        float cy = panelH / 2f;

        // Simple 3D to 2D projection using orbit camera
        float cosYaw = MathF.Cos(_csrCamera.Yaw);
        float sinYaw = MathF.Sin(_csrCamera.Yaw);
        float cosPitch = MathF.Cos(_csrCamera.Pitch);
        float sinPitch = MathF.Sin(_csrCamera.Pitch);

        // Limit node count for stackalloc
        int count = Math.Min(_csrNodeCount, 8000);

        // First pass: compute data bounds and center
        float dataMinX = float.MaxValue, dataMaxX = float.MinValue;
        float dataMinY = float.MaxValue, dataMaxY = float.MinValue;
        float sumX = 0, sumY = 0;

        Span<Vector2> rotatedPos = stackalloc Vector2[count];
        Span<float> depths = stackalloc float[count];

        for (int i = 0; i < count; i++)
        {
            float x = _csrNodeX[i];
            float y = _csrNodeY![i];
            float z = _csrNodeZ![i];

            // Rotate around Y (yaw)
            float rx = x * cosYaw - z * sinYaw;
            float rz = x * sinYaw + z * cosYaw;

            // Rotate around X (pitch)
            float ry = y * cosPitch - rz * sinPitch;
            float rz2 = y * sinPitch + rz * cosPitch;

            rotatedPos[i] = new Vector2(rx, ry);
            depths[i] = rz2;

            dataMinX = Math.Min(dataMinX, rx);
            dataMaxX = Math.Max(dataMaxX, rx);
            dataMinY = Math.Min(dataMinY, ry);
            dataMaxY = Math.Max(dataMaxY, ry);
            sumX += rx;
            sumY += ry;
        }

        // Calculate auto-scale to fit data in panel with margin
        float dataRangeX = dataMaxX - dataMinX;
        float dataRangeY = dataMaxY - dataMinY;
        float dataRange = Math.Max(dataRangeX, dataRangeY);
        if (dataRange < 0.001f) dataRange = 1f; // Prevent division by zero

        float margin = 0.1f; // 10% margin on each side
        float availableSize = Math.Min(panelW, panelH) * (1f - 2f * margin);
        float autoScale = availableSize / dataRange;

        // Apply zoom from camera (zoom = 100 is default, larger = zoomed out)
        float zoomFactor = 100f / Math.Max(_csrCamera.Distance, 1f);
        float scale = autoScale * zoomFactor;

        // Calculate data center for centering
        float dataCenterX = sumX / count;
        float dataCenterY = sumY / count;

        // Second pass: convert to screen coordinates
        Span<Vector2> screenPos = stackalloc Vector2[count];
        float minScreenX = float.MaxValue, maxScreenX = float.MinValue;
        float minScreenY = float.MaxValue, maxScreenY = float.MinValue;

        for (int i = 0; i < count; i++)
        {
            // Center data around origin, then scale and offset to screen center
            float screenX = cx + (rotatedPos[i].X - dataCenterX) * scale;
            float screenY = cy - (rotatedPos[i].Y - dataCenterY) * scale;
            screenPos[i] = new Vector2(screenX, screenY);

            minScreenX = Math.Min(minScreenX, screenX);
            maxScreenX = Math.Max(maxScreenX, screenX);
            minScreenY = Math.Min(minScreenY, screenY);
            maxScreenY = Math.Max(maxScreenY, screenY);
        }

        // Debug: log screen bounds once per second
        if ((DateTime.Now - _csrLastDebugLog).TotalSeconds > 1)
        {
            System.Diagnostics.Debug.WriteLine($"[CSR Draw] Nodes={count}, AutoScale={autoScale:F1}, Scale={scale:F1}, ScreenBounds=[{minScreenX:F0},{minScreenY:F0}]-[{maxScreenX:F0},{maxScreenY:F0}], Panel={panelW}x{panelH}");
            _csrLastDebugLog = DateTime.Now;
        }

        // Draw edges first (behind nodes)
        if (_csrShowEdges && _csrEdgeList is not null)
        {
            foreach (var (u, v, w) in _csrEdgeList)
            {
                if (u < count && v < count)
                {
                    // Use mode-based edge styling
                    var (edgeColor, thickness) = GetCsrEdgeStyle(u, v, w);
                    uint col = ImGui.ColorConvertFloat4ToU32(edgeColor);
                    drawList.AddLine(screenPos[u], screenPos[v], col, thickness);
                }
            }
        }

        // Draw nodes as circles - use mode-based coloring
        for (int i = 0; i < count; i++)
        {
            // Use mode-based node coloring
            Vector4 color = GetCsrNodeColor(i);

            float depthFactor = 1f - Math.Clamp(depths[i] / 10f, -0.5f, 0.5f);
            float radius = 1.3f + depthFactor * 1f; // Reduced radius (was 4f + 3f, now ~3x smaller)

            uint col = ImGui.ColorConvertFloat4ToU32(color);
            drawList.AddCircleFilled(screenPos[i], radius, col);
        }

        // Update FPS counter
        _csrFrameCount++;
        if ((DateTime.Now - _csrFpsUpdateTime).TotalSeconds >= 1.0)
        {
            _csrFps = _csrFrameCount / (float)(DateTime.Now - _csrFpsUpdateTime).TotalSeconds;
            _csrFrameCount = 0;
            _csrFpsUpdateTime = DateTime.Now;
        }
    }

    private void DrawCsrImGuiOverlay()
    {
        // Safety check: don't call ImGui if device is lost
        if (_csrDx12Host?.IsDeviceLost == true)
            return;

        // Controls hint at bottom - shifted right to avoid overlapping with left panel scrollbar
        // Leave extra space for the left controls panel + metrics panel.
        float leftMargin = Math.Max(280f, (_csrControlsHostPanel?.Width ?? 0) + 40f);
        ImGui.SetNextWindowPos(new Vector2(leftMargin, (float)((_csrRenderPanel?.Height ?? 600) - 50)), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.5f);
        if (ImGui.Begin("##Controls", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "LMB: Rotate | Wheel: Zoom");
        }
        ImGui.End();
    }

    private void CsrRenderPanel_Resize(object? sender, EventArgs e)
    {
        if (_csrRenderHost is not null && _csrRenderPanel is not null)
            _csrRenderHost.Resize(Math.Max(_csrRenderPanel.Width, 1), Math.Max(_csrRenderPanel.Height, 1));
    }

    /// <summary>
    /// Generate synthetic test data to verify the render pipeline works.
    /// Creates a simple circular arrangement of nodes with edges.
    /// </summary>
    private void GenerateTestVisualizationData()
    {
        const int testNodeCount = 50;

        _csrNodeCount = testNodeCount;
        _csrNodeX = new float[testNodeCount];
        _csrNodeY = new float[testNodeCount];
        _csrNodeZ = new float[testNodeCount];
        _csrNodeStates = new NodeState[testNodeCount];

        // Generate circular layout
        float radius = 15f;
        for (int i = 0; i < testNodeCount; i++)
        {
            float angle = 2f * MathF.PI * i / testNodeCount;
            _csrNodeX[i] = radius * MathF.Cos(angle);
            _csrNodeY[i] = radius * MathF.Sin(angle);
            _csrNodeZ[i] = 0f;
            _csrNodeStates[i] = i % 3 == 0 ? NodeState.Excited :
                                i % 3 == 1 ? NodeState.Refractory : NodeState.Rest;
        }

        // Generate edges connecting adjacent nodes
        _csrEdgeList ??= new List<(int, int, float)>(testNodeCount * 2);
        _csrEdgeList.Clear();

        for (int i = 0; i < testNodeCount; i++)
        {
            int next = (i + 1) % testNodeCount;
            _csrEdgeList.Add((i, next, 0.5f));

            // Also connect to opposite node for cross pattern
            int opposite = (i + testNodeCount / 2) % testNodeCount;
            if (i < testNodeCount / 2)
            {
                _csrEdgeList.Add((i, opposite, 0.3f));
            }
        }

        _csrEdgeCount = _csrEdgeList.Count;
        _csrSpectralDim = 2.0; // Test value

        System.Diagnostics.Debug.WriteLine($"[CSR Render] Generated test data: {testNodeCount} nodes, {_csrEdgeCount} edges");
    }
}
