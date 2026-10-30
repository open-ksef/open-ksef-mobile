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
    private CancellationTokenSource? _polling;

    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    [ObservableProperty]
    private string? _tenantId;

    [ObservableProperty]
    private string? _invoiceId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(Title), nameof(IsDraft), nameof(IsSending), nameof(IsAccepted), nameof(IsRejected),
        nameof(GrossText), nameof(BuyerText), nameof(Lines), nameof(KsefNumber), nameof(AcceptedText), nameof(RejectionText),
        nameof(KsefUnavailableText), nameof(Environment), nameof(DatesText))]
    private IssuedInvoiceDto? _invoice;

    [ObservableProperty]
    private byte[]? _verificationQr;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public IssuedInvoiceDetailsViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    public string? Status => Invoice?.Status;
    public string Title => Invoice?.Number ?? "Szkic faktury";
    public bool IsDraft => Status == "Draft";
    public bool IsSending => IssuingFormat.IsSending(Status);
    public bool IsAccepted => Status == "Accepted";
    public bool IsRejected => Status == "Rejected";
    public string? Environment => Invoice?.Ksef.Environment;
    public string GrossText => Invoice is null ? string.Empty : IssuingFormat.Money(Invoice.TotalGross, Invoice.Currency);
    public string KsefNumber => Invoice?.Ksef.KsefNumber ?? string.Empty;

    public string BuyerText => Invoice is null ? string.Empty
        : string.Join("\n", new[] { Invoice.Buyer.Name, Invoice.Buyer.Nip is { Length: > 0 } nip ? $"NIP {nip}" : null, Invoice.Buyer.AddressLine1, Invoice.Buyer.AddressLine2 }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    public string DatesText => Invoice is null ? string.Empty
        : $"Wystawiona {IssuingFormat.Date(Invoice.IssueDate)} · termin {IssuingFormat.Date(Invoice.DueDate)}";

    public IReadOnlyList<IssuedLineRow> Lines => Invoice?.Lines.Select(l => new IssuedLineRow(
        l.Name,
        $"{IssuingFormat.Number(l.Quantity)} × {IssuingFormat.Money(l.UnitNetPrice)} netto · VAT {IssuingFormat.VatRateLabel(l.VatRate)}",
        IssuingFormat.Money(l.NetAmount + l.VatAmount, Invoice.Currency))).ToList() ?? [];

    public string AcceptedText => Invoice?.Ksef.AcceptedAt is { } at ? $"Przyjęta {at.ToLocalTime():dd.MM.yyyy, HH:mm}" : string.Empty;

    public string RejectionText => Invoice is null ? string.Empty
        : $"Kod KSeF {Invoice.Ksef.StatusCode}: {Invoice.Ksef.StatusMessage}\nNumer {Invoice.Number} zostaje — po poprawce wyślemy fakturę z tym samym numerem.";

    public string? KsefUnavailableText => IsSending && Invoice?.Ksef.LastError is not null
        ? "KSeF nie odpowiada, ponowimy wysyłkę. Nie wysyłaj faktury drugi raz."
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
        while (!ct.IsCancellationRequested && IsSending)
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
        }
        catch (Exception ex)
        {
            if (Invoice is null)
                ErrorMessage = $"Nie udało się załadować faktury: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        try { await Shell.Current.GoToAsync($"invoiceDraft?tenantId={TenantId}&invoiceId={InvoiceId}"); } catch { }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Invoice is null || !await Shell.Current.DisplayAlertAsync("Usuń szkic", "Usunąć szkic faktury?", "Usuń", "Anuluj"))
            return;

        try
        {
            await _apiService.DeleteIssuedInvoiceAsync(Guid.Parse(TenantId!), Invoice.Id);
            try { await Shell.Current.GoToAsync(".."); } catch { }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task IssueAsync()
    {
        if (Invoice is null || !Guid.TryParse(TenantId, out var tenantId))
            return;

        try
        {
            var profile = await _apiService.GetSellerProfileAsync(tenantId);
            if (!await InvoiceDraftViewModel.ConfirmIssueAsync(profile?.KsefEnvironment ?? Environment, profile?.NextNumberPreview, Invoice))
                return;

            Invoice = await _apiService.IssueInvoiceAsync(tenantId, Invoice.Id);
            Start();
        }
        catch (ApiException ex) when (ex.Validation is { Errors.Count: > 0 } validation)
        {
            ErrorMessage = string.Join("\n", validation.Errors.Select(e => e.Message));
            await EditAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Błąd", ex.Message, "OK");
        }
    }

    [RelayCommand]
    private async Task CopyAsNewAsync()
    {
        if (Invoice is null || !Guid.TryParse(TenantId, out var tenantId))
            return;

        try
        {
            var copy = await _apiService.CopyIssuedInvoiceAsync(tenantId, Invoice.Id);
            try { await Shell.Current.GoToAsync($"../invoiceDraft?tenantId={tenantId}&invoiceId={copy.Id}"); } catch { }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Błąd", ex.Message, "OK");
        }
    }

    [RelayCommand]
    private async Task CopyKsefNumberAsync()
    {
        if (KsefNumber.Length > 0)
            await Clipboard.Default.SetTextAsync(KsefNumber);
    }

    private static byte[] QrPng(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(10);
    }
}
