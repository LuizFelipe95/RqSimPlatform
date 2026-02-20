namespace RqSimRenderingEngine.Abstractions;

public sealed record RenderHostInitOptions(
    IntPtr WindowHandle,
    int Width,
    int Height,
    bool VSync = true);
