using System.Security.Cryptography;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class IssuedInvoiceShareTests
{
    private const string KsefNumber = "1111111111-20261103-0100A0B0C0D0-E1";
    private const string VerificationUrl = "https://qr-test.ksef.mf.gov.pl/invoice/1111111111/03-11-2026/abc";
    private static readonly Guid TenantId = Guid.Parse("304f3ff7-06b6-43e8-bdf4-a0ad77f194e3");
    private static readonly Guid InvoiceId = Guid.Parse("0b7c9a62-3f6e-4d1a-9c55-1f2e3d4c5b6a");
    private static readonly byte[] Xml = [1, 2, 3];
    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46];

    private readonly IApiService _api = Substitute.For<IApiService>();

    public IssuedInvoiceShareTests()
    {
        Shell.Current.Reset();
        Share.Default.Requests.Clear();
        Browser.Default.LastUri = null;
        _api.GetIssuedInvoiceXmlAsync(TenantId, InvoiceId).Returns(Xml);
        _api.GetIssuedInvoiceUpoAsync(TenantId, InvoiceId).Returns(Xml);
    }

    private async Task<IssuedInvoiceDetailsViewModel> Loaded(string status = "Accepted", bool hasUpo = true, string? url = VerificationUrl)
    {
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(new IssuedInvoiceDto
        {
            Id = InvoiceId,
            Status = status,
            Number = "FV/12/11/2026",
            Ksef = new KsefSubmissionDto { Environment = "test", KsefNumber = KsefNumber, VerificationUrl = url, HasUpo = hasUpo }
        });
        var vm = new IssuedInvoiceDetailsViewModel(_api, Substitute.For<IServerSettingsService>()) { TenantId = TenantId.ToString(), InvoiceId = InvoiceId.ToString() };
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task SharePdf_RendersStoredXmlWithKsefNumberAndKodI()
    {
        var vm = await Loaded();
        (byte[] Xml, string Number, string Url)? call = null;
        vm.RenderPdf = (xml, number, url) => { call = (xml, number, url); return Task.FromResult(Pdf); };

        await vm.SharePdfCommand.ExecuteAsync(null);

        Assert.Equal((Xml, KsefNumber, VerificationUrl), call);
        var file = Assert.Single(Share.Default.Requests).File!;
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("FV_12_11_2026.pdf", Path.GetFileName(file.FullPath));
        Assert.Equal(FileSystem.CacheDirectory, Path.GetDirectoryName(file.FullPath));
        Assert.Equal(Pdf, File.ReadAllBytes(file.FullPath));
        Assert.False(vm.IsPdfFallback);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task SharePdf_RendererThrows_ShowsFallbackWithoutSharing()
    {
        var vm = await Loaded();
        vm.RenderPdf = (_, _, _) => throw new InvalidOperationException("WebView missing");

        await vm.SharePdfCommand.ExecuteAsync(null);

        Assert.True(vm.IsPdfFallback);
        Assert.Null(vm.ErrorMessage);
        Assert.Empty(Share.Default.Requests);
    }

    [Fact]
    public async Task SharePdf_RendererHangs_FallsBackAfterTimeout()
    {
        var vm = await Loaded();
        vm.PdfTimeout = TimeSpan.FromMilliseconds(10);
        vm.RenderPdf = (_, _, _) => new TaskCompletionSource<byte[]>().Task;

        await vm.SharePdfCommand.ExecuteAsync(null);

        Assert.True(vm.IsPdfFallback);
        Assert.Empty(Share.Default.Requests);
    }

    [Fact]
    public async Task SharePdf_NoRendererHost_ShowsFallback()
    {
        var vm = await Loaded();

        await vm.SharePdfCommand.ExecuteAsync(null);

        Assert.True(vm.IsPdfFallback);
    }

    [Fact]
    public async Task SharePdf_NoKodI_NeverRendersPdfWithoutIt()
    {
        var vm = await Loaded(url: null);
        var rendered = false;
        vm.RenderPdf = (_, _, _) => { rendered = true; return Task.FromResult(Pdf); };

        await vm.SharePdfCommand.ExecuteAsync(null);

        Assert.False(rendered);
        Assert.True(vm.IsPdfFallback);
        Assert.False(vm.HasVerificationLink);
    }

    [Fact]
    public async Task SharePdf_XmlDownloadFails_ShowsErrorNotFallback()
    {
        var vm = await Loaded();
        vm.RenderPdf = (_, _, _) => Task.FromResult(Pdf);
        _api.GetIssuedInvoiceXmlAsync(TenantId, InvoiceId).ThrowsAsync(new HttpRequestException("offline"));

        await vm.SharePdfCommand.ExecuteAsync(null);

        Assert.False(vm.IsPdfFallback);
        Assert.Contains("offline", vm.ErrorMessage);
        Assert.Empty(Share.Default.Requests);
    }

    [Fact]
    public async Task ShareXml_SharesStoredBytes()
    {
        var vm = await Loaded();

        await vm.ShareXmlCommand.ExecuteAsync(null);

        var file = Assert.Single(Share.Default.Requests).File!;
        Assert.Equal("application/xml", file.ContentType);
        Assert.Equal("FV_12_11_2026.xml", Path.GetFileName(file.FullPath));
        Assert.Equal(Xml, File.ReadAllBytes(file.FullPath));
    }

    [Fact]
    public async Task ShareUpo_SharesUnderOwnName()
    {
        var vm = await Loaded();

        await vm.ShareUpoCommand.ExecuteAsync(null);

        Assert.True(vm.HasUpo);
        Assert.Equal("FV_12_11_2026_UPO.xml", Path.GetFileName(Assert.Single(Share.Default.Requests).File!.FullPath));
    }

    [Fact]
    public async Task ShareUpo_WithoutUpo_NotOffered()
    {
        var vm = await Loaded(hasUpo: false);

        await vm.ShareUpoCommand.ExecuteAsync(null);

        Assert.False(vm.HasUpo);
        await _api.DidNotReceiveWithAnyArgs().GetIssuedInvoiceUpoAsync(default, default);
        Assert.Empty(Share.Default.Requests);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Sending")]
    [InlineData("Rejected")]
    public async Task NotAccepted_NothingOffered(string status)
    {
        var vm = await Loaded(status);
        vm.RenderPdf = (_, _, _) => Task.FromResult(Pdf);

        await vm.SharePdfCommand.ExecuteAsync(null);
        await vm.ShareXmlCommand.ExecuteAsync(null);
        await vm.ShareUpoCommand.ExecuteAsync(null);

        Assert.False(vm.IsAccepted);
        Assert.False(vm.HasUpo);
        await _api.DidNotReceiveWithAnyArgs().GetIssuedInvoiceXmlAsync(default, default);
        await _api.DidNotReceiveWithAnyArgs().GetIssuedInvoiceUpoAsync(default, default);
        Assert.Empty(Share.Default.Requests);
    }

    [Fact]
    public async Task OpenVerification_OpensKodIUrl()
    {
        var vm = await Loaded();

        await vm.OpenVerificationCommand.ExecuteAsync(null);

        Assert.Equal(VerificationUrl, Browser.Default.LastUri);
    }

    [Fact]
    public async Task OpenVerification_NonHttpsUrl_Refused()
    {
        var vm = await Loaded(url: "javascript:alert(1)");

        await vm.OpenVerificationCommand.ExecuteAsync(null);

        Assert.False(vm.HasVerificationLink);
        Assert.Null(Browser.Default.LastUri);
    }

    [Theory]
    [InlineData("FV/12/11/2026", "FV_12_11_2026")]
    [InlineData("../../etc", ".._.._etc")]
    [InlineData(@"A B\C", "A_B_C")]
    public void FileStem_LeavesNoPathSeparators(string number, string expected) =>
        Assert.Equal(expected, IssuingFormat.FileStem(number));

    [Fact]
    public void Bundle_MatchesPinnedBuildAndShipsLicense()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "OpenKSeF.Mobile", "Resources", "Raw", "pdf")))
            dir = dir.Parent;
        var pdf = Path.Combine(dir!.FullName, "OpenKSeF.Mobile", "Resources", "Raw", "pdf");

        Assert.StartsWith("MIT License", File.ReadAllText(Path.Combine(pdf, "LICENSE")));
        Assert.Equal(
            "551b4ba283db648620c2d03cac9c2f2dd50375b47173c79d6e264c88dad152e4",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(pdf, "ksef-fe-invoice-converter.js")))).ToLowerInvariant());

        var notices = File.ReadAllText(Path.Combine(pdf, "THIRD-PARTY-NOTICES.txt"));
        Assert.All(new[] { "ksef-pdf-generator 1.1.46", "pdfmake 0.3.11", "i18next", "xml-js", "core-js", "js-md5", "buffer" }, name => Assert.Contains(name, notices));
        Assert.Contains("License: SIL Open Font License 1.1", notices);

        var project = File.ReadAllText(Path.Combine(dir.FullName, "OpenKSeF.Mobile", "OpenKSeF.Mobile.csproj"));
        var excluded = System.Text.RegularExpressions.Regex.Match(project, "<MauiAsset Include=\"Resources.Raw.[*][*]\" Exclude=\"([^\"]*)\"").Groups[1].Value;
        Assert.DoesNotContain("THIRD-PARTY", excluded);
        Assert.Contains("README.md", excluded);
    }
}
