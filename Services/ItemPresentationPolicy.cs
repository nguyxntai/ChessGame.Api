using ChessGame.Api.DTOs.Presentation;
using MongoDB.Bson;

namespace ChessGame.Api.Services;

public static class ItemPresentationStatus
{
    public const string Approved = "APPROVED";
    public const string UnknownItemType = "UNKNOWN_ITEM_TYPE";
    public const string UnsupportedCategory = "UNSUPPORTED_CATEGORY";
    public const string MissingAssetKey = "MISSING_ASSET_KEY";
    public const string UnapprovedAssetKey = "UNAPPROVED_ASSET_KEY";
    public const string MissingOrInvalidMetadata = "MISSING_OR_INVALID_METADATA";
    public const string HistoricalSnapshotUnavailable = "HISTORICAL_SNAPSHOT_UNAVAILABLE";
    public const string IntegrationCandidate = "INTEGRATION_CANDIDATE";
    public const string MissingUnityCatalogEntry = "MISSING_UNITY_CATALOG_ENTRY";
}

public sealed record ItemCategoryRegistration(
    string Category, string? UnityPresentationCategory, bool Supported, bool IsEquippable);
public sealed record ItemTypeRegistration(string ExactRawType, string Category);
public sealed record ItemArtworkRegistration(ObjectId ItemId, string Category, string ExactAssetKey);
// Offline evidence only. Raw backend key, Unity local ID and full address are distinct fields.
// A null local ID/address explicitly records a missing association, never a replacement reward.
public sealed record ItemArtworkCandidate(ObjectId ItemId, string ExactRawType, string Category,
    string ExactRawAssetKey, string? UnityLocalCatalogId, string? UnityAddressablesAddress);
public sealed record ItemTypeClassification(
    string State, string? Category, string? UnityPresentationCategory, bool? IsEquippable);

// Pure, exact, versioned registry. No DB, Unity, network, environment or mutable global policy.
// No real type or ItemId->art binding has been approved in repository evidence. Current is empty.
// Fixture construction deliberately cannot replace Current or claim a production-approved scope.
public sealed class ItemPresentationPolicy
{
    public const string AlgorithmVersion = "C1_EXACT_POLICY_V1";
    private readonly Dictionary<string, ItemCategoryRegistration> categories;
    private readonly Dictionary<string, ItemTypeRegistration> types;
    private readonly Dictionary<ObjectId, ItemArtworkRegistration> artwork;
    private readonly Dictionary<ObjectId, ItemArtworkCandidate> candidates = new();
    public string? ManifestVersion { get; }
    public string ApprovalScope { get; }
    public IReadOnlyList<string> RegisteredRawTypes => Array.AsReadOnly(types.Keys.Order(StringComparer.Ordinal).ToArray());
    public static ItemPresentationPolicy Current { get; } = new(null, "NO_APPROVED_MANIFEST",
        Array.Empty<ItemCategoryRegistration>(), Array.Empty<ItemTypeRegistration>(), Array.Empty<ItemArtworkRegistration>());

    private ItemPresentationPolicy(string? version, string scope,
        IEnumerable<ItemCategoryRegistration> categoryRows, IEnumerable<ItemTypeRegistration> typeRows,
        IEnumerable<ItemArtworkRegistration> artworkRows)
    {
        ManifestVersion = version; ApprovalScope = scope;
        categories = new(StringComparer.Ordinal); types = new(StringComparer.Ordinal); artwork = new();
        foreach (var row in categoryRows)
        {
            if (row is null || !Token(row.Category)
                || (row.UnityPresentationCategory is not (null or "PieceSkin" or "Board" or "Environment"))
                || (row.Supported && row.UnityPresentationCategory is not ("PieceSkin" or "Board"))
                || !categories.TryAdd(row.Category, row))
                throw new ArgumentException("Invalid or duplicate category registration.");
        }
        foreach (var row in typeRows)
        {
            if (row is null || !ValidRawType(row.ExactRawType) || row.ExactRawType != row.ExactRawType.Trim()
                || !categories.ContainsKey(row.Category) || !types.TryAdd(row.ExactRawType, row))
                throw new ArgumentException("Invalid, duplicate or unbound exact type registration.");
        }
        foreach (var row in artworkRows)
        {
            if (row is null || row.ItemId == ObjectId.Empty || !IsAssetKeyWellFormed(row.ExactAssetKey)
                || !categories.TryGetValue(row.Category, out var category) || !category.Supported
                || !types.Values.Any(t => t.Category == row.Category) || !artwork.TryAdd(row.ItemId, row))
                throw new ArgumentException("Invalid, duplicate or unbound ItemId artwork registration.");
        }
    }

    public static ItemPresentationPolicy ForControlledFixture(string manifestVersion,
        IEnumerable<ItemCategoryRegistration> categories, IEnumerable<ItemTypeRegistration> types,
        IEnumerable<ItemArtworkRegistration> artwork)
    {
        if (!Token(manifestVersion)) throw new ArgumentException("Explicit fixture manifest version required.");
        ArgumentNullException.ThrowIfNull(categories); ArgumentNullException.ThrowIfNull(types); ArgumentNullException.ThrowIfNull(artwork);
        return new(manifestVersion, "CONTROLLED_FIXTURE", categories, types, artwork);
    }

