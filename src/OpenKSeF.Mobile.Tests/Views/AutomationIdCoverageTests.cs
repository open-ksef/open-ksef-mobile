using Xunit;

namespace OpenKSeF.Mobile.Tests.Views;

public class AutomationIdCoverageTests
{
    [Fact]
    public void CriticalViews_ExposeExpectedAutomationIds()
    {
        AssertFileContainsAll(
            "LoginPage.xaml",
            "LoginPageLabelTitle",
            "LoginPageButtonLogin",
            "LoginPageButtonRegister",
            "LoginPageButtonGoogle",
            "LoginPageEntryEmail",
            "LoginPageEntryPassword",
            "LoginPageEntryServerUrl",
            "LoginPageButtonSaveServer",
            "LoginPageButtonScanQr",
            "LoginPageActivityIndicatorBusy",
            "LoginPageLabelError");

        AssertFileContainsAll(
            "TenantsPage.xaml",
            "TenantsPageButtonAdd",
            "TenantsPageButtonAddEmptyState",
            "TenantsPageCollectionViewTenants",
            "TenantsPageFrameTenantItem",
            "TenantsPageLabelError");

        AssertFileContainsAll(
            "TenantFormPage.xaml",
            "TenantFormPageEntryNip",
            "TenantFormPageEntryDisplayName",
            "TenantFormPageButtonCancel",
            "TenantFormPageButtonSave",
            "TenantFormPageLabelError");

        AssertFileContainsAll(
            "InvoiceListPage.xaml",
            "InvoiceListPageLabelTitle",
            "InvoiceListPageButtonRefreshEmptyState",
            "InvoiceListPageCollectionViewInvoices",
            "InvoiceListPageFrameInvoiceItem",
            "InvoiceListPageButtonRetry",
            "InvoiceListPageLabelError");

        AssertFileContainsAll(
            "InvoiceDetailsPage.xaml",
            "InvoiceDetailsPageLabelAmount",
            "InvoiceDetailsPageLabelVendorName",
            "InvoiceDetailsPageLabelKsefNumber",
            "InvoiceDetailsPageButtonCopyTransfer",
            "InvoiceDetailsPageButtonShowQr",
            "InvoiceDetailsPageButtonRetry");

        AssertFileContainsAll(
            "QrCodePage.xaml",
            "QrCodePageLabelTitle",
            "QrCodePageImageQr",
            "QrCodePageButtonShare",
            "QrCodePageButtonRetry",
            "QrCodePageLabelError");
    }

    private static void AssertFileContainsAll(string fileName, params string[] automationIds)
    {
        var filePath = GetViewPath(fileName);
        var content = File.ReadAllText(filePath);

        foreach (var automationId in automationIds)
        {
            Assert.Contains($"AutomationId=\"{automationId}\"", content);
        }
    }

    private static string GetViewPath(string fileName)
    {
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../OpenKSeF.Mobile/Views",
                fileName));
    }
}
