using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Push;

/// <summary>
/// Dispatches pending game push notification deliveries to the configured endpoints.
/// 派发待处理的游戏推送通知发送到配置的端点。
/// </summary>
public class GamePushDispatcher(AppDbContext context, NapCatClient client, ILogger<GamePushDispatcher> logger)
{
    internal const int MaxAttempts = 5;

    public async Task<int> DispatchPendingAsync(CancellationToken token = default)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = await context.GamePushDeliveries.AsNoTracking()
            .Where(item => item.Status == PushDeliveryStatus.Pending && item.NextAttemptAtUtc <= now &&
                (item.LeaseExpiresAtUtc == null || item.LeaseExpiresAtUtc <= now))
            .OrderBy(item => item.Id).Take(20).ToArrayAsync(token);
        foreach (var item in pending)
        {
            var claimTime = DateTimeOffset.UtcNow;
            var lease = Guid.NewGuid();
            // Compare-and-set prevents two application instances from sending the same row concurrently.
            var claimed = await context.GamePushDeliveries.Where(delivery => delivery.Id == item.Id &&
                    delivery.Status == PushDeliveryStatus.Pending && delivery.AttemptCount == item.AttemptCount &&
                    (delivery.LeaseExpiresAtUtc == null || delivery.LeaseExpiresAtUtc <= claimTime))
                .ExecuteUpdateAsync(update => update.SetProperty(delivery => delivery.LeaseToken, lease)
                    .SetProperty(delivery => delivery.LeaseExpiresAtUtc, claimTime.AddSeconds(30))
                    .SetProperty(delivery => delivery.AttemptCount, delivery =>
                        delivery.AttemptCount < MaxAttempts ? delivery.AttemptCount + 1 : delivery.AttemptCount), token);
            if (claimed == 0) continue;
            var attempt = item.AttemptCount + 1;
            var status = PushDeliveryStatus.Sent;
            string? error = null;
            DateTimeOffset? sentAt = null;
            var nextAttempt = DateTimeOffset.UtcNow;
            try
            {
                var config = await context.GamePushConfigs.AsNoTracking()
                    .SingleOrDefaultAsync(config => config.GameId == item.GameId, token);
                if (config is null || !config.Enabled || config.Revision != item.ConfigRevision ||
                    !(item.SubmissionResult.HasValue ? config.IncludesSubmission(item.Type, item.SubmissionResult.Value) : config.Includes(item.Type)))
                    status = PushDeliveryStatus.Cancelled;
                else if (attempt > MaxAttempts)
                {
                    status = PushDeliveryStatus.Failed;
                    error = "The final delivery attempt expired before it could be acknowledged.";
                }
                else
                {
                    await client.SendAsync(config, item.Message, token);
                    sentAt = DateTimeOffset.UtcNow;
                }
            }
            catch (NapCatDeliveryException exception)
            {
                error = exception.Message;
                status = exception.Retryable && attempt < MaxAttempts ? PushDeliveryStatus.Pending : PushDeliveryStatus.Failed;
                nextAttempt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, 5 * Math.Pow(2, attempt - 1)));
                logger.LogWarning("QQ push for game {GameId}, delivery {DeliveryId}, attempt {Attempt}: {Error}",
                    item.GameId, item.Id, attempt, error);
            }
            await context.GamePushDeliveries.Where(delivery => delivery.Id == item.Id && delivery.LeaseToken == lease)
                .ExecuteUpdateAsync(update => update.SetProperty(delivery => delivery.Status, status)
                    .SetProperty(delivery => delivery.LastError, error)
                    .SetProperty(delivery => delivery.SentAtUtc, sentAt)
                    .SetProperty(delivery => delivery.NextAttemptAtUtc, nextAttempt)
                    .SetProperty(delivery => delivery.LeaseExpiresAtUtc, (DateTimeOffset?)null), token);
        }
        return pending.Length;
    }
}

/// <summary>
/// Background service that continuously dispatches pending game push notification deliveries.
/// 持续派发待处理的游戏推送通知发送的后台服务。
/// </summary>
public class GamePushWorker(IServiceScopeFactory scopeFactory, ILogger<GamePushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<GamePushDispatcher>().DispatchPendingAsync(stoppingToken);
                if (count == 0) await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to process queued QQ game messages.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
