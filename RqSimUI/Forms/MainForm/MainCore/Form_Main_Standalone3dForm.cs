using System.Diagnostics;
using RqSim3DForm;
using RqSimForms.ProcessesDispatcher.Contracts;
using RQSimulation;

namespace RqSimForms;

/// <summary>
/// Standalone DX12 3D visualization form — opened via checkBox_StanaloneDX12Form.
/// Provides a separate window with full DX12 rendering of the simulation graph.
/// </summary>
partial class Form_Main_RqSim
{
    private Form_Rsim3DForm? _standalone3DForm;

    // Standalone DX12 provider cache to reduce allocations/GC pressure
    private float[]? _standaloneNodeX;
    private float[]? _standaloneNodeY;
    private float[]? _standaloneNodeZ;
    private NodeState[]? _standaloneStates;
    private List<(int, int, float)>? _standaloneEdges;
    private int _standaloneCachedN;
    private int _standaloneEdgeRebuildCounter;
    private double _standaloneCachedSpectralDim = double.NaN;

    // Cached edges for external simulation
    private List<(int, int, float)>? _standaloneExternalEdgesCache;
    private int _standaloneExternalEdgesCacheN;

    /// <summary>
    /// Opens or closes standalone 3D visualization form.
    /// </summary>
    private void checkBox_StanaloneDX12Form_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not CheckBox checkBox) return;

        if (checkBox.Checked)
        {
            if (_standalone3DForm is null || _standalone3DForm.IsDisposed)
            {
                _standalone3DForm = new Form_Rsim3DForm();
                _standalone3DForm.SetDataProvider(GetGraphDataForStandalone3D);
                _standalone3DForm.FormClosed += (s, args) =>
                {
                    _standalone3DForm = null;
                    if (!checkBox.IsDisposed)
                    {
                        checkBox.Checked = false;
                    }
                };
                _standalone3DForm.Show();
                AppendSysConsole("[3D] Standalone visualization form opened\n");
            }
            else
            {
                _standalone3DForm.BringToFront();
                _standalone3DForm.Focus();
            }
        }
        else
        {
            if (_standalone3DForm is not null && !_standalone3DForm.IsDisposed)
            {
                _standalone3DForm.Close();
                _standalone3DForm = null;
                AppendSysConsole("[3D] Standalone visualization form closed\n");
            }
        }
    }

    /// <summary>
    /// Provides graph data for standalone 3D form.
    /// </summary>
    private GraphRenderData GetGraphDataForStandalone3D()
    {
        // External simulation via shared memory
        if (_isExternalSimulation)
        {
            var externalNodes = _lifeCycleManager?.GetExternalRenderNodes();
            if (externalNodes is not null && externalNodes.Length > 0)
            {
                return GetGraphDataFromExternalNodes(externalNodes);
            }
        }

        RQGraph? graph = _simApi?.SimulationEngine?.Graph ?? _simApi?.ActiveGraph;

        if (graph is null || graph.N <= 0)
        {
            _standaloneCachedN = 0;
            _standaloneEdges?.Clear();
            _standaloneCachedSpectralDim = double.NaN;
            return new GraphRenderData(null, null, null, null, null, 0, 0);
        }

        int n = graph.N;

        if (_standaloneNodeX is null || _standaloneNodeX.Length != n)
        {
            _standaloneNodeX = new float[n];
            _standaloneNodeY = new float[n];
            _standaloneNodeZ = new float[n];
            _standaloneStates = new NodeState[n];
            _standaloneCachedN = n;
            _standaloneEdgeRebuildCounter = 0;
            _standaloneCachedSpectralDim = double.NaN;
        }

        bool hasSpectral = graph.SpectralX is not null && graph.SpectralX.Length == n;

        if (hasSpectral)
        {
            for (int i = 0; i < n; i++)
            {
                _standaloneNodeX[i] = (float)graph.SpectralX![i];
                _standaloneNodeY[i] = (float)graph.SpectralY![i];
                _standaloneNodeZ[i] = (float)graph.SpectralZ![i];
                _standaloneStates![i] = graph.State[i];
            }
        }
        else
        {
            int gridSize = (int)Math.Ceiling(Math.Sqrt(n));
            float spacing = 2.0f;
            for (int i = 0; i < n; i++)
            {
                int gx = i % gridSize;
                int gy = i / gridSize;
                _standaloneNodeX[i] = (gx - gridSize / 2f) * spacing;
                _standaloneNodeY[i] = (gy - gridSize / 2f) * spacing;
                _standaloneNodeZ[i] = 0f;
                _standaloneStates![i] = graph.State[i];
            }
        }

        // Build edges periodically
        _standaloneEdges ??= new List<(int, int, float)>(n * 4);

        bool graphSizeChanged = _standaloneCachedN != n;
        _standaloneEdgeRebuildCounter++;
        int rebuildPeriod = _simApi?.SimulationEngine?.Graph is not null ? 4 : 30;

        if (graphSizeChanged || _standaloneEdges.Count == 0 || _standaloneEdgeRebuildCounter >= rebuildPeriod)
        {
            _standaloneEdgeRebuildCounter = 0;
            _standaloneCachedN = n;
            _standaloneEdges.Clear();

            int step = Math.Max(1, n / 500);
            for (int i = 0; i < n; i += step)
            {
                foreach (int j in graph.Neighbors(i))
                {
                    if (j > i)
                    {
                        float w = (float)graph.Weights[i, j];
                        if (w > 0.01f)
                        {
                            _standaloneEdges.Add((i, j, w));
                        }
                    }
                }
            }
        }

        double spectralDim = graph.SmoothedSpectralDimension;
        if (!double.IsNaN(spectralDim) && spectralDim > 0)
        {
            _standaloneCachedSpectralDim = spectralDim;
        }
        else if (!double.IsNaN(_standaloneCachedSpectralDim))
        {
            spectralDim = _standaloneCachedSpectralDim;
        }

        return new GraphRenderData(_standaloneNodeX, _standaloneNodeY, _standaloneNodeZ, _standaloneStates, _standaloneEdges, n, spectralDim);
    }

    /// <summary>
    /// Converts external RenderNode[] from shared memory to GraphRenderData.
    /// </summary>
    private GraphRenderData GetGraphDataFromExternalNodes(RenderNode[] nodes)
    {
        int n = nodes.Length;
        if (n == 0)
        {
            return new GraphRenderData(null, null, null, null, null, 0, 0);
        }

        if (_standaloneNodeX is null || _standaloneNodeX.Length != n)
        {
            _standaloneNodeX = new float[n];
            _standaloneNodeY = new float[n];
            _standaloneNodeZ = new float[n];
            _standaloneStates = new NodeState[n];
            _standaloneCachedN = n;
            _standaloneExternalEdgesCache = null;
            _standaloneExternalEdgesCacheN = 0;
        }

        for (int i = 0; i < n; i++)
        {
            _standaloneNodeX[i] = nodes[i].X;
            _standaloneNodeY[i] = nodes[i].Y;
            _standaloneNodeZ[i] = nodes[i].Z;
            _standaloneStates![i] = nodes[i].R > 0.5f ? NodeState.Excited : NodeState.Rest;
        }

        var externalEdges = _lifeCycleManager?.GetExternalRenderEdges();
        bool hasRealEdges = externalEdges is not null && externalEdges.Length > 0;

        if (hasRealEdges)
        {
            _standaloneEdges ??= new List<(int, int, float)>(externalEdges!.Length);
            _standaloneEdges.Clear();
            for (int e = 0; e < externalEdges!.Length; e++)
            {
                int from = externalEdges[e].FromNode;
                int to = externalEdges[e].ToNode;
                float weight = externalEdges[e].Weight;
                if (from >= 0 && from < n && to >= 0 && to < n && from != to)
                {
                    _standaloneEdges.Add((from, to, weight));
                }
            }
            _standaloneExternalEdgesCache = new List<(int, int, float)>(_standaloneEdges);
            _standaloneExternalEdgesCacheN = n;
        }
        else if (_standaloneExternalEdgesCache is not null && _standaloneExternalEdgesCacheN == n)
        {
            _standaloneEdges!.Clear();
            _standaloneEdges.AddRange(_standaloneExternalEdgesCache);
        }

        var externalState = _lifeCycleManager?.TryGetExternalSimulationState();
        double spectralDim = externalState?.SpectralDimension ?? 0;

        return new GraphRenderData(_standaloneNodeX, _standaloneNodeY, _standaloneNodeZ, _standaloneStates, _standaloneEdges, n, spectralDim);
    }
}
