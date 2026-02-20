using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using RQSimulation.Core.Observability;

namespace RqSimTelemetryForm;

/// <summary>
/// MeterListener wrapper for subscribing to RqSimPlatformTelemetry metrics.
/// Thread-safe: callbacks arrive from background threads, snapshots are read from UI thread.
/// </summary>
public partial class TelemetryForm
{
    // ============================================================
    // METRIC SNAPSHOT
    // ============================================================

    /// <summary>
    /// Immutable snapshot of a metric's last observed value.
    /// </summary>
    private readonly record struct MetricSnapshot(
        string Name,
        double Value,
        string Unit,
        DateTime TimestampUtc,
        IReadOnlyList<KeyValuePair<string, object?>> Tags);

    // ============================================================
    // FIELDS
    // ============================================================

    private MeterListener? _meterListener;
    private readonly ConcurrentDictionary<string, MetricSnapshot> _metricSnapshots = new();

    /// <summary>
    /// Module performance aggregation: module name → (totalMs, count, errors).
    /// Updated from MeterListener callbacks, read from UI thread.
    /// </summary>
    private readonly ConcurrentDictionary<string, (double TotalMs, long Count, long Errors)> _modulePerformance = new();

    // ============================================================
    // LIFECYCLE
    // ============================================================

    /// <summary>
    /// Starts the MeterListener to subscribe to all RqSimPlatform metrics.
    /// </summary>
    private void StartMetricsListener()
    {
        _meterListener = new MeterListener();

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == RqSimPlatformTelemetry.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _meterListener.SetMeasurementEventCallback<double>(OnDoubleMeasurement);
        _meterListener.SetMeasurementEventCallback<long>(OnLongMeasurement);
        _meterListener.SetMeasurementEventCallback<int>(OnIntMeasurement);

        _meterListener.Start();
    }

    /// <summary>
    /// Stops and disposes the MeterListener.
    /// </summary>
    private void StopMetricsListener()
    {
        _meterListener?.Dispose();
        _meterListener = null;
    }

    // ============================================================
    // MEASUREMENT CALLBACKS (called from background threads)
    // ============================================================

    private void OnDoubleMeasurement(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        RecordMetric(instrument, measurement, tags);
        AggregateModulePerformance(instrument, measurement, tags);
    }

    private void OnLongMeasurement(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        RecordMetric(instrument, measurement, tags);
        AggregateModulePerformance(instrument, measurement, tags);
    }

    private void OnIntMeasurement(
        Instrument instrument,
        int measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        RecordMetric(instrument, measurement, tags);
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private void RecordMetric(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string key = BuildMetricKey(instrument.Name, tags);

        var snapshot = new MetricSnapshot(
            instrument.Name,
            value,
            instrument.Unit ?? "",
            DateTime.UtcNow,
            tags.ToArray());

        _metricSnapshots[key] = snapshot;
    }

    private void AggregateModulePerformance(
        Instrument instrument,
        double value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? moduleName = null;
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key is "module" or "module.name")
            {
                moduleName = tag.Value?.ToString();
                break;
            }
        }

        if (moduleName is null) return;

        if (instrument.Name == "rqsim.module.duration_ms")
        {
            _modulePerformance.AddOrUpdate(
                moduleName,
                (value, 1, 0),
                (_, existing) => (existing.TotalMs + value, existing.Count + 1, existing.Errors));
        }
        else if (instrument.Name == "rqsim.module.error.count")
        {
            _modulePerformance.AddOrUpdate(
                moduleName,
                (0, 0, (long)value),
                (_, existing) => (existing.TotalMs, existing.Count, existing.Errors + (long)value));
        }
    }

    private static string BuildMetricKey(string name, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (tags.Length == 0)
            return name;

        // Include tag values in the key for multi-dimensional metrics
        Span<char> buffer = stackalloc char[256];
        int pos = 0;

        foreach (char c in name)
        {
            if (pos >= buffer.Length - 1) break;
            buffer[pos++] = c;
        }

        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (pos >= buffer.Length - 3) break;
            buffer[pos++] = '|';

            string tagValue = tag.Value?.ToString() ?? "";
            foreach (char c in tagValue)
            {
                if (pos >= buffer.Length - 1) break;
                buffer[pos++] = c;
            }
        }

        return new string(buffer[..pos]);
    }
}
