using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.UseWindowSoftInputModeAdjust(
            Microsoft.Maui.Controls.Application.Current!.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>(),
            Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize);
#endif
        if (BindingContext is LoginViewModel vm)
        {
            vm.RefreshServerSettings();
            _ = vm.LoadRegistrationAllowedAsync();
            _ = vm.RestoreSessionAsync();
        }
    }

    private VisualElement? _keyboardTarget;

    private void OnLoginFieldFocused(object? sender, FocusEventArgs e) => ShowWithKeyboard(LoginButton);

    private void OnRegisterFieldFocused(object? sender, FocusEventArgs e) => ShowWithKeyboard(RegisterButton);

    private void OnServerFieldFocused(object? sender, FocusEventArgs e) => ShowWithKeyboard(SaveServerButton);

    private void OnFieldUnfocused(object? sender, FocusEventArgs e) => _keyboardTarget = null;

    private void OnScrollSizeChanged(object? sender, EventArgs e)
    {
        if (_keyboardTarget is not null)
            ShowWithKeyboard(_keyboardTarget);
    }

    private void ShowWithKeyboard(VisualElement target)
    {
        _keyboardTarget = target;
        Dispatcher.Dispatch(() => Scroll.ScrollToAsync(target, ScrollToPosition.MakeVisible, true));
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
