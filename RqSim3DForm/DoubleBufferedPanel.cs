namespace RqSim3DForm;

/// <summary>
/// A Panel with double buffering enabled to prevent flickering during redraws.
/// </summary>
public class DoubleBufferedPanel : Panel
{
    public DoubleBufferedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | 
                 ControlStyles.UserPaint | 
                 ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }
}
