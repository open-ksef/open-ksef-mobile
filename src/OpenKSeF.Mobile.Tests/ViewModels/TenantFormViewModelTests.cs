using System.Net.Http.Json;
using System.Text.Json;
using NSubstitute;
using OpenKSeF.Mobile.Models;
using OpenKSeF.Mobile.Services;
using OpenKSeF.Mobile.ViewModels;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

[Collection("Shell")]
public class TenantFormViewModelTests
{
    private const string TenantsJson = """
        [{"id":"304f3ff7-06b6-43e8-bdf4-a0ad77f194e3","nip":"1111111111","displayName":"Test T-066","notificationEmail":"faktury@example.com","createdAt":"2026-10-28T13:11:56.612034Z"}]
        """;

    private static readonly Guid TenantId = Guid.Parse("304f3ff7-06b6-43e8-bdf4-a0ad77f194e3");

    private static async Task<List<TenantDto>> DeserializeAsBackendResponse()
    {
        using var content = new StringContent(TenantsJson, System.Text.Encoding.UTF8, "application/json");
        return (await content.ReadFromJsonAsync<List<TenantDto>>())!;
    }

    [Fact]
    public async Task TenantDto_DeserializesBackendTenantResponse()
    {
        var tenant = Assert.Single(await DeserializeAsBackendResponse());

        Assert.Equal(TenantId, tenant.Id);
        Assert.Equal("1111111111", tenant.Nip);
        Assert.Equal("Test T-066", tenant.DisplayName);
        Assert.Equal("faktury@example.com", tenant.NotificationEmail);
    }

    [Fact]
    public async Task Save_InEditMode_PreservesNotificationEmailAfterNameOnlyEdit()
    {
        var api = Substitute.For<IApiService>();
        api.GetTenantsAsync().Returns(await DeserializeAsBackendResponse());
        UpdateTenantRequest? sent = null;
        api.UpdateTenantAsync(TenantId, Arg.Do<UpdateTenantRequest>(r => sent = r)).Returns(new TenantDto());

        var vm = new TenantFormViewModel(api) { TenantId = TenantId.ToString() };
        vm.DisplayName = "Renamed";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(sent);
        Assert.Equal("Renamed", sent!.DisplayName);
        Assert.Equal("faktury@example.com", sent.NotificationEmail);
    }

    [Fact]
    public void UpdateTenantRequest_SerializesNotificationEmailInBackendCasing()
    {
        var json = JsonSerializer.Serialize(
            new UpdateTenantRequest { DisplayName = "Renamed", NotificationEmail = "faktury@example.com" },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("""{"displayName":"Renamed","notificationEmail":"faktury@example.com"}""", json);
    }
}
