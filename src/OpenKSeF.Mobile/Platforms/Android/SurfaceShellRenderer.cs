using Google.Android.Material.AppBar;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using Microsoft.Maui.Platform;
using AToolbar = AndroidX.AppCompat.Widget.Toolbar;

namespace OpenKSeF.Mobile;

/// <summary>
/// Paints the Shell AppBarLayout with the toolbar colour. On edge-to-edge (API 35+) its top padding
/// sits under the status bar, MAUI colours only the toolbar, and the padding shows ?attr/colorPrimary.
/// </summary>
public class SurfaceShellRenderer : ShellRenderer
{
    protected override IShellToolbarAppearanceTracker CreateToolbarAppearanceTracker() => new Tracker(this);

    private sealed class Tracker(IShellContext shellContext) : ShellToolbarAppearanceTracker(shellContext)
    {
        protected override void SetColors(AToolbar toolbar, IShellToolbarTracker toolbarTracker, Color foreground, Color background, Color title)
        {
            base.SetColors(toolbar, toolbarTracker, foreground, background, title);

            if (background is not null && toolbar.Parent is AppBarLayout appBar)
            {
                appBar.SetBackgroundColor(background.ToPlatform());
            }
        }
    }
}
