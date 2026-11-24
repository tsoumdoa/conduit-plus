namespace ConduitPlus.Core;

public static class LabelExportLayout
{
    public static LabelPlacement[] Place(IEnumerable<LabelCandidate> candidates, Size2 deviceSize,
        double outputDpi, IReadOnlyList<Rect2> reserved) =>
        new LabelLayout().Place(candidates.Select(candidate => candidate with
        {
            Activity = candidate.Activity == LabelActivity.Selected ? LabelActivity.Selected : LabelActivity.Ordinary
        }), deviceSize, outputDpi, reserved);
}
