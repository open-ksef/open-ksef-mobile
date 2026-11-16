namespace OpenKSeF.Mobile.Services;

public interface IPostLoginNavigationService
{
    Task NavigateAsync();
    Task ActivatePushAsync();
    Task RestoreSessionAsync();
    Task SignOutAsync();
    Task HandleSessionEndedAsync();
}
