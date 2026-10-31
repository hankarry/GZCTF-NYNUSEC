using GZCTF.Models.Request.Edit;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public class GameTimePolicy(AppDbContext db)
{
    public const int BlockedCode = 460;
    public const string BlockedMessage = "非比赛时间段";

    public async Task<bool> IsBlockedAsync(int gameId, CancellationToken token = default) =>
        (await GetStatusAsync(gameId, DateTimeOffset.UtcNow, token))?.IsBlocked is true;

    public async Task<GamePlayStatusModel?> GetStatusAsync(int gameId, DateTimeOffset now, CancellationToken token = default)
    {
        var game = await db.Games.AsNoTracking().SingleOrDefaultAsync(game => game.Id == gameId, token);
        if (game is null) return null;
        var rules = await db.GameBlackouts.AsNoTracking().Where(rule => rule.GameId == gameId && rule.Enabled).ToArrayAsync(token);
        var windows = rules.SelectMany(rule => Windows(rule, game, now)).OrderBy(window => window.Start).ToArray();
        return new(now, windows.Any(window => window.Start <= now && now < window.End), windows);
    }

    /// <summary>UTC+8 daily intervals, including overnight intervals. Boundaries are [start, end).</summary>
    internal static IEnumerable<BlackoutWindow> Windows(GameBlackout rule, Game game, DateTimeOffset now)
    {
        if (!rule.Enabled) yield break;
        if (!rule.Daily)
        {
            if (rule.StartAtUtc is { } start && rule.EndAtUtc is { } end && Clip(start, end, game) is { } window)
                yield return window;
            yield break;
        }
        var date = new DateTimeOffset(now.ToOffset(TimeSpan.FromHours(8)).Date, TimeSpan.FromHours(8));
        for (var day = -1; day <= 2; day++)
        {
            var start = date.AddDays(day).AddMinutes(rule.StartMinute);
            var end = date.AddDays(day + (rule.EndMinute <= rule.StartMinute ? 1 : 0)).AddMinutes(rule.EndMinute);
            if (Clip(start, end, game) is { } window) yield return window;
        }
    }

    private static BlackoutWindow? Clip(DateTimeOffset start, DateTimeOffset end, Game game)
    {
        start = start > game.StartTimeUtc ? start : game.StartTimeUtc;
        end = end < game.EndTimeUtc ? end : game.EndTimeUtc;
        return start < end ? new(start.ToUniversalTime(), end.ToUniversalTime()) : null;
    }
}
