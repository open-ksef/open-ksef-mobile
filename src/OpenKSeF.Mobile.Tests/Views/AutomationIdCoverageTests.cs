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
            "LoginPageButtonPair",
            "LoginPageEntryServerUrl",
            "LoginPageButtonSaveServer",
            "LoginPageButtonScanQr",
            "LoginPageActivityIndicatorBusy",
            "LoginPageLabelError",
            "LoginPageLabelServerHost",
            "LoginPageButtonChangeServer");

        AssertFileContainsAll(
            "PairingPage.xaml",
            "PairingPageLabelCode",
            "PairingPageLabelCountdown",
            "PairingPageLabelError",
            "PairingPageButtonNewCode",
            "PairingPageButtonCancel");

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
            "QrCodePageLabelError",
            "QrCodePageLabelMessage",
            "QrCodePageLabelTransferText",
            "QrCodePageButtonCopy");

        AssertFileContainsAll(
            "SellerProfilePage.xaml",
            "SellerProfilePageAlertUnsupported",
            "SellerProfilePageAlertIncomplete",
            "SellerProfilePageButtonLookup",
            "SellerProfilePageLabelLookupStatus",
            "SellerProfilePageEntryLegalName",
            "SellerProfilePageEntryBankAccount",
            "SellerProfilePageButtonSave");

        AssertFileContainsAll(
            "IssuedInvoiceListPage.xaml",
            "IssuedInvoiceListPageCollectionViewInvoices",
            "IssuedInvoiceListPageFrameInvoiceItem",
            "IssuedInvoiceListPageButtonNew",
            "IssuedInvoiceListPageUnsupportedState",
            "IssuedInvoiceListPageEmptyState",
            "IssuedInvoiceListPageLabelError");

        AssertFileContainsAll(
            "InvoiceDraftPage.xaml",
            "InvoiceDraftPageSegmentCompany",
            "InvoiceDraftPageSegmentPerson",
            "InvoiceDraftPageEntryBuyerNip",
            "InvoiceDraftPageButtonLookup",
            "InvoiceDraftPageButtonAddLine",
            "InvoiceDraftPageAlertSplitPayment",
            "InvoiceDraftPageButtonIssue",
            "InvoiceDraftPageLabelError");

        AssertFileContainsAll(
            "IssuedInvoiceDetailsPage.xaml",
            "IssuedInvoiceDetailsPageStatusCard",
            "IssuedInvoiceDetailsPageButtonIssue",
            "IssuedInvoiceDetailsPageButtonFix",
            "IssuedInvoiceDetailsPageLabelKsefNumber",
            "IssuedInvoiceDetailsPageLabelRejection",
            "IssuedInvoiceDetailsPageToolbarCopy");

        AssertFileContainsAll(
            "AccountPage.xaml",
            "AccountPageRowDeleteAccount");
    }

    [Theory]
    [InlineData("InvoiceListPageButtonRefreshEmptyState")]
    [InlineData("InvoiceListPageButtonGoToTenants")]
    public void InvoiceList_StateButtons_DrawAboveRefreshView(string automationId)
    {
        var content = File.ReadAllText(GetViewPath("InvoiceListPage.xaml"));

        Assert.True(
            content.IndexOf($"AutomationId=\"{automationId}\"", StringComparison.Ordinal) > content.IndexOf("</RefreshView>", StringComparison.Ordinal),
            $"{automationId} sits under the RefreshView, which takes its taps.");
    }

    [Fact]
    public void LoginPage_HasNoPasswordForm_AndBrowserButtonsAlwaysShow()
    {
        var page = System.Xml.Linq.XDocument.Load(GetViewPath("LoginPage.xaml"));
        var elements = page.Descendants().ToList();
        string? Attr(System.Xml.Linq.XElement e, string name) => e.Attribute(name)?.Value;

        var entries = elements.Where(e => e.Name.LocalName == "Entry").ToList();
        Assert.DoesNotContain(entries, e => Attr(e, "IsPassword") is not null);
        Assert.Equal(["LoginPageEntryServerUrl"], entries.Select(e => Attr(e, "AutomationId")));

        foreach (var id in new[] { "LoginPageButtonLogin", "LoginPageButtonPair", "LoginPageButtonGoogle" })
        {
            var button = Assert.Single(elements, e => Attr(e, "AutomationId") == id);
            foreach (var node in button.AncestorsAndSelf().TakeWhile(e => e.Name.LocalName != "ScrollView"))
                Assert.Null(Attr(node, "IsVisible"));
        }

        var authMethods = typeof(OpenKSeF.Mobile.Services.IAuthService).GetMethods().Select(m => m.Name).ToList();
        Assert.DoesNotContain(authMethods, n => n.Contains("Credentials") || n == "RegisterAsync");

        var vmMembers = typeof(OpenKSeF.Mobile.ViewModels.LoginViewModel).GetMembers().Select(m => m.Name).ToList();
        Assert.DoesNotContain(vmMembers, n => n.Contains("BrowserLogin") || n.Contains("Password") || n == "IsLoginMode");
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
