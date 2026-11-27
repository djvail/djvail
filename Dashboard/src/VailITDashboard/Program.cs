// VailITDashboard - entry point.
//
// Receives reports from VailITMonitor services, shows every server grouped by type, lets you drill into
// one machine, keeps per-machine (and per-group) thresholds, and emails when something needs a look.
//
//   VailITDashboard.exe                          run (as a Windows service, or in the console if started by hand)
//   VailITDashboard.exe --Dashboard:SeedDemoData=true    fill an empty database with demo servers
//   VailITDashboard.exe hash-password            make a PasswordHash for Dashboard:Auth:Users
//
// Settings live in appsettings.json next to the .exe. Guide: Dashboard/README.md.

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using VailITDashboard;
using VailITDashboard.Alerts;
using VailITDashboard.Api;
using VailITDashboard.Security;
using VailITDashboard.Tools;
using VailITDashboard.Workers;

if (args.FirstOrDefault()?.Equals("hash-password", StringComparison.OrdinalIgnoreCase) == true)
{
    Console.Write("Password: ");
    var password = ReadHidden();
    Console.WriteLine();
    Console.WriteLine("Add this to Dashboard:Auth:Users in appsettings.json:");
    Console.WriteLine($"  {{ \"UserName\": \"admin\", \"PasswordHash\": \"{PasswordHasher.Hash(password)}\" }}");
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Services start in C:\Windows\System32, so always look for appsettings.json next to the .exe.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Host.UseWindowsService(o => o.ServiceName = "VailITDashboard");   // no-op when not running as a service

builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection(DashboardOptions.SectionName));

// --- Our own classes. Each asks for what it needs in its constructor. ---
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<MachineStore>();
builder.Services.AddSingleton<ApiKeyStore>();
builder.Services.AddSingleton<ThresholdStore>();
builder.Services.AddSingleton<AlertStore>();
builder.Services.AddSingleton<AlertNotifier>();
builder.Services.AddSingleton<AlertEngine>();
builder.Services.AddSingleton<StatusService>();
builder.Services.AddSingleton<DemoData>();
builder.Services.AddHostedService<EmailWorker>();
builder.Services.AddHostedService<WatchdogWorker>();

// --- Who may open the pages (Dashboard:Auth:Mode). The /api endpoints use API keys and are always open to them. ---
var auth = builder.Configuration.GetSection(DashboardOptions.SectionName).Get<DashboardOptions>()?.Auth ?? new AuthOptions();
switch (auth.Mode.ToLowerInvariant())
{
    case "windows":
        builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
        builder.Services.AddAuthorization(o =>
        {
            var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();
            if (auth.AllowedWindowsUsersOrGroups.Length > 0)
                policy.RequireAssertion(ctx => auth.AllowedWindowsUsersOrGroups.Any(n =>
                    string.Equals(ctx.User.Identity?.Name, n, StringComparison.OrdinalIgnoreCase) || ctx.User.IsInRole(n)));
            o.FallbackPolicy = policy.Build();
        });
        break;
    case "none":
        builder.Services.AddAuthorization();
        break;
    default:   // "password"
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.LoginPath = "/login";
            o.Cookie.Name = "VailITDashboard";
            o.Cookie.HttpOnly = true;
            o.ExpireTimeSpan = TimeSpan.FromHours(Math.Max(1, auth.SessionHours));
            o.SlidingExpiration = true;
            // Scripts calling /api get a plain 401 instead of being sent to the sign-in page.
            o.Events.OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            };
        });
        builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        break;
}

// --- Web plumbing ---
builder.Services.AddRazorPages();
builder.Services.AddProblemDetails();
builder.Services.AddRequestDecompression();   // monitors gzip their catch-up batches
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (ctx, _) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "30";
        return ValueTask.CompletedTask;
    };
    // Each API key gets its own allowance per minute.
    o.AddPolicy(ReportingEndpoints.RateLimitPolicy, http =>
    {
        int limit = http.RequestServices.GetRequiredService<IOptions<DashboardOptions>>().Value.MaxReportsPerMinutePerKey;
        string partition = http.Request.Headers["X-Api-Key"].ToString() is { Length: > 0 } k ? ApiKeyStore.Hash(k) : "no-key";
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, limit),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

var app = builder.Build();

// --- Database: create/upgrade tables, add default thresholds the first time, optional demo data ---
app.Services.GetRequiredService<Database>().EnsureCreated();
app.Services.GetRequiredService<ThresholdStore>().SeedDefaultsOnce();
if (app.Services.GetRequiredService<IOptions<DashboardOptions>>().Value.SeedDemoData)
    app.Services.GetRequiredService<DemoData>().SeedIfEmpty();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseRequestDecompression();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();
app.MapReportingApi();

app.Logger.LogInformation("VailITDashboard started. Database: {Path}", app.Services.GetRequiredService<Database>().DatabasePath);
if (auth.Mode.Equals("None", StringComparison.OrdinalIgnoreCase))
    app.Logger.LogWarning("Dashboard:Auth:Mode is None: anyone who can reach this site can change thresholds and keys");
app.Run();

static string ReadHidden()
{
    if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
    var sb = new System.Text.StringBuilder();
    while (true)
    {
        var k = Console.ReadKey(intercept: true);
        if (k.Key == ConsoleKey.Enter) return sb.ToString();
        if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
        else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
    }
}
