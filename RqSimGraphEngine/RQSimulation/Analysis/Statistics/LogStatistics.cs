using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RqSimGraphEngine.Experiments;

namespace RQSimulation.Analysis
{
    /// <summary>
    /// Class for collecting significant calculation statistics and comparing CPU vs GPU execution.
    /// </summary>
    public class LogStatistics
    {
        // Thread-safe log buffers
        private static readonly ConcurrentQueue<string> _cpuSimLogBuffer = new();
        private static readonly ConcurrentQueue<string> _gpuLogBuffer = new();

        // Statistics storage for comparison
        public class RunStats
        {
            public string DeviceType { get; set; } = "Unknown"; // "CPU" or "GPU"
            public long TotalTimeMs { get; set; }
            public int TotalSteps { get; set; }
            public double FinalEnergy { get; set; }
            public int FinalExcited { get; set; }
            public double FinalHeavyMass { get; set; }
            public int FinalLargestCluster { get; set; }
            public double FinalSpectralDimension { get; set; }
            public List<double> SpectralDimensionHistory { get; set; } = new();
            public List<string> OperationLogs { get; set; } = new();
        }

        private static RunStats? _lastCpuStats;
        private static RunStats? _lastGpuStats;

        /// <summary>
        /// Logs a message to the CPU/General console buffer.
        /// </summary>
        public static void LogCPU(string message)
        {
            _cpuSimLogBuffer.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }

        /// <summary>
        /// Logs a message to the GPU console buffer.
        /// </summary>
        public static void LogGPU(string message)
        {
            _gpuLogBuffer.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }

        /// <summary>
        /// Retrieves and clears pending CPU logs.
        /// </summary>
        public static string[] FetchCpuLogs()
        {
            if (_cpuSimLogBuffer.IsEmpty) return Array.Empty<string>();

            var logs = new List<string>();
            while (_cpuSimLogBuffer.TryDequeue(out var msg))
            {
                logs.Add(msg);
            }
            return logs.ToArray();
        }

        /// <summary>
        /// Retrieves and clears pending GPU logs.
        /// </summary>
        public static string[] FetchGpuLogs()
        {
            if (_gpuLogBuffer.IsEmpty) return Array.Empty<string>();

            var logs = new List<string>();
            while (_gpuLogBuffer.TryDequeue(out var msg))
            {
                logs.Add(msg);
            }
            return logs.ToArray();
        }

        /// <summary>
        /// Runs a comparison scenario on CPU and GPU sequentially.
        /// Returns the comparison report.
        /// </summary>
        public static async Task<string> CompareCpuGpu(SimulationConfig config, Func<SimulationConfig, bool, Task<RunStats>> runner)
        {
            LogCPU("=== Starting CPU vs GPU Comparison ===");
            LogGPU("=== Starting CPU vs GPU Comparison ===");

            // 1. Run CPU
            LogCPU("Starting CPU Run...");
            _lastCpuStats = await runner(config, false); // false = no GPU
            LogCPU($"CPU Run Complete. Time: {_lastCpuStats.TotalTimeMs}ms");

            // 2. Run GPU
            LogGPU("Starting GPU Run...");
            _lastGpuStats = await runner(config, true); // true = use GPU
            LogGPU($"GPU Run Complete. Time: {_lastGpuStats.TotalTimeMs}ms");

            // 3. Compare
            return CompareResults();
        }

        private static string CompareResults()
        {
            if (_lastCpuStats == null || _lastGpuStats == null) return "No stats available.";

            var sb = new StringBuilder();
            sb.AppendLine("\n=== Comparison Results ===");
            sb.AppendLine($"Metric            | CPU          | GPU          | Diff");
            sb.AppendLine($"------------------|--------------|--------------|------");

            AppendDiff(sb, "Time (ms)", _lastCpuStats.TotalTimeMs, _lastGpuStats.TotalTimeMs);
            AppendDiff(sb, "Excited Nodes", _lastCpuStats.FinalExcited, _lastGpuStats.FinalExcited);
            AppendDiff(sb, "Heavy Mass", _lastCpuStats.FinalHeavyMass, _lastGpuStats.FinalHeavyMass);
            AppendDiff(sb, "Largest Cluster", _lastCpuStats.FinalLargestCluster, _lastGpuStats.FinalLargestCluster);
            AppendDiff(sb, "Energy", _lastCpuStats.FinalEnergy, _lastGpuStats.FinalEnergy);
            AppendDiff(sb, "Spectral Dim", _lastCpuStats.FinalSpectralDimension, _lastGpuStats.FinalSpectralDimension);

            sb.AppendLine("\n=== Spectral Dimension Dynamics ===");
            sb.AppendLine("Step Index | CPU d_S  | GPU d_S  | Diff");

            int count = Math.Min(_lastCpuStats.SpectralDimensionHistory.Count, _lastGpuStats.SpectralDimensionHistory.Count);
            // Show up to 20 points evenly distributed
            int stride = Math.Max(1, count / 20);

            for (int i = 0; i < count; i += stride)
            {
                double cpuDs = _lastCpuStats.SpectralDimensionHistory[i];
                double gpuDs = _lastGpuStats.SpectralDimensionHistory[i];
                sb.AppendLine($"{i,10} | {cpuDs,8:F4} | {gpuDs,8:F4} | {(gpuDs - cpuDs),8:F4}");
            }

            string report = sb.ToString();
            LogCPU(report);
            LogGPU(report); // Show in both for visibility
            return report;
        }

        private static void AppendDiff(StringBuilder sb, string label, double cpuVal, double gpuVal)
        {
            double diff = gpuVal - cpuVal;
            double pct = cpuVal != 0 ? (diff / cpuVal) * 100.0 : 0.0;
            sb.AppendLine($"{label,-17} | {cpuVal,12:F2} | {gpuVal,12:F2} | {diff,12:F2} ({pct:F1}%)");
        }
    }
}