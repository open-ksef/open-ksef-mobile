using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class DraftLineViewModel : ObservableObject
{
    public static string[] VatRateLabels { get; } = [.. IssuedInvoiceTotals.VatRates.Select(IssuingFormat.VatRateLabel)];

    public static string[] UnitLabels { get; } = ["szt.", "usł.", "godz.", "mies."];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(Description))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _unit = "szt.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gross), nameof(GrossText), nameof(NetVatText), nameof(Summary), nameof(Description))]
    private string _quantity = "1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gross), nameof(GrossText), nameof(NetVatText), nameof(Summary), nameof(Description))]
    private string _unitNetPrice = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gross), nameof(GrossText), nameof(NetVatText), nameof(Summary), nameof(Description))]
    private string _vatRateLabel = "23%";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GrossText), nameof(NetVatText), nameof(QuantityInvalid), nameof(PriceInvalid))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuantityInvalid), nameof(PriceInvalid))]
    private string? _errorField;

    public bool QuantityInvalid => Error is not null && ErrorField == "quantity";

    public bool PriceInvalid => Error is not null && ErrorField == "unitNetPrice";

    public string VatRate => IssuingFormat.VatRateCode(VatRateLabel);

    public string Gross => IssuingFormat.Money(IssuedInvoiceTotals.Compute([ToRequest()]).TotalGross);

    public string GrossText => Error is null ? Gross : "—";

    public string NetVatText
    {
        get
        {
            if (Error is not null)
                return "—";
            var totals = IssuedInvoiceTotals.Compute([ToRequest()]);
            return $"Netto {IssuingFormat.Amount(totals.TotalNet)} · VAT {IssuingFormat.Amount(totals.TotalVat)}";
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Nowa pozycja" : Name;

    public string Summary =>
        $"{Quantity} {Unit} × {(IssuingFormat.TryParseDecimal(UnitNetPrice, out var price) ? IssuingFormat.Amount(price) : "—")} netto · VAT {VatRateLabel}";

    public string Description => $"{DisplayName}, {Gross}";

    public DraftLineViewModel Clone() => new DraftLineViewModel().CopyFrom(this);

    public DraftLineViewModel CopyFrom(DraftLineViewModel source)
    {
        Name = source.Name;
        Unit = source.Unit;
        Quantity = source.Quantity;
        UnitNetPrice = source.UnitNetPrice;
        VatRateLabel = source.VatRateLabel;
        Error = source.Error;
        ErrorField = source.ErrorField;
        return this;
    }

    public static DraftLineViewModel From(IssuedInvoiceLineRequest line) => new()
    {
        Name = line.Name,
        Unit = line.Unit ?? "szt.",
        Quantity = IssuingFormat.EditableNumber(line.Quantity),
        UnitNetPrice = line.UnitNetPrice.ToString("0.00", IssuingFormat.Pl),
        VatRateLabel = IssuingFormat.VatRateLabel(line.VatRate)
    };

    public IssuedInvoiceLineRequest ToRequest() => new()
    {
        Name = Name.Trim(),
        Unit = string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim(),
        Quantity = IssuingFormat.TryParseDecimal(Quantity, out var q) ? q : 0,
        UnitNetPrice = IssuingFormat.TryParseDecimal(UnitNetPrice, out var p) ? p : 0,
        VatRate = VatRate
    };

    private (string?, string?) InputError =>
        IssuingFormat.DecimalInputError(Quantity) is { } quantity ? (quantity, "quantity")
        : IssuingFormat.DecimalInputError(UnitNetPrice) is { } price ? (price, "unitNetPrice")
        : (null, null);

    public bool Validate()
    {
        (Error, ErrorField) = InputError is (not null, _) and var input ? input
            : !IssuingFormat.TryParseDecimal(Quantity, out _) ? ("Podaj ilość, np. 1 lub 0,5", "quantity")
            : !IssuingFormat.TryParseDecimal(UnitNetPrice, out var price) ? ("Podaj cenę netto, np. 1 200,00", "unitNetPrice")
            : price < 0 ? ("Cena netto nie może być ujemna.", "unitNetPrice")
            : (null, null);
        return Error is null;
    }

    partial void OnQuantityChanged(string value) => (Error, ErrorField) = InputError;

    partial void OnUnitNetPriceChanged(string value) => (Error, ErrorField) = InputError;
}

public sealed record VatSummaryRow(string Rate, string Net, string Vat, string Gross);

[QueryProperty(nameof(TenantId), "tenantId")]
[QueryProperty(nameof(InvoiceId), "invoiceId")]
public partial class InvoiceDraftViewModel : ObservableObject
{
    public const decimal SplitPaymentThreshold = 15000m;

    public static string[] ExemptionKindLabels { get; } = ["Ustawa o VAT", "Dyrektywa", "Inna podstawa"];

    private static readonly string[] ExemptionKinds = ["Act", "Directive", "Other"];

    private const string NoConnection = "Brak połączenia z serwerem. Dane w formularzu zostały. Spróbuj ponownie po powrocie sieci, nie twórz nowej faktury.";

    internal const string NotLoaded = "Nie udało się wczytać faktury. Wczytaj ją ponownie, zanim zapiszesz lub wystawisz.";

    internal const string Sent = "Faktura została wysłana do KSeF. Status sprawdzisz na liście wystawionych. Nie wystawiaj jej drugi raz.";

    internal const string CheckIssuedList = "Sprawdź listę wystawionych, zanim zapiszesz ponownie.";

    internal const string IssuedList = "//main/issued?status=Draft";

    private readonly IApiService _apiService;
    private SellerProfileDto? _profile;
    private string? _routeOnReturn;

    public bool IsShown { get; set; } = true;
    private string? _invoiceNumber;
    private uint? _version;
    private bool _loaded;
    private bool _portalOnly;
    private bool _sent;

    [ObservableProperty]
    private string? _tenantId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string? _invoiceId;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompany), nameof(BuyerNameLabel))]
    private string _buyerKind = "Company";

    [ObservableProperty]
    private string? _buyerNip;

    [ObservableProperty]
    private string? _buyerName;

    [ObservableProperty]
    private string? _buyerAddressLine1;

    [ObservableProperty]
    private string? _buyerAddressLine2;

    [ObservableProperty]
    private bool _isLookingUp;

    [ObservableProperty]
    private string? _lookupStatus;

    [ObservableProperty]
    private string _lookupTone = "Success";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DatesDiffer), nameof(NumberPreview))]
    private DateTime _issueDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DatesDiffer))]
    private DateTime _saleDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _dueDate = DateTime.Today.AddDays(14);

    [ObservableProperty]
    private string _dueDays = "14";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTransfer))]
    private string _paymentMethod = "Transfer";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSplitPaymentWarning))]
    private bool _splitPayment;

    [ObservableProperty]
    private string _exemptionKindLabel = "Ustawa o VAT";

    [ObservableProperty]
    private string _exemptionText = "art. 113 ust. 1 ustawy o VAT";

    [ObservableProperty]
    private bool _hasExemptLine;

    [ObservableProperty]
    private ObservableCollection<DraftLineViewModel> _lines = [];

    [ObservableProperty]
    private ObservableCollection<VatSummaryRow> _vatSummary = [];

    private DraftLineViewModel? _editedLine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditingTitle), nameof(CanDeleteEditingLine))]
    private DraftLineViewModel? _editingLine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalGrossText), nameof(ShowSplitPaymentWarning))]
    private decimal _totalGross;

    [ObservableProperty]
    private string? _savedAt;

    [ObservableProperty]
    private string? _buyerNipError;

    [ObservableProperty]
    private string? _buyerNameError;

    [ObservableProperty]
    private string? _buyerAddressError;

    [ObservableProperty]
    private string? _issueDateError;

    [ObservableProperty]
    private bool _showCopyAsNew;

    [ObservableProperty]
    private string? _dueDateError;

    [ObservableProperty]
    private string? _paymentError;

    [ObservableProperty]
    private string? _exemptionError;

    [ObservableProperty]
    private string? _linesError;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _canReload;

    public InvoiceDraftViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Attach(new DraftLineViewModel());
    }

    public string Title => InvoiceId is null ? "Nowa faktura" : "Edycja szkicu";

    public bool IsCompany => BuyerKind == "Company";

    public string BuyerNameLabel => IsCompany ? "Nazwa" : "Imię i nazwisko";

    public string EditingTitle => _editedLine is null ? "Nowa pozycja" : $"Pozycja {Lines.IndexOf(_editedLine) + 1}";

    public bool CanDeleteEditingLine => _editedLine is not null;

    public bool IsTransfer => PaymentMethod == "Transfer";

    public bool DatesDiffer => IssueDate.Date != SaleDate.Date;

    public string TotalGrossText => IssuingFormat.Money(TotalGross);

    public bool ShowSplitPaymentWarning => TotalGross > SplitPaymentThreshold && !SplitPayment;

    public string? Environment => _profile?.KsefEnvironment;

    public string BankAccount => IssuingFormat.Nrb(_profile?.BankAccount);

    public string NumberPreview => _invoiceNumber is null && IsCurrentMonth(DateOnly.FromDateTime(IssueDate)) && _profile?.NextNumberPreview is { } preview
        ? $"Numer nadamy przy wystawieniu: {preview}"
        : string.Empty;

    partial void OnIssueDateChanged(DateTime oldValue, DateTime newValue)
    {
        if (SaleDate.Date == oldValue.Date)
            SaleDate = newValue;
        ApplyDueDays();
    }

    partial void OnDueDaysChanged(string value) => ApplyDueDays();

    private void ApplyDueDays()
    {
        if (int.TryParse(DueDays, out var days))
            DueDate = IssueDate.Date.AddDays(days);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_routeOnReturn is { } route)
        {
            _routeOnReturn = null;
            if (route == IssuedList)
                await OpenIssuedListAsync();
            else
                try { await Shell.Current.GoToAsync(route); } catch { _routeOnReturn = route; }
            return;
        }

        if (_loaded || IsBusy || !Guid.TryParse(TenantId, out var tenantId))
            return;

        try
        {
            IsBusy = true;
            _profile = await _apiService.GetSellerProfileAsync(tenantId);
            OnPropertyChanged(nameof(Environment));
            OnPropertyChanged(nameof(BankAccount));
            OnPropertyChanged(nameof(NumberPreview));

            if (Guid.TryParse(InvoiceId, out var invoiceId))
            {
                var invoice = await _apiService.GetIssuedInvoiceAsync(tenantId, invoiceId);
                if (IssuingFormat.IsPortalOnly(invoice))
                {
                    _loaded = _portalOnly = true;
                    ErrorMessage = "Tę fakturę edytujesz w portalu.";
                    if (IsShown)
                        try { await Shell.Current.GoToAsync($"../issuedInvoiceDetails?tenantId={tenantId}&invoiceId={invoiceId}"); } catch { }
                    return;
                }

                Fill(invoice);
            }

            _loaded = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            CanReload = Guid.TryParse(InvoiceId, out _);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (IsBusy || _loaded)
            return;

        ErrorMessage = null;
        CanReload = false;
        await LoadAsync();
    }

    private void Fill(IssuedInvoiceDto invoice)
    {
        _invoiceNumber = invoice.Number;
        _version = invoice.Version;
        OnPropertyChanged(nameof(NumberPreview));
        BuyerKind = invoice.Buyer.Kind;
        BuyerNip = invoice.Buyer.Nip;
        BuyerName = invoice.Buyer.Name;
        BuyerAddressLine1 = invoice.Buyer.AddressLine1;
        BuyerAddressLine2 = invoice.Buyer.AddressLine2;
        IssueDate = invoice.IssueDate.ToDateTime(TimeOnly.MinValue);
        SaleDate = invoice.SaleDate.ToDateTime(TimeOnly.MinValue);
        var days = invoice.DueDate.DayNumber - invoice.IssueDate.DayNumber;
        DueDays = days is 7 or 14 or 30 ? days.ToString() : string.Empty;
        DueDate = invoice.DueDate.ToDateTime(TimeOnly.MinValue);
        PaymentMethod = invoice.PaymentMethod;
        SplitPayment = invoice.SplitPayment;
        if (invoice.ExemptionBasis is { } basis)
        {
            ExemptionKindLabel = ExemptionKindLabels[Math.Max(0, Array.IndexOf(ExemptionKinds, basis.Kind))];
            ExemptionText = basis.Text;
        }

        foreach (var line in Lines)
            line.PropertyChanged -= OnLineChanged;
        Lines = new ObservableCollection<DraftLineViewModel>(invoice.Lines.Select(DraftLineViewModel.From));
        foreach (var line in Lines)
            line.PropertyChanged += OnLineChanged;
        Recalculate();
    }

    [RelayCommand]
    private void AddLine() => OpenLine(null);

    [RelayCommand]
    private void EditLine(DraftLineViewModel line) => OpenLine(line);

    private void OpenLine(DraftLineViewModel? line)
    {
        if (IsBusy)
            return;

        _editedLine = line;
        EditingLine = line?.Clone() ?? new DraftLineViewModel();
    }

    [RelayCommand]
    private void SaveLine()
    {
        if (EditingLine is not { } editing || !editing.Validate())
            return;

        if (_editedLine is { } line)
            line.CopyFrom(editing);
        else
            Attach(editing);
        CloseLine();
    }

    [RelayCommand]
    private void DeleteLine()
    {
        if (_editedLine is { } line)
        {
            line.PropertyChanged -= OnLineChanged;
            Lines.Remove(line);
            Recalculate();
        }
        CloseLine();
    }

    [RelayCommand]
    private void CloseLine()
    {
        _editedLine = null;
        EditingLine = null;
    }

    private void Attach(DraftLineViewModel line)
    {
        line.PropertyChanged += OnLineChanged;
        Lines.Add(line);
        Recalculate();
    }

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DraftLineViewModel.Gross))
            Recalculate();
    }

    private void Recalculate()
    {
        var totals = IssuedInvoiceTotals.Compute(Lines.Select(l => l.ToRequest()));
        VatSummary = new ObservableCollection<VatSummaryRow>(totals.VatSummary.Select(s => new VatSummaryRow(
            IssuingFormat.VatRateLabel(s.VatRate), IssuingFormat.Money(s.Net), IssuingFormat.Money(s.Vat), IssuingFormat.Money(s.Net + s.Vat))));
        TotalGross = totals.TotalGross;
        HasExemptLine = Lines.Any(l => l.VatRate == "zw");
    }

    [RelayCommand]
    private async Task LookupBuyerNipAsync()
    {
        if (IsLookingUp || string.IsNullOrWhiteSpace(BuyerNip))
            return;

        try
        {
            IsLookingUp = true;
            LookupStatus = null;
            var result = await _apiService.LookupNipAsync(IssuingFormat.Digits(BuyerNip));
            if (result is null)
            {
                (LookupStatus, LookupTone) = (IssuingFormat.LookupNotFound, "Warning");
                return;
            }

            var typed = new[] { BuyerName, BuyerAddressLine1, BuyerAddressLine2 }.Any(v => !string.IsNullOrWhiteSpace(v));
            var differs = BuyerName != result.Name || BuyerAddressLine1 != result.AddressLine1 || BuyerAddressLine2 != result.AddressLine2;
            if (!typed || !differs || IsShown && await Shell.Current.DisplayAlertAsync(
                    "Pobierz z NIP", "Zastąpić wpisane dane danymi z białej listy?", "Zastąp", "Zostaw moje"))
            {
                BuyerName = result.Name;
                BuyerAddressLine1 = result.AddressLine1;
                BuyerAddressLine2 = result.AddressLine2;
            }

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
    private async Task SaveDraftAsync()
    {
        if (!IsBusy)
            await SaveAsync();
    }

    [RelayCommand]
    private async Task IssueAsync()
    {
        if (IsBusy || !Guid.TryParse(TenantId, out var tenantId))
            return;

        if (!IsCompany)
        {
            BuyerNameError = string.IsNullOrWhiteSpace(BuyerName) ? "Uzupełnij nazwę nabywcy" : null;
            BuyerAddressError = string.IsNullOrWhiteSpace(BuyerAddressLine1) ? "Uzupełnij adres nabywcy" : null;
            if (BuyerNameError is not null || BuyerAddressError is not null)
            {
                await AlertInvalidFormAsync(string.Join("\n", new[] { BuyerNameError, BuyerAddressError }.OfType<string>()));
                return;
            }
        }

        var saved = await SaveAsync();
        if (saved is null)
            return;
        if (!IsShown)
        {
            ErrorMessage = "Zapisano szkic. Faktura nie została wystawiona.";
            return;
        }

        try
        {
            IsBusy = true;
            if (!await ConfirmIssueAsync(_profile?.NextNumberPreview, saved))
                return;

            var issued = await IssueConfirmingPastDateAsync(_apiService, tenantId, saved);
            if (issued is null)
                return;

            _sent = true;
            if (IsShown)
                try { await Shell.Current.GoToAsync($"../issuedInvoiceDetails?tenantId={tenantId}&invoiceId={issued.Id}"); }
                catch { await ShowSentAsync(); }
            else
                _routeOnReturn = $"../issuedInvoiceDetails?tenantId={tenantId}&invoiceId={issued.Id}";
        }
        catch (ApiException ex) when (ex.Validation is { Errors.Count: > 0 } validation)
        {
            ApplyValidation(validation);
            await AlertInvalidFormAsync(ValidationSummary(validation));
        }
        catch (Exception ex) when (ApiException.IsNetworkFailure(ex) || ex is ApiException { StatusCode: >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices })
        {
            _sent = ex is ApiException { StatusCode: >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices };
            if (IsShown)
                try { await Shell.Current.GoToAsync($"../issuedInvoiceDetails?tenantId={tenantId}&invoiceId={saved.Id}"); }
                catch { await (_sent ? ShowSentAsync() : ShowErrorAsync(NoConnection)); }
            else
                _routeOnReturn = $"../issuedInvoiceDetails?tenantId={tenantId}&invoiceId={saved.Id}";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyAsNewAsync()
    {
        if (!Guid.TryParse(TenantId, out var tenantId) || !Guid.TryParse(InvoiceId, out var invoiceId))
            return;

        try
        {
            var copy = await _apiService.CopyIssuedInvoiceAsync(tenantId, invoiceId);
            _sent = false;
            ClearErrors();
            InvoiceId = copy.Id.ToString();
            Fill(copy);
        }
        catch (ApiException ex) when (!ApiException.IsNetworkFailure(ex) && ex.StatusCode is not (>= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices))
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception)
        {
            await SendToIssuedListAsync();
        }
    }

    internal static async Task<IssuedInvoiceDto?> IssueConfirmingPastDateAsync(IApiService api, Guid tenantId, IssuedInvoiceDto invoice)
    {
        try
        {
            return await api.IssueInvoiceAsync(tenantId, invoice.Id, invoice.Version);
        }
        catch (ApiException ex) when (ex.Validation?.Errors is [{ Code: "INV-W-PAST-DATE" } past])
        {
            if (!await Shell.Current.DisplayAlertAsync("Data wystawienia w przeszłości", past.Message, "Potwierdź", "Anuluj"))
                return null;

            return await api.IssueInvoiceAsync(tenantId, invoice.Id, invoice.Version, confirmPastIssueDate: true);
        }
    }

    /// <summary>Only a known test or demo environment skips the dialog: an irreversible send must not depend on a value that failed to load.</summary>
    internal static Task<bool> ConfirmIssueAsync(string? numberPreview, IssuedInvoiceDto invoice)
    {
        var environment = invoice.Ksef.Environment;
        if (string.Equals(environment, "test", StringComparison.OrdinalIgnoreCase) || string.Equals(environment, "demo", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(true);

        var buyer = invoice.Buyer.Nip is { Length: > 0 } nip ? $"{invoice.Buyer.Name}, NIP {nip}" : invoice.Buyer.Name;
        var message =
            "Faktura trafi do KSeF jako dokument księgowy. Po przyjęciu nie można jej usunąć ani zmienić.\n\n" +
            $"{NumberLine(numberPreview, invoice)}\n" +
            $"Nabywca: {buyer}\n" +
            $"Data wystawienia: {IssuingFormat.Date(invoice.IssueDate)}\n" +
            $"Do zapłaty: {IssuingFormat.Money(invoice.TotalGross, invoice.Currency)}\n\n" +
            "Korekt nie wystawisz jeszcze w OpenKSeF — użyj Aplikacji Podatnika KSeF.";
        var title = IssuingFormat.IsProduction(environment) ? "PRODUKCJA · Wystawić fakturę i wysłać do KSeF?" : "Wystawić fakturę i wysłać do KSeF?";
        return Shell.Current.DisplayAlertAsync(title, message, "Wystaw i wyślij", "Wróć do edycji");
    }

    private static string NumberLine(string? numberPreview, IssuedInvoiceDto invoice)
    {
        if (invoice.Number is { Length: > 0 } number)
            return $"Numer: {number}";

        return numberPreview is { Length: > 0 } && IsCurrentMonth(invoice.IssueDate)
            ? $"Numer (przewidywany): {numberPreview}"
            : "Numer: nadamy przy wysłaniu, zależy od daty wystawienia";
    }

    private static bool IsCurrentMonth(DateOnly date) => date.Year == DateTime.Today.Year && date.Month == DateTime.Today.Month;

    private async Task<IssuedInvoiceDto?> SaveAsync()
    {
        if (_portalOnly || !Guid.TryParse(TenantId, out var tenantId))
            return null;

        if (_sent)
        {
            await ShowSentAsync();
            return null;
        }

        if (!_loaded && Guid.TryParse(InvoiceId, out _))
        {
            await ShowNotLoadedAsync();
            return null;
        }

        ClearErrors();
        var invalid = Lines.Where(l => !l.Validate()).ToList();
        if (invalid.Count > 0)
        {
            await AlertInvalidFormAsync(string.Join("\n", invalid.Select(l => $"Pozycja {Lines.IndexOf(l) + 1}: {l.Error}")));
            return null;
        }

        var creating = !Guid.TryParse(InvoiceId, out var id);
        try
        {
            IsBusy = true;
            var request = BuildRequest();
            var saved = !creating
                ? await _apiService.UpdateIssuedInvoiceAsync(tenantId, id, request, _version)
                : await _apiService.CreateIssuedInvoiceAsync(tenantId, request);
            _loaded = true;
            _version = saved.Version;
            InvoiceId = saved.Id.ToString();
            _routeOnReturn = null;
            SavedAt = $"Zapisano {IssuingFormat.Time(DateTime.Now)}";
            return saved;
        }
        catch (ApiException ex) when (ex.Validation is { Errors.Count: > 0 } validation)
        {
            ApplyValidation(validation);
            await AlertInvalidFormAsync(ValidationSummary(validation));
            return null;
        }
        catch (ApiException ex) when (creating && ex.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices)
        {
            await SendToIssuedListAsync();
            return null;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            ErrorMessage = ex.Message;
            if (IsShown && await Shell.Current.DisplayAlertAsync("Faktura zmieniona", "Ta faktura została zmieniona w innym miejscu. Wczytać aktualną wersję? Zmiany wpisane w tym formularzu przepadną.", "Wczytaj aktualną", "Zostaw formularz"))
            {
                ErrorMessage = null;
                _loaded = false;
                IsBusy = false;
                await LoadAsync();
            }
            return null;
        }
        catch (Exception ex) when (ApiException.IsNetworkFailure(ex))
        {
            await ShowErrorAsync(NoConnection);
            return null;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal IssuedInvoiceDraftRequest BuildRequest() => new()
    {
        Buyer = new PartyDto
        {
            Kind = BuyerKind,
            Nip = IsCompany ? IssuingFormat.Digits(BuyerNip) : null,
            Name = BuyerName?.Trim(),
            AddressLine1 = BuyerAddressLine1?.Trim(),
            AddressLine2 = BuyerAddressLine2?.Trim()
        },
        IssueDate = DateOnly.FromDateTime(IssueDate),
        SaleDate = DateOnly.FromDateTime(SaleDate),
        DueDate = DateOnly.FromDateTime(DueDate),
        PaymentMethod = PaymentMethod,
        SplitPayment = SplitPayment,
        ExemptionBasis = HasExemptLine
            ? new ExemptionBasisDto { Kind = ExemptionKinds[Math.Max(0, ExemptionKindLabels.ToList().IndexOf(ExemptionKindLabel))], Text = ExemptionText.Trim() }
            : null,
        Lines = [.. Lines.Select(l => l.ToRequest())]
    };

    private void ClearErrors()
    {
        ShowCopyAsNew = false;
        BuyerNipError = BuyerNameError = BuyerAddressError = IssueDateError = DueDateError = PaymentError = ExemptionError = LinesError = ErrorMessage = null;
        foreach (var line in Lines)
            line.Error = null;
    }

    internal void ApplyValidation(ValidationEnvelope validation)
    {
        ShowCopyAsNew = validation.Errors.Any(e => e.Code == "INV-VAL-022");
        var unplaced = new List<string>();
        foreach (var issue in validation.Errors)
        {
            var field = issue.Field ?? string.Empty;
            switch (field)
            {
                case "buyer.nip": BuyerNipError = issue.Message; break;
                case "buyer.name": BuyerNameError = issue.Message; break;
                case "buyer.addressLine1" or "buyer.addressLine2": BuyerAddressError = issue.Message; break;
                case "issueDate" or "saleDate": IssueDateError = issue.Message; break;
                case "dueDate": DueDateError = issue.Message; break;
                case "paymentMethod" or "splitPayment": PaymentError = issue.Message; break;
                case "exemptionBasis" or "exemptionBasis.kind": ExemptionError = issue.Message; break;
                case "lines": LinesError = issue.Message; break;
                default:
                    if (LineIndex(field) is { } i && i < Lines.Count)
                        (Lines[i].Error, Lines[i].ErrorField) = (issue.Message, field.Split('.').Last());
                    else
                        unplaced.Add(issue.Message);
                    break;
            }
        }

        ErrorMessage = string.Join("\n", ["Popraw błędy zaznaczone w formularzu.", .. unplaced]);
    }

    private async Task SendToIssuedListAsync()
    {
        ErrorMessage = CheckIssuedList;
        if (IsShown)
            await OpenIssuedListAsync();
        else
            _routeOnReturn = IssuedList;
    }

    private async Task OpenIssuedListAsync()
    {
        await Shell.Current.DisplayAlertAsync("Nie wiadomo, czy zapisano szkic", CheckIssuedList, "OK");
        try { await Shell.Current.GoToAsync(IssuedList); } catch { _routeOnReturn = IssuedList; }
    }

    private async Task ShowNotLoadedAsync()
    {
        ErrorMessage = NotLoaded;
        CanReload = true;
        if (IsShown && await Shell.Current.DisplayAlertAsync("Faktura niewczytana", NotLoaded, "Wczytaj ponownie", "Anuluj"))
            await ReloadAsync();
    }

    private Task ShowSentAsync()
    {
        ErrorMessage = Sent;
        return IsShown ? Shell.Current.DisplayAlertAsync("Faktura wysłana", Sent, "OK") : Task.CompletedTask;
    }

    private Task AlertInvalidFormAsync(string message) => IsShown ? Shell.Current.DisplayAlertAsync("Popraw fakturę", message, "OK") : Task.CompletedTask;

    private Task ShowErrorAsync(string message)
    {
        ErrorMessage = message;
        return IsShown ? Shell.Current.DisplayAlertAsync("Błąd", message, "OK") : Task.CompletedTask;
    }

    private static string ValidationSummary(ValidationEnvelope validation) =>
        string.Join("\n", validation.Errors.Select(e => LineIndex(e.Field ?? string.Empty) is { } i ? $"Pozycja {i + 1}: {e.Message}" : e.Message));

    private static int? LineIndex(string field) =>
        field.StartsWith("lines[") && field.IndexOf(']') is var end and > 6 && int.TryParse(field[6..end], out var i) ? i : null;
}
