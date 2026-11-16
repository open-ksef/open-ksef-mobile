using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenKSeF.Mobile.Services;
using Sentry;
using Sentry.Protocol;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class CrashReportingTests
{
    private const string Nip = "1111111111";
    private const string Nrb = "11 0000 0000 0000 0000 0000 0000";
    private const string Email = "jan.testowy@example.com";
    private const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ0ZXN0In0.c2lnbmF0dXJlLWZha2UtZmFrZQ";
    private const string Refresh = "fake-refresh-0123456789abcdefghijklmnop";
    private const string KsefToken = "20251010-EC-0000000000-0000000000-00|nip-1111111111|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Release = "com.openksef.mobile@1.2.3+45";

    private static readonly string[] Secrets = [Nip, "0000 0000 0000 0000 0000 0000", "00000000000000000000000000", Email, Jwt, Refresh, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Fakturowo", "123.45", "sekret-kod"];

    [Theory]
    [InlineData("Authorization: Bearer " + Jwt, "Authorization: [Filtered]")]
    [InlineData("header Bearer " + Jwt, "header Bearer [Filtered]")]
    [InlineData("refresh_token=" + Refresh + "&x=1", "refresh_token=[Filtered]&x=1")]
    [InlineData("ksefToken: " + KsefToken, "ksefToken: [Filtered]")]
    [InlineData("token " + KsefToken, "token [Filtered]")]
    [InlineData("tenant " + Nip + " failed", "tenant [number] failed")]
    [InlineData("NIP 111-111-11-11", "NIP [number]")]
    [InlineData("PL1111111111", "[number]")]
    [InlineData("pay to " + Nrb, "pay to [NRB]")]
    [InlineData("pay to PL11000000000000000000000000", "pay to [NRB]")]
    [InlineData("mail " + Email + " bounced", "mail [email] bounced")]
    [InlineData("GET https://api.example.pl/api/invoices?nip=1111111111&code=sekret-kod", "GET https://api.example.pl/api/invoices?[Filtered]")]
    [InlineData("//invoices/details?invoiceId=42", "//invoices/details?[Filtered]")]
    [InlineData("{\"vendorName\":\"Fakturowo\",\"grossAmount\":123.45,\"isPaid\":true}", "{\"vendorName\":\"[Filtered]\",\"grossAmount\":\"[Filtered]\",\"isPaid\":\"[Filtered]\"}")]
    [InlineData("Is it ok? Yes", "Is it ok? Yes")]
    [InlineData("Index was out of range.", "Index was out of range.")]
    public void Scrub_RemovesSensitiveText(string input, string expected) =>
        Assert.Equal(expected, CrashReporting.Scrub(input));

    [Fact]
    public void ScrubBreadcrumb_HttpKeepsOnlyUrlMethodStatus()
    {
        var crumb = new Breadcrumb("POST", "http", new Dictionary<string, string>
        {
            ["url"] = "https://api.example.pl/api/devices/register?token=" + Refresh,
            ["method"] = "POST",
            ["status_code"] = "500",
            ["request_body"] = "{\"token\":\"" + Refresh + "\"}",
            ["response_body"] = "{\"email\":\"" + Email + "\"}",
        }, "http", BreadcrumbLevel.Info);

        var scrubbed = CrashReporting.ScrubBreadcrumb(crumb);

        Assert.Equal(["method", "status_code", "url"], scrubbed.Data!.Keys.Order());
        Assert.Equal("https://api.example.pl/api/devices/register?[Filtered]", scrubbed.Data["url"]);
    }

    [Fact]
    public void ScrubBreadcrumb_ScrubsMessageAndData()
    {
        var crumb = new Breadcrumb("Synced " + Nip + " for " + Email, "default", new Dictionary<string, string> { ["nrb"] = Nrb }, "ApiService", BreadcrumbLevel.Info);

        var scrubbed = CrashReporting.ScrubBreadcrumb(crumb);

        Assert.Equal("Synced [number] for [email]", scrubbed.Message);
        Assert.Equal("[NRB]", scrubbed.Data!["nrb"]);
    }

    [Fact]
    public void ScrubEvent_ClearsUserRequestAndScrubsTextFields()
    {
        var e = new SentryEvent(new InvalidOperationException("Bearer " + Jwt + " rejected for " + Nip))
        {
            Message = new SentryMessage { Message = "user " + Email, Formatted = "user " + Email },
            User = new SentryUser { Email = Email, Id = Nip, IpAddress = "10.0.0.1" },
            ServerName = "host-name",
        };
        e.SentryExceptions = [new SentryException { Type = "InvalidOperationException", Value = "Bearer " + Jwt + " rejected for " + Nip }];
        e.Request.Headers["Authorization"] = "Bearer " + Jwt;
        e.SetExtra("body", "{\"buyerNip\":\"" + Nip + "\"}");
        e.SetTag("account", Nrb);

        var scrubbed = CrashReporting.ScrubEvent(e);

        Assert.Null(scrubbed.User.Email);
        Assert.Null(scrubbed.User.Id);
        Assert.Null(scrubbed.User.IpAddress);
        Assert.Empty(scrubbed.Request.Headers);
        Assert.Null(scrubbed.ServerName);
        Assert.Equal("user [email]", scrubbed.Message!.Formatted);
        Assert.Equal("Bearer [Filtered] rejected for [number]", scrubbed.SentryExceptions!.Single().Value);
        Assert.Equal("{\"buyerNip\":\"[Filtered]\"}", scrubbed.Extra["body"]);
        Assert.Equal("[NRB]", scrubbed.Tags["account"]);
    }

    [Fact]
    public void ScrubEvent_DropsUserSetDeviceName()
    {
        var e = new SentryEvent();
        e.Contexts.Device.Name = "Telefon Jana Kowalskiego";

        var scrubbed = CrashReporting.ScrubEvent(e);

        Assert.Null(scrubbed.Contexts.Device.Name);
    }

    [Fact]
    public void ScrubEvent_DropsDeviceAndAppIdentifiers()
    {
        var e = new SentryEvent();
        e.Contexts.Device.DeviceUniqueIdentifier = "a1b2c3d4-0000-0000-0000-000000000000";
        e.Contexts.App.Hash = "deadbeef";

        var scrubbed = CrashReporting.ScrubEvent(e);

        Assert.Null(scrubbed.Contexts.Device.DeviceUniqueIdentifier);
        Assert.Null(scrubbed.Contexts.App.Hash);
    }

    [Fact]
    public async Task LoggerEventThroughSdk_ReachesEndpointScrubbedWithRelease()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var listener = new HttpListener { Prefixes = { $"http://localhost:{port}/" } };
        listener.Start();
        var envelope = ReceiveEnvelopeAsync(listener);

        using (var factory = LoggerFactory.Create(b => b.AddSentry(o =>
        {
            CrashReporting.Configure(o, $"http://fakepublickey@localhost:{port}/1", Release);
            o.AutoSessionTracking = false;
            o.CacheDirectoryPath = null;
            o.InitializeSdk = true;
        })))
        {
            var logger = factory.CreateLogger("OpenKSeF.Mobile.Services.ApiService");
            logger.LogInformation("Loaded {Count} invoices for {Nip} from https://api.example.pl/x?access_token={Token}", 3, Nip, Jwt);
            SentrySdk.AddBreadcrumb("POST", "http", "http", new Dictionary<string, string>
            {
                ["url"] = "https://api.example.pl/api/tenants?nip=" + Nip,
                ["method"] = "POST",
                ["status_code"] = "500",
                ["request_body"] = "{\"vendorName\":\"Fakturowo\",\"grossAmount\":123.45}",
            });
            logger.LogError(
                new HttpRequestException("Authorization: Bearer " + Jwt + " refresh_token=" + Refresh + " ksefToken=" + KsefToken),
                "Upload failed for {Nip} {Email} {Nrb} {Body}",
                Nip, Email, Nrb, "{\"vendorName\":\"Fakturowo\",\"grossAmount\":123.45,\"code\":\"sekret-kod\"}");
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(10));
        }

        var payload = await envelope.WaitAsync(TimeSpan.FromSeconds(15));
        if (Environment.GetEnvironmentVariable("SENTRY_ENVELOPE_OUT") is { Length: > 0 } path)
            await File.WriteAllTextAsync(path, payload);

        using var evt = JsonDocument.Parse(payload.Split('\n')[2]);
        Assert.Equal(Release, evt.RootElement.GetProperty("release").GetString());
        Assert.Empty(evt.RootElement.GetProperty("user").EnumerateObject());
        Assert.Contains("HttpRequestException", payload);
        Assert.Contains("Upload failed for [number] [email] [NRB]", payload);
        Assert.DoesNotContain("request_body", payload);
        foreach (var secret in Secrets)
            Assert.DoesNotContain(secret, payload);
    }


    private static async Task<string> ReceiveEnvelopeAsync(HttpListener listener)
    {
        while (true)
        {
            var context = await listener.GetContextAsync();
            Stream body = context.Request.InputStream;
            if (string.Equals(context.Request.Headers["Content-Encoding"], "gzip", StringComparison.OrdinalIgnoreCase))
                body = new GZipStream(body, CompressionMode.Decompress);
            string text;
            using (var reader = new StreamReader(body, Encoding.UTF8))
                text = await reader.ReadToEndAsync();
            context.Response.StatusCode = 200;
            context.Response.Close();
            if (context.Request.Url!.AbsolutePath.EndsWith("/envelope/", StringComparison.Ordinal) && text.Contains("\"type\":\"event\"", StringComparison.Ordinal))
                return text;
        }
    }
}
