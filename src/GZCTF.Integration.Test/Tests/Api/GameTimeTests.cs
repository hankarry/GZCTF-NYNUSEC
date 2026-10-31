using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Services;
using GZCTF.Services.Container.Manager;
using GZCTF.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(GameTimeTestCollection))]
public class GameTimeTests(GameTimeTestFactory factory)
{
    private const string Password = "TimeRule@Test123";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new DateTimeOffsetJsonConverter() } };
    private static string Url(int gameId) => $"/api/edit/games/{gameId}/time";

    private async Task<HttpClient> Login(Role role)
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password, role: role);
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/account/login", new LoginModel { UserName = user.UserName, Password = Password })).EnsureSuccessStatusCode();
        return client;
    }
    private async Task Process(DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<GameTimeScheduler>().ProcessAsync(now);
    }

    [Fact]
    public async Task Configuration_ValidatesTimesChallengeOwnershipAndAdminPermissions()
    {
        using var admin = await Login(Role.Admin);
        var start = DateTimeOffset.UtcNow.AddHours(1);
        var game = await TestDataSeeder.CreateGameAsync(factory.Services, "Future timed game", start: start, end: start.AddHours(5));
        var other = await TestDataSeeder.CreateGameAsync(factory.Services, "Other timed game");
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id, "Selected", "flag{test}");
        var foreign = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, other.Id, "Foreign", "flag{test}");
        var input = new ScheduledTaskInput { Name = "Start batch", AtGameStart = true, ChallengeIds = [challenge.Id], EnableChallenges = true };
        var created = await admin.PostAsJsonAsync(Url(game.Id) + "/tasks", input);
        created.EnsureSuccessStatusCode();
        var task = (await created.Content.ReadFromJsonAsync<ScheduledTaskModel>(JsonOptions))!;
        input.ChallengeIds = [foreign.Id];
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Url(game.Id) + $"/tasks/{task.Id}", input)).StatusCode);
        input.ChallengeIds = [challenge.Id]; input.AtGameStart = false; input.ExecuteAtUtc = start.AddMinutes(-1);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Url(game.Id) + "/tasks", input)).StatusCode);
        input.ExecuteAtUtc = start.AddMinutes(20).ToOffset(TimeSpan.FromHours(8));
        input.Notify = false; input.Message = "";
        var updated = await admin.PutAsJsonAsync(Url(game.Id) + $"/tasks/{task.Id}", input);
        updated.EnsureSuccessStatusCode();
        Assert.Equal(TimeSpan.Zero, (await updated.Content.ReadFromJsonAsync<ScheduledTaskModel>(JsonOptions))!.ExecuteAtUtc!.Value.Offset);
        var blackout = new BlackoutInput { Name = "Overnight", Daily = true, StartMinute = 22 * 60, EndMinute = 2 * 60 };
        var ruleResponse = await admin.PostAsJsonAsync(Url(game.Id) + "/blackouts", blackout);
        ruleResponse.EnsureSuccessStatusCode();
        var rule = (await ruleResponse.Content.ReadFromJsonAsync<BlackoutModel>(JsonOptions))!;
        blackout.EndMinute = blackout.StartMinute;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Url(game.Id) + $"/blackouts/{rule.Id}", blackout)).StatusCode);
        using var player = await Login(Role.User);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync(Url(game.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsJsonAsync(Url(game.Id) + "/tasks", input)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.DeleteAsync(Url(game.Id) + $"/blackouts/{rule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync(Url(other.Id) + $"/tasks/{task.Id}", input)).StatusCode);
        (await admin.DeleteAsync(Url(game.Id) + $"/tasks/{task.Id}")).EnsureSuccessStatusCode();
        (await admin.DeleteAsync(Url(game.Id) + $"/blackouts/{rule.Id}")).EnsureSuccessStatusCode();
        var settings = (await admin.GetFromJsonAsync<GameTimeSettingsModel>(Url(game.Id), JsonOptions))!;
        Assert.Empty(settings.Tasks); Assert.Empty(settings.Blackouts);
    }

    [Fact]
    public async Task Tasks_EnableBatchAndCreateInstancesOnceThenDisableAndCloseContainers()
    {
        using var admin = await Login(Role.Admin);
        var game = await TestDataSeeder.CreateGameAsync(factory.Services, "Scheduled batch game");
        var first = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id, "First", "flag{1}");
        var second = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id, "Second", "flag{2}");
        var third = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id, "Unselected", "flag{3}");
        var member = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password);
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, member.Id, TestDataSeeder.RandomName());
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().GameChallenges.Where(c => c.GameId == game.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(c => c.IsEnabled, false));
        var participation = await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, team.Id, member.Id);
        (await admin.PutAsJsonAsync($"/api/edit/games/{game.Id}/push", new GamePushSettingsModel { Enabled = true, EndpointUrl = "http://napcat.test", GroupId = "12345", Events = [NoticeType.Normal] })).EnsureSuccessStatusCode();
        var now = DateTimeOffset.UtcNow;
        int taskId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = new GameScheduledTask { GameId = game.Id, Name = "Opening batch", AtGameStart = true, ChallengeIds = [first.Id, second.Id], EnableChallenges = true, Message = "第一批题目已开放" };
            db.GameScheduledTasks.Add(task); await db.SaveChangesAsync(); taskId = task.Id;
        }
        await Task.WhenAll(Process(now), Process(now));
        await Process(now.AddSeconds(1));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await db.GameChallenges.CountAsync(c => c.GameId == game.Id && c.IsEnabled));
            Assert.False((await db.GameChallenges.SingleAsync(c => c.Id == third.Id)).IsEnabled);
            Assert.Equal(2, await db.GameInstances.CountAsync(i => i.ParticipationId == participation.Id));
            Assert.NotNull((await db.GameScheduledTasks.SingleAsync(t => t.Id == taskId)).ExecutedAtUtc);
            Assert.Equal("第一批题目已开放", Assert.Single(await db.GameNotices.Where(n => n.GameId == game.Id).ToArrayAsync()).Values!.Last());
            Assert.Single(await db.GamePushDeliveries.Where(d => d.GameId == game.Id).ToArrayAsync());
        }
        var container = await AddContainer(participation.Id, first.Id);
        factory.Containers.FailOnce.TryAdd(container, true);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GameScheduledTasks.Add(new() { GameId = game.Id, Name = "Closing batch", ExecuteAtUtc = now, ChallengeIds = [first.Id], EnableChallenges = false, Notify = false });
            await db.SaveChangesAsync();
        }
        await Process(now.AddSeconds(2));
        await Process(now.AddSeconds(3));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Containers.AnyAsync(c => c.Id == container));
            Assert.Single(await db.GameNotices.Where(n => n.GameId == game.Id).ToArrayAsync());
        }
        Assert.Equal(2, factory.Containers.Attempts[container]);
    }

    private async Task<Guid> AddContainer(int participationId, int challengeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instance = await db.GameInstances.SingleAsync(i => i.ParticipationId == participationId && i.ChallengeId == challengeId);
        var container = new Container { Id = Guid.NewGuid(), ContainerId = "fake-time-container", Image = "test", Status = ContainerStatus.Running, GameInstance = instance };
        instance.Container = container;
        db.Containers.Add(container); await db.SaveChangesAsync(); return container.Id;
    }

    [Fact]
    public async Task SpecifiedTasks_WaitUntilDueAndEndedGamesAreNotReplayed()
    {
        var now = DateTimeOffset.UtcNow;
        var game = await TestDataSeeder.CreateGameAsync(factory.Services, "Wait until due");
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id, "Later disabled", "flag{later}");
        var expired = await TestDataSeeder.CreateGameAsync(factory.Services, "Expired schedule", start: now.AddHours(-2), end: now.AddHours(-1));
        var expiredChallenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, expired.Id, "Keep enabled", "flag{expired}");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GameScheduledTasks.AddRange(
                new() { GameId = game.Id, Name = "Later", ExecuteAtUtc = now.AddMinutes(10), ChallengeIds = [challenge.Id], EnableChallenges = false, Notify = false },
                new() { GameId = expired.Id, Name = "Expired", AtGameStart = true, ChallengeIds = [expiredChallenge.Id], EnableChallenges = false, Notify = false });
            await db.SaveChangesAsync();
        }
        await Process(now);
        using (var scope = factory.Services.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<AppDbContext>().GameChallenges.SingleAsync(c => c.Id == challenge.Id)).IsEnabled);
        await Process(now.AddMinutes(10));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False((await db.GameChallenges.SingleAsync(c => c.Id == challenge.Id)).IsEnabled);
            Assert.True((await db.GameChallenges.SingleAsync(c => c.Id == expiredChallenge.Id)).IsEnabled);
            Assert.Null((await db.GameScheduledTasks.SingleAsync(task => task.GameId == expired.Id)).ExecutedAtUtc);
        }
    }

    [Fact]
    public async Task DailyBlackouts_NotifyOnceForEachDayAndRestartDoesNotDuplicate()
    {
        var day = new DateTimeOffset(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date, TimeSpan.FromHours(8));
        var game = await TestDataSeeder.CreateGameAsync(factory.Services, "Daily blackout", start: day.AddDays(-1).ToUniversalTime(), end: day.AddDays(3).ToUniversalTime());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GameBlackouts.Add(new() { GameId = game.Id, Name = "Daily lunch", Daily = true, StartMinute = 12 * 60, EndMinute = 13 * 60, Message = "每日休息通知" });
            await db.SaveChangesAsync();
        }
        await Process(day.AddHours(12));
        await Process(day.AddHours(12).AddMinutes(30));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Single(await db.GameNotices.Where(notice => notice.GameId == game.Id).ToArrayAsync());
            Assert.False((await scope.ServiceProvider.GetRequiredService<GameTimePolicy>().GetStatusAsync(game.Id, day.AddHours(13)))!.IsBlocked);
        }
        await Process(day.AddDays(1).AddHours(12));
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AppDbContext>().GameNotices.CountAsync(notice => notice.GameId == game.Id));
    }

    [Fact]
    public async Task Blackouts_BlockApisCloseExistingContainersNotifyOnceAndResumeAcrossOverlaps()
    {
        using var admin = await Login(Role.Admin);
        var member = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password);
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, member.Id, TestDataSeeder.RandomName());
        var game = await TestDataSeeder.CreateGameAsync(factory.Services, "Blackout API game");
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id, "Paused challenge", "flag{paused}");
        var participation = await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, team.Id, member.Id);
        using var player = factory.CreateClient();
        (await player.PostAsJsonAsync("/api/account/login", new LoginModel { UserName = member.UserName, Password = Password })).EnsureSuccessStatusCode();
        var container = await AddContainer(participation.Id, challenge.Id);
        var otherGame = await TestDataSeeder.CreateGameAsync(factory.Services, "Unblocked game");
        var otherChallenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, otherGame.Id, "Unblocked", "flag{other}");
        var otherMember = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), Password);
        var otherTeam = await TestDataSeeder.CreateTeamAsync(factory.Services, otherMember.Id, TestDataSeeder.RandomName());
        var otherPart = await TestDataSeeder.JoinGameAsync(factory.Services, otherGame.Id, otherTeam.Id, otherMember.Id);
        var otherContainer = await AddContainer(otherPart.Id, otherChallenge.Id);
        var now = DateTimeOffset.UtcNow;
        var rule = new BlackoutInput { Name = "Lunch", StartAtUtc = now.AddMinutes(-1), EndAtUtc = now.AddMinutes(30), Message = "午间暂停，稍后继续" };
        var response = await admin.PostAsJsonAsync(Url(game.Id) + "/blackouts", rule); response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<BlackoutModel>(JsonOptions))!;
        var status = (await player.GetFromJsonAsync<GamePlayStatusModel>($"/api/game/{game.Id}/play-status", JsonOptions))!;
        Assert.True(status.IsBlocked);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ChoiceExams.Add(new() { GameId = game.Id, Enabled = true });
            await db.SaveChangesAsync();
        }
        var choiceBlocked = await player.PostAsync($"/api/game/{game.Id}/choice/attempt", null);
        Assert.Equal(HttpStatusCode.BadRequest, choiceBlocked.StatusCode);
        Assert.Contains("非比赛时间段", (await choiceBlocked.Content.ReadFromJsonAsync<RequestResponse>())!.Title);
        foreach (var path in new[] { $"/api/game/{game.Id}/Container/{challenge.Id}", $"/api/game/{game.Id}/Container/{challenge.Id}/Extend" })
        {
            var blocked = await player.PostAsync(path, null);
            Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
            Assert.Contains("非比赛时间段", (await blocked.Content.ReadFromJsonAsync<RequestResponse>())!.Title);
        }
        var rejected = await player.PostAsJsonAsync($"/api/game/{game.Id}/Challenges/{challenge.Id}", new FlagSubmitModel { Flag = "flag{paused}" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await Task.WhenAll(Process(now), Process(now)); await Process(now.AddSeconds(1));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Containers.AnyAsync(c => c.Id == container));
            Assert.True(await db.Containers.AnyAsync(c => c.Id == otherContainer));
            Assert.False(await db.Submissions.AnyAsync(s => s.GameId == game.Id));
            Assert.Equal("午间暂停，稍后继续", Assert.Single(await db.GameNotices.Where(n => n.GameId == game.Id).ToArrayAsync()).Values!.Last());
            db.GameBlackouts.Add(new() { GameId = game.Id, Name = "Overlap", StartAtUtc = now, EndAtUtc = now.AddHours(1), Notify = false });
            await db.SaveChangesAsync();
            var policy = scope.ServiceProvider.GetRequiredService<GameTimePolicy>();
            Assert.True((await policy.GetStatusAsync(game.Id, now.AddMinutes(45)))!.IsBlocked);
            Assert.False((await policy.GetStatusAsync(game.Id, now.AddHours(1)))!.IsBlocked);
            await db.GameBlackouts.Where(b => b.GameId == game.Id).ExecuteUpdateAsync(update => update.SetProperty(b => b.Enabled, false));
        }
        (await player.GetAsync($"/api/game/{game.Id}/Challenges/{challenge.Id}")).EnsureSuccessStatusCode();
        Assert.False((await player.GetFromJsonAsync<GamePlayStatusModel>($"/api/game/{game.Id}/play-status", JsonOptions))!.IsBlocked);
        (await admin.DeleteAsync(Url(game.Id) + $"/blackouts/{saved.Id}")).EnsureSuccessStatusCode();
    }
}

[CollectionDefinition(nameof(GameTimeTestCollection), DisableParallelization = true)]
public class GameTimeTestCollection : ICollectionFixture<GameTimeTestFactory>;

public class GameTimeTestFactory : GamePushTestFactory
{
    public FakeTimeContainers Containers { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(GameTimeWorker)).ToArray()) services.Remove(descriptor);
            services.RemoveAll<IContainerManager>();
            services.AddSingleton<IContainerManager>(Containers);
        });
    }
}

public class FakeTimeContainers : IContainerManager
{
    public ConcurrentDictionary<Guid, int> Attempts { get; } = new();
    public ConcurrentDictionary<Guid, bool> FailOnce { get; } = new();
    public Task<Container?> CreateContainerAsync(ContainerConfig config, CancellationToken token = default) => throw new NotSupportedException();
    public Task DestroyContainerAsync(Container container, CancellationToken token = default)
    {
        Attempts.AddOrUpdate(container.Id, 1, (_, count) => count + 1);
        if (!FailOnce.TryRemove(container.Id, out _)) container.Status = ContainerStatus.Destroyed;
        return Task.CompletedTask;
    }
}
