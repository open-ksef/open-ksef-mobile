using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class IssuedInvoiceDetailsPage : ContentPage
{
    private readonly IssuedInvoiceDetailsViewModel _viewModel;

    public IssuedInvoiceDetailsPage(IssuedInvoiceDetailsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Stop();
    }
}
