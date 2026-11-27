using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace VailITHeartbeat;

/// <summary>The outcome of one request to one target.</summary>
public sealed record CheckResult(bool Healthy, string Summary, int? StatusCode, long ElapsedMs);

/// <summary>Makes one HTTP request and decides whether the answer counts as healthy.</summary>
public sealed class HealthChecker
{
    private const int MaxBodyCharsInSummary = 300;

    private readonly HttpClient _http;
    private readonly CertificateCapture _certificates;

    public HealthChecker(HttpClient http, CertificateCapture certificates)
    {
        _http = http;
        _certificates = certificates;
    }

    public async Task<CheckResult> CheckAsync(TargetOptions target, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, target.TimeoutSeconds)));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
            request.Headers.CacheControl = new() { NoCache = true };
            using var response = await _http.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            watch.Stop();

            return Evaluate(target, (int)response.StatusCode, body, _certificates.ExpiryFor(target.Url), DateTimeOffset.UtcNow, watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, $"No answer within {target.TimeoutSeconds} s (timed out).", null, watch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            // DNS failure, connection refused, TLS/certificate error, etc.
            var detail = ex.InnerException is { } inner ? $"{ex.Message} ({inner.Message})" : ex.Message;
            return new(false, $"Could not connect: {detail}", null, watch.ElapsedMilliseconds);
        }
    }

    /// <summary>The pass/fail rules, kept separate from the network code so they are easy to test and change.</summary>
    internal static CheckResult Evaluate(TargetOptions target, int statusCode, string body, DateTimeOffset? certificateExpiry, DateTimeOffset now, long elapsedMs)
    {
        var expected = target.ExpectedStatusCodes.Count > 0 ? target.ExpectedStatusCodes : [200];
        if (!expected.Contains(statusCode))
            return new(false, $"HTTP {statusCode} (expected {string.Join(" or ", expected)}). Body: {Trim(body)}", statusCode, elapsedMs);

        if (!string.IsNullOrEmpty(target.ExpectedBodyContains) &&
            !body.Contains(target.ExpectedBodyContains, StringComparison.OrdinalIgnoreCase))
            return new(false, $"HTTP {statusCode} but the body did not contain {target.ExpectedBodyContains}. Body: {Trim(body)}", statusCode, elapsedMs);

        if (!string.IsNullOrEmpty(target.ExpectedJsonProperty))
        {
            var actual = ReadJsonProperty(body, target.ExpectedJsonProperty);
            if (!string.Equals(actual, target.ExpectedJsonValue, StringComparison.OrdinalIgnoreCase))
                return new(false, actual is null
                    ? $"HTTP {statusCode} but the body has no JSON property \"{target.ExpectedJsonProperty}\". Body: {Trim(body)}"
                    : $"HTTP {statusCode} but \"{target.ExpectedJsonProperty}\" is \"{actual}\" (expected \"{target.ExpectedJsonValue}\"). Body: {Trim(body)}", statusCode, elapsedMs);
        }

        if (target.CertificateExpiryWarningDays > 0 && certificateExpiry is { } expiry)
        {
            var daysLeft = (expiry - now).TotalDays;
            if (daysLeft < target.CertificateExpiryWarningDays)
                return new(false, $"HTTPS certificate expires {expiry:yyyy-MM-dd HH:mm} UTC ({Math.Max(0, daysLeft):0.#} days left).", statusCode, elapsedMs);
        }

        return new(true, $"HTTP {statusCode} in {elapsedMs} ms.", statusCode, elapsedMs);
    }

    private static string? ReadJsonProperty(string body, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in doc.RootElement.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Trim(string body)
    {
        body = body.ReplaceLineEndings(" ").Trim();
        return body.Length <= MaxBodyCharsInSummary ? body : body[..MaxBodyCharsInSummary] + "...";
    }
}

/// <summary>
/// Remembers the expiry date of the HTTPS certificate each host presented. Normal certificate
/// validation still applies: an invalid certificate fails the request as usual.
/// </summary>
public sealed class CertificateCapture
{
    private readonly Dictionary<string, DateTimeOffset> _expiryByHost = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public bool Validate(HttpRequestMessage request, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is not null && request.RequestUri is { } uri)
            lock (_lock) _expiryByHost[uri.Authority] = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        return errors == SslPolicyErrors.None;
    }

    public DateTimeOffset? ExpiryFor(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        lock (_lock) return _expiryByHost.TryGetValue(uri.Authority, out var expiry) ? expiry : null;
    }
}
