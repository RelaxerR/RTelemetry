using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using RTelemetry.Client;

namespace RTelemetry.Maui;

internal static class PlatformInput
{
    private static IServiceProvider? Services => IPlatformApplication.Current?.Services;
    private static ClickTracker? Tracker
    {
        get { try { return Services?.GetService<ClickTracker>(); } catch (Exception) { return null; } }
    }
    private static void Foreground() { try { Services?.GetService<TelemetryClient>()?.OnForeground(); } catch(Exception) { } }
    private static async void Background()
    {
        try { Tracker?.Cancel(); if(Services?.GetService<TelemetryClient>() is { } client) await client.OnBackgroundAsync(); } catch(Exception) { }
    }
    public static void Configure(MauiAppBuilder builder) => builder.ConfigureLifecycleEvents(events =>
    {
#if ANDROID
        events.AddAndroid(android => android.OnCreate((activity, _) =>
        {
            if (activity.Window?.Callback is { } callback && callback is not TouchCallback)
                activity.Window.Callback = new TouchCallback(callback, activity);
        }).OnResume(_ => Foreground()).OnStop(_ => Background()));
#elif IOS || MACCATALYST
        events.AddiOS(ios => ios.OnActivated(app =>
        {
            foreach(var scene in app.ConnectedScenes.OfType<UIKit.UIWindowScene>())
                foreach(var window in scene.Windows)
                    if (!(window.GestureRecognizers?.Any(g => g is TouchObserver) ?? false)) window.AddGestureRecognizer(new TouchObserver());
            Foreground();
        }).DidEnterBackground(_ => Background())
          .SceneOnActivated(scene =>
          {
              if (scene is UIKit.UIWindowScene windowScene)
                  foreach (var window in windowScene.Windows)
                      if (!(window.GestureRecognizers?.Any(g => g is TouchObserver) ?? false)) window.AddGestureRecognizer(new TouchObserver());
              Foreground();
          }).SceneDidEnterBackground(_ => Background()));
#elif WINDOWS
        events.AddWindows(windows => windows.OnWindowCreated(window =>
        {
            if (window.Content is not Microsoft.UI.Xaml.UIElement root) return;
            root.AddHandler(Microsoft.UI.Xaml.UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_,e) =>
            {
                var p=e.GetCurrentPoint(null); Tracker?.Begin(p.PointerId,p.Position.X,p.Position.Y);
            }),true);
            root.AddHandler(Microsoft.UI.Xaml.UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_,e) =>
            {
                var p=e.GetCurrentPoint(null); Tracker?.Move(p.PointerId,p.Position.X,p.Position.Y);
            }),true);
            root.AddHandler(Microsoft.UI.Xaml.UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_,e) =>
            {
                var p=e.GetCurrentPoint(null); Tracker?.End(p.PointerId,p.Position.X,p.Position.Y,FindWindow(window));
            }),true);
            root.AddHandler(Microsoft.UI.Xaml.UIElement.PointerCanceledEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,_)=>Tracker?.Cancel()),true);
            window.Activated += (_,e) => { if(e.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated) Background(); else Foreground(); };
            window.Closed += (_,_) => Background();
            Foreground();
        }));
#endif
    });
    private static Microsoft.Maui.Controls.Window? FindWindow(object native) => Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault(w => ReferenceEquals(w.Handler?.PlatformView,native));
#if ANDROID
    #pragma warning disable XAOBS001 // Forwarding wrapper delegates every callback unchanged; covered by platform build.
    private sealed class TouchCallback(Android.Views.Window.ICallback wrapped, Android.App.Activity activity) : AndroidX.AppCompat.View.WindowCallbackWrapper(wrapped)
    {
        public override bool DispatchTouchEvent(Android.Views.MotionEvent? e)
        {
            try
            {
                if(e is not null)
                {
                    var density=activity.Resources?.DisplayMetrics?.Density ?? 1;
                    var id=e.GetPointerId(e.ActionIndex);var x=e.RawX/density;var y=e.RawY/density;
                    switch(e.ActionMasked)
                    {
                        case Android.Views.MotionEventActions.Down: Tracker?.Begin(id,x,y);break;
                        case Android.Views.MotionEventActions.Move: Tracker?.Move(e.GetPointerId(0),x,y);break;
                        case Android.Views.MotionEventActions.Up: Tracker?.End(id,x,y,FindWindow(activity));break;
                        case Android.Views.MotionEventActions.PointerUp: if(e.ActionIndex==0) Tracker?.End(id,x,y,FindWindow(activity));break;
                        case Android.Views.MotionEventActions.Cancel: Tracker?.Cancel();break;
                    }
                }
            } catch(Exception) { }
            return base.DispatchTouchEvent(e);
        }
    }
#elif IOS || MACCATALYST
    private sealed class TouchObserver : UIKit.UIGestureRecognizer
    {
        private UIKit.UITouch? _first;
        private readonly ObserverDelegate _delegate = new();
        public TouchObserver()
        {
            CancelsTouchesInView=false;DelaysTouchesBegan=false;DelaysTouchesEnded=false;Delegate=_delegate;
        }
        public override void TouchesBegan(Foundation.NSSet touches, UIKit.UIEvent evt)
        {
            if(_first is not null) return;
            _first=touches.AnyObject as UIKit.UITouch;
            if(_first is null)return;
            var p=_first.LocationInView(View);Tracker?.Begin(0,p.X,p.Y);
        }
        public override void TouchesMoved(Foundation.NSSet touches, UIKit.UIEvent evt)
        {
            if(_first is null)return;var p=_first.LocationInView(View);Tracker?.Move(0,p.X,p.Y);
        }
        public override void TouchesEnded(Foundation.NSSet touches, UIKit.UIEvent evt)
        {
            if(_first is null || !touches.Contains(_first))return;
            var p=_first.LocationInView(View);Tracker?.End(0,p.X,p.Y,View is null ? null : FindWindow(View));_first=null;State=UIKit.UIGestureRecognizerState.Failed;
        }
        public override void TouchesCancelled(Foundation.NSSet touches, UIKit.UIEvent evt) { Tracker?.Cancel();_first=null;State=UIKit.UIGestureRecognizerState.Cancelled; }
        public override void Reset() { _first=null; }
        public override bool CanPreventGestureRecognizer(UIKit.UIGestureRecognizer preventedGestureRecognizer) => false;
        public override bool CanBePreventedByGestureRecognizer(UIKit.UIGestureRecognizer preventingGestureRecognizer) => false;
        private sealed class ObserverDelegate : UIKit.UIGestureRecognizerDelegate
        {
            public override bool ShouldRecognizeSimultaneously(UIKit.UIGestureRecognizer gestureRecognizer, UIKit.UIGestureRecognizer otherGestureRecognizer)=>true;
        }
    }
#endif
}
