using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RQSimulation.Core.Infrastructure;

/// <summary>
/// Utility class for dumping simulation settings via reflection to JSON files.
/// Used to diagnose differences between local mode and console mode settings.
/// Files are saved to <c>&lt;SolutionRoot&gt;/Users/default/debug/</c>,
/// falling back to <c>&lt;AppDir&gt;/Users/default/debug/</c>.
/// 
/// NOTE: Uses Utf8JsonWriter directly instead of JsonSerializer.Serialize
/// because the project is AOT-compatible and Dictionary&lt;string, object?&gt;
/// cannot use source-generated serialization.
/// </summary>
public static class SettingsDumper
{
    private static readonly string DebugDirectory = ResolveDebugDirectory();

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true
    };

    /// <summary>
    /// Dumps RQGraph settings to a JSON file via reflection.
    /// Captures all public properties and fields that represent simulation parameters.
    /// </summary>
    /// <param name="graph">The RQGraph instance to dump</param>
    /// <param name="mode">Mode identifier: "local" or "console"</param>
    /// <param name="additionalData">Optional additional data to include</param>
    public static string DumpGraphSettings(RQGraph? graph, string mode, Dictionary<string, object?>? additionalData = null)
    {
        ArgumentNullException.ThrowIfNull(mode);

        try
        {
            EnsureDebugDirectory();

            var dump = new Dictionary<string, object?>
            {
                ["DumpTimestamp"] = DateTime.UtcNow.ToString("O"),
                ["Mode"] = mode,
                ["MachineName"] = Environment.MachineName,
                ["ProcessId"] = Environment.ProcessId
            };

            if (graph is not null)
            {
                dump["GraphSettings"] = ExtractGraphSettings(graph);
                dump["GraphState"] = ExtractGraphState(graph);
            }
            else
            {
                dump["GraphSettings"] = null;
                dump["GraphState"] = null;
            }

            dump["PhysicsConstants"] = ExtractPhysicsConstants();

            if (additionalData is not null)
            {
                foreach (var kvp in additionalData)
                {
                    dump[$"Custom_{kvp.Key}"] = kvp.Value;
                }
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string filename = $"{mode}_mode_settings_{timestamp}.json";
            string filepath = Path.Combine(DebugDirectory, filename);

            Console.WriteLine($"[SettingsDumper] Writing to: {filepath}");

            string json = SerializeDictionary(dump);
            File.WriteAllText(filepath, json);

            Console.WriteLine($"[SettingsDumper] Successfully saved {json.Length} bytes");

            return filepath;
        }
        catch (Exception ex)
        {
            // Don't let dump failures affect simulation
            Console.WriteLine($"[SettingsDumper] Failed to dump settings: {ex.Message}");
            Console.WriteLine($"[SettingsDumper] Stack trace: {ex.StackTrace}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Dumps SimulationConfig to a JSON file via reflection.
    /// </summary>
    public static string DumpSimulationConfig(SimulationConfig? config, string mode, Dictionary<string, object?>? additionalData = null)
    {
        ArgumentNullException.ThrowIfNull(mode);

        try
        {
            EnsureDebugDirectory();

            var dump = new Dictionary<string, object?>
            {
                ["DumpTimestamp"] = DateTime.UtcNow.ToString("O"),
                ["Mode"] = mode,
                ["MachineName"] = Environment.MachineName,
                ["ProcessId"] = Environment.ProcessId
            };

            if (config is not null)
            {
                dump["SimulationConfig"] = ExtractObjectProperties(config);
            }

            dump["PhysicsConstants"] = ExtractPhysicsConstants();

            if (additionalData is not null)
            {
                foreach (var kvp in additionalData)
                {
                    dump[$"Custom_{kvp.Key}"] = kvp.Value;
                }
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string filename = $"{mode}_config_{timestamp}.json";
            string filepath = Path.Combine(DebugDirectory, filename);

            string json = SerializeDictionary(dump);
            File.WriteAllText(filepath, json);

            return filepath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsDumper] Failed to dump config: {ex.Message}");
            return string.Empty;
        }
    }

    private static void EnsureDebugDirectory()
    {
        try
        {
            if (!Directory.Exists(DebugDirectory))
            {
                Directory.CreateDirectory(DebugDirectory);
                Console.WriteLine($"[SettingsDumper] Created debug directory: {DebugDirectory}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsDumper] Failed to create debug directory '{DebugDirectory}': {ex.Message}");
            throw;
        }
    }

    private static Dictionary<string, object?> ExtractGraphSettings(RQGraph graph)
    {
        var settings = new Dictionary<string, object?>
        {
            // Core parameters
            ["N"] = graph.N,
            ["EdgeCount"] = graph.FlatEdgesFrom?.Length ?? 0,
            
            // Try to extract private fields via reflection
        };

        // Use reflection to get private fields that control simulation
        Type graphType = typeof(RQGraph);

        // Get all fields including private
        FieldInfo[] fields = graphType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var field in fields)
        {
            string name = field.Name;
            
            // Skip backing fields for auto-properties (they have angle brackets)
            if (name.Contains('<') && name.Contains('>'))
                continue;
                
            // Skip large arrays and collections
            if (field.FieldType.IsArray)
                continue;
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType) && 
                field.FieldType != typeof(string))
                continue;
                
            // Skip delegates and events
            if (typeof(Delegate).IsAssignableFrom(field.FieldType))
                continue;

            try
            {
                object? value = field.GetValue(graph);
                
                // Only include simple types
                if (IsSimpleType(field.FieldType))
                {
                    settings[$"Field_{name}"] = value;
                }
            }
            catch
            {
                // Skip fields that can't be read
            }
        }

        // Get public properties (simple types only)
        PropertyInfo[] properties = graphType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (!prop.CanRead)
                continue;
                
            // Skip indexers
            if (prop.GetIndexParameters().Length > 0)
                continue;
                
            // Skip arrays and collections
            if (prop.PropertyType.IsArray)
                continue;
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType) && 
                prop.PropertyType != typeof(string))
                continue;

            try
            {
                if (IsSimpleType(prop.PropertyType))
                {
                    object? value = prop.GetValue(graph);
                    settings[$"Prop_{prop.Name}"] = value;
                }
            }
            catch
            {
                // Skip properties that throw
            }
        }

        return settings;
    }

    private static Dictionary<string, object?> ExtractGraphState(RQGraph graph)
    {
        var state = new Dictionary<string, object?>();

        try
        {
            // Extract summary statistics about arrays (not the arrays themselves)
            if (graph.SpectralX is not null && graph.SpectralX.Length > 0)
            {
                state["SpectralX_Length"] = graph.SpectralX.Length;
                state["SpectralX_Min"] = graph.SpectralX.Min();
                state["SpectralX_Max"] = graph.SpectralX.Max();
                state["SpectralX_Mean"] = graph.SpectralX.Average();
            }

            if (graph.SpectralY is not null && graph.SpectralY.Length > 0)
            {
                state["SpectralY_Length"] = graph.SpectralY.Length;
                state["SpectralY_Min"] = graph.SpectralY.Min();
                state["SpectralY_Max"] = graph.SpectralY.Max();
                state["SpectralY_Mean"] = graph.SpectralY.Average();
            }

            if (graph.SpectralZ is not null && graph.SpectralZ.Length > 0)
            {
                state["SpectralZ_Length"] = graph.SpectralZ.Length;
                state["SpectralZ_Min"] = graph.SpectralZ.Min();
                state["SpectralZ_Max"] = graph.SpectralZ.Max();
                state["SpectralZ_Mean"] = graph.SpectralZ.Average();
            }

            // Edge statistics
            if (graph.FlatEdgesFrom is not null)
            {
                state["FlatEdgesFrom_Length"] = graph.FlatEdgesFrom.Length;
            }

            if (graph.EdgeWeightsFlat is not null && graph.EdgeWeightsFlat.Length > 0)
            {
                state["EdgeWeightsFlat_Length"] = graph.EdgeWeightsFlat.Length;
                state["EdgeWeightsFlat_Min"] = graph.EdgeWeightsFlat.Min();
                state["EdgeWeightsFlat_Max"] = graph.EdgeWeightsFlat.Max();
                state["EdgeWeightsFlat_Mean"] = graph.EdgeWeightsFlat.Average();
                state["EdgeWeightsFlat_NonZeroCount"] = graph.EdgeWeightsFlat.Count(w => w > 0);
            }
        }
        catch
        {
            // Ignore extraction errors
        }

        return state;
    }

    private static Dictionary<string, object?> ExtractPhysicsConstants()
    {
        var constants = new Dictionary<string, object?>();

        try
        {
            Type constantsType = typeof(PhysicsConstants);

            // Get all public static fields
            FieldInfo[] fields = constantsType.GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (var field in fields)
            {
                if (IsSimpleType(field.FieldType))
                {
                    try
                    {
                        constants[field.Name] = field.GetValue(null);
                    }
                    catch
                    {
                        // Skip
                    }
                }
            }

            // Get all public static properties
            PropertyInfo[] properties = constantsType.GetProperties(BindingFlags.Public | BindingFlags.Static);

            foreach (var prop in properties)
            {
                if (prop.CanRead && IsSimpleType(prop.PropertyType))
                {
                    try
                    {
                        constants[prop.Name] = prop.GetValue(null);
                    }
                    catch
                    {
                        // Skip
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }

        return constants;
    }

    private static Dictionary<string, object?> ExtractObjectProperties(object obj)
    {
        var result = new Dictionary<string, object?>();
        Type type = obj.GetType();

        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (!prop.CanRead)
                continue;

            if (prop.GetIndexParameters().Length > 0)
                continue;

            try
            {
                object? value = prop.GetValue(obj);
                
                if (value is null || IsSimpleType(prop.PropertyType))
                {
                    result[prop.Name] = value;
                }
                else if (prop.PropertyType.IsEnum)
                {
                    result[prop.Name] = value.ToString();
                }
            }
            catch
            {
                // Skip properties that throw
            }
        }

        return result;
    }

    private static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive ||
               type == typeof(string) ||
               type == typeof(decimal) ||
               type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) ||
               type == typeof(TimeSpan) ||
               type == typeof(Guid) ||
               type.IsEnum ||
               (Nullable.GetUnderlyingType(type) is not null && IsSimpleType(Nullable.GetUnderlyingType(type)!));
    }

    /// <summary>
    /// Serializes a Dictionary&lt;string, object?&gt; to JSON using Utf8JsonWriter.
    /// Avoids reflection-based JsonSerializer which is disabled under AOT.
    /// </summary>
    private static string SerializeDictionary(Dictionary<string, object?> dict)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            WriteDictionaryValue(writer, dict);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteDictionaryValue(Utf8JsonWriter writer, Dictionary<string, object?> dict)
    {
        writer.WriteStartObject();
        foreach (var kvp in dict)
        {
            writer.WritePropertyName(kvp.Key);
            WriteValue(writer, kvp.Value);
        }
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case float f:
                WriteDoubleValue(writer, f);
                break;
            case double d:
                WriteDoubleValue(writer, d);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case DateTime dt:
                writer.WriteStringValue(dt.ToString("O"));
                break;
            case Enum e:
                writer.WriteStringValue(e.ToString());
                break;
            case Dictionary<string, object?> nested:
                WriteDictionaryValue(writer, nested);
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    private static void WriteDoubleValue(Utf8JsonWriter writer, double value)
    {
        if (double.IsNaN(value))
            writer.WriteStringValue("NaN");
        else if (double.IsPositiveInfinity(value))
            writer.WriteStringValue("Infinity");
        else if (double.IsNegativeInfinity(value))
            writer.WriteStringValue("-Infinity");
        else
            writer.WriteNumberValue(value);
    }

    /// <summary>
    /// Custom converter to handle NaN, Infinity in double values
    /// </summary>
    private class DoubleNaNConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? str = reader.GetString();
                return str switch
                {
                    "NaN" => double.NaN,
                    "Infinity" => double.PositiveInfinity,
                    "-Infinity" => double.NegativeInfinity,
                    _ => double.Parse(str ?? "0")
                };
            }
            return reader.GetDouble();
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (double.IsNaN(value))
                writer.WriteStringValue("NaN");
            else if (double.IsPositiveInfinity(value))
                writer.WriteStringValue("Infinity");
            else if (double.IsNegativeInfinity(value))
                writer.WriteStringValue("-Infinity");
            else
                writer.WriteNumberValue(value);
        }
    }

    /// <summary>
    /// Resolves the debug directory by finding the solution root and returning
    /// <c>&lt;SolutionRoot&gt;/Users/default/debug/</c>.
    /// Falls back to <c>&lt;AppDir&gt;/Users/default/debug/</c>.
    /// </summary>
    private static string ResolveDebugDirectory()
    {
        string startDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(startDir);

        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "Users", "default", "debug");
            dir = dir.Parent;
        }

        // Fallback: use AppContext.BaseDirectory
        return Path.Combine(startDir, "Users", "default", "debug");
    }
}
