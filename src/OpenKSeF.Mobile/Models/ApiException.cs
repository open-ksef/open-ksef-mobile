using System.Net;

namespace OpenKSeF.Mobile.Models;

public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public ApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public static bool IsNetworkFailure(Exception ex) =>
        ex is ApiException { StatusCode: HttpStatusCode.ServiceUnavailable }
            or TaskCanceledException
            or HttpRequestException;
}
