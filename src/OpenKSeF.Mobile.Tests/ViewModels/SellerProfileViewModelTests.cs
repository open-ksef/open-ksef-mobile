using System.Net;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class SellerProfileViewModelTests
{
    private static readonly Guid TenantId = Guid.Parse("304f3ff7-06b6-43e8-bdf4-a0ad77f194e3");
    private const string Account = "61109010140000071219812874";
    private const string AccountGrouped = "61 1090 1014 0000 0712 1981 2874";

    private readonly IApiService _api = Substitute.For<IApiService>();

    public SellerProfileViewModelTests() => Shell.Current.Reset();

    private static SellerProfileDto Profile(bool complete = false, string? name = null) => new()
    {
        Nip = "1111111111",
        LegalName = name,
        NumberingPattern = "FV/{nr}/{MM}/{RRRR}",
        NextNumber = 57,
        NextNumberPeriod = "2026-11",
        IsComplete = complete,
        KsefEnvironment = "test"
    };

    private async Task<SellerProfileViewModel> LoadedAsync(SellerProfileDto? profile)
    {
        _api.GetSellerProfileAsync(TenantId).Returns(profile);
        var vm = new SellerProfileViewModel(_api) { TenantId = TenantId.ToString() };
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Load_ServerWithoutIssuing_MarksUnsupported()
    {
        var vm = await LoadedAsync(null);

        Assert.True(vm.IsUnsupported);
        Assert.False(vm.IsLoaded);
    }

    [Fact]
    public async Task Load_IncompleteProfile_ShowsIncompleteAlertAndValues()
    {
        var vm = await LoadedAsync(Profile());

        Assert.True(vm.IsIncomplete);
        Assert.Equal("1111111111", vm.Nip);
        Assert.Equal("57", vm.NextNumber);
        Assert.True(vm.IsOtherAccount);
    }

    [Fact]
    public async Task LookupNip_Found_FillsFieldsAndOffersWhiteListAccounts()
    {
        var vm = await LoadedAsync(Profile());
        _api.LookupNipAsync("1111111111").Returns(new NipLookupDto
        {
            Nip = "1111111111", Name = "OpenKSeF Test A sp. z o.o.", AddressLine1 = "ul. Prosta 1", AddressLine2 = "00-001 Warszawa",
            VatStatus = "Czynny", BankAccounts = [Account]
        });

        await vm.LookupNipCommand.ExecuteAsync(null);

        Assert.Equal(("OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa"), (vm.LegalName, vm.AddressLine1, vm.AddressLine2));
        Assert.Equal([AccountGrouped, SellerProfileViewModel.OtherAccount], vm.BankAccountOptions);
        Assert.Equal(AccountGrouped, vm.SelectedBankAccount);
        Assert.False(vm.IsOtherAccount);
        Assert.StartsWith("Czynny podatnik VAT · biała lista ", vm.LookupStatus);
        Assert.Equal("Success", vm.LookupTone);
    }

    [Fact]
    public async Task LookupNip_TypedValuesAndUserDeclines_KeepsTypedValues()
    {
        var vm = await LoadedAsync(Profile(name: "Moja nazwa"));
        _api.LookupNipAsync("1111111111").Returns(new NipLookupDto { Name = "Z białej listy", VatStatus = "Czynny" });
        Shell.Current.AlertAnswer = false;

        await vm.LookupNipCommand.ExecuteAsync(null);

        Assert.Equal("Moja nazwa", vm.LegalName);
        Assert.Equal("Zastąpić wpisane dane danymi z białej listy?", Shell.Current.LastAlertMessage);
    }

    [Fact]
    public async Task LookupNip_NotFound_KeepsManualEntry()
    {
        var vm = await LoadedAsync(Profile(name: "Ręcznie"));
        _api.LookupNipAsync("1111111111").Returns((NipLookupDto?)null);

        await vm.LookupNipCommand.ExecuteAsync(null);

        Assert.Equal("Ręcznie", vm.LegalName);
        Assert.Equal("Nie ma na białej liście — wpisz dane ręcznie", vm.LookupStatus);
        Assert.Equal("Warning", vm.LookupTone);
    }

    [Fact]
    public async Task LookupNip_WhiteListDown_KeepsManualEntry()
    {
        var vm = await LoadedAsync(Profile(name: "Ręcznie"));
        _api.LookupNipAsync("1111111111").ThrowsAsync(new ApiException(HttpStatusCode.ServiceUnavailable, "503"));

        await vm.LookupNipCommand.ExecuteAsync(null);

        Assert.Equal("Ręcznie", vm.LegalName);
        Assert.Equal("Biała lista nie odpowiada, wpisz dane ręcznie", vm.LookupStatus);
        Assert.Equal("Error", vm.LookupTone);
    }

    [Fact]
    public async Task Save_ApiRejectsBankAccount_ShowsMessageUnderField()
    {
        var vm = await LoadedAsync(Profile());
        vm.OtherBankAccount = "12 3456";
        _api.UpdateSellerProfileAsync(TenantId, Arg.Any<SellerProfileRequest>()).ThrowsAsync(new ApiException(
            HttpStatusCode.BadRequest, "400",
            new ValidationEnvelope { Errors = [new() { Code = "INV-VAL-012", Field = "seller.bankAccount", Message = "Niepoprawny numer rachunku" }] }));

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Niepoprawny numer rachunku", vm.BankAccountError);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Save_Complete_SendsDigitsOnlyAndGoesBack()
    {
        var vm = await LoadedAsync(Profile());
        vm.LegalName = "Firma";
        vm.OtherBankAccount = AccountGrouped;
        SellerProfileRequest? sent = null;
        _api.UpdateSellerProfileAsync(TenantId, Arg.Do<SellerProfileRequest>(r => sent = r)).Returns(Profile(complete: true));

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Account, sent!.BankAccount);
        Assert.Equal((57, "2026-11"), (sent.NextNumber, sent.NextNumberPeriod));
        Assert.False(vm.IsIncomplete);
        Assert.Equal("..", Shell.Current.LastRoute);
    }

    [Fact]
    public async Task Save_ChangedNextNumber_StartsCurrentPeriod()
    {
        var vm = await LoadedAsync(Profile());
        vm.NextNumber = "1";
        SellerProfileRequest? sent = null;
        _api.UpdateSellerProfileAsync(TenantId, Arg.Do<SellerProfileRequest>(r => sent = r)).Returns(Profile());

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal((1, DateTime.Today.ToString("yyyy-MM")), (sent!.NextNumber, sent.NextNumberPeriod));
    }

    [Fact]
    public void NumberPreview_FillsPatternTokens()
    {
        var vm = new SellerProfileViewModel(_api) { NumberingPattern = "FV/{nr}/{MM}/{RRRR}", NextNumber = "12" };

        Assert.Equal($"Następna faktura otrzyma numer FV/12/{DateTime.Today:MM}/{DateTime.Today:yyyy}", vm.NumberPreview);
    }
}
