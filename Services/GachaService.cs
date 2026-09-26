using System.Security.Cryptography;
using ChessGame.Api.DTOs.Gacha;
using ChessGame.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Services;

public sealed class GachaService
{
    private readonly IMongoClient _client;
    private readonly IMongoCollection<GachaBanner> _banners;
    private readonly IMongoCollection<GachaPity> _pity;
    private readonly IMongoCollection<GachaRoll> _rolls;
    private readonly IMongoCollection<CurrencyTransaction> _transactions;
    private readonly IMongoCollection<Item> _items;
    private readonly IMongoCollection<User> _users;
    private readonly InventoryService _inventory;

    public GachaService(IMongoDatabase database, IMongoClient client, InventoryService inventory)
    {
        _client = client;
        _inventory = inventory;
        _banners = database.GetCollection<GachaBanner>("gacha_banners");
        _pity = database.GetCollection<GachaPity>("player_gacha_pity");
        _rolls = database.GetCollection<GachaRoll>("gacha_rolls");
        _transactions = database.GetCollection<CurrencyTransaction>("currency_transactions");
        _items = database.GetCollection<Item>("items");
        _users = database.GetCollection<User>("users");
    }

    public async Task<List<GachaBannerSummaryResponse>> GetActiveBannersAsync()
    {
        var banners = await _banners.Find(ActiveBannerFilter(DateTime.UtcNow))
            .SortBy(x => x.Code)
            .ToListAsync();
        var responses = new List<GachaBannerSummaryResponse>(banners.Count);

        foreach (var banner in banners)
        {
            var pool = await LoadAndValidatePoolAsync(banner, null);
            responses.Add(ToSummary(banner, pool.Rates));
        }

        return responses;
    }

    public async Task<GachaBannerDetailResponse> GetBannerAsync(string bannerCode)
    {
        var code = NormalizeCode(bannerCode);
        var banner = await _banners.Find(
                Builders<GachaBanner>.Filter.Eq(x => x.Code, code)
                & ActiveBannerFilter(DateTime.UtcNow))
            .FirstOrDefaultAsync();

        if (banner is null)
            throw new GachaException("BANNER_NOT_FOUND");

        var pool = await LoadAndValidatePoolAsync(banner, null);
        var summary = ToSummary(banner, pool.Rates);
        return new GachaBannerDetailResponse
        {
            Code = summary.Code,
            Name = summary.Name,
            Description = summary.Description,
            PityGroup = summary.PityGroup,
            StartsAt = summary.StartsAt,
            EndsAt = summary.EndsAt,
            Costs = summary.Costs,
            RarityRates = summary.RarityRates,
            PityLimit = summary.PityLimit,
            PoolItems = banner.Pool.Select(entry =>
            {
                var item = pool.Items[entry.ItemId];
                return new GachaPoolItemResponse
                {
                    ItemId = item.Id.ToString(),
                    Code = item.Code,
                    Name = item.Name,
                    Type = item.Type,
                    Rarity = item.Rarity.ToUpperInvariant(),
                    UnityAssetKey = item.UnityAssetKey,
                    Weight = entry.Weight
                };
            }).ToList(),
            DuplicateRewards = banner.DuplicateRewards.ToDictionary(
                x => x.Key,
                x => new GachaRewardCurrencyResponse
                {
                    Currency = NormalizeCurrency(x.Value.Currency),
                    Amount = x.Value.Amount
                })
        };
    }

    public async Task<GachaPityResponse> GetPityAsync(ObjectId userId, string bannerCode)
    {
        var code = NormalizeCode(bannerCode);
        await EnsureActiveUserAsync(userId);
        var banner = await _banners.Find(x => x.Code == code).FirstOrDefaultAsync();
        if (banner is null)
            throw new GachaException("BANNER_NOT_FOUND");

        var pityGroup = NormalizePityGroup(banner.PityGroup);
        var state = await _pity
            .Find(x => x.UserId == userId && x.PityGroup == pityGroup)
            .FirstOrDefaultAsync();

        return ToPityResponse(
            state?.EpicCurrent ?? 0,
            state?.LegendaryCurrent ?? 0,
            banner.Pity);
    }

