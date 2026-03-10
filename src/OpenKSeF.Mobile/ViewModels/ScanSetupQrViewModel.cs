using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;

namespace OpenKSeF.Mobile.ViewModels;

public partial class ScanSetupQrViewModel : ObservableObject
{
    private readonly IServerSettingsService _serverSettings;
    private readonly IAuthService _authService;
    private readonly IApiService _apiService;
    private readonly IDeviceTokenService _deviceTokenService;
    private bool _processed;

    [ObservableProperty]
    private string _statusText = "Skieruj kamerę na kod QR";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public ScanSetupQrViewModel(
        IServerSettingsService serverSettings,
        IAuthService authService,
        IApiService apiService,
        IDeviceTokenService deviceTokenService)
    {
        _serverSettings = serverSettings;
        _authService = authService;
        _apiService = apiService;
        _deviceTokenService = deviceTokenService;
    }

    public async Task ProcessBarcodeAsync(string rawValue)
    {
        if (_processed || IsBusy)
            return;

        _processed = true;
        IsBusy = true;
        StatusText = "Przetwarzanie kodu QR...";
        ErrorMessage = null;

        try
        {
            var payload = ParsePayload(rawValue);
            if (payload is null)
            {
                ErrorMessage = "Nieprawidłowy kod QR. Użyj kodu z portalu OpenKSeF.";
                _processed = false;
                return;
            }

            if (!_serverSettings.TryUpdateServerUrl(payload.ServerUrl, out var normalizedUrl, out var validationError))
            {
                ErrorMessage = validationError;
                _processed = false;
                return;
            }

            if (!string.IsNullOrEmpty(payload.SetupToken))
            {
                var redeemed = await _authService.RedeemSetupTokenAsync(normalizedUrl, payload.SetupToken);
                if (redeemed)
                {
                    StatusText = "Zalogowano automatycznie!";
                    await NavigateAfterAutoLogin();
                    return;
                }

                ErrorMessage = "Token wygasł lub jest nieprawidłowy. Zaloguj się ręcznie.";
            }

            StatusText = $"Serwer ustawiony: {normalizedUrl}";
            await Shell.Current.GoToAsync("//login");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Błąd: {ex.Message}";
            _processed = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task NavigateAfterAutoLogin()
    {
        bool needsOnboarding = false;

        try
        {
            var status = await _apiService.GetOnboardingStatusAsync();
            needsOnboarding = !status.IsComplete;
        }
        catch
        {
        }

        if (!needsOnboarding)
        {
            try { await _deviceTokenService.EnsureDeviceRegisteredAsync(); } catch { }
        }

        if (needsOnboarding)
        {
            await Shell.Current.GoToAsync("//onboarding");
        }
        else
        {
            await Shell.Current.GoToAsync("//main/invoices");
        }
    }

    [RelayCommand]
    private async Task SkipAsync()
    {
        _serverSettings.MarkAsConfigured();
        await Shell.Current.GoToAsync("//login");
    }

    [RelayCommand]
    private async Task PickFromGalleryAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Wybierz obraz kodu QR",
                FileTypes = FilePickerFileType.Images
            });

            if (result is null)
                return;

            await using var stream = await result.OpenReadAsync();
            var bytes = new byte[stream.Length];
            _ = await stream.ReadAsync(bytes);

            var reader = new ZXing.BarcodeReaderGeneric();
            var luminanceSource = new ZXing.RGBLuminanceSource(bytes, 1, 1);

            // ZXing can't decode raw file bytes directly; use the ImageSharp/SkiaSharp approach
            // For now, fall back to a simpler decode via ZXing.Net MAUI built-in
            // The gallery pick + decode is best handled by reading the image properly
            var decoded = DecodeQrFromImageBytes(bytes);
            if (decoded is not null)
            {
                await ProcessBarcodeAsync(decoded);
            }
            else
            {
                ErrorMessage = "Nie udało się odczytać kodu QR z obrazu.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Błąd wyboru pliku: {ex.Message}";
        }
    }

    public void Reset()
    {
        _processed = false;
        IsBusy = false;
        ErrorMessage = null;
        StatusText = "Skieruj kamerę na kod QR";
    }

    private static QrSetupPayload? ParsePayload(string rawValue)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<QrSetupPayload>(rawValue);
            return payload?.IsValid == true ? payload : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? DecodeQrFromImageBytes(byte[] imageBytes)
    {
        try
        {
            using var ms = new MemoryStream(imageBytes);
#if ANDROID
            var bitmap = Android.Graphics.BitmapFactory.DecodeStream(ms);
            if (bitmap is null) return null;

            var pixels = new int[bitmap.Width * bitmap.Height];
            bitmap.GetPixels(pixels, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);

            var bytes = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                bytes[i * 4] = (byte)((pixels[i] >> 16) & 0xFF); // R
                bytes[i * 4 + 1] = (byte)((pixels[i] >> 8) & 0xFF); // G
                bytes[i * 4 + 2] = (byte)(pixels[i] & 0xFF); // B
                bytes[i * 4 + 3] = (byte)((pixels[i] >> 24) & 0xFF); // A
            }

            var source = new ZXing.RGBLuminanceSource(bytes, bitmap.Width, bitmap.Height, ZXing.RGBLuminanceSource.BitmapFormat.RGBA32);
            var reader = new ZXing.BarcodeReaderGeneric();
            var result = reader.Decode(source);
            return result?.Text;
#elif IOS
            return null;
#else
            return null;
#endif
        }
        catch
        {
            return null;
        }
    }
}
