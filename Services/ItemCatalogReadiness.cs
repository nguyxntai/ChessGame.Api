using ChessGame.Api.DTOs.Presentation;
using ChessGame.Api.Models;
using MongoDB.Bson;

namespace ChessGame.Api.Services;

public sealed record ItemReadinessEntry(string? ItemId, string Status, ItemPresentationAuthorityResponse? PresentationAuthority);
public sealed record CatalogReadinessResult(string Status, string RuleSet, IReadOnlyList<ItemReadinessEntry> Entries);

// Internal-use pure diagnostic, not an HTTP endpoint or a roll gate. No state writes.
// VALID means existing pool/config rules only, not wallet/account/availability or Unity loadability.
public static class ItemCatalogReadiness
{
    public const string Presentable = "ECONOMICALLY_VALID_AND_PRESENTABLE";
    public const string Unsupported = "ECONOMICALLY_VALID_PRESENTATION_UNSUPPORTED";
    public const string Invalid = "ECONOMICALLY_INVALID";
    public const string Unknown = "UNKNOWN_EVIDENCE_MISSING";
    public const string RuleSet = "GACHA_POOL_RULES_4C33BF9";

    public static CatalogReadinessResult Evaluate(GachaBanner? banner, IReadOnlyCollection<Item>? catalog,
        bool catalogComplete, ItemPresentationPolicy? policy = null)
    {
        if (banner is null || catalog is null || !catalogComplete)
            return new(Unknown, RuleSet, Array.Empty<ItemReadinessEntry>());
        policy ??= ItemPresentationPolicy.Current;
        var valid = ValidPool(banner, catalog);
        var entries = (banner.Pool ?? new()).Select(entry =>
        {
            var matches = catalog.Where(item => item is not null && entry is not null && item.Id == entry.ItemId).ToArray();
            var authority = matches.Length == 1 ? policy.ResolveLive(matches[0].Id.ToString(), matches[0].Type, matches[0].UnityAssetKey) : null;
            return new ItemReadinessEntry(entry?.ItemId.ToString(), !valid ? Invalid
                : authority?.ResolutionStatus == ItemPresentationStatus.Approved ? Presentable : Unsupported, authority);
        }).ToArray();
        return new(!valid ? Invalid : entries.All(e => e.Status == Presentable) ? Presentable : Unsupported,
            RuleSet, Array.AsReadOnly(entries));
    }

    private static bool ValidPool(GachaBanner b, IReadOnlyCollection<Item> catalog)
    {
        if (string.IsNullOrWhiteSpace(b.PityGroup) || b.Pity is null || b.Pity.Epic <= 0 || b.Pity.Legendary <= 0
            || b.Pity.Epic > b.Pity.Legendary || b.Costs is null || b.Costs.Count == 0
            || b.Costs.Any(c => c is null || c.RollCount is not (1 or 10) || c.Amount <= 0 || !Currency(c.Currency))
            || b.Costs.Select(c => c.RollCount).Distinct().Count() != b.Costs.Count
            || b.Pool is null || b.Pool.Count == 0
            || b.Pool.Any(e => e is null || e.ItemId == ObjectId.Empty || e.Weight <= 0)
            || b.Pool.Select(e => e.ItemId).Distinct().Count() != b.Pool.Count
            || b.DuplicateRewards is null) return false;
        var ids = b.Pool.Select(e => e.ItemId).ToHashSet();
        var items = catalog.Where(i => i is not null && ids.Contains(i.Id)).ToArray();
        if (items.Length != ids.Count || items.Select(i => i.Id).Distinct().Count() != ids.Count
            || items.Any(i => !i.IsActive || i.IsDefault || i.Rarity is null
                || i.Rarity.ToUpperInvariant() is not ("COMMON" or "RARE" or "EPIC" or "LEGENDARY"))) return false;
        try
        {
            var rates = GachaDrawEngine.CalculateRates(b.Pool.Select(e => (items.Single(i => i.Id == e.ItemId).Rarity, e.Weight)));
            if (rates.Epic == 0 || rates.Legendary == 0 || rates.Total <= 0) return false;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { return false; }
        return new[] { "COMMON", "RARE", "EPIC", "LEGENDARY" }.All(r => b.DuplicateRewards.TryGetValue(r, out var reward)
            && reward is not null && reward.Amount >= 0 && Currency(reward.Currency));
    }
    private static bool Currency(string? value) => value?.Trim().ToUpperInvariant() is "GOLDS" or "DIAMONDS" or "TICKETS";
}
