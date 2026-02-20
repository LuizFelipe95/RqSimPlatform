namespace RQSimulation.Core.Plugins;

/// <summary>
/// Interface for physics modules that support state serialization and deserialization.
///
/// PURPOSE:
/// ========
/// Enables simulation checkpointing and resumption by allowing modules to save/restore
/// their internal state. This is critical for:
/// - Long-running simulations (save/resume across sessions)
/// - Experiment reproducibility (exact state restoration)
/// - Debugging and analysis (inspect saved states)
/// - Distributed computing (migrate work across nodes)
///
/// IMPLEMENTATION GUIDELINES:
/// ==========================
/// 1. SaveState() should capture ALL internal state needed to resume execution
/// 2. LoadState() should restore the module to the exact same state
/// 3. Use serializable types (primitives, arrays, dictionaries, POCOs)
/// 4. Avoid capturing transient resources (file handles, GPU buffers)
/// 5. GPU modules should save CPU-side copies of buffer data
/// 6. Include version information for future compatibility
///
/// SERIALIZATION FORMAT:
/// =====================
/// The state object should be JSON-serializable. Recommended formats:
/// - Dictionary&lt;string, object&gt; for flexible key-value state
/// - Custom record types for strongly-typed state
/// - byte[] for binary data (will be base64-encoded in JSON)
///
/// EXAMPLE IMPLEMENTATION:
/// =======================
/// <code>
/// public class GravityModule : IPhysicsModule, ISerializableModule
/// {
///     private double _dampingFactor;
///     private int _evolutionStep;
///
///     public object SaveState()
///     {
///         return new Dictionary&lt;string, object&gt;
///         {
///             ["dampingFactor"] = _dampingFactor,
///             ["evolutionStep"] = _evolutionStep,
///             ["version"] = 1
///         };
///     }
///
///     public void LoadState(object state)
///     {
///         if (state is not Dictionary&lt;string, object&gt; dict) return;
///
///         if (dict.TryGetValue("dampingFactor", out var df))
///             _dampingFactor = Convert.ToDouble(df);
///         if (dict.TryGetValue("evolutionStep", out var step))
///             _evolutionStep = Convert.ToInt32(step);
///     }
/// }
/// </code>
/// </summary>
public interface ISerializableModule
{
    /// <summary>
    /// Captures the current internal state of the module.
    ///
    /// REQUIREMENTS:
    /// - Must return a JSON-serializable object
    /// - Should include version information for future compatibility
    /// - Should NOT capture transient resources (file handles, GPU buffers)
    /// - GPU modules should save CPU-side copies of data
    ///
    /// RETURN VALUE:
    /// - Dictionary&lt;string, object&gt; (recommended for flexibility)
    /// - Custom record/class (for strongly-typed state)
    /// - null if module has no persistent state
    /// </summary>
    /// <returns>Serializable state object or null if no state to save</returns>
    object? SaveState();

    /// <summary>
    /// Restores the module's internal state from a previously saved state.
    ///
    /// REQUIREMENTS:
    /// - Must handle null state gracefully (reset to defaults)
    /// - Should validate state object type before casting
    /// - Should handle version migration if state format changed
    /// - Should validate loaded values for sanity (bounds checking)
    /// - Should not throw exceptions (log errors instead)
    ///
    /// IMPLEMENTATION NOTES:
    /// - Called after Initialize() when loading a saved simulation
    /// - GPU buffers should be recreated and populated from loaded data
    /// - Consider calling Initialize() first if module needs setup
    /// </summary>
    /// <param name="state">Previously saved state object (may be null)</param>
    void LoadState(object? state);
}
