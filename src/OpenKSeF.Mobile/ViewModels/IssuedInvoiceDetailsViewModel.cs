using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using QRCoder;

namespace OpenKSeF.Mobile.ViewModels;

public sealed record IssuedLineRow(string Name, string Detail, string Gross);

[QueryProperty(nameof(TenantId), "tenantId")]
[QueryProperty(nameof(InvoiceId), "invoiceId")]
public partial class IssuedInvoiceDetailsViewModel : ObservableObject
{
    public static readonly TimeSpan FastPoll = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan SlowPoll = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan FastPollWindow = TimeSpan.FromMinutes(2);

    private readonly IApiService _apiService;
    private readonly IServerSettingsService _serverSettings;
    private const string NoConnection = "Brak połączenia z serwerem. Status faktury pojawi się tu po powrocie sieci. Nie wystawiaj jej drugi raz.";
    private const string ChangedMessage = "Ta faktura została zmieniona w innym miejscu. Sprawdź aktualne dane i wybierz „Wystaw i wyślij” jeszcze raz.";

    private CancellationTokenSource? _polling;
    private bool _loadFailedOnNetwork;
    private bool _loadUnauthorized;

    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;
    internal TimeSpan PdfTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public Func<byte[], string, string, Task<byte[]>>? RenderPdf { get; set; }

    [ObservableProperty]
    private string? _tenantId;

