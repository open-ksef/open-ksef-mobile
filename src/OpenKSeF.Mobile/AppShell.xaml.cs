#if ANDROID
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Platform;
#endif
using OpenKSeF.Mobile.Views;

namespace OpenKSeF.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("invoiceDetails", typeof(InvoiceDetailsPage));
        Routing.RegisterRoute("tenantForm", typeof(TenantFormPage));
        Routing.RegisterRoute("qrCode", typeof(QrCodePage));
        Routing.RegisterRoute("scanSetupQr", typeof(ScanSetupQrPage));
        Routing.RegisterRoute("sellerProfile", typeof(SellerProfilePage));
        Routing.RegisterRoute("invoiceDraft", typeof(InvoiceDraftPage));
        Routing.RegisterRoute("issuedInvoiceDetails", typeof(IssuedInvoiceDetailsPage));

#if ANDROID
        ApplyStatusBar();
        Loaded += (_, _) => ApplyStatusBar();
        Navigated += (_, _) => ApplyStatusBar();
        Application.Current!.RequestedThemeChanged += (_, _) => ApplyStatusBar();
#endif
    }

#if ANDROID
    private static void ApplyStatusBar()
    {
        var dark = Application.Current!.RequestedTheme == AppTheme.Dark;
        var onGradient = Shell.Current?.CurrentPage is null or { Background: LinearGradientBrush };
        StatusBar.SetColor((Color)Application.Current.Resources[onGradient ? "GradientStart" : dark ? "UiSurfaceDark" : "UiSurface"]);
        StatusBar.SetStyle(onGradient || dark ? StatusBarStyle.LightContent : StatusBarStyle.DarkContent);
    }
#endif
}
