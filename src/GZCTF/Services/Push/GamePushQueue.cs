using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Push;

public class GamePushQueue(AppDbContext context)
{
    /// <summary>Stage an outbox row; the caller saves it atomically with the notice.</summary>
    public async Task EnqueueAsync(GameNotice notice, CancellationToken token = default, bool allowOutsideGame = false)
    {
        var config = await context.GamePushConfigs.AsNoTracking()
            .SingleOrDefaultAsync(config => config.GameId == notice.GameId && config.Enabled, token);
        if (config is null || !config.Includes(notice.Type)) return;
        var game = await context.Games.AsNoTracking().SingleAsync(game => game.Id == notice.GameId, token);
        if (!game.IsActive && !allowOutsideGame) return;
        context.GamePushDeliveries.Add(new()
        {
            GameId = notice.GameId, Notice = notice, Type = notice.Type, ConfigRevision = config.Revision,
            Message = FormatMessage(game.Title, notice, config.NoticeTemplate)
        });
    }

    /// <summary>Persist the final result and its outbox row together, once per submission.</summary>
    public async Task EnqueueSubmissionAsync(Submission item, SubmissionType type, CancellationToken token = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})", [-2, item.Id], token);
        var submission = await context.Submissions.Include(submission => submission.Game)
            .SingleAsync(submission => submission.Id == item.Id, token);
        submission.Status = item.Status;
        var config = await context.GamePushConfigs.AsNoTracking()
            .SingleOrDefaultAsync(config => config.GameId == submission.GameId && config.Enabled, token);
        var noticeType = type switch
        {
            SubmissionType.FirstBlood => NoticeType.FirstBlood, SubmissionType.SecondBlood => NoticeType.SecondBlood,
            SubmissionType.ThirdBlood => NoticeType.ThirdBlood, _ => NoticeType.Normal
        };
        if (config is not null && submission.Game!.IsActive && config.IncludesSubmission(noticeType, submission.Status) &&
            !await context.GamePushDeliveries.AnyAsync(delivery => delivery.SubmissionId == item.Id, token))
        {
            int? rank = null;
            if (submission.Status == AnswerResult.Accepted &&
                await context.FirstSolves.AnyAsync(solve => solve.SubmissionId == submission.Id, token))
            {
                // Count first solves, never repeated correct submissions. Break timestamp ties by submission ID.
                rank = await context.FirstSolves.Where(solve => solve.ChallengeId == submission.ChallengeId &&
                    solve.Participation.Status == ParticipationStatus.Accepted &&
                    solve.Submission.SubmitTimeUtc >= submission.Game.StartTimeUtc &&
                    solve.Submission.SubmitTimeUtc < submission.Game.EndTimeUtc &&
                    (solve.Submission.SubmitTimeUtc < submission.SubmitTimeUtc ||
                     (solve.Submission.SubmitTimeUtc == submission.SubmitTimeUtc && solve.SubmissionId <= submission.Id)))
                    .CountAsync(token);
                if (rank == 0) rank = null;
            }
            context.GamePushDeliveries.Add(new()
            {
                GameId = submission.GameId, SubmissionId = submission.Id, SubmissionResult = submission.Status,
                Type = noticeType, ConfigRevision = config.Revision,
                Message = GamePushTemplate.FormatSubmission(config.SubmissionTemplate, submission.Game.Title, submission, noticeType, rank)
            });
        }
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    internal static string FormatMessage(string gameTitle, GameNotice notice, string template = GamePushTemplate.DefaultNotice)
    {
        var values = notice.Values ?? [];
        var title = values.ElementAtOrDefault(0) ?? "";
        var challenge = values.ElementAtOrDefault(1) ?? "";
        var content = notice.Type switch
        {
            NoticeType.FirstBlood => $"🩸 一血：队伍「{title}」率先解出「{challenge}」！",
            NoticeType.SecondBlood => $"🩸 二血：队伍「{title}」解出「{challenge}」！",
            NoticeType.ThirdBlood => $"🩸 三血：队伍「{title}」解出「{challenge}」！",
            NoticeType.NewChallenge => $"🟢 题目上线：「{title}」已开放。",
            NoticeType.ChallengeClosed => $"🔴 题目下线：「{title}」已关闭。",
            NoticeType.NewHint => $"💡 提示启用：「{title}」发布了新提示，请查看题目。",
            _ => $"📢 比赛公告：{values.LastOrDefault() ?? ""}"
        };
        var eventName = notice.Type switch
        {
            NoticeType.FirstBlood => "一血", NoticeType.SecondBlood => "二血", NoticeType.ThirdBlood => "三血",
            NoticeType.NewChallenge => "题目上线", NoticeType.ChallengeClosed => "题目下线", NoticeType.NewHint => "提示启用", _ => "比赛公告"
        };
        return GamePushTemplate.Render(template, new Dictionary<string, string>
        {
            ["game"] = gameTitle, ["event"] = eventName, ["content"] = content,
            ["challenge"] = notice.Type == NoticeType.Normal ? "" : notice.Type is NoticeType.FirstBlood or NoticeType.SecondBlood or NoticeType.ThirdBlood ? challenge : title,
            ["team"] = notice.Type is NoticeType.FirstBlood or NoticeType.SecondBlood or NoticeType.ThirdBlood ? title : "",
            ["time"] = notice.PublishTimeUtc.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
        });
    }
}
