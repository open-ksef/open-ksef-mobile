using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class DraftLineViewModel : ObservableObject
{
    public static IReadOnlyList<string> VatRateLabels { get; } = [.. IssuedInvoiceTotals.VatRates.Select(IssuingFormat.VatRateLabel)];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _unit = "szt.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gross))]
    private string _quantity = "1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gross))]
    private string _unitNetPrice = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gross))]
    private string _vatRateLabel = "23%";

    [ObservableProperty]
    private string? _error;

    public string VatRate => IssuingFormat.VatRateCode(VatRateLabel);

    public string Gross => IssuingFormat.Money(IssuedInvoiceTotals.Compute([ToRequest()]).TotalGross);

    public static DraftLineViewModel From(IssuedInvoiceLineRequest line) => new()
    {
        Name = line.Name,
        Unit = line.Unit ?? "szt.",
        Quantity = IssuingFormat.Number(line.Quantity),
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

    public bool Validate()
    {
        Error = !IssuingFormat.TryParseDecimal(Quantity, out _) ? "Podaj ilość, np. 1 lub 0,5"
            : !IssuingFormat.TryParseDecimal(UnitNetPrice, out _) ? "Podaj cenę netto, np. 1 200,00"
            : null;
        return Error is null;
    }
}

public sealed record VatSummaryRow(string Rate, string Net, string Vat, string Gross);

[QueryProperty(nameof(TenantId), "tenantId")]
[QueryProperty(nameof(InvoiceId), "invoiceId")]
public partial class InvoiceDraftViewModel : ObservableObject
{
    public const decimal SplitPaymentThreshold = 15000m;

    public static IReadOnlyList<string> ExemptionKindLabels { get; } = ["Ustawa o VAT", "Dyrektywa", "Inna podstawa"];

    private static readonly string[] ExemptionKinds = ["Act", "Directive", "Other"];

    private readonly IApiService _apiService;
    private SellerProfileDto? _profile;
    private bool _loaded;

