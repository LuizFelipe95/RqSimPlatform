using RQSimulation;
using System.Runtime.CompilerServices;

namespace RqSimVisualization;

// Partial class extension with optimized drawing helper methods and cached GDI resources
public partial class RqSimVisualizationForm
{
    // === Cached GDI Resources for zero-allocation drawing ===
    // Pre-allocated Pens for edges (by weight range)
    private readonly Pen _edgePenVeryStrong = new(Color.FromArgb(220, 255, 0, 0), 3f);
    private readonly Pen _edgePenStrong = new(Color.FromArgb(200, 255, 69, 0), 2.5f);
    private readonly Pen _edgePenMedium = new(Color.FromArgb(180, 255, 140, 0), 2f);
    private readonly Pen _edgePenWeak = new(Color.FromArgb(120, 218, 165, 32), 1.2f);
    private readonly Pen _edgePenVeryWeak = new(Color.FromArgb(80, 160, 160, 160), 0.6f);

    // Pre-allocated Brushes for nodes (by state)
    private readonly SolidBrush _nodeExcited = new(Color.Red);
    private readonly SolidBrush _nodeRefractory = new(Color.Orange);
    private readonly SolidBrush _nodeRest = new(Color.LightBlue);
    private readonly SolidBrush _nodeComposite = new(Color.DarkRed);
    private readonly SolidBrush _nodeClock = new(Color.Blue);

    // Reusable Bitmap for graph rendering (avoids GC pressure)
    private Bitmap? _reusableBitmap;
    private int _lastBitmapWidth;
    private int _lastBitmapHeight;

    /// <summary>
    /// Gets or creates a reusable Bitmap. Disposes old one only if size changed.
    /// Performance: Eliminates ~10 Bitmap allocations per second at 10 FPS.
    /// </summary>
    private Bitmap GetOrCreateBitmap(int width, int height)
    {
        if (_reusableBitmap != null && _lastBitmapWidth == width && _lastBitmapHeight == height)
        {
            return _reusableBitmap;
        }

        _reusableBitmap?.Dispose();
        _reusableBitmap = new Bitmap(Math.Max(width, 1), Math.Max(height, 1));
        _lastBitmapWidth = width;
        _lastBitmapHeight = height;
        return _reusableBitmap;
    }

    /// <summary>
    /// Returns pre-allocated Pen for edge based on weight. Zero allocations.
    /// Performance: Eliminates O(E) Pen allocations per frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Pen GetEdgePen(double weight)
    {
        return weight switch
        {
            > 0.7 => _edgePenVeryStrong,
            > 0.5 => _edgePenStrong,
            > 0.3 => _edgePenMedium,
            > 0.15 => _edgePenWeak,
            _ => _edgePenVeryWeak
        };
    }

    /// <summary>
    /// Returns pre-allocated Brush for node based on state. Zero allocations.
    /// Performance: Eliminates O(N) Brush allocations per frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SolidBrush GetNodeBrush(RQGraph graph, int nodeIndex)
    {
        if (graph.PhysicsProperties[nodeIndex].IsClock)
            return _nodeClock;
        if (graph.PhysicsProperties[nodeIndex].Type == ParticleType.Composite)
            return _nodeComposite;

        return graph.State[nodeIndex] switch
        {
            NodeState.Excited => _nodeExcited,
            NodeState.Refractory => _nodeRefractory,
            _ => _nodeRest
        };
    }

    /// <summary>
    /// Disposes all pre-allocated GDI resources.
    /// Called from OnFormClosing.
    /// </summary>
    private void DisposeGdiResources()
    {
        _edgePenVeryStrong?.Dispose();
        _edgePenStrong?.Dispose();
        _edgePenMedium?.Dispose();
        _edgePenWeak?.Dispose();
        _edgePenVeryWeak?.Dispose();

        _nodeExcited?.Dispose();
        _nodeRefractory?.Dispose();
        _nodeRest?.Dispose();
        _nodeComposite?.Dispose();
        _nodeClock?.Dispose();

        _reusableBitmap?.Dispose();
        _reusableBitmap = null;
    }
}
