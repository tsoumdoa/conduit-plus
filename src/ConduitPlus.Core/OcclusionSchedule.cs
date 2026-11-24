namespace ConduitPlus.Core;

public readonly record struct OcclusionAnswer(bool Visible, bool Occluded, bool Current)
{
    public double Opacity(bool active) => !Visible ? 0 : !Occluded ? 1 : active ? 0.45 : 0;
    public bool HasHit(bool active) => Current && Opacity(active) > 0;
}

/// <summary>Stable candidate slots belong to one anchor revision and viewport.</summary>
public sealed class OcclusionSchedule(int count)
{
    private readonly Entry[] _entries = new Entry[count];
    private int _cursor;
    private long _scene = -1;

    public int Classify(OccluderIndex index, long scene, long projection,
        ReadOnlySpan<OcclusionSegment?> segments, ReadOnlySpan<int> active, Func<bool> timeRemaining, int minimumBackgroundSlots = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(segments.Length, _entries.Length);
        if (_scene != scene)
        {
            Array.Clear(_entries);
            _scene = scene;
            _cursor = 0;
        }
        var visited = 0;
        foreach (var slot in active)
        {
            if (!timeRemaining()) break;
            if (Visit(slot, index, projection, segments[slot])) visited++;
        }
        // Reserve a caller-selected amount of round-robin progress even after an expensive frame.
        for (var scanned = 0; scanned < count && (scanned < minimumBackgroundSlots || timeRemaining()); scanned++)
        {
            var slot = _cursor;
            _cursor = (_cursor + 1) % count;
            if (Visit(slot, index, projection, segments[slot])) visited++;
        }
        return visited;
    }

    public OcclusionAnswer Answer(int slot, long scene, long projection)
    {
        var entry = _entries[slot];
        if (_scene != scene || !entry.Classified) return default;
        return new(entry.InView, entry.Blocked, entry.Projection == projection);
    }

    private bool Visit(int slot, OccluderIndex index, long projection, OcclusionSegment? segment)
    {
        ref var entry = ref _entries[slot];
        if (entry.Classified && entry.Projection == projection) return false;
        entry = new(true, segment.HasValue, segment is { } ray && ray.IsOccluded(index), projection);
        return true;
    }

    private readonly record struct Entry(bool Classified, bool InView, bool Blocked, long Projection);
}

