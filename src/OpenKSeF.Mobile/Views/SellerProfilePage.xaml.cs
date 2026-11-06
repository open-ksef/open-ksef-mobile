using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class SellerProfilePage : ContentPage
{
    public SellerProfilePage(SellerProfileViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

#if ANDROID
    protected override void OnAppearing()
    {
        base.OnAppearing();
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.UseWindowSoftInputModeAdjust(
            Microsoft.Maui.Controls.Application.Current!.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>(),
            Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.UseWindowSoftInputModeAdjust(
            Microsoft.Maui.Controls.Application.Current!.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>(),
            Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Pan);
    }
#endif
}
