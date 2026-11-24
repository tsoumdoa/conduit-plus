using ConduitPlus.Core;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class LabelPaintBudgetTests
{
    [Fact]
    public void MotionCapConsumesOnlyOrdinaryPillsInRankOrder()
    {
        var budget = new LabelPaintBudget(new Clock(), fixedLimit: 2);
        var frame = budget.BeginFrame(1);

        Assert.False(frame.Allows(LabelForm.Dot, LabelActivity.Ordinary));
        Assert.True(frame.Allows(LabelForm.Full, LabelActivity.Ordinary));
        Assert.True(frame.Allows(LabelForm.Short, LabelActivity.Ordinary));
        Assert.False(frame.Allows(LabelForm.Full, LabelActivity.Ordinary));
        Assert.True(frame.Allows(LabelForm.Full, LabelActivity.Selected));
        Assert.True(frame.Allows(LabelForm.Short, LabelActivity.Hovered));
    }

    [Fact]
    public void JitterDoesNotChangeTheMotionCutoff()
    {
        var clock = new Clock();
        var budget = new LabelPaintBudget(clock);
        for (var revision = 0; revision < 120; revision++)
        {
            var frame = budget.BeginFrame(revision);
            for (var i = 0; i < 105; i++)
                Assert.Equal(i < 100, frame.Allows(LabelForm.Full, LabelActivity.Ordinary));
            budget.Observe(100, revision % 2 == 0 ? 1.1 : 1.9);
            clock.Advance(16);
        }
    }

    [Fact]
    public void SettlingRestoresEveryPlacedPill()
    {
        var clock = new Clock();
        var budget = new LabelPaintBudget(clock, fixedLimit: 0);
        var moving = budget.BeginFrame(1);
        Assert.False(moving.Allows(LabelForm.Full, LabelActivity.Ordinary));
        clock.Advance(119);
        moving = budget.BeginFrame(1);
        Assert.False(moving.Allows(LabelForm.Short, LabelActivity.Ordinary));

        clock.Advance(1);
        var settled = budget.BeginFrame(1);
        Assert.Null(budget.RedrawAfter);
        for (var i = 0; i < 500; i++)
            Assert.True(settled.Allows(LabelForm.Full, LabelActivity.Ordinary));
    }

    [Fact]
    public void SustainedCostChangesAdjustOnlyAfterTheMinimumInterval()
    {
        var clock = new Clock();
        var budget = new LabelPaintBudget(clock);
        for (var revision = 0; revision < 25; revision++)
        {
            var frame = budget.BeginFrame(revision);
            for (var i = 0; i < 100; i++)
                Assert.True(frame.Allows(LabelForm.Full, LabelActivity.Ordinary));
            budget.Observe(100, 3);
            clock.Advance(10);
        }

        budget.Observe(100, 3);
        var adjusted = budget.BeginFrame(25);
        Assert.True(adjusted.Allows(LabelForm.Full, LabelActivity.Ordinary));
        for (var i = 1; i < 99; i++)
            adjusted.Allows(LabelForm.Full, LabelActivity.Ordinary);
        Assert.False(adjusted.Allows(LabelForm.Full, LabelActivity.Ordinary));
    }

    private sealed class Clock : TimeProvider
    {
        public override long TimestampFrequency => 1000;
        private long _timestamp;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(int milliseconds) => _timestamp += milliseconds;
    }
}
