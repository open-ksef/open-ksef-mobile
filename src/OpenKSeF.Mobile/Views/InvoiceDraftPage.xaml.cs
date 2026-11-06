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

    // A Shell tab switch raises NavigatedFrom/To but not Disappearing/Appearing on the pushed draft page.
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
#if ANDROID
        var display = DeviceDisplay.Current.MainDisplayInfo;
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.UseWindowSoftInputModeAdjust(
            Microsoft.Maui.Controls.Application.Current!.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>(),
            // Below 720 dp the tab bar and action row above the keyboard leave no room for the focused field.
            display.Height / display.Density >= 720 ? Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize : Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Pan);
#endif
        _viewModel.IsShown = true;
        _viewModel.LoadCommand.Execute(null);
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
#if ANDROID
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.UseWindowSoftInputModeAdjust(
            Microsoft.Maui.Controls.Application.Current!.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>(),
            Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Pan);
#endif
        _viewModel.IsShown = false;
    }
}
