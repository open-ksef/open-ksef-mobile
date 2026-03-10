using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class QrCodePage : ContentPage
{
    public QrCodePage(QrCodeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