    // Explicit opt-in construction for offline integration tests. Never assigned to Current,
    // loaded from application configuration, or used to attest historical V1 receipts.
    public static ItemPresentationPolicy ForOfflineIntegrationCandidate(string manifestVersion,
        IEnumerable<ItemCategoryRegistration> categories, IEnumerable<ItemTypeRegistration> types,
        IEnumerable<ItemArtworkCandidate> associations)
    {
        if (!Token(manifestVersion)) throw new ArgumentException("Explicit candidate manifest version required.");
        ArgumentNullException.ThrowIfNull(categories); ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(associations);
        var policy = new ItemPresentationPolicy(manifestVersion, "INTEGRATION_CANDIDATE", categories, types,
            Array.Empty<ItemArtworkRegistration>());
        foreach (var row in associations)
        {
            if (row is null || row.ItemId == ObjectId.Empty
                || !policy.types.TryGetValue(row.ExactRawType, out var type) || type.Category != row.Category
                || !policy.categories[row.Category].Supported || !IsAssetKeyWellFormed(row.ExactRawAssetKey)
                || (row.UnityLocalCatalogId is null) != (row.UnityAddressablesAddress is null)
                || (row.UnityLocalCatalogId is not null && (!IsAssetKeyWellFormed(row.UnityLocalCatalogId)
                    || !IsAssetKeyWellFormed(row.UnityAddressablesAddress)
                    || row.UnityAddressablesAddress != policy.categories[row.Category].UnityPresentationCategory + "/" + row.UnityLocalCatalogId))
                || !policy.candidates.TryAdd(row.ItemId, row))
                throw new ArgumentException("Invalid, duplicate or unbound candidate association.");
        }
        return policy;
    }

    public ItemTypeClassification Classify(string? rawType)
    {
        if (!ValidRawType(rawType)) return new("MISSING_OR_INVALID", null, null, null);
        if (!types.TryGetValue(rawType!, out var binding)) return new("UNKNOWN", null, null, null);
        var category = categories[binding.Category];
        return new(category.Supported ? "SUPPORTED" : "UNSUPPORTED", category.Category,
            category.UnityPresentationCategory, ApprovalScope == "INTEGRATION_CANDIDATE" ? null : category.IsEquippable);
    }

    public ItemPresentationAuthorityResponse ResolveLive(string? itemId, string? rawType, string? rawAssetKey)
    {
        var type = Classify(rawType);
        ItemPresentationAuthorityResponse Result(string status, string? key = null) => new(itemId,
            ApprovalScope == "INTEGRATION_CANDIDATE" ? "OFFLINE_INTEGRATION_INPUT" : "LIVE_CATALOG",
            status, type.State, type.Category, type.UnityPresentationCategory,
            type.IsEquippable, key, AlgorithmVersion, ManifestVersion, ApprovalScope);
        if (!ObjectId.TryParse(itemId, out var id) || id == ObjectId.Empty || type.State == "MISSING_OR_INVALID")
            return Result(ItemPresentationStatus.MissingOrInvalidMetadata);
        if (type.State == "UNKNOWN") return Result(ItemPresentationStatus.UnknownItemType);
        if (type.State == "UNSUPPORTED") return Result(ItemPresentationStatus.UnsupportedCategory);
        if (string.IsNullOrWhiteSpace(rawAssetKey)) return Result(ItemPresentationStatus.MissingAssetKey);
        if (!IsAssetKeyWellFormed(rawAssetKey)) return Result(ItemPresentationStatus.MissingOrInvalidMetadata);
        if (ApprovalScope == "INTEGRATION_CANDIDATE")
        {
            if (!candidates.TryGetValue(id, out var candidate) || candidate.ExactRawType != rawType
                || candidate.Category != type.Category || candidate.ExactRawAssetKey != rawAssetKey)
                return Result(ItemPresentationStatus.UnapprovedAssetKey);
            return Result(candidate.UnityLocalCatalogId is null ? ItemPresentationStatus.MissingUnityCatalogEntry
                : ItemPresentationStatus.IntegrationCandidate) with
            {
                CandidateUnityLocalCatalogId = candidate.UnityLocalCatalogId,
                CandidateUnityAddressablesAddress = candidate.UnityAddressablesAddress
            };
        }
        if (!artwork.TryGetValue(id, out var binding) || binding.Category != type.Category
            || !string.Equals(binding.ExactAssetKey, rawAssetKey, StringComparison.Ordinal))
            return Result(ItemPresentationStatus.UnapprovedAssetKey);
        return Result(ItemPresentationStatus.Approved, binding.ExactAssetKey);
    }

    // V1 captured raw observations, not a policy/category/artwork approval. Do not consult Current.
    public static ItemPresentationAuthorityResponse ForV1Receipt(string? itemId) => new(itemId,
        "RECEIPT_V1_RAW_METADATA", ObjectId.TryParse(itemId, out var id) && id != ObjectId.Empty
            ? ItemPresentationStatus.HistoricalSnapshotUnavailable : ItemPresentationStatus.MissingOrInvalidMetadata,
        "UNATTESTED", null, null, null, null, null, null, "NO_STORED_AUTHORITY");

    public static bool IsAssetKeyWellFormed(string? key) => key is { Length: > 0 and <= 256 }
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '/' or '.')
        && key.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));
    private static bool ValidRawType(string? type) => !string.IsNullOrWhiteSpace(type)
        && type.Length <= 128 && !type.Any(char.IsControl);
    private static bool Token(string? value) => value is { Length: > 0 and <= 64 }
        && value.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-');
}
