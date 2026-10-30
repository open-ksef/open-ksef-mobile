using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

[QueryProperty(nameof(TenantId), "tenantId")]
public partial class SellerProfileViewModel : ObservableObject
{
    public const string OtherAccount = "Inny rachunek";
    public const string IncompleteMessage = "Uzupełnij dane firmy, aby wystawiać faktury.";

    private readonly IApiService _apiService;
    private int? _loadedNextNumber;
    private string? _loadedNextNumberPeriod;

    [ObservableProperty]
    private string? _tenantId;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLookingUp;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isUnsupported;

    [ObservableProperty]
    private bool _isIncomplete;

    [ObservableProperty]
    private string _nip = string.Empty;

    [ObservableProperty]
    private string? _legalName;

    [ObservableProperty]
    private string? _addressLine1;

    [ObservableProperty]
    private string? _addressLine2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOtherAccount), nameof(HasBankAccountOptions))]
    private ObservableCollection<string> _bankAccountOptions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOtherAccount))]
    private string? _selectedBankAccount;

    [ObservableProperty]
    private string? _otherBankAccount;

    [ObservableProperty]
    private bool _cashAccounting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NumberPreview))]
    private string? _numberingPattern = "FV/{nr}/{MM}/{RRRR}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NumberPreview))]
    private string? _nextNumber = "1";

    [ObservableProperty]
    private string? _lookupStatus;

    [ObservableProperty]
    private string _lookupTone = "Success";

    [ObservableProperty]
    private string? _legalNameError;

    [ObservableProperty]
    private string? _addressError;

    [ObservableProperty]
    private string? _bankAccountError;

    [ObservableProperty]
    private string? _numberingPatternError;

    [ObservableProperty]
    private string? _nextNumberError;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasBankAccountOptions => BankAccountOptions.Count > 0;

    public bool IsOtherAccount => BankAccountOptions.Count == 0 || SelectedBankAccount == OtherAccount;

    public string NumberPreview => int.TryParse(NextNumber, out var n) && !string.IsNullOrWhiteSpace(NumberingPattern)
        ? $"Następna faktura otrzyma numer {IssuingFormat.NumberPreview(NumberingPattern, n, DateOnly.FromDateTime(DateTime.Today))}"
        : string.Empty;

    public SellerProfileViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    partial void OnTenantIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy || !Guid.TryParse(TenantId, out var tenantId))
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var profile = await _apiService.GetSellerProfileAsync(tenantId);
            if (profile is null)
            {
                IsUnsupported = true;
                return;
            }

            Nip = profile.Nip;
            LegalName = profile.LegalName;
            AddressLine1 = profile.AddressLine1;
            AddressLine2 = profile.AddressLine2;
            OtherBankAccount = IssuingFormat.Nrb(profile.BankAccount);
            CashAccounting = profile.CashAccounting;
            NumberingPattern = profile.NumberingPattern ?? NumberingPattern;
            _loadedNextNumber = profile.NextNumber;
            _loadedNextNumberPeriod = profile.NextNumberPeriod;
            NextNumber = (profile.NextNumber ?? 1).ToString();
            IsIncomplete = !profile.IsComplete;
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LookupNipAsync()
    {
        if (IsLookingUp || string.IsNullOrEmpty(Nip))
            return;

        try
        {
            IsLookingUp = true;
            LookupStatus = null;
            var result = await _apiService.LookupNipAsync(Nip);
            if (result is null)
            {
                (LookupStatus, LookupTone) = (IssuingFormat.LookupNotFound, "Warning");
                return;
            }

            var typed = new[] { LegalName, AddressLine1, AddressLine2 }.Any(v => !string.IsNullOrWhiteSpace(v));
            var differs = LegalName != result.Name || AddressLine1 != result.AddressLine1 || AddressLine2 != result.AddressLine2;
            if (!typed || !differs || await Shell.Current.DisplayAlertAsync(
                    "Pobierz z NIP", "Zastąpić wpisane dane danymi z białej listy?", "Zastąp", "Zostaw moje"))
            {
                LegalName = result.Name;
                AddressLine1 = result.AddressLine1;
                AddressLine2 = result.AddressLine2;
            }

            var current = IssuingFormat.Nrb(OtherBankAccount);
            var accounts = result.BankAccounts.Select(IssuingFormat.Nrb).ToList();
            BankAccountOptions = new ObservableCollection<string>([.. accounts, OtherAccount]);
            SelectedBankAccount = accounts.Contains(current) ? current
                : string.IsNullOrEmpty(current) && accounts.Count > 0 ? accounts[0]
                : OtherAccount;
            (LookupStatus, LookupTone) = (IssuingFormat.LookupStatus(result, DateTime.Now), result.VatStatus == "Czynny" ? "Success" : "Warning");
        }
        catch (Exception ex) when (ApiException.IsNetworkFailure(ex))
        {
            (LookupStatus, LookupTone) = (IssuingFormat.LookupDown, "Error");
        }
        catch (ApiException ex)
        {
            (LookupStatus, LookupTone) = (ex.Validation?.Errors.FirstOrDefault()?.Message ?? ex.Message, "Error");
        }
        finally
        {
            IsLookingUp = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy || !Guid.TryParse(TenantId, out var tenantId))
            return;

        LegalNameError = AddressError = BankAccountError = NumberingPatternError = NextNumberError = ErrorMessage = null;

        int? nextNumber = null;
        if (!string.IsNullOrWhiteSpace(NextNumber))
        {
            if (!int.TryParse(NextNumber, out var parsed) || parsed < 1)
            {
                NextNumberError = "Podaj numer większy od zera";
                return;
            }
            nextNumber = parsed;
        }

        var request = new SellerProfileRequest
        {
            LegalName = LegalName,
            AddressLine1 = AddressLine1,
            AddressLine2 = AddressLine2,
            BankAccount = IssuingFormat.Digits(IsOtherAccount ? OtherBankAccount : SelectedBankAccount) is { Length: > 0 } account ? account : null,
            CashAccounting = CashAccounting,
            NumberingPattern = NumberingPattern,
            NextNumber = nextNumber,
            NextNumberPeriod = nextNumber == _loadedNextNumber ? _loadedNextNumberPeriod : DateTime.Today.ToString("yyyy-MM")
        };

        try
        {
            IsBusy = true;
            var saved = await _apiService.UpdateSellerProfileAsync(tenantId, request);
            IsIncomplete = !saved.IsComplete;
            if (saved.IsComplete)
                try { await Shell.Current.GoToAsync(".."); } catch { }
        }
        catch (ApiException ex) when (ex.Validation is { Errors.Count: > 0 } validation)
        {
            foreach (var issue in validation.Errors)
                ApplyError(issue);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyError(ValidationIssue issue)
    {
        switch (issue.Field?.ToLowerInvariant().Replace("seller.", string.Empty))
        {
            case "legalname" or "name": LegalNameError = issue.Message; break;
            case "addressline1" or "addressline2": AddressError = issue.Message; break;
            case "bankaccount": BankAccountError = issue.Message; break;
            case "numberingpattern": NumberingPatternError = issue.Message; break;
            case "nextnumber" or "nextnumberperiod": NextNumberError = issue.Message; break;
            default: ErrorMessage = ErrorMessage is null ? issue.Message : $"{ErrorMessage}\n{issue.Message}"; break;
        }
    }
}
