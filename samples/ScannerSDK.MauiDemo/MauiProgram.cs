using Microsoft.Extensions.DependencyInjection;

namespace ScannerSDK.MauiDemo;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        RegisterMauiDemoServices(builder.Services);
        return builder.Build();
    }

    internal static void RegisterMauiDemoServices(IServiceCollection services)
    {
        services.AddSingleton<IMauiDemoBackend>(static _ => new ScannerMauiDemoBackend());
        services.AddSingleton<IMauiDemoDispatcher>(static _ => new MauiDemoDispatcher());
        services.AddSingleton(static services => new MauiDemoViewModel(
            services.GetRequiredService<IMauiDemoBackend>(),
            services.GetRequiredService<IMauiDemoDispatcher>()));
    }
}