    [ObservableProperty]
    private string? _tenantId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string? _invoiceId;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompany))]
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
    [NotifyPropertyChangedFor(nameof(DatesDiffer))]
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
    private string? _dueDateError;

    [ObservableProperty]
    private string? _paymentError;

    [ObservableProperty]
    private string? _exemptionError;

    [ObservableProperty]
    private string? _linesError;

    [ObservableProperty]
    private string? _errorMessage;

    public InvoiceDraftViewModel(IApiService apiService)
    {
        _apiService = apiService;
        AddLine();
    }

    public string Title => InvoiceId is null ? "Nowa faktura" : "Edycja szkicu";

    public bool IsCompany => BuyerKind == "Company";

    public bool IsTransfer => PaymentMethod == "Transfer";

    public bool DatesDiffer => IssueDate.Date != SaleDate.Date;

    public string TotalGrossText => IssuingFormat.Money(TotalGross);

    public bool ShowSplitPaymentWarning => TotalGross > SplitPaymentThreshold && !SplitPayment;

    public string? Environment => _profile?.KsefEnvironment;

    public string BankAccount => IssuingFormat.Nrb(_profile?.BankAccount);

    public string NumberPreview => _profile?.NextNumberPreview is { } preview ? $"Numer nadamy przy wystawieniu: {preview}" : string.Empty;

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
                Fill(await _apiService.GetIssuedInvoiceAsync(tenantId, invoiceId));
            _loaded = true;
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

    private void Fill(IssuedInvoiceDto invoice)
    {
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
    private void AddLine()
    {
        var line = new DraftLineViewModel();
        line.PropertyChanged += OnLineChanged;
        Lines.Add(line);
        Recalculate();
    }

    [RelayCommand]
    private void RemoveLine(DraftLineViewModel line)
    {
        line.PropertyChanged -= OnLineChanged;
        Lines.Remove(line);
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
            if (!typed || !differs || await Shell.Current.DisplayAlertAsync(
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
                return;
        }

        var saved = await SaveAsync();
        if (saved is null)
            return;

        try
        {
            IsBusy = true;
            if (!await ConfirmIssueAsync(Environment, _profile?.NextNumberPreview, saved))
                return;

            var issued = await _apiService.IssueInvoiceAsync(tenantId, saved.Id);
            try { await Shell.Current.GoToAsync($"../issuedInvoiceDetails?tenantId={tenantId}&invoiceId={issued.Id}"); } catch { }
        }
        catch (ApiException ex) when (ex.Validation is { Errors.Count: > 0 } validation)
        {
            ApplyValidation(validation);
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

    internal static Task<bool> ConfirmIssueAsync(string? environment, string? numberPreview, IssuedInvoiceDto invoice)
    {
        if (!IssuingFormat.IsProduction(environment))
            return Task.FromResult(true);

        var buyer = invoice.Buyer.Nip is { Length: > 0 } nip ? $"{invoice.Buyer.Name}, NIP {nip}" : invoice.Buyer.Name;
        var message =
            "Faktura trafi do KSeF jako dokument księgowy. Po przyjęciu nie można jej usunąć ani zmienić.\n\n" +
            $"Numer: {numberPreview ?? invoice.Number ?? "—"}\n" +
            $"Nabywca: {buyer}\n" +
            $"Data wystawienia: {IssuingFormat.Date(invoice.IssueDate)}\n" +
            $"Do zapłaty: {IssuingFormat.Money(invoice.TotalGross, invoice.Currency)}\n\n" +
            "Korekt nie wystawisz jeszcze w OpenKSeF — użyj Aplikacji Podatnika KSeF.";
        return Shell.Current.DisplayAlertAsync("PRODUKCJA · Wystawić fakturę i wysłać do KSeF?", message, "Wystaw i wyślij", "Wróć do edycji");
    }

    private async Task<IssuedInvoiceDto?> SaveAsync()
    {
        if (!Guid.TryParse(TenantId, out var tenantId))
            return null;

        ClearErrors();
        if (!Lines.All(l => l.Validate()))
            return null;

        try
        {
            IsBusy = true;
            var request = BuildRequest();
            var saved = Guid.TryParse(InvoiceId, out var id)
                ? await _apiService.UpdateIssuedInvoiceAsync(tenantId, id, request)
                : await _apiService.CreateIssuedInvoiceAsync(tenantId, request);
            _loaded = true;
            InvoiceId = saved.Id.ToString();
            SavedAt = $"Zapisano {DateTime.Now:HH:mm}";
            return saved;
        }
        catch (ApiException ex) when (ex.Validation is { Errors.Count: > 0 } validation)
        {
            ApplyValidation(validation);
            return null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
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
        BuyerNipError = BuyerNameError = BuyerAddressError = DueDateError = PaymentError = ExemptionError = LinesError = ErrorMessage = null;
        foreach (var line in Lines)
            line.Error = null;
    }

    internal void ApplyValidation(ValidationEnvelope validation)
    {
        var unplaced = new List<string>();
        foreach (var issue in validation.Errors)
        {
            var field = issue.Field ?? string.Empty;
            switch (field)
            {
                case "buyer.nip": BuyerNipError = issue.Message; break;
                case "buyer.name": BuyerNameError = issue.Message; break;
                case "buyer.addressLine1" or "buyer.addressLine2": BuyerAddressError = issue.Message; break;
                case "dueDate" or "issueDate" or "saleDate": DueDateError = issue.Message; break;
                case "paymentMethod" or "splitPayment": PaymentError = issue.Message; break;
                case "exemptionBasis": ExemptionError = issue.Message; break;
                case "lines": LinesError = issue.Message; break;
                default:
                    if (LineIndex(field) is { } i && i < Lines.Count)
                        Lines[i].Error = issue.Message;
                    else
                        unplaced.Add(issue.Message);
                    break;
            }
        }

        ErrorMessage = string.Join("\n", ["Popraw błędy zaznaczone w formularzu.", .. unplaced]);
    }

    private static int? LineIndex(string field) =>
        field.StartsWith("lines[") && field.IndexOf(']') is var end and > 6 && int.TryParse(field[6..end], out var i) ? i : null;
}
