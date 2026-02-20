using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RqSimForms.Events;

/// <summary>
/// Thread-safe store for physics verification events.
/// Supports filtering, export, and live update notifications.
/// </summary>
public sealed class PhysicsEventStore
{
    private readonly ConcurrentQueue<PhysicsVerificationEvent> _events = new();
    private readonly object _syncRoot = new();
    private int _maxEvents = 10000;

    /// <summary>
    /// Fired when a new event is added.
    /// </summary>
    public event EventHandler<PhysicsVerificationEvent>? EventAdded;

    /// <summary>
    /// Fired when events are cleared.
    /// </summary>
    public event EventHandler? EventsCleared;

    /// <summary>
    /// Maximum number of events to retain (FIFO eviction).
    /// </summary>
    public int MaxEvents
    {
        get => _maxEvents;
        set => _maxEvents = Math.Max(100, value);
    }

    /// <summary>
    /// Current number of events stored.
    /// </summary>
    public int Count => _events.Count;

    /// <summary>
    /// Adds an event to the store.
    /// </summary>
    public void Add(PhysicsVerificationEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        _events.Enqueue(evt);

        // FIFO eviction
        while (_events.Count > _maxEvents && _events.TryDequeue(out _)) { }

        EventAdded?.Invoke(this, evt);
    }

    /// <summary>
    /// Clears all events.
    /// </summary>
    public void Clear()
    {
        while (_events.TryDequeue(out _)) { }

        EventsCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets all events as a snapshot.
    /// </summary>
    public IReadOnlyList<PhysicsVerificationEvent> GetAll()
        => _events.ToArray();

    /// <summary>
    /// Gets events filtered by type.
    /// </summary>
    public IReadOnlyList<PhysicsVerificationEvent> GetByType(PhysicsEventType type)
        => _events.Where(e => e.EventType == type).ToArray();

    /// <summary>
    /// Gets events filtered by type (null = all types).
    /// </summary>
    public IReadOnlyList<PhysicsVerificationEvent> GetFiltered(PhysicsEventType? type = null, int? limit = null)
    {
        IEnumerable<PhysicsVerificationEvent> query = _events;

        if (type.HasValue)
        {
            query = query.Where(e => e.EventType == type.Value);
        }

        if (limit.HasValue && limit.Value > 0)
        {
            query = query.TakeLast(limit.Value);
        }

        return query.ToArray();
    }

    /// <summary>
    /// Gets the most recent events.
    /// </summary>
    public IReadOnlyList<PhysicsVerificationEvent> GetRecent(int count)
        => _events.TakeLast(Math.Max(1, count)).ToArray();

    /// <summary>
    /// Gets events within a timestamp range.
    /// </summary>
    public IReadOnlyList<PhysicsVerificationEvent> GetByTimeRange(long startStep, long endStep)
        => _events.Where(e => e.Timestamp >= startStep && e.Timestamp <= endStep).ToArray();

    /// <summary>
    /// Exports all events to JSON string.
    /// </summary>
    public string ExportToJson(PhysicsEventType? filterType = null)
    {
        var events = filterType.HasValue ? GetByType(filterType.Value) : GetAll();

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        return JsonSerializer.Serialize(events, options);
    }

    /// <summary>
    /// Exports events to a file.
    /// </summary>
    public async Task ExportToFileAsync(string filePath, PhysicsEventType? filterType = null, CancellationToken ct = default)
    {
        var json = ExportToJson(filterType);
        await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets summary statistics for events.
    /// </summary>
    public Dictionary<PhysicsEventType, int> GetEventCounts()
        => _events
            .GroupBy(e => e.EventType)
            .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// Gets the latest event of a specific type.
    /// </summary>
    public PhysicsVerificationEvent? GetLatest(PhysicsEventType type)
        => _events.Where(e => e.EventType == type).LastOrDefault();
}
