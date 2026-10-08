using CommunityToolkit.Maui;
using RTelemetry.Maui;
namespace RTelemetry.Sample.Maui;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder=MauiApp.CreateBuilder().UseMauiApp<App>().UseMauiCommunityToolkit();
        builder.UseRTelemetry(o=>
        {
            o.Project="dev";o.ApiKey="dev-key";
#if ANDROID
            o.Endpoint=new Uri("http://10.0.2.2:5180/");
#else
            o.Endpoint=new Uri("http://localhost:5180/");
#endif
            o.ContentVersion="sample-1";
            o.FlushInterval=TimeSpan.FromSeconds(3);
            o.OnDiagnostic=m=>System.Diagnostics.Debug.WriteLine(m);
        });
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
