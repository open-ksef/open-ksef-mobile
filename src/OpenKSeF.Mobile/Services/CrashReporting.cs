using System.Text.RegularExpressions;
using Sentry;

namespace OpenKSeF.Mobile.Services;

/// <summary>Sentry options and the scrubber every event and breadcrumb passes before it leaves the device.</summary>
public static class CrashReporting
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        (new(@"""(\w+)""\s*:\s*(?:""(?:[^""\\]|\\.)*""|-?\d+(?:\.\d+)?|true|false)", Options), "\"$1\":\"[Filtered]\""),
        (new(@"(?<=[\w/.:-])\?[^\s""'<>]*=[^\s""'<>]*", Options), "?[Filtered]"),
        (new(@"\b(authorization|access_token|refresh_token|id_token|token|ksef_?token|password|client_secret|code_verifier|device_code)\b(\s*[:=]\s*)(?:(?:Bearer|Basic)\s+)?[^\s&,;""']+", Options), "$1$2[Filtered]"),
        (new(@"Bearer\s+\S+", Options), "Bearer [Filtered]"),
        (new(@"(?<![\w+=.|-])(?=[\w+=.|-]*\d)[\w+=.|-]{32,}", Options), "[Filtered]"),
        (new(@"[^\s""'<>@:,;()\[\]]+@[^\s""'<>@:,;()\[\]]+\.[a-z]{2,}", Options), "[email]"),
        (new(@"(?<!\d)(?:PL)?\d{2}(?:[ ]?\d{4}){6}(?!\d)", Options), "[NRB]"),
        (new(@"(?<!\d)(?:PL)?\d(?:[ -]?\d){8,}(?!\d)", Options), "[number]"),
    ];

    private static readonly HashSet<string> HttpBreadcrumbKeys = ["url", "method", "status_code", "reason"];

    public static void Configure(SentryOptions options, string dsn, string release)
    {
        options.Dsn = dsn;
        options.Release = release;
        options.SendDefaultPii = false;
        options.CaptureFailedRequests = false;
        options.EnableLogs = false;
        options.SetBeforeSend(ScrubEvent);
        options.SetBeforeBreadcrumb(ScrubBreadcrumb);
#if ANDROID
        options.Native.EnableBeforeSend = true;
#endif
    }

    public static string? Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        foreach (var (pattern, replacement) in Rules)
            text = pattern.Replace(text, replacement);
        return text;
    }

    public static SentryEvent ScrubEvent(SentryEvent e)
    {
        e.User = new SentryUser();
        e.Request = new SentryRequest();
        e.ServerName = null;
        e.TransactionName = Scrub(e.TransactionName);
        if (e.Message is { } message)
            e.Message = new SentryMessage { Message = Scrub(message.Message), Formatted = Scrub(message.Formatted) };

        foreach (var exception in e.SentryExceptions ?? [])
        {
            exception.Value = Scrub(exception.Value);
            if (exception.Mechanism is { } mechanism)
            {
                mechanism.Description = Scrub(mechanism.Description);
                foreach (var key in mechanism.Data.Keys.ToList())
                    mechanism.Data[key] = Scrub(mechanism.Data[key]?.ToString())!;
            }
        }

        foreach (var (key, value) in e.Extra.ToList())
            e.SetExtra(key, Scrub(value?.ToString()));
        foreach (var (key, value) in e.Tags.ToList())
            e.SetTag(key, Scrub(value) ?? "");
        return e;
    }

    public static Breadcrumb ScrubBreadcrumb(Breadcrumb breadcrumb)
    {
        var data = breadcrumb.Data?
            .Where(kv => breadcrumb.Type != "http" || HttpBreadcrumbKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => Scrub(kv.Value) ?? "");
        return new Breadcrumb(Scrub(breadcrumb.Message) ?? "", breadcrumb.Type, data, breadcrumb.Category, breadcrumb.Level);
    }
}
