using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using GZCTF.Services.Push;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

// <summary>
// A scheduler that executes game time rules, including scheduled tasks and blackout periods.
// 游戏时间调度器，执行游戏时间规则，包括定时任务和黑屏时间段。
// </summary>
public class GameTimeScheduler(AppDbContext db, IGameChallengeRepository challenges, IContainerRepository containers,
    IGameNoticeRepository notices, GamePushQueue pushQueue, CacheHelper cache, ILogger<GameTimeScheduler> logger)
{
    public async Task ProcessAsync(DateTimeOffset now, CancellationToken token = default)
    {
        now = now.ToUniversalTime();
        // Execute scheduled tasks that are due, in order of their scheduled time and ID.
        // 执行到期的定时任务，按其计划时间和ID顺序执行。
        var due = await db.GameScheduledTasks.AsNoTracking().Where(task => task.Enabled && task.ExecutedAtUtc == null &&
            task.Game.StartTimeUtc <= now && task.Game.EndTimeUtc > now &&
            (task.AtGameStart || task.ExecuteAtUtc <= now))
            .OrderBy(task => task.AtGameStart ? task.Game.StartTimeUtc : task.ExecuteAtUtc).ThenBy(task => task.Id)
            .Select(task => task.Id).ToArrayAsync(token);
        foreach (var id in due)
        {
            try { await ExecuteTask(id, now, token); }
            catch (Exception exception) when (!token.IsCancellationRequested)
            {
                logger.LogError(exception, "Failed to execute scheduled game task {TaskId}", id);
                db.ChangeTracker.Clear();
                await db.GameScheduledTasks.Where(task => task.Id == id && task.ExecutedAtUtc == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(task => task.LastError, "任务执行失败，将自动重试，请检查服务日志。"), token);
            }
        }

        // Execute blackout periods.
        // 执行黑屏时间。
        var rules = await db.GameBlackouts.AsNoTracking().Include(rule => rule.Game)
            .Where(rule => rule.Enabled && rule.Game.StartTimeUtc <= now &&
                (rule.Game.EndTimeUtc > now || rule.PendingEndAtUtc != null)).ToArrayAsync(token);
        var blockedGames = new HashSet<int>();
        foreach (var rule in rules)
        {
            var window = GameTimePolicy.Windows(rule, rule.Game, now).FirstOrDefault(window => window.Start <= now && now < window.End);
            if (window is not null) blockedGames.Add(rule.GameId);
            if (window is null && rule.PendingEndAtUtc is null) continue;
            if (window is not null && rule.LastStartedAtUtc == window.Start && rule.PendingEndAtUtc == window.End) continue;
            try { await AnnounceBlackout(rule.Id, now, token); }
            catch (Exception exception) when (!token.IsCancellationRequested)
            {
                logger.LogError(exception, "Failed to announce game blackout {RuleId}", rule.Id);
                db.ChangeTracker.Clear();
            }
        }

        // Retry unsuccessful shutdowns every tick, including containers created just across the boundary.
        var containerIds = await db.GameInstances.IgnoreAutoIncludes().AsNoTracking().Where(instance => instance.ContainerId != null &&
            (!instance.Challenge.IsEnabled || blockedGames.Contains(instance.Participation.GameId)))
            .Select(instance => instance.ContainerId!.Value).Distinct().ToArrayAsync(token);
        foreach (var id in containerIds)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            if (!await TryLock(-5, id.GetHashCode(), token)) continue;
            var container = await db.Containers.SingleOrDefaultAsync(container => container.Id == id, token);
            if (container is not null) await containers.DestroyContainer(container, token);
            await transaction.CommitAsync(token);
        }
    }

    private async Task<bool> TryLock(int category, int id, CancellationToken token) =>
        await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock({0}, {1}) AS \"Value\"", category, id).SingleAsync(token);

    private async Task ExecuteTask(int id, DateTimeOffset now, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // Wait for an earlier worker rather than skipping ahead to a later, conflicting task.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})", [-3, id], token);
        var task = await db.GameScheduledTasks.Include(task => task.Game).SingleOrDefaultAsync(task => task.Id == id, token);
        if (task is null || !task.Enabled || task.ExecutedAtUtc.HasValue || now < task.Game.StartTimeUtc || now >= task.Game.EndTimeUtc ||
            (!task.AtGameStart && task.ExecuteAtUtc > now)) return;
        var selected = await db.GameChallenges.Where(challenge => challenge.GameId == task.GameId && task.ChallengeIds.Contains(challenge.Id)).ToArrayAsync(token);
        if (selected.Length != task.ChallengeIds.Length)
        {
            task.Enabled = false;
            task.LastError = "所选题目已被删除，任务已停用。";
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return;
        }
        foreach (var challenge in selected)
        {
            challenge.IsEnabled = task.EnableChallenges;
            if (task.EnableChallenges) await challenges.EnsureInstances(challenge, task.Game, token);
        }
        task.ExecutedAtUtc = now;
        task.LastError = null;
        GameNotice? notice = null;
        if (task.Notify)
        {
            notice = new() { GameId = task.GameId, Type = NoticeType.Normal, Values = [task.Message], PublishTimeUtc = now };
            await notices.AddNotice(notice, token, publish: false);
        }
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        await cache.FlushScoreboardCache(task.GameId, token);
        if (notice is not null) await notices.PublishNotice(notice, token);
    }

    //<summary>
    // Announces a blackout period if it has not been announced yet.
    // 如果尚未宣布黑屏时间段，则宣布该时间段。
    //</summary>
    private async Task AnnounceBlackout(int id, DateTimeOffset now, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await TryLock(-4, id, token)) return;
        var rule = await db.GameBlackouts.Include(rule => rule.Game).SingleOrDefaultAsync(rule => rule.Id == id, token);
        if (rule is null || !rule.Enabled || rule.Game.StartTimeUtc > now) return;
        var active = GameTimePolicy.Windows(rule, rule.Game, now)
            .FirstOrDefault(window => window.Start <= now && now < window.End);
        var published = new List<GameNotice>();

        if (rule.PendingEndAtUtc.HasValue && rule.LastStartedAtUtc is { } previousStart)
        {
            // Re-read the occurrence from the current configuration so extending or shortening a
            // blackout while it is active also changes when its ending notification is sent.
            var previous = GameTimePolicy.Windows(rule, rule.Game, previousStart)
                .FirstOrDefault(window => window.Start == previousStart);
            rule.PendingEndAtUtc = previous?.End;
            if (previous is not null && previous.End <= now)
            {
                rule.PendingEndAtUtc = null;
                // If a restart missed the gap and another daily occurrence is already active,
                // consume the old end without sending a stale ending notification.
                if (rule.Notify && active is null)
                {
                    var endNotice = new GameNotice
                    {
                        GameId = rule.GameId, Type = NoticeType.Normal,
                        Values = [rule.EndMessage], PublishTimeUtc = now
                    };
                    await notices.AddNotice(endNotice, token, publish: false, enqueuePush: false);
                    // A blackout may end at the same instant as the game itself.
                    await pushQueue.EnqueueAsync(endNotice, token, allowOutsideGame: true);
                    published.Add(endNotice);
                }
            }
        }

        if (active is not null)
        {
            var newOccurrence = rule.LastStartedAtUtc != active.Start;
            rule.LastStartedAtUtc = active.Start;
            rule.PendingEndAtUtc = active.End;
            if (rule.Notify && newOccurrence)
            {
                var startNotice = new GameNotice
                {
                    GameId = rule.GameId, Type = NoticeType.Normal,
                    Values = [rule.Message], PublishTimeUtc = now
                };
                await notices.AddNotice(startNotice, token, publish: false);
                published.Add(startNotice);
            }
        }

        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        foreach (var notice in published) await notices.PublishNotice(notice, token);
    }
}

// <summary>
// A background service that runs the game time scheduler every second.
// 背景服务，每秒运行一次游戏时间调度器。
// </summary>
public class GameTimeWorker(IServiceScopeFactory scopes, ILogger<GameTimeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 检查是否需要停止
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<GameTimeScheduler>().ProcessAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Failed to process game time rules."); }
            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
