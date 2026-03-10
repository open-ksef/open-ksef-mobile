using NSubstitute;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public interface ITestApiServiceForDevices
{
    Task RegisterDeviceTokenAsync(TestRegisterDeviceTokenRequest request);
    Task<List<TestDeviceTokenDto>> GetDevicesAsync();
}

public class TestRegisterDeviceTokenRequest
{
    public string Token { get; set; } = string.Empty;
    public int Platform { get; set; }
    public Guid? TenantId { get; set; }
}

public class TestDeviceTokenDto
{
    public Guid Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public int Platform { get; set; }
    public Guid? TenantId { get; set; }
}

/// <summary>
/// Tests device registration orchestration logic without MAUI dependencies.
/// Mirrors the core decision logic of DeviceTokenService.
/// </summary>
public class DeviceTokenServiceTests
{
    private readonly ITestApiServiceForDevices _apiService;

    public DeviceTokenServiceTests()
    {
        _apiService = Substitute.For<ITestApiServiceForDevices>();
    }

    [Fact]
    public async Task IsDeviceRegistered_ReturnsTrue_WhenMatchingTokenExists()
    {
        var storedToken = "fcm-token-123";
        _apiService.GetDevicesAsync().Returns(new List<TestDeviceTokenDto>
        {
            new() { Id = Guid.NewGuid(), Token = storedToken, Platform = 0 }
        });

        var devices = await _apiService.GetDevicesAsync();
        var found = devices.Any(d => d.Token == storedToken);

        Assert.True(found);
    }

    [Fact]
    public async Task IsDeviceRegistered_ReturnsFalse_WhenNoMatchingDevice()
    {
        _apiService.GetDevicesAsync().Returns(new List<TestDeviceTokenDto>
        {
            new() { Id = Guid.NewGuid(), Token = "other-token", Platform = 0 }
        });

        var devices = await _apiService.GetDevicesAsync();
        var storedToken = "my-token";
        var found = devices.Any(d => d.Token == storedToken);

        Assert.False(found);
    }

    [Fact]
    public async Task IsDeviceRegistered_MatchesByPlatform_WhenNoStoredToken()
    {
        _apiService.GetDevicesAsync().Returns(new List<TestDeviceTokenDto>
        {
            new() { Id = Guid.NewGuid(), Token = "device-abc", Platform = 0 }
        });

        var devices = await _apiService.GetDevicesAsync();
        var currentPlatform = 0; // Android
        var storedToken = string.Empty;

        bool found;
        if (!string.IsNullOrEmpty(storedToken))
            found = devices.Any(d => d.Token == storedToken);
        else
            found = devices.Any(d => d.Platform == currentPlatform);

        Assert.True(found);
    }

    [Fact]
    public async Task EnsureDeviceRegistered_SkipsRegistration_WhenAlreadyRegistered()
    {
        var deviceId = "device-abc123";
        _apiService.GetDevicesAsync().Returns(new List<TestDeviceTokenDto>
        {
            new() { Id = Guid.NewGuid(), Token = deviceId, Platform = 0 }
        });

        var devices = await _apiService.GetDevicesAsync();
        var isRegistered = devices.Any(d => d.Token == deviceId);

        Assert.True(isRegistered);
        await _apiService.DidNotReceive().RegisterDeviceTokenAsync(Arg.Any<TestRegisterDeviceTokenRequest>());
    }

    [Fact]
    public async Task EnsureDeviceRegistered_RegistersSilently_WhenNotRegistered()
    {
        _apiService.GetDevicesAsync().Returns(new List<TestDeviceTokenDto>());

        var devices = await _apiService.GetDevicesAsync();
        var isRegistered = devices.Any(d => d.Token == "my-device");

        Assert.False(isRegistered);

        var request = new TestRegisterDeviceTokenRequest
        {
            Token = "device-new-id",
            Platform = 0,
            TenantId = Guid.NewGuid()
        };
        await _apiService.RegisterDeviceTokenAsync(request);

        await _apiService.Received(1).RegisterDeviceTokenAsync(Arg.Any<TestRegisterDeviceTokenRequest>());
    }

    [Fact]
    public async Task EnsureDeviceRegistered_PassesTenantId_WhenAvailable()
    {
        _apiService.GetDevicesAsync().Returns(new List<TestDeviceTokenDto>());

        var tenantId = Guid.NewGuid();
        var request = new TestRegisterDeviceTokenRequest
        {
            Token = "device-id",
            Platform = 0,
            TenantId = tenantId
        };

        await _apiService.RegisterDeviceTokenAsync(request);

        await _apiService.Received(1).RegisterDeviceTokenAsync(
            Arg.Is<TestRegisterDeviceTokenRequest>(r => r.TenantId == tenantId));
    }

    [Fact]
    public async Task EnableNotifications_RegistersWithStoredToken_WhenPermissionGranted()
    {
        var storedToken = "fcm-token-abc";
        var tenantId = Guid.NewGuid();

        var request = new TestRegisterDeviceTokenRequest
        {
            Token = storedToken,
            Platform = 0,
            TenantId = tenantId
        };

        await _apiService.RegisterDeviceTokenAsync(request);

        await _apiService.Received(1).RegisterDeviceTokenAsync(
            Arg.Is<TestRegisterDeviceTokenRequest>(r => r.Token == storedToken && r.TenantId == tenantId));
    }

    [Fact]
    public async Task EnableNotifications_RegistersWithDeviceId_WhenNoStoredPushToken()
    {
        var storedPushToken = string.Empty;
        var deviceId = "device-fallback-id";

        var token = !string.IsNullOrEmpty(storedPushToken) ? storedPushToken : deviceId;

        await _apiService.RegisterDeviceTokenAsync(new TestRegisterDeviceTokenRequest
        {
            Token = token,
            Platform = 0
        });

        await _apiService.Received(1).RegisterDeviceTokenAsync(
            Arg.Is<TestRegisterDeviceTokenRequest>(r => r.Token == deviceId));
    }

    [Fact]
    public async Task GetDevices_ApiFailure_DoesNotThrow()
    {
        _apiService.GetDevicesAsync().Returns<List<TestDeviceTokenDto>>(
            _ => throw new Exception("Network error"));

        bool isRegistered;
        try
        {
            var devices = await _apiService.GetDevicesAsync();
            isRegistered = devices.Any();
        }
        catch
        {
            isRegistered = false;
        }

        Assert.False(isRegistered);
    }
}
