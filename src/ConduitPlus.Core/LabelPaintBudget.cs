namespace ConduitPlus.Core;

/// <summary>Freezes the ordinary pill allowance for each foreground frame.</summary>
public sealed class LabelPaintBudget(TimeProvider? clock = null, int? fixedLimit = null)
{
    public static readonly TimeSpan PromoteInterval = TimeSpan.FromMilliseconds(120);
    private const double TargetMilliseconds = 1.5;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private long? _revision;
    private long _changedAt;
    private long? _adjustedAt;
    private double _estimate = .015;
    private double _appliedEstimate = .015;
    private int _limit = 100;

    public TimeSpan? RedrawAfter { get; private set; }

    public Frame BeginFrame(long revision)
    {
        var now = _clock.GetTimestamp();
        if (_revision != revision)
        {
            _revision = revision;
            _changedAt = now;
        }

        var remaining = PromoteInterval - _clock.GetElapsedTime(_changedAt, now);
        RedrawAfter = remaining > TimeSpan.Zero ? remaining : null;
        return new(RedrawAfter.HasValue ? fixedLimit ?? _limit : int.MaxValue);
    }

    public void Observe(int count, double milliseconds)
    {
        if (fixedLimit.HasValue || count == 0 || milliseconds <= 0) return;
        var sample = milliseconds / count;
        _estimate += .05 * (Math.Clamp(sample, _estimate / 2, _estimate * 2) - _estimate);
        var now = _clock.GetTimestamp();
        _adjustedAt ??= now;
        if (_clock.GetElapsedTime(_adjustedAt.Value, now) < TimeSpan.FromMilliseconds(250) ||
            (_estimate >= _appliedEstimate * .75 && _estimate <= _appliedEstimate * 1.25)) return;

        _appliedEstimate = _estimate;
        _adjustedAt = now;
        _limit = (int)Math.Clamp(TargetMilliseconds / _appliedEstimate, 1, int.MaxValue);
    }

    public struct Frame
    {
        private int _remaining;

        internal Frame(int limit) => _remaining = limit;

        public bool Allows(LabelForm form, LabelActivity activity)
        {
            if (form == LabelForm.Dot) return false;
            if (activity != LabelActivity.Ordinary) return true;
            if (_remaining == 0) return false;
            _remaining--;
            return true;
        }
    }
}
