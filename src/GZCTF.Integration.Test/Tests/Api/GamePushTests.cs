using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Edit;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Push;
using GZCTF.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(GamePushTestCollection))]
public class GamePushTests(GamePushTestFactory factory)
{
    private const string Password = "Push@Test123";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new DateTimeOffsetJsonConverter() } };

    private async Task<(HttpClient Client, int GameId)> Setup(NoticeType[]? events = null)
    {
        factory.NapCat.Requests.Clear();
        factory.NapCat.Responses.Clear();
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Admin);
        var game = await TestDataSeeder.CreateGameAsync(factory.Services, "QQ push test");
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/account/login",
            new LoginModel { UserName = admin.UserName, Password = Password })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(Url(game.Id), Settings(events))).EnsureSuccessStatusCode();
        return (client, game.Id);
    }

    private static string Url(int id) => $"/api/edit/games/{id}/push";
    private static GamePushSettingsModel Settings(NoticeType[]? events = null) => new()
    {
        Enabled = true, EndpointUrl = "http://napcat.test/onebot", GroupId = "123456789", AccessToken = "test-access-token",
        Events = events ?? Enum.GetValues<NoticeType>()
    };

    private async Task AddNotice(int gameId, NoticeType type)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IGameNoticeRepository>().AddNotice(new()
        {
            GameId = gameId, Type = type, Values = ["Team [CQ:at,qq=all]", "Challenge B"]
        });
    }

    private async Task<int> Dispatch()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GamePushDispatcher>().DispatchPendingAsync();
    }

    private async Task<Submission> Submit(int gameId, int challengeId, int participationId, int teamId, Guid userId, string answer)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var submission = new Submission
        {
            GameId = gameId, ChallengeId = challengeId, ParticipationId = participationId,
            TeamId = teamId, UserId = userId, Answer = answer, Status = AnswerResult.FlagSubmitted,
            SubmitTimeUtc = DateTimeOffset.UtcNow
        };
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return await db.Submissions.AsNoTracking().Include(submission => submission.Game).SingleAsync(s => s.Id == submission.Id);
    }

    private async Task<GamePushDelivery> CheckAndWaitForPush(Submission submission)
    {
        await factory.Services.GetRequiredService<ChannelWriter<Submission>>().WriteAsync(submission);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var scope = factory.Services.CreateScope();
            var delivery = await scope.ServiceProvider.GetRequiredService<AppDbContext>().GamePushDeliveries.AsNoTracking()
                .SingleOrDefaultAsync(delivery => delivery.SubmissionId == submission.Id);
            if (delivery is not null) return delivery;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Submission {submission.Id} did not produce a push delivery.");
    }

    [Fact]
    public async Task Upgrade_PreservesExistingDeliveriesAndInitializesTemplates()
    {
#pragma warning disable EF1003 // The generated schema identifier is escaped by the provider; identifiers cannot be SQL parameters.
        using var scope = factory.Services.CreateScope();
        var mainDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schema = "push_upgrade_" + Guid.NewGuid().ToString("N");
        var schemaIdentifier = mainDb.GetService<ISqlGenerationHelper>().DelimitIdentifier(schema);
        await mainDb.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + schemaIdentifier);
        var connection = new NpgsqlConnectionStringBuilder(mainDb.Database.GetConnectionString()) { SearchPath = schema };
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options);
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261025070744_AddGameQQPush");
            var game = new Game
            {
                Title = "Existing game", StartTimeUtc = DateTimeOffset.UtcNow.AddHours(-1),
                EndTimeUtc = DateTimeOffset.UtcNow.AddHours(1)
            };
            db.Games.Add(game);
            var notices = new[] { new GameNotice { Game = game, Type = NoticeType.Normal }, new GameNotice { Game = game, Type = NoticeType.ThirdBlood } };
            db.GameNotices.AddRange(notices);
            await db.SaveChangesAsync();
            var revision = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "GamePushConfigs" ("GameId", "Enabled", "EndpointUrl", "GroupId", "EventMask", "Revision")
                VALUES ({game.Id}, true, {"http://napcat.test"}, {"12345"}, 127, {revision})
                """);
            foreach (var notice in notices)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "GamePushDeliveries" ("NoticeId", "GameId", "ConfigRevision", "Type", "Message", "Status", "AttemptCount", "CreatedAtUtc", "NextAttemptAtUtc", "LeaseToken")
                    VALUES ({notice.Id}, {game.Id}, {revision}, {(int)notice.Type}, {"existing message"}, 0, 0, {notice.PublishTimeUtc}, {notice.PublishTimeUtc}, {Guid.Empty})
                    """);
            await migrator.MigrateAsync();
            var config = await db.GamePushConfigs.SingleAsync();
            Assert.Equal(GamePushTemplate.DefaultSubmission, config.SubmissionTemplate);
            Assert.Equal(GamePushTemplate.DefaultNotice, config.NoticeTemplate);
            Assert.Equal(0, config.SubmissionEventMask);
            Assert.Equal("12345", config.GroupId);
            var deliveries = await db.GamePushDeliveries.ToArrayAsync();
            Assert.Equal(2, deliveries.Select(delivery => delivery.Id).Distinct().Count());
            Assert.All(deliveries, delivery => Assert.Equal("existing message", delivery.Message));
            Assert.Equal(notices.Select(notice => notice.Id).Order(), deliveries.Select(delivery => delivery.NoticeId!.Value).Order());
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
            await mainDb.Database.ExecuteSqlRawAsync("DROP SCHEMA " + schemaIdentifier + " CASCADE");
        }
