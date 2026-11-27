using Microsoft.Extensions.Options;

namespace VailITHeartbeat;

/// <summary>
/// One "round": check every target, apply the alert rules, send emails, save state,
/// then ping the optional self-heartbeat URL. Used by both the service loop and 'once'.
/// </summary>
public sealed class HeartbeatRunner
{
    private readonly HeartbeatOptions _options;
    private readonly HealthChecker _checker;
    private readonly EmailNotifier _email;
    private readonly StateStore _store;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<HeartbeatRunner> _log;

    public HeartbeatRunner(IOptions<HeartbeatOptions> options, HealthChecker checker, EmailNotifier email,
        StateStore store, IHttpClientFactory httpFactory, ILogger<HeartbeatRunner> log)
    {
        _options = options.Value;
        _checker = checker;
        _email = email;
        _store = store;
        _httpFactory = httpFactory;
        _log = log;
    }

    /// <returns>True when every target is healthy.</returns>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        if (_options.Targets.Count == 0)
        {
            _log.LogError("No targets configured (Heartbeat:Targets in appsettings.json).");
            return false;
        }

        var states = _store.Load();
        var allHealthy = true;

        foreach (var target in _options.Targets)
        {
            var result = await _checker.CheckAsync(target, ct);
            allHealthy &= result.Healthy;

            if (!states.TryGetValue(target.Url, out var state))
                states[target.Url] = state = new TargetState();

            var now = DateTimeOffset.UtcNow;
            var alert = AlertRules.Apply(state, target, result, now);

            if (result.Healthy)
                _log.LogInformation("{Target}: OK, {Summary}", target.DisplayName, result.Summary);
            else
                _log.LogWarning("{Target}: FAILED ({Count} in a row), {Summary}", target.DisplayName, state.ConsecutiveFailures, result.Summary);

            // If the email fails we do not mark it sent, so the next round tries again.
            if (alert != AlertKind.None && await _email.SendAlertAsync(alert, target, state, result, ct))
                AlertRules.MarkSent(state, alert, now);
        }

        // Forget targets that were removed from the config.
        foreach (var url in states.Keys.Except(_options.Targets.Select(t => t.Url)).ToList())
            states.Remove(url);

        _store.Save(states);
        await PingSelfAsync(ct);
        return allHealthy;
    }

    private async Task PingSelfAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.SelfPingUrl)) return;
        try
        {
            // The ping only says "the checker itself ran". Dashboard problems are reported by email above.
            using var http = _httpFactory.CreateClient("self-ping");
            http.Timeout = TimeSpan.FromSeconds(10);
            using var _ = await http.GetAsync(_options.SelfPingUrl, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning("Self-ping to {Url} failed: {Message}", _options.SelfPingUrl, ex.Message);
        }
    }
}

/// <summary>The long-running service: runs a round every IntervalSeconds.</summary>
public sealed class HeartbeatWorker(HeartbeatRunner runner, IOptions<HeartbeatOptions> options, ILogger<HeartbeatWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(10, options.Value.IntervalSeconds));
        log.LogInformation("VailITHeartbeat started: {Count} target(s), every {Interval}.", options.Value.Targets.Count, interval);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await runner.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Heartbeat round failed unexpectedly; will try again next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
