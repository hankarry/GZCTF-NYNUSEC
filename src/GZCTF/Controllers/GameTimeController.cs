using GZCTF.Middlewares;
using GZCTF.Models.Request.Edit;
using GZCTF.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/edit/games/{gameId:int}/time")]
public class GameTimeController(AppDbContext db, GameTimePolicy policy) : ControllerBase
{
    [RequireAdmin, HttpGet]
    [ProducesResponseType(typeof(GameTimeSettingsModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(int gameId, CancellationToken token)
    {
        if (!await db.Games.AnyAsync(game => game.Id == gameId, token)) return NotFound();
        var tasks = await db.GameScheduledTasks.AsNoTracking().Where(task => task.GameId == gameId).OrderBy(task => task.Id).ToArrayAsync(token);
        var rules = await db.GameBlackouts.AsNoTracking().Where(rule => rule.GameId == gameId).OrderBy(rule => rule.Id).ToArrayAsync(token);
        return Ok(new GameTimeSettingsModel(tasks.Select(ScheduledTaskModel.From).ToArray(), rules.Select(BlackoutModel.From).ToArray()));
    }

    [RequireAdmin, HttpPost("tasks"), HttpPut("tasks/{id:int}")]
    [ProducesResponseType(typeof(ScheduledTaskModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveTask(int gameId, ScheduledTaskInput model, CancellationToken token, [FromRoute] int id = 0)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (id > 0) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})", [-3, id], token);
        var game = await db.Games.AsNoTracking().SingleOrDefaultAsync(game => game.Id == gameId, token);
        if (game is null) return NotFound();
        var task = id == 0 ? new GameScheduledTask { GameId = gameId } :
            await db.GameScheduledTasks.SingleOrDefaultAsync(task => task.Id == id && task.GameId == gameId, token);
        if (task is null) return NotFound();
        if (task.ExecutedAtUtc.HasValue) return Conflict(new RequestResponse("已执行的任务不可修改，请新建任务。"));
        var time = model.AtGameStart ? game.StartTimeUtc : model.ExecuteAtUtc;
        if (time < game.StartTimeUtc || time >= game.EndTimeUtc || (model.Enabled && time <= DateTimeOffset.UtcNow))
            return BadRequest(new RequestResponse("执行时间须在比赛开始至结束之间，且尚未到达。"));
        if (await db.GameChallenges.CountAsync(challenge => challenge.GameId == gameId && model.ChallengeIds.Contains(challenge.Id), token) != model.ChallengeIds.Distinct().Count())
            return BadRequest(new RequestResponse("计划中的题目必须属于当前比赛。"));
        model.Apply(task);
        if (id == 0) db.GameScheduledTasks.Add(task);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return Ok(ScheduledTaskModel.From(task));
    }

    [RequireAdmin, HttpPost("blackouts"), HttpPut("blackouts/{id:int}")]
    [ProducesResponseType(typeof(BlackoutModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveBlackout(int gameId, BlackoutInput model, CancellationToken token, [FromRoute] int id = 0)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (id > 0) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})", [-4, id], token);
        var game = await db.Games.AsNoTracking().SingleOrDefaultAsync(game => game.Id == gameId, token);
        if (game is null) return NotFound();
        if (!model.Daily && (model.StartAtUtc < game.StartTimeUtc || model.EndAtUtc > game.EndTimeUtc))
            return BadRequest(new RequestResponse("指定日期的禁赛时段须位于比赛时间范围内。"));
        var rule = id == 0 ? new GameBlackout { GameId = gameId } :
            await db.GameBlackouts.SingleOrDefaultAsync(rule => rule.Id == id && rule.GameId == gameId, token);
        if (rule is null) return NotFound();
        model.Apply(rule);
        if (id == 0) db.GameBlackouts.Add(rule);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return Ok(BlackoutModel.From(rule));
    }

    [RequireAdmin, HttpDelete("tasks/{id:int}")]
    public async Task<IActionResult> DeleteTask(int gameId, int id, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})", [-3, id], token);
        await db.GameScheduledTasks.Where(task => task.Id == id && task.GameId == gameId).ExecuteDeleteAsync(token);
        await transaction.CommitAsync(token);
        return NoContent();
    }

    [RequireAdmin, HttpDelete("blackouts/{id:int}")]
    public async Task<IActionResult> DeleteBlackout(int gameId, int id, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})", [-4, id], token);
        await db.GameBlackouts.Where(rule => rule.Id == id && rule.GameId == gameId).ExecuteDeleteAsync(token);
        await transaction.CommitAsync(token);
        return NoContent();
    }

    [RequireUser, HttpGet("~/api/game/{gameId:int}/play-status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(GamePlayStatusModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> PlayStatus(int gameId, CancellationToken token)
    {
        var result = await policy.GetStatusAsync(gameId, DateTimeOffset.UtcNow, token);
        return result is null ? NotFound() : Ok(result);
    }
}
