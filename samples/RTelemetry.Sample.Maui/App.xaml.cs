namespace RTelemetry.Sample.Maui;
public partial class App(MainPage page) : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new NavigationPage(page));
}
