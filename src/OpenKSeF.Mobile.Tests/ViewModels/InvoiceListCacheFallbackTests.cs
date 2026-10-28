using System.Net;
using OpenKSeF.Mobile.Models;
using Xunit;

namespace OpenKSeF.Mobile.Tests.ViewModels;

public class InvoiceListCacheFallbackTests
{
    [Fact]
    public void ServiceUnavailable_FallsBackToCache()
    {
        Assert.True(ApiException.IsNetworkFailure(new ApiException(HttpStatusCode.ServiceUnavailable, "offline")));
    }

    [Fact]
    public void Timeout_FallsBackToCache()
    {
        Assert.True(ApiException.IsNetworkFailure(new TaskCanceledException()));
    }

    [Fact]
    public void HttpRequestException_FallsBackToCache()
    {
        Assert.True(ApiException.IsNetworkFailure(new HttpRequestException()));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void ApiErrors_DoNotFallBackToCache(HttpStatusCode status)
    {
        Assert.False(ApiException.IsNetworkFailure(new ApiException(status, "error")));
    }

    [Fact]
    public void UnexpectedException_DoesNotFallBackToCache()
    {
        Assert.False(ApiException.IsNetworkFailure(new InvalidOperationException()));
    }
}
