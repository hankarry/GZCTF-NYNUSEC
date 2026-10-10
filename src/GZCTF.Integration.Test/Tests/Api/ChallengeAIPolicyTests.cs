using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Edit;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public class ChallengeAIPolicyTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task PolicyLifecycle_PersistsAndReturnsPolicyToAdminAndParticipant()
    {
        const string password = "Policy@Test123";
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password,
            role: Role.Admin);
        var player = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password);
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, player.Id, TestDataSeeder.RandomName());
        var game = await TestDataSeeder.CreateGameAsync(factory.Services, "AI Policy test");
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id, "Policy challenge",
            "flag{ai_policy_test}");
        await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, team.Id, player.Id);

        using var adminClient = factory.CreateClient();
        using var playerClient = factory.CreateClient();
        (await adminClient.PostAsJsonAsync("/api/account/login",
            new LoginModel { UserName = admin.UserName, Password = password })).EnsureSuccessStatusCode();
        (await playerClient.PostAsJsonAsync("/api/account/login",
            new LoginModel { UserName = player.UserName, Password = password })).EnsureSuccessStatusCode();

        var createdResponse = await adminClient.PostAsJsonAsync($"/api/edit/games/{game.Id}/challenges",
            new ChallengeInfoModel { Title = "New policy challenge" });
        createdResponse.EnsureSuccessStatusCode();
        Assert.False((await createdResponse.Content.ReadFromJsonAsync<ChallengeEditDetailModel>())!.AllowAI);

        var editUrl = $"/api/edit/games/{game.Id}/challenges/{challenge.Id}";
        var playerUrl = $"/api/game/{game.Id}/challenges/{challenge.Id}";
        await AssertPolicy(false);

        // Begin with an existing challenge that explicitly allows AI, then disable it.
        (await adminClient.PutAsJsonAsync(editUrl, new { allowAI = true })).EnsureSuccessStatusCode();
        await AssertPolicy(true);

        var response = await adminClient.PutAsJsonAsync(editUrl, new
        {
            title = "Updated policy challenge", allowAI = false, difficultyLevel = ChallengeDifficulty.Hard
        });
        response.EnsureSuccessStatusCode();
        var updated = (await response.Content.ReadFromJsonAsync<ChallengeEditDetailModel>())!;
        Assert.False(updated.AllowAI);
        Assert.Equal(ChallengeDifficulty.Hard, updated.DifficultyLevel);
        await AssertPolicy(false);

        // Releasing a hint must not reset the challenge's AI policy.
        (await adminClient.PutAsJsonAsync(editUrl, new { hints = new[] { "hint" }, hintEnabled = new[] { true } }))
            .EnsureSuccessStatusCode();
        await AssertPolicy(false);

        (await adminClient.PutAsJsonAsync(editUrl, new { allowAI = true })).EnsureSuccessStatusCode();
        await AssertPolicy(true);

        async Task AssertPolicy(bool expected)
        {
            var edit = await adminClient.GetFromJsonAsync<ChallengeEditDetailModel>(editUrl);
            Assert.Equal(expected, edit!.AllowAI);

            using var json = JsonDocument.Parse(await playerClient.GetStringAsync(playerUrl));
            Assert.Equal(expected, json.RootElement.GetProperty("allowAI").GetBoolean());

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var persisted = await db.GameChallenges.AsNoTracking().SingleAsync(c => c.Id == challenge.Id);
            Assert.Equal(expected, persisted.AllowAI);
            if (persisted.Title == "Updated policy challenge")
                Assert.Equal(ChallengeDifficulty.Hard, persisted.DifficultyLevel);
        }
    }
}
