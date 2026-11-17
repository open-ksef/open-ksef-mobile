using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class InvoiceLineSheet : ContentPage
{
    private readonly InvoiceDraftViewModel _viewModel;

    public InvoiceLineSheet(InvoiceDraftViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override bool OnBackButtonPressed()
    {
        _viewModel.CloseLineCommand.Execute(null);
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.CloseLineCommand.Execute(null);
    }
}
