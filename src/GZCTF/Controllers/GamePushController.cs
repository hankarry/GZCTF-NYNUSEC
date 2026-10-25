using GZCTF.Middlewares;
using GZCTF.Models.Request.Edit;
using GZCTF.Services.Push;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[ApiController]
[RequireAdmin]
[Route("api/edit/games/{gameId:int}/push")]
public class GamePushController(AppDbContext context, IDataProtectionProvider protectionProvider, NapCatClient napCat) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(GamePushConfigModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(int gameId, CancellationToken token)
    {
        if (!await context.Games.AnyAsync(game => game.Id == gameId, token)) return NotFound();
        var config = await context.GamePushConfigs.AsNoTracking().SingleOrDefaultAsync(config => config.GameId == gameId, token);
        return Ok(config is null ? new GamePushConfigModel() : GamePushConfigModel.FromConfig(config));
    }

    [HttpPut]
    [ProducesResponseType(typeof(GamePushConfigModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(int gameId, [FromBody] GamePushSettingsModel model, CancellationToken token)
    {
        if (!await context.Games.AnyAsync(game => game.Id == gameId, token)) return NotFound();
        var config = await context.GamePushConfigs.SingleOrDefaultAsync(config => config.GameId == gameId, token);
        if (config is null)
        {
            config = new() { GameId = gameId };
            context.GamePushConfigs.Add(config);
        }
        var endpoint = model.EndpointUrl.Trim().TrimEnd('/');
        var groupId = model.GroupId.Trim();
        if (config.EndpointUrl != endpoint || config.GroupId != groupId || model.ClearAccessToken ||
            !string.IsNullOrEmpty(model.AccessToken))
            config.Revision = Guid.CreateVersion7();
        config.Enabled = model.Enabled;
        config.EndpointUrl = endpoint;
        config.GroupId = groupId;
        config.EventMask = model.Events.Aggregate(0, (mask, type) => mask | (1 << (int)type));
        config.SubmissionEventMask = model.SubmissionEvents.Aggregate(0, (mask, result) => mask | GamePushConfig.SubmissionBit(result));
        config.SubmissionTemplate = model.SubmissionTemplate;
        config.NoticeTemplate = model.NoticeTemplate;
        if (model.ClearAccessToken) config.ProtectedAccessToken = null;
        else if (!string.IsNullOrEmpty(model.AccessToken))
            // 加密AccessToken以便存储在数据库中，防止泄露，使用IDataProtectionProvider进行加密和解密
            config.ProtectedAccessToken = protectionProvider.CreateProtector(NapCatClient.TokenPurpose).Protect(model.AccessToken);
        await context.SaveChangesAsync(token);
        return Ok(GamePushConfigModel.FromConfig(config));
    }

    /// <summary>Send one test message using the saved destination.</summary>
    [HttpPost("test")]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Test(int gameId, CancellationToken token)
    {
        var config = await context.GamePushConfigs.AsNoTracking().SingleOrDefaultAsync(config => config.GameId == gameId, token);
        if (config is null) return BadRequest(new RequestResponse("Save the QQ push settings first."));
        var title = await context.Games.Where(game => game.Id == gameId).Select(game => game.Title).SingleAsync(token);
        try
        {
            await napCat.SendAsync(config, $"【{title}】\n✅ GZCTF QQ 群消息推送测试成功。", token);
            return Ok(new RequestResponse("Test message sent."));
        }
        catch (NapCatDeliveryException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new RequestResponse(exception.Message));
        }
    }

    [HttpGet("deliveries")]
    [ProducesResponseType(typeof(GamePushDeliveryModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeliveries(int gameId, CancellationToken token)
    {
        if (!await context.Games.AnyAsync(game => game.Id == gameId, token)) return NotFound();
        return Ok(await context.GamePushDeliveries.AsNoTracking().Where(delivery => delivery.GameId == gameId)
            .OrderByDescending(delivery => delivery.Id).Take(20).Select(delivery => new GamePushDeliveryModel
            {
                Id = delivery.Id, NoticeId = delivery.NoticeId, SubmissionId = delivery.SubmissionId,
                SubmissionResult = delivery.SubmissionResult, Message = delivery.Message,
                Type = delivery.Type, Status = delivery.Status,
                AttemptCount = delivery.AttemptCount, CreatedAtUtc = delivery.CreatedAtUtc,
                SentAtUtc = delivery.SentAtUtc, LastError = delivery.LastError
            }).ToArrayAsync(token));
    }
}
