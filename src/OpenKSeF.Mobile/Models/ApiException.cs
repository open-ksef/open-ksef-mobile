using System.Net;

namespace OpenKSeF.Mobile.Models;

public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public ValidationEnvelope? Validation { get; }

    public ApiException(HttpStatusCode statusCode, string message, ValidationEnvelope? validation = null)
        : base(message)
    {
        StatusCode = statusCode;
        Validation = validation;
    }

    public static bool IsNetworkFailure(Exception ex) =>
        ex is ApiException { StatusCode: HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout }
            or TaskCanceledException
            or HttpRequestException;
}
