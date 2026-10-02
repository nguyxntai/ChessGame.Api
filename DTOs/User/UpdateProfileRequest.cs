using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ChessGame.Api.DTOs.User;

// Reject unknown fields instead of silently accepting changes to server-owned data.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateProfileRequest : IValidatableObject
{
    private string? _displayName;
    private string? _avatarId;

    public string? DisplayName
    {
        get => _displayName;
        set { _displayName = value; HasDisplayName = true; }
    }

    public string? AvatarId
    {
        get => _avatarId;
        set { _avatarId = value; HasAvatarId = true; }
    }

    internal bool HasDisplayName { get; private set; }

    internal bool HasAvatarId { get; private set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!HasDisplayName && !HasAvatarId)
            yield return new ValidationResult("Cần cung cấp displayName hoặc avatarId.");

        if (HasDisplayName && (string.IsNullOrWhiteSpace(DisplayName)
            || DisplayName.Trim().Length > 18 || DisplayName.Any(char.IsControl)))
            yield return new ValidationResult(
                "DisplayName phải từ 1 đến 18 ký tự và không chứa ký tự điều khiển.",
                new[] { nameof(DisplayName) });

        // Unity has Avatar0.png through Avatar5.png. Null restores its default avatar.
        if (HasAvatarId && AvatarId is not null && AvatarId is not
            ("avatar_00" or "avatar_01" or "avatar_02" or "avatar_03" or "avatar_04" or "avatar_05"))
            yield return new ValidationResult(
                "AvatarId phải là avatar_00 đến avatar_05 hoặc null.",
                new[] { nameof(AvatarId) });
    }
}
