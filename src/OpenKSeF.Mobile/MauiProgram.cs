using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using OidcBrowser = Duende.IdentityModel.OidcClient.Browser.IBrowser;
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
                fonts.AddFont("Inter-Regular.ttf", "Inter");
                fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");
                fonts.AddFont("Inter-Bold.ttf", "InterBold");
                fonts.AddFont("JetBrainsMono-Regular.ttf", "JetBrainsMono");
            });

        builder.Services.AddSingleton<IServerSettingsService, ServerSettingsService>();
        builder.Services.AddSingleton(new AuthOptions());
        builder.Services.AddSingleton<OidcBrowser, WebAuthenticatorBrowser>();
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        builder.Services.AddSingleton<IAuthService, AuthService>();

        ApiService.Register(builder.Services);

        builder.Services.AddSingleton<LocalDbService>();

        builder.Services.AddSingleton<IDeviceTokenService, DeviceTokenService>();
        builder.Services.AddSingleton<INotificationHubService, NotificationHubService>();

        builder.Services.AddTransient<IPostLoginNavigationService, PostLoginNavigationService>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<OnboardingViewModel>();
        builder.Services.AddTransient<TenantsViewModel>();
        builder.Services.AddTransient<TenantFormViewModel>();
        builder.Services.AddTransient<InvoiceListViewModel>();
        builder.Services.AddTransient<InvoiceDetailsViewModel>();
        builder.Services.AddTransient<QrCodeViewModel>();
        builder.Services.AddTransient<ScanSetupQrViewModel>();
        builder.Services.AddTransient<PairingViewModel>();
        builder.Services.AddTransient<AccountViewModel>();
        builder.Services.AddTransient<SellerProfileViewModel>();
        builder.Services.AddTransient<IssuedInvoiceListViewModel>();
        builder.Services.AddTransient<InvoiceDraftViewModel>();
        builder.Services.AddTransient<IssuedInvoiceDetailsViewModel>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<OnboardingPage>();
        builder.Services.AddTransient<TenantsPage>();
        builder.Services.AddTransient<TenantFormPage>();
        builder.Services.AddTransient<InvoiceListPage>();
        builder.Services.AddTransient<InvoiceDetailsPage>();
        builder.Services.AddTransient<QrCodePage>();
        builder.Services.AddTransient<ScanSetupQrPage>();
        builder.Services.AddTransient<PairingPage>();
        builder.Services.AddTransient<AccountPage>();
        builder.Services.AddTransient<SellerProfilePage>();
        builder.Services.AddTransient<IssuedInvoiceListPage>();
        builder.Services.AddTransient<InvoiceDraftPage>();
        builder.Services.AddTransient<IssuedInvoiceDetailsPage>();

#if ANDROID
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<Shell, SurfaceShellRenderer>());

        // Keyboard.Numeric installs a locale key listener that deletes the other decimal separator, so 2,5 became 25 on en-US.
        // DigitsKeyListener is an InputFilter and strips pasted text, so 1e5 became 15. TextKeyListener filters nothing; the form refuses what it cannot parse.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("DecimalEntryInput", (handler, entry) =>
        {
            if (entry is not Views.Controls.DecimalEntry)
                return;

            handler.PlatformView.KeyListener = Android.Text.Method.TextKeyListener.GetInstance(false, Android.Text.Method.TextKeyListener.Capitalize.None);
            handler.PlatformView.SetRawInputType(Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal);
        });

        // The native underline follows the Android night config, which can disagree with the MAUI theme, so tint the AndroidX line layer MAUI adds.
        static void TintUnderline(Android.Views.View view)
        {
            if (view.Background is not Android.Graphics.Drawables.LayerDrawable { NumberOfLayers: 2 } layers)
                return;

            var app = Microsoft.Maui.Controls.Application.Current!;
            var dark = app.RequestedTheme == AppTheme.Dark;
            var line = ((Color)app.Resources[dark ? "UiMutedDark" : "UiMuted"]).ToInt();
            var focused = ((Color)app.Resources[dark ? "UiPrimaryDark" : "UiPrimary"]).ToInt();
            layers.GetDrawable(1)!.Mutate().SetTintList(new Android.Content.Res.ColorStateList(
                [[Android.Resource.Attribute.StateFocused], []], [focused, line]));
        }
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping(nameof(IView.Background), (handler, _) => TintUnderline(handler.PlatformView));
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping(nameof(IView.Background), (handler, _) => TintUnderline(handler.PlatformView));
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping(nameof(IView.Background), (handler, _) => TintUnderline(handler.PlatformView));
        Microsoft.Maui.Handlers.DatePickerHandler.Mapper.AppendToMapping(nameof(IView.Background), (handler, _) => TintUnderline(handler.PlatformView));

        // Runs once per handler. The IsChecked mapping below runs on every change, and setting the state description makes TalkBack announce it.
        Microsoft.Maui.Handlers.RadioButtonHandler.Mapper.AppendToMapping("CheckableAccessibility", (handler, radio) =>
        {
            if (handler.PlatformView is not Android.Widget.CompoundButton)
                AndroidX.Core.View.ViewCompat.SetAccessibilityDelegate(handler.PlatformView, new CheckableRadioAccessibilityDelegate(AndroidX.Core.View.ViewCompat.GetAccessibilityDelegate(handler.PlatformView), radio));
        });
        Microsoft.Maui.Handlers.RadioButtonHandler.Mapper.AppendToMapping(nameof(IRadioButton.IsChecked), (handler, radio) =>
        {
            if (handler.PlatformView is not Android.Widget.CompoundButton)
                AndroidX.Core.View.ViewCompat.SetStateDescription(handler.PlatformView, radio.IsChecked ? "Wybrany" : "Niewybrany");
        });
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
