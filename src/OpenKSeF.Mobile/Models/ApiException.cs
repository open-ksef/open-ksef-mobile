using System.Net;

namespace OpenKSeF.Mobile.Models;

public class ApiException : Exception
{
    public const string ServerInvalidTokenMessage = "KSeF odrzucił token (nieprawidłowy lub unieważniony). Wygeneruj nowy token i podmień go w ustawieniach firmy.";

    public const string TokenRejectedMessage = "KSeF nie przyjął tokenu. Sprawdź, czy skopiowano go w całości i czy nie wygasł.";

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
