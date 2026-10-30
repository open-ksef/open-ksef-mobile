using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class IssuedInvoiceListPage : ContentPage
{
    private readonly IssuedInvoiceListViewModel _viewModel;

    public IssuedInvoiceListPage(IssuedInvoiceListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
