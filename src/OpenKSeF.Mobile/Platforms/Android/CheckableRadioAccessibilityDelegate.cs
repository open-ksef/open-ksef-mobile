using AView = Android.Views.View;
using AndroidX.Core.View;
using AndroidX.Core.View.Accessibility;
using Microsoft.Maui.Platform;

namespace OpenKSeF.Mobile;

/// <summary>A RadioButton with a ControlTemplate renders as a plain ViewGroup, so TalkBack and the UI tree cannot see its checked state without this.</summary>
public class CheckableRadioAccessibilityDelegate(AccessibilityDelegateCompat? original, IRadioButton radio)
    : AccessibilityDelegateCompatWrapper(original)
{
    // AccessibilityNodeInfoCompat.CHECKED_STATE_TRUE / CHECKED_STATE_FALSE; the binding does not expose them.
    private const int CheckedStateTrue = 1;
    private const int CheckedStateFalse = 0;

    public override void OnInitializeAccessibilityNodeInfo(AView? host, AccessibilityNodeInfoCompat? info)
    {
        base.OnInitializeAccessibilityNodeInfo(host, info);
        if (info is null)
            return;

        info.ClassName = "android.widget.RadioButton";
        info.Checkable = true;
        info.Checked = radio.IsChecked ? CheckedStateTrue : CheckedStateFalse;
    }
}
