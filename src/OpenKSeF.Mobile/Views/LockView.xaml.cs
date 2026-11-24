using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class LockView : ContentView
{
    public LockView(LockViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
