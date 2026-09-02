using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;

namespace FaceRecognitionExample
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseMauiCommunityToolkitCamera()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                })
                .ConfigureMauiHandlers(handlers =>
                {
#if ANDROID
                    handlers.AddHandler(typeof(Controls.LiveCameraView), typeof(Platforms.Android.Handlers.LiveCameraViewHandler));
#endif
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            // Register Services
            builder.Services.AddSingleton<Services.IFaceStorageService, Services.FaceStorageService>();
            builder.Services.AddSingleton<Services.IFaceMatchingService, Services.FaceMatchingService>();

            // Register Pages
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<LiveTrackingPage>();

            return builder.Build();
        }
    }
}
