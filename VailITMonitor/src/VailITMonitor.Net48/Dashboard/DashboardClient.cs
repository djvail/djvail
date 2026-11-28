using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text.Json;
using System.Threading.Tasks;
using VailIT.Reporting.Contracts;
using VailITMonitor.Configuration;

namespace VailITMonitor.Dashboard
{
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

    public sealed class SendResult<T> where T : class
    {
        public SendResult(SendOutcome outcome, int statusCode, string message, T value, TimeSpan? retryAfter)
        {
            Outcome = outcome;
            StatusCode = statusCode;
            Message = message ?? "";
            Value = value;
            RetryAfter = retryAfter;
        }

        public SendOutcome Outcome { get; }
        public int StatusCode { get; }
        public string Message { get; }
        public T Value { get; }
        public TimeSpan? RetryAfter { get; }

        public string Explain()
        {
            return StatusCode == 0 ? Message : "HTTP " + StatusCode + (Message.Length > 0 ? ": " + Message : "");
        }
    }

    /// <summary>
    /// The HTTP side of reporting: one long-lived HttpClient that sends reports to VailITDashboard.
    /// Only this class knows about HTTP; DashboardReporter decides what to send and when.
    ///
    /// The methods are async (HttpClient only has async methods) and the reporter waits for them
    /// with GetAwaiter().GetResult() on its timer thread - safe in a service, which has no UI thread.
    /// </summary>
    public sealed class DashboardClient : IDisposable
    {
        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);   // camelCase

        private readonly HttpClient _http;

        public DashboardClient(ReportingSettings reporting)
        {
            Uri baseUri;
            if (!Uri.TryCreate((reporting.DashboardUrl ?? "").TrimEnd('/') + "/", UriKind.Absolute, out baseUri))
                throw new InvalidOperationException("Reporting.DashboardUrl '" + reporting.DashboardUrl + "' is not a valid URL.");
            BaseUri = baseUri;

            // Older Windows builds may not offer TLS 1.2 to .NET Framework by default.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var pin = (reporting.PinnedCertificateThumbprint ?? "").Replace(" ", "").Trim();
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            if (pin.Length > 0)
            {
                // Accept a self-signed dashboard certificate only if it is exactly the pinned one.
                handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
                    errors == SslPolicyErrors.None ||
                    (cert != null && string.Equals(cert.Thumbprint, pin, StringComparison.OrdinalIgnoreCase));
            }

            _http = new HttpClient(handler)
            {
                BaseAddress = baseUri,
                Timeout = TimeSpan.FromSeconds(Math.Min(120, Math.Max(5, reporting.TimeoutSeconds))),
            };
            _http.DefaultRequestHeaders.Add(ReportingApi.ApiKeyHeader, ApiKeyProtector.Unprotect(reporting.ApiKeyProtected));
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VailITMonitor", MachineInfo.AgentVersion));

            // .NET Framework keeps connections (and their DNS answer) forever unless told otherwise.
            ServicePointManager.FindServicePoint(baseUri).ConnectionLeaseTimeout = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
        }

        public Uri BaseUri { get; }

        public bool IsHttps
        {
            get { return BaseUri.Scheme == Uri.UriSchemeHttps; }
        }

        /// <summary>POST /api/v1/reports. Larger (catch-up) reports are gzip-compressed.</summary>
        public SendResult<ReportAck> SendReport(MonitorReport report)
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(report, Json);
            HttpContent content;
            if (report.Samples.Count > 1)
            {
                var buffer = new MemoryStream();
                using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, true))
                    gzip.Write(json, 0, json.Length);
                content = new ByteArrayContent(buffer.ToArray());
                content.Headers.ContentEncoding.Add("gzip");
            }
            else
            {
                content = new ByteArrayContent(json);
            }
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using (var request = new HttpRequestMessage(HttpMethod.Post, ReportingApi.ReportsPath.TrimStart('/')) { Content = content })
                return SendAsync<ReportAck>(request).GetAwaiter().GetResult();
        }

        /// <summary>GET /api/v1/monitors/me: checks the key and returns the name and group the dashboard assigned.</summary>
        public SendResult<MonitorInfo> GetMe()
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, ReportingApi.MePath.TrimStart('/')))
                return SendAsync<MonitorInfo>(request).GetAwaiter().GetResult();
        }

        private async Task<SendResult<T>> SendAsync<T>(HttpRequestMessage request) where T : class
        {
            try
            {
                using (var response = await _http.SendAsync(request).ConfigureAwait(false))
                {
                    int code = (int)response.StatusCode;
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        var value = JsonSerializer.Deserialize<T>(body, Json);
                        return value == null
                            ? new SendResult<T>(SendOutcome.Failed, code, "The dashboard sent an empty reply.", null, null)
                            : new SendResult<T>(SendOutcome.Ok, code, "", value, null);
                    }

                    var retryAfter = RetryAfter(response);
                    SendOutcome outcome;
                    switch (code)
                    {
                        case 400: outcome = SendOutcome.BadRequest; break;
                        case 401:
                        case 403: outcome = SendOutcome.Unauthorized; break;
                        case 413: outcome = SendOutcome.TooLarge; break;
                        case 429:
                        case 503: outcome = retryAfter.HasValue ? SendOutcome.Busy : SendOutcome.Failed; break;
                        default: outcome = SendOutcome.Failed; break;
                    }
                    return new SendResult<T>(outcome, code, Describe(body), null, retryAfter);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
            {
                // Timeouts surface as TaskCanceledException.
                var message = ex.InnerException != null ? ex.Message + " " + ex.InnerException.Message : ex.Message;
                return new SendResult<T>(SendOutcome.Failed, 0, message, null, null);
            }
        }

        private static TimeSpan? RetryAfter(HttpResponseMessage response)
        {
            var header = response.Headers.RetryAfter;
            if (header == null) return null;
            if (header.Delta.HasValue) return header.Delta.Value;
            if (header.Date.HasValue) return header.Date.Value - DateTimeOffset.UtcNow;
            return null;
        }

        /// <summary>Pulls the "detail" out of an ASP.NET Core problem-details reply, for the log.</summary>
        private static string Describe(string body)
        {
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    JsonElement e;
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (doc.RootElement.TryGetProperty("detail", out e)) return e.GetString() ?? "";
                        if (doc.RootElement.TryGetProperty("title", out e)) return e.GetString() ?? "";
                    }
                }
            }
            catch (JsonException)
            {
            }
            return body.Length > 200 ? body.Substring(0, 200) : body;
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
