using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class AccountPage : ContentPage
{
    private readonly AccountViewModel _viewModel;
    private bool _isLoadingSettings;

    public AccountPage(AccountViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _viewModel = vm;
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

    private void OnNotificationToggled(object? sender, ToggledEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        _viewModel.ToggleNotificationsCommand.Execute(null);
    }
}
