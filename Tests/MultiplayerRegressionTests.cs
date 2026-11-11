using ChessButWeird.Domain;
using ChessGame.Api.Online;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using MongoDB.Driver;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed class MultiplayerRegressionTests
{
    [Theory]
    [InlineData(11000, 409, "LobbyReservationConflict")]
    [InlineData(112, 503, "OnlineTemporarilyUnavailable")]
    public void MongoReservationAndTransientErrorsAreNotInternalServerErrors(int code, int status, string expected)
    {
        var result = new BsonDocument { { "ok", 0 }, { "code", code }, { "errmsg", "fixture" } };
        if (code == 112) result["errorLabels"] = new BsonArray { "TransientTransactionError" };
        var error = new MongoCommandException(new MongoDB.Driver.Core.Connections.ConnectionId(
            new MongoDB.Driver.Core.Servers.ServerId(new MongoDB.Driver.Core.Clusters.ClusterId(), new System.Net.DnsEndPoint("fixture.invalid", 1))),
            "fixture", new BsonDocument("insert", "online_player_seats"), result);
        var context = new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new System.Collections.Generic.List<IFilterMetadata>()) { Exception = error };
        new OnlineExceptionFilter().OnException(context);
        Assert.True(context.ExceptionHandled);
        var response = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(expected, Assert.IsType<OnlineError>(response.Value).Code);
    }

    private static readonly DateTime Now = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
    private static OnlineTicket Ticket(string user, bool rated = false, int rating = 1200, int wait = 0) => new()
    {
        UserId = user, Rated = rated, Rating = rating, CreatedAt = Now.AddSeconds(-wait),
        Settings = new GameSettings(), ExpiresAt = Now.AddMinutes(3)
    };

    [Fact]
    public void MissingRatedFlagDefaultsToNormal()
    {
        var request = System.Text.Json.JsonSerializer.Deserialize<QueueRequest>("{\"requestId\":\"old-client\",\"settings\":{\"mode\":\"Classic\"}}", OnlineJson.Options)!;
        Assert.False(request.Rated);
        var document = new OnlineTicket().ToBsonDocument();
        document.Remove("Rated");
        var legacy = BsonSerializer.Deserialize<OnlineTicket>(document);
        Assert.False(legacy.Rated);
    }

    [Fact]
    public void NormalAndRankedNeverShareAPoolOrPair()
    {
        var settings = new GameSettings();
        Assert.NotEqual(MatchmakingPolicy.PoolKey(settings, false), MatchmakingPolicy.PoolKey(settings, true));
        Assert.Null(MatchmakingPolicy.Cost(Ticket("a"), Ticket("b", true), Now, new(), 0, null, true));
    }

    [Fact]
    public void NormalPairsImmediatelyDespiteRatingAndRecentOpponent()
    {
        Assert.NotNull(MatchmakingPolicy.Cost(Ticket("a", rating: 100), Ticket("b", rating: 3000), Now, new(), 10, Now, true));
    }

    [Fact]
    public void TwoRankedPlayersRelaxAfterFiveSeconds()
    {
        Assert.Null(MatchmakingPolicy.Cost(Ticket("a", true, 100, 4), Ticket("b", true, 3000, 4), Now, new(), 3, Now, true));
        Assert.NotNull(MatchmakingPolicy.Cost(Ticket("a", true, 100, 5), Ticket("b", true, 3000, 5), Now, new(), 3, Now, true));
        Assert.Null(MatchmakingPolicy.Cost(Ticket("a", true, 100, 5), Ticket("b", true, 3000, 5), Now, new(), 3, Now, false));
    }

    [Theory]
    [InlineData("Classic", "VN", 600)]
    [InlineData("Chaos", "VN", 300)]
    public void DifferentSettingsRemainIncompatible(string mode, string region, int seconds)
    {
        var other = Ticket("b");
        other.Settings = new GameSettings { Mode = mode, Region = region, InitialSeconds = seconds, IncrementSeconds = 0 };
        Assert.Null(MatchmakingPolicy.Cost(Ticket("a", wait: 180), other, Now, new(), 0, null, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServerProjectsCheckForClassicAndChaos(bool chaos)
    {
        var m = new OnlineMatch { Status = "InProgress", Turn = "Black" };
        GameRules.Write(m, FenCodec.Parse("4k3/8/8/8/8/8/4R3/K7 b - - 0 1"));
        if (chaos) m.Chaos = new ChaosState { RulesVersion = 3, Phase = "Playing", Sides = new() {
            new() { Team = "White", BuffId = 6 }, new() { Team = "Black", BuffId = 6 } } };
        Assert.Equal("Black", OnlineService.Snapshot(m, Now).CheckedTeam);
        m.Board.Single(p => p.Kind == "Rook").Square = "d2";
        Assert.Null(OnlineService.Snapshot(m, Now).CheckedTeam);
        m.Status = "AwaitingReady";
        Assert.Null(OnlineService.Snapshot(m, Now).CheckedTeam);
    }

    [Fact]
    public void KinglessChaosNeverReportsCheck()
    {
        var m = new OnlineMatch { Status = "InProgress", Turn = "Black", Chaos = new ChaosState {
            RulesVersion = 3, Phase = "Playing", Sides = new() { new() { Team = "White", BuffId = 6 }, new() { Team = "Black", BuffId = 32 } } } };
        GameRules.Write(m, FenCodec.Parse("8/7p/8/8/8/8/4R3/K7 b - - 0 1"));
        Assert.Null(OnlineService.Snapshot(m, Now).CheckedTeam);
    }

    [Fact]
    public void LegacyCheckProjectionDoesNotMutateStoredEffects()
    {
        var m = new OnlineMatch { Status = "InProgress", Turn = "Black", Chaos = new ChaosState {
            RulesVersion = 2, Phase = "Playing", Sides = new() { new() { Team = "White", BuffId = 6 }, new() { Team = "Black", BuffId = 6 } } } };
        GameRules.Write(m, FenCodec.Parse("4k3/8/8/8/8/8/4R3/K7 b - - 0 1"));
        string before = OnlineJson.Write(m);
        Assert.Equal("Black", OnlineService.Snapshot(m, Now).CheckedTeam);
        Assert.Equal(before, OnlineJson.Write(m));
    }
}
