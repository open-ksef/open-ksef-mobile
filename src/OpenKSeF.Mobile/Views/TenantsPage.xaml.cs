using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class TenantsPage : ContentPage
{
    private readonly TenantsViewModel _viewModel;

    public TenantsPage(TenantsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadTenantsCommand.Execute(null);
    }
}
