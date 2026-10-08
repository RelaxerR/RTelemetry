using Stopwatch = System.Diagnostics.Stopwatch;
using Microsoft.Maui.Controls;
using RTelemetry.Client;
using RTelemetry.Input;

namespace RTelemetry.Maui;

/// <summary>Opt-in marker for custom interactive surfaces. No text is inspected.</summary>
public static class RTelemetry
{
    public static readonly BindableProperty IsInteractiveProperty = BindableProperty.CreateAttached("IsInteractive", typeof(bool), typeof(RTelemetry), false);
    public static bool GetIsInteractive(BindableObject element) => (bool)element.GetValue(IsInteractiveProperty);
    public static void SetIsInteractive(BindableObject element, bool value) => element.SetValue(IsInteractiveProperty, value);
}

public sealed class ClickTracker(ITelemetryClient client, InputOptions options)
{
    private readonly TouchClassifier _touch = new(options);
    /// <summary>Raised for consented input; intended for local diagnostics such as the sample.</summary>
    public event Action<string, IReadOnlyDictionary<string, object?>>? InputCaptured;
    /// <summary>Compatibility method; UseRTelemetry installs platform observers automatically.</summary>
    public void Attach(Application application) { }
    private Snapshot? _pressed;
    private sealed record Snapshot(Page Page, string PageId, string PageType, InputRect Bounds, IReadOnlyList<InputNode> Nodes);
    public void Detach(Application application) => Cancel();
    internal void Begin(long id, double x, double y, Window? window)
    {
        try
        {
            if (!_touch.Begin(id, new(x,y), Stopwatch.GetElapsedTime(0))) return;
            _pressed = client.Consent == ConsentState.Granted && TopPage(window) is { } page ? Capture(page,x,y) : null;
        }
        catch (Exception) { _pressed = null; }
    }
    internal void Move(long id, double x, double y) => _touch.Move(id, new(x,y));
    internal void Cancel() { _touch.Cancel(); _pressed = null; }
    private static Page? TopPage(Window? window)
    {
        if (window?.Page is not { } root) return null;
        var page = root.Navigation.ModalStack.LastOrDefault() ?? root;
        while (page is NavigationPage navigation) page = navigation.CurrentPage;
        if (page is Shell shell) page = shell.CurrentPage;
        return page;
    }
    internal void End(long id, double x, double y, Window? window)
    {
        try
        {
            var isLong = _touch.End(id, new(x,y), Stopwatch.GetElapsedTime(0));
            if (isLong is null)
            {
                if (!_touch.HasActivePointer) _pressed = null;
                return;
            }
            var pressed = _pressed;
            _pressed = null;
            if (client.Consent != ConsentState.Granted) return;
            var currentPage = TopPage(window);
            // A bubbling release can arrive after a child navigated away. Never describe the
            // replacement page as the target of the gesture that removed the original page.
            var snapshot = pressed is not null && !ReferenceEquals(pressed.Page,currentPage)
                ? pressed
                : currentPage is null ? pressed : Capture(currentPage,x,y);
            if (snapshot is null) return;
            var hit = HitClassifier.Classify(snapshot.Nodes,new(x,y),snapshot.Bounds);
            var props = new Dictionary<string,object?>
            {
                ["element"] = hit.Target?.Id ?? snapshot.PageId, ["element_type"] = hit.Target?.Type ?? snapshot.PageType,
                ["page"] = snapshot.PageId, ["x"] = hit.X, ["y"] = hit.Y, ["long_press"] = isLong.Value,
            };
            if (!hit.Click)
            {
                props["nearest_element"] = hit.Nearest?.Id;
                props["nearest_distance"] = hit.NearestDistance;
                props["disabled_target"] = hit.DisabledTarget;
            }
            Emit(hit.Click ? "ui.click" : "ui.miss",props);
        }
        catch (Exception) { /* Observers must never interrupt native input dispatch. */ }
    }
    private static Snapshot Capture(Page page, double x, double y)
    {
            var bounds = Bounds(page);
            // GetVisualTreeElements supplies platform candidates, while ordered traversal corrects
            // sibling ZIndex and explicit clipping keeps offscreen scroll content out of nearest hits.
            var candidates = ((IVisualTreeElement)page).GetVisualTreeElements(x, y).OfType<VisualElement>().ToHashSet();
            var elements = OrderedTree(page).OfType<VisualElement>().Concat(candidates).Distinct().ToList();
            var nodes = new List<InputNode>();
            foreach (var element in elements)
            {
                var parent = elements.IndexOf(element.Parent as VisualElement ?? page);
                var clipped = Clip(Bounds(element), bounds);
                for (var ancestor = element.Parent as VisualElement; ancestor is not null; ancestor = ancestor.Parent as VisualElement)
                    if (ancestor is ScrollView or CollectionView || ancestor is Layout { IsClippedToBounds: true })
                        clipped = Clip(clipped, Bounds(ancestor));
                nodes.Add(new(Describe(element),element.GetType().Name,clipped,Interactive(element,x,y),element.IsEnabled,element.IsVisible,
                    parent >= nodes.Count ? -1 : parent));
            }
        return new(page,Describe(page),page.GetType().Name,bounds,Array.AsReadOnly(nodes.ToArray()));
    }
    public void TrackClick(Element element, string kind = "custom", IReadOnlyDictionary<string, object?>? extra = null)
    {
        try
        {
        var props = new Dictionary<string,object?> { ["element"] = Describe(element), ["element_type"] = element.GetType().Name, ["kind"] = kind, ["long_press"] = false };
        Element? parent = element;
        while (parent is not null and not Page) parent = parent.Parent;
        props["page"] = parent is null ? null : Describe(parent);
        if (extra is not null) foreach (var pair in extra) props[pair.Key] = pair.Value;
        Emit("ui.click",props);
            }
        catch (Exception) { }
    }

