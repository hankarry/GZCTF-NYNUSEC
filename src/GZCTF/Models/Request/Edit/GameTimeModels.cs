using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Edit;

public class ScheduledTaskInput : IValidatableObject
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool AtGameStart { get; set; }
    public DateTimeOffset? ExecuteAtUtc { get; set; }
    [Required, MinLength(1), MaxLength(500)] public int[] ChallengeIds { get; set; } = [];
    public bool EnableChallenges { get; set; }
    public bool Notify { get; set; } = true;
    [MaxLength(4000)] public string Message { get; set; } = "题目开放状态已按计划更新，请查看题目列表。";

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!AtGameStart && ExecuteAtUtc is null) yield return new("请选择执行时间。", [nameof(ExecuteAtUtc)]);
        if (ChallengeIds?.Any(id => id <= 0) is true) yield return new("请选择有效题目。", [nameof(ChallengeIds)]);
        if (Notify && string.IsNullOrWhiteSpace(Message)) yield return new("通知内容不能为空。", [nameof(Message)]);
    }

    internal void Apply(GameScheduledTask task)
    {
        task.Name = Name.Trim(); task.Enabled = Enabled; task.AtGameStart = AtGameStart;
        task.ExecuteAtUtc = AtGameStart ? null : ExecuteAtUtc?.ToUniversalTime(); task.ChallengeIds = ChallengeIds.Distinct().ToArray();
        task.EnableChallenges = EnableChallenges; task.Notify = Notify; task.Message = Message; task.LastError = null;
    }
}

public class BlackoutInput : IValidatableObject
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool Daily { get; set; }
    public DateTimeOffset? StartAtUtc { get; set; }
    public DateTimeOffset? EndAtUtc { get; set; }
    [Range(0, 1439)] public int StartMinute { get; set; }
    [Range(0, 1439)] public int EndMinute { get; set; }
    public bool Notify { get; set; } = true;
    [MaxLength(4000)] public string Message { get; set; } = "非比赛时间段，暂停答题，已创建的容器将关闭。";

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Daily && StartMinute == EndMinute) yield return new("每日开始和结束时间不能相同。");
        if (!Daily && (StartAtUtc is null || EndAtUtc is null || StartAtUtc >= EndAtUtc))
            yield return new("禁赛结束时间必须晚于开始时间。");
        if (Notify && string.IsNullOrWhiteSpace(Message)) yield return new("通知内容不能为空。", [nameof(Message)]);
    }

    internal void Apply(GameBlackout rule)
    {
        rule.Name = Name.Trim(); rule.Enabled = Enabled; rule.Daily = Daily;
        rule.StartAtUtc = Daily ? null : StartAtUtc?.ToUniversalTime(); rule.EndAtUtc = Daily ? null : EndAtUtc?.ToUniversalTime();
        rule.StartMinute = StartMinute; rule.EndMinute = EndMinute; rule.Notify = Notify; rule.Message = Message;
    }
}

public record ScheduledTaskModel(int Id, string Name, bool Enabled, bool AtGameStart, DateTimeOffset? ExecuteAtUtc,
    int[] ChallengeIds, bool EnableChallenges, bool Notify, string Message, DateTimeOffset? ExecutedAtUtc, string? LastError)
{
    internal static ScheduledTaskModel From(GameScheduledTask task) => new(task.Id, task.Name, task.Enabled, task.AtGameStart,
        task.ExecuteAtUtc, task.ChallengeIds, task.EnableChallenges, task.Notify, task.Message, task.ExecutedAtUtc, task.LastError);
}

public record BlackoutModel(int Id, string Name, bool Enabled, bool Daily, DateTimeOffset? StartAtUtc,
    DateTimeOffset? EndAtUtc, int StartMinute, int EndMinute, bool Notify, string Message)
{
    internal static BlackoutModel From(GameBlackout rule) => new(rule.Id, rule.Name, rule.Enabled, rule.Daily, rule.StartAtUtc,
        rule.EndAtUtc, rule.StartMinute, rule.EndMinute, rule.Notify, rule.Message);
}

public record GameTimeSettingsModel(ScheduledTaskModel[] Tasks, BlackoutModel[] Blackouts);
public record BlackoutWindow(DateTimeOffset Start, DateTimeOffset End);
public record GamePlayStatusModel(DateTimeOffset ServerTime, bool IsBlocked, BlackoutWindow[] Windows);
