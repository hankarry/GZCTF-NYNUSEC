using GZCTF.Models.Data;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Models.Transfer;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public class ChallengeAIPolicyTests
{
    [Fact]
    public void MissingPolicy_DefaultsToAllowedForNewChallengesAndLegacyImports()
    {
        Assert.True(new GameChallenge().AllowAI);
        var imported = TransferHelper.FromJson<TransferChallenge>("{\"title\":\"Legacy challenge\"}")!.ToChallenge();
        Assert.True(imported.AllowAI);
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
        var challenge = new GameChallenge();
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
}