    private void Emit(string name, Dictionary<string,object?> props)
    {
        if (client.Consent != ConsentState.Granted) return;
        client.Track(name,props);
        try { InputCaptured?.Invoke(name,props); } catch (Exception) { }
    }
    #pragma warning disable CS0618 // ListView remains supported for existing applications.
    private static bool Interactive(VisualElement element, double x, double y)
    {
        var kind = element switch
        {
            Button => InputElementKind.Button,
            ImageButton => InputElementKind.ImageButton,
            CheckBox => InputElementKind.CheckBox,
            Switch => InputElementKind.Switch,
            RadioButton => InputElementKind.RadioButton,
            Slider => InputElementKind.Slider,
            Stepper => InputElementKind.Stepper,
            Picker => InputElementKind.Picker,
            DatePicker => InputElementKind.DatePicker,
            TimePicker => InputElementKind.TimePicker,
            Entry => InputElementKind.Entry,
            Editor => InputElementKind.Editor,
            CollectionView => InputElementKind.CollectionView,
            ListView => InputElementKind.ListView,
            _ => InputElementKind.Other,
        };
        var selectionEnabled = element is CollectionView { SelectionMode: not SelectionMode.None } or ListView { SelectionMode: not ListViewSelectionMode.None };
        return InteractionRules.IsInteractive(kind,
            element is View view && view.GestureRecognizers.OfType<TapGestureRecognizer>().Any(),
            RTelemetry.GetIsInteractive(element), selectionEnabled,
            selectionEnabled && IsSelectionHit(element,x,y));
    }
    private static bool IsSelectionHit(VisualElement element, double x, double y)
    {
        if (element is not CollectionView { SelectionMode: not SelectionMode.None } && element is not ListView { SelectionMode: not ListViewSelectionMode.None }) return false;
#if IOS || MACCATALYST
        if (element.Handler?.PlatformView is UIKit.UIView view && view.Window is { } window)
        {
            foreach (var candidate in NativeChildren(view))
            {
                var point = candidate.ConvertPointFromView(new CoreGraphics.CGPoint(x,y),window);
                if (candidate is UIKit.UICollectionView collection && collection.IndexPathForItemAtPoint(point) is not null) return true;
                if (candidate is UIKit.UITableView list && list.IndexPathForRowAtPoint(point) is not null) return true;
            }
        }
#elif ANDROID
        if (element.Handler?.PlatformView is Android.Views.View view)
        {
            var position=new int[2];view.GetLocationOnScreen(position);
            var density=view.Context?.Resources?.DisplayMetrics?.Density ?? 1;
            var px=(float)(x*density-position[0]);var py=(float)(y*density-position[1]);
            if (view is AndroidX.RecyclerView.Widget.RecyclerView collection) return collection.FindChildViewUnder(px,py) is not null;
            if (view is Android.Widget.ListView list) return list.PointToPosition((int)px,(int)py) != Android.Widget.AdapterView.InvalidPosition;
        }
#elif WINDOWS
        if (element.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ListViewBase list)
            return Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(new Windows.Foundation.Point(x,y),list).Any(v => v is Microsoft.UI.Xaml.Controls.ListViewItem or Microsoft.UI.Xaml.Controls.GridViewItem);
#endif
        return false;
    }
#if IOS || MACCATALYST
    private static IEnumerable<UIKit.UIView> NativeChildren(UIKit.UIView view)
    {
        yield return view;
        foreach(var child in view.Subviews)
            foreach(var descendant in NativeChildren(child)) yield return descendant;
    }
#endif
    private static string Describe(Element e) => !string.IsNullOrEmpty(e.AutomationId) ? e.AutomationId : !string.IsNullOrEmpty(e.StyleId) ? e.StyleId : e.GetType().Name;
    private static IEnumerable<IVisualTreeElement> OrderedTree(IVisualTreeElement root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren().OrderBy(c => (c as VisualElement)?.ZIndex ?? 0))
            foreach (var descendant in OrderedTree(child)) yield return descendant;
    }
    private static InputRect Clip(InputRect rectangle, InputRect clip)
    {
        var x=Math.Max(rectangle.X,clip.X);var y=Math.Max(rectangle.Y,clip.Y);
        return new(x,y,Math.Max(0,Math.Min(rectangle.X+rectangle.Width,clip.X+clip.Width)-x),Math.Max(0,Math.Min(rectangle.Y+rectangle.Height,clip.Y+clip.Height)-y));
    }
    private static InputRect Bounds(VisualElement e)
    {
#if ANDROID
        if (e.Handler?.PlatformView is Android.Views.View view)
        {
            var position = new int[2]; view.GetLocationOnScreen(position);
            var density = view.Context?.Resources?.DisplayMetrics?.Density ?? 1;
            return new(position[0]/density,position[1]/density,view.Width/density,view.Height/density);
        }
#elif IOS || MACCATALYST
        if (e.Handler?.PlatformView is UIKit.UIView view && view.Window is { } window)
        {
            var rect = view.ConvertRectToView(view.Bounds,window);
            return new(rect.X,rect.Y,rect.Width,rect.Height);
        }
#elif WINDOWS
        if(e.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement view)
        {
            var p = view.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point());
            return new(p.X,p.Y,view.ActualWidth,view.ActualHeight);
        }
#endif
        var x=e.X;var y=e.Y;
        for (var parent=e.Parent as VisualElement;parent is not null;parent=parent.Parent as VisualElement) { x+=parent.X;y+=parent.Y; }
        return new(x,y,e.Width,e.Height);
    }
}
