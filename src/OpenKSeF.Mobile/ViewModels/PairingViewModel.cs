using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class PairingViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IServerSettingsService _serverSettings;
    private readonly IPostLoginNavigationService _postLoginNav;
    private CancellationTokenSource? _cts;

    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    [ObservableProperty]
    private string? _userCode;

    [ObservableProperty]
    private string? _countdownText;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canRequestNewCode;

    public PairingViewModel(IAuthService authService, IServerSettingsService serverSettings, IPostLoginNavigationService postLoginNav)
    {
        _authService = authService;
        _serverSettings = serverSettings;
        _postLoginNav = postLoginNav;
    }

    public async Task StartAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        UserCode = null;
        CountdownText = null;
        ErrorMessage = null;
        CanRequestNewCode = false;
        IsBusy = true;

        try
        {
            var (result, pairing) = await _authService.StartPairingAsync(cts.Token);
            if (pairing is not null)
            {
                UserCode = pairing.UserCode;
                _ = CountdownAsync(pairing.ExpiresAt, cts.Token);
                result = await _authService.CompletePairingAsync(pairing, cts.Token);
            }

            if (result == AuthResult.Success)
            {
                await _postLoginNav.NavigateAsync();
                return;
            }

            ShowFailure(result);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ShowFailure(AuthResult.Rejected);
            ErrorMessage = $"Błąd parowania: {ex.Message}";
        }
        finally
        {
            cts.Cancel();
            if (_cts == cts)
                IsBusy = false;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    [RelayCommand]
    private Task NewCodeAsync() => StartAsync();

    [RelayCommand]
    private async Task CancelAsync()
    {
        Stop();
        try { await Shell.Current.GoToAsync(".."); } catch { }
    }

    private void ShowFailure(AuthResult result)
    {
        UserCode = null;
        CountdownText = null;
        CanRequestNewCode = result is not (AuthResult.PairingNotSupported or AuthResult.ClientRejected);
        ErrorMessage = result switch
        {
            AuthResult.PairingExpired => "Kod wygasł. Poproś o nowy kod.",
            AuthResult.PairingDenied => "Odrzucono parowanie w portalu.",
            AuthResult.PairingNotSupported => "Ten serwer nie obsługuje parowania kodem. Wróć i zaloguj się przyciskiem Zaloguj się.",
            AuthResult.ClientRejected => $"Serwer {ServerUrlRules.DisplayHost(_serverSettings.ServerUrl)} odrzucił aplikację mobilną. Skontaktuj się z administratorem serwera.",
            AuthResult.ServerUnreachable => $"Nie można połączyć się z serwerem {ServerUrlRules.DisplayHost(_serverSettings.ServerUrl)}. Sprawdź połączenie z siecią.",
            _ => "Parowanie nie powiodło się."
        };
    }

    private async Task CountdownAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var left = expiresAt - Clock.GetUtcNow();
                if (left < TimeSpan.Zero)
                    left = TimeSpan.Zero;

                CountdownText = $"Kod ważny jeszcze {(int)left.TotalMinutes}:{left.Seconds:00}";
                if (left == TimeSpan.Zero)
                    return;

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
