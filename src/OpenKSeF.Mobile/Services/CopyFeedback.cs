using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;

namespace OpenKSeF.Mobile.Services;

/// <summary>Confirms a clipboard copy with a best-effort haptic and one spoken confirmation: Android before 13 speaks the toast, Android 13+ shows its own clipboard bubble.</summary>
public static class CopyFeedback
{
    public static async Task ShowAsync(string message, bool longPress = false)
    {
        try
        {
            HapticFeedback.Default.Perform(longPress ? HapticFeedbackType.LongPress : HapticFeedbackType.Click);
        }
        catch (Exception)
        {
        }

        if (OperatingSystem.IsAndroid() && !OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            await Toast.Make(message, ToastDuration.Short).Show();
            return;
        }

        SemanticScreenReader.Announce(message);

        if (!OperatingSystem.IsAndroid())
            await Toast.Make(message, ToastDuration.Short).Show();
    }
}
