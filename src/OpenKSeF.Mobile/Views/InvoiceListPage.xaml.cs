using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class InvoiceListPage : ContentPage
{
    private readonly InvoiceListViewModel _viewModel;

    public InvoiceListPage(InvoiceListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadInvoicesCommand.Execute(null);
    }

    private async void OnGoToTenantsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//main/tenants");
    }
}
