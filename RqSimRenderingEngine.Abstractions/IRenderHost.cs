namespace RqSimRenderingEngine.Abstractions;

/// <summary>
/// Interface for unified rendering host across different backends.
/// </summary>
public interface IRenderHost : IDisposable
{
    bool IsInitialized { get; }

    void Initialize(RenderHostInitOptions options);

    void Resize(int width, int height);

    void BeginFrame();

    void EndFrame();

    /// <summary>
    /// Update input state for the current frame.
    /// Routes input to ImGui and camera controls.
    /// </summary>
    /// <param name="snapshot">Input snapshot for the frame</param>
    void UpdateInput(InputSnapshot snapshot);

    /// <summary>
    /// Whether ImGui wants to capture mouse input.
    /// When true, mouse events should not be processed by camera/scene.
    /// </summary>
    bool WantCaptureMouse { get; }

    /// <summary>
    /// Whether ImGui wants to capture keyboard input.
    /// When true, keyboard events should not be processed by camera/scene.
    /// </summary>
    bool WantCaptureKeyboard { get; }
}
