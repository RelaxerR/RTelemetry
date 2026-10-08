using RTelemetry.Input;

public class InputTests
{
    [Fact] public void ChildResolvesInteractiveAncestor()
    {
        InputNode[] nodes = [new("button", "Button", new(0,0,50,50), true), new("icon", "Image", new(0,0,20,20), false, Parent:0)];
        Assert.Equal("button", HitClassifier.Classify(nodes,new(10,10),new(0,0,100,100)).Target?.Id);
    }
    [Theory] [InlineData(false,true)] [InlineData(true,false)]
    public void UnavailableControlIsMiss(bool enabled,bool visible)
    {
        var hit = HitClassifier.Classify([new("button","Button",new(0,0,50,50),true,enabled,visible)],new(10,10),new(0,0,100,100));
        Assert.False(hit.Click); Assert.True(hit.DisabledTarget);
    }
    [Fact] public void MissHasNormalizedNearestDistance()
    {
        var hit = HitClassifier.Classify([new("button","Button",new(0,0,10,10),true)],new(40,50),new(0,0,60,80));
        Assert.False(hit.Click); Assert.Equal(.5,hit.NearestDistance,8); Assert.Equal(.625,hit.Y);
    }
    [Fact] public void DragReturningToOriginIsStillIgnored()
    {
        var t = new TouchClassifier(new()); t.Begin(1,new(0,0),TimeSpan.Zero);t.Move(1,new(30,0));
        Assert.Null(t.End(1,new(0,0),TimeSpan.FromSeconds(1)));
    }
    [Fact] public void OnlyFirstPointerProducesOneLongPress()
    {
        var t = new TouchClassifier(new());t.Begin(1,new(0,0),TimeSpan.Zero);t.Begin(2,new(0,0),TimeSpan.Zero);
        Assert.Null(t.End(2,new(0,0),TimeSpan.FromSeconds(1)));
        Assert.True(t.End(1,new(0,0),TimeSpan.FromSeconds(1)));
        Assert.Null(t.End(1,new(0,0),TimeSpan.FromSeconds(1)));
    }
    [Fact] public void CancelDoesNotEmit() {var t=new TouchClassifier(new());t.Begin(1,new(),TimeSpan.Zero);t.Cancel();Assert.Null(t.End(1,new(),TimeSpan.Zero));}
}

public class AncestorTests
{
    [Theory] [InlineData(false,true)] [InlineData(true,false)]
    public void AncestorAvailabilityAffectsHitAndNearest(bool enabled,bool visible)
    {
        InputNode[] nodes=[new("parent","Layout",new(0,0,50,50),false,enabled,visible),new("child","Button",new(0,0,20,20),true,Parent:0)];
        var hit=HitClassifier.Classify(nodes,new(10,10),new(0,0,100,100));
        Assert.False(hit.Click);Assert.True(hit.DisabledTarget);Assert.Null(hit.Nearest);
    }
    [Fact] public void CyclicParentIsUnavailableAndTerminates()
    {
        InputNode[] nodes=[new("cycle","Button",new(0,0,20,20),true,Parent:0)];
        var hit=HitClassifier.Classify(nodes,new(10,10),new(0,0,100,100));Assert.False(hit.Click);Assert.Null(hit.Nearest);
    }
    [Fact] public void ForegroundBackgroundObscuresButton()
    {
        InputNode[] nodes=[new("back","Button",new(0,0,20,20),true),new("overlay","BoxView",new(0,0,20,20),false)];
        var hit=HitClassifier.Classify(nodes,new(10,10),new(0,0,100,100));Assert.False(hit.Click);Assert.Equal("overlay",hit.Target?.Id);
    }
}

public class SnapshotTests
{
    [Fact] public void SecondPointerCannotReplacePressSnapshot()
    {
        var tracker=new TouchClassifier(new());
        Assert.True(tracker.Begin(1,new(5,5),TimeSpan.Zero));
        Assert.False(tracker.Begin(2,new(90,90),TimeSpan.Zero));
        Assert.False(tracker.End(1,new(5,5),TimeSpan.FromMilliseconds(100)));
        Assert.True(tracker.Begin(3,new(10,10),TimeSpan.FromSeconds(1)));
    }
    [Fact] public void ReleaseUsesFrozenGeometryAfterOriginalTreeChanges()
    {
        var source=new List<InputNode> { new("original","Button",new(0,0,20,20),true) };
        var snapshot=Array.AsReadOnly(source.ToArray());
        source.Clear();source.Add(new("replacement","Label",new(0,0,20,20),false));
        var hit=HitClassifier.Classify(snapshot,new(10,10),new(0,0,100,100));
        Assert.True(hit.Click);Assert.Equal("original",hit.Target?.Id);
    }
}
