using System.Net;
using System.Text;
using System.Threading.Channels;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace VailITDashboard.Alerts;

/// <summary>
/// Queues alert emails so a slow or broken mail server never slows down a monitor's report.
/// The <see cref="EmailWorker"/> sends them in the background.
/// </summary>
public sealed class AlertNotifier(IOptions<DashboardOptions> options)
{
    private readonly Channel<MimeMessage> _queue = Channel.CreateBounded<MimeMessage>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<MimeMessage> Reader => _queue.Reader;

    /// <summary>Queues one email for a batch of events about the same machine.</summary>
    public void Enqueue(IReadOnlyList<AlertEvent> events)
    {
        var o = options.Value;
        if (!o.Alerts.EmailEnabled || o.Alerts.To.Length == 0 || events.Count == 0) return;
        _queue.Writer.TryWrite(Build(events, o));
    }

    /// <summary>Queues a test email (from the Alerts page).</summary>
    public void EnqueueTest()
    {
        var o = options.Value;
        var msg = NewMessage(o, $"{o.Alerts.SubjectPrefix} Test email from VailIT Dashboard");
        msg.Body = new TextPart("plain") { Text = $"This is a test email from VailIT Dashboard at {o.PublicUrl}.\nIf you can read it, alert emails will reach you." };
        _queue.Writer.TryWrite(msg);
    }

    // ------------------------------------------------------------------ building the email (edit freely)

    private static MimeMessage Build(IReadOnlyList<AlertEvent> events, DashboardOptions o)
    {
        var machine = events[0].Machine;
        var worst = events.OrderByDescending(e => e.Change == AlertChange.Resolved ? -1 : (int)e.Level).First();
        string headline = worst.Change == AlertChange.Resolved ? "RESOLVED" : worst.Level.ToString().ToUpperInvariant();
        string extra = events.Count > 1 ? $" (+{events.Count - 1} more)" : "";
        string subject = $"{o.Alerts.SubjectPrefix} {headline} {machine.Name} ({machine.GroupName}): {Title(worst)}{extra}";

        string link = $"{o.PublicUrl.TrimEnd('/')}/machines/{machine.MachineId}";

        var text = new StringBuilder();
        text.AppendLine($"{machine.Name} ({machine.GroupName}) needs a look.").AppendLine();
        foreach (var e in events) text.AppendLine($"  {Verb(e),-10} {Title(e)}");
        text.AppendLine().AppendLine($"Details: {link}").AppendLine($"Sent {DateTime.Now:yyyy-MM-dd HH:mm} by VailIT Dashboard.");

        var html = new StringBuilder();
        html.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1f2937\">");
        html.Append($"<p><b>{Enc(machine.Name)}</b> ({Enc(machine.GroupName)}) needs a look.</p><table cellpadding=\"6\" style=\"border-collapse:collapse\">");
        foreach (var e in events)
        {
            string colour = e.Change == AlertChange.Resolved ? "#15803d" : e.Level == AlertLevel.Critical ? "#b91c1c" : "#b45309";
            html.Append($"<tr><td style=\"color:{colour};font-weight:600\">{Enc(Verb(e))}</td><td>{Enc(Title(e))}</td></tr>");
        }
        html.Append($"</table><p><a href=\"{Enc(link)}\">Open {Enc(machine.Name)} in the dashboard</a></p>");
        html.Append($"<p style=\"color:#6b7280;font-size:12px\">Sent {DateTime.Now:yyyy-MM-dd HH:mm} by VailIT Dashboard.</p></div>");

        var msg = NewMessage(o, subject);
        msg.Body = new BodyBuilder { TextBody = text.ToString(), HtmlBody = html.ToString() }.ToMessageBody();
        return msg;
    }

    private static string Verb(AlertEvent e) => e.Change switch
    {
        AlertChange.Resolved => "Resolved",
        AlertChange.Reminder => "Still " + e.Level.ToString().ToLowerInvariant(),
        AlertChange.Escalated => "Now critical",
        _ => e.Level.ToString(),
    };

    private static string Title(AlertEvent e) => e.Change == AlertChange.Resolved
        ? $"{MetricCatalog.Label(e.Key)} back to normal" + (e.Value is double v && e.Key.Metric != AlertEngine.OfflineMetric
            ? $" ({Format.Value(v, MetricCatalog.Unit(e.Key.Metric))})" : "")
        : e.Message;

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    private static MimeMessage NewMessage(DashboardOptions o, string subject)
    {
        var msg = new MimeMessage { Subject = subject };
        msg.From.Add(MailboxAddress.Parse(o.Alerts.From));
        foreach (var to in o.Alerts.To) msg.To.Add(MailboxAddress.Parse(to));
        return msg;
    }
}

/// <summary>Sends queued emails over SMTP, or writes them to the log when no SMTP host is set.</summary>
public sealed class EmailWorker(AlertNotifier notifier, IOptions<DashboardOptions> options, ILogger<EmailWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in notifier.Reader.ReadAllAsync(stoppingToken))
        {
            var smtp = options.Value.Alerts.Smtp;
            if (string.IsNullOrWhiteSpace(smtp.Host))
            {
                log.LogWarning("No SMTP host set, so this email was not sent. To: {To} Subject: {Subject}", msg.To, msg.Subject);
                continue;
            }

            // Try a few times, then give up on this one email (the alert itself is still on the dashboard).
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await SendAsync(msg, smtp, stoppingToken);
                    log.LogInformation("Emailed {To}: {Subject}", msg.To, msg.Subject);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Sending email failed (attempt {Attempt} of 3): {Subject}", attempt, msg.Subject);
                    if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(30 * attempt), stoppingToken);
                }
            }
        }
    }

    private static async Task SendAsync(MimeMessage msg, SmtpOptions smtp, CancellationToken ct)
    {
        var security = smtp.Security.ToLowerInvariant() switch
        {
            "none" => SecureSocketOptions.None,
            "sslonconnect" or "ssl" => SecureSocketOptions.SslOnConnect,
            "auto" => SecureSocketOptions.Auto,
            _ => SecureSocketOptions.StartTls,
        };

        using var client = new SmtpClient { Timeout = 30_000 };
        await client.ConnectAsync(smtp.Host, smtp.Port, security, ct);
        if (!string.IsNullOrEmpty(smtp.UserName))
            await client.AuthenticateAsync(smtp.UserName, smtp.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}
