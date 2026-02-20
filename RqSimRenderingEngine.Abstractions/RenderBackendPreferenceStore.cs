using System.Text.Json;

namespace RqSimRenderingEngine.Abstractions;

public static class RenderBackendPreferenceStore
{
    private sealed record State(RenderBackendKind Preferred);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true
    };

    public static RenderBackendKind Load()
    {
        try
        {
            string path = GetPath();
            if (!File.Exists(path))
                return RenderBackendKind.Auto;

            string json = File.ReadAllText(path);
            State? state = JsonSerializer.Deserialize<State>(json, JsonOptions);
            return state?.Preferred ?? RenderBackendKind.Auto;
        }
        catch
        {
            return RenderBackendKind.Auto;
        }
    }

    public static void Save(RenderBackendKind preferred)
    {
        try
        {
            string path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            string json = JsonSerializer.Serialize(new State(preferred), JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best-effort persistence; ignore I/O failures.
        }
    }

    private static string GetPath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RqSimulator",
            "RqSimUI");

        return Path.Combine(dir, "render-backend.json");
    }
}
