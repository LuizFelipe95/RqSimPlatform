using RQSimulation;
using RQSimulation.GPUOptimized;
using RQSimulation.EventBasedModel;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RqSimUI.FormSimAPI.Interfaces;

namespace RqSimForms.Forms.Interfaces;

public partial class RqSimEngineApi
{
    // === Session History ===
    public readonly List<SimulationSession> SessionHistory = [];
    public SimulationSession? CurrentSession { get; set; }

    /// <summary>
    /// Creates a new simulation session
    /// </summary>
    public SimulationSession CreateSession(bool gpuEnabled, string? gpuDeviceName, DisplayFilters filters)
    {
        return new SimulationSession
        {
            SessionId = Guid.NewGuid(),
            StartedAt = DateTime.UtcNow,
            Config = LastConfig,
            GpuEnabled = gpuEnabled,
            GpuDeviceName = gpuDeviceName,
            Filters = filters
        };
    }

    /// <summary>
    /// Archives current session to history
    /// </summary>
    public void ArchiveSession(SessionEndReason reason, string consoleLog, string summaryText, List<ImportantEvent> events)
    {
        if (CurrentSession == null) return;

        double wallClockSeconds = (DateTime.UtcNow - SimulationWallClockStart).TotalSeconds;

        double finalSpectralDim = FinalSpectralDimension;
        double finalNetworkTemp = FinalNetworkTemperature;
        if (finalSpectralDim == 0 && SeriesSpectralDimension.Count > 0)
            finalSpectralDim = SeriesSpectralDimension[^1];
        if (finalNetworkTemp == 0 && SeriesNetworkTemperature.Count > 0)
            finalNetworkTemp = SeriesNetworkTemperature[^1];

        var session = CurrentSession with
        {
            EndedAt = DateTime.UtcNow,
            EndReason = reason,
            Config = LastConfig,
            Result = LastResult,
            ModernResult = ModernResult,
            SeriesSteps = [.. SeriesSteps],
            SeriesExcited = [.. SeriesExcited],
            SeriesHeavyMass = [.. SeriesHeavyMass],
            SeriesHeavyCount = [.. SeriesHeavyCount],
            SeriesLargestCluster = [.. SeriesLargestCluster],
            SeriesAvgDist = [.. SeriesAvgDist],
            SeriesDensity = [.. SeriesDensity],
            SeriesEnergy = [.. SeriesEnergy],
            SeriesCorr = [.. SeriesCorr],
            SeriesStrongEdges = [.. SeriesStrongEdges],
            SeriesQNorm = [.. SeriesQNorm],
            SeriesQEnergy = [.. SeriesQEnergy],
            SeriesEntanglement = [.. SeriesEntanglement],
            SeriesSpectralDimension = [.. SeriesSpectralDimension],
            SeriesNetworkTemperature = [.. SeriesNetworkTemperature],
            SeriesEffectiveG = [.. SeriesEffectiveG],
            SeriesAdaptiveThreshold = [.. SeriesAdaptiveThreshold],
            SynthesisData = SynthesisData?.ToList(),
            SynthesisCount = SynthesisCount,
            FissionCount = FissionCount,
            ConsoleLog = consoleLog,
            SummaryText = summaryText,
            LastStep = SeriesSteps.Count > 0 ? SeriesSteps[^1] : 0,
            TotalStepsPlanned = LastConfig?.TotalSteps ?? 0,
            FinalSpectralDimension = finalSpectralDim,
            FinalNetworkTemperature = finalNetworkTemp,
            WallClockDurationSeconds = wallClockSeconds,
            ImportantEvents = events
        };

        SessionHistory.Add(session);
    }
}
