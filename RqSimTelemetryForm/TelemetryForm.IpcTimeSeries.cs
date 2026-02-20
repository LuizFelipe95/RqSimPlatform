namespace RqSimTelemetryForm;

/// <summary>
/// Lightweight local time-series accumulator for IPC (Console) mode.
/// Collects point-in-time snapshots from SharedMemory and provides
/// decimated arrays for <see cref="Helpers.ChartRenderer"/>.
///
/// Thread safety: UI-thread only (timer tick + paint handlers).
/// Memory: capped at <see cref="MaxPoints"/> entries (~2.7 MB at 50 000).
/// </summary>
public partial class TelemetryForm
{
    /// <summary>
    /// Accumulates IPC metric snapshots over time and produces decimated
    /// arrays compatible with <see cref="Helpers.ChartRenderer"/>.
    /// </summary>
    internal sealed class IpcTimeSeries
    {
        private const int MaxPoints = 50_000;
        private const int DecimatedTarget = 500;
        private const int TailPoints = 100;

        // Raw storage (appended every timer tick)
        private readonly List<int> _steps = new(1024);
        private readonly List<int> _excited = new(1024);
        private readonly List<double> _heavyMass = new(1024);
        private readonly List<int> _largestCluster = new(1024);
        private readonly List<double> _energy = new(1024);
        private readonly List<double> _networkTemp = new(1024);

        // Cached decimated views (rebuilt on demand)
        private int[] _decSteps = [];
        private int[] _decExcited = [];
        private double[] _decHeavyMass = [];
        private int[] _decLargestCluster = [];
        private double[] _decEnergy = [];
        private double[] _decNetworkTemp = [];
        private bool _dirty = true;

        /// <summary>Total raw data points stored.</summary>
        public int TotalCount => _steps.Count;

        /// <summary>
        /// Appends a single IPC snapshot. Deduplicates by iteration
        /// (skips if step equals the last recorded step).
        /// Detects simulation reset (step goes backwards) and clears stale data.
        /// </summary>
        public void Append(int step, int excited, double heavyMass,
            int largestCluster, double energy, double networkTemp)
        {
            if (_steps.Count > 0)
            {
                int lastStep = _steps[^1];

                // Deduplicate: skip if same iteration as last point
                if (lastStep == step)
                    return;

                // Detect simulation reset: step went backwards → clear stale data
                if (step < lastStep)
                    Clear();
            }

            // Cap: drop oldest 10 % when buffer is full
            if (_steps.Count >= MaxPoints)
                TrimOldest();

            _steps.Add(step);
            _excited.Add(excited);
            _heavyMass.Add(heavyMass);
            _largestCluster.Add(largestCluster);
            _energy.Add(energy);
            _networkTemp.Add(networkTemp);

            _dirty = true;
        }

        // ============================================================
        // DECIMATED ACCESSORS (lazy rebuild)
        // ============================================================

        public int[] GetDecimatedSteps() { EnsureDecimated(); return _decSteps; }
        public int[] GetDecimatedExcited() { EnsureDecimated(); return _decExcited; }
        public double[] GetDecimatedHeavyMass() { EnsureDecimated(); return _decHeavyMass; }
        public int[] GetDecimatedLargestCluster() { EnsureDecimated(); return _decLargestCluster; }
        public double[] GetDecimatedEnergy() { EnsureDecimated(); return _decEnergy; }
        public double[] GetDecimatedNetworkTemp() { EnsureDecimated(); return _decNetworkTemp; }

        /// <summary>Clears all accumulated data.</summary>
        public void Clear()
        {
            _steps.Clear();
            _excited.Clear();
            _heavyMass.Clear();
            _largestCluster.Clear();
            _energy.Clear();
            _networkTemp.Clear();

            _decSteps = [];
            _decExcited = [];
            _decHeavyMass = [];
            _decLargestCluster = [];
            _decEnergy = [];
            _decNetworkTemp = [];
            _dirty = false;
        }

        // ============================================================
        // INTERNALS
        // ============================================================

        private void EnsureDecimated()
        {
            if (!_dirty)
                return;

            _dirty = false;
            int count = _steps.Count;

            if (count == 0)
            {
                _decSteps = [];
                _decExcited = [];
                _decHeavyMass = [];
                _decLargestCluster = [];
                _decEnergy = [];
                _decNetworkTemp = [];
                return;
            }

            if (count <= DecimatedTarget)
            {
                // No decimation needed — copy as-is
                _decSteps = [.. _steps];
                _decExcited = [.. _excited];
                _decHeavyMass = [.. _heavyMass];
                _decLargestCluster = [.. _largestCluster];
                _decEnergy = [.. _energy];
                _decNetworkTemp = [.. _networkTemp];
                return;
            }

            // Decimation: head 10 % + sampled middle + tail at full resolution
            int headCount = Math.Min(TailPoints, count / 10);
            int tailCount = Math.Min(TailPoints, count / 10);
            int middleCount = count - headCount - tailCount;
            int middleSamples = DecimatedTarget - headCount - tailCount;

            if (middleSamples <= 0 || middleCount <= 0)
            {
                // Fall back to uniform sampling
                BuildUniformSample(count);
                return;
            }

            List<int> indices = new(DecimatedTarget);

            // Head
            for (int i = 0; i < headCount; i++)
                indices.Add(i);

            // Middle (evenly spaced)
            int middleStart = headCount;
            double step = (double)middleCount / middleSamples;
            for (int i = 0; i < middleSamples; i++)
            {
                int idx = middleStart + (int)(i * step);
                if (indices.Count == 0 || indices[^1] != idx)
                    indices.Add(idx);
            }

            // Tail (last N at full resolution)
            int tailStart = count - tailCount;
            for (int i = tailStart; i < count; i++)
            {
                if (indices.Count == 0 || indices[^1] != i)
                    indices.Add(i);
            }

            BuildFromIndices(indices);
        }

        private void BuildUniformSample(int count)
        {
            int skip = Math.Max(1, count / DecimatedTarget);
            List<int> indices = new(DecimatedTarget + 1);
            for (int i = 0; i < count; i += skip)
                indices.Add(i);

            // Always include last point
            if (indices.Count == 0 || indices[^1] != count - 1)
                indices.Add(count - 1);

            BuildFromIndices(indices);
        }

        private void BuildFromIndices(List<int> indices)
        {
            int n = indices.Count;
            _decSteps = new int[n];
            _decExcited = new int[n];
            _decHeavyMass = new double[n];
            _decLargestCluster = new int[n];
            _decEnergy = new double[n];
            _decNetworkTemp = new double[n];

            for (int i = 0; i < n; i++)
            {
                int idx = indices[i];
                _decSteps[i] = _steps[idx];
                _decExcited[i] = _excited[idx];
                _decHeavyMass[i] = _heavyMass[idx];
                _decLargestCluster[i] = _largestCluster[idx];
                _decEnergy[i] = _energy[idx];
                _decNetworkTemp[i] = _networkTemp[idx];
            }
        }

        /// <summary>Drops oldest 10 % of data to stay within <see cref="MaxPoints"/>.</summary>
        private void TrimOldest()
        {
            int trimCount = MaxPoints / 10;
            _steps.RemoveRange(0, trimCount);
            _excited.RemoveRange(0, trimCount);
            _heavyMass.RemoveRange(0, trimCount);
            _largestCluster.RemoveRange(0, trimCount);
            _energy.RemoveRange(0, trimCount);
            _networkTemp.RemoveRange(0, trimCount);
        }
    }
}
