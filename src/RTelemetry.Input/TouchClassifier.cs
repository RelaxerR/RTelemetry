namespace RTelemetry.Input;

public sealed class InputOptions
{
    public double DragThreshold { get; set; } = 12;
    public TimeSpan LongPressThreshold { get; set; } = TimeSpan.FromMilliseconds(600);
    public void Validate()
    {
        if (!double.IsFinite(DragThreshold) || DragThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(DragThreshold));
        if (LongPressThreshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(LongPressThreshold));
    }
}

public readonly record struct InputPoint(double X, double Y);
public readonly record struct InputRect(double X, double Y, double Width, double Height)
{
    public bool Contains(InputPoint p) => Width > 0 && Height > 0 && p.X >= X && p.Y >= Y && p.X <= X + Width && p.Y <= Y + Height;
    public double Distance(InputPoint p) => Math.Sqrt(Math.Pow(Math.Max(Math.Max(X - p.X, 0), p.X - X - Width), 2) + Math.Pow(Math.Max(Math.Max(Y - p.Y, 0), p.Y - Y - Height), 2));
}

/// <summary>Nodes are ordered back to front; parent indices refer to this snapshot.</summary>
public sealed record InputNode(string Id, string Type, InputRect Bounds, bool Interactive, bool Enabled = true, bool Visible = true, int Parent = -1, bool HitCandidate = true);
public sealed record InputHit(bool Click, InputNode? Target, bool DisabledTarget, InputNode? Nearest, double NearestDistance, double X, double Y);

public static class HitClassifier
{
    public static InputHit Classify(IReadOnlyList<InputNode> nodes, InputPoint point, InputRect page)
    {
        InputNode? target = null;
        InputNode? surface = null;
        var disabled = false;
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            if (!nodes[i].HitCandidate || !nodes[i].Bounds.Contains(point)) continue;
            surface = nodes[i];
            disabled = !Available(nodes, i);
            var j = i;
            for (var guard = 0; j >= 0 && j < nodes.Count && guard < nodes.Count; guard++)
            {
                var node = nodes[j];
                if (node.Interactive) { target = node; break; }
                j = node.Parent;
            }
            // A foreground noninteractive surface obscures controls behind it.
            break;
        }
        var nearest = nodes.Select((node,index)=>(node,index)).Where(n => n.node.Interactive && n.node.Bounds.Width > 0 && n.node.Bounds.Height > 0 && Available(nodes,n.index))
            .OrderBy(n => n.node.Bounds.Distance(point)).Select(n=>n.node).FirstOrDefault();
        var diagonal = Math.Sqrt(page.Width * page.Width + page.Height * page.Height);
        return new(target is not null && !disabled, target ?? surface, disabled, nearest,
            nearest is null || diagonal <= 0 ? 0 : nearest.Bounds.Distance(point) / diagonal,
            page.Width <= 0 ? 0 : Math.Clamp((point.X - page.X) / page.Width, 0, 1),
            page.Height <= 0 ? 0 : Math.Clamp((point.Y - page.Y) / page.Height, 0, 1));
    }
    private static bool Available(IReadOnlyList<InputNode> nodes, int index)
    {
        for (var guard = 0; index >= 0 && index < nodes.Count; guard++)
        {
            if (guard >= nodes.Count || !nodes[index].Enabled || !nodes[index].Visible) return false;
            index = nodes[index].Parent;
        }
        return true;
    }
}

/// <summary>Tracks the first pointer only and rejects a gesture after any threshold crossing.</summary>
public sealed class TouchClassifier(InputOptions options)
{
    private long? _pointer;
    public bool HasActivePointer => _pointer is not null;
    private InputPoint _start;
    private TimeSpan _started;
    private bool _drag;
    public bool Begin(long pointer, InputPoint point, TimeSpan time)
    {
        if (_pointer is not null) return false;
        _pointer = pointer; _start = point; _started = time; _drag = false;
        return true;
    }
    public void Move(long pointer, InputPoint point)
    {
        if (_pointer != pointer) return;
        if (Math.Pow(point.X - _start.X, 2) + Math.Pow(point.Y - _start.Y, 2) > options.DragThreshold * options.DragThreshold) _drag = true;
    }
    public bool? End(long pointer, InputPoint point, TimeSpan time)
    {
        if (_pointer != pointer) return null;
        Move(pointer, point); _pointer = null;
        return _drag ? null : time - _started >= options.LongPressThreshold;
    }
    public void Cancel() => _pointer = null;
}
