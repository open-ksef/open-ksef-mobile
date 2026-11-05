using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class InvoiceDraftPage : ContentPage
{
    private readonly InvoiceDraftViewModel _viewModel;

    public InvoiceDraftPage(InvoiceDraftViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        var display = DeviceDisplay.Current.MainDisplayInfo;
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.UseWindowSoftInputModeAdjust(
            Microsoft.Maui.Controls.Application.Current!.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>(),
            // Below 720 dp the tab bar and action row above the keyboard leave no room for the focused field.
            display.Height / display.Density >= 720 ? Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize : Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Pan);
#endif
    }

    // A Shell tab switch raises NavigatedFrom/To but not Disappearing/Appearing on the pushed draft page.
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _viewModel.IsShown = true;
        _viewModel.LoadCommand.Execute(null);
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        _viewModel.IsShown = false;
    }

#if ANDROID
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.UseWindowSoftInputModeAdjust(
            Microsoft.Maui.Controls.Application.Current!.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>(),
            Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Pan);
    }
#endif
}
