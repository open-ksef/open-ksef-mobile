using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class AccountPage : ContentPage
{
    private readonly AccountViewModel _viewModel;

    public AccountPage(AccountViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _viewModel = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadSettingsCommand.Execute(null);
    }

    private void OnNotificationToggled(object? sender, ToggledEventArgs e)
    {
        _viewModel.ToggleNotificationsCommand.Execute(null);
    }
}
