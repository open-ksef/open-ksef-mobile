using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class InvoiceLineSheet : ContentPage
{
    private readonly InvoiceDraftViewModel _viewModel;

    public InvoiceLineSheet(InvoiceDraftViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
#if ANDROID
        Loaded += (_, _) => ApplyStatusBarStyle();
#endif
    }

    protected override bool OnBackButtonPressed()
    {
        _viewModel.CloseLineCommand.Execute(null);
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.CloseLineCommand.Execute(null);
    }

#if ANDROID
    private void ApplyStatusBarStyle()
    {
        if (Application.Current!.RequestedTheme == AppTheme.Dark || Handler?.PlatformView is not Android.Views.View view)
            return;
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            view.WindowInsetsController?.SetSystemBarsAppearance(
                (int)Android.Views.WindowInsetsControllerAppearance.LightStatusBars,
                (int)Android.Views.WindowInsetsControllerAppearance.LightStatusBars);
        else
#pragma warning disable CA1422
            view.RootView!.SystemUiFlags |= Android.Views.SystemUiFlags.LightStatusBar;
#pragma warning restore CA1422
    }
#endif
}
