using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;

namespace OpenKSeF.Mobile.Services;

/// <summary>Confirms a clipboard copy with a haptic click, a screen reader announcement and a toast; Android 13+ shows its own clipboard bubble, so no toast there.</summary>
public static class CopyFeedback
{
    public static async Task ShowAsync(string message, bool longPress = false)
    {
        try
        {
            HapticFeedback.Default.Perform(longPress ? HapticFeedbackType.LongPress : HapticFeedbackType.Click);
        }
        catch (FeatureNotSupportedException)
        {
        }

        SemanticScreenReader.Announce(message);

        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            await Toast.Make(message, ToastDuration.Short).Show();
    }
}
