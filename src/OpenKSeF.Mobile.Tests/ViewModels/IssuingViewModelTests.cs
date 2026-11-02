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
        var saved = Invoice();
        saved.Ksef.Environment = profile.KsefEnvironment!;
        saved.IssueDate = DateOnly.FromDateTime(DateTime.Today);
        _api.CreateIssuedInvoiceAsync(TenantId, Arg.Any<IssuedInvoiceDraftRequest>()).Returns(saved);
        _api.UpdateIssuedInvoiceAsync(TenantId, InvoiceId, Arg.Any<IssuedInvoiceDraftRequest>()).Returns(saved);
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

        Assert.Contains("Numer (przewidywany): FV/12/11/2026", Shell.Current.LastAlertMessage);
        await _api.Received(1).CreateIssuedInvoiceAsync(TenantId, Arg.Any<IssuedInvoiceDraftRequest>());
        await _api.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default, default);
    }

    [Fact]
    public async Task Draft_ProfileGetFailed_SavedProductionInvoice_StillConfirms()
    {
        var vm = Draft(Profile("production"));
        _api.GetSellerProfileAsync(TenantId).ThrowsAsync(new HttpRequestException("no route"));
        await vm.LoadCommand.ExecuteAsync(null);
        Shell.Current.AlertAnswer = false;

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.StartsWith("Faktura trafi do KSeF", Shell.Current.LastAlertMessage);
        Assert.Contains("Numer: nadamy przy wysłaniu", Shell.Current.LastAlertMessage);
        await _api.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default, default);
    }

    [Fact]
    public async Task Draft_StaleTestProfile_SavedProductionInvoice_Confirms()
    {
        var vm = Draft(Profile("production"));
        _api.GetSellerProfileAsync(TenantId).Returns(Profile("test"));
        await vm.LoadCommand.ExecuteAsync(null);
        Shell.Current.AlertAnswer = false;

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.NotNull(Shell.Current.LastAlertMessage);
        await _api.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    [InlineData("staging")]
    public async Task Confirm_ShownUnlessTestOrDemo(string? environment)
    {
        var invoice = Invoice();
        invoice.Ksef.Environment = environment!;
        Shell.Current.AlertAnswer = false;

        Assert.False(await InvoiceDraftViewModel.ConfirmIssueAsync(null, invoice));

        Assert.NotNull(Shell.Current.LastAlertMessage);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("demo")]
    [InlineData("TEST")]
    public async Task Confirm_SkippedOnTestAndDemo(string environment)
    {
        var invoice = Invoice();
        invoice.Ksef.Environment = environment;
        Shell.Current.AlertAnswer = false;

        Assert.True(await InvoiceDraftViewModel.ConfirmIssueAsync(null, invoice));

        Assert.Null(Shell.Current.LastAlertMessage);
    }

    [Fact]
    public async Task Confirm_RejectedResend_ShowsItsOwnNumber()
    {
        var invoice = Invoice("Rejected");
        invoice.Number = "FV/3/10/2026";
        invoice.Ksef.Environment = "production";

        await InvoiceDraftViewModel.ConfirmIssueAsync("FV/12/11/2026", invoice);

        Assert.Contains("Numer: FV/3/10/2026", Shell.Current.LastAlertMessage);
        Assert.DoesNotContain("FV/12/11/2026", Shell.Current.LastAlertMessage);
    }

    [Fact]
    public async Task Confirm_DraftInAnotherPeriod_DoesNotNameThePreview()
    {
        var invoice = Invoice();
        invoice.IssueDate = DateOnly.FromDateTime(DateTime.Today).AddMonths(-1);
        invoice.Ksef.Environment = "production";

        await InvoiceDraftViewModel.ConfirmIssueAsync("FV/12/11/2026", invoice);

        Assert.DoesNotContain("FV/12/11/2026", Shell.Current.LastAlertMessage);
        Assert.Contains("zależy od daty wystawienia", Shell.Current.LastAlertMessage);
    }

    public static TheoryData<Exception> NetworkFailures => new()
    {
        new HttpRequestException("no route to host"),
        new TaskCanceledException("HttpClient.Timeout of 30 seconds elapsing"),
        new ApiException(HttpStatusCode.ServiceUnavailable, "Brak polaczenia z serwerem: no route to host"),
    };

    [Theory]
    [MemberData(nameof(NetworkFailures))]
    public async Task Draft_IssueNetworkFailure_OpensDetailsOfSavedInvoice_NoRawError(Exception failure)
    {
        var vm = Draft(Profile());
        _api.IssueInvoiceAsync(TenantId, InvoiceId).ThrowsAsync(failure);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Equal($"../issuedInvoiceDetails?tenantId={TenantId}&invoiceId={InvoiceId}", Shell.Current.LastRoute);
        Assert.Null(vm.ErrorMessage);
    }

    private const string FormNoConnection = "Brak połączenia z serwerem. Dane w formularzu zostały. Spróbuj ponownie po powrocie sieci, nie twórz nowej faktury.";

    [Theory]
    [MemberData(nameof(NetworkFailures))]
    public async Task Draft_IssueNetworkFailure_NavigationFails_ShowsNoConnectionText(Exception failure)
    {
        var vm = Draft(Profile());
        _api.IssueInvoiceAsync(TenantId, InvoiceId).ThrowsAsync(failure);
        await vm.LoadCommand.ExecuteAsync(null);
        Shell.Current.GoToFailure = new InvalidOperationException("no route");

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Equal(FormNoConnection, vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [MemberData(nameof(NetworkFailures))]
    public async Task Draft_IssueSaveNetworkFailure_NoRawText_FormKept_RetryWorks(Exception failure)
    {
        var vm = Draft(Profile());
        await vm.LoadCommand.ExecuteAsync(null);
        var saved = Invoice();
        _api.CreateIssuedInvoiceAsync(TenantId, Arg.Any<IssuedInvoiceDraftRequest>()).Returns(
            _ => Task.FromException<IssuedInvoiceDto>(failure),
            _ => Task.FromResult(saved));

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Equal(FormNoConnection, vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.InvoiceId);
        Assert.Equal("Nabywca sp. z o.o.", vm.BuyerName);
        Assert.Equal("100,00", Assert.Single(vm.Lines).UnitNetPrice);
        await _api.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default, default);

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        await _api.Received(1).IssueInvoiceAsync(TenantId, InvoiceId);
    }

    [Fact]
    public async Task Draft_SaveDraftNetworkFailure_ShowsNoConnectionText()
    {
        var vm = Draft(Profile());
        await vm.LoadCommand.ExecuteAsync(null);
        _api.CreateIssuedInvoiceAsync(TenantId, Arg.Any<IssuedInvoiceDraftRequest>()).ThrowsAsync(new HttpRequestException("no route to host"));

        await vm.SaveDraftCommand.ExecuteAsync(null);

        Assert.Equal(FormNoConnection, vm.ErrorMessage);
    }

    [Fact]
    public async Task Draft_SaveServerError_KeepsServerMessage()
    {
        var vm = Draft(Profile());
        await vm.LoadCommand.ExecuteAsync(null);
        _api.CreateIssuedInvoiceAsync(TenantId, Arg.Any<IssuedInvoiceDraftRequest>()).ThrowsAsync(new ApiException(HttpStatusCode.InternalServerError, "Blad serwera (500)"));

        await vm.SaveDraftCommand.ExecuteAsync(null);

        Assert.Equal("Blad serwera (500)", vm.ErrorMessage);
    }

    [Fact]
    public async Task Draft_NumberPreview_FollowsIssueDate()
    {
        var vm = Draft(Profile());
        await vm.LoadCommand.ExecuteAsync(null);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("Numer nadamy przy wystawieniu: FV/12/11/2026", vm.NumberPreview);

        vm.IssueDate = DateTime.Today.AddMonths(-1);

        Assert.Equal(string.Empty, vm.NumberPreview);
        Assert.Contains(nameof(vm.NumberPreview), changed);

        vm.IssueDate = DateTime.Today;

        Assert.Equal("Numer nadamy przy wystawieniu: FV/12/11/2026", vm.NumberPreview);
    }

    [Fact]
    public async Task Draft_NumberPreview_HiddenForInvoiceWithItsOwnNumber()
    {
        var vm = Draft(Profile());
        var rejected = Invoice("Rejected");
        rejected.IssueDate = DateOnly.FromDateTime(DateTime.Today);
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(rejected);
        vm.InvoiceId = InvoiceId.ToString();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.NumberPreview);
    }

    [Fact]
    public async Task Draft_NumberPreview_ShownAfterCopyOfNumberedInvoice()
    {
        var vm = Draft(Profile());
        var rejected = Invoice("Rejected");
        rejected.IssueDate = DateOnly.FromDateTime(DateTime.Today);
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(rejected);
        var copy = Invoice();
        copy.IssueDate = rejected.IssueDate;
        _api.CopyIssuedInvoiceAsync(TenantId, InvoiceId).Returns(copy);
        vm.InvoiceId = InvoiceId.ToString();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.CopyAsNewCommand.ExecuteAsync(null);

        Assert.Equal("Numer nadamy przy wystawieniu: FV/12/11/2026", vm.NumberPreview);
    }

    public static TheoryData<string, int?> NotIssuableFresh => new()
    {
        { "Sending", null },
        { "Accepted", null },
        { "Rejected", 440 },
    };

    [Theory]
    [MemberData(nameof(NotIssuableFresh))]
    public async Task Details_StaleDraft_FreshNotIssuable_NoDialogNoPost(string status, int? code)
    {
        var fresh = Invoice(status);
        fresh.Ksef.Environment = "production";
        fresh.Ksef.StatusCode = code;
        _api.GetSellerProfileAsync(TenantId).Returns(Profile("production"));
        var vm = Details([]);
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(Invoice(), fresh, Invoice("Accepted"));
        await vm.RunAsync(CancellationToken.None);
        Assert.True(vm.IsDraft);

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Null(Shell.Current.LastAlertMessage);
        await _api.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default, default);
        vm.Stop();
    }

    [Fact]
    public async Task Details_StaleDraft_FreshSending_PollsToResult()
    {
        _api.GetSellerProfileAsync(TenantId).Returns(Profile());
        var vm = Details([]);
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(Invoice(), Invoice("Sending"), Invoice("Sending"), Invoice("Accepted"));
        await vm.RunAsync(CancellationToken.None);

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.True(vm.IsAccepted);
    }

    [Fact]
    public async Task Details_FreshFixableRejected_ConfirmsAndIssues()
    {
        var fresh = Invoice("Rejected");
        fresh.Ksef.Environment = "production";
        fresh.Ksef.StatusCode = 450;
        _api.GetSellerProfileAsync(TenantId).Returns(Profile("production"));
        _api.IssueInvoiceAsync(TenantId, InvoiceId).Returns(Invoice("Accepted"));
        var vm = Details([]);
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(Invoice(), fresh, Invoice("Accepted"));
        await vm.RunAsync(CancellationToken.None);

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Contains("Numer: FV/12/11/2026", Shell.Current.LastAlertMessage);
        await _api.Received(1).IssueInvoiceAsync(TenantId, InvoiceId);
    }

    [Theory]
    [InlineData("""{"exception":{"code":21405}}""")]
    [InlineData("{ \"exception\": { \"code\": 21405 } }")]
    [InlineData("""[{"code":21405}]""")]
    [InlineData("HTTP 400: Bad Request, AdditionalInfo: brak")]
    [InlineData("Błąd serwera, traceId 00-abc")]
    public async Task Details_RawStatusMessage_NotShown_SentenceRemains(string raw)
    {
        var rejected = Invoice("Rejected");
        rejected.Ksef.StatusCode = 450;
        rejected.Ksef.StatusMessage = raw;

        var vm = await DetailsOf(rejected);

        Assert.StartsWith("Kod KSeF 450: KSeF odrzucił fakturę.\n", vm.RejectionText);
        Assert.DoesNotContain(raw, vm.RejectionText);
    }

    [Fact]
    public async Task Details_StatusMessageWithTraceSuffix_KeepsSentence()
    {
        var rejected = Invoice("Rejected");
        rejected.Ksef.StatusCode = 450;
        rejected.Ksef.StatusMessage = "Nieprawidłowy NIP nabywcy (traceId: 00-abc)";

        var vm = await DetailsOf(rejected);

        Assert.StartsWith("Kod KSeF 450: Nieprawidłowy NIP nabywcy\n", vm.RejectionText);
    }

    [Fact]
    public async Task Draft_IssueServerError_StaysOnForm()
    {
        var vm = Draft(Profile());
        _api.IssueInvoiceAsync(TenantId, InvoiceId).ThrowsAsync(new ApiException(HttpStatusCode.InternalServerError, "Blad serwera (500)"));
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Null(Shell.Current.LastRoute);
        Assert.Equal("Blad serwera (500)", vm.ErrorMessage);
    }

    [Theory]
    [MemberData(nameof(NetworkFailures))]
    public async Task Details_IssueNetworkFailure_StillDraft_ShowsDraftAndCanIssueAgain(Exception failure)
    {
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(Invoice());
        _api.GetSellerProfileAsync(TenantId).Returns(Profile());
        _api.IssueInvoiceAsync(TenantId, InvoiceId).ThrowsAsync(failure);
        var vm = Details([]);
        await vm.RunAsync(CancellationToken.None);

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.True(vm.IsDraft);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(Shell.Current.LastAlertMessage);

        _api.IssueInvoiceAsync(TenantId, InvoiceId).Returns(Invoice("Accepted"));
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(Invoice("Accepted"));
        await vm.IssueCommand.ExecuteAsync(null);
        Assert.True(vm.IsAccepted);
    }

    [Fact]
    public async Task Details_IssueNetworkFailure_ReallySending_ShowsSendingThenResult()
    {
        var states = new Queue<IssuedInvoiceDto>([Invoice(), Invoice(), Invoice("Sending"), Invoice("Accepted")]);
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(_ => states.Dequeue());
        _api.GetSellerProfileAsync(TenantId).Returns(Profile());
        _api.IssueInvoiceAsync(TenantId, InvoiceId).ThrowsAsync(new HttpRequestException("timeout"));
        var sawSending = false;
        var vm = Details([]);
        vm.PropertyChanged += (_, e) => sawSending |= e.PropertyName == nameof(vm.Invoice) && vm.IsSending;
        await vm.RunAsync(CancellationToken.None);

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.True(sawSending);
        Assert.True(vm.IsAccepted);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Details_NetworkStillDown_ShowsPlainMessage_AndRetriesUntilBack()
    {
        var attempts = 0;
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(_ => ++attempts < 3
            ? Task.FromException<IssuedInvoiceDto>(new HttpRequestException("no route: 10.0.2.2"))
            : Task.FromResult(Invoice(attempts == 3 ? "Sending" : "Accepted")));
        string? shown = null;
        var vm = Details([]);
        vm.PropertyChanged += (_, e) => shown = e.PropertyName == nameof(vm.ErrorMessage) && vm.ErrorMessage is { } m ? m : shown;

        await vm.RunAsync(CancellationToken.None);

        Assert.Equal(4, attempts);
        Assert.True(vm.IsAccepted);
        Assert.Contains("Brak połączenia", shown);
        Assert.DoesNotContain("10.0.2.2", shown);
        Assert.Null(vm.ErrorMessage);
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
        vm.Lines[0].Quantity = "1,5,5";

        vm.SaveDraftCommand.Execute(null);

        Assert.Equal("Podaj ilość, np. 1 lub 0,5", vm.Lines[0].Error);
        _api.DidNotReceiveWithAnyArgs().CreateIssuedInvoiceAsync(default, default!);
    }

    [Fact]
    public void Draft_PastedDisallowedCharacter_BlocksSave()
    {
        var vm = Draft(Profile());
        vm.Lines[0].Quantity = "1e5";

        vm.SaveDraftCommand.Execute(null);

        Assert.Equal("1e5", vm.Lines[0].Quantity);
        Assert.Contains("tylko cyfry", vm.Lines[0].Error);
        _api.DidNotReceiveWithAnyArgs().CreateIssuedInvoiceAsync(default, default!);
    }

    private IssuedInvoiceDetailsViewModel Details(List<TimeSpan> delays) => new(_api)
    {
        TenantId = TenantId.ToString(),
        InvoiceId = InvoiceId.ToString(),
        Delay = (step, _) => { delays.Add(step); return Task.CompletedTask; }
    };

    [Theory]
    [InlineData("2,5", "100,50")]
    [InlineData("2.5", "100.50")]
    [InlineData("2,5", "100.50")]
    public void DraftLine_BothSeparators_GiveTheSameAmounts(string quantity, string price)
    {
        var line = new DraftLineViewModel { Quantity = quantity, UnitNetPrice = price };

        Assert.True(line.Validate());
        Assert.Equal((2.5m, 100.50m), (line.ToRequest().Quantity, line.ToRequest().UnitNetPrice));
        Assert.Equal("309,04 PLN", line.Gross.Replace('\u00A0', ' '));
    }

    [Theory]
    [InlineData("1,000", "100")]
    [InlineData("1", "1.000")]
    public void DraftLine_ThousandsLookingInput_ShowsFieldErrorAndBlocksSave(string quantity, string price)
    {
        var line = new DraftLineViewModel { Quantity = quantity, UnitNetPrice = price };

        Assert.Contains("Nie wiadomo", line.Error);
        Assert.False(line.Validate());
        Assert.Contains("Nie wiadomo", line.Error);
    }

    [Fact]
    public void DraftLine_FixingAmbiguousInput_ClearsTheError()
    {
        var line = new DraftLineViewModel { UnitNetPrice = "1,000" };

        line.UnitNetPrice = "1000";

        Assert.Null(line.Error);
    }

    [Fact]
    public void DraftLine_LoadedThreeDecimalQuantity_SavesUnchanged()
    {
        var line = DraftLineViewModel.From(new IssuedInvoiceLineRequest { Name = "x", Quantity = 1.125m, UnitNetPrice = 10m, VatRate = "23" });

        Assert.True(line.Validate());
        Assert.Equal(1.125m, line.ToRequest().Quantity);
    }

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
        Assert.Equal($"../invoiceDraft?tenantId={TenantId}&invoiceId={InvoiceId}", Shell.Current.LastRoute);
    }

    [Theory]
    [InlineData("1e5")]
    [InlineData("5/2")]
    [InlineData("2x3")]
    [InlineData("2,5\n3")]
    [InlineData("\u22122,5")]
    [InlineData("\u0662")]
    public void Line_PastedDisallowedCharacter_KeepsRawTextAndShowsError(string pasted)
    {
        var quantity = new DraftLineViewModel { UnitNetPrice = "10" };
        quantity.Quantity = pasted;
        var price = new DraftLineViewModel();
        price.UnitNetPrice = pasted;

        Assert.Equal(pasted, quantity.Quantity);
        Assert.Equal(pasted, price.UnitNetPrice);
        Assert.Contains("tylko cyfry", quantity.Error);
        Assert.Contains("tylko cyfry", price.Error);
        Assert.False(quantity.Validate());
        Assert.False(price.Validate());
        Assert.Equal(0m, quantity.ToRequest().Quantity);
        Assert.Equal(0m, price.ToRequest().UnitNetPrice);
    }

    [Theory]
    [InlineData("1000,50", 1000.5)]
    [InlineData("1000.50", 1000.5)]
    [InlineData("1 000,50", 1000.5)]
    [InlineData("1\u00A0000,50", 1000.5)]
    [InlineData("1\u202F000.50", 1000.5)]
    [InlineData("2,5\n", 2.5)]
    public void Line_PastedPlainNumber_Works(string pasted, double expected)
    {
        var line = new DraftLineViewModel { UnitNetPrice = "10" };

        line.Quantity = pasted;

        Assert.Null(line.Error);
        Assert.True(line.Validate());
        Assert.Equal((decimal)expected, line.ToRequest().Quantity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData(",")]
    [InlineData("-2")]
    [InlineData("5,")]
    [InlineData("1 ")]
    public void Line_HalfTypedNumber_ShowsNoLiveError(string typed)
    {
        var line = new DraftLineViewModel();

        line.Quantity = typed;

        Assert.Null(line.Error);
    }

    [Fact]
    public async Task Details_RejectedWithStatusDetails_ShowsThem()
    {
        var rejected = Invoice("Rejected");
        rejected.Ksef = new KsefSubmissionDto { Environment = "test", StatusCode = 450, StatusMessage = "Błąd", StatusDetails = "Nieprawidłowy NIP nabywcy" };
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(rejected);
        var vm = Details([]);

        await vm.RunAsync(CancellationToken.None);

        Assert.StartsWith("Kod KSeF 450: Błąd\nNieprawidłowy NIP nabywcy\nNumer", vm.RejectionText);
    }

    [Fact]
    public async Task Details_IssueSchemaError_AlertsThenOpensForm()
    {
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(Invoice());
        _api.GetSellerProfileAsync(TenantId).Returns(Profile());
        _api.IssueInvoiceAsync(TenantId, InvoiceId).ThrowsAsync(new ApiException(HttpStatusCode.UnprocessableEntity, "422", new ValidationEnvelope
        {
            Errors = [new() { Code = "INV-XSD", Field = null, Message = "Faktura nie spełnia schematu FA(3)" }]
        }));
        var vm = Details([]);
        await vm.RunAsync(CancellationToken.None);

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Equal("Faktura nie spełnia schematu FA(3)", Shell.Current.LastAlertMessage);
        Assert.Equal($"../invoiceDraft?tenantId={TenantId}&invoiceId={InvoiceId}", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Details_DraftInProduction_ConfirmsBeforeIssue()
    {
        var draft = Invoice();
        draft.Ksef.Environment = "production";
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(draft);
        _api.GetSellerProfileAsync(TenantId).Returns((SellerProfileDto?)null);
        var vm = Details([]);
        await vm.RunAsync(CancellationToken.None);
        Shell.Current.AlertAnswer = false;

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.Contains("Do zapłaty: 123,00 PLN", Shell.Current.LastAlertMessage);
        await _api.DidNotReceiveWithAnyArgs().IssueInvoiceAsync(default, default);
    }

    private async Task<IssuedInvoiceDetailsViewModel> DetailsOf(IssuedInvoiceDto invoice)
    {
        _api.GetIssuedInvoiceAsync(TenantId, InvoiceId).Returns(invoice);
        var vm = new IssuedInvoiceDetailsViewModel(_api) { TenantId = TenantId.ToString(), InvoiceId = InvoiceId.ToString() };
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Details_Rejected440_OffersOnlyCopy_AndShowsKsefNumber()
    {
        var rejected = Invoice("Rejected");
        rejected.Ksef.StatusCode = 440;
        rejected.Ksef.StatusMessage = "Duplikat faktury";
        rejected.Ksef.DuplicateKsefNumber = "1234567890-20261102-AAAAAAAAAAAA-00";

        var vm = await DetailsOf(rejected);

        Assert.True(vm.IsDuplicateNumberRejection);
        Assert.False(vm.CanFixRejection);
        Assert.DoesNotContain("z tym samym numerem", vm.RejectionText);
        Assert.Contains("Kopiuj jako nową", vm.RejectionText);
        Assert.Contains("1234567890-20261102-AAAAAAAAAAAA-00", vm.RejectionText);
    }

    [Fact]
    public async Task Details_Real403_ShowsSentenceWithoutJsonOrTraceId()
    {
        var rejected = Invoice("Rejected");
        rejected.Ksef.StatusCode = 403;
        rejected.Ksef.StatusMessage = "Token KSeF nie ma uprawnienia do wystawiania faktur.";
        rejected.Ksef.StatusDetails = """missing-permissions: Brak wymaganych uprawnień do wykonania operacji w bieżącym kontekście. (security: {"requiredAnyOfPermissions":["InvoiceWrite","PefInvoiceWrite","EnforcementOperations"],"presentPermissions":["InvoiceRead"]}) (traceId: 00-2470e4b7abb5463217e63e4fa6d7b628-3b762c4cf0014420-00)""";

        var vm = await DetailsOf(rejected);

        Assert.Contains("\nBrak wymaganych uprawnień do wykonania operacji w bieżącym kontekście.\n", vm.RejectionText);
        Assert.DoesNotContain("traceId", vm.RejectionText);
        Assert.DoesNotContain("{", vm.RejectionText);
        Assert.DoesNotContain("missing-permissions", vm.RejectionText);
        Assert.Contains("z tym samym numerem", vm.RejectionText);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("Nieprawidłowy NIP nabywcy", "Nieprawidłowy NIP nabywcy")]
    [InlineData("Zły NIP (pole P_1); Zła data", "Zły NIP (pole P_1); Zła data")]
    [InlineData("""(traceId: 00-aa)""", null)]
    [InlineData("""{"a":1}""", null)]
    [InlineData("""Błąd {"a":1} dalej""", null)]
    [InlineData("""bad-code: Pierwszy (x: ["a"]); other-code: Drugi""", "Pierwszy; Drugi")]
    public void CleanKsefDetails(string? details, string? expected) =>
        Assert.Equal(expected, IssuingFormat.CleanKsefDetails(details));

    [Fact]
    public async Task Details_Real440_ShowsKsefNumberOnce()
    {
        const string number = "1234567890-20261102-AAAAAAAAAAAA-00";
        var rejected = Invoice("Rejected");
        rejected.Ksef.StatusCode = 440;
        rejected.Ksef.StatusMessage = "Ten numer faktury jest już użyty w KSeF przez inny dokument.";
        rejected.Ksef.StatusDetails = $"Duplikat faktury. Faktura o numerze KSeF: {number} została już prawidłowo przesłana do systemu w sesji: 20261102-SO-AAAAAAAAAA-AAAAAAAAAA-AA";
        rejected.Ksef.DuplicateKsefNumber = number;

        var vm = await DetailsOf(rejected);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(vm.RejectionText, number));
        Assert.DoesNotContain("20261102-SO", vm.RejectionText);
    }

    [Fact]
    public async Task Details_440OlderApiWithoutDuplicateField_ShowsNumberOnceFromDetails()
    {
        const string number = "1234567890-20261102-AAAAAAAAAAAA-00";
        var rejected = Invoice("Rejected");
        rejected.Ksef.StatusCode = 440;
        rejected.Ksef.StatusDetails = $"Duplikat faktury. Faktura o numerze KSeF: {number}";

        var vm = await DetailsOf(rejected);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(vm.RejectionText, number));
    }

    [Fact]
    public async Task Details_Rejected440WithoutKnownNumber_StillOffersCopy()
    {
        var rejected = Invoice("Rejected");
        rejected.Ksef.StatusCode = 440;

        var vm = await DetailsOf(rejected);

        Assert.True(vm.IsDuplicateNumberRejection);
        Assert.DoesNotContain("Numer KSeF istniejącego", vm.RejectionText);
    }

    [Theory]
    [InlineData(450)]
    [InlineData(430)]
    [InlineData(403)]
    public async Task Details_OtherRejectionCodes_KeepFix(int code)
    {
        var rejected = Invoice("Rejected");
        rejected.Ksef.StatusCode = code;

        var vm = await DetailsOf(rejected);

        Assert.True(vm.CanFixRejection);
        Assert.False(vm.IsDuplicateNumberRejection);
        Assert.Contains("z tym samym numerem", vm.RejectionText);
    }

    [Fact]
    public async Task Details_SendingWithLastError_ShowsRealErrorAndWarning()
    {
        var sending = Invoice("Sending");
        sending.Ksef.LastError = "Status KSeF 550 po drugim POST";

        var vm = await DetailsOf(sending);

        Assert.Equal("Status KSeF 550 po drugim POST\nNie wysyłaj faktury drugi raz.", vm.SendingProblemText);
    }

    [Fact]
    public async Task Details_SendingWithoutLastError_ShowsNoProblem() =>
        Assert.Null((await DetailsOf(Invoice("Sending"))).SendingProblemText);

    [Fact]
    public async Task Details_AcceptedWithMissingUpoLastError_IsNotASendingProblem()
    {
        var accepted = Invoice("Accepted");
        accepted.Ksef.LastError = "Brak UPO";

        Assert.Null((await DetailsOf(accepted)).SendingProblemText);
    }

    [Theory]
    [InlineData("Draft", true)]
    [InlineData("Accepted", true)]
    [InlineData("Rejected", true)]
    [InlineData("Sending", false)]
    [InlineData("Approved", false)]
    public async Task Details_CopyIsOfferedExceptWhileSending(string status, bool offered) =>
        Assert.Equal(offered, (await DetailsOf(Invoice(status))).CanCopyAsNew);

    [Fact]
    public async Task Details_CopyWhileSending_DoesNotCallApi()
    {
        var vm = await DetailsOf(Invoice("Sending"));

        await vm.CopyAsNewCommand.ExecuteAsync(null);

        await _api.DidNotReceiveWithAnyArgs().CopyIssuedInvoiceAsync(default, default);
    }

    private static ValidationEnvelope Invalid(string code, string field) =>
        new() { Errors = [new ValidationIssue { Code = code, Field = field, Message = code }] };

    [Theory]
    [InlineData("INV-VAL-020", false)]
    [InlineData("INV-VAL-022", true)]
    public void Draft_IssueDateErrors_ShowAtIssueDate(string code, bool copyOffered)
    {
        var vm = new InvoiceDraftViewModel(_api);

        vm.ApplyValidation(Invalid(code, "issueDate"));

        Assert.Equal(code, vm.IssueDateError);
        Assert.Null(vm.DueDateError);
        Assert.Equal(copyOffered, vm.ShowCopyAsNew);
    }

    [Fact]
    public void Draft_DueDateError_StaysAtDueDate()
    {
        var vm = new InvoiceDraftViewModel(_api);

        vm.ApplyValidation(Invalid("INV-VAL-0xx", "dueDate"));

        Assert.Equal("INV-VAL-0xx", vm.DueDateError);
        Assert.Null(vm.IssueDateError);
    }

    [Fact]
    public async Task Draft_CopyAfterInvVal022_SwitchesFormToNewDraft()
    {
        var copy = Invoice("Draft");
        copy.Id = Guid.NewGuid();
        _api.CopyIssuedInvoiceAsync(TenantId, InvoiceId).Returns(copy);
        var vm = new InvoiceDraftViewModel(_api) { TenantId = TenantId.ToString(), InvoiceId = InvoiceId.ToString() };
        vm.ApplyValidation(Invalid("INV-VAL-022", "issueDate"));

        await vm.CopyAsNewCommand.ExecuteAsync(null);

        Assert.Equal(copy.Id.ToString(), vm.InvoiceId);
        Assert.False(vm.ShowCopyAsNew);
        Assert.Null(vm.IssueDateError);
        Assert.Equal(copy.Buyer.Name, vm.BuyerName);
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
