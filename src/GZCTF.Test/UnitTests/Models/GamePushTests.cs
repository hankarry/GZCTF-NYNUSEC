using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Edit;
using GZCTF.Services.Push;
using GZCTF.Utils;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public class GamePushTests
{
    [Theory]
    [InlineData("http://127.0.0.1:3000", "123456789", true)]
    [InlineData("https://napcat.example/onebot/", "12345", true)]
    [InlineData("file:///secret", "12345", false)]
    [InlineData("http://user:secret@localhost:3000", "12345", false)]
    [InlineData("http://localhost:3000?access_token=secret", "12345", false)]
    [InlineData("http://localhost:3000#fragment", "12345", false)]
    [InlineData("http://localhost:3000", "12oops", false)]
    [InlineData("http://localhost:3000", "123456789012345678901", false)]
    public void Settings_ValidateDestination(string endpoint, string groupId, bool valid)
    {
        var model = new GamePushSettingsModel { Enabled = true, EndpointUrl = endpoint, GroupId = groupId };
        Assert.Equal(valid, Validate(model));
    }

    [Fact]
    public void DisabledDefaultsAndEventSelection_AreValidated()
    {
        Assert.True(Validate(new()));
        Assert.False(Validate(new() { Enabled = true }));
        Assert.False(Validate(new() { Events = [(NoticeType)255] }));
        Assert.False(Validate(new() { AccessToken = "token\r\nInjected: value" }));
        Assert.False(Validate(new() { Enabled = true, EndpointUrl = "http://localhost", GroupId = "12345", Events = [] }));
        var config = new GamePushConfig { EventMask = 1 << (int)NoticeType.ThirdBlood };
        Assert.True(config.Includes(NoticeType.ThirdBlood));
        Assert.False(config.Includes(NoticeType.FirstBlood));
        Assert.False(config.Includes((NoticeType)255));
    }

    [Theory]
    [InlineData(NoticeType.FirstBlood, "一血")]
    [InlineData(NoticeType.SecondBlood, "二血")]
    [InlineData(NoticeType.ThirdBlood, "三血")]
    [InlineData(NoticeType.NewChallenge, "题目上线")]
    [InlineData(NoticeType.ChallengeClosed, "题目下线")]
    [InlineData(NoticeType.NewHint, "提示启用")]
    [InlineData(NoticeType.Normal, "比赛公告")]
    public void Messages_IncludeGameAndEvent(NoticeType type, string label)
    {
        var message = GamePushQueue.FormatMessage("Test contest", new GameNotice { Type = type, Values = ["Team A", "Challenge B"] });
        Assert.Contains("Test contest", message);
        Assert.Contains(label, message);
        Assert.Contains("UTC+8", message);
    }

    [Theory]
    [InlineData(NoticeType.FirstBlood, 1, "🥇 一血")]
    [InlineData(NoticeType.SecondBlood, 2, "🥈 二血")]
    [InlineData(NoticeType.ThirdBlood, 3, "🥉 三血")]
    [InlineData(NoticeType.Normal, 27, "第27名")]
    [InlineData(NoticeType.Normal, null, "")]
    public void SubmissionTemplate_FormatsMedalsAndSolveOrder(NoticeType type, int? rank, string suffix)
    {
        var submission = new Submission
        {
            Status = AnswerResult.Accepted, User = new() { UserName = "成员名" },
            Team = new() { Name = "队伍名" }, GameChallenge = new() { Title = "题目名" }
        };
        Assert.Equal($"【NYNUCTF-S7】[提交:通过 ✅] 团队「队伍名」成员「成员名」题目「题目名」{suffix}",
            GamePushTemplate.FormatSubmission(GamePushTemplate.DefaultSubmission, "NYNUCTF-S7", submission, type, rank));
    }

    [Fact]
    public void Templates_ValidateAndReplaceOnceWithoutInterpretingValues()
    {
        Assert.True(GamePushTemplate.IsValid("{{literal}} {game} {member}"));
        foreach (var template in new[] { "{unknown}", "{game", "game}", " ", new string('x', 4001) })
            Assert.False(Validate(new() { SubmissionTemplate = template }));
        Assert.Equal("{literal} {member} [CQ:at,qq=all]", GamePushTemplate.Render("{{literal}} {game} {member}",
            new Dictionary<string, string> { ["game"] = "{member}", ["member"] = "[CQ:at,qq=all]" }));
        Assert.False(Validate(new() { SubmissionEvents = [AnswerResult.FlagSubmitted] }));
        Assert.True(Validate(new()
        {
            Enabled = true, EndpointUrl = "http://napcat.test", GroupId = "12345", Events = [],
            SubmissionEvents = [AnswerResult.WrongAnswer]
        }));
    }

    [Fact]
    public async Task Client_UsesBearerTokenAndSafeTextSegments()
    {
        var provider = new EphemeralDataProtectionProvider();
        var handler = new StubHandler(HttpStatusCode.OK, "{\"status\":\"ok\",\"retcode\":0}");
        using var httpClient = new HttpClient(handler);
        var client = new NapCatClient(httpClient, provider);
        const string message = "Contest [CQ:at,qq=all] & <text>";
        await client.SendAsync(new()
        {
            EndpointUrl = "http://napcat.test/onebot", GroupId = "123456789",
            ProtectedAccessToken = provider.CreateProtector(NapCatClient.TokenPurpose).Protect("test-token")
        }, message);
        Assert.Equal("/onebot/send_group_msg", handler.Path);
        Assert.Equal("Bearer test-token", handler.Authorization);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("123456789", json.RootElement.GetProperty("group_id").GetString());
        var segment = Assert.Single(json.RootElement.GetProperty("message").EnumerateArray());
        Assert.Equal("text", segment.GetProperty("type").GetString());
        Assert.Equal(message, segment.GetProperty("data").GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(200, "{\"status\":\"failed\",\"retcode\":1200}", true)]
    [InlineData(200, "{\"status\":\"ok\"}", true)]
    [InlineData(200, "{\"status\":42,\"retcode\":0}", true)]
    [InlineData(200, "[]", true)]
    [InlineData(200, "invalid json", true)]
    [InlineData(401, "token rejected", false)]
    [InlineData(302, "", false)]
    [InlineData(500, "error", true)]
    public async Task Client_RequiresSuccessfulHttpAndOneBotAcknowledgement(int status, string body, bool retryable)
    {
        using var httpClient = new HttpClient(new StubHandler((HttpStatusCode)status, body));
        var client = new NapCatClient(httpClient, new EphemeralDataProtectionProvider());
        var exception = await Assert.ThrowsAsync<NapCatDeliveryException>(() => client.SendAsync(
            new() { EndpointUrl = "http://napcat.test", GroupId = "123456789" }, "Test"));
        Assert.Equal(retryable, exception.Retryable);
    }

    [Fact]
    public void ConfigResponse_DoesNotExposeTokenAndMigrationMatchesModel()
    {
        var model = GamePushConfigModel.FromConfig(new() { ProtectedAccessToken = "protected-secret" });
        Assert.True(model.HasAccessToken);
        var json = JsonSerializer.Serialize(model);
        Assert.DoesNotContain("protected-secret", json);
        Assert.DoesNotContain("ProtectedAccessToken", json);
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql().Options);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static bool Validate(GamePushSettingsModel model) => Validator.TryValidateObject(model,
        new ValidationContext(model), new List<ValidationResult>(), true);

    private class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Path = request.RequestUri!.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(token);
            return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
