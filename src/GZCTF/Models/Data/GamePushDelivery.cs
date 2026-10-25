using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GZCTF.Models.Data;

/// <summary>
/// The status of a game push notification delivery.
/// 游戏推送通知的发送状态。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PushDeliveryStatus>))]
public enum PushDeliveryStatus : byte
{
    Pending,
    Sent,
    Failed,
    Cancelled
}

/// <summary>
/// Represents a game push notification delivery record.
/// 表示游戏推送通知的发送记录。
/// </summary>
public class GamePushDelivery
{
    [Key]
    public int Id { get; set; }
    public int? NoticeId { get; set; }
    public int? SubmissionId { get; set; }
    public AnswerResult? SubmissionResult { get; set; }
    public int GameId { get; set; }
    public Guid ConfigRevision { get; set; }
    public NoticeType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public PushDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAtUtc { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public Guid LeaseToken { get; set; }
    [MaxLength(512)]
    public string? LastError { get; set; }
    public GameNotice? Notice { get; set; }
    public Submission? Submission { get; set; }
    public Game Game { get; set; } = null!;
}
