using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Models.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public class ChallengeAIPolicyTests
{
    [Fact]
    public void MissingPolicy_DefaultsToDisallowedForNewChallengesAndLegacyImports()
    {
        Assert.False(new GameChallenge().AllowAI);
        Assert.False(new ChallengeEditDetailModel().AllowAI);
        Assert.False(new ChallengeDetailModel().AllowAI);
        var imported = TransferHelper.FromJson<TransferChallenge>("{\"title\":\"Legacy challenge\"}")!.ToChallenge();
        Assert.False(imported.AllowAI);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Policy_IsIncludedInAdminAndParticipantDetails(bool allowAI)
    {
        var challenge = new GameChallenge { AllowAI = allowAI };

        Assert.Equal(allowAI, ChallengeEditDetailModel.FromChallenge(challenge).AllowAI);
        Assert.Equal(allowAI, ChallengeDetailModel.FromInstance(new GameInstance { Challenge = challenge }, 0).AllowAI);
    }

    [Fact]
    public void PartialUpdates_PreservePolicyAndAllowReenabling()
    {
        var challenge = new GameChallenge { AllowAI = true };
        challenge.Update(new() { AllowAI = false });
        challenge.Update(new() { Title = "Updated title", Hints = ["hint"], HintEnabled = [true] });
        Assert.False(challenge.AllowAI);

        challenge.Update(new() { AllowAI = true });
        Assert.True(challenge.AllowAI);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExportImport_PreservesPolicyThroughJson(bool allowAI)
    {
        var exported = TransferHelper.ToJson(new GameChallenge { AllowAI = allowAI }.ToTransfer());
        var imported = TransferHelper.FromJson<TransferChallenge>(exported)!.ToChallenge();

        Assert.Equal(allowAI, imported.AllowAI);
    }

    [Fact]
    public void Migration_ChangesDefaultWithoutOverwritingExistingPolicies()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql().Options;
        using var context = new AppDbContext(options);
        Assert.False(context.Database.HasPendingModelChanges());
        var script = context.GetService<IMigrator>().GenerateScript(
            "20261010121203_AddChallengeAIPolicy", "20261010132540_DefaultChallengeAIPolicyToDisallowed");
        Assert.Contains("ALTER COLUMN \"AllowAI\" SET DEFAULT FALSE", script);
        Assert.DoesNotContain("UPDATE \"GameChallenges\"", script);
    }
}
