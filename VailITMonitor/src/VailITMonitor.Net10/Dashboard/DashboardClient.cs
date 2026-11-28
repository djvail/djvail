using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VailIT.Reporting.Contracts;

namespace VailITMonitor.Dashboard;

/// <summary>What happened to one request, reduced to what the reporter needs to decide its next step.</summary>
public enum SendOutcome
{
    Ok,            // 200
    BadRequest,    // 400: the dashboard can't use this batch - skip it
    Unauthorized,  // 401: missing, revoked or wrong-machine key
    TooLarge,      // 413: send fewer samples
    Busy,          // 429 / 503 with Retry-After
    Failed,        // network error, timeout, other 5xx
}

public readonly record struct SendResult(SendOutcome Outcome, int StatusCode, string Message, TimeSpan? RetryAfter = null);

/// <summary>
/// The HTTP side of reporting: one long-lived HttpClient that sends reports to VailITDashboard.
/// Only this class knows about HTTP; <see cref="DashboardReporter"/> decides what to send and when.
/// </summary>
public sealed class DashboardClient : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);   // camelCase

    private readonly HttpClient _http;

    public DashboardClient(IOptions<MonitorOptions> options)
    {
        var reporting = options.Value.Reporting;
        if (!Uri.TryCreate(reporting.DashboardUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException($"Reporting:DashboardUrl '{reporting.DashboardUrl}' is not a valid URL.");
        BaseUri = baseUri;

        var pin = (reporting.PinnedCertificateThumbprint ?? "").Replace(" ", "").Trim();
        var handler = new SocketsHttpHandler
        {
            // Re-resolve DNS now and then, so a moved dashboard is found without restarting the service.
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = DecompressionMethods.All,
        };
        if (pin.Length > 0)
        {
            // Accept a self-signed dashboard certificate only if it is exactly the pinned one.
            handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, errors) =>
                errors == SslPolicyErrors.None ||
                (cert is not null && string.Equals(cert.GetCertHashString(), pin, StringComparison.OrdinalIgnoreCase));
        }

        _http = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(Math.Clamp(reporting.TimeoutSeconds, 5, 120)),
        };
        _http.DefaultRequestHeaders.Add(ReportingApi.ApiKeyHeader, ApiKeyProtector.Unprotect(reporting.ApiKeyProtected));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VailITMonitor", MachineInfo.AgentVersion));
    }

    public Uri BaseUri { get; }

    public bool IsHttps => BaseUri.Scheme == Uri.UriSchemeHttps;

    /// <summary>POST /api/v1/reports. Larger (catch-up) reports are gzip-compressed.</summary>
    public async Task<(SendResult Result, ReportAck? Ack)> SendReportAsync(MonitorReport report, CancellationToken ct)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(report, Json);
        HttpContent content;
        if (report.Samples.Count > 1)
        {
            var buffer = new MemoryStream();
            using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
                gzip.Write(json);
            content = new ByteArrayContent(buffer.ToArray());
            content.Headers.ContentEncoding.Add("gzip");
        }
        else
        {
            content = new ByteArrayContent(json);
        }
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, ReportingApi.ReportsPath.TrimStart('/')) { Content = content };
        return await SendAsync<ReportAck>(request, ct);
    }

    /// <summary>GET /api/v1/monitors/me: checks the key and returns the name and group the dashboard assigned.</summary>
    public async Task<(SendResult Result, MonitorInfo? Info)> GetMeAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReportingApi.MePath.TrimStart('/'));
        return await SendAsync<MonitorInfo>(request, ct);
    }

    private async Task<(SendResult, T?)> SendAsync<T>(HttpRequestMessage request, CancellationToken ct) where T : class
    {
        try
        {
            using var response = await _http.SendAsync(request, ct);
            int code = (int)response.StatusCode;
            string body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var value = JsonSerializer.Deserialize<T>(body, Json);
                return value is null
                    ? (new SendResult(SendOutcome.Failed, code, "The dashboard sent an empty reply."), null)
                    : (new SendResult(SendOutcome.Ok, code, ""), value);
            }

            var outcome = code switch
            {
                400 => SendOutcome.BadRequest,
                401 or 403 => SendOutcome.Unauthorized,
                413 => SendOutcome.TooLarge,
                429 or 503 when RetryAfter(response) is not null => SendOutcome.Busy,
                _ => SendOutcome.Failed,
            };
            return (new SendResult(outcome, code, Describe(body), RetryAfter(response)), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            // Timeouts surface as TaskCanceledException; a real shutdown is let through.
            return (new SendResult(SendOutcome.Failed, 0, ex.Message), null);
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return delta;
        if (header?.Date is { } date) return date - DateTimeOffset.UtcNow;
        return null;
    }

    /// <summary>Pulls the "detail" out of an ASP.NET Core problem-details reply, for the log.</summary>
    private static string Describe(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var detail)) return detail.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("title", out var title)) return title.GetString() ?? "";
        }
        catch (JsonException)
        {
        }
        return body.Length > 200 ? body[..200] : body;
    }

    public void Dispose() => _http.Dispose();
}
