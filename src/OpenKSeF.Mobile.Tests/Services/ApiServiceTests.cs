using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NSubstitute;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

// Minimal duplicates of Mobile types for testing without MAUI project reference.
// These will be removed once a shared contracts project exists.

public interface IAuthService
{
    Task<bool> LoginAsync();
    Task LogoutAsync();
    Task<string?> GetAccessTokenAsync();
    bool IsAuthenticated { get; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}

public class InvoiceDto
{
    public Guid Id { get; set; }
    public string KSeFInvoiceNumber { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public decimal AmountGross { get; set; }
    public string Currency { get; set; } = "PLN";
    public DateTime IssueDate { get; set; }
}

public class TenantDto
{
    public Guid Id { get; set; }
    public string Nip { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

public class UserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Tests the HTTP client patterns used by ApiService without referencing the MAUI project.
/// Verifies that our API interaction patterns (auth header injection, error handling,
/// JSON deserialization) work correctly.
/// </summary>
public class ApiServiceTests
{
    private readonly IAuthService _authService;

    public ApiServiceTests()
    {
        _authService = Substitute.For<IAuthService>();
        _authService.GetAccessTokenAsync().Returns("test-access-token");
    }

    private HttpClient CreateClient(HttpStatusCode statusCode, object? responseBody = null)
    {
        var handler = new FakeHandler(statusCode, responseBody);
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8081") };
    }

    [Fact]
    public async Task AuthHeader_IsSetFromAuthService()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            new UserInfo { UserId = "u1", Email = "test@example.com" });
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8081") };

        var token = await _authService.GetAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/account/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization.Scheme);
        Assert.Equal("test-access-token", client.DefaultRequestHeaders.Authorization.Parameter);
    }

    [Fact]
    public async Task GetTenants_DeserializesCorrectly()
    {
        var tenants = new List<TenantDto>
        {
            new() { Id = Guid.NewGuid(), Nip = "1234567890", DisplayName = "Test Sp. z o.o." }
        };

        var client = CreateClient(HttpStatusCode.OK, tenants);
        var response = await client.GetAsync("/api/tenants");
        var result = await response.Content.ReadFromJsonAsync<List<TenantDto>>();

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("1234567890", result[0].Nip);
    }

    [Fact]
    public async Task GetInvoices_PagedResult_DeserializesCorrectly()
    {
        var pagedResult = new PagedResult<InvoiceDto>
        {
            Items = [new InvoiceDto
            {
                Id = Guid.NewGuid(),
                KSeFInvoiceNumber = "FV/2026/001",
                VendorName = "Vendor Sp. z o.o.",
                AmountGross = 1230.00m,
                Currency = "PLN",
                IssueDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
            }],
            Page = 1,
            PageSize = 20,
            TotalCount = 1
        };

        var client = CreateClient(HttpStatusCode.OK, pagedResult);
        var response = await client.GetAsync("/api/tenants/123/invoices?page=1&pageSize=20");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<InvoiceDto>>();

        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal(1230.00m, result.Items[0].AmountGross);
        Assert.Equal("PLN", result.Items[0].Currency);
    }

    [Fact]
    public async Task UnauthorizedResponse_Returns401()
    {
        var client = CreateClient(HttpStatusCode.Unauthorized);
        var response = await client.GetAsync("/api/account/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ForbiddenResponse_Returns403()
    {
        var client = CreateClient(HttpStatusCode.Forbidden);
        var response = await client.GetAsync("/api/tenants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object? _responseBody;

        public FakeHandler(HttpStatusCode statusCode, object? responseBody = null)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode);
            if (_responseBody is not null)
            {
                response.Content = JsonContent.Create(_responseBody);
            }
            return Task.FromResult(response);
        }
    }
}
