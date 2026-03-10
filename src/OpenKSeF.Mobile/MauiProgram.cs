using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using OidcBrowser = IdentityModel.OidcClient.Browser.IBrowser;
using OpenKSeF.Mobile.Views;
using ZXing.Net.Maui.Controls;

namespace OpenKSeF.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseBarcodeReader()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Auth
        builder.Services.AddSingleton<IServerSettingsService, ServerSettingsService>();
        builder.Services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IServerSettingsService>();
            return new AuthOptions
            {
                Authority = settings.Authority,
                ClientId = "openksef-mobile",
                RedirectUri = "openksef://auth/callback",
                PostLogoutRedirectUri = "openksef://auth/logout"
            };
        });
        builder.Services.AddSingleton<OidcBrowser, WebAuthenticatorBrowser>();
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<IAuthService, AuthService>();

        // API
        builder.Services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IServerSettingsService>();
            return new ApiOptions { BaseUrl = settings.ServerUrl };
        });
        builder.Services.AddHttpClient<IApiService, ApiService>();

        // Local cache
        builder.Services.AddSingleton<LocalDbService>();

        // Push notifications
        builder.Services.AddSingleton<IDeviceTokenService, DeviceTokenService>();
        builder.Services.AddSingleton<INotificationHubService, NotificationHubService>();

        // ViewModels
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<OnboardingViewModel>();
        builder.Services.AddTransient<TenantsViewModel>();
        builder.Services.AddTransient<TenantFormViewModel>();
        builder.Services.AddTransient<InvoiceListViewModel>();
        builder.Services.AddTransient<InvoiceDetailsViewModel>();
        builder.Services.AddTransient<QrCodeViewModel>();
        builder.Services.AddTransient<ScanSetupQrViewModel>();
        builder.Services.AddTransient<AccountViewModel>();

        // Pages
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<OnboardingPage>();
        builder.Services.AddTransient<TenantsPage>();
        builder.Services.AddTransient<TenantFormPage>();
        builder.Services.AddTransient<InvoiceListPage>();
        builder.Services.AddTransient<InvoiceDetailsPage>();
        builder.Services.AddTransient<QrCodePage>();
        builder.Services.AddTransient<ScanSetupQrPage>();
        builder.Services.AddTransient<AccountPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
