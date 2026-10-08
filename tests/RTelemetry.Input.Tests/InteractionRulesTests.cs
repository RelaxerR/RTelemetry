using RTelemetry.Input;

public class InteractionRulesTests
{
    public static TheoryData<InputElementKind> Controls => new(
        Enum.GetValues<InputElementKind>().Where(k => k is not InputElementKind.Other and not InputElementKind.CollectionView and not InputElementKind.ListView));

    [Theory, MemberData(nameof(Controls))]
    public void StandardControlsAreInteractive(InputElementKind kind) => Assert.True(InteractionRules.IsInteractive(kind));

    [Theory]
    [InlineData(InputElementKind.CollectionView, false, false, false)]
    [InlineData(InputElementKind.CollectionView, true, false, false)]
    [InlineData(InputElementKind.CollectionView, false, true, false)]
    [InlineData(InputElementKind.CollectionView, true, true, true)]
    [InlineData(InputElementKind.ListView, true, false, false)]
    [InlineData(InputElementKind.ListView, true, true, true)]
    public void SelectionRequiresItem(InputElementKind kind, bool enabled, bool hit, bool expected) =>
        Assert.Equal(expected,InteractionRules.IsInteractive(kind, selectionEnabled:enabled,itemHit:hit));

    [Fact] public void CustomMarkerAndTapMakeOtherwisePassiveElementInteractive()
    {
        Assert.False(InteractionRules.IsInteractive(InputElementKind.Other));
        Assert.True(InteractionRules.IsInteractive(InputElementKind.Other,isMarked:true));
        Assert.True(InteractionRules.IsInteractive(InputElementKind.Other,hasTapGesture:true));
    }

    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(-1)]
    public void InvalidDistanceRejected(double distance) => Assert.Throws<ArgumentOutOfRangeException>(() => new InputOptions { DragThreshold=distance }.Validate());
    [Fact] public void InvalidDurationRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => new InputOptions { LongPressThreshold=TimeSpan.Zero }.Validate());
    [Fact] public void ZeroDistanceAndPositiveDurationAllowed() => new InputOptions { DragThreshold=0,LongPressThreshold=TimeSpan.FromTicks(1) }.Validate();
}
