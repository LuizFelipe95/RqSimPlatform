namespace RqSimVisualization.Controls;

/// <summary>
/// A Panel optimized for hosting native rendering surfaces (DX12, Vulkan, etc.).
/// Disables WinForms background painting to prevent flickering and overwriting
/// the native render output.
/// </summary>
public class NativeRenderPanel : Panel
{
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    public NativeRenderPanel()
    {
        // Disable all WinForms painting
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
    /// Override CreateParams to add WS_EX_NOREDIRECTIONBITMAP.
    /// This prevents Windows from redirecting this window's content to a bitmap,
    /// which is required for proper DX12/Vulkan swapchain presentation.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_NOREDIRECTIONBITMAP: Window content is not redirected to a bitmap
            cp.ExStyle |= WS_EX_NOREDIRECTIONBITMAP;
            return cp;
        }
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
