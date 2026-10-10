using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Models.Transfer;
using GZCTF.Utils;
using MemoryPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public class ChallengeDifficultyTests
{
    [Theory]
    [InlineData(ChallengeDifficulty.VeryEasy)]
    [InlineData(ChallengeDifficulty.Easy)]
    [InlineData(ChallengeDifficulty.Medium)]
    [InlineData(ChallengeDifficulty.Hard)]
    [InlineData(ChallengeDifficulty.VeryHard)]
    public void UpdatingDifficulty_PreservesScoringAndAppearsInResponses(ChallengeDifficulty difficulty)
    {
        var challenge = new GameChallenge { Difficulty = 7.5 };
        var score = challenge.CurrentScore;
        challenge.Update(new() { DifficultyLevel = difficulty });

        Assert.Equal(difficulty, challenge.DifficultyLevel);
        Assert.Equal(7.5, challenge.Difficulty);
        Assert.Equal(score, challenge.CurrentScore);
        Assert.Equal(difficulty, ChallengeInfoModel.FromChallenge(challenge).DifficultyLevel);
        Assert.Equal(difficulty, ChallengeEditDetailModel.FromChallenge(challenge).DifficultyLevel);
        Assert.Equal(difficulty,
            ChallengeDetailModel.FromInstance(new GameInstance { Challenge = challenge }, 0).DifficultyLevel);
        Assert.Equal(difficulty, challenge.ToTransfer().ToChallenge().DifficultyLevel);
    }

    [Fact]
    public void DefaultsAndLegacyImports_UseMedium()
    {
        Assert.Equal(ChallengeDifficulty.Medium, new GameChallenge().DifficultyLevel);
        Assert.Equal(ChallengeDifficulty.Medium, new ChallengeInfoModel().DifficultyLevel);
        Assert.Equal(ChallengeDifficulty.Medium, new TransferChallenge().ToChallenge().DifficultyLevel);
        var challenge = new GameChallenge { DifficultyLevel = ChallengeDifficulty.VeryHard };
        challenge.Update(new() { Title = "Updated title" });
        Assert.Equal(ChallengeDifficulty.VeryHard, challenge.DifficultyLevel);
    }

    [Fact]
    public void JsonAndScoreboardCache_PreserveDifficulty()
    {
        var detail = new ChallengeDetailModel { DifficultyLevel = ChallengeDifficulty.VeryEasy };
        var json = JsonSerializer.Serialize(detail);
        Assert.Equal("VeryEasy", JsonDocument.Parse(json).RootElement.GetProperty("DifficultyLevel").GetString());

        var info = new ChallengeInfo { DifficultyLevel = ChallengeDifficulty.VeryHard };
        var cached = MemoryPackSerializer.Deserialize<ChallengeInfo>(MemoryPackSerializer.Serialize(info));
        Assert.Equal(info.DifficultyLevel, cached!.DifficultyLevel);
    }

    [Fact]
    public void LegacyScoreboardCache_DefaultsToMedium()
    {
        var legacy = new LegacyDifficultyChallengeInfo { Title = "Cached challenge", Score = 500 };
        var cached = MemoryPackSerializer.Deserialize<ChallengeInfo>(MemoryPackSerializer.Serialize(legacy));
        Assert.Equal(legacy.Title, cached!.Title);
        Assert.Equal(legacy.Score, cached.Score);
        Assert.Equal(ChallengeDifficulty.Medium, cached.DifficultyLevel);
    }

    [Fact]
    public void InvalidDifficulty_IsRejectedInCreateUpdateAndTransfer()
    {
        const ChallengeDifficulty invalid = (ChallengeDifficulty)255;
        object[] models =
        [
            new ChallengeInfoModel { Title = "Challenge", DifficultyLevel = invalid },
            new ChallengeUpdateModel { DifficultyLevel = invalid },
            new TransferChallenge { Title = "Challenge", DifficultyLevel = invalid }
        ];
        foreach (var model in models)
        {
            var errors = new List<ValidationResult>();
            Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
            Assert.Contains(errors, error => error.MemberNames.Contains(nameof(GameChallenge.DifficultyLevel)));
        }
    }

    [Fact]
    public void Migration_MatchesModelAndBackfillsExistingChallengesWithMedium()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql().Options;
        using var context = new AppDbContext(options);
        Assert.False(context.Database.HasPendingModelChanges());
        var script = context.GetService<IMigrator>().GenerateScript(
            "20261001090424_AddChallengeHintRelease", "20261010000000_AddChallengeDifficultyLevel");
        Assert.Contains("ADD \"DifficultyLevel\" smallint NOT NULL DEFAULT 2", script);
    }
}

// The scoreboard cache layout before display difficulty was added.
[MemoryPackable]
internal partial class LegacyDifficultyChallengeInfo
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public ChallengeCategory Category { get; set; }
    public int Score { get; set; }
    public int SolvedCount { get; set; }
    public DateTimeOffset? DeadlineUtc { get; set; }
    public List<Blood> Bloods { get; set; } = [];
}
