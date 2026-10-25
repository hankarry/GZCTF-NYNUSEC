using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Edit;

public class GamePushSettingsModel : IValidatableObject
{
    public bool Enabled { get; set; }
    [MaxLength(2048)]
    public string EndpointUrl { get; set; } = string.Empty;
    [MaxLength(20)]
    public string GroupId { get; set; } = string.Empty;
    /// <summary>Leave null or empty to preserve the saved token.</summary>
    [MaxLength(2048)]
    public string? AccessToken { get; set; }
    public bool ClearAccessToken { get; set; }
    public NoticeType[] Events { get; set; } = Enum.GetValues<NoticeType>();
    public AnswerResult[] SubmissionEvents { get; set; } = [];
    [Required, MaxLength(4000)]
    public string SubmissionTemplate { get; set; } = GamePushTemplate.DefaultSubmission;
    [Required, MaxLength(4000)]
    public string NoticeTemplate { get; set; } = GamePushTemplate.DefaultNotice;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((Enabled || !string.IsNullOrWhiteSpace(EndpointUrl)) && !IsValidEndpoint(EndpointUrl))
            yield return new("Enter an HTTP(S) NapCat API base URL without credentials, query, or fragment.",
                [nameof(EndpointUrl)]);
        if ((Enabled || !string.IsNullOrWhiteSpace(GroupId)) && !IsValidGroupId(GroupId))
            yield return new("Enter a valid QQ group ID (5 to 20 digits, starting with 1-9).", [nameof(GroupId)]);
        if (Events is null || Events.Any(type => !Enum.IsDefined(type)))
            yield return new("Choose valid game notice types.", [nameof(Events)]);
        else if (Enabled && Events.Length == 0 && SubmissionEvents is { Length: 0 })
            yield return new("Select at least one event to push.", [nameof(Events)]);
        if (SubmissionEvents is null || SubmissionEvents.Any(result => !GamePushConfig.SubmissionResults.Contains(result)))
            yield return new("Choose valid submission results.", [nameof(SubmissionEvents)]);
        if (!GamePushTemplate.IsValid(SubmissionTemplate))
            yield return new("Enter a submission template with supported placeholders (maximum 4000 characters).", [nameof(SubmissionTemplate)]);
        if (!GamePushTemplate.IsValid(NoticeTemplate))
            yield return new("Enter a notice template with supported placeholders (maximum 4000 characters).", [nameof(NoticeTemplate)]);
        if (AccessToken?.Any(char.IsControl) is true)
            yield return new("The access token cannot contain control characters.", [nameof(AccessToken)]);
    }

    /// <summary>
    /// Validates the NapCat HTTP API endpoint URL.
    /// 验证 NapCat HTTP API 端点 URL 的有效性。
    /// The URL must be absolute, use http or https scheme, and not contain user info, query, or fragment.
    /// URL 必须是绝对的，使用 http 或 https 方案，并且不包含用户信息、查询或片段。
    /// </summary>
    /// <param name="endpoint">The endpoint URL to validate.</param>
    /// <returns>True if the endpoint is valid; otherwise, false.</returns>
    internal static bool IsValidEndpoint(string? endpoint) =>
        Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    /// <summary>
    /// Validates the QQ group ID.
    /// 验证 QQ 群号的有效性。
    /// The group ID must be a string of 5 to 20 digits, starting with a digit from 1 to 9.
    /// 群号必须是一个由 5 到 20 位数字组成的字符串，且首位数字为 1 到 9。
    /// </summary>
    /// <param name="groupId">The QQ group ID to validate.</param>
    /// <returns>True if the group ID is valid; otherwise, false.</returns>
    internal static bool IsValidGroupId(string? groupId) =>
        groupId?.Trim() is { Length: >= 5 and <= 20 } value && value[0] is >= '1' and <= '9' &&
        value.All(character => character is >= '0' and <= '9');
}

/// <summary>
/// Represents the configuration for game push notifications.
/// 表示游戏推送通知的配置。
/// </summary>
public class GamePushConfigModel
{
    public bool Enabled { get; set; }
    public string EndpointUrl { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public bool HasAccessToken { get; set; }
    public NoticeType[] Events { get; set; } = Enum.GetValues<NoticeType>();
    public AnswerResult[] SubmissionEvents { get; set; } = [];
    public string SubmissionTemplate { get; set; } = GamePushTemplate.DefaultSubmission;
    public string NoticeTemplate { get; set; } = GamePushTemplate.DefaultNotice;

    internal static GamePushConfigModel FromConfig(GamePushConfig config) => new()
    {
        Enabled = config.Enabled, EndpointUrl = config.EndpointUrl, GroupId = config.GroupId,
        HasAccessToken = !string.IsNullOrEmpty(config.ProtectedAccessToken),
        Events = Enum.GetValues<NoticeType>().Where(config.Includes).ToArray(),
        SubmissionEvents = GamePushConfig.SubmissionResults.Where(config.Includes).ToArray(),
        SubmissionTemplate = config.SubmissionTemplate, NoticeTemplate = config.NoticeTemplate
    };
}

/// <summary>
/// Represents a game push notification delivery record.
/// 表示游戏推送通知的发送记录。
/// </summary>
public class GamePushDeliveryModel
{
    public int Id { get; set; }
    public int? NoticeId { get; set; }
    public int? SubmissionId { get; set; }
    public AnswerResult? SubmissionResult { get; set; }
    public string Message { get; set; } = string.Empty;
    public NoticeType Type { get; set; }
    public PushDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }
    public string? LastError { get; set; }
}
