using System.Net;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class IssuingViewModelTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("304f3ff7-06b6-43e8-bdf4-a0ad77f194e3");
    private static readonly Guid InvoiceId = Guid.Parse("0b7c9a62-3f6e-4d1a-9c55-1f2e3d4c5b6a");

    private readonly IApiService _api = Substitute.For<IApiService>();

    public IssuingViewModelTests()
    {
        Shell.Current.Reset();
        Preferences.Default.Set("SelectedTenantId", TenantId.ToString());
    }

    public void Dispose() => Preferences.Default.Clear();

    private static SellerProfileDto Profile(string environment = "test", bool complete = true) => new()
    {
        Nip = "1111111111", LegalName = "Firma", AddressLine1 = "ul. Prosta 1", BankAccount = "61109010140000071219812874",
        IsComplete = complete, NextNumberPreview = "FV/12/11/2026", KsefEnvironment = environment
    };

    private static IssuedInvoiceDto Invoice(string status = "Draft", decimal gross = 123m) => new()
    {
        Id = InvoiceId, Status = status, Number = status == "Draft" ? null : "FV/12/11/2026",
        Buyer = new PartyDto { Kind = "Company", Nip = "5261040828", Name = "Nabywca sp. z o.o.", AddressLine1 = "ul. Długa 2" },
        IssueDate = new DateOnly(2026, 11, 3), SaleDate = new DateOnly(2026, 11, 3), DueDate = new DateOnly(2026, 11, 17),
        Lines = [new() { LineNo = 1, Name = "Usługa", Unit = "szt.", Quantity = 1, UnitNetPrice = 100m, VatRate = "23", NetAmount = 100m, VatAmount = 23m }],
        TotalNet = 100m, TotalVat = 23m, TotalGross = gross,
        Ksef = new KsefSubmissionDto { Environment = "test" }
    };

    private InvoiceDraftViewModel Draft(SellerProfileDto profile)
    {
        _api.GetSellerProfileAsync(TenantId).Returns(profile);
        _api.CreateIssuedInvoiceAsync(TenantId, Arg.Any<IssuedInvoiceDraftRequest>()).Returns(Invoice());
        _api.UpdateIssuedInvoiceAsync(TenantId, InvoiceId, Arg.Any<IssuedInvoiceDraftRequest>()).Returns(Invoice());
        _api.IssueInvoiceAsync(TenantId, InvoiceId).Returns(Invoice("Sending"));
        var vm = new InvoiceDraftViewModel(_api) { TenantId = TenantId.ToString() };
        vm.BuyerNip = "5261040828";
        vm.BuyerName = "Nabywca sp. z o.o.";
        vm.BuyerAddressLine1 = "ul. Długa 2";
        vm.Lines[0].Name = "Usługa";
        vm.Lines[0].UnitNetPrice = "100,00";
        return vm;
    }

    [Fact]
    public async Task List_ServerWithoutIssuing_HidesIssuing()
    {
        _api.GetSellerProfileAsync(TenantId).Returns((SellerProfileDto?)null);
        var vm = new IssuedInvoiceListViewModel(_api);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsUnsupported);
        Assert.False(vm.CanIssue);
        await _api.DidNotReceiveWithAnyArgs().GetIssuedInvoicesAsync(default);
    }

    [Fact]
    public async Task List_LoadsRowsInPolishFormat_WithStatusFilter()
    {
        _api.GetSellerProfileAsync(TenantId).Returns(Profile());
        _api.GetIssuedInvoicesAsync(TenantId, 1, 20, "Accepted", null, null).Returns(new PagedResult<IssuedInvoiceListItemDto>
        {
            Items = [new() { Id = InvoiceId, Status = "Accepted", Number = "FV/1/11/2026", BuyerName = "Nabywca", IssueDate = new DateOnly(2026, 11, 3), TotalGross = 15162m }],
            Page = 1, PageSize = 20, TotalCount = 1
        });
        var vm = new IssuedInvoiceListViewModel(_api);

        vm.SelectedStatus = "Accepted";
        await vm.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Invoices);
        Assert.Equal("15 162,00 PLN", row.Gross.Replace(' ', ' '));
        Assert.Equal("FV/1/11/2026 · 03.11.2026", row.Subtitle);
        Assert.Equal("test", vm.Environment);
    }

    [Fact]
    public async Task List_NewInvoice_IncompleteProfile_RedirectsToSellerProfile()
    {
        _api.GetSellerProfileAsync(TenantId).Returns(Profile(complete: false));
        var vm = new IssuedInvoiceListViewModel(_api);

        await vm.NewInvoiceCommand.ExecuteAsync(null);

        Assert.Equal("Uzupełnij dane firmy, aby wystawiać faktury.", Shell.Current.LastAlertMessage);
        Assert.Equal($"sellerProfile?tenantId={TenantId}", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task List_NewInvoice_CompleteProfile_OpensDraft()
    {
        _api.GetSellerProfileAsync(TenantId).Returns(Profile());
        var vm = new IssuedInvoiceListViewModel(_api);

        await vm.NewInvoiceCommand.ExecuteAsync(null);

        Assert.Equal($"invoiceDraft?tenantId={TenantId}", Shell.Current.LastRoute);
    }

    [Fact]
    public void Draft_PersonBuyer_SendsNoNip()
    {
        var vm = Draft(Profile());

        vm.BuyerKind = "Person";

        Assert.False(vm.IsCompany);
        Assert.Null(vm.BuildRequest().Buyer.Nip);
        Assert.Equal("Person", vm.BuildRequest().Buyer.Kind);
    }

    [Fact]
    public async Task Draft_PersonWithoutNameAndAddress_BlocksIssue()
    {
        var vm = Draft(Profile());
        vm.BuyerKind = "Person";
        vm.BuyerName = " ";
        vm.BuyerAddressLine1 = null;

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Equal("Uzupełnij nazwę nabywcy", vm.BuyerNameError);
        Assert.Equal("Uzupełnij adres nabywcy", vm.BuyerAddressError);
        await _api.DidNotReceiveWithAnyArgs().CreateIssuedInvoiceAsync(default, default!);
    }

    [Theory]
    [InlineData("12195,13", false, true)]
    [InlineData("12195,12", false, false)]
    [InlineData("20000", true, false)]
    public void Draft_SplitPaymentWarning_AboveFifteenThousandGross(string net, bool split, bool expected)
    {
        var vm = Draft(Profile());

        vm.Lines[0].UnitNetPrice = net;
        vm.SplitPayment = split;

        Assert.Equal(expected, vm.ShowSplitPaymentWarning);
    }

    [Fact]
    public void Draft_ExemptLine_SendsExemptionBasis()
    {
        var vm = Draft(Profile());
        Assert.Null(vm.BuildRequest().ExemptionBasis);

        vm.Lines[0].VatRateLabel = "zw";

        Assert.True(vm.HasExemptLine);
        var basis = vm.BuildRequest().ExemptionBasis!;
        Assert.Equal(("Act", "art. 113 ust. 1 ustawy o VAT"), (basis.Kind, basis.Text));
        Assert.Equal("zw", vm.BuildRequest().Lines[0].VatRate);
    }

    [Fact]
    public void Draft_TotalsAndSummary_FollowLines()
    {
        var vm = Draft(Profile());
        vm.AddLineCommand.Execute(null);
        vm.Lines[1].Name = "Towar";
        vm.Lines[1].Quantity = "2";
        vm.Lines[1].UnitNetPrice = "50";
        vm.Lines[1].VatRateLabel = "8%";

        Assert.Equal("231,00 PLN", vm.TotalGrossText);
        Assert.Equal(["23%", "8%"], vm.VatSummary.Select(s => s.Rate));
        Assert.Equal("108,00 PLN", vm.Lines[1].Gross);
    }

    [Fact]
    public void Draft_DueDayChips_SetDueDateFromIssueDate()
    {
        var vm = Draft(Profile());
        vm.IssueDate = new DateTime(2026, 11, 3);

        vm.DueDays = "7";

        Assert.Equal(new DateTime(2026, 11, 10), vm.DueDate);
        Assert.Equal(new DateTime(2026, 11, 3), vm.SaleDate);
    }

    [Fact]
    public async Task Draft_Production_DeclinedConfirm_DoesNotIssue()
    {
        var vm = Draft(Profile("production"));
        await vm.LoadCommand.ExecuteAsync(null);
        Shell.Current.AlertAnswer = false;

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Contains("Numer: FV/12/11/2026", Shell.Current.LastAlertMessage);
        await _api.Received(1).CreateIssuedInvoiceAsync(TenantId, Arg.Any<IssuedInvoiceDraftRequest>());
        await _api.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default, default);
    }

    [Fact]
    public async Task Draft_Production_ConfirmedIssue_OpensDetails()
    {
        var vm = Draft(Profile("production"));
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.IssueCommand.ExecuteAsync(null);

        await _api.Received(1).IssueInvoiceAsync(TenantId, InvoiceId);
        Assert.Equal($"../issuedInvoiceDetails?tenantId={TenantId}&invoiceId={InvoiceId}", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Draft_TestEnvironment_IssuesWithoutConfirm()
    {
        var vm = Draft(Profile("test"));
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Null(Shell.Current.LastAlertMessage);
        await _api.Received(1).IssueInvoiceAsync(TenantId, InvoiceId);
    }

    [Fact]
    public async Task Draft_IssueValidation_ShownPerField()
    {
        var vm = Draft(Profile());
        await vm.LoadCommand.ExecuteAsync(null);
        _api.IssueInvoiceAsync(TenantId, InvoiceId).ThrowsAsync(new ApiException(HttpStatusCode.UnprocessableEntity, "422", new ValidationEnvelope
        {
            Errors =
            [
                new() { Code = "INV-VAL-013", Field = "buyer.nip", Message = "Dla nabywcy firmy wymagany jest poprawny NIP" },
                new() { Code = "INV-VAL-051", Field = "lines[0].quantity", Message = "Ilość na pozycji musi być większa od zera" },
                new() { Code = "INV-VAL-061", Field = "exemptionBasis", Message = "Podaj podstawę zwolnienia z VAT" },
                new() { Code = "INV-VAL-012", Field = "seller.bankAccount", Message = "Uzupełnij numer rachunku w danych firmy" }
            ]
        }));

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Equal("Dla nabywcy firmy wymagany jest poprawny NIP", vm.BuyerNipError);
        Assert.Equal("Ilość na pozycji musi być większa od zera", vm.Lines[0].Error);
        Assert.Equal("Podaj podstawę zwolnienia z VAT", vm.ExemptionError);
        Assert.Equal("Popraw błędy zaznaczone w formularzu.\nUzupełnij numer rachunku w danych firmy", vm.ErrorMessage);
        Assert.Null(Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Draft_EditMode_FillsFormFromInvoice()
    {
        _api.GetSellerProfileAsync(TenantId).Returns(Profile());
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(Invoice());
        var vm = new InvoiceDraftViewModel(_api) { TenantId = TenantId.ToString(), InvoiceId = InvoiceId.ToString() };

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Edycja szkicu", vm.Title);
        Assert.Equal("Nabywca sp. z o.o.", vm.BuyerName);
        Assert.Equal("14", vm.DueDays);
        var line = Assert.Single(vm.Lines);
        Assert.Equal(("1", "100,00", "23%"), (line.Quantity, line.UnitNetPrice, line.VatRateLabel));
        Assert.Equal("123,00 PLN", vm.TotalGrossText);
    }

    [Fact]
    public void Draft_InvalidLineNumber_BlocksSave()
    {
        var vm = Draft(Profile());
        vm.Lines[0].Quantity = "abc";

        vm.SaveDraftCommand.Execute(null);

        Assert.Equal("Podaj ilość, np. 1 lub 0,5", vm.Lines[0].Error);
        _api.DidNotReceiveWithAnyArgs().CreateIssuedInvoiceAsync(default, default!);
    }

    private IssuedInvoiceDetailsViewModel Details(List<TimeSpan> delays) => new(_api)
    {
        TenantId = TenantId.ToString(),
        InvoiceId = InvoiceId.ToString(),
        Delay = (step, _) => { delays.Add(step); return Task.CompletedTask; }
    };

    [Fact]
    public async Task Details_Sending_PollsUntilAccepted()
    {
        var accepted = Invoice("Accepted");
        accepted.Ksef = new KsefSubmissionDto
        {
            Environment = "test", KsefNumber = "1234567890-20261103-0100A0B0C0D0-E1", AcceptedAt = new DateTime(2026, 11, 3, 9, 15, 0, DateTimeKind.Utc),
            VerificationUrl = "https://qr-test.ksef.mf.gov.pl/invoice/1234567890/03-11-2026/abc"
        };
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(Invoice("Sending"), Invoice("Sending"), accepted);
        var delays = new List<TimeSpan>();
        var vm = Details(delays);

        await vm.RunAsync(CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)], delays);
        Assert.True(vm.IsAccepted);
        Assert.Equal("1234567890-20261103-0100A0B0C0D0-E1", vm.KsefNumber);
        Assert.NotNull(vm.VerificationQr);
    }

    [Fact]
    public async Task Details_LongSending_SlowsDownAfterTwoMinutes()
    {
        var responses = Enumerable.Repeat(Invoice("Sending"), 42).Append(Invoice("Rejected")).ToArray();
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(responses[0], responses[1..]);
        var delays = new List<TimeSpan>();
        var vm = Details(delays);

        await vm.RunAsync(CancellationToken.None);

        Assert.Equal(40, delays.Count(d => d == TimeSpan.FromSeconds(3)));
        Assert.Equal(2, delays.Count(d => d == TimeSpan.FromSeconds(15)));
        Assert.True(vm.IsRejected);
    }

    [Fact]
    public async Task Details_Rejected_ShowsKsefMessage()
    {
        var rejected = Invoice("Rejected");
        rejected.Ksef = new KsefSubmissionDto { Environment = "test", StatusCode = 450, StatusMessage = "Błąd weryfikacji semantyki dokumentu" };
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(rejected);
        var vm = Details([]);

        await vm.RunAsync(CancellationToken.None);

        Assert.StartsWith("Kod KSeF 450: Błąd weryfikacji semantyki dokumentu", vm.RejectionText);
        await vm.EditCommand.ExecuteAsync(null);
        Assert.Equal($"invoiceDraft?tenantId={TenantId}&invoiceId={InvoiceId}", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Details_DraftInProduction_ConfirmsBeforeIssue()
    {
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(Invoice());
        _api.GetSellerProfileAsync(TenantId).Returns(Profile("production"));
        var vm = Details([]);
        await vm.RunAsync(CancellationToken.None);
        Shell.Current.AlertAnswer = false;

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Contains("Do zapłaty: 123,00 PLN", Shell.Current.LastAlertMessage);
        await _api.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default, default);
    }

    [Fact]
    public async Task Details_CopyAsNew_OpensNewDraft()
    {
        var copyId = Guid.NewGuid();
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(Invoice("Accepted"));
        _api.CopyIssuedInvoiceAsync(TenantId, InvoiceId).Returns(new IssuedInvoiceDto { Id = copyId });
        var vm = Details([]);
        await vm.RunAsync(CancellationToken.None);

        await vm.CopyAsNewCommand.ExecuteAsync(null);

        Assert.Equal($"../invoiceDraft?tenantId={TenantId}&invoiceId={copyId}", Shell.Current.LastRoute);
    }
}
