using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using ChessGame.Api.Controllers;
using ChessGame.Api.DTOs.Gacha;
using ChessGame.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed class GachaRecoveryTests
{
    private static JsonSerializerOptions WebJson => GachaSnapshotTests.WebJson;
    private static string Json(object value) => JsonSerializer.Serialize(value, WebJson);

    [Fact] public void TestRuntimeIsNetEight() => Assert.StartsWith(".NET 8.", RuntimeInformation.FrameworkDescription);

    [Theory][InlineData(1)][InlineData(10)]
    public async Task OwnedReceiptIsExactRepeatableAndStrictlyReadOnly(int count)
    {
        await using var f = new GachaRecoveryFixture(count); await f.StartApi();
        var before = f.State();
        var expected = GachaSnapshotTests.Map(BsonSerializer.Deserialize<GachaRoll>(f.Rows[0]));
        var first = await f.Http.GetAsync(GachaRecoveryFixture.PathFor());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl!.NoStore);
        Assert.True(first.Headers.CacheControl.NoCache);
        var json = await first.Content.ReadAsStringAsync();
        Assert.Equal(Json(expected), json);
        Assert.Equal(json, await f.Http.GetStringAsync(GachaRecoveryFixture.PathFor()));
        var actual = JsonSerializer.Deserialize<GachaRollResponse>(json, WebJson)!;
        Assert.Equal(count, actual.Results.Count);
        Assert.Single(actual.Results.Select(r => r.ItemId).Distinct());
        Assert.All(actual.Results, r => Assert.Equal(long.MaxValue, r.DuplicateReward!.Amount));
        Assert.Equal(long.MaxValue, actual.Wallet.Golds);
        Assert.Equal(2, actual.Pity.Epic.Current); Assert.Equal(7, actual.Pity.Legendary.Current);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(GachaRecoveryFixture.RequestId.ToString("D"), actual.RequestId);
        Assert.Equal("TEST_BANNER", actual.BannerCode); Assert.Equal(count, actual.Count);
        Assert.Equal(Enumerable.Range(0, count), actual.PresentationSnapshot!.Rewards.Select(r => r.SlotIndex));
        Assert.Equal(before, f.State());
        Assert.Equal(new[] { "user.read", "readConcern.majority", "readPreference.primary", "roll.read",
            "user.read", "readConcern.majority", "readPreference.primary", "roll.read" }, f.Calls);
        Assert.All(f.RollFilters, r => Assert.Equal(GachaRecoveryFixture.Owner, r["userId"].AsObjectId));
        f.AssertRecoveryOnly();
    }

    [Fact]
    public async Task MixedRewardIdentitiesRaritiesAndDuplicateSlotsKeepOriginalOrder()
    {
        await using var f = new GachaRecoveryFixture(10);
        var rarities = new[] { "COMMON", "RARE", "EPIC", "LEGENDARY" };
        for (var i=0;i<10;i++)
        {
            var id = ObjectId.Parse($"00000000000000000000000{i%4+1}");
            var row = f.Rows[0]["results"][i].AsBsonDocument;
            row["itemId"]=id; row["itemCode"]=$"code-{i%4}"; row["itemName"]=$"Name {i%4}";
            row["rarity"]=rarities[i%4]; row["isDuplicate"]=i>=4;
            if(i<4)row["duplicateReward"]=BsonNull.Value;
            var metadata = f.Rows[0]["presentationSnapshot"]["rewards"][i].AsBsonDocument;
            metadata["itemId"]=id; metadata["itemCode"]=$"code-{i%4}";
        }
        var before=f.State(); await f.StartApi();
        var r=await f.Http.GetFromJsonAsync<GachaRollResponse>(GachaRecoveryFixture.PathFor());
        Assert.Equal(Enumerable.Range(0,10).Select(i=>$"code-{i%4}"),r!.Results.Select(v=>v.ItemCode));
        Assert.Equal(Enumerable.Range(0,10).Select(i=>rarities[i%4]),r.Results.Select(v=>v.Rarity));
        Assert.Equal(Enumerable.Range(0,10).Select(i=>i>=4),r.Results.Select(v=>v.IsDuplicate));
        Assert.Equal(4,r.Results.Select(v=>v.ItemId).Distinct().Count());
        Assert.Equal(Enumerable.Range(0,10),r.PresentationSnapshot!.Rewards.Select(v=>v.SlotIndex));
        Assert.Equal(r.Results.Select(v=>v.ItemId),r.PresentationSnapshot.Rewards.Select(v=>v.ItemId));
        Assert.Equal(before,f.State()); f.AssertRecoveryOnly();
    }

    [Theory][InlineData("D")][InlineData("N")][InlineData("B")][InlineData("P")][InlineData("X")]
    public async Task GuidSpellingsCanonicalizeToOriginalStoredIdentity(string format)
    {
        await using var f = new GachaRecoveryFixture(); await f.StartApi();
        var path = GachaRecoveryFixture.PathFor(Uri.EscapeDataString(GachaRecoveryFixture.RequestId.ToString(format).ToUpperInvariant()));
        var response = await f.Http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GachaRecoveryFixture.RequestId.ToString("D"), Assert.Single(f.RollFilters)["requestId"].AsString);
        f.AssertRecoveryOnly();
    }

    [Theory][InlineData("not-a-guid")][InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("aabbccdd-1122-3344-5566-778899aabbcc-extra")]
    public async Task InvalidRequestIdentityNeverReadsStorage(string value)
    {
        await using var f = new GachaRecoveryFixture(); await f.StartApi();
        await AssertCode(await f.Http.GetAsync(GachaRecoveryFixture.PathFor(value)), HttpStatusCode.BadRequest, "INVALID_RECOVERY_REQUEST");
        Assert.Empty(f.Calls); f.AssertRecoveryOnly();
    }

    [Fact]
    public async Task MissingRouteSegmentIsRouting404_NotAReceiptOutcome()
    {
        await using var f = new GachaRecoveryFixture(); await f.StartApi();
        var r = await f.Http.GetAsync("/api/gacha/rolls/by-request/");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.DoesNotContain("NOT_OBSERVED", await r.Content.ReadAsStringAsync());
        Assert.Empty(f.Calls);
    }

    [Theory][InlineData(null)][InlineData("not-an-object-id")][InlineData("")]
    public async Task SignedTokenWithInvalidSubjectIsRejected(string? subject)
    {
        await using var f = new GachaRecoveryFixture(); await f.StartApi(); f.SetSubject(subject);
        await AssertCode(await f.Http.GetAsync(GachaRecoveryFixture.PathFor()), HttpStatusCode.Unauthorized, "INVALID_TOKEN_USER");
        Assert.Empty(f.Calls); f.AssertRecoveryOnly();
    }

    [Theory][InlineData("missing")][InlineData("bad-signature")][InlineData("expired")][InlineData("garbage")]
    public async Task BearerAuthenticationIsActuallyEnforced(string kind)
    {
        await using var f = new GachaRecoveryFixture(); await f.StartApi();
        switch (kind)
        {
            case "missing": f.Http.DefaultRequestHeaders.Authorization = null; break;
            case "bad-signature": f.SetSubject(GachaRecoveryFixture.Owner.ToString(), invalidSignature: true); break;
            case "expired": f.SetSubject(GachaRecoveryFixture.Owner.ToString(), expired: true); break;
            case "garbage": f.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "synthetic-invalid-token"); break;
        }
        var response = await f.Http.GetAsync(GachaRecoveryFixture.PathFor());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(f.Calls); f.AssertRecoveryOnly();
    }

    [Fact]
    public async Task CrossAccountAndUnknownGuidHaveIdenticalNonDisclosingOutcome()
    {
        await using var f = new GachaRecoveryFixture(); await f.StartApi(); f.SetSubject(GachaRecoveryFixture.Other.ToString());
        var denied = await f.Http.GetAsync(GachaRecoveryFixture.PathFor());
        var absent = await f.Http.GetAsync(GachaRecoveryFixture.PathFor("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        await AssertCode(denied, HttpStatusCode.NotFound, "NOT_OBSERVED");
        await AssertCode(absent, HttpStatusCode.NotFound, "NOT_OBSERVED");
        Assert.Equal(await denied.Content.ReadAsStringAsync(), await absent.Content.ReadAsStringAsync());
        Assert.All(f.RollFilters, r => Assert.Equal(GachaRecoveryFixture.Other, r["userId"].AsObjectId));
        f.AssertRecoveryOnly();
    }

    [Fact]
    public async Task SameGuidInTwoAccountsAndQueryHeaderOverridesStayIsolated()
    {
        await using var f = new GachaRecoveryFixture();
        var other = GachaRecoveryFixture.Receipt(owner: GachaRecoveryFixture.Other); other.WalletAfter.Golds = 77;
        f.Rows.Add(other.ToBsonDocument()); await f.StartApi();
        f.Http.DefaultRequestHeaders.Add("X-UserId", GachaRecoveryFixture.Other.ToString());
        var a = await f.Http.GetFromJsonAsync<GachaRollResponse>(GachaRecoveryFixture.PathFor()+"?userId="+GachaRecoveryFixture.Other);
        Assert.Equal(long.MaxValue, a!.Wallet.Golds);
        f.SetSubject(GachaRecoveryFixture.Other.ToString());
        var b = await f.Http.GetFromJsonAsync<GachaRollResponse>(GachaRecoveryFixture.PathFor()+"?userId="+GachaRecoveryFixture.Owner);
        Assert.Equal(77, b!.Wallet.Golds);
        Assert.Equal(a.RequestId, b.RequestId);
        f.AssertRecoveryOnly();
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task DisabledOrDeletedAccountMatchesExistingGetPolicy(bool deleted)
    {
        await using var f = new GachaRecoveryFixture(); await f.StartApi();
        if (deleted) f.Users.Remove(GachaRecoveryFixture.Owner); else f.Users[GachaRecoveryFixture.Owner].IsActive = false;
        await AssertCode(await f.Http.GetAsync(GachaRecoveryFixture.PathFor()),
            deleted ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, deleted ? "USER_NOT_FOUND" : "ACCOUNT_DISABLED");
        Assert.Equal(new[] { "user.read" }, f.Calls); f.AssertRecoveryOnly();
    }

    [Theory]
    [InlineData("?bannerCode=test_banner")][InlineData("?count=1")]
    [InlineData("?bannerCode=&count=1")][InlineData("?bannerCode=%20%20&count=1")]
    [InlineData("?bannerCode=test_banner&count=")][InlineData("?bannerCode=&count=")]
    [InlineData("?bannerCode=")][InlineData("?count=")]
    [InlineData("?bannerCode=test_banner&count=0")][InlineData("?bannerCode=test_banner&count=2")]
    [InlineData("?bannerCode=test_banner&count=11")][InlineData("?bannerCode=test_banner&count=-1")]
    [InlineData("?bannerCode=test_banner&bannerCode=other&count=1")]
    [InlineData("?bannerCode=test_banner&count=1&count=10")]
    public async Task InvalidCorrelationNeverReadsReceipt(string query)
    {
        await using var f = new GachaRecoveryFixture(); await f.StartApi();
        await AssertCode(await f.Http.GetAsync(GachaRecoveryFixture.PathFor()+query), HttpStatusCode.BadRequest, "INVALID_RECOVERY_REQUEST");
        Assert.Empty(f.Calls); f.AssertRecoveryOnly();
    }

    [Theory][InlineData("abc")][InlineData("2147483648")]
    public async Task MalformedCountUsesFramework400_NotARecoveryOutcome(string count)
    {
        await using var f = new GachaRecoveryFixture(); await f.StartApi();
        var r = await f.Http.GetAsync(GachaRecoveryFixture.PathFor()+"?bannerCode=test_banner&count="+count);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.DoesNotContain("NOT_OBSERVED", await r.Content.ReadAsStringAsync());
        Assert.Empty(f.Calls);
    }

    [Theory][InlineData("OTHER",1)][InlineData("TEST_BANNER",10)]
    public async Task CorrelationMismatchIsConflictWithoutMutation(string banner, int count)
    {
        await using var f = new GachaRecoveryFixture(); await f.StartApi(); var before = f.State();
        await AssertCode(await f.Http.GetAsync(GachaRecoveryFixture.PathFor()+$"?bannerCode={banner}&count={count}"), HttpStatusCode.Conflict, "REQUEST_ID_CONFLICT");
        Assert.Equal(before, f.State()); f.AssertRecoveryOnly();
    }

    [Theory][InlineData(1)][InlineData(10)]
    public async Task ValidCorrelationUsesExistingTrimUpperPolicy(int count)
    {
        await using var f = new GachaRecoveryFixture(count); await f.StartApi();
        var r = await f.Http.GetAsync(GachaRecoveryFixture.PathFor()+$"?bannerCode=%20test_banner%20&count={count}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); f.AssertRecoveryOnly();
    }

    [Theory][InlineData("historical")][InlineData("invalid")][InlineData("scalar")][InlineData("mismatch")]
    public async Task MissingOrBadOptionalMetadataCannotInvalidateCommittedReward(string kind)
    {
        await using var f = new GachaRecoveryFixture();
        switch (kind)
        {
            case "historical": f.Rows[0].Remove("presentationSnapshot"); break;
            case "invalid": f.Rows[0]["presentationSnapshot"] = new BsonDocument("schemaVersion",999); break;
            case "scalar": f.Rows[0]["presentationSnapshot"] = "bad"; break;
            case "mismatch": f.Rows[0]["presentationSnapshot"]["rewards"][0]["itemId"] = GachaRecoveryFixture.Other; break;
        }
        var before = f.State(); await f.StartApi();
        var r = await f.Http.GetFromJsonAsync<GachaRollResponse>(GachaRecoveryFixture.PathFor());
        Assert.NotNull(r); Assert.Null(r.PresentationSnapshot);
        Assert.Equal(long.MaxValue, r.Results[0].DuplicateReward!.Amount);
        Assert.Equal(before, f.State()); f.AssertRecoveryOnly();
    }

    [Theory][InlineData("timeout")][InlineData("mongo")][InlineData("cancel")][InlineData("unexpected")][InlineData("corrupt-economic")][InlineData("unknown-commit")]
    public async Task ReceiptReadFailuresRemainUnavailableNeverNotObserved(string kind)
    {
        await using var f = new GachaRecoveryFixture();
        f.RollReadError = kind switch
        {
            "timeout" => new TimeoutException("SYNTHETIC_PRIVATE_CONNECTION_DETAIL"),
            "mongo" => new MongoException("SYNTHETIC_PRIVATE_CONNECTION_DETAIL"),
            "cancel" => new OperationCanceledException("SYNTHETIC_PRIVATE_CONNECTION_DETAIL"),
            "corrupt-economic" => new FormatException("SYNTHETIC_PRIVATE_CONNECTION_DETAIL"),
            _ => new InvalidOperationException("SYNTHETIC_PRIVATE_CONNECTION_DETAIL")
        };
        if (kind == "unknown-commit")
        {
            var unknown = new MongoException("SYNTHETIC_PRIVATE_CONNECTION_DETAIL");
            unknown.AddErrorLabel("UnknownTransactionCommitResult"); f.RollReadError = unknown;
        }
        if (kind == "corrupt-economic")
        {
            f.RollReadError = null;
            f.Rows[0]["walletAfter"] = "invalid-economic-BSON";
        }
        var before = f.State(); await f.StartApi();
        var r = await f.Http.GetAsync(GachaRecoveryFixture.PathFor());
        await AssertCode(r, HttpStatusCode.ServiceUnavailable, "RECOVERY_UNAVAILABLE");
        Assert.DoesNotContain("SYNTHETIC_PRIVATE_CONNECTION_DETAIL", await r.Content.ReadAsStringAsync());
        Assert.Equal(before, f.State()); f.AssertRecoveryOnly();
    }

    [Fact]
    public async Task AccountReadTimeoutIsAlsoUnknown_NotAnAbsentAccountOrReceipt()
    {
        await using var f = new GachaRecoveryFixture(); f.UserReadError = new TimeoutException("synthetic"); await f.StartApi();
        await AssertCode(await f.Http.GetAsync(GachaRecoveryFixture.PathFor()), HttpStatusCode.ServiceUnavailable, "RECOVERY_UNAVAILABLE");
        Assert.Equal(new[] { "user.read" }, f.Calls); f.AssertRecoveryOnly();
    }

    [Fact]
    public async Task CancellationPropagatesToReadAndCannotReturnNotObserved()
    {
        await using var f = new GachaRecoveryFixture();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.GetRollByRequestAsync(
            GachaRecoveryFixture.Owner,GachaRecoveryFixture.RequestId,cancellationToken:cts.Token));
        f.AssertRecoveryOnly();
    }

    [Fact]
    public async Task ReceiptQueryReceivesCallerCancellationToken()
    {
        await using var f = new GachaRecoveryFixture(); using var cts = new CancellationTokenSource();
        await f.Service.GetRollByRequestAsync(GachaRecoveryFixture.Owner,GachaRecoveryFixture.RequestId,cancellationToken:cts.Token);
        Assert.Equal(cts.Token,f.LastReadToken); f.AssertRecoveryOnly();
    }

    [Theory][InlineData(1)][InlineData(10)]
    public async Task ReplayHistoryRecoveryAndOriginalMapperAreIdentical(int count)
    {
        await using var f = new GachaRecoveryFixture(count);
        var original = GachaSnapshotTests.Map(BsonSerializer.Deserialize<GachaRoll>(f.Rows[0]));
        var replay = await f.Service.RollAsync(GachaRecoveryFixture.Owner,new GachaRollRequest
        { RequestId = GachaRecoveryFixture.RequestId, BannerCode = " test_banner ", Count = count });
        var history = await f.Service.GetHistoryAsync(GachaRecoveryFixture.Owner,1,20);
        f.Calls.Clear(); f.RollFilters.Clear();
        var recovery = await f.Service.GetRollByRequestAsync(GachaRecoveryFixture.Owner,GachaRecoveryFixture.RequestId);
        Assert.Equal(Json(original),Json(recovery)); Assert.Equal(Json(replay),Json(recovery));
        Assert.Equal(Json(Assert.Single(history.Items)),Json(recovery)); f.AssertRecoveryOnly();
    }

    [Fact]
    public async Task CatalogIsNotConsultedAndConcurrentLookupsRemainReadOnly()
    {
        await using var f = new GachaRecoveryFixture(10); await f.StartApi(); var before = f.State();
        // Item collection is strict, has no read setup and no catalog data at all.
        var results = await Task.WhenAll(Enumerable.Range(0,32).Select(_ => f.Http.GetStringAsync(GachaRecoveryFixture.PathFor())));
        Assert.All(results, value => Assert.Equal(results[0],value));
        Assert.Equal(32,f.Calls.Count(c=>c=="roll.read")); Assert.Equal(before,f.State()); f.AssertRecoveryOnly();
    }

    [Fact]
    public async Task RecoveryOfMockCommittedRollIgnoresSubsequentCatalogMutationAndRemoval()
    {
        var source = new GachaFakeStore();
        var request = source.Request(10);
        var original = await source.Service.RollAsync(source.User.Id,request); // strict fakes only
        var committed = source.Stored!;
        await using var f = new GachaRecoveryFixture(10);
        f.Rows.Clear(); f.Rows.Add(committed.ToBsonDocument());
        f.Users[source.User.Id] = new User { Id = source.User.Id };
        await f.StartApi(); f.SetSubject(source.User.Id.ToString());
        source.Legendary.Type = "catalog-changed"; source.Legendary.UnityAssetKey = "catalog-changed";
        source.Legendary.Code = "catalog-changed"; source.Items.Clear();
        var response = await f.Http.GetFromJsonAsync<GachaRollResponse>(GachaRecoveryFixture.PathFor(request.RequestId.ToString("D")));
        Assert.Equal(Json(GachaSnapshotTests.Map(committed)),Json(response!));
        Assert.Equal(Json(original.PresentationSnapshot!),Json(response!.PresentationSnapshot!));
        Assert.Equal(Json(original.Results),Json(response.Results)); f.AssertRecoveryOnly();
    }

    private static async Task AssertCode(HttpResponseMessage response,HttpStatusCode status,string code)
    {
        Assert.Equal(status,response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code,json.RootElement.GetProperty("code").GetString());
        Assert.Equal(new[] { "code" },json.RootElement.EnumerateObject().Select(p=>p.Name));
    }
}
