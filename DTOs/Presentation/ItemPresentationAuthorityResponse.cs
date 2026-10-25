namespace ChessGame.Api.DTOs.Presentation;

// Presentation attestation is separate from economic success and the legacy raw asset key.
public sealed record ItemPresentationAuthorityResponse(
    string? ItemId,
    string MetadataSource,
    string ResolutionStatus,
    string TypeStatus,
    string? Category,
    string? UnityPresentationCategory,
    bool? IsEquippable,
    string? ApprovedUnityAssetKey,
    string? PolicyVersion,
    string? ManifestVersion,
    string ApprovalScope)
{
    public string ContractVersion => "C2_ADDITIVE_V1";
    public string? CandidateUnityLocalCatalogId { get; init; }
    public string? CandidateUnityAddressablesAddress { get; init; }
    public string CategoryAuthorityStatus => ApprovalScope == "INTEGRATION_CANDIDATE" && Category is not null
        ? "INTEGRATION_CANDIDATE" : "UNATTESTED";
    public string TeamAuthorityStatus => "TEAM_AUTHORITY_PENDING";
    public string RuntimeVerificationStatus => "RUNTIME_NOT_TESTED";
    public string ProductionStatus => "PRODUCTION_BLOCKED";
    public string UnityAssetAvailability => "UNVERIFIED";
    public bool IsProductionArtworkVerified => false;
}