    [ObservableProperty]
    private string? _invoiceId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(Title), nameof(IsDraft), nameof(IsSending), nameof(IsAccepted), nameof(IsRejected),
        nameof(GrossText), nameof(BuyerText), nameof(Lines), nameof(KsefNumber), nameof(AcceptedText), nameof(RejectionText),
        nameof(SendingProblemText), nameof(CanCopyAsNew), nameof(IsDuplicateNumberRejection), nameof(CanFixRejection), nameof(Environment), nameof(DatesText), nameof(HasUpo), nameof(HasVerificationLink),
        nameof(IsPortalOnly), nameof(IsEditableDraft), nameof(CanCopyRejected), nameof(CanOpenInPortal), nameof(PortalButtonText), nameof(ForeignVatText))]
    private IssuedInvoiceDto? _invoice;

    [ObservableProperty]
    private byte[]? _verificationQr;

    [ObservableProperty]
    private bool _isPdfFallback;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public IssuedInvoiceDetailsViewModel(IApiService apiService, IServerSettingsService serverSettings)
    {
        _apiService = apiService;
        _serverSettings = serverSettings;
    }

    public string? Status => Invoice?.Status;
    public string Title => Invoice?.Number ?? "Szkic faktury";
    public bool IsDraft => Status == "Draft";
    public bool IsSending => IssuingFormat.IsSending(Status);
    public bool IsAccepted => Status == "Accepted";
    public bool IsRejected => Status == "Rejected";
    public bool IsPortalOnly => Invoice is not null && IssuingFormat.IsPortalOnly(Invoice);
    public bool IsEditableDraft => IsDraft && !IsPortalOnly;
    public bool CanCopyAsNew => Invoice is not null && !IsSending && !IsPortalOnly;
    public bool CanCopyRejected => IsDuplicateNumberRejection && !IsPortalOnly;
    public bool CanOpenInPortal => IsPortalOnly && (IsDraft || IsRejected);
    public string PortalButtonText => IsDuplicateNumberRejection ? "Otwórz w portalu" : "Edytuj w portalu";
    public bool IsDuplicateNumberRejection => IsRejected && Invoice!.Ksef.StatusCode == 440;
    public bool CanFixRejection => IsRejected && !IsDuplicateNumberRejection && !IsPortalOnly;
    public bool HasUpo => IsAccepted && Invoice!.Ksef.HasUpo;
    public bool HasVerificationLink => Uri.TryCreate(Invoice?.Ksef.VerificationUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    public string? Environment => Invoice?.Ksef.Environment;
    public string GrossText => Invoice is null ? string.Empty : IssuingFormat.Money(Invoice.TotalGross, Invoice.Currency);
    public string KsefNumber => Invoice?.Ksef.KsefNumber ?? string.Empty;

    public string BuyerText => Invoice is null ? string.Empty
        : string.Join("\n", new[] { Invoice.Buyer.Name, Invoice.Buyer.Nip is { Length: > 0 } nip ? $"NIP {nip}" : null, Invoice.Buyer.ForeignId is { Length: > 0 } id ? $"Nr podatkowy {id}" : null, Invoice.Buyer.AddressLine1, Invoice.Buyer.AddressLine2, Invoice.Buyer.CountryCode is { Length: > 0 } country ? $"Kraj {country}" : null }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    public string? ForeignVatText => Invoice is { Buyer.Kind: "Foreign", VatSummary.Count: > 0 }
        ? $"Stawka {string.Join(", ", Invoice.VatSummary.Select(s => IssuingFormat.VatRateLabel(s.VatRate)))} · VAT {IssuingFormat.Money(Invoice.TotalVat, Invoice.Currency)}"
        : null;

    public string DatesText => Invoice is null ? string.Empty
        : $"Wystawiona {IssuingFormat.Date(Invoice.IssueDate)} · termin {IssuingFormat.Date(Invoice.DueDate)}";

    public IReadOnlyList<IssuedLineRow> Lines => Invoice?.Lines.Select(l => new IssuedLineRow(
        l.Name,
        $"{IssuingFormat.Quantity(l.Quantity)} × {IssuingFormat.Money(l.UnitNetPrice, Invoice.Currency)} netto · VAT {IssuingFormat.VatRateLabel(l.VatRate)}",
        IssuingFormat.Money(l.NetAmount + l.VatAmount, Invoice.Currency))).ToList() ?? [];

    public string AcceptedText => Invoice?.Ksef.AcceptedAt is { } at ? $"Przyjęta {IssuingFormat.DateAndTime(at.ToLocalTime())}" : string.Empty;

    public string RejectionText => Invoice is null ? string.Empty
        : $"Kod KSeF {Invoice.Ksef.StatusCode}: {RejectionMessage}{(RejectionDetails is { } details ? $"\n{details}" : string.Empty)}\n{RejectionAdvice}";

    private string RejectionMessage => IssuingFormat.CleanKsefDetails(Invoice!.Ksef.StatusMessage) ?? "KSeF odrzucił fakturę.";

    private string? RejectionDetails => IsDuplicateNumberRejection && Invoice!.Ksef.DuplicateKsefNumber is { Length: > 0 }
        ? null
        : IssuingFormat.CleanKsefDetails(Invoice!.Ksef.StatusDetails);

    private string RejectionAdvice => !IsDuplicateNumberRejection
        ? $"Numer {Invoice!.Number} zostaje — po poprawce wyślemy fakturę z tym samym numerem."
        : $"Numer {Invoice!.Number} jest już zajęty w KSeF, więc tej faktury nie wyślemy ponownie. Użyj „Kopiuj jako nową”{(IsPortalOnly ? " w portalu" : string.Empty)}."
            + (Invoice.Ksef.DuplicateKsefNumber is { Length: > 0 } duplicate ? $"\nNumer KSeF istniejącego dokumentu: {duplicate}" : string.Empty);

    public string? SendingProblemText => IsSending && Invoice?.Ksef.LastError is { Length: > 0 } lastError
        ? $"{lastError}\nNie wysyłaj faktury drugi raz."
        : null;

    partial void OnInvoiceChanged(IssuedInvoiceDto? value)
    {
        VerificationQr = value?.Ksef.VerificationUrl is { Length: > 0 } url ? QrPng(url) : null;
    }

    public void Start()
    {
        Stop();
        _polling = new CancellationTokenSource();
        _ = RunAsync(_polling.Token);
    }

    public void Stop()
    {
        _polling?.Cancel();
        _polling = null;
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        await LoadAsync();
        var waited = TimeSpan.Zero;
        while (!ct.IsCancellationRequested && !_loadUnauthorized && (IsSending || _loadFailedOnNetwork))
        {
            var step = waited < FastPollWindow ? FastPoll : SlowPoll;
            try { await Delay(step, ct); } catch (OperationCanceledException) { return; }
            waited += step;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (!Guid.TryParse(TenantId, out var tenantId) || !Guid.TryParse(InvoiceId, out var invoiceId))
            return;

        try
        {
            IsBusy = Invoice is null;
            Invoice = await _apiService.GetIssuedInvoiceAsync(tenantId, invoiceId);
            ErrorMessage = null;
            _loadFailedOnNetwork = false;
            _loadUnauthorized = false;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _loadFailedOnNetwork = false;
            _loadUnauthorized = true;
            ErrorMessage = Invoice is null ? $"Nie udało się załadować faktury: {ex.Message}" : ex.Message;
        }
        catch (Exception ex)
        {
            _loadFailedOnNetwork = ApiException.IsNetworkFailure(ex);
            _loadUnauthorized = false;
            if (Invoice is null)
                ErrorMessage = _loadFailedOnNetwork ? NoConnection : $"Nie udało się załadować faktury: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        try
        {
            Invoice = await _apiService.GetIssuedInvoiceAsync(Guid.Parse(TenantId!), Invoice!.Id);
            if (IsPortalOnly)
                return;

            await Shell.Current.GoToAsync($"../invoiceDraft?tenantId={TenantId}&invoiceId={InvoiceId}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nie udało się załadować faktury: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var shown = Invoice;
        if (!IsEditableDraft || !await Shell.Current.DisplayAlertAsync("Usuń szkic", "Usunąć szkic faktury?", "Usuń", "Anuluj"))
            return;

        try
        {
            await _apiService.DeleteIssuedInvoiceAsync(Guid.Parse(TenantId!), shown!.Id, shown.Version);
            try { await Shell.Current.GoToAsync(".."); } catch { }
        }
        catch (Exception ex)
        {
            await LoadAsync();
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task IssueAsync()
    {
        if (Invoice is null || !Guid.TryParse(TenantId, out var tenantId))
            return;

        var displayed = Invoice.Version;
        try
        {
            var profile = await _apiService.GetSellerProfileAsync(tenantId);
            var shown = await _apiService.GetIssuedInvoiceAsync(tenantId, Invoice.Id);
            Invoice = shown;
            if (IsPortalOnly || !IsDraft && !CanFixRejection)
            {
                if (IsSending)
                    Start();
                return;
            }

            if (shown.Version != displayed)
            {
                await Shell.Current.DisplayAlertAsync("Faktura zmieniona", ChangedMessage, "OK");
                return;
            }

            if (!await InvoiceDraftViewModel.ConfirmIssueAsync(profile?.NextNumberPreview, shown))
                return;

            Invoice = await _apiService.IssueInvoiceAsync(tenantId, shown.Id, shown.Version);
            Start();
        }
        catch (ApiException ex) when (ex.Validation is { Errors.Count: > 0 } validation)
        {
            await Shell.Current.DisplayAlertAsync("Popraw fakturę", string.Join("\n", validation.Errors.Select(e => e.Message)), "OK");
            await EditAsync();
        }
        catch (Exception ex) when (ApiException.IsNetworkFailure(ex))
        {
            ErrorMessage = NoConnection;
            Start();
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            await LoadAsync();
            if (IsSending)
                Start();

            if (Invoice?.Version != displayed && IsEditableDraft)
                await Shell.Current.DisplayAlertAsync("Faktura zmieniona", ChangedMessage, "OK");
            else
                await Shell.Current.DisplayAlertAsync("Błąd", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Błąd", ex.Message, "OK");
        }
    }

    [RelayCommand]
    private async Task CopyAsNewAsync()
    {
        if (!CanCopyAsNew || !Guid.TryParse(TenantId, out var tenantId))
            return;

        try
        {
            var copy = await _apiService.CopyIssuedInvoiceAsync(tenantId, Invoice!.Id);
            try { await Shell.Current.GoToAsync($"../invoiceDraft?tenantId={tenantId}&invoiceId={copy.Id}"); } catch { }
        }
        catch (ApiException ex) when (!ApiException.IsNetworkFailure(ex) && ex.StatusCode is not (>= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices))
        {
            await Shell.Current.DisplayAlertAsync("Błąd", ex.Message, "OK");
        }
        catch (Exception)
        {
            await Shell.Current.DisplayAlertAsync("Nie wiadomo, czy zapisano szkic", InvoiceDraftViewModel.CheckIssuedList, "OK");
            try { await Shell.Current.GoToAsync(InvoiceDraftViewModel.IssuedList); } catch { }
        }
    }

    [RelayCommand]
    private async Task CopyKsefNumberAsync()
    {
        if (KsefNumber.Length > 0)
            await Clipboard.Default.SetTextAsync(KsefNumber);
    }

    [RelayCommand]
    private async Task SharePdfAsync()
    {
        if (!IsAccepted || !Guid.TryParse(TenantId, out var tenantId))
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            IsPdfFallback = false;
            var xml = await _apiService.GetIssuedInvoiceXmlAsync(tenantId, Invoice!.Id);
            var pdf = await TryRenderPdfAsync(xml);
            if (pdf is null)
            {
                IsPdfFallback = true;
                return;
            }

            await ShareFileAsync(pdf, ".pdf", "application/pdf", "Faktura PDF");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nie udało się przygotować PDF: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task ShareXmlAsync() => IsAccepted
        ? ShareDocumentAsync(_apiService.GetIssuedInvoiceXmlAsync, ".xml", "Faktura XML")
        : Task.CompletedTask;

    [RelayCommand]
    private Task ShareUpoAsync() => HasUpo
        ? ShareDocumentAsync(_apiService.GetIssuedInvoiceUpoAsync, "_UPO.xml", "UPO")
        : Task.CompletedTask;

    [RelayCommand]
    private async Task OpenVerificationAsync()
    {
        if (HasVerificationLink)
            await Browser.Default.OpenAsync(Invoice!.Ksef.VerificationUrl!, BrowserLaunchMode.SystemPreferred);
    }

    [RelayCommand]
    private async Task OpenInPortalAsync()
    {
        if (CanOpenInPortal)
            await Browser.Default.OpenAsync(
                $"{_serverSettings.ServerUrl}/issued/{Invoice!.Id}{(IsDuplicateNumberRejection ? string.Empty : "/edit")}?tenantId={TenantId}",
                BrowserLaunchMode.SystemPreferred);
    }

    private async Task<byte[]?> TryRenderPdfAsync(byte[] xml)
    {
        if (RenderPdf is null || Invoice?.Ksef.VerificationUrl is not { Length: > 0 } url || KsefNumber.Length == 0)
            return null;

        try
        {
            return await RenderPdf(xml, KsefNumber, url).WaitAsync(PdfTimeout);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PDF render failed: {ex}");
            return null;
        }
    }

    private async Task ShareDocumentAsync(Func<Guid, Guid, Task<byte[]>> download, string suffix, string title)
    {
        if (!Guid.TryParse(TenantId, out var tenantId))
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await ShareFileAsync(await download(tenantId, Invoice!.Id), suffix, "application/xml", title);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nie udało się pobrać pliku: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ShareFileAsync(byte[] content, string suffix, string contentType, string title)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, IssuingFormat.FileStem(Invoice!.Number ?? Invoice.Id.ToString()) + suffix);
        await File.WriteAllBytesAsync(path, content);
        await Share.Default.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path, contentType) });
    }

    internal static byte[] QrPng(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(10);
    }
}
