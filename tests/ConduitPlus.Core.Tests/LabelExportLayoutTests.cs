using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class LabelExportLayoutTests
{
    private static LabelCandidate Candidate(int index, double x) =>
        new(new(Guid.Empty, new(Guid.Empty, "{0}"), null, index), new(x, 100), 0,
            LabelActivity.Ordinary, new(true, false, true), 35, 20);

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void RecomputesTheSameSettledFormsAtCaptureDpi(int scale)
    {
        LabelCandidate[] candidates = [Candidate(0, 100), Candidate(1, 200)];
        Rect2[] reserved = [new(112, 90, 42, 20)];
        var expected = LabelExportLayout.Place(candidates, new(400, 240), 1, reserved);
        var actual = LabelExportLayout.Place(candidates.AsEnumerable().Reverse().Select(c => c with
        {
            Anchor = new(c.Anchor.X * scale, c.Anchor.Y * scale)
        }), new(400 * scale, 240 * scale), scale,
            reserved.Select(r => new Rect2(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale)).ToArray());
        Assert.Equal(expected, actual);
        Assert.All(actual, placement => Assert.Equal(LabelForm.Full, placement.Form));
        Assert.True(actual[0].Left);
    }

    [Fact]
    public void RemovesHoverPromotionAndHiddenHoverButKeepsFadedSelection()
    {
        var hiddenHover = Candidate(2, 230) with { Activity = LabelActivity.Hovered, Visibility = new(true, true, true) };
        var selection = Candidate(3, 320) with { Activity = LabelActivity.Selected, Visibility = new(true, true, true) };
        var frame = LabelExportLayout.Place(
            [Candidate(0, 100) with { Activity = LabelActivity.Hovered }, Candidate(1, 110), hiddenHover, selection],
            new(400, 240), 1, []);
        Assert.DoesNotContain(frame, p => p.Identity == hiddenHover.Identity);
        Assert.Equal(LabelForm.Dot, frame.Single(p => p.Identity == Candidate(0, 100).Identity).Form);
        Assert.Equal(.45, frame.Single(p => p.Identity == selection.Identity).Opacity);
    }
}
