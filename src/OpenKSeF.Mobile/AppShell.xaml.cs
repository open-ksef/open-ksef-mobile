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
    }
}
