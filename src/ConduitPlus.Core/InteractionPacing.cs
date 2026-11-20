namespace ConduitPlus.Core;

public static class InteractionTiming
{
    public const int FrameMilliseconds = 16;
    public const int HoverDwellMilliseconds = 100;
    public const int ScrollRestMilliseconds = 100;
    public const double HoverRadiusPixels = 3;
}

/// <summary>Re-hit a scrolling card only after its last wheel message and after its new rows are presented.</summary>
public sealed class ScrollHoverRest
{
    private long _since;
    public bool Pending { get; private set; }
    public void Defer(long now) { _since = now; Pending = true; }
    public bool Take(long now, bool scrolledThisFrame)
    {
        if (!Pending || scrolledThisFrame || now - _since < InteractionTiming.ScrollRestMilliseconds) return false;
        Pending = false;
        return true;
    }
    public void Cancel() => Pending = false;
}

/// <summary>One intent per resting position. Hosts inject monotonic milliseconds and client pixels.</summary>
public sealed class HoverDwell
{
    private Point2? _anchor;
    private long _since;
    public bool Pending { get; private set; }

    public void Move(Point2 point, long now)
    {
        if (_anchor is { } anchor)
        {
            var dx = point.X - anchor.X; var dy = point.Y - anchor.Y;
            if (dx * dx + dy * dy <= InteractionTiming.HoverRadiusPixels * InteractionTiming.HoverRadiusPixels) return;
        }
        _anchor = point;
        _since = now;
        Pending = true;
    }

    public bool Take(long now)
    {
        if (!Pending || now - _since < InteractionTiming.HoverDwellMilliseconds) return false;
        Pending = false;
        return true;
    }

    public void Cancel() { _anchor = null; Pending = false; }
}

/// <summary>Accumulates high-resolution wheel messages; each frame consumes whole notches once per card.</summary>
public sealed class WheelAccumulator<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, long> _deltas = [];
    public void Add(TKey key, int delta) => _deltas[key] = _deltas.GetValueOrDefault(key) + delta;
    public (TKey Key, int Rows)[] Take()
    {
        var rows = new List<(TKey, int)>();
        foreach (var (key, delta) in _deltas.ToArray())
        {
            var notches = delta / 120;
            if (notches == 0) continue;
            rows.Add((key, (int)Math.Clamp(-notches * 3, int.MinValue, int.MaxValue)));
            _deltas[key] = delta % 120;
        }
        return rows.ToArray();
    }
    public void RemoveWhere(Func<TKey, bool> predicate)
    { foreach (var key in _deltas.Keys.Where(predicate).ToArray()) _deltas.Remove(key); }
    public void Clear() => _deltas.Clear();
}

/// <summary>A snapshot leaves requests made while processing it pending for the next frame.</summary>
public sealed class InteractionFrameQueue<T> where T : notnull
{
    private readonly HashSet<T> _pending = [];
    public bool Pending => _pending.Count > 0;
    public bool Contains(T item) => _pending.Contains(item);
    public void Request(T item) => _pending.Add(item);
    public T[] Take() { var batch = _pending.ToArray(); _pending.Clear(); return batch; }
    public void Remove(T item) => _pending.Remove(item);
    public void Clear() => _pending.Clear();
}
