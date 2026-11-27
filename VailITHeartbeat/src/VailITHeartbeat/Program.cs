// VailITHeartbeat - watches the VailIT dashboard from the outside and emails you when it stops answering.
//
// Commands:
//   VailITHeartbeat              run as a long-lived service (Windows service, systemd, or console)
//   VailITHeartbeat once         one check round, then exit (for Task Scheduler, cron, GitHub Actions)
//                                exit code 0 = all healthy, 1 = something failing, 2 = bad config
//   VailITHeartbeat test-email   send a test email to check the SMTP settings
//
// Settings come from appsettings.json next to the .exe, then environment variables
// (e.g. Email__Password, Heartbeat__Targets__0__Url), then the command line.
using VailITHeartbeat;

var command = args.FirstOrDefault(a => !a.StartsWith('-') && !a.Contains('='))?.ToLowerInvariant() ?? "run";

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddWindowsService(o => o.ServiceName = "VailITHeartbeat");
builder.Services.AddSystemd();

builder.Services.Configure<HeartbeatOptions>(builder.Configuration.GetSection("Heartbeat"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

builder.Services.AddSingleton<CertificateCapture>();
builder.Services.AddHttpClient<HealthChecker>()
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var capture = sp.GetRequiredService<CertificateCapture>();
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = capture.Validate,
            AllowAutoRedirect = false, // a redirect to a login page is not "healthy"
        };
    })
    .ConfigureHttpClient(http =>
    {
        http.Timeout = Timeout.InfiniteTimeSpan; // each target sets its own timeout
        http.DefaultRequestHeaders.UserAgent.ParseAdd("VailITHeartbeat/1.0");
    });
builder.Services.AddHttpClient("self-ping");

builder.Services.AddSingleton(sp => new StateStore(
    builder.Configuration["Heartbeat:StateFile"] ?? "heartbeat-state.json",
    sp.GetRequiredService<ILogger<StateStore>>()));
builder.Services.AddSingleton<EmailNotifier>();
builder.Services.AddSingleton<HeartbeatRunner>();

switch (command)
{
    case "run":
        builder.Services.AddHostedService<HeartbeatWorker>();
        await builder.Build().RunAsync();
        return 0;

    case "once":
    {
        using var host = builder.Build();
        var runner = host.Services.GetRequiredService<HeartbeatRunner>();
        var targets = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HeartbeatOptions>>().Value.Targets;
        if (targets.Count == 0) { Console.Error.WriteLine("No targets configured."); return 2; }
        return await runner.RunOnceAsync(CancellationToken.None) ? 0 : 1;
    }

    case "test-email":
    {
        using var host = builder.Build();
        var email = host.Services.GetRequiredService<EmailNotifier>();
        var ok = await email.SendAsync("[VailIT Heartbeat] Test email",
            $"This is a test from VailITHeartbeat on {Environment.MachineName} at {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC. Email settings work.",
            CancellationToken.None);
        Console.WriteLine(ok ? "Test email sent." : "Test email FAILED, see the log above.");
        return ok ? 0 : 1;
    }

    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Use: (no command) | once | test-email");
        return 2;
}
