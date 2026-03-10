using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class TenantFormPage : ContentPage
{
    public TenantFormPage(TenantFormViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