#pragma warning restore EF1003
    }

    [Fact]
    public async Task AllSubmissions_IncludeMembersMedalsAndTwentySeventhSolveWithoutDuplicateBloodMessages()
    {
        var (client, gameId) = await Setup();
        using var adminClient = client;
        var settings = Settings();
        settings.SubmissionEvents = [AnswerResult.Accepted, AnswerResult.WrongAnswer, AnswerResult.CheatDetected, AnswerResult.NotFound];
        (await adminClient.PutAsJsonAsync(Url(gameId), settings)).EnsureSuccessStatusCode();
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, gameId, "Flag challenge", "flag{secret_not_in_push}");
        Submission? lastSubmission = null;
        for (var rank = 1; rank <= 27; rank++)
        {
            var member = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password);
            var team = await TestDataSeeder.CreateTeamAsync(factory.Services, member.Id, $"SolveTeam{gameId}_{rank}");
            var participation = await TestDataSeeder.JoinGameAsync(factory.Services, gameId, team.Id, member.Id);
            var submission = await Submit(gameId, challenge.Id, participation.Id, team.Id, member.Id, challenge.Flag);
            var delivery = await CheckAndWaitForPush(submission);
            var suffix = rank switch { 1 => "🥇 一血", 2 => "🥈 二血", 3 => "🥉 三血", _ => $"第{rank}名" };
            Assert.Equal($"【QQ push test】[提交:通过 ✅] 团队「{team.Name}」成员「{member.UserName}」题目「Flag challenge」{suffix}", delivery.Message);
            lastSubmission = submission;
        }
        var last = lastSubmission!;
        var wrong = await Submit(gameId, challenge.Id, last.ParticipationId, last.TeamId, last.UserId!.Value, "wrong{secret}");
        Assert.Contains("[提交:错误 ❌]", (await CheckAndWaitForPush(wrong)).Message);
        var repeated = await Submit(gameId, challenge.Id, last.ParticipationId, last.TeamId, last.UserId!.Value, challenge.Flag);
        var repeatDelivery = await CheckAndWaitForPush(repeated);
        Assert.EndsWith("题目「Flag challenge」", repeatDelivery.Message);
        Assert.DoesNotContain("🥇", repeatDelivery.Message);

        // Replaying a finalized submission does not insert another delivery.
        using (var scope = factory.Services.CreateScope())
        {
            repeated.Status = AnswerResult.Accepted;
            await scope.ServiceProvider.GetRequiredService<GamePushQueue>().EnqueueSubmissionAsync(repeated, SubmissionType.Normal);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(29, await db.GamePushDeliveries.CountAsync(delivery => delivery.GameId == gameId));
            Assert.Equal(27, await db.FirstSolves.CountAsync(solve => solve.ChallengeId == challenge.Id));
            Assert.Equal(3, await db.GameNotices.CountAsync(notice => notice.GameId == gameId));
            Assert.All(await db.GamePushDeliveries.Where(delivery => delivery.GameId == gameId).ToArrayAsync(), delivery =>
            {
                Assert.Null(delivery.NoticeId);
                Assert.DoesNotContain("secret", delivery.Message);
            });
        }
        while (await Dispatch() == 20) { }
        Assert.Equal(29, factory.NapCat.Requests.Count);
    }

    [Fact]
    public async Task ConcurrentBloodSubmissions_AreAwardedDistinctMedalsAndSentWithoutAllSubmissionsEnabled()
    {
        var (client, gameId) = await Setup();
        using var adminClient = client;
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, gameId, "Concurrent challenge", "flag{race}");
        var submissions = new List<Submission>();
        for (var index = 0; index < 2; index++)
        {
            var member = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password);
            var team = await TestDataSeeder.CreateTeamAsync(factory.Services, member.Id, TestDataSeeder.RandomName());
            var participation = await TestDataSeeder.JoinGameAsync(factory.Services, gameId, team.Id, member.Id);
            submissions.Add(await Submit(gameId, challenge.Id, participation.Id, team.Id, member.Id, challenge.Flag));
        }
        var deliveries = await Task.WhenAll(submissions.Select(CheckAndWaitForPush));
        Assert.Single(deliveries, delivery => delivery.Message.EndsWith("🥇 一血"));
        Assert.Single(deliveries, delivery => delivery.Message.EndsWith("🥈 二血"));
        await Dispatch();
        Assert.Equal(2, factory.NapCat.Requests.Count);
    }

    [Fact]
    public async Task TemplatesAndSubmissionFilters_ArePersistedAppliedAndCancelExcludedResults()
    {
        var (client, gameId) = await Setup();
        using var adminClient = client;
        var settings = Settings([]);
        settings.SubmissionEvents = [AnswerResult.WrongAnswer, AnswerResult.CheatDetected, AnswerResult.NotFound];
        settings.SubmissionTemplate = "{{提交}} {game}|{result}|{team}|{member}|{challenge}|{rank}|{submission_id}";
        settings.NoticeTemplate = "{game}: {event} -> {content}";
        (await adminClient.PutAsJsonAsync(Url(gameId), settings)).EnsureSuccessStatusCode();
        var saved = (await adminClient.GetFromJsonAsync<GamePushConfigModel>(Url(gameId)))!;
        Assert.Equal(settings.SubmissionTemplate, saved.SubmissionTemplate);
        Assert.Equal(settings.NoticeTemplate, saved.NoticeTemplate);
        Assert.Equal(settings.SubmissionEvents, saved.SubmissionEvents);
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, gameId, "Template challenge", "flag{template}");
        var member = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password);
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, member.Id, TestDataSeeder.RandomName());
        var participation = await TestDataSeeder.JoinGameAsync(factory.Services, gameId, team.Id, member.Id);
        var accepted = await Submit(gameId, challenge.Id, participation.Id, team.Id, member.Id, challenge.Flag);
        using (var scope = factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IGameInstanceRepository>().VerifyAnswer(accepted);
            accepted.Status = result.AnsRes;
            await scope.ServiceProvider.GetRequiredService<GamePushQueue>().EnqueueSubmissionAsync(accepted, result.SubType);
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().GamePushDeliveries.AnyAsync(d => d.SubmissionId == accepted.Id));
        }
        var wrong = await Submit(gameId, challenge.Id, participation.Id, team.Id, member.Id, "invalid");
        var delivery = await CheckAndWaitForPush(wrong);
        Assert.Equal($"{{提交}} QQ push test|错误 ❌|{team.Name}|{member.UserName}|Template challenge||{wrong.Id}", delivery.Message);
        // Verify all remaining terminal result filters without fabricating public announcements.
        foreach (var result in new[] { AnswerResult.CheatDetected, AnswerResult.NotFound })
        {
            var submission = await Submit(gameId, challenge.Id, participation.Id, team.Id, member.Id, "private flag");
            submission.Status = result;
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<GamePushQueue>().EnqueueSubmissionAsync(submission, SubmissionType.Unaccepted);
        }
        settings.AccessToken = null;
        settings.SubmissionEvents = [AnswerResult.NotFound];
        settings.Events = [NoticeType.Normal];
        (await adminClient.PutAsJsonAsync(Url(gameId), settings)).EnsureSuccessStatusCode();
        await AddNotice(gameId, NoticeType.Normal);
        await Dispatch();
        Assert.Equal(2, factory.NapCat.Requests.Count);
        var history = (await adminClient.GetFromJsonAsync<GamePushDeliveryModel[]>(Url(gameId) + "/deliveries", JsonOptions))!;
        Assert.Equal(2, history.Count(row => row.Status == PushDeliveryStatus.Cancelled));
        Assert.Contains(history, row => row.Message == "QQ push test: 比赛公告 -> 📢 比赛公告：Challenge B");
        Assert.Contains(history, row => row.SubmissionResult == AnswerResult.NotFound && row.Status == PushDeliveryStatus.Sent);
        settings.SubmissionTemplate = "{unsupported_variable}";
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PutAsJsonAsync(Url(gameId), settings)).StatusCode);
    }

    [Fact]
    public async Task AdminConfiguration_ProtectsTokenAndForbidsOtherUsers()
    {
        var (client, gameId) = await Setup();
        using var adminClient = client;
        var response = await adminClient.GetStringAsync(Url(gameId));
        Assert.DoesNotContain("test-access-token", response);
        Assert.DoesNotContain("protectedAccessToken", response);
        Assert.True((await adminClient.GetFromJsonAsync<GamePushConfigModel>(Url(gameId)))!.HasAccessToken);
        using (var scope = factory.Services.CreateScope())
        {
            var config = await scope.ServiceProvider.GetRequiredService<AppDbContext>().GamePushConfigs.SingleAsync(c => c.GameId == gameId);
            Assert.NotEqual("test-access-token", config.ProtectedAccessToken);
        }

        (await adminClient.PostAsync(Url(gameId) + "/test", null)).EnsureSuccessStatusCode();
        var request = Assert.Single(factory.NapCat.Requests);
        Assert.Equal("/onebot/send_group_msg", request.Path);
        Assert.Equal("Bearer test-access-token", request.Authorization);
        using (var json = JsonDocument.Parse(request.Body))
        {
            Assert.Equal("123456789", json.RootElement.GetProperty("group_id").GetString());
            Assert.Contains("推送测试成功", json.RootElement.GetProperty("message")[0].GetProperty("data").GetProperty("text").GetString());
        }

        var settings = Settings();
        settings.AccessToken = null;
        (await adminClient.PutAsJsonAsync(Url(gameId), settings)).EnsureSuccessStatusCode();
        Assert.True((await adminClient.GetFromJsonAsync<GamePushConfigModel>(Url(gameId)))!.HasAccessToken);
        settings.ClearAccessToken = true;
        (await adminClient.PutAsJsonAsync(Url(gameId), settings)).EnsureSuccessStatusCode();
        Assert.False((await adminClient.GetFromJsonAsync<GamePushConfigModel>(Url(gameId)))!.HasAccessToken);

        foreach (var role in new[] { Role.User, Role.Monitor })
        {
            var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password, role: role);
            using var ordinaryClient = factory.CreateClient();
            (await ordinaryClient.PostAsJsonAsync("/api/account/login",
                new LoginModel { UserName = user.UserName, Password = Password })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Forbidden, (await ordinaryClient.GetAsync(Url(gameId))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ordinaryClient.PutAsJsonAsync(Url(gameId), settings)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ordinaryClient.PostAsync(Url(gameId) + "/test", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ordinaryClient.GetAsync(Url(gameId) + "/deliveries")).StatusCode);
        }
        Assert.Single(factory.NapCat.Requests);
    }

    [Fact]
    public async Task GameEvents_AreQueuedOnceFilteredAndDeliveredAsText()
    {
        var (client, gameId) = await Setup();
        using var adminClient = client;
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, gameId, "Push challenge", "flag{push}");
        var editUrl = $"/api/edit/games/{gameId}/challenges/{challenge.Id}";
        foreach (var enabled in new[] { false, false, true, true })
            (await adminClient.PutAsJsonAsync(editUrl, new { isEnabled = enabled })).EnsureSuccessStatusCode();
        (await adminClient.PutAsJsonAsync(editUrl, new { hints = new[] { "released hint" }, hintEnabled = new[] { true } }))
            .EnsureSuccessStatusCode();
        (await adminClient.PutAsJsonAsync(editUrl, new { title = "Updated push challenge" })).EnsureSuccessStatusCode();
        (await adminClient.PostAsJsonAsync($"/api/edit/games/{gameId}/notices", new GameNoticeModel { Content = "比赛公告 [CQ:at,qq=all]" }))
            .EnsureSuccessStatusCode();
        foreach (var type in new[] { NoticeType.FirstBlood, NoticeType.SecondBlood, NoticeType.ThirdBlood })
            await AddNotice(gameId, type);

        var otherGame = await TestDataSeeder.CreateGameAsync(factory.Services, "Not configured");
        await AddNotice(otherGame.Id, NoticeType.ThirdBlood);
        var futureGame = await TestDataSeeder.CreateGameAsync(factory.Services, "Future game", start: DateTimeOffset.UtcNow.AddHours(1));
        (await adminClient.PutAsJsonAsync(Url(futureGame.Id), Settings())).EnsureSuccessStatusCode();
        await AddNotice(futureGame.Id, NoticeType.ThirdBlood);

        using (var scope = factory.Services.CreateScope())
        {
            var pending = await scope.ServiceProvider.GetRequiredService<AppDbContext>().GamePushDeliveries.AsNoTracking().ToArrayAsync();
            var forGame = pending.Where(delivery => delivery.GameId == gameId).ToArray();
            Assert.Equal(7, forGame.Length);
            Assert.Equal(Enum.GetValues<NoticeType>().OrderBy(type => type), forGame.Select(delivery => delivery.Type).OrderBy(type => type));
            Assert.DoesNotContain(pending, delivery => delivery.GameId == otherGame.Id || delivery.GameId == futureGame.Id);
        }
        Assert.Empty(factory.NapCat.Requests);
        await Dispatch();
        Assert.Equal(7, factory.NapCat.Requests.Count);
        foreach (var request in factory.NapCat.Requests)
        {
            using var json = JsonDocument.Parse(request.Body);
            var segment = Assert.Single(json.RootElement.GetProperty("message").EnumerateArray());
            Assert.Equal("text", segment.GetProperty("type").GetString());
            Assert.Contains("QQ push test", segment.GetProperty("data").GetProperty("text").GetString());
        }
        await Dispatch();
        Assert.Equal(7, factory.NapCat.Requests.Count);
        var history = (await adminClient.GetFromJsonAsync<GamePushDeliveryModel[]>(Url(gameId) + "/deliveries", JsonOptions))!;
        Assert.Equal(7, history.Length);
        Assert.All(history, delivery => Assert.Equal(PushDeliveryStatus.Sent, delivery.Status));

        var filtered = Settings([NoticeType.ThirdBlood]);
        filtered.AccessToken = null;
        (await adminClient.PutAsJsonAsync(Url(gameId), filtered)).EnsureSuccessStatusCode();
        await AddNotice(gameId, NoticeType.FirstBlood);
        await AddNotice(gameId, NoticeType.ThirdBlood);
        filtered.Enabled = false;
        (await adminClient.PutAsJsonAsync(Url(gameId), filtered)).EnsureSuccessStatusCode();
        await Dispatch();
        Assert.Equal(7, factory.NapCat.Requests.Count);
        history = (await adminClient.GetFromJsonAsync<GamePushDeliveryModel[]>(Url(gameId) + "/deliveries", JsonOptions))!;
        Assert.Equal(8, history.Length);
        Assert.Equal(PushDeliveryStatus.Cancelled, history[0].Status);
    }

    [Fact]
    public async Task RolledBackNotice_IsNeverDelivered()
    {
        var (client, gameId) = await Setup();
        using var adminClient = client;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IGameNoticeRepository>().AddNotice(
                new() { GameId = gameId, Type = NoticeType.ThirdBlood, Values = ["Team A", "Challenge B"] }, publish: false);
            Assert.Equal(1, await db.GamePushDeliveries.CountAsync(delivery => delivery.GameId == gameId));
            await transaction.RollbackAsync();
        }
        await Dispatch();
        Assert.Empty(factory.NapCat.Requests);
        Assert.Empty((await adminClient.GetFromJsonAsync<GamePushDeliveryModel[]>(Url(gameId) + "/deliveries", JsonOptions))!);
    }

    [Fact]
    public async Task FailedDelivery_RetriesAndSuccessfulRowsAreNotSentTwice()
    {
        var (client, gameId) = await Setup();
        using var adminClient = client;
        await AddNotice(gameId, NoticeType.ThirdBlood);
        factory.NapCat.Responses.Enqueue(HttpStatusCode.ServiceUnavailable);
        await Dispatch();
        var delivery = Assert.Single((await adminClient.GetFromJsonAsync<GamePushDeliveryModel[]>(Url(gameId) + "/deliveries", JsonOptions))!);
        Assert.Equal(PushDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Contains("503", delivery.LastError);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().GamePushDeliveries
                .Where(item => item.GameId == gameId).ExecuteUpdateAsync(update =>
                    update.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        await Task.WhenAll(Dispatch(), Dispatch());
        delivery = Assert.Single((await adminClient.GetFromJsonAsync<GamePushDeliveryModel[]>(Url(gameId) + "/deliveries", JsonOptions))!);
        Assert.Equal(PushDeliveryStatus.Sent, delivery.Status);
        Assert.Equal(2, delivery.AttemptCount);
        Assert.Null(delivery.LastError);
        Assert.Equal(2, factory.NapCat.Requests.Count);
    }

    [Fact]
    public async Task ChangingDestination_CancelsPreviouslyQueuedMessages()
    {
        var (client, gameId) = await Setup();
        using var adminClient = client;
        await AddNotice(gameId, NoticeType.ThirdBlood);
        var settings = Settings();
        settings.GroupId = "987654321";
        (await adminClient.PutAsJsonAsync(Url(gameId), settings)).EnsureSuccessStatusCode();
        await Dispatch();
        Assert.Empty(factory.NapCat.Requests);
        Assert.Equal(PushDeliveryStatus.Cancelled,
            Assert.Single((await adminClient.GetFromJsonAsync<GamePushDeliveryModel[]>(Url(gameId) + "/deliveries", JsonOptions))!).Status);
    }

    [Fact]
    public async Task RepeatedFailures_StopAfterFiveAttempts()
    {
        var (client, gameId) = await Setup();
        using var adminClient = client;
        await AddNotice(gameId, NoticeType.ThirdBlood);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            factory.NapCat.Responses.Enqueue(HttpStatusCode.ServiceUnavailable);
            using (var scope = factory.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().GamePushDeliveries
                    .Where(item => item.GameId == gameId).ExecuteUpdateAsync(update =>
                        update.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
            await Dispatch();
            var delivery = Assert.Single((await adminClient.GetFromJsonAsync<GamePushDeliveryModel[]>(Url(gameId) + "/deliveries", JsonOptions))!);
            Assert.Equal(attempt, delivery.AttemptCount);
            Assert.Equal(attempt < 5 ? PushDeliveryStatus.Pending : PushDeliveryStatus.Failed, delivery.Status);
        }
        await Dispatch();
        Assert.Equal(5, factory.NapCat.Requests.Count);
    }

    [Fact]
    public async Task ExpiredFinalLease_IsMarkedFailedWithoutAnotherSend()
    {
        var (client, gameId) = await Setup();
        using var adminClient = client;
        await AddNotice(gameId, NoticeType.ThirdBlood);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().GamePushDeliveries
                .Where(item => item.GameId == gameId).ExecuteUpdateAsync(update =>
                    update.SetProperty(item => item.AttemptCount, 5)
                        .SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        await Dispatch();
        var delivery = Assert.Single((await adminClient.GetFromJsonAsync<GamePushDeliveryModel[]>(Url(gameId) + "/deliveries", JsonOptions))!);
        Assert.Equal(PushDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(5, delivery.AttemptCount);
        Assert.Empty(factory.NapCat.Requests);
    }
}

[CollectionDefinition(nameof(GamePushTestCollection), DisableParallelization = true)]
public class GamePushTestCollection : ICollectionFixture<GamePushTestFactory>;

public class GamePushTestFactory : GZCTFApplicationFactory
{
    public RecordingNapCatHandler NapCat { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            // Use the real dispatcher with deterministic test scheduling and an entirely fake NapCat HTTP transport.
            foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                         descriptor.ImplementationType == typeof(GamePushWorker)).ToArray())
                services.Remove(descriptor);
            services.AddHttpClient<NapCatClient>().ConfigurePrimaryHttpMessageHandler(() => NapCat)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        });
    }
}

public class RecordingNapCatHandler : HttpMessageHandler
{
    public record RecordedRequest(string Path, string? Authorization, string Body);
    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();
    public ConcurrentQueue<HttpStatusCode> Responses { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests.Enqueue(new(request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(),
            await request.Content!.ReadAsStringAsync(token)));
        return new(Responses.TryDequeue(out var status) ? status : HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\",\"retcode\":0,\"data\":{\"message_id\":1}}", Encoding.UTF8, "application/json")
        };
    }
}
