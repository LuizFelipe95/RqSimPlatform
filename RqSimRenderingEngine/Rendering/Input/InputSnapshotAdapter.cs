using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows.Forms;
using RqSimRenderingEngine.Abstractions;

namespace RqSimRenderingEngine.Rendering.Input;

/// <summary>
/// Adapter that converts WinForms input events into <see cref="InputSnapshot"/>.
/// Tracks cumulative state across a frame and produces a snapshot for rendering.
/// 
/// USAGE:
/// 1. Hook WinForms events to OnMouse*, OnKey*, OnKeyPress methods
/// 2. Call CreateSnapshot() once per frame to get the current input state
/// 3. Call ResetFrame() after processing the snapshot
/// </summary>
public sealed class InputSnapshotAdapter
{
    private Vector2 _mousePosition;
    private float _wheelDelta;
    private MouseButtonState _mouseButtons;
    private KeyModifiers _modifiers;

    private readonly List<KeyCode> _keysPressed = new(8);
    private readonly List<KeyCode> _keysReleased = new(8);
    private readonly List<char> _textInput = new(16);

    private DateTime _lastFrameTime = DateTime.Now;

    /// <summary>
    /// Current mouse position.
    /// </summary>
    public Vector2 MousePosition => _mousePosition;

    /// <summary>
    /// Current mouse button state.
    /// </summary>
    public MouseButtonState MouseButtons => _mouseButtons;

    /// <summary>
    /// Current modifier keys.
    /// </summary>
    public KeyModifiers Modifiers => _modifiers;

    /// <summary>
    /// Handle mouse move event.
    /// </summary>
    public void OnMouseMove(MouseEventArgs e)
    {
        _mousePosition = new Vector2(e.X, e.Y);
    }

    /// <summary>
    /// Handle mouse down event.
    /// </summary>
    public void OnMouseDown(MouseEventArgs e)
    {
        _mousePosition = new Vector2(e.X, e.Y);
        _mouseButtons |= ToMouseButtonState(e.Button);
    }

    /// <summary>
    /// Handle mouse up event.
    /// </summary>
    public void OnMouseUp(MouseEventArgs e)
    {
        _mousePosition = new Vector2(e.X, e.Y);
        _mouseButtons &= ~ToMouseButtonState(e.Button);
    }

    /// <summary>
    /// Handle mouse wheel event.
    /// </summary>
    public void OnMouseWheel(MouseEventArgs e)
    {
        _wheelDelta += e.Delta / 120f;
    }

    /// <summary>
    /// Handle key down event.
    /// </summary>
    public void OnKeyDown(KeyEventArgs e)
    {
        UpdateModifiers(e);

        KeyCode key = ToKeyCode(e.KeyCode);
        if (key != KeyCode.None && !_keysPressed.Contains(key))
        {
            _keysPressed.Add(key);
        }
    }

    /// <summary>
    /// Handle key up event.
    /// </summary>
    public void OnKeyUp(KeyEventArgs e)
    {
        UpdateModifiers(e);

        KeyCode key = ToKeyCode(e.KeyCode);
        if (key != KeyCode.None && !_keysReleased.Contains(key))
        {
            _keysReleased.Add(key);
        }
    }

    /// <summary>
    /// Handle key press event (text input).
    /// </summary>
    public void OnKeyPress(KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) || e.KeyChar == '\t' || e.KeyChar == '\r' || e.KeyChar == '\n')
        {
            _textInput.Add(e.KeyChar);
        }
    }

    /// <summary>
    /// Create an input snapshot for the current frame.
    /// </summary>
    public InputSnapshot CreateSnapshot()
    {
        DateTime now = DateTime.Now;
        float deltaTime = (float)(now - _lastFrameTime).TotalSeconds;
        deltaTime = Math.Clamp(deltaTime, 0.0001f, 0.1f);

        return new InputSnapshot
        {
            MousePosition = _mousePosition,
            WheelDelta = _wheelDelta,
            MouseButtons = _mouseButtons,
            Modifiers = _modifiers,
            KeysPressed = _keysPressed.ToArray(),
            KeysReleased = _keysReleased.ToArray(),
            TextInput = _textInput.ToArray(),
            DeltaTime = deltaTime
        };
    }

    /// <summary>
    /// Reset per-frame state (wheel delta, key events, text input).
    /// Call after processing the snapshot.
    /// </summary>
    public void ResetFrame()
    {
        _wheelDelta = 0f;
        _keysPressed.Clear();
        _keysReleased.Clear();
        _textInput.Clear();
        _lastFrameTime = DateTime.Now;
    }

    private void UpdateModifiers(KeyEventArgs e)
    {
        _modifiers = KeyModifiers.None;
        if (e.Shift) _modifiers |= KeyModifiers.Shift;
        if (e.Control) _modifiers |= KeyModifiers.Control;
        if (e.Alt) _modifiers |= KeyModifiers.Alt;
    }

    private static MouseButtonState ToMouseButtonState(System.Windows.Forms.MouseButtons button)
    {
        return button switch
        {
            System.Windows.Forms.MouseButtons.Left => MouseButtonState.Left,
            System.Windows.Forms.MouseButtons.Right => MouseButtonState.Right,
            System.Windows.Forms.MouseButtons.Middle => MouseButtonState.Middle,
            System.Windows.Forms.MouseButtons.XButton1 => MouseButtonState.XButton1,
            System.Windows.Forms.MouseButtons.XButton2 => MouseButtonState.XButton2,
            _ => MouseButtonState.None
        };
    }

    private static KeyCode ToKeyCode(Keys key)
    {
        // Direct mapping for letters A-Z (65-90)
        if (key >= Keys.A && key <= Keys.Z)
            return (KeyCode)key;

        // Direct mapping for digits 0-9 (48-57)
        if (key >= Keys.D0 && key <= Keys.D9)
            return (KeyCode)key;

        // Function keys F1-F12 (112-123)
        if (key >= Keys.F1 && key <= Keys.F12)
            return (KeyCode)key;

        return key switch
        {
            // Navigation
            Keys.Left => KeyCode.Left,
            Keys.Up => KeyCode.Up,
            Keys.Right => KeyCode.Right,
            Keys.Down => KeyCode.Down,
            Keys.Home => KeyCode.Home,
            Keys.End => KeyCode.End,
            Keys.PageUp => KeyCode.PageUp,
            Keys.PageDown => KeyCode.PageDown,
            Keys.Insert => KeyCode.Insert,
            Keys.Delete => KeyCode.Delete,

            // Editing
            Keys.Back => KeyCode.Back,
            Keys.Tab => KeyCode.Tab,
            Keys.Enter => KeyCode.Enter,
            Keys.Escape => KeyCode.Escape,
            Keys.Space => KeyCode.Space,

            // Modifiers
            Keys.ShiftKey => KeyCode.ShiftKey,
            Keys.ControlKey => KeyCode.ControlKey,
            Keys.Menu => KeyCode.AltKey,
            Keys.LWin => KeyCode.LWin,
            Keys.RWin => KeyCode.RWin,

            _ => KeyCode.None
        };
    }
}
