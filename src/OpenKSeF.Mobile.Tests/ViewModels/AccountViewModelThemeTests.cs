using NSubstitute;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class AccountViewModelThemeTests
{
    public AccountViewModelThemeTests()
    {
        Preferences.Default.Remove("app_theme");
        Application.Current!.UserAppTheme = AppTheme.Unspecified;
    }

    private static AccountViewModel NewViewModel() => new(
        Substitute.For<IServerSettingsService>(),
        Substitute.For<IDeviceTokenService>(),
        Substitute.For<INotificationHubService>(),
        Substitute.For<IPostLoginNavigationService>());

    [Theory]
    [InlineData("System", AppTheme.Unspecified)]
    [InlineData("Light", AppTheme.Light)]
    [InlineData("Dark", AppTheme.Dark)]
    public void ToAppTheme_MapsEveryChoice(string choice, AppTheme expected)
    {
        Assert.Equal(expected, ThemeSettings.ToAppTheme(choice));
    }

    [Fact]
    public void NoStoredChoice_FollowsTheSystem()
    {
        Assert.Equal("System", NewViewModel().ThemeChoice);
        Assert.Equal(AppTheme.Unspecified, ThemeSettings.ToAppTheme(ThemeSettings.Load()));
    }

    [Theory]
    [InlineData("Light", AppTheme.Light)]
    [InlineData("Dark", AppTheme.Dark)]
    public void Choosing_AppliesAndSurvivesRestart(string choice, AppTheme expected)
    {
        NewViewModel().ThemeChoice = choice;

        Assert.Equal(expected, Application.Current!.UserAppTheme);
        Assert.Equal(choice, NewViewModel().ThemeChoice);
    }

    [Fact]
    public void ChoosingSystem_ClearsAnEarlierChoice()
    {
        var vm = NewViewModel();
        vm.ThemeChoice = "Dark";

        vm.ThemeChoice = "System";

        Assert.Equal(AppTheme.Unspecified, Application.Current!.UserAppTheme);
        Assert.Equal("System", ThemeSettings.Load());
    }
}
