using System.Globalization;
using System.Text.RegularExpressions;

namespace GZCTF.Utils;

/// <summary>Plain text substitution only; values are never interpreted as templates.</summary>
public static partial class GamePushTemplate
{
    public const string DefaultSubmission = "【{game}】[提交:{result}] 团队「{team}」成员「{member}」题目「{challenge}」{solve_rank}";
    public const string DefaultNotice = "【{game}】\n{content}\n{time} (UTC+8)";
    private static readonly HashSet<string> Variables =
        ["game", "event", "content", "time", "team", "member", "challenge", "result", "rank", "blood", "solve_rank", "submission_id"];

    [GeneratedRegex(@"\{\{|\}\}|\{[a-z_]+\}|[{}]")]
    private static partial Regex Tokens();

    public static bool IsValid(string? template) => !string.IsNullOrWhiteSpace(template) && template.Length <= 4000 &&
        Tokens().Matches(template).All(match => match.Value is "{{" or "}}" ||
            (match.Length > 2 && Variables.Contains(match.Value[1..^1])));

    internal static string Render(string template, IReadOnlyDictionary<string, string> values) =>
        Tokens().Replace(template, match => match.Value switch
        {
            "{{" => "{", "}}" => "}",
            _ => values.GetValueOrDefault(match.Value[1..^1], "")
        });

    internal static string FormatSubmission(string template, string gameTitle, Submission submission, NoticeType type, int? rank)
    {
        var blood = type switch
        {
            NoticeType.FirstBlood => "🥇 一血", NoticeType.SecondBlood => "🥈 二血", NoticeType.ThirdBlood => "🥉 三血", _ => ""
        };
        return Render(template, new Dictionary<string, string>
        {
            ["game"] = gameTitle, ["event"] = "提交", ["team"] = submission.TeamName,
            ["member"] = submission.UserName, ["challenge"] = submission.ChallengeName,
            ["result"] = submission.Status switch
            {
                AnswerResult.Accepted => "通过 ✅", AnswerResult.WrongAnswer => "错误 ❌",
                AnswerResult.CheatDetected => "作弊嫌疑 ⚠️", AnswerResult.NotFound => "实例不存在 ⚠️", _ => "待判定 ⏳"
            },
            ["rank"] = rank?.ToString(CultureInfo.InvariantCulture) ?? "", ["blood"] = blood,
            ["solve_rank"] = blood.Length > 0 ? blood : rank.HasValue ? $"第{rank}名" : "",
            ["time"] = submission.SubmitTimeUtc.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["submission_id"] = submission.Id.ToString(CultureInfo.InvariantCulture)
        });
    }
}
