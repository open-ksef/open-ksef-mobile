using System.Globalization;
using System.Runtime.CompilerServices;

namespace OpenKSeF.Mobile.Tests;

/// <summary>Runs the suite under the culture named by TEST_CULTURE (e.g. pl-PL, en-US) instead of the host culture.</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Apply()
    {
        if (Environment.GetEnvironmentVariable("TEST_CULTURE") is not { Length: > 0 } name)
            return;

        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
