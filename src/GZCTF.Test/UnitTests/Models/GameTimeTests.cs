using System;
using System.Linq;
using GZCTF.Models.Data;
using GZCTF.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public class GameTimeTests
{
    private static DateTimeOffset Time(string value) => DateTimeOffset.Parse(value + "+08:00");
    private static readonly Game Game = new() { StartTimeUtc = Time("2026-10-30T09:00:00"), EndTimeUtc = Time("2026-11-02T18:00:00") };

    [Theory]
    [InlineData("2026-10-30T21:59:59", false)]
    [InlineData("2026-10-30T22:00:00", true)]
    [InlineData("2026-10-31T01:59:59", true)]
    [InlineData("2026-10-31T02:00:00", false)]
    [InlineData("2026-10-31T12:00:00", false)]
    [InlineData("2026-10-31T22:00:00", true)]
    [InlineData("2026-11-02T22:00:00", false)]
    public void DailyBlackout_HandlesOvernightAndHalfOpenBoundaries(string instant, bool blocked)
    {
        var now = Time(instant);
        var rule = new GameBlackout { Daily = true, StartMinute = 22 * 60, EndMinute = 2 * 60 };
        Assert.Equal(blocked, GameTimePolicy.Windows(rule, Game, now).Any(window => window.Start <= now && now < window.End));
    }

    [Fact]
    public void Rules_AreClippedToGameTimeAndDisabledRulesDoNotBlock()
    {
        var now = Time("2026-10-30T09:00:00");
        var rule = new GameBlackout { Daily = true, StartMinute = 8 * 60, EndMinute = 10 * 60 };
        Assert.Equal(Game.StartTimeUtc, GameTimePolicy.Windows(rule, Game, now).First(window => window.End > now).Start);
        rule.Enabled = false;
        Assert.Empty(GameTimePolicy.Windows(rule, Game, now));
        rule = new() { StartAtUtc = now.AddHours(-3), EndAtUtc = now.AddHours(1) };
        Assert.Equal(Game.StartTimeUtc, Assert.Single(GameTimePolicy.Windows(rule, Game, now)).Start);
    }
}
