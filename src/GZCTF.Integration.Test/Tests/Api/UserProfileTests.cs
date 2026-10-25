using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Admin;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public class UserProfileTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task PersonalFields_CanBeSavedAndViewedByOwnerAndAdminWithoutPublicExposure()
    {
        const string password = "Profile@Test123";
        var owner = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password);
        var other = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password);
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password,
            role: Role.Admin);
        var monitor = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password,
            role: Role.Monitor);
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, owner.Id, TestDataSeeder.RandomName());
        using var ownerClient = factory.CreateClient();
        using var otherClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        using var monitorClient = factory.CreateClient();
        using var guestClient = factory.CreateClient();
        foreach (var (client, user) in new[]
                 { (ownerClient, owner), (otherClient, other), (adminClient, admin), (monitorClient, monitor) })
            (await client.PostAsJsonAsync("/api/account/login",
                new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();

        var update = new ProfileUpdateModel
        {
            RealName = "Profile Owner", StdNumber = "20260001", Phone = "13800138000", QQ = "123456789"
        };
        (await ownerClient.PutAsJsonAsync("/api/account/update", update)).EnsureSuccessStatusCode();
        await AssertPrivateFields("123456789");
        (await adminClient.PutAsJsonAsync($"/api/admin/users/{owner.Id}", new AdminUserInfoModel { QQ = "987654321" }))
            .EnsureSuccessStatusCode();
        await AssertPrivateFields("987654321");

        // Public team responses retain only the same public member fields for every viewer.
        foreach (var client in new[] { guestClient, otherClient, monitorClient })
        {
            using var json = JsonDocument.Parse(await client.GetStringAsync($"/api/team/{team.Id}"));
            var member = json.RootElement.GetProperty("members")[0];
            Assert.Equal(owner.UserName, member.GetProperty("userName").GetString());
            foreach (var field in new[] { "realName", "studentNumber", "stdNumber", "phone", "phoneNumber", "qq" })
                Assert.False(member.TryGetProperty(field, out _), $"Public member response exposed {field}");
        }
        foreach (var client in new[] { otherClient, monitorClient })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/admin/users/{owner.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PutAsJsonAsync($"/api/admin/users/{owner.Id}", new { qq = "555555555" })).StatusCode);
        }

        // Partial updates preserve QQ; clearing it is an explicit empty string.
        (await ownerClient.PutAsJsonAsync("/api/account/update", new { bio = "Updated public bio" }))
            .EnsureSuccessStatusCode();
        await AssertPrivateFields("987654321");
        (await ownerClient.PutAsJsonAsync("/api/account/update", new { qq = "" })).EnsureSuccessStatusCode();
        await AssertPrivateFields("");

        async Task AssertPrivateFields(string qq)
        {
            foreach (var (client, url) in new[]
                     { (ownerClient, "/api/account/profile"), (adminClient, $"/api/admin/users/{owner.Id}") })
            {
                var profile = (await client.GetFromJsonAsync<ProfileUserInfoModel>(url))!;
                Assert.Equal(update.RealName, profile.RealName);
                Assert.Equal(update.StdNumber, profile.StdNumber);
                Assert.Equal(update.Phone, profile.Phone);
                Assert.Equal(qq, profile.QQ);
            }
            using var scope = factory.Services.CreateScope();
            var persisted = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking()
                .SingleAsync(user => user.Id == owner.Id);
            Assert.Equal(qq, persisted.QQ);
            Assert.Equal(update.RealName, persisted.RealName);
            Assert.Equal(update.StdNumber, persisted.StdNumber);
            Assert.Equal(update.Phone, persisted.PhoneNumber);
        }
    }

    [Fact]
    public async Task InvalidQQ_IsRejectedWithoutChangingExistingProfile()
    {
        const string password = "Profile@Test123";
        var owner = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password);
        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/account/login",
            new LoginModel { UserName = owner.UserName, Password = password })).EnsureSuccessStatusCode();
        foreach (var qq in new[] { "not-a-number", "1234", "123456789012345678901" })
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PutAsJsonAsync("/api/account/update", new { qq })).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<ProfileUserInfoModel>("/api/account/profile"))!.QQ!);
    }
}
