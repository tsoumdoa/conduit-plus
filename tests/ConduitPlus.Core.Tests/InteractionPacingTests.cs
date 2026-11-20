using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class InteractionPacingTests
{
    [Fact]
    public void MovingModelPointerDoesNotPickUntilRestedAndThenPicksOnlyOnce()
    {
        var dwell = new HoverDwell();
        for (var now = 0; now <= 500; now += 10)
        {
            dwell.Move(new(now, 0), now);
            Assert.False(dwell.Take(now));
        }
        Assert.False(dwell.Take(599));
        Assert.True(dwell.Take(600));
        Assert.False(dwell.Take(1000));
        dwell.Move(new(501, 1), 1000);
        Assert.False(dwell.Pending);
    }

    [Fact]
    public void SmallJitterDoesNotRestartDwellButSlowCumulativeMovementDoes()
    {
        var dwell = new HoverDwell();
        dwell.Move(new(0, 0), 0);
        dwell.Move(new(2, 1), 90);
        Assert.True(dwell.Take(100));
        dwell.Move(new(4, 0), 110);
        Assert.False(dwell.Take(209));
        Assert.True(dwell.Take(210));
    }

    [Fact]
    public void CancellationPreventsAnOldPickAndReentryStartsFreshDwell()
    {
        var dwell = new HoverDwell();
        dwell.Move(new(10, 10), 0);
        dwell.Cancel();
        Assert.False(dwell.Take(200));
        dwell.Move(new(10, 10), 200);
        Assert.False(dwell.Take(299));
        Assert.True(dwell.Take(300));
    }

    [Fact]
    public void ScrollHoverWaitsForRestAfterTheLastMessageAndCommitsOnlyOnce()
    {
        var rest = new ScrollHoverRest();
        rest.Defer(0);
        Assert.False(rest.Take(99, false));
        rest.Defer(99);
        Assert.False(rest.Take(198, false));
        Assert.True(rest.Take(199, false));
        Assert.False(rest.Take(300, false));
        rest.Defer(300); rest.Cancel(); Assert.False(rest.Take(500, false));
    }

    [Fact]
    public void DelayedFirstWheelFramePresentsRowsBeforeRehittingTheStationaryPointer()
    {
        var rest = new ScrollHoverRest();
        rest.Defer(0);
        Assert.False(rest.Take(200, true));
        Assert.True(rest.Pending);
        Assert.True(rest.Take(216, false));
    }

    [Fact]
    public void WheelBurstSumsPerCardAndDoesNotRepeatOnNextFrame()
    {
        var wheel = new WheelAccumulator<string>();
        wheel.Add("a", -120); wheel.Add("a", -240); wheel.Add("b", 120);
        var rows = wheel.Take().ToDictionary(x => x.Key, x => x.Rows);
        Assert.Equal(9, rows["a"]); Assert.Equal(-3, rows["b"]);
        Assert.Empty(wheel.Take());
    }

    [Fact]
    public void PrecisionWheelRetainsFractionsAcrossFramesAndCancelsOppositeMotion()
    {
        var wheel = new WheelAccumulator<string>();
        wheel.Add("a", -30); Assert.False(wheel.HasWhole); Assert.Empty(wheel.Take());
        wheel.Add("a", -60); Assert.Empty(wheel.Take());
        wheel.Add("a", -60); Assert.True(wheel.HasWhole); Assert.Equal(3, Assert.Single(wheel.Take()).Rows);
        Assert.False(wheel.HasWhole); // a fractional remainder must not keep the pump running
        wheel.Add("a", 30); Assert.Empty(wheel.Take());
        wheel.Add("a", 120); Assert.Equal(-3, Assert.Single(wheel.Take()).Rows);
        wheel.Add("a", -120); wheel.Add("a", 120); Assert.Empty(wheel.Take());
    }

    [Fact]
    public void RemovedSessionsLoseWheelRemainders()
    {
        var wheel = new WheelAccumulator<string>();
        wheel.Add("removed", -90); wheel.Add("live", -120);
        wheel.RemoveWhere(key => key == "removed");
        Assert.Equal("live", Assert.Single(wheel.Take()).Key);
        wheel.Add("removed", -30); Assert.Empty(wheel.Take());
        wheel.Clear(); wheel.Add("removed", -90); Assert.Empty(wheel.Take());
    }

    [Fact]
    public void SessionsAndViewsCoalesceAndReentrantRequestsWaitForNextFrame()
    {
        var sessions = new InteractionFrameQueue<string>();
        var views = new InteractionFrameQueue<int>();
        sessions.Request("a"); sessions.Request("a"); sessions.Request("b");
        Assert.True(sessions.Contains("a"));
        Assert.Equal(2, sessions.Take().Length);
        Assert.False(sessions.Contains("a"));
        Assert.False(sessions.Pending);
        sessions.Request("a"); // callback while processing the snapshot
        views.Request(1); views.Request(1);
        Assert.Single(views.Take());
        Assert.Equal("a", Assert.Single(sessions.Take()));
        sessions.Request("disposed"); sessions.Remove("disposed"); Assert.Empty(sessions.Take());
    }
}
