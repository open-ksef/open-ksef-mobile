#if ANDROID
using Microsoft.Maui.Platform;
#endif
using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views;

public partial class InvoiceLineSheet : ContentPage
{
    private readonly InvoiceDraftViewModel _viewModel;

    public InvoiceLineSheet(InvoiceDraftViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
#if ANDROID
        Loaded += (_, _) =>
        {
            ApplyStatusBarStyle();
            Application.Current!.RequestedThemeChanged += OnThemeChanged;
        };
        Unloaded += (_, _) => Application.Current!.RequestedThemeChanged -= OnThemeChanged;
#endif
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

#if ANDROID
    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => ApplyStatusBarStyle();

    private static void ApplyStatusBarStyle()
    {
        if (Platform.CurrentActivity is not AndroidX.Fragment.App.FragmentActivity activity
            || activity.SupportFragmentManager.Fragments.OfType<AndroidX.Fragment.App.DialogFragment>().LastOrDefault()?.Dialog?.Window is not { } window)
            return;
        var dark = Application.Current!.RequestedTheme == AppTheme.Dark;
#pragma warning disable CA1422
        window.SetStatusBarColor(((Color)Application.Current.Resources[dark ? "UiSurfaceDark" : "UiSurface"]).ToPlatform());
#pragma warning restore CA1422
        AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView).AppearanceLightStatusBars = !dark;
    }
#endif
}
