using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GZCTF.Models.Data;

/// <summary>Administrator-only NapCat destination for one game.</summary>
public class GamePushConfig
{
    [Key]
    public int GameId { get; set; }
    public bool Enabled { get; set; }
    [MaxLength(2048)]
    public string EndpointUrl { get; set; } = string.Empty;
    [MaxLength(20)]
    public string GroupId { get; set; } = string.Empty;
    [JsonIgnore]
    public string? ProtectedAccessToken { get; set; }
    public int EventMask { get; set; } = 127;
    public int SubmissionEventMask { get; set; }
    [MaxLength(4000)]
    public string SubmissionTemplate { get; set; } = GamePushTemplate.DefaultSubmission;
    [MaxLength(4000)]
    public string NoticeTemplate { get; set; } = GamePushTemplate.DefaultNotice;
    public Guid Revision { get; set; } = Guid.CreateVersion7();
    public Game Game { get; set; } = null!;

    internal bool Includes(NoticeType type) => Enum.IsDefined(type) && (EventMask & (1 << (int)type)) != 0;

    internal static readonly AnswerResult[] SubmissionResults =
        [AnswerResult.Accepted, AnswerResult.WrongAnswer, AnswerResult.CheatDetected, AnswerResult.NotFound];

    internal bool Includes(AnswerResult result) => SubmissionResults.Contains(result) &&
        (SubmissionEventMask & SubmissionBit(result)) != 0;

    internal static int SubmissionBit(AnswerResult result) => 1 << ((int)result + 1);

    internal bool IncludesSubmission(NoticeType type, AnswerResult result) => Includes(result) ||
        (result == AnswerResult.Accepted && type is NoticeType.FirstBlood or NoticeType.SecondBlood or NoticeType.ThirdBlood && Includes(type));
}
