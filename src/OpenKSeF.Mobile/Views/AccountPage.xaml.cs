using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class AccountPage : ContentPage
{
    private readonly AccountViewModel _viewModel;
    private bool _isLoadingSettings;

    public AccountPage(AccountViewModel vm)
    {
        _viewModel = vm;
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isLoadingSettings = true;
        try
        {
            await _viewModel.LoadSettingsAsync();
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Unload();
    }

    private void OnNotificationToggled(object? sender, ToggledEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        _viewModel.ToggleNotificationsCommand.Execute(e.Value);
    }

    private void OnAppLockToggled(object? sender, ToggledEventArgs e) =>
        _viewModel.ToggleAppLockCommand.Execute(e.Value);
}
