namespace CalendarBooking.Services;

/// <summary>
/// Background worker that keeps standing weekly rules materialized into upcoming slots, rolling
/// the horizon forward over time. Idempotent (clashing occurrences are skipped), so running it
/// repeatedly only fills new gaps.
/// </summary>
public class AvailabilityRuleMaterializer(
    IServiceScopeFactory scopeFactory,
    ILogger<AvailabilityRuleMaterializer> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var rules = scope.ServiceProvider.GetRequiredService<AvailabilityRuleService>();
                await rules.MaterializeAllAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Weekly-rule materialization failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
