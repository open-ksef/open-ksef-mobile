using OpenKSeF.Mobile.ViewModels;
using ZXing.Net.Maui;

namespace OpenKSeF.Mobile.Views;

public partial class ScanSetupQrPage : ContentPage
{
    private readonly ScanSetupQrViewModel _viewModel;

    public ScanSetupQrPage(ScanSetupQrViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        BarcodeReader.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormat.QrCode,
            AutoRotate = true,
            Multiple = false
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Reset();
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var result = e.Results?.FirstOrDefault();
        if (result is null)
            return;

        Dispatcher.DispatchAsync(() => _viewModel.ProcessBarcodeAsync(result.Value));
    }
}
