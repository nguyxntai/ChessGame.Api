using ChessGame.Api.DTOs.Auth;
using ChessGame.Api.Models;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace ChessGame.Api.Services;

public class AuthService
{
    private readonly UserService _userService;
    private readonly InventoryService _inventoryService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly JwtService _jwtService;
    private readonly IMongoClient _mongoClient;
    private readonly RefreshTokenService _refreshTokenService;

    public AuthService(
        UserService userService,
        InventoryService inventoryService,
        IPasswordHasher<User> passwordHasher,
        IMongoClient mongoClient,
        JwtService jwtService,
        RefreshTokenService refreshTokenService)
    {
        _userService = userService;
        _inventoryService = inventoryService;
        _passwordHasher = passwordHasher;
        _mongoClient = mongoClient;
        _jwtService = jwtService;
        _refreshTokenService =
            refreshTokenService;
    }

    public async Task<RegisterResponse> RegisterAsync(
        RegisterRequest request)
    {
        string username =
            request.Username.Trim();

        string email =
            request.Email
                .Trim()
                .ToLowerInvariant();

        string normalizedUsername =
            username.ToUpperInvariant();

        string normalizedEmail =
            email.ToUpperInvariant();

        if (await _userService.UsernameExistsAsync(
                normalizedUsername))
        {
            throw new InvalidOperationException(
                "USERNAME_EXISTS"
            );
        }

        if (await _userService.EmailExistsAsync(
                normalizedEmail))
        {
            throw new InvalidOperationException(
                "EMAIL_EXISTS"
            );
        }

        // Lấy skin mặc định từ collection items
        var defaultSkins =
            await _inventoryService
                .GetDefaultSkinsAsync();

        var now = DateTime.UtcNow;

        var user = new User
        {
            Username = username,

            NormalizedUsername =
                normalizedUsername,

            Email = email,

            NormalizedEmail =
                normalizedEmail,

            IsActive = true,

            Profile = new UserProfile
            {
                DisplayName = username,
                AvatarId = null
            },

            Wallet = new PlayerWallet
            {
                Golds = 1000,
                Diamonds = 0,
                Tickets = 10
            },

            Stats = new PlayerStats
            {
                Elo = 1000,
                Wins = 0,
                Losses = 0,
                Draws = 0,
                GamesPlayed = 0
            },

            Equipped = new EquippedSkins
            {
                ChessSkinId =
                    defaultSkins.ChessSkin.Id,

                BoardSkinId =
                    defaultSkins.BoardSkin.Id
            },

            CreatedAt = now,
            UpdatedAt = now
        };

        user.PasswordHash =
            _passwordHasher.HashPassword(
                user,
                request.Password
            );

        using var session =
            await _mongoClient.StartSessionAsync();

        session.StartTransaction();

        try
        {
            // 1. Tạo user
            await _userService.CreateAsync(
                session,
                user
            );

            // 2. Cấp 2 skin mặc định
            await _inventoryService
                .GrantDefaultSkinsAsync(
                    session,
                    user.Id,
                    defaultSkins.ChessSkin.Id,
                    defaultSkins.BoardSkin.Id
                );

            // Nếu cả 2 đều thành công mới lưu
            await session.CommitTransactionAsync();
        }
        catch (MongoWriteException ex)
            when (
                ex.WriteError?.Category ==
                ServerErrorCategory.DuplicateKey
            )
        {
            await session.AbortTransactionAsync();

            throw new InvalidOperationException(
                "USER_ALREADY_EXISTS"
            );
        }
        catch
        {
            await session.AbortTransactionAsync();
            throw;
        }

        return new RegisterResponse
        {
            UserId = user.Id.ToString(),
            Username = user.Username,
            Email = user.Email,

            Golds = user.Wallet.Golds,
            Diamonds = user.Wallet.Diamonds,
            Tickets = user.Wallet.Tickets,

            Elo = user.Stats.Elo,

            CreatedAt = user.CreatedAt
        };
    }
    public async Task<LoginResponse> LoginAsync(
    LoginRequest request)
    {
        string email =
            request.Email
                .Trim()
                .ToLowerInvariant();

        string normalizedEmail =
            email.ToUpperInvariant();

        var user =
            await _userService
                .GetByNormalizedEmailAsync(
                    normalizedEmail
                );

        if (user is null)
        {
            throw new InvalidOperationException(
                "INVALID_CREDENTIALS"
            );
        }

        if (!user.IsActive)
        {
            throw new InvalidOperationException(
                "ACCOUNT_DISABLED"
            );
        }

        var passwordResult =
            _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password
            );

        if (passwordResult ==
            PasswordVerificationResult.Failed)
        {
            throw new InvalidOperationException(
                "INVALID_CREDENTIALS"
            );
        }

        var token = _jwtService.GenerateAccessToken(user);

        var refreshToken = await _refreshTokenService.CreateAsync(user.Id);

        return new LoginResponse
        {
            UserId = user.Id.ToString(),

            Username = user.Username,

            Email = user.Email,

            AccessToken = token.Token,

            ExpiresAt = token.ExpiresAt,

            RefreshToken = refreshToken.RawToken,

            RefreshTokenExpiresAt = refreshToken.ExpiresAt
        };
    }

    public async Task<RefreshResponse>
        RefreshAsync(
            RefreshRequest request)
    {
        var rotated =
            await _refreshTokenService
                .RotateAsync(
                    request.RefreshToken
                );

        var user =
            await _userService
                .GetByIdAsync(
                    rotated.UserId
                );

        if (user is null)
        {
            throw new InvalidOperationException(
                "INVALID_REFRESH_TOKEN"
            );
        }

        if (!user.IsActive)
        {
            throw new InvalidOperationException(
                "ACCOUNT_DISABLED"
            );
        }

        var accessToken =
            _jwtService
                .GenerateAccessToken(user);

        return new RefreshResponse
        {
            AccessToken =
                accessToken.Token,

            ExpiresAt =
                accessToken.ExpiresAt,

            RefreshToken =
                rotated.RawToken,

            RefreshTokenExpiresAt =
                rotated.ExpiresAt
        };
    }

    public async Task LogoutAsync(
        LogoutRequest request)
    {
        await _refreshTokenService
            .RevokeAsync(
                request.RefreshToken
            );
    }
}