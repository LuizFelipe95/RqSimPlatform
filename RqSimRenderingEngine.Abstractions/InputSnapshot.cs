using System.Numerics;

namespace RqSimRenderingEngine.Abstractions;

/// <summary>
/// Unified input snapshot for rendering backends.
/// Captures mouse, keyboard, and modifier state for a single frame.
/// Used to abstract WinForms input events for both Veldrid and DX12 backends.
/// </summary>
public readonly struct InputSnapshot
{
    /// <summary>
    /// Mouse position in client coordinates.
    /// </summary>
    public Vector2 MousePosition { get; init; }

    /// <summary>
    /// Mouse wheel delta (positive = forward/up, negative = back/down).
    /// Normalized: 1.0 = one "click" of typical scroll wheel.
    /// </summary>
    public float WheelDelta { get; init; }

    /// <summary>
    /// State of each mouse button (indexed by <see cref="MouseButton"/>).
    /// </summary>
    public MouseButtonState MouseButtons { get; init; }

    /// <summary>
    /// Currently held modifier keys.
    /// </summary>
    public KeyModifiers Modifiers { get; init; }

    /// <summary>
    /// Keys that were pressed this frame (key down events).
    /// </summary>
    public IReadOnlyList<KeyCode> KeysPressed { get; init; }

    /// <summary>
    /// Keys that were released this frame (key up events).
    /// </summary>
    public IReadOnlyList<KeyCode> KeysReleased { get; init; }

    /// <summary>
    /// Text input characters received this frame.
    /// </summary>
    public IReadOnlyList<char> TextInput { get; init; }

    /// <summary>
    /// Time since last frame in seconds.
    /// </summary>
    public float DeltaTime { get; init; }

    /// <summary>
    /// Create an empty input snapshot with default values.
    /// </summary>
    public static InputSnapshot Empty { get; } = new()
    {
        MousePosition = Vector2.Zero,
        WheelDelta = 0f,
        MouseButtons = MouseButtonState.None,
        Modifiers = KeyModifiers.None,
        KeysPressed = Array.Empty<KeyCode>(),
        KeysReleased = Array.Empty<KeyCode>(),
        TextInput = Array.Empty<char>(),
        DeltaTime = 0f
    };
}

/// <summary>
/// Mouse button identifier.
/// </summary>
public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2,
    XButton1 = 3,
    XButton2 = 4
}

/// <summary>
/// Packed mouse button state (bit flags).
/// </summary>
[Flags]
public enum MouseButtonState
{
    None = 0,
    Left = 1 << MouseButton.Left,
    Right = 1 << MouseButton.Right,
    Middle = 1 << MouseButton.Middle,
    XButton1 = 1 << MouseButton.XButton1,
    XButton2 = 1 << MouseButton.XButton2
}

/// <summary>
/// Keyboard modifier flags.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Super = 8  // Windows key / Command key
}

/// <summary>
/// Virtual key codes for keyboard input.
/// Subset of common keys needed for ImGui and camera control.
/// </summary>
public enum KeyCode
{
    None = 0,

    // Letters
    A = 65, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    // Numbers
    D0 = 48, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    // Function keys
    F1 = 112, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    // Navigation
    Left = 37,
    Up = 38,
    Right = 39,
    Down = 40,

    Home = 36,
    End = 35,
    PageUp = 33,
    PageDown = 34,
    Insert = 45,
    Delete = 46,

    // Editing
    Back = 8,     // Backspace
    Tab = 9,
    Enter = 13,
    Escape = 27,
    Space = 32,

    // Modifiers (as keys)
    ShiftKey = 16,
    ControlKey = 17,
    AltKey = 18,
    LWin = 91,
    RWin = 92
}

/// <summary>
/// Extension methods for input types.
/// </summary>
public static class InputExtensions
{
    /// <summary>
    /// Check if a specific mouse button is pressed.
    /// </summary>
    public static bool IsPressed(this MouseButtonState state, MouseButton button)
    {
        int flag = 1 << (int)button;
        return ((int)state & flag) != 0;
    }

    /// <summary>
    /// Convert ImGui mouse button index to <see cref="MouseButton"/>.
    /// </summary>
    public static MouseButton ToMouseButton(int imguiButton)
    {
        return imguiButton switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Right,
            2 => MouseButton.Middle,
            3 => MouseButton.XButton1,
            4 => MouseButton.XButton2,
            _ => MouseButton.Left
        };
    }
}
