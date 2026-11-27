using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace VailITHeartbeat;

/// <summary>Sends the alert emails over SMTP (MailKit).</summary>
public sealed class EmailNotifier
{
    private readonly EmailOptions _options;
    private readonly ILogger<EmailNotifier> _log;

    public EmailNotifier(IOptions<EmailOptions> options, ILogger<EmailNotifier> log)
    {
        _options = options.Value;
        _log = log;
    }

    public Task<bool> SendAlertAsync(AlertKind kind, TargetOptions target, TargetState state, CheckResult result, CancellationToken ct)
    {
        var (word, intro) = kind switch
        {
            AlertKind.Down => ("DOWN", "The heartbeat check cannot reach your dashboard. Please go and look into it."),
            AlertKind.StillDown => ("STILL DOWN", "Reminder: your dashboard is still failing its heartbeat check."),
            AlertKind.Recovered => ("RECOVERED", "Good news: your dashboard is answering its heartbeat check again."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var subject = $"{_options.SubjectPrefix} {word}: {target.DisplayName}".Trim();
        var now = DateTimeOffset.UtcNow;
        var lines = new List<string>
        {
            intro,
            "",
            $"Target:        {target.DisplayName}",
            $"URL:           {target.Url}",
            $"Checked at:    {now:yyyy-MM-dd HH:mm:ss} UTC (from {Environment.MachineName})",
            $"Result:        {result.Summary}",
        };
        if (kind != AlertKind.Recovered)
        {
            lines.Add($"Failed checks: {state.ConsecutiveFailures} in a row");
            if (state.FirstFailureUtc is { } since)
                lines.Add($"Failing since: {since:yyyy-MM-dd HH:mm:ss} UTC ({FormatDuration(now - since)})");
        }
        if (state.LastSuccessUtc is { } ok)
            lines.Add($"Last success:  {ok:yyyy-MM-dd HH:mm:ss} UTC");

        return SendAsync(subject, string.Join(Environment.NewLine, lines), ct);
    }

    public async Task<bool> SendAsync(string subject, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost) || _options.To.Count == 0)
        {
            _log.LogError("Email is not configured (Email:SmtpHost / Email:To). Would have sent: {Subject}", subject);
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_options.From));
            foreach (var to in _options.To) message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var smtp = new SmtpClient { Timeout = 30_000 };
            var security = _options.Security.ToLowerInvariant() switch
            {
                "none" => SecureSocketOptions.None,
                "sslonconnect" => SecureSocketOptions.SslOnConnect,
                "auto" => SecureSocketOptions.Auto,
                _ => SecureSocketOptions.StartTls,
            };
            await smtp.ConnectAsync(_options.SmtpHost, _options.SmtpPort, security, ct);
            if (!string.IsNullOrEmpty(_options.Username))
                await smtp.AuthenticateAsync(_options.Username, _options.Password, ct);
            await smtp.SendAsync(message, ct);
            await smtp.DisconnectAsync(true, ct);

            _log.LogInformation("Sent email: {Subject}", subject);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _log.LogError(ex, "Could not send email: {Subject}", subject);
            return false;
        }
    }

    private static string FormatDuration(TimeSpan span) =>
        span.TotalMinutes < 1 ? $"{span.TotalSeconds:0} s"
        : span.TotalHours < 1 ? $"{span.TotalMinutes:0} min"
        : span.TotalDays < 1 ? $"{span.TotalHours:0.#} h"
        : $"{span.TotalDays:0.#} days";
}