    public async Task<GachaHistoryResponse> GetHistoryAsync(
        ObjectId userId, int page, int pageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100
            || (long)(page - 1) * pageSize > int.MaxValue)
            throw new GachaException("INVALID_PAGE");

        await EnsureActiveUserAsync(userId);
        var filter = Builders<GachaRoll>.Filter.Eq(x => x.UserId, userId);
        var total = await _rolls.CountDocumentsAsync(filter);
        var rows = await _rolls.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return new GachaHistoryResponse
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = rows.Select(ToRollResponse).ToList()
        };
    }

    public async Task<GachaRollResponse> RollAsync(ObjectId userId, GachaRollRequest request)
    {
        if (request.Count is not (1 or 10) || request.RequestId == Guid.Empty)
            throw new GachaException("INVALID_ROLL_REQUEST");

        var code = NormalizeCode(request.BannerCode);
        var requestId = request.RequestId.ToString("D");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var existing = await FindRollAsync(userId, requestId);
            if (existing is not null)
                return ReplayOrConflict(existing, code, request.Count);

            try
            {
                return await RollOnceAsync(userId, code, request.Count, requestId);
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                existing = await FindRollAsync(userId, requestId);
                if (existing is not null)
                    return ReplayOrConflict(existing, code, request.Count);
                if (attempt == 4)
                    throw new GachaException("ROLL_BUSY");
                await Task.Delay(10 * (attempt + 1));
            }
        }

        throw new GachaException("ROLL_BUSY");
    }

    private async Task<GachaRollResponse> RollOnceAsync(
        ObjectId userId, string code, int count, string requestId)
    {
        using var session = await _client.StartSessionAsync();
        session.StartTransaction();

        try
        {
            var previous = await _rolls
                .Find(session, x => x.UserId == userId && x.RequestId == requestId)
                .FirstOrDefaultAsync();
            if (previous is not null)
            {
                await session.AbortTransactionAsync();
                return ReplayOrConflict(previous, code, count);
            }

            var now = DateTime.UtcNow;
            var banner = await _banners.Find(session,
                    Builders<GachaBanner>.Filter.Eq(x => x.Code, code)
                    & ActiveBannerFilter(now))
                .FirstOrDefaultAsync();
            if (banner is null)
                throw new GachaException("BANNER_NOT_FOUND");

            var pool = await LoadAndValidatePoolAsync(banner, session);
            var cost = banner.Costs.SingleOrDefault(x => x.RollCount == count);
            if (cost is null)
                throw new GachaException("INVALID_ROLL_REQUEST");
            var currency = NormalizeCurrency(cost.Currency);
            var totalCost = cost.Amount;
            var walletField = WalletField(currency);
            var pityGroup = NormalizePityGroup(banner.PityGroup);

            var user = await _users.Find(session, x => x.Id == userId)
                .FirstOrDefaultAsync();
            if (user is null)
                throw new GachaException("USER_NOT_FOUND");
            if (!user.IsActive)
                throw new GachaException("ACCOUNT_DISABLED");
            if (WalletBalance(user, currency) < totalCost)
                throw new GachaException("INSUFFICIENT_CURRENCY");

            var walletAfter = await _users.FindOneAndUpdateAsync(
                session,
                Builders<User>.Filter.Eq(x => x.Id, userId)
                    & Builders<User>.Filter.Eq(x => x.IsActive, true)
                    & Builders<User>.Filter.Gte(walletField, totalCost),
                Builders<User>.Update.Inc(walletField, -totalCost)
                    .Set(x => x.UpdatedAt, now),
                new FindOneAndUpdateOptions<User>
                {
                    ReturnDocument = ReturnDocument.After
                });
            if (walletAfter is null)
                throw new InvalidOperationException("WALLET_CONFLICT");

            var state = await _pity
                .Find(session, x => x.UserId == userId && x.PityGroup == pityGroup)
                .FirstOrDefaultAsync();
            var epic = state?.EpicCurrent ?? 0;
            var legendary = state?.LegendaryCurrent ?? 0;
            if (epic < 0 || epic >= banner.Pity.Epic
                || legendary < 0 || legendary >= banner.Pity.Legendary)
                throw new GachaException("PITY_STATE_INVALID");

            var results = new List<GachaRollReward>(count);
            var ledger = new List<CurrencyTransaction>
            {
                new()
                {
                    UserId = userId,
                    Currency = currency,
                    Amount = -totalCost,
                    BalanceAfter = WalletBalance(walletAfter, currency),
                    Source = "GACHA",
                    RequestId = requestId,
                    EntryIndex = 0,
                    CreatedAt = now
                }
            };
            var credits = new Dictionary<string, long>();

            for (var i = 0; i < count; i++)
            {
                var rarity = GachaDrawEngine.ChooseRarity(
                    pool.Rates, banner.Pity, epic, legendary,
                    RandomNumberGenerator.GetInt32(pool.Rates.Total));
                var item = ChooseItem(banner, pool.Items, rarity);
                var isDuplicate = await _inventory.GrantGachaItemAsync(
                    session, userId, item.Id, now);
                GachaDuplicateReward? payout = null;

                if (isDuplicate)
                {
                    var configured = banner.DuplicateRewards[rarity];
                    var payoutCurrency = NormalizeCurrency(configured.Currency);
                    payout = new GachaDuplicateReward
                    {
                        Currency = payoutCurrency,
                        Amount = configured.Amount
                    };

                    IncreaseWalletBalance(walletAfter, payoutCurrency, configured.Amount);
                    credits[payoutCurrency] = checked(
                        credits.GetValueOrDefault(payoutCurrency) + configured.Amount);
                    ledger.Add(new CurrencyTransaction
                    {
                        UserId = userId,
                        Currency = payoutCurrency,
                        Amount = configured.Amount,
                        BalanceAfter = WalletBalance(walletAfter, payoutCurrency),
                        Source = "GACHA",
                        RequestId = requestId,
                        EntryIndex = i + 1,
                        CreatedAt = now
                    });
                }

                results.Add(new GachaRollReward
                {
                    ItemId = item.Id,
                    ItemCode = item.Code,
                    ItemName = item.Name,
                    Rarity = rarity,
                    IsDuplicate = isDuplicate,
                    DuplicateReward = payout
                });

                (epic, legendary) = GachaDrawEngine.Advance(epic, legendary, rarity);
            }

            if (credits.Count > 0)
            {
                var updates = credits.Select(x =>
                    Builders<User>.Update.Inc(WalletField(x.Key), x.Value)).ToList();
                updates.Add(Builders<User>.Update.Set(x => x.UpdatedAt, now));
                var creditResult = await _users.UpdateOneAsync(
                    session,
                    x => x.Id == userId,
                    Builders<User>.Update.Combine(updates));
                if (creditResult.MatchedCount != 1)
                    throw new InvalidOperationException("WALLET_CONFLICT");
            }

            if (state is null)
            {
                await _pity.InsertOneAsync(session, new GachaPity
                {
                    UserId = userId,
                    PityGroup = pityGroup,
                    EpicCurrent = epic,
                    LegendaryCurrent = legendary,
                    Version = 1,
                    UpdatedAt = now
                });
            }
            else
            {
                var updated = await _pity.UpdateOneAsync(
                    session,
                    x => x.Id == state.Id && x.Version == state.Version,
                    Builders<GachaPity>.Update
                        .Set(x => x.EpicCurrent, epic)
                        .Set(x => x.LegendaryCurrent, legendary)
                        .Set(x => x.UpdatedAt, now)
                        .Inc(x => x.Version, 1));
                if (updated.MatchedCount != 1)
                    throw new InvalidOperationException("PITY_CONFLICT");
            }

            var roll = new GachaRoll
            {
                UserId = userId,
                BannerCode = code,
                PityGroup = pityGroup,
                RequestId = requestId,
                Count = count,
                Currency = currency,
                TotalCost = totalCost,
                WalletAfter = new GachaWalletSnapshot
                {
                    Golds = walletAfter.Wallet.Golds,
                    Diamonds = walletAfter.Wallet.Diamonds,
                    Tickets = walletAfter.Wallet.Tickets
                },
                Results = results,
                PityAfter = new GachaPitySnapshot
                {
                    Epic = epic,
                    Legendary = legendary
                },
                PityLimit = new GachaPityLimits
                {
                    Epic = banner.Pity.Epic,
                    Legendary = banner.Pity.Legendary
                },
                CreatedAt = now
            };

            await _rolls.InsertOneAsync(session, roll);
            await _transactions.InsertManyAsync(session, ledger);
            await session.CommitTransactionAsync();
            return ToRollResponse(roll);
        }
        catch
        {
            if (session.IsInTransaction)
            {
                try
                {
                    await session.AbortTransactionAsync();
                }
                catch (MongoException)
                {
                    // Preserve the original error; commit status may be unknown.
                }
            }
            throw;
        }
    }

    private async Task EnsureActiveUserAsync(ObjectId userId)
    {
        var user = await _users.Find(x => x.Id == userId).FirstOrDefaultAsync();
        if (user is null)
            throw new GachaException("USER_NOT_FOUND");
        if (!user.IsActive)
            throw new GachaException("ACCOUNT_DISABLED");
    }

    private async Task<GachaRoll?> FindRollAsync(ObjectId userId, string requestId) =>
        await _rolls.Find(x => x.UserId == userId && x.RequestId == requestId)
            .FirstOrDefaultAsync();

    private static GachaRollResponse ReplayOrConflict(
        GachaRoll previous, string code, int count)
    {
        if (previous.BannerCode != code || previous.Count != count)
            throw new GachaException("REQUEST_ID_CONFLICT");
        return ToRollResponse(previous);
    }

    private async Task<PoolContext> LoadAndValidatePoolAsync(
        GachaBanner banner, IClientSessionHandle? session)
    {
        if (string.IsNullOrWhiteSpace(banner.PityGroup)
            || banner.Pity.Epic <= 0 || banner.Pity.Legendary <= 0
            || banner.Pity.Epic > banner.Pity.Legendary
            || banner.Costs.Count == 0
            || banner.Costs.Any(x => x.RollCount is not (1 or 10) || x.Amount <= 0)
            || banner.Costs.Select(x => x.RollCount).Distinct().Count() != banner.Costs.Count
            || banner.Pool.Count == 0
            || banner.Pool.Any(x => x.ItemId == ObjectId.Empty || x.Weight <= 0)
            || banner.Pool.Select(x => x.ItemId).Distinct().Count() != banner.Pool.Count)
            throw new GachaException("BANNER_MISCONFIGURED");

        foreach (var cost in banner.Costs)
            NormalizeCurrency(cost.Currency);

        var ids = banner.Pool.Select(x => x.ItemId).ToList();
        var filter = Builders<Item>.Filter.In(x => x.Id, ids);
        var items = session is null
            ? await _items.Find(filter).ToListAsync()
            : await _items.Find(session, filter).ToListAsync();
        var byId = items.ToDictionary(x => x.Id);

        if (byId.Count != ids.Count || items.Any(x => !x.IsActive || x.IsDefault
            || !IsKnownRarity(x.Rarity)))
            throw new GachaException("BANNER_MISCONFIGURED");

        GachaRates rates;
        try
        {
            rates = GachaDrawEngine.CalculateRates(
                banner.Pool.Select(x => (byId[x.ItemId].Rarity, x.Weight)));
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            throw new GachaException("BANNER_MISCONFIGURED");
        }
        if (rates.Epic == 0 || rates.Legendary == 0 || rates.Total <= 0)
            throw new GachaException("BANNER_MISCONFIGURED");

        foreach (var rarity in new[] { "COMMON", "RARE", "EPIC", "LEGENDARY" })
        {
            if (!banner.DuplicateRewards.TryGetValue(rarity, out var reward)
                || reward.Amount < 0)
                throw new GachaException("BANNER_MISCONFIGURED");
            NormalizeCurrency(reward.Currency);
        }

        return new PoolContext(byId, rates);
    }

    private static Item ChooseItem(
        GachaBanner banner, Dictionary<ObjectId, Item> pool, string rarity)
    {
        var entries = banner.Pool
            .Where(x => pool[x.ItemId].Rarity.Equals(rarity, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var total = entries.Sum(x => x.Weight);
        var number = RandomNumberGenerator.GetInt32(total);

        foreach (var entry in entries)
        {
            if (number < entry.Weight)
                return pool[entry.ItemId];
            number -= entry.Weight;
        }

        throw new GachaException("BANNER_MISCONFIGURED");
    }

    private static FilterDefinition<GachaBanner> ActiveBannerFilter(DateTime now)
    {
        var f = Builders<GachaBanner>.Filter;
        return f.Eq(x => x.IsActive, true)
            & (f.Eq(x => x.StartsAt, null) | f.Lte(x => x.StartsAt, now))
            & (f.Eq(x => x.EndsAt, null) | f.Gt(x => x.EndsAt, now));
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new GachaException("INVALID_BANNER_CODE");
        return code.Trim().ToUpperInvariant();
    }

    private static string NormalizePityGroup(string group)
    {
        if (string.IsNullOrWhiteSpace(group))
            throw new GachaException("BANNER_MISCONFIGURED");
        return group.Trim().ToUpperInvariant();
    }

    private static string NormalizeCurrency(string currency)
    {
        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized is not ("GOLDS" or "DIAMONDS" or "TICKETS"))
            throw new GachaException("BANNER_MISCONFIGURED");
        return normalized;
    }

    private static string WalletField(string currency) => currency switch
    {
        "GOLDS" => "wallet.golds",
        "DIAMONDS" => "wallet.diamonds",
        "TICKETS" => "wallet.tickets",
        _ => throw new GachaException("BANNER_MISCONFIGURED")
    };

    private static long WalletBalance(User user, string currency) => currency switch
    {
        "GOLDS" => user.Wallet.Golds,
        "DIAMONDS" => user.Wallet.Diamonds,
        "TICKETS" => user.Wallet.Tickets,
        _ => throw new GachaException("BANNER_MISCONFIGURED")
    };

    private static void IncreaseWalletBalance(User user, string currency, long amount)
    {
        try
        {
            switch (currency)
            {
                case "GOLDS": user.Wallet.Golds = checked(user.Wallet.Golds + amount); break;
                case "DIAMONDS": user.Wallet.Diamonds = checked(user.Wallet.Diamonds + amount); break;
                case "TICKETS": user.Wallet.Tickets = checked(user.Wallet.Tickets + amount); break;
                default: throw new GachaException("BANNER_MISCONFIGURED");
            }
        }
        catch (OverflowException)
        {
            throw new GachaException("WALLET_OVERFLOW");
        }
    }

    private static bool IsKnownRarity(string rarity) =>
        rarity.Equals("COMMON", StringComparison.OrdinalIgnoreCase)
        || rarity.Equals("RARE", StringComparison.OrdinalIgnoreCase)
        || rarity.Equals("EPIC", StringComparison.OrdinalIgnoreCase)
        || rarity.Equals("LEGENDARY", StringComparison.OrdinalIgnoreCase);

    private static GachaBannerSummaryResponse ToSummary(GachaBanner banner, GachaRates rates)
    {
        var total = rates.Total;
        return new GachaBannerSummaryResponse
        {
            Code = banner.Code,
            Name = banner.Name,
            Description = banner.Description,
            PityGroup = banner.PityGroup,
            StartsAt = banner.StartsAt,
            EndsAt = banner.EndsAt,
            Costs = banner.Costs.Select(x => new GachaCostResponse
            {
                RollCount = x.RollCount,
                Currency = NormalizeCurrency(x.Currency),
                Amount = x.Amount
            }).ToList(),
            RarityRates = new GachaRatesResponse
            {
                Common = rates.Common * 100m / total,
                Rare = rates.Rare * 100m / total,
                Epic = rates.Epic * 100m / total,
                Legendary = rates.Legendary * 100m / total
            },
            PityLimit = new GachaPityLimitResponse
            {
                Epic = banner.Pity.Epic,
                Legendary = banner.Pity.Legendary
            }
        };
    }

    private static GachaPityResponse ToPityResponse(
        int epic, int legendary, GachaPityLimits limits) => new()
    {
        Epic = new GachaPityTierResponse
        {
            Current = epic,
            Limit = limits.Epic
        },
        Legendary = new GachaPityTierResponse
        {
            Current = legendary,
            Limit = limits.Legendary
        }
    };

    private static GachaRollResponse ToRollResponse(GachaRoll roll) => new()
    {
        RequestId = roll.RequestId,
        BannerCode = roll.BannerCode,
        Count = roll.Count,
        Currency = roll.Currency,
        TotalCost = roll.TotalCost,
        Wallet = new GachaWalletResponse
        {
            Golds = roll.WalletAfter.Golds,
            Diamonds = roll.WalletAfter.Diamonds,
            Tickets = roll.WalletAfter.Tickets
        },
        Results = roll.Results.Select(x => new GachaRewardResponse
        {
            ItemId = x.ItemId.ToString(),
            ItemCode = x.ItemCode,
            ItemName = x.ItemName,
            Rarity = x.Rarity,
            IsDuplicate = x.IsDuplicate,
            DuplicateReward = x.DuplicateReward is null ? null
                : new GachaRewardCurrencyResponse
                {
                    Currency = x.DuplicateReward.Currency,
                    Amount = x.DuplicateReward.Amount
                }
        }).ToList(),
        Pity = ToPityResponse(
            roll.PityAfter.Epic, roll.PityAfter.Legendary, roll.PityLimit),
        CreatedAt = roll.CreatedAt
    };

    private static bool IsRetryable(Exception ex)
    {
        if (ex is InvalidOperationException
            && ex.Message is "WALLET_CONFLICT" or "PITY_CONFLICT")
            return true;
        if (ex is MongoWriteException write
            && write.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            return true;
        if (ex is MongoCommandException command && command.Code == 11000)
            return true;
        return ex is MongoException mongo
            && (mongo.HasErrorLabel("TransientTransactionError")
                || mongo.HasErrorLabel("UnknownTransactionCommitResult"));
    }

    public async Task EnsureIndexesAsync()
    {
        await _banners.Indexes.CreateOneAsync(new CreateIndexModel<GachaBanner>(
            Builders<GachaBanner>.IndexKeys.Ascending(x => x.Code),
            new CreateIndexOptions { Unique = true, Name = "uq_gacha_banners_code" }));

        await _pity.Indexes.CreateOneAsync(new CreateIndexModel<GachaPity>(
            Builders<GachaPity>.IndexKeys.Ascending(x => x.UserId)
                .Ascending(x => x.PityGroup),
            new CreateIndexOptions { Unique = true, Name = "uq_gacha_pity_user_group" }));

        await _rolls.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<GachaRoll>(
                Builders<GachaRoll>.IndexKeys.Ascending(x => x.UserId)
                    .Ascending(x => x.RequestId),
                new CreateIndexOptions { Unique = true, Name = "uq_gacha_rolls_user_request" }),
            new CreateIndexModel<GachaRoll>(
                Builders<GachaRoll>.IndexKeys.Ascending(x => x.UserId)
                    .Descending(x => x.CreatedAt)
                    .Descending(x => x.Id),
                new CreateIndexOptions { Name = "idx_gacha_rolls_user_created" })
        });

        await _transactions.Indexes.CreateOneAsync(
            new CreateIndexModel<CurrencyTransaction>(
                Builders<CurrencyTransaction>.IndexKeys.Ascending(x => x.UserId)
                    .Ascending(x => x.Source)
                    .Ascending(x => x.RequestId)
                    .Ascending(x => x.EntryIndex),
                new CreateIndexOptions<CurrencyTransaction>
                {
                    Unique = true,
                    PartialFilterExpression = Builders<CurrencyTransaction>.Filter
                        .Eq(x => x.Source, "GACHA")
                        & Builders<CurrencyTransaction>.Filter.Exists("requestId", true),
                    Name = "uq_currency_transactions_user_source_request_entry"
                }));
    }

    private sealed record PoolContext(Dictionary<ObjectId, Item> Items, GachaRates Rates);
}

public sealed class GachaException : Exception
{
    public string Code { get; }

    public GachaException(string code) : base(code)
    {
        Code = code;
    }
}
