using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class LockViewModel : ObservableObject
{
    private readonly IAppLockService _appLock;
    private readonly IPostLoginNavigationService _session;

    [ObservableProperty]
    private bool _isBusy;

    public LockViewModel(IAppLockService appLock, IPostLoginNavigationService session)
    {
        _appLock = appLock;
        _session = session;
    }

    [RelayCommand]
    private Task UnlockAsync() => _appLock.UnlockAsync();

    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (IsBusy)
            return;
        if (!await Shell.Current.DisplayAlertAsync("Wyloguj", "Czy na pewno chcesz się wylogować?", "Wyloguj", "Anuluj"))
            return;

        IsBusy = true;
        try
        {
            await _session.SignOutAsync();
        }
        finally
        {
            IsBusy = false;
        }

        Application.Current!.Windows[0].Page = new AppShell();
        _appLock.Release();
    }
}
