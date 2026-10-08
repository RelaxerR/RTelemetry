using CommunityToolkit.Maui.Extensions;
using RTelemetry.Client;
using RTelemetry.Maui;
namespace RTelemetry.Sample.Maui;
public sealed class MainPage : ContentPage
{
    private readonly Label _events=new() { FontSize=11,AutomationId="event-stream" };
    private readonly Queue<string> _recent=new();
    public MainPage(ITelemetryClient client,ClickTracker tracker)
    {
        Title="RTelemetry input lab";AutomationId="input-lab";
        var body=new VerticalStackLayout { Spacing=12,Padding=20 };
        body.Add(new Label { Text="Grant consent, then tap controls or empty space. Dragging must not emit.",AutomationId="instructions" });
        body.Add(Button("Grant consent","consent-grant",()=>client.SetConsent(ConsentState.Granted)));
        body.Add(Button("Revoke consent","consent-revoke",()=>client.SetConsent(ConsentState.Denied)));
        body.Add(Button("Button","button",()=>{}));
        body.Add(new ImageButton { Source="dotnet_bot.png",HeightRequest=60,AutomationId="image-button" });
        var tap=new Label { Text="Tap recognizer",AutomationId="tap-label" };tap.GestureRecognizers.Add(new TapGestureRecognizer());body.Add(tap);
        body.Add(new CheckBox { AutomationId="checkbox" });body.Add(new Switch { AutomationId="switch" });
        body.Add(new RadioButton { Content="Radio option",AutomationId="radio" });
        body.Add(new Slider { AutomationId="slider" });body.Add(new Stepper { AutomationId="stepper" });
        body.Add(new Picker { Title="Picker",ItemsSource=new[] { "First","Second" },AutomationId="picker" });
        body.Add(new DatePicker { AutomationId="date" });body.Add(new TimePicker { AutomationId="time" });
        body.Add(new Entry { Placeholder="Never collected",AutomationId="entry" });
        body.Add(new Editor { Placeholder="Never collected",HeightRequest=70,AutomationId="editor" });
        body.Add(new CollectionView { ItemsSource=Enumerable.Range(1,30).Select(i=>$"Item {i}").ToArray(),HeightRequest=150,SelectionMode=SelectionMode.Single,AutomationId="collection" });
#pragma warning disable CS0618
        body.Add(new ListView { ItemsSource=new[] { "One","Two","Three" },HeightRequest=140,SelectionMode=ListViewSelectionMode.Single,AutomationId="list" });
#pragma warning restore CS0618
        var graphics=new GraphicsView { HeightRequest=90,Drawable=new Surface(),AutomationId="custom-surface" };
        global::RTelemetry.Maui.RTelemetry.SetIsInteractive(graphics,true);body.Add(graphics);
        body.Add(new Button { Text="Disabled target",IsEnabled=false,AutomationId="disabled" });
        body.Add(new BoxView { Color=Colors.LightGray,HeightRequest=80,AutomationId="empty-background" });
        body.Add(Button("Modal page","open-modal",async()=>
        {
            var modal=new ContentPage { AutomationId="modal-page",Title="Modal" };
            modal.Content=new VerticalStackLayout { Padding=30,Children={new Label { Text="Modal background" },Button("Close","close-modal",async()=>await Navigation.PopModalAsync())} };
            await Navigation.PushModalAsync(modal);
        }));
        body.Add(Button("Toolkit popup","open-popup",async()=> await this.ShowPopupAsync(new VerticalStackLayout
        {
            AutomationId="popup-content",Padding=30,Children={new Label { Text="Popup background" },Button("Popup button","popup-button",()=>{})}
        })));
        body.Add(_events);Content=new ScrollView { Content=body };
        tracker.InputCaptured+=(name,props)=>MainThread.BeginInvokeOnMainThread(()=>
        {
            _recent.Enqueue($"{name}: {props["element"]} ({props.GetValueOrDefault("x")}, {props.GetValueOrDefault("y")})");
            while(_recent.Count>12)_recent.Dequeue();_events.Text=string.Join("\n",_recent);
        });
    }
    private static Button Button(string text,string id,Action action) {var b=new Button { Text=text,AutomationId=id };b.Clicked+=(_,_)=>action();return b;}
    private sealed class Surface : IDrawable
    {
        public void Draw(ICanvas canvas,RectF dirtyRect) {canvas.FillColor=Colors.CornflowerBlue;canvas.FillRectangle(dirtyRect);canvas.FontColor=Colors.White;canvas.DrawString("Custom interactive GraphicsView",dirtyRect,HorizontalAlignment.Center,VerticalAlignment.Center);}
    }
}
