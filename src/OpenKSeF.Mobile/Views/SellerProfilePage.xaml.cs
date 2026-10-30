using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class SellerProfilePage : ContentPage
{
    public SellerProfilePage(SellerProfileViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
