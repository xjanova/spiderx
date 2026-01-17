using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using SpiderX.App.Services;
using SpiderX.App.ViewModels;
using SpiderX.App.Views;
using SpiderX.Core;
using SpiderX.Services;

namespace SpiderX.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register services
        builder.Services.AddSingleton<ISpiderXService, SpiderXService>();

        // Register ViewModels
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<ContactsViewModel>();
        builder.Services.AddSingleton<ChatViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<NearbyDiscoveryViewModel>();
        builder.Services.AddSingleton<VirtualLanViewModel>();
        builder.Services.AddSingleton<FileSharingViewModel>();

        // Register Views
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<ContactsPage>();
        builder.Services.AddSingleton<ChatPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<NearbyDiscoveryPage>();
        builder.Services.AddSingleton<VirtualLanPage>();
        builder.Services.AddSingleton<FileSharingPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
