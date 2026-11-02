using System.Text.Json.Serialization;
using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class IssuedInvoiceDetailsPage : ContentPage
{
    private readonly IssuedInvoiceDetailsViewModel _viewModel;
    private readonly TaskCompletionSource _pdfHostReady = new();
    private HybridWebView? _pdfHost;

    public IssuedInvoiceDetailsPage(IssuedInvoiceDetailsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.RenderPdf = RenderPdfAsync;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IssuedInvoiceDetailsViewModel.CanCopyAsNew))
                SyncCopyToolbarItem();
        };
        SyncCopyToolbarItem();
    }

    // ToolbarItem has no IsVisible in MAUI 10.0.20, so hiding means removing it.
    private void SyncCopyToolbarItem()
    {
        if (!_viewModel.CanCopyAsNew)
            ToolbarItems.Remove(CopyToolbarItem);
        else if (!ToolbarItems.Contains(CopyToolbarItem))
            ToolbarItems.Add(CopyToolbarItem);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Stop();
    }

    private async Task<byte[]> RenderPdfAsync(byte[] xml, string ksefNumber, string verificationUrl)
    {
        if (_pdfHost is null)
        {
            _pdfHost = new HybridWebView
            {
                HybridRoot = "pdf",
                DefaultFile = "index.html",
                WidthRequest = 1,
                HeightRequest = 1,
                Opacity = 0,
                InputTransparent = true,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Start
            };
            _pdfHost.RawMessageReceived += (_, _) => _pdfHostReady.TrySetResult();
            PageRoot.Children.Add(_pdfHost);
        }

        await _pdfHostReady.Task;
        var text = PdfHostJsonContext.Default.String;
        var base64 = await _pdfHost.InvokeJavaScriptAsync(
            "renderInvoicePdf",
            text,
            [Convert.ToBase64String(xml), ksefNumber, verificationUrl],
            [text, text, text]);
        return Convert.FromBase64String(base64!);
    }
}

[JsonSerializable(typeof(string))]
internal partial class PdfHostJsonContext : JsonSerializerContext;
