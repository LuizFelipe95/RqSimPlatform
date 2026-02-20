using System;
using RQSimulation;
using RQSimulation.GPUCompressedSparseRow.Unified;

namespace RqSimForms.Forms.Interfaces;

public partial class RqSimEngineApi
{
    private int _csrUnifiedLastSyncSweep;

    internal void ResetCsrUnifiedSyncState()
    {
        _csrUnifiedLastSyncSweep = 0;
    }

    /// <summary>
    /// True when CSR Unified is treated as the owner of weights during CSR mode.
    /// In this mode, the simulation loop pulls weights back from CSR Unified into the graph.
    /// </summary>
    public bool CsrUnifiedOwnsWeights { get; set; } = true;

    /// <summary>
    /// Ensures CSR Unified engine is initialized and synchronized from the current graph.
    /// This is intentionally conservative: currently it rebuilds topology via UpdateTopology
    /// because the CSR unified engine stores weights/topology internally.
    /// </summary>
    /// <param name="graph">Current simulation graph</param>
    /// <param name="sweepCount">Current sweep counter</param>
    /// <param name="syncInterval">Minimum sweeps between sync operations</param>
    /// <returns>The CSR unified engine instance, or null if not available</returns>
    public CsrUnifiedEngine? TrySyncCsrUnifiedEngine(RQGraph graph, int sweepCount, int syncInterval)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (ActiveEngineType != GpuEngineType.Csr)
            return null;

        if (CsrUnifiedEngine == null)
            return null;

        if (syncInterval < 1)
            syncInterval = 1;

        if (sweepCount == 0 || (sweepCount - _csrUnifiedLastSyncSweep) >= syncInterval)
        {
            graph.EnsureCorrelationMassComputed();

            // Conservative sync: rebuild CSR buffers from the current dense graph snapshot.
            CsrUnifiedEngine.UpdateTopology(graph);
            _csrUnifiedLastSyncSweep = sweepCount;
        }

        return CsrUnifiedEngine;
    }

    /// <summary>
    /// Synchronize weights from graph to CSR unified without rebuilding topology.
    /// </summary>
    public void PushWeightsToCsrUnified(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (ActiveEngineType != GpuEngineType.Csr || CsrUnifiedEngine == null)
            return;

        CsrUnifiedEngine.SyncWeightsFromGraph(graph);
    }

    /// <summary>
    /// Synchronize weights from CSR unified back to the graph without rebuilding topology.
    /// Use when CSR Unified owns weights.
    /// </summary>
    public void PullWeightsFromCsrUnified(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (ActiveEngineType != GpuEngineType.Csr || CsrUnifiedEngine == null)
            return;

        CsrUnifiedEngine.CopyWeightsToGraph(graph);
    }
}
