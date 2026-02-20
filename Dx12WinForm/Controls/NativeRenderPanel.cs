namespace RqSimUI.Controls;

/// <summary>
/// A Panel optimized for hosting native rendering surfaces (DX12, Vulkan, etc.).
/// Disables WinForms background painting to prevent flickering and overwriting
/// the native render output.
/// </summary>
public class NativeRenderPanel : Panel
{
    public NativeRenderPanel()
    {
        // Disable all WinForms painting - let DX12 handle it
        SetStyle(ControlStyles.Opaque, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        SetStyle(ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.DoubleBuffer, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        SetStyle(ControlStyles.ResizeRedraw, false);
        
        // Ensure we don't paint the background
        BackColor = Color.Black;
    }

    /// <summary>
    /// Prevent WinForms from painting background.
    /// </summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Do nothing - DX12 will handle all rendering
    }

    /// <summary>
    /// Prevent WinForms from painting.
    /// </summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        // Do nothing - DX12 will handle all rendering
    }

    /// <summary>
    /// Handle WM_ERASEBKGND to prevent flicker.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        const int WM_ERASEBKGND = 0x0014;
        
        if (m.Msg == WM_ERASEBKGND)
        {
            // Return non-zero to indicate we handled background erasing
            m.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref m);
    }
}
