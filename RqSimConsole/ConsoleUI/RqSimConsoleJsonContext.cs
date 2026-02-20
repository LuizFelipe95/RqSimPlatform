using System.Text.Json.Serialization;

namespace RqSimConsole.ConsoleUI;

/// <summary>
/// JSON source generator context for AOT compatibility.
/// Required because reflection-based serialization is disabled in AOT builds.
/// </summary>
[JsonSerializable(typeof(ConsoleConfig))]
[JsonSerializable(typeof(ConsoleResult))]
internal partial class RqSimConsoleJsonContext : JsonSerializerContext
{
}
