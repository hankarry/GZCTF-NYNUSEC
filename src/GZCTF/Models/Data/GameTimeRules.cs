using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

public class GameScheduledTask
{
    public int Id { get; set; }
    public int GameId { get; set; }
    [MaxLength(100)] public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool AtGameStart { get; set; }
    public DateTimeOffset? ExecuteAtUtc { get; set; }
    public int[] ChallengeIds { get; set; } = [];
    public bool EnableChallenges { get; set; }
    public bool Notify { get; set; } = true;
    [MaxLength(4000)] public string Message { get; set; } = string.Empty;
    public DateTimeOffset? ExecutedAtUtc { get; set; }
    [MaxLength(512)] public string? LastError { get; set; }
    public Game Game { get; set; } = null!;
}

public class GameBlackout
{
    public int Id { get; set; }
    public int GameId { get; set; }
    [MaxLength(100)] public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool Daily { get; set; }
    public DateTimeOffset? StartAtUtc { get; set; }
    public DateTimeOffset? EndAtUtc { get; set; }
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
    public bool Notify { get; set; } = true;
    [MaxLength(4000)] public string Message { get; set; } = "非比赛时间段，暂停答题，已创建的容器将关闭。";
    public DateTimeOffset? LastStartedAtUtc { get; set; }
    public Game Game { get; set; } = null!;
}
