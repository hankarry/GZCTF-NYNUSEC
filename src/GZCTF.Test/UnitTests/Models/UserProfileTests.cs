using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Admin;
using GZCTF.Models.Request.Info;
using MemoryPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public class UserProfileTests
{
    [Fact]
    public void OwnerAndAdmin_CanUpdatePersonalFieldsAndClearQQ()
    {
        var user = new UserInfo();
        user.UpdateUserInfo(new ProfileUpdateModel
        {
            RealName = "Test User", StdNumber = "20260001", Phone = "13800138000", QQ = "123456789"
        });
        var profile = ProfileUserInfoModel.FromUserInfo(user);
        Assert.Equal("Test User", profile.RealName);
        Assert.Equal("20260001", profile.StdNumber);
        Assert.Equal("13800138000", profile.Phone);
        Assert.Equal("123456789", profile.QQ);

        user.UpdateUserInfo(new AdminUserInfoModel { QQ = "987654321" });
        user.UpdateUserInfo(new ProfileUpdateModel { Bio = "Updated bio" });
        Assert.Equal("987654321", UserInfoModel.FromUserInfo(user).QQ);
        Assert.Equal("Test User", user.RealName);
        Assert.Equal("20260001", user.StdNumber);
        Assert.Equal("13800138000", user.PhoneNumber);
        user.UpdateUserInfo(new ProfileUpdateModel { QQ = "" });
        Assert.Empty(user.QQ);
    }

    [Fact]
    public void BatchCreationAndUpdates_PreserveQQ()
    {
        var model = new UserCreateModel
        {
            UserName = "profiletest", Password = "Profile@Test123", Email = "profile@example.com", QQ = "123456789"
        };
        var user = model.ToUserInfo();
        Assert.Equal(model.QQ, user.QQ);
        model.QQ = "987654321";
        user.UpdateUserInfo(model);
        Assert.Equal(model.QQ, user.QQ);
        model.QQ = null;
        user.UpdateUserInfo(model);
        Assert.Equal("987654321", user.QQ);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("12345", true)]
    [InlineData("12345678901234567890", true)]
    [InlineData("1234", false)]
    [InlineData("012345", false)]
    [InlineData("qq123456", false)]
    [InlineData("123456789012345678901", false)]
    public void QQValidation_IsConsistentAcrossWriteModels(string? qq, bool valid)
    {
        object[] models =
        [
            new ProfileUpdateModel { QQ = qq },
            new AdminUserInfoModel { QQ = qq },
            new UserCreateModel
            {
                UserName = "profiletest", Password = "Profile@Test123", Email = "profile@example.com", QQ = qq
            }
        ];
        foreach (var model in models)
            Assert.Equal(valid, Validator.TryValidateObject(model, new ValidationContext(model),
                new List<ValidationResult>(), true));
    }

    [Fact]
    public void PublicTeamMembers_KeepExistingFieldsWithoutPersonalDetails()
    {
        var user = new UserInfo
        {
            UserName = "profiletest", Bio = "Public bio", RealName = "Private Name", StdNumber = "20260001",
            PhoneNumber = "13800138000", QQ = "123456789"
        };
        var team = new Team { CaptainId = user.Id, Members = [user] };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(TeamInfoModel.FromTeam(team),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var member = json.RootElement.GetProperty("members")[0];
        Assert.Equal(user.UserName, member.GetProperty("userName").GetString());
        Assert.Equal(user.Bio, member.GetProperty("bio").GetString());
        foreach (var field in new[] { "realName", "studentNumber", "stdNumber", "phone", "phoneNumber", "qq" })
            Assert.False(member.TryGetProperty(field, out _));
    }

    [Fact]
    public void UserCache_PreservesQQ()
    {
        var user = new UserInfo { QQ = "123456789" };
        var cached = MemoryPackSerializer.Deserialize<UserInfo>(MemoryPackSerializer.Serialize(user));
        Assert.Equal(user.QQ, cached!.QQ);
    }

    [Fact]
    public void Migration_MatchesModelAndPreservesExistingUsers()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql().Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var script = context.GetService<IMigrator>().GenerateScript(
            "20261010132540_DefaultChallengeAIPolicyToDisallowed", "20261021171018_AddUserQQ");
        Assert.Contains("ADD \"QQ\" character varying(20) NOT NULL DEFAULT ''", script);
    }
}
